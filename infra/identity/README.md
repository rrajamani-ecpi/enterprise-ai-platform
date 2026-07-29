# Microsoft Entra identity bootstrap

This deployment creates the local-development Microsoft Entra application registration and its corresponding service principal (Enterprise Application). It configures:

- single-tenant workforce sign-in (`AzureADMyOrg`);
- Web redirect URIs for `https://localhost:7231/signin-oidc` and `https://localhost:7231/signout-callback-oidc`;
- the front-channel logout URL `https://localhost:7231/signout-oidc`;
- security-group claims in tokens; and
- authorization-code flow only (implicit token issuance remains disabled).

The application password is intentionally **not** part of Bicep. Microsoft Graph Bicep doesn't support safely returning application-password secret text, and deployment outputs/state must not contain local credentials. Use the separate local-secret command below.

## Prerequisites

- Azure CLI with Bicep CLI `0.36.1` or later.
- An Azure subscription and an existing resource group used to record the deployment.
- An interactive deployment identity with permission to create a resource-group deployment.
- Microsoft Graph delegated `Application.ReadWrite.All` permission, or equivalent Entra authority approved by the identity team, to create the application and service principal.
- .NET 10 SDK for configuring local user-secrets.

The Graph permission is tenant-sensitive. In an enterprise tenant, run this through an identity bootstrap pipeline or an identity administrator rather than granting it to the ordinary application deployment identity.

## Deploy the application registration

Sign in and select the intended development subscription and tenant:

```bash
az login
az account set --subscription <development-subscription-id>
az account show --query '{subscription:id, tenant:tenantId, user:user.name}'
```

Deploy into an existing bootstrap resource group:

```bash
az deployment group create \
  --name eap-identity-dev \
  --resource-group <identity-bootstrap-resource-group> \
  --template-file infra/identity/main.bicep \
  --parameters infra/identity/dev.bicepparam
```

Capture the non-secret client ID:

```bash
EAP_CLIENT_ID="$(az deployment group show \
  --name eap-identity-dev \
  --resource-group <identity-bootstrap-resource-group> \
  --query properties.outputs.clientId.value \
  --output tsv)"
```

Resource groups don't own the lifecycle of Microsoft Graph resources. Deleting the bootstrap resource group does not delete the app registration. Treat deletion of the Entra application as a separate, explicitly approved identity operation.

## Create the local-development credential

The following script creates a 90-day credential and writes the value directly to the Web project's .NET user-secrets store:

```bash
chmod +x infra/identity/create-local-secret.sh
infra/identity/create-local-secret.sh "$EAP_CLIENT_ID"
```

The script uses `--append`; running it again creates another credential rather than replacing existing credentials. Remove expired or superseded local credentials from the app registration.

Verify the non-secret settings without displaying the secret value:

```bash
dotnet user-secrets list \
  --project src/EnterpriseAIPlatform.Web \
  | sed -E 's/ = .*/ = [REDACTED]/'
```

## Configure role mappings

Enabling `SecurityGroup` claims makes the user's security-group object IDs available to the application. It does not decide which group is Admin, Employee, or Student. Configure the existing organizational group IDs separately:

```bash
dotnet user-secrets set "RoleDerivation:Mappings:0:GroupId" "<admin-group-object-id>" \
  --project src/EnterpriseAIPlatform.Web

dotnet user-secrets set "RoleDerivation:Mappings:1:GroupId" "<employee-group-object-id>" \
  --project src/EnterpriseAIPlatform.Web

dotnet user-secrets set "RoleDerivation:Mappings:2:GroupId" "<student-group-object-id>" \
  --project src/EnterpriseAIPlatform.Web
```

The corresponding role names already live in `appsettings.json`.

## Run locally

```bash
dotnet dev-certs https --trust
dotnet run --project src/EnterpriseAIPlatform.Web --launch-profile https
```

Open `https://localhost:7231`. The application should redirect to the selected Entra tenant and return to the Blazor UI after sign-in.

## Production direction

Do not deploy the local client secret to Azure. For hosted environments, create a separate app registration per environment and authenticate the confidential client with a user-assigned managed identity plus a federated identity credential (`SignedAssertionFromManagedIdentity`).
