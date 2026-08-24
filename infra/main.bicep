@description('ArtSync lab: Azure SQL as the cloud source stand-in. Not production.')
param location string = resourceGroup().location
param administratorLogin string
@secure()
param administratorLoginPassword string
param clientIpv4 string
param databaseName string = 'MARS'
param skuName string = 'GP_S_Gen5_1'
param maxSizeBytes int = 34359738368

var serverPrefix = 'artsyncdev'

resource sqlServer 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: '${serverPrefix}${uniqueString(resourceGroup().id)}'
  location: location
  properties: {
    administratorLogin: administratorLogin
    administratorLoginPassword: administratorLoginPassword
    version: '12.0'
    publicNetworkAccess: 'Enabled'
    minimalTlsVersion: '1.2'
  }
}

resource allowAzure 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = {
  parent: sqlServer
  name: 'AllowAzureServices'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource allowClient 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = {
  parent: sqlServer
  name: 'AllowLabClient'
  properties: {
    startIpAddress: clientIpv4
    endIpAddress: clientIpv4
  }
}

resource marsDb 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sqlServer
  name: databaseName
  location: location
  sku: {
    name: skuName
    tier: 'GeneralPurpose'
    family: 'Gen5'
    capacity: 1
  }
  properties: {
    collation: 'SQL_Latin1_General_CP1_CI_AS'
    maxSizeBytes: maxSizeBytes
    autoPauseDelay: 60
    minCapacity: json('0.5')
    requestedBackupStorageRedundancy: 'Local'
  }
}

output sqlServerName string = sqlServer.name
output sqlServerFqdn string = sqlServer.properties.fullyQualifiedDomainName
output databaseName string = marsDb.name
output adoNet string = 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Database=${marsDb.name};User ID=${administratorLogin};Encrypt=True;TrustServerCertificate=False;Connection Timeout=60'
