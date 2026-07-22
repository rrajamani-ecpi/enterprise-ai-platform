using './main.bicep'

param applicationUniqueName = 'enterprise-ai-platform-local-development'
param applicationDisplayName = 'Enterprise AI Platform - Local Development'
param webRedirectUris = [
  'https://localhost:7231/signin-oidc'
  'https://localhost:7231/signout-callback-oidc'
]
param frontChannelLogoutUrl = 'https://localhost:7231/signout-oidc'
