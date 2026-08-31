using System.Net;
using System.Net.Http.Json;

namespace EnterpriseAIPlatform.IntegrationTests.Prompts;

/// <summary>Shared HTTP helpers for spec 016's integration tests.</summary>
internal static class PromptTestClient
{
    internal sealed record ShareTargetPayload(string Type, string? Identity, string? GroupToken);

    internal sealed record WritePayload(
        string Name,
        string Description,
        List<string> CollaboratorEmails,
        List<ShareTargetPayload> SharedWith);

    internal sealed record PromptPayload(
        string Id,
        string OwnerUserId,
        string Name,
        string Description,
        List<string> CollaboratorPartitionKeys,
        List<ShareTargetPayload> SharedWith,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset UpdatedAtUtc);

    internal sealed record ListItemPayload(PromptPayload Prompt, bool IsFavorite);

    internal static HttpClient AsUser(this PromptWebApplicationFactory factory, string user, bool isAdmin = false, string? groups = null)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, user);

        if (isAdmin)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.AdminHeader, "true");
        }

        client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, "Employee");

        if (groups is not null)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.GroupsHeader, groups);
        }

        return client;
    }

    internal static WritePayload Draft(
        string name = "Summarize",
        string description = "Summarize the following text.",
        List<string>? collaborators = null,
        List<ShareTargetPayload>? sharedWith = null) =>
        new(name, description, collaborators ?? new List<string>(), sharedWith ?? new List<ShareTargetPayload>());

    internal static async Task<PromptPayload> CreatePromptAsync(this HttpClient client, WritePayload? payload = null)
    {
        var response = await client.PostAsJsonAsync("/api/prompts", payload ?? Draft());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PromptPayload>())!;
    }
}
