$projectDir = $PSScriptRoot;
nswag swagger2csclient `
	/runtime:Net50 `
	/input:https://localhost:5001/swagger/v1/swagger.json `
	/output:$projectDir/Apis/Api.cs `
	/namespace:VpnHood.AccessServer.UI.Apis `
	/operationGenerationMode:MultipleClientsFromFirstTagAndOperationId `
	/jsonLibrary:SystemTextJson `
	/injectHttpClient:true `
    /disposeHttpClient:false `
	/generateOptionalParameters:true `
	/useBaseUrl:false `
	/classname:"{controller}Controller"
