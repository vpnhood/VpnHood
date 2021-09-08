$projectDir = $PSScriptRoot;
nswag swagger2csclient `
	/runtime:Net50 `
	/input:https://localhost:5001/swagger/v1/swagger.json `
	/output:$projectDir/Apis/Api.cs `
	/namespace:VpnHood.AccessServer.Cmd.Apis `
	/operationGenerationMode:MultipleClientsFromFirstTagAndOperationId `
	/generatePrepareRequestAndProcessResponseAsAsyncMethods:true `
	/jsonLibrary:SystemTextJson `
	/clientBaseClass:ApiBase `
	/injectHttpClient:false `
    /disposeHttpClient:false `
	/generateOptionalParameters:true `
	/useBaseUrl:false `
	/classname:"{controller}Controller"
