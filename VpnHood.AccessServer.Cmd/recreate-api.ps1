$projectDir = $PSScriptRoot;
nswag swagger2csclient `
	/runtime:Net50 `
	/input:https://localhost:5001/swagger/v1/swagger.json `
	/output:$projectDir/Api.cs `
	/namespace:VpnHood.AccessServer.Apis `
	/operationGenerationMode:MultipleClientsFromFirstTagAndOperationId `
	/generatePrepareRequestAndProcessResponseAsAsyncMethods:true `
	/clientBaseClass:ApiBase `
	/injectHttpClient:false `
    /disposeHttpClient:false `
	/generateOptionalParameters:true `
	/useBaseUrl:false
