using System.Net;
using System.Net.Http.Json;
using EnterpriseAIPlatform.Web.Endpoints.Personas;

namespace EnterpriseAIPlatform.IntegrationTests.Personas;

using PersonaResponse = PersonaEndpoints.PersonaResponse;
using PersonaWriteRequest = PersonaEndpoints.PersonaWriteRequest;

/// <summary>Spec 009 US4 — FR-011 / SC-007: "dataProducts required when DataProduct extension is selected," enforced at the real HTTP entry point.</summary>
public sealed class PersonaValidationTests : IClassFixture<PersonaWebApplicationFactory>
{
    private readonly PersonaWebApplicationFactory _factory;

    public PersonaValidationTests(PersonaWebApplicationFactory factory) => _factory = factory;

    private HttpClient CreateClient(string user)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, user);
        client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, "Employee");
        return client;
    }

    private static PersonaWriteRequest Request(List<string> extensions, List<string> dataProducts) => new(
        "azure-foundry:gpt-5", "Validation Test Persona", null, "You are a helpful assistant.",
        extensions, dataProducts, new List<string>(), new List<PersonaEndpoints.PersonaShareTargetRequest>(), false);

    [Fact]
    public async Task Create_WithDataProductExtension_AndEmptyDataProducts_IsRejected()
    {
        var client = CreateClient("validation-1@contoso.com");

        var response = await client.PostAsJsonAsync("/api/personas", Request(new List<string> { "DataProduct" }, new List<string>()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithDataProductExtension_AndPopulatedDataProducts_Succeeds()
    {
        var client = CreateClient("validation-2@contoso.com");

        var response = await client.PostAsJsonAsync(
            "/api/personas", Request(new List<string> { "DataProduct" }, new List<string> { "product-1" }));

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Create_WithoutDataProductExtension_AndEmptyDataProducts_Succeeds()
    {
        var client = CreateClient("validation-3@contoso.com");

        var response = await client.PostAsJsonAsync("/api/personas", Request(new List<string>(), new List<string>()));

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Create_WithDataProductExtension_AndOnlyEmptyStringEntries_IsRejected()
    {
        // Spec Edge Cases: an empty-string-array entry (not omitted/undefined) still trips validation.
        var client = CreateClient("validation-4@contoso.com");

        var response = await client.PostAsJsonAsync(
            "/api/personas", Request(new List<string> { "DataProduct" }, new List<string> { "", "   " }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_WithDataProductExtension_AndEmptyDataProducts_IsRejected()
    {
        var client = CreateClient("validation-5@contoso.com");
        var createResponse = await client.PostAsJsonAsync("/api/personas", Request(new List<string>(), new List<string>()));
        createResponse.EnsureSuccessStatusCode();
        var persona = (await createResponse.Content.ReadFromJsonAsync<PersonaResponse>())!;

        var updateResponse = await client.PatchAsJsonAsync(
            $"/api/personas/{persona.Id}", Request(new List<string> { "DataProduct" }, new List<string>()));

        Assert.Equal(HttpStatusCode.BadRequest, updateResponse.StatusCode);
    }
}
