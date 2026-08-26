using System.Net;
using System.Net.Http.Json;
using EnterpriseAIPlatform.Domain.Chat;
using EnterpriseAIPlatform.Infrastructure.Identity;

namespace EnterpriseAIPlatform.IntegrationTests;

/// <summary>Spec 024 US3 — FR-005 (list), FR-006 (history), FR-007/FR-013 (rename, not-found).</summary>
public sealed class ChatConversationListTests : IClassFixture<ChatWebApplicationFactory>
{
    private const string SeededModelId = "azure-foundry:gpt-5";

    private readonly ChatWebApplicationFactory _factory;

    public ChatConversationListTests(ChatWebApplicationFactory factory) => _factory = factory;

    private HttpClient CreateClient(string user)
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
    public async Task ListThreads_OnlyReturnsCallersOwnConversations()
    {
        var ownerClient = CreateClient("list-owner@contoso.com");
        var otherClient = CreateClient("list-other@contoso.com");
        var ownerThreadId = await CreateThreadAsync(ownerClient);
        var otherThreadId = await CreateThreadAsync(otherClient);

        var response = await ownerClient.GetAsync("/api/chat/threads");

        response.EnsureSuccessStatusCode();
        var threads = await response.Content.ReadFromJsonAsync<List<ConversationSummaryResponse>>();
        Assert.Contains(threads!, t => t.Id == ownerThreadId);
        Assert.DoesNotContain(threads!, t => t.Id == otherThreadId);
    }

    [Fact]
    public async Task ListThreads_NoConversations_ReturnsEmptyList_NotAnError()
    {
        var client = CreateClient("list-empty@contoso.com");

        var response = await client.GetAsync("/api/chat/threads");

        response.EnsureSuccessStatusCode();
        var threads = await response.Content.ReadFromJsonAsync<List<ConversationSummaryResponse>>();
        Assert.Empty(threads!);
    }

    [Fact]
    public async Task RenameThread_ValidName_Persists()
    {
        var client = CreateClient("rename-owner@contoso.com");
        var threadId = await CreateThreadAsync(client);

        var response = await client.PatchAsJsonAsync($"/api/chat/threads/{threadId}", new { displayName = "Trip planning" });

        response.EnsureSuccessStatusCode();
        var updated = await response.Content.ReadFromJsonAsync<ConversationSummaryResponse>();
        Assert.Equal("Trip planning", updated!.DisplayName);

        var listResponse = await client.GetAsync("/api/chat/threads");
        var threads = await listResponse.Content.ReadFromJsonAsync<List<ConversationSummaryResponse>>();
        Assert.Contains(threads!, t => t.Id == threadId && t.DisplayName == "Trip planning");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RenameThread_EmptyOrWhitespace_IsRejected_WithPriorNameIntact(string invalidName)
    {
        var client = CreateClient("rename-empty@contoso.com");
        var threadId = await CreateThreadAsync(client);
        var beforeResponse = await client.GetAsync("/api/chat/threads");
        var before = (await beforeResponse.Content.ReadFromJsonAsync<List<ConversationSummaryResponse>>())!
            .Single(t => t.Id == threadId);

        var response = await client.PatchAsJsonAsync($"/api/chat/threads/{threadId}", new { displayName = invalidName });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("EMPTY_NAME", body);

        var afterResponse = await client.GetAsync("/api/chat/threads");
        var after = (await afterResponse.Content.ReadFromJsonAsync<List<ConversationSummaryResponse>>())!
            .Single(t => t.Id == threadId);
        Assert.Equal(before.DisplayName, after.DisplayName);
    }

    [Fact]
    public async Task RenameThread_NotOwnedByCaller_ReturnsNotFound()
    {
        const string owner = "rename-victim@contoso.com";
        var ownerClient = CreateClient(owner);
        var threadId = await CreateThreadAsync(ownerClient);
        var attackerClient = CreateClient("rename-attacker@contoso.com");

        var response = await attackerClient.PatchAsJsonAsync($"/api/chat/threads/{threadId}", new { displayName = "Hijacked" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetMessages_ReturnsSeededHistory_InOrder()
    {
        const string owner = "history-owner@contoso.com";
        var partitionKey = new IdentityHasher().ForEmail(owner).Value;
        var client = CreateClient(owner);
        var threadId = await CreateThreadAsync(client);

        _factory.MessageStore.Seed(new ChatMessageModel
        {
            Id = "m1", PartitionKey = partitionKey, ThreadId = threadId, Role = ChatMessageRole.User,
            Content = "hello", CreatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
        });
        _factory.MessageStore.Seed(new ChatMessageModel
        {
            Id = "m2", PartitionKey = partitionKey, ThreadId = threadId, Role = ChatMessageRole.Assistant,
            Content = "hi there", CreatedAtUtc = DateTimeOffset.UtcNow,
        });

        var response = await client.GetAsync($"/api/chat/threads/{threadId}/messages");

        response.EnsureSuccessStatusCode();
        var messages = await response.Content.ReadFromJsonAsync<List<MessageResponse>>();
        Assert.Equal(2, messages!.Count);
        Assert.Equal("hello", messages[0].Content);
        Assert.Equal("hi there", messages[1].Content);
    }

    [Fact]
    public async Task GetMessages_ThreadNotOwnedByCaller_ReturnsNotFound()
    {
        const string owner = "history-victim@contoso.com";
        var ownerClient = CreateClient(owner);
        var threadId = await CreateThreadAsync(ownerClient);
        var attackerClient = CreateClient("history-attacker@contoso.com");

        var response = await attackerClient.GetAsync($"/api/chat/threads/{threadId}/messages");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetMessages_NonexistentThread_ReturnsNotFound()
    {
        var client = CreateClient("history-none@contoso.com");

        var response = await client.GetAsync("/api/chat/threads/does-not-exist/messages");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed record ThreadResponse(string Id, string Version, string ModelId);

    private sealed record ConversationSummaryResponse(string Id, string DisplayName, DateTimeOffset LastActivityAtUtc, string ModelId);

    private sealed record MessageResponse(string Role, string Content, DateTimeOffset CreatedAtUtc);
}
