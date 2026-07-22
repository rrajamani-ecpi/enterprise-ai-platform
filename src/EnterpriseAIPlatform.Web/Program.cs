using EnterpriseAIPlatform.Application.Authorization;
using EnterpriseAIPlatform.Application.Common;
using EnterpriseAIPlatform.Application.Identity;
using EnterpriseAIPlatform.Infrastructure.Authentication;
using EnterpriseAIPlatform.Infrastructure.DependencyInjection;
using EnterpriseAIPlatform.Infrastructure.Telemetry;
using EnterpriseAIPlatform.Web.Components;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;

var builder = WebApplication.CreateBuilder(args);

// --- Authentication: Microsoft Entra ID (OIDC) via Microsoft.Identity.Web (spec 002 FR-010) ---
builder.Services
    .AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("AzureAd"));

builder.Services.AddControllersWithViews().AddMicrosoftIdentityUI();

// --- Canonical session/role/identity services + telemetry ---
builder.Services.AddPlatformInfrastructure(builder.Configuration);
builder.Services.AddPlatformTelemetry(builder.Configuration);

// --- Authorization: deny-by-default fallback + server-side admin gate (spec 002 FR-011/012/013) ---
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
    .AddPolicy(PolicyNames.RequireAuthenticated, policy => policy.RequireAuthenticatedUser())
    .AddPolicy(PolicyNames.RequireAdmin, policy => policy.RequireClaim(AppClaimTypes.IsAdmin, "true"));

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

// Public routes (explicit allow-list) — spec 002 contracts/route-table.md
app.MapGet("/health/live", () => Results.Ok(new { status = "live" })).AllowAnonymous();
app.MapGet("/health/ready", () => Results.Ok(new { status = "ready" })).AllowAnonymous();

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

app.MapControllers();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

/// <summary>Exposed so integration tests can use <c>WebApplicationFactory&lt;Program&gt;</c>.</summary>
public partial class Program;
