$projectDir = $PSScriptRoot;
nswag swagger2csclient `
	/runtime:Net60 `
	/input:https://localhost:55001/swagger/v1/swagger.json `
	/output:$projectDir/Api/Api.cs `
	/namespace:VpnHood.AccessServer.Api `
	/operationGenerationMode:MultipleClientsFromFirstTagAndPathSegments `
	/jsonLibrary:SystemTextJson `
	/injectHttpClient:true `
    /disposeHttpClient:false `
	/generateOptionalParameters:true `
	/useBaseUrl:false `
	/generateNullableReferenceTypes:false `
	/generateOptionalPropertiesAsNullable:false `
	/generateExceptionClasses:false `
	/dateType:System.DateTime `
	/dateTimeType:System.DateTime `
	/classname:"{controller}Controller"
