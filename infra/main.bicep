targetScope = 'subscription'

@minLength(1)
@maxLength(32)
param environmentName string

param location string = 'swedencentral'

@description('Object ID receiving inference access. azd supplies the signed-in principal.')
param principalId string

@allowed([
  'GlobalStandard'
  'DataZoneStandard'
])
param modelSku string = 'GlobalStandard'

@minValue(1)
param modelCapacity int = 10

var tags = {
  'azd-env-name': environmentName
  purpose: 'agentic-sdlc-demo'
}

resource resourceGroup 'Microsoft.Resources/resourceGroups@2025-04-01' = {
  name: 'rg-${environmentName}'
  location: location
  tags: tags
}

module foundry 'foundry.bicep' = {
  name: 'foundry'
  scope: resourceGroup
  params: {
    location: location
    accountName: 'aif-${uniqueString(subscription().id, resourceGroup.name)}'
    principalId: principalId
    modelSku: modelSku
    modelCapacity: modelCapacity
    tags: tags
  }
}

output AZURE_RESOURCE_GROUP string = resourceGroup.name
output AZURE_FOUNDRY_ACCOUNT_NAME string = foundry.outputs.accountName
output AZURE_FOUNDRY_PROJECT_NAME string = foundry.outputs.projectName
output AZURE_FOUNDRY_PROJECT_ENDPOINT string = foundry.outputs.projectEndpoint
output AZURE_OPENAI_ENDPOINT string = foundry.outputs.openAIEndpoint
output AZURE_OPENAI_DEPLOYMENT_NAME string = foundry.outputs.deploymentName
