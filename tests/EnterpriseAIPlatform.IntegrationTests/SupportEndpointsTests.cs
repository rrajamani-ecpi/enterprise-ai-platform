using System.Net;
using System.Net.Http.Json;
using EnterpriseAIPlatform.Domain.Chat;
using EnterpriseAIPlatform.Infrastructure.Identity;

namespace EnterpriseAIPlatform.IntegrationTests;

/// <summary>Spec 017 US2/US3/US4: sanitized health checks, feedback proxy, version-alert acknowledgment.</summary>
public sealed class SupportEndpointsTests : IClassFixture<SupportWebApplicationFactory>
{
    private readonly SupportWebApplicationFactory _factory;

    public SupportEndpointsTests(SupportWebApplicationFactory factory) => _factory = factory;

    private HttpClient CreateClient(string? user = null)
    {
        var client = _factory.CreateClient();
        if (user is not null)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, user);
        }

        return client;
    }

    [Fact]
    public async Task Health_Unconfigured_ReportsHealthy_WithNameAndStatusBody()
    {
        var response = await CreateClient().GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"status\":\"Healthy\"", body);
        Assert.Contains("cosmos", body);
        Assert.Contains("keyvault", body);
    }

    [Fact]
    public async Task Health_FailingDependency_ReportsUnhealthy_NeverLeaksRawErrorText()
    {
        using var failingFactory = new SupportWebApplicationFactory { UseFailingHealthCheck = true };
        var client = failingFactory.CreateClient();

        var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("failing-dependency", body);
        Assert.Contains("\"status\":\"Unhealthy\"", body);
        Assert.DoesNotContain(FakeFailingHealthCheck.SensitiveMarker, body);
        Assert.DoesNotContain("Password", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Feedback_ThreadNotOwnedByCaller_IsRejected_BeforeAnyForwarding()
    {
        const string owner = "feedback-thread-owner@contoso.com";
        var ownerPartitionKey = new IdentityHasher().ForEmail(owner).Value;
        _factory.ThreadStore.Seed(new ChatThreadModel
        {
            Id = "not-my-thread", PartitionKey = ownerPartitionKey, OwnerUserId = owner,
            Version = "v3", ModelId = "azure-foundry:gpt-5", DisplayName = "Conversation — Jan 1, 2026 12:00 PM",
        });

        var client = CreateClient("someone-else@contoso.com");
        var response = await client.PostAsJsonAsync("/api/feedback", new { threadId = "not-my-thread", content = "great chat" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        // Shared fixture across tests in this class — assert no call for *this* thread specifically,
        // rather than an absolute Assert.Empty (another test may have already recorded its own call).
        Assert.DoesNotContain(_factory.FeedbackForwarder.Calls, c => c.ThreadId == "not-my-thread");
    }

    [Fact]
    public async Task Feedback_OwnedThread_ForwarderFails_CallerStillSeesSuccess()
    {
        const string owner = "feedback-happy@contoso.com";
        var ownerPartitionKey = new IdentityHasher().ForEmail(owner).Value;
        _factory.ThreadStore.Seed(new ChatThreadModel
        {
            Id = "my-thread", PartitionKey = ownerPartitionKey, OwnerUserId = owner,
            Version = "v3", ModelId = "azure-foundry:gpt-5", DisplayName = "Conversation — Jan 1, 2026 12:00 PM",
        });
        _factory.FeedbackForwarder.ShouldSucceed = false;

        var client = CreateClient(owner);
        var response = await client.PostAsJsonAsync("/api/feedback", new { threadId = "my-thread", content = "great chat" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(_factory.FeedbackForwarder.Calls, c => c.ThreadId == "my-thread");
    }

    [Fact]
    public async Task Acknowledgment_NoPriorAcknowledgment_ShowsAlert()
    {
        var client = CreateClient("ack-new@contoso.com");

        var response = await client.GetFromJsonAsync<AcknowledgmentDto>("/api/changelog/acknowledgment");

        Assert.True(response!.ShowAlert);
    }

    [Fact]
    public async Task Acknowledgment_PostThenGet_RoundTrips_AndSuppressesAlert()
    {
        var client = CreateClient("ack-roundtrip@contoso.com");

        var post = await client.PostAsync("/api/changelog/acknowledgment", null);
        Assert.Equal(HttpStatusCode.OK, post.StatusCode);

        var response = await client.GetFromJsonAsync<AcknowledgmentDto>("/api/changelog/acknowledgment");
        Assert.False(response!.ShowAlert);
        Assert.Equal("1.0.0", response.AcknowledgedVersion);
    }

    [Fact]
    public async Task Acknowledgment_PersistenceFailure_ReturnsNonSuccess_NeverFabricatesSuccess()
    {
        _factory.AcknowledgmentStore.ThrowOnSet = true;
        try
        {
            var client = CreateClient("ack-failure@contoso.com");

            var response = await client.PostAsync("/api/changelog/acknowledgment", null);

            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        }
        finally
        {
            _factory.AcknowledgmentStore.ThrowOnSet = false;
        }
    }

    private sealed record AcknowledgmentDto(string? LatestVersion, string? AcknowledgedVersion, DateTimeOffset? AcknowledgedAtUtc, bool ShowAlert);
}
