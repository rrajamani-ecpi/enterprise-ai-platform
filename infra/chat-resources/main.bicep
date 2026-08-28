@description('Object ID of the principal (your Azure AD user for local dev, or a managed identity) granted data-plane access to Cosmos DB and Azure AI Foundry, and set as the SQL Server Azure AD administrator.')
param principalId string

@description('Type of the principal above.')
@allowed([
  'User'
  'ServicePrincipal'
])
param principalType string = 'User'

@description('Display name (UPN or app name) shown as the SQL Server Azure AD administrator. Required by Microsoft.Sql/servers administrators.login.')
param principalDisplayName string

@description('Azure region for Cosmos DB and Azure AI Foundry.')
param location string = 'eastus2'

@description('Azure region for the SQL Server/Database. Kept separate because SQL logical server provisioning is restricted to a subset of regions on this subscription.')
param sqlLocation string = 'centralus'

@description('Client IP address allowed through the SQL Server firewall (e.g. your dev machine public IP).')
param clientIpAddress string

@description('Azure OpenAI model to deploy for chat.')
param modelName string = 'gpt-4.1'

@description('Model version for the deployment.')
param modelVersion string = '2025-04-14'

@description('Model format for the Cognitive Services deployment.')
param modelFormat string = 'OpenAI'

@description('Capacity (in thousands of tokens-per-minute units) for the model deployment.')
param modelCapacity int = 10

var uniqueSuffix = uniqueString(resourceGroup().id)
var cosmosAccountName = 'eap-chat-cosmos-${uniqueSuffix}'
var sqlServerName = 'eap-chat-sql-${uniqueSuffix}'
var sqlDatabaseName = 'eap-model-access'
var foundryAccountName = 'eap-chat-foundry-${uniqueSuffix}'
var foundryModelDeploymentName = 'gpt-5'

var cosmosDataContributorRoleId = '00000000-0000-0000-0000-000000000002'
var cognitiveServicesOpenAiUserRoleId = '5e0bd9bd-7b93-4f28-af87-19fc36ad61bd'

resource cosmosAccount 'Microsoft.DocumentDB/databaseAccounts@2024-05-15' = {
  name: cosmosAccountName
  location: location
  kind: 'GlobalDocumentDB'
  properties: {
    databaseAccountOfferType: 'Standard'
    disableLocalAuth: true
    consistencyPolicy: {
      defaultConsistencyLevel: 'Session'
    }
    locations: [
      {
        locationName: location
        failoverPriority: 0
      }
    ]
    capabilities: [
      {
        name: 'EnableServerless'
      }
    ]
  }
}

resource cosmosDatabase 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases@2024-05-15' = {
  parent: cosmosAccount
  name: 'eap'
  properties: {
    resource: {
      id: 'eap'
    }
  }
}

resource cosmosUsersContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-05-15' = {
  parent: cosmosDatabase
  name: 'users'
  properties: {
    resource: {
      id: 'users'
      partitionKey: {
        paths: [
          '/PartitionKey'
        ]
        kind: 'Hash'
      }
    }
  }
}

resource cosmosChatContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-05-15' = {
  parent: cosmosDatabase
  name: 'chat'
  properties: {
    resource: {
      id: 'chat'
      partitionKey: {
        paths: [
          '/PartitionKey'
        ]
        kind: 'Hash'
      }
    }
  }
}

resource cosmosDataContributorAssignment 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2024-05-15' = {
  parent: cosmosAccount
  name: guid(cosmosAccount.id, principalId, cosmosDataContributorRoleId)
  properties: {
    roleDefinitionId: '${cosmosAccount.id}/sqlRoleDefinitions/${cosmosDataContributorRoleId}'
    principalId: principalId
    scope: cosmosAccount.id
  }
}

resource sqlServer 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: sqlServerName
  location: sqlLocation
  properties: {
    version: '12.0'
    administrators: {
      administratorType: 'ActiveDirectory'
      principalType: principalType
      login: principalDisplayName
      sid: principalId
      azureADOnlyAuthentication: true
    }
  }
}

resource sqlDatabase 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sqlServer
  name: sqlDatabaseName
  location: sqlLocation
  sku: {
    name: 'Basic'
    tier: 'Basic'
  }
}

resource sqlFirewallClientIp 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = {
  parent: sqlServer
  name: 'AllowClientIp'
  properties: {
    startIpAddress: clientIpAddress
    endIpAddress: clientIpAddress
  }
}

resource foundryAccount 'Microsoft.CognitiveServices/accounts@2024-10-01' = {
  name: foundryAccountName
  location: location
  kind: 'AIServices'
  sku: {
    name: 'S0'
  }
  properties: {
    customSubDomainName: foundryAccountName
  }
}

resource foundryModelDeployment 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = {
  parent: foundryAccount
  name: foundryModelDeploymentName
  sku: {
    name: 'GlobalStandard'
    capacity: modelCapacity
  }
  properties: {
    model: {
      format: modelFormat
      name: modelName
      version: modelVersion
    }
  }
}

resource foundryOpenAiUserAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(foundryAccount.id, principalId, cognitiveServicesOpenAiUserRoleId)
  scope: foundryAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', cognitiveServicesOpenAiUserRoleId)
    principalId: principalId
    principalType: principalType
  }
}

output cosmosAccountEndpoint string = cosmosAccount.properties.documentEndpoint
output sqlServerFqdn string = sqlServer.properties.fullyQualifiedDomainName
output sqlDatabaseName string = sqlDatabaseName
output foundryEndpoint string = foundryAccount.properties.endpoint
