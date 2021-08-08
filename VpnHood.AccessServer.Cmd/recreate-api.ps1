$projectDir = $PSScriptRoot;
nswag swagger2csclient `
	/runtime:Net50 `
	/input:http://localhost:5000/swagger/v1/swagger.json `
	/output:$projectDir/Api.cs `
	/namespace:VpnHood.AccessServer.Apis `
	/operationGenerationMode:MultipleClientsFromFirstTagAndOperationId `
	/generatePrepareRequestAndProcessResponseAsAsyncMethods:true `
	/clientBaseClass:ApiBase `
	/injectHttpClient:false `
    /disposeHttpClient:false `
	/useBaseUrl:false
