using EnterpriseAIPlatform.Application.Support;
using EnterpriseAIPlatform.Infrastructure.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace EnterpriseAIPlatform.IntegrationTests;

/// <summary>
/// Spec 017 integration test host. Extends <see cref="ChatWebApplicationFactory"/>'s fakes (needed
/// for feedback's thread-ownership check) with in-memory <see cref="IVersionAcknowledgmentStore"/>/
/// <see cref="IFeedbackForwarder"/> fakes — no live Cosmos/ECPI dependency in tests. Also points
/// the changelog reader at a seeded temp directory so acknowledgment tests always have a "latest
/// version" to compare against.
/// </summary>
public sealed class SupportWebApplicationFactory : ChatWebApplicationFactory
{
    private readonly string _changelogDirectory = Path.Combine(Path.GetTempPath(), "eap-changelog-" + Guid.NewGuid());

    public FakeVersionAcknowledgmentStore AcknowledgmentStore { get; } = new();

    public FakeFeedbackForwarder FeedbackForwarder { get; } = new();

    /// <summary>When true, replaces the real health checks with one deterministically-failing fake (FR-005 sanitization test).</summary>
    public bool UseFailingHealthCheck { get; set; }

    public SupportWebApplicationFactory()
    {
        Directory.CreateDirectory(_changelogDirectory);
        File.WriteAllText(Path.Combine(_changelogDirectory, "1.0.0.md"), "Initial release.");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
            services.Configure<ChangelogOptions>(o => o.ContentDirectory = _changelogDirectory);

            services.RemoveAll<IVersionAcknowledgmentStore>();
            services.AddSingleton<IVersionAcknowledgmentStore>(AcknowledgmentStore);

            services.RemoveAll<IFeedbackForwarder>();
            services.AddSingleton<IFeedbackForwarder>(FeedbackForwarder);

            if (UseFailingHealthCheck)
            {
                services.Configure<HealthCheckServiceOptions>(options =>
                {
                    options.Registrations.Clear();
                    options.Registrations.Add(new HealthCheckRegistration(
                        "failing-dependency", _ => new FakeFailingHealthCheck(), HealthStatus.Unhealthy, new[] { "live", "ready" }));
                });
            }
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(_changelogDirectory))
        {
            Directory.Delete(_changelogDirectory, recursive: true);
        }
    }
}
