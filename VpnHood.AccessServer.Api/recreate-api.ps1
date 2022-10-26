$projectDir = $PSScriptRoot;

nswag run "$projectDir/Api/Api.nswag"  `
	/variables:namespace=VpnHood.AccessServer.Api,apiFile=Api.cs,projectDir=$projectDir/../VpnHood.AccessServer/VpnHood.AccessServer.csproj `
	/runtime:Net60
