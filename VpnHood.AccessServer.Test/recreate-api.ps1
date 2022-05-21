$projectDir = $PSScriptRoot;

nswag run "$projectDir/Api/Api.nswag"  `
	/variables:namespace=VpnHood.AccessServer.Api,projectDir=$projectDir/../VpnHood.AccessServer/VpnHood.AccessServer.csproj `
	/runtime:Net60