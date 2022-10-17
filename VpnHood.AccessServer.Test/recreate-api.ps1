$projectDir = $PSScriptRoot;

nswag run "$projectDir/Api/Api.nswag"  `
	/variables:namespace=VpnHood.AccessServer.Agent.Api,apiFile=Api.cs,projectDir=$projectDir/../VpnHood.AccessServer/VpnHood.AccessServer.csproj `
	/runtime:Net60

nswag run "$projectDir/Api/Api.nswag"  `
	/variables:namespace=VpnHood.AccessServer.Api,apiFile=AgentApi.cs,projectDir=$projectDir/../VpnHood.AccessServer.Agent/VpnHood.AccessServer.Agent.csproj `
	/runtime:Net60

