namespace VpnHood.AppLib.Api.SwaggerHost.Exceptions;

internal class SwaggerOnlyException()
    : Exception("This method is intended exclusively for use by Swagger and should not be called directly.");