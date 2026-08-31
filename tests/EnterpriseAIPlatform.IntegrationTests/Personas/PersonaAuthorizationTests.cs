using System.Net;
using System.Net.Http.Json;
using EnterpriseAIPlatform.Web.Endpoints.Personas;

namespace EnterpriseAIPlatform.IntegrationTests.Personas;

using PersonaResponse = PersonaEndpoints.PersonaResponse;
using PersonaWriteRequest = PersonaEndpoints.PersonaWriteRequest;
using PersonaShareTargetRequest = PersonaEndpoints.PersonaShareTargetRequest;

/// <summary>
/// Spec 009 US2 — FR-003–FR-009/FR-011/FR-012: the full role x ownership x lesson-persona
/// authorization surface, exercised directly against the API (SC-002, SC-004, SC-005).
/// </summary>
public sealed class PersonaAuthorizationTests : IClassFixture<PersonaWebApplicationFactory>
{
    private readonly PersonaWebApplicationFactory _factory;

    public PersonaAuthorizationTests(PersonaWebApplicationFactory factory) => _factory = factory;

    private HttpClient CreateClient(string user, bool isAdmin = false, string? roles = null)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, user);
        if (isAdmin)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.AdminHeader, "true");
        }

        if (roles is not null)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, roles);
        }

        return client;
    }

    private static PersonaWriteRequest NewPersonaRequest(
        bool isLessonPersona = false,
        List<string>? collaboratorEmails = null,
        List<PersonaShareTargetRequest>? sharedWith = null,
        List<string>? extensions = null,
        List<string>? dataProducts = null) => new(
        Model: "azure-foundry:gpt-5",
        Name: "Test Persona",
        Description: null,
        PersonaMessage: "You are a helpful assistant.",
        Extensions: extensions ?? new List<string>(),
        DataProducts: dataProducts ?? new List<string>(),
        CollaboratorEmails: collaboratorEmails ?? new List<string>(),
        SharedWith: sharedWith ?? new List<PersonaShareTargetRequest>(),
        IsLessonPersona: isLessonPersona);

    private static async Task<PersonaResponse> CreatePersonaAsync(HttpClient client, PersonaWriteRequest request)
    {
        var response = await client.PostAsJsonAsync("/api/personas", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PersonaResponse>())!;
    }

    [Fact]
    public async Task UnrelatedCaller_GetsIdenticalUnauthorizedShape_ForNonExistentAndForbiddenPersona()
    {
        var ownerClient = CreateClient("auth-owner-1@contoso.com", roles: "Employee");
        var unrelatedClient = CreateClient("auth-unrelated-1@contoso.com", roles: "Employee");
        var persona = await CreatePersonaAsync(ownerClient, NewPersonaRequest());

        var forbiddenResponse = await unrelatedClient.GetAsync($"/api/personas/{persona.Id}");
        var notFoundResponse = await unrelatedClient.GetAsync("/api/personas/does-not-exist");

        Assert.Equal(HttpStatusCode.Unauthorized, forbiddenResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, notFoundResponse.StatusCode);
        Assert.Equal(await forbiddenResponse.Content.ReadAsStringAsync(), await notFoundResponse.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task NonAdminListing_ExcludesLessonPersonas_EvenForACollaborator()
    {
        var adminClient = CreateClient("auth-admin-1@contoso.com", isAdmin: true);
        var collaboratorClient = CreateClient("auth-collaborator-1@contoso.com", roles: "Employee");
        var lessonPersona = await CreatePersonaAsync(
            adminClient,
            NewPersonaRequest(isLessonPersona: true, collaboratorEmails: new List<string> { "auth-collaborator-1@contoso.com" }));

        var listResponse = await collaboratorClient.GetAsync("/api/personas");

        listResponse.EnsureSuccessStatusCode();
        var visible = await listResponse.Content.ReadFromJsonAsync<List<PersonaResponse>>();
        Assert.DoesNotContain(visible!, p => p.Id == lessonPersona.Id);
    }

    [Fact]
    public async Task Student_GetsReadOnlyAccess_ToLessonPersona_ButCannotEditIt()
    {
        var adminClient = CreateClient("auth-admin-2@contoso.com", isAdmin: true);
        var studentClient = CreateClient("auth-student-1@contoso.com", roles: "Student");
        var lessonPersona = await CreatePersonaAsync(adminClient, NewPersonaRequest(isLessonPersona: true));

        var getResponse = await studentClient.GetAsync($"/api/personas/{lessonPersona.Id}");
        var editResponse = await studentClient.PatchAsJsonAsync($"/api/personas/{lessonPersona.Id}", NewPersonaRequest());

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, editResponse.StatusCode);
    }

    [Fact]
    public async Task NonAdminUpdate_DiscardsSubmittedIsLessonPersonaChange()
    {
        var ownerClient = CreateClient("auth-owner-2@contoso.com", roles: "Employee");
        var persona = await CreatePersonaAsync(ownerClient, NewPersonaRequest(isLessonPersona: false));

        var updateResponse = await ownerClient.PatchAsJsonAsync($"/api/personas/{persona.Id}", NewPersonaRequest(isLessonPersona: true));
        updateResponse.EnsureSuccessStatusCode();

        var getResponse = await ownerClient.GetAsync($"/api/personas/{persona.Id}");
        var updated = await getResponse.Content.ReadFromJsonAsync<PersonaResponse>();
        Assert.False(updated!.IsLessonPersona);
    }

    [Fact]
    public async Task NonAdminSharingWithGroupToken_IsRejected_AdminSucceeds()
    {
        var ownerClient = CreateClient("auth-owner-3@contoso.com", roles: "Employee");
        var adminClient = CreateClient("auth-admin-3@contoso.com", isAdmin: true);
        var persona = await CreatePersonaAsync(ownerClient, NewPersonaRequest());
        var groupShare = new List<PersonaShareTargetRequest> { new("Group", null, "@employees") };

        var nonAdminResponse = await ownerClient.PatchAsJsonAsync($"/api/personas/{persona.Id}", NewPersonaRequest(sharedWith: groupShare));
        var adminResponse = await adminClient.PatchAsJsonAsync($"/api/personas/{persona.Id}", NewPersonaRequest(sharedWith: groupShare));

        Assert.Equal(HttpStatusCode.BadRequest, nonAdminResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, adminResponse.StatusCode);
    }

    [Fact]
    public async Task RejectedUpdate_LeavesPersonaStateUnchanged()
    {
        // FR-012: no partial writes. Cover both an unauthorized attempt and a failed-validation attempt.
        var ownerClient = CreateClient("auth-owner-4@contoso.com", roles: "Employee");
        var unrelatedClient = CreateClient("auth-unrelated-4@contoso.com", roles: "Employee");
        var original = await CreatePersonaAsync(ownerClient, NewPersonaRequest());

        var unauthorizedAttempt = await unrelatedClient.PatchAsJsonAsync(
            $"/api/personas/{original.Id}", NewPersonaRequest() with { Name = "Hijacked Name" });
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorizedAttempt.StatusCode);

        var invalidAttempt = await ownerClient.PatchAsJsonAsync(
            $"/api/personas/{original.Id}",
            NewPersonaRequest(extensions: new List<string> { "DataProduct" }, dataProducts: new List<string>()) with { Name = "Should Not Stick" });
        Assert.Equal(HttpStatusCode.BadRequest, invalidAttempt.StatusCode);

        var afterResponse = await ownerClient.GetAsync($"/api/personas/{original.Id}");
        var after = await afterResponse.Content.ReadFromJsonAsync<PersonaResponse>();
        Assert.Equal(original.Name, after!.Name);
        Assert.Equal(original.UpdatedAtUtc, after.UpdatedAtUtc);
    }
}
