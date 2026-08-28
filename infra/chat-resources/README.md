# Chat resources bootstrap (Cosmos DB, SQL Server, Azure AI Foundry)

This deployment creates the three Azure resources chat needs at runtime:

- a **serverless Cosmos DB** account (containers `users` and `chat`, partitioned on `/PartitionKey`), with account keys disabled — access is granted only via the Cosmos DB Built-in Data Contributor data-plane role;
- an **Azure SQL Server/Database** (Azure AD-only authentication, no SQL logins) for the model access catalog;
- an **Azure AI Foundry (Cognitive Services) account** with a `gpt-5`-named deployment, granting the Cognitive Services OpenAI User role.

Nothing here issues a static secret or key. The app already authenticates to Cosmos and Foundry with `DefaultAzureCredential`; pointing the SQL connection string at Active Directory Default auth (see bottom of this file) keeps SQL passwordless too.

This is meant to be created on demand for local dev/test and deleted when you're done — everything lives in one resource group, so teardown is a single command.

## Prerequisites

- Azure CLI with Bicep CLI `0.36.1` or later.
- An Azure subscription and permission to create a resource group and role assignments within it.
- .NET 10 SDK for configuring local user-secrets and running the EF Core migration.

## Deploy

```bash
az login
az account set --subscription <development-subscription-id>

az group create --name eap-chat-dev --location eastus2

export EAP_PRINCIPAL_ID="$(az ad signed-in-user show --query id -o tsv)"
export EAP_PRINCIPAL_DISPLAY_NAME="$(az ad signed-in-user show --query userPrincipalName -o tsv)"
export EAP_CLIENT_IP="$(curl -s ifconfig.me)"

az deployment group create \
  --name eap-chat-dev \
  --resource-group eap-chat-dev \
  --template-file infra/chat-resources/main.bicep \
  --parameters infra/chat-resources/dev.bicepparam
```

## Wire the outputs into local config

```bash
COSMOS_ENDPOINT="$(az deployment group show --name eap-chat-dev --resource-group eap-chat-dev --query properties.outputs.cosmosAccountEndpoint.value -o tsv)"
SQL_SERVER_FQDN="$(az deployment group show --name eap-chat-dev --resource-group eap-chat-dev --query properties.outputs.sqlServerFqdn.value -o tsv)"
SQL_DATABASE_NAME="$(az deployment group show --name eap-chat-dev --resource-group eap-chat-dev --query properties.outputs.sqlDatabaseName.value -o tsv)"
FOUNDRY_ENDPOINT="$(az deployment group show --name eap-chat-dev --resource-group eap-chat-dev --query properties.outputs.foundryEndpoint.value -o tsv)"

dotnet user-secrets set "Cosmos:AccountEndpoint" "$COSMOS_ENDPOINT" --project src/EnterpriseAIPlatform.Web
dotnet user-secrets set "ModelAccessSql:ConnectionString" \
  "Server=tcp:${SQL_SERVER_FQDN},1433;Database=${SQL_DATABASE_NAME};Authentication=Active Directory Default;Encrypt=True;" \
  --project src/EnterpriseAIPlatform.Web
dotnet user-secrets set "ModelProviders:AzureFoundry:Endpoint" "$FOUNDRY_ENDPOINT" --project src/EnterpriseAIPlatform.Web
```

Apply the model access schema migration to the new database (uses the same `Authentication=Active Directory Default` connection, so your signed-in `az login` identity — the SQL AAD admin — needs to be the one running this):

```bash
dotnet ef database update \
  --project src/EnterpriseAIPlatform.Infrastructure \
  --startup-project src/EnterpriseAIPlatform.Web
```

Run the app (`az login` must still be active locally — the Cosmos/Foundry/SQL clients all resolve credentials through `DefaultAzureCredential`, which falls back to the Azure CLI credential):

```bash
dotnet run --project src/EnterpriseAIPlatform.Web
```

## Teardown

```bash
az group delete --name eap-chat-dev --yes --no-wait
```

Deleting the resource group removes the Cosmos account, SQL Server/Database, and Foundry account together — there's nothing left to clean up separately (no Key Vault secrets, no standalone role assignments outside the group).

## Notes

- The `gpt-4o` model/version deployed under the `gpt-5` deployment name is a default (`modelName`/`modelVersion` params in `main.bicep`) chosen to match the model id (`azure-foundry:gpt-5`) already seeded in the model access catalog. Override the params if you want a different underlying model — just keep the deployment name `gpt-5`, or update the seeded `ModelConfigs` row to match a different deployment name.
- Content Safety, Key Vault, and Redis are intentionally not provisioned here — the chat pipeline runs without them in Development (Content Safety fails open when unconfigured; the others aren't required for a basic chat send/receive).
