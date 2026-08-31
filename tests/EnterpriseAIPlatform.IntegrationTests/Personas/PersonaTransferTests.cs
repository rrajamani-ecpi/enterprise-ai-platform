using System.Net;
using System.Net.Http.Json;
using EnterpriseAIPlatform.Application.Identity;
using EnterpriseAIPlatform.Application.Personas;
using EnterpriseAIPlatform.Domain.Identity;
using EnterpriseAIPlatform.Infrastructure.Personas;
using EnterpriseAIPlatform.Web.Endpoints.Personas;
using Microsoft.Extensions.DependencyInjection;

namespace EnterpriseAIPlatform.IntegrationTests.Personas;

using PersonaResponse = PersonaEndpoints.PersonaResponse;
using PersonaWriteRequest = PersonaEndpoints.PersonaWriteRequest;
using TransferOwnershipRequest = PersonaEndpoints.TransferOwnershipRequest;

/// <summary>
/// Spec 009 US1 — FR-001/FR-002/FR-013 / SC-001: ownership transfer is a single atomic row
/// update (research.md D1) — never lost, never duplicated, safely retryable, and a genuine
/// concurrent write conflict is rejected, never silently overwritten or queued.
/// </summary>
public sealed class PersonaTransferTests : IClassFixture<PersonaWebApplicationFactory>
{
    private readonly PersonaWebApplicationFactory _factory;

    public PersonaTransferTests(PersonaWebApplicationFactory factory) => _factory = factory;

    private HttpClient CreateAdminClient(string user)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, user);
        client.DefaultRequestHeaders.Add(TestAuthHandler.AdminHeader, "true");
        return client;
    }

    private HttpClient CreateClient(string user)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, user);
        client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, "Employee");
        return client;
    }

    private static async Task<PersonaResponse> CreatePersonaAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/personas", new PersonaWriteRequest(
            "azure-foundry:gpt-5", "Transfer Test Persona", null, "You are a helpful assistant.",
            new List<string>(), new List<string>(), new List<string>(), new List<PersonaEndpoints.PersonaShareTargetRequest>(), false));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PersonaResponse>())!;
    }

    [Fact]
    public async Task SuccessfulTransfer_PersonaExistsExactlyOnce_UnderNewOwner_WithFieldsIntact()
    {
        var ownerClient = CreateClient("transfer-owner-1@contoso.com");
        var adminClient = CreateAdminClient("transfer-admin-1@contoso.com");
        var persona = await CreatePersonaAsync(ownerClient);

        var transferResponse = await adminClient.PostAsJsonAsync(
            $"/api/personas/{persona.Id}/transfer-ownership", new TransferOwnershipRequest("transfer-new-owner-1@contoso.com"));

        transferResponse.EnsureSuccessStatusCode();
        var transferred = await transferResponse.Content.ReadFromJsonAsync<PersonaResponse>();
        Assert.Equal("transfer-new-owner-1@contoso.com", transferred!.OwnerUserId);
        Assert.Equal(persona.Name, transferred.Name);
        Assert.Equal(persona.Id, transferred.Id);

        // The original owner no longer has access — the persona moved, it wasn't duplicated.
        var originalOwnerAttempt = await ownerClient.GetAsync($"/api/personas/{persona.Id}");
        Assert.Equal(HttpStatusCode.Unauthorized, originalOwnerAttempt.StatusCode);
    }

    [Fact]
    public async Task RetryingTransfer_AfterSuccess_SucceedsWithoutDuplicating()
    {
        var ownerClient = CreateClient("transfer-owner-2@contoso.com");
        var adminClient = CreateAdminClient("transfer-admin-2@contoso.com");
        var persona = await CreatePersonaAsync(ownerClient);
        var request = new TransferOwnershipRequest("transfer-new-owner-2@contoso.com");

        var first = await adminClient.PostAsJsonAsync($"/api/personas/{persona.Id}/transfer-ownership", request);
        var retry = await adminClient.PostAsJsonAsync($"/api/personas/{persona.Id}/transfer-ownership", request);

        first.EnsureSuccessStatusCode();
        retry.EnsureSuccessStatusCode(); // FR-002: retry succeeds without manual recovery.

        var listResponse = await adminClient.GetAsync("/api/personas");
        var all = await listResponse.Content.ReadFromJsonAsync<List<PersonaResponse>>();
        Assert.Single(all!, p => p.Id == persona.Id); // never duplicated under both owners.
    }

    [Fact]
    public async Task ConcurrentWrite_RacingATransfer_IsRejected_NeverSilentlyOverwritten()
    {
        // A genuine race (not just two sequential calls) requires two DbContext instances that
        // both load the same RowVersion before either commits — reproduced directly against
        // PersonaService rather than via two overlapping HTTP calls, since HTTP timing isn't
        // deterministic (research.md D7's self-managed RowVersion is what makes this reproducible).
        var ownerClient = CreateClient("transfer-owner-3@contoso.com");
        var persona = await CreatePersonaAsync(ownerClient);

        using var scopeA = _factory.Services.CreateScope();
        using var scopeB = _factory.Services.CreateScope();
        var dbA = scopeA.ServiceProvider.GetRequiredService<PersonaDbContext>();
        var dbB = scopeB.ServiceProvider.GetRequiredService<PersonaDbContext>();
        var hasher = scopeA.ServiceProvider.GetRequiredService<IIdentityHasher>();
        var serviceA = new PersonaService(dbA, hasher);
        var serviceB = new PersonaService(dbB, hasher);
        var admin = new UserModel { Name = "Admin", Email = "transfer-admin-3@contoso.com", Roles = new RoleFlags(true, false, false, false) };

        // Both scopes load the persona (same RowVersion) before either writes.
        await dbA.Personas.FindAsync(persona.Id);
        await dbB.Personas.FindAsync(persona.Id);

        var winner = await serviceA.TransferOwnershipAsync(persona.Id, "transfer-winner@contoso.com", admin);
        var loser = await serviceB.TransferOwnershipAsync(persona.Id, "transfer-loser@contoso.com", admin);

        Assert.True(winner.IsSuccess);
        Assert.False(loser.IsSuccess); // rejected, not silently applied and not queued.

        var finalState = await ownerClient.GetAsync($"/api/personas/{persona.Id}"); // 401 for original owner either way
        Assert.Equal(HttpStatusCode.Unauthorized, finalState.StatusCode);
    }
}
