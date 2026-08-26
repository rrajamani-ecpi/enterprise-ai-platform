using System.Net;
using System.Net.Http.Json;

namespace EnterpriseAIPlatform.IntegrationTests;

/// <summary>
/// Spec 004 send → stream → persist vertical: US1 rejection paths (SC-001), FR-024's generic 500,
/// and the streaming + persistence happy path (Phase 7).
/// </summary>
public sealed class ChatSendMessageTests : IClassFixture<ChatWebApplicationFactory>
{
    private const string SeededModelId = "azure-foundry:gpt-5";

    private readonly ChatWebApplicationFactory _factory;

    public ChatSendMessageTests(ChatWebApplicationFactory factory) => _factory = factory;

    private HttpClient CreateClient(string user = "grace@contoso.com")
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, user);
        return client;
    }

    private async Task<string> CreateThreadAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/chat/threads", new { modelId = SeededModelId });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ThreadResponse>();
        return body!.Id;
    }

    [Fact]
    public async Task SendMessage_HappyPath_StreamsChunks_AndPersistsBothMessages()
    {
        var client = CreateClient("happy-path@contoso.com");
        var threadId = await CreateThreadAsync(client);
        _factory.CompletionClient.Chunks = new[] { "Hi", " there" };

        var response = await client.PostAsJsonAsync(
            $"/api/chat/threads/{threadId}/messages", new { text = "hello", modelId = SeededModelId });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("data: Hi", body);
        Assert.Contains("data: there".Replace(" ", string.Empty), body.Replace(" ", string.Empty));
        Assert.Contains("data: [DONE]", body);

        var persisted = _factory.MessageStore.Messages.Where(m => m.ThreadId == threadId).ToList();
        Assert.Equal(2, persisted.Count);
        Assert.Contains(persisted, m => m.Role == Domain.Chat.ChatMessageRole.User && m.Content == "hello");
        Assert.Contains(persisted, m => m.Role == Domain.Chat.ChatMessageRole.Assistant && m.Content == "Hi there");
    }

    [Fact]
    public async Task SendMessage_ToNonexistentThread_IsRejected_AsThreadReadOnly()
    {
        var client = CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/chat/threads/does-not-exist/messages", new { text = "hello", modelId = SeededModelId });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("THREAD_READ_ONLY", body);
    }

    [Fact]
    public async Task SendMessage_ToNonV3Thread_IsRejected()
    {
        const string owner = "legacy-thread@contoso.com";
        var partitionKey = new EnterpriseAIPlatform.Infrastructure.Identity.IdentityHasher().ForEmail(owner).Value;
        var threadId = Guid.NewGuid().ToString("n");
        _factory.ThreadStore.Seed(new Domain.Chat.ChatThreadModel
        {
            Id = threadId,
            PartitionKey = partitionKey,
            OwnerUserId = owner,
            Version = "v2",
            ModelId = SeededModelId,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            DisplayName = "Conversation — Jan 1, 2026 12:00 PM",
        });

        var client = CreateClient(owner);
        var response = await client.PostAsJsonAsync(
            $"/api/chat/threads/{threadId}/messages", new { text = "hello", modelId = SeededModelId });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task SendMessage_UnhandledException_Returns500_WithNoInternalDetail()
    {
        var client = CreateClient("boom@contoso.com");
        var threadId = await CreateThreadAsync(client);
        _factory.ThreadStore.ThrowOnGet = true;

        try
        {
            var response = await client.PostAsJsonAsync(
                $"/api/chat/threads/{threadId}/messages", new { text = "hello", modelId = SeededModelId });

            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("Simulated Cosmos read failure", body);
            Assert.DoesNotContain("StackTrace", body, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            _factory.ThreadStore.ThrowOnGet = false;
        }
    }

    private sealed record ThreadResponse(string Id, string Version, string ModelId);
}
