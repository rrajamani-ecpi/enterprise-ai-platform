using './main.bicep'

param principalId = readEnvironmentVariable('EAP_PRINCIPAL_ID', '')
param principalDisplayName = readEnvironmentVariable('EAP_PRINCIPAL_DISPLAY_NAME', '')
param clientIpAddress = readEnvironmentVariable('EAP_CLIENT_IP', '')
