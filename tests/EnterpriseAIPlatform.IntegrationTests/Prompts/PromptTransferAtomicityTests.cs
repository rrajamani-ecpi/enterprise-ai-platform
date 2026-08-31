using EnterpriseAIPlatform.Application.Identity;
using EnterpriseAIPlatform.Application.Sharing;
using EnterpriseAIPlatform.Domain.Identity;
using EnterpriseAIPlatform.Domain.Prompts;
using EnterpriseAIPlatform.Domain.Sharing;
using EnterpriseAIPlatform.Infrastructure.Identity;
using EnterpriseAIPlatform.Infrastructure.Prompts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace EnterpriseAIPlatform.IntegrationTests.Prompts;

/// <summary>
/// Spec 016 SC-002 / US2 — a transfer that fails partway must leave the prompt intact under its
/// original owner, report the failure, and be immediately retryable with no manual repair.
/// Drives <see cref="PromptService"/> directly against an InMemory context with an interceptor that
/// fails the first <c>SaveChanges</c>, because the failure has to be injected at the persistence
/// boundary — it cannot be provoked through HTTP.
/// </summary>
public class PromptTransferAtomicityTests
{
    /// <summary>Fails the first save it sees, then behaves normally — models a transient outage.</summary>
    private sealed class FailOnceInterceptor : SaveChangesInterceptor
    {
        private bool _hasFailed;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!_hasFailed)
            {
                _hasFailed = true;
                throw new DbUpdateException("Simulated transient persistence failure.");
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed class AllowAllSharingPolicy : ISharingPolicyService
    {
        public SharingDecision Evaluate(UserModel caller, ShareTargetRequest request) =>
            new(true, SharingDecisionReason.AdminBypass);
    }

    private static UserModel User(string email) => new()
    {
        Name = email,
        Email = email,
        Roles = new RoleFlags(false, true, false, false),
    };

    private static PromptDbContext CreateContext(
        IServiceProvider internalServices, string databaseName, FailOnceInterceptor? interceptor = null)
    {
        // The internal service provider owns the InMemory store, so every context in one test must
        // share the same instance or they silently get separate databases.
        var builder = new DbContextOptionsBuilder<PromptDbContext>()
            .UseInMemoryDatabase(databaseName)
            .UseInternalServiceProvider(internalServices);

        if (interceptor is not null)
        {
            builder.AddInterceptors(interceptor);
        }

        return new PromptDbContext(builder.Options);
    }

    private static IServiceProvider InternalServices() =>
        new ServiceCollection().AddEntityFrameworkInMemoryDatabase().BuildServiceProvider();

    private static PromptService CreateService(PromptDbContext db) =>
        new(db, new IdentityHasher(), new AllowAllSharingPolicy());

    [Fact]
    public async Task FailedTransfer_LeavesPromptIntactUnderOriginalOwner_AndRetrySucceeds()
    {
        var databaseName = $"atomicity-{Guid.NewGuid()}";
        var internalServices = InternalServices();
        var owner = User("atomic-owner@co.com");

        // Arrange: seed a prompt through a clean context.
        string promptId;
        DateTimeOffset originalUpdatedAt;
        await using (var seedDb = CreateContext(internalServices, databaseName))
        {
            var created = await CreateService(seedDb).CreateAsync(
                new PromptModel
                {
                    Id = string.Empty,
                    OwnerUserId = string.Empty,
                    OwnerPartitionKey = string.Empty,
                    Name = "Atomic",
                    Description = "Original text.",
                },
                owner);

            promptId = created.Response!.Id;
            originalUpdatedAt = created.Response.UpdatedAtUtc;
        }

        // Act 1: a transfer whose save fails.
        await using (var failingDb = CreateContext(internalServices, databaseName, new FailOnceInterceptor()))
        {
            var service = CreateService(failingDb);
            await Assert.ThrowsAsync<DbUpdateException>(
                () => service.TransferOwnershipAsync(promptId, "atomic-new@co.com", owner));
        }

        // Assert: the record is untouched — still exactly one row, still the original owner.
        await using (var verifyDb = CreateContext(internalServices, databaseName))
        {
            var rows = await verifyDb.Prompts.AsNoTracking().Where(p => p.Id == promptId).ToListAsync();
            Assert.Single(rows);
            Assert.Equal("atomic-owner@co.com", rows[0].OwnerUserId);
            Assert.Equal("Original text.", rows[0].Description);
            Assert.Equal(originalUpdatedAt, rows[0].UpdatedAtUtc);
        }

        // Act 2: an immediate retry against the intact record, with no repair step in between.
        await using (var retryDb = CreateContext(internalServices, databaseName))
        {
            var retry = await CreateService(retryDb).TransferOwnershipAsync(promptId, "atomic-new@co.com", owner);
            Assert.Equal(EnterpriseAIPlatform.Application.Common.ResponseStatus.OK, retry.Status);
            Assert.Equal("atomic-new@co.com", retry.Response!.OwnerUserId);
        }

        // Assert: exactly one prompt, exactly one owner — never zero copies, never two (FR-008).
        await using (var finalDb = CreateContext(internalServices, databaseName))
        {
            var rows = await finalDb.Prompts.AsNoTracking().Where(p => p.Id == promptId).ToListAsync();
            Assert.Single(rows);
            Assert.Equal("atomic-new@co.com", rows[0].OwnerUserId);
            Assert.Equal("Original text.", rows[0].Description);
        }
    }

    [Fact]
    public async Task RepeatedTransferToTheSameOwner_IsHarmless()
    {
        // A retry after an ambiguous outcome (the client never learned whether the first attempt
        // landed) must be safe: re-reading current state first means it either reapplies the same
        // owner or proceeds normally — never a stale write, never a duplicate.
        var databaseName = $"atomicity-retry-{Guid.NewGuid()}";
        var internalServices = InternalServices();
        var owner = User("retry-owner@co.com");

        await using var db = CreateContext(internalServices, databaseName);
        var service = CreateService(db);

        var created = await service.CreateAsync(
            new PromptModel
            {
                Id = string.Empty,
                OwnerUserId = string.Empty,
                OwnerPartitionKey = string.Empty,
                Name = "Retry",
                Description = "Text.",
            },
            owner);

        var promptId = created.Response!.Id;

        var first = await service.TransferOwnershipAsync(promptId, "retry-new@co.com", owner);
        Assert.Equal(EnterpriseAIPlatform.Application.Common.ResponseStatus.OK, first.Status);

        // The original owner no longer has transfer rights, so the admin repeats it — the same
        // destination, applied twice.
        var admin = new UserModel
        {
            Name = "admin",
            Email = "retry-admin@co.com",
            Roles = new RoleFlags(true, true, false, false),
        };

        var second = await service.TransferOwnershipAsync(promptId, "retry-new@co.com", admin);
        Assert.Equal(EnterpriseAIPlatform.Application.Common.ResponseStatus.OK, second.Status);

        var rows = await db.Prompts.AsNoTracking().Where(p => p.Id == promptId).ToListAsync();
        Assert.Single(rows);
        Assert.Equal("retry-new@co.com", rows[0].OwnerUserId);
    }
}
