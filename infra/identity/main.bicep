extension microsoftGraphV1

@description('Immutable, tenant-unique key used by Microsoft Graph Bicep to deploy the application idempotently.')
param applicationUniqueName string

@description('Display name shown in Microsoft Entra admin center and consent experiences.')
param applicationDisplayName string

@description('Web redirect URIs. Include both the sign-in and signed-out callback URIs used by Microsoft.Identity.Web.')
param webRedirectUris array

@description('Endpoint that receives Entra front-channel single-sign-out notifications.')
param frontChannelLogoutUrl string

resource webApplication 'Microsoft.Graph/applications@v1.0' = {
  uniqueName: applicationUniqueName
  displayName: applicationDisplayName
  description: 'Microsoft Entra application registration for the Enterprise AI Platform Blazor web application.'
  signInAudience: 'AzureADMyOrg'
  groupMembershipClaims: 'SecurityGroup'
  isFallbackPublicClient: false
  web: {
    redirectUris: webRedirectUris
    logoutUrl: frontChannelLogoutUrl
    implicitGrantSettings: {
      enableAccessTokenIssuance: false
      enableIdTokenIssuance: false
    }
  }
}

resource enterpriseApplication 'Microsoft.Graph/servicePrincipals@v1.0' = {
  appId: webApplication.appId
}

output clientId string = webApplication.appId
output applicationObjectId string = webApplication.id
output servicePrincipalObjectId string = enterpriseApplication.id
