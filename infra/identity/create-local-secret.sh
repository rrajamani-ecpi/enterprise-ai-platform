#!/usr/bin/env bash

set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "Usage: $0 <application-client-id>" >&2
  exit 2
fi

if ! command -v az >/dev/null 2>&1; then
  echo "Azure CLI is required. Install it, sign in with 'az login', and try again." >&2
  exit 1
fi

if ! command -v dotnet >/dev/null 2>&1; then
  echo ".NET SDK is required." >&2
  exit 1
fi

application_client_id="$1"
web_project="src/EnterpriseAIPlatform.Web/EnterpriseAIPlatform.Web.csproj"
credential_name="local-development-$(date -u +%Y%m%d)"
credential_end_date="$(date -u -d '+90 days' +%Y-%m-%d)"
tenant_id="$(az account show --query tenantId --output tsv)"

# Microsoft Graph Bicep deliberately does not return application-password values.
# Create this explicitly as a local-development credential and put it directly in
# .NET user-secrets. Re-running this script adds another credential; remove expired
# or superseded local credentials from the app registration.
client_secret="$(az ad app credential reset \
  --id "$application_client_id" \
  --append \
  --display-name "$credential_name" \
  --end-date "$credential_end_date" \
  --query password \
  --output tsv)"

trap 'unset client_secret' EXIT

dotnet user-secrets set "AzureAd:TenantId" "$tenant_id" --project "$web_project" >/dev/null
dotnet user-secrets set "AzureAd:ClientId" "$application_client_id" --project "$web_project" >/dev/null
dotnet user-secrets set "AzureAd:ClientSecret" "$client_secret" --project "$web_project" >/dev/null

echo "Configured .NET user-secrets for $web_project"
echo "Tenant ID: $tenant_id"
echo "Client ID: $application_client_id"
echo "Credential expires: $credential_end_date"
echo "The client-secret value was not written to the repository or printed."
