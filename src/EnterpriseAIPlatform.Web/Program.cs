using EnterpriseAIPlatform.Application.Authorization;
using EnterpriseAIPlatform.Application.Common;
using EnterpriseAIPlatform.Application.Identity;
using EnterpriseAIPlatform.Infrastructure.Authentication;
using EnterpriseAIPlatform.Infrastructure.DependencyInjection;
using EnterpriseAIPlatform.Infrastructure.Telemetry;
using EnterpriseAIPlatform.Web.Components;
using EnterpriseAIPlatform.Web.Endpoints;
using EnterpriseAIPlatform.Web.Endpoints.Chat;
using EnterpriseAIPlatform.Web.Endpoints.ModelAccess;
using EnterpriseAIPlatform.Web.Endpoints.MultiChat;
using EnterpriseAIPlatform.Web.Endpoints.Support;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;

var builder = WebApplication.CreateBuilder(args);

var authenticationSection = builder.Configuration.GetSection(PlatformAuthenticationOptions.SectionName);
var authenticationOptions = authenticationSection.Get<PlatformAuthenticationOptions>() ?? new();

if (authenticationOptions.Mode == PlatformAuthenticationMode.Development && !builder.Environment.IsDevelopment())
{
    throw new InvalidOperationException(
        "Development authentication is enabled outside the Development environment. " +
        "Set PlatformAuthentication:Mode to Entra before deploying.");
}

builder.Services.AddOptions<PlatformAuthenticationOptions>()
    .Bind(authenticationSection)
    .Validate(
        options => options.Mode != PlatformAuthenticationMode.Development ||
                   (!string.IsNullOrWhiteSpace(options.DevelopmentUser.Name) &&
                    !string.IsNullOrWhiteSpace(options.DevelopmentUser.Email)),
        "PlatformAuthentication:DevelopmentUser:Name and Email are required in Development mode.")
    .ValidateOnStart();

// Entra in deployed environments; explicitly simulated identity in local development.
if (authenticationOptions.Mode == PlatformAuthenticationMode.Development)
{
    builder.Services
        .AddAuthentication(DevelopmentAuthenticationHandler.SchemeName)
        .AddScheme<AuthenticationSchemeOptions, DevelopmentAuthenticationHandler>(
            DevelopmentAuthenticationHandler.SchemeName,
            _ => { });
}
else
{
    builder.Services
        .AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
        .AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("AzureAd"));
}

builder.Services.AddControllersWithViews().AddMicrosoftIdentityUI();

// Enums (e.g. ModelAccessTier) serialize/bind as their string names, not raw ints, on JSON API endpoints.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

// --- Canonical session/role/identity services + telemetry ---
builder.Services.AddPlatformInfrastructure(builder.Configuration);
builder.Services.AddPlatformTelemetry(builder.Configuration);

// --- Spec 014: model registry, access gating, config management (Layer 1, depends on 002) ---
builder.Services.AddModelAccessInfrastructure(builder.Configuration);

// --- Spec 004: chat message pipeline (Layer 3, depends on 002 + 014) ---
builder.Services.AddChatInfrastructure(builder.Configuration);

// --- Spec 017: changelog, health probes, feedback proxy (Layer 1, depends on 002 + 004) ---
builder.Services.AddSupportInfrastructure(builder.Configuration);

// --- Spec 018: sharing-policy evaluator (Layer 1, depends only on 002) ---
builder.Services.AddSharingInfrastructure(builder.Configuration);

// --- Authorization: deny-by-default fallback + server-side admin gate (spec 002 FR-011/012/013) ---
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
    .AddPolicy(PolicyNames.RequireAuthenticated, policy => policy.RequireAuthenticatedUser())
    .AddPolicy(PolicyNames.RequireAdmin, policy => policy.RequireClaim(AppClaimTypes.IsAdmin, "true"));

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Spec 024: chat home screen state, scoped per Blazor circuit.
builder.Services.AddScoped<EnterpriseAIPlatform.Web.Services.ChatComposerState>();
builder.Services.AddScoped<EnterpriseAIPlatform.Web.Services.ConversationListState>();
builder.Services.AddScoped<EnterpriseAIPlatform.Web.Services.CompareSessionState>();
builder.Services.AddScoped<EnterpriseAIPlatform.Web.Services.UpdateBannerState>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

// Scoped to non-API requests: re-executing an API error response (403/404/etc.) against the
// Blazor "/not-found" page — which only supports GET/HEAD/POST — corrupts PUT/DELETE admin
// responses into a spurious 405. Browser page navigation still gets the friendly not-found page.
app.UseWhen(
    context => !context.Request.Path.StartsWithSegments("/api"),
    branch => branch.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true));
app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

// Public routes (explicit allow-list) — spec 002 contracts/route-table.md.
// Spec 017: real dependency checks (Cosmos DB always; Key Vault on readiness only, FR-006/007),
// via a response writer that serializes only check name + status — never HealthReportEntry
// .Exception/.Description, which is where raw provider error text would otherwise leak (FR-005).
var healthResponseOptions = new HealthCheckOptions { ResponseWriter = WriteSanitizedHealthResponseAsync };
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live"),
    ResponseWriter = healthResponseOptions.ResponseWriter,
}).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = healthResponseOptions.ResponseWriter,
}).AllowAnonymous();

// Protected endpoint: resolves the current user via the canonical accessor (FR-001/005).
app.MapGet("/api/whoami", (ICurrentUserAccessor currentUser) =>
{
    var result = currentUser.GetCurrentUser();
    return result.Status == ResponseStatus.OK
        ? Results.Ok(result.Response)
        : Results.Unauthorized();
}).RequireAuthorization(PolicyNames.RequireAuthenticated);

// Admin-only endpoint: server-side gate, independent of any UI state (FR-012/013).
app.MapGet("/api/admin/ping", () => Results.Ok(new { pong = true }))
    .RequireAuthorization(PolicyNames.RequireAdmin);

// Spec 014: model registry, access gating, config management, and the preferences 401 fix.
app.MapModelAccessEndpoints();
app.MapUserPreferencesEndpoints();

// Spec 004: chat message pipeline (send -> stream -> persist).
app.MapChatEndpoints();

// Spec 006: multi-chat session persistence + parallel dispatch.
app.MapMultiChatEndpoints();

// Spec 017: changelog, version-alert acknowledgment, feedback proxy.
app.MapSupportEndpoints();

app.MapControllers();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

/// <summary>Exposed so integration tests can use <c>WebApplicationFactory&lt;Program&gt;</c>.</summary>
public partial class Program
{
    /// <summary>
    /// Spec 017 FR-005: serializes only the aggregate status and, per check, its name and status —
    /// never <see cref="HealthReportEntry.Exception"/>/<see cref="HealthReportEntry.Description"/>,
    /// which is exactly where raw provider error text would otherwise leak.
    /// </summary>
    internal static Task WriteSanitizedHealthResponseAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";
        var payload = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new { name = e.Key, status = e.Value.Status.ToString() }),
        };
        return context.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(payload));
    }
}
