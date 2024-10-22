export class ApiException extends Error {
    statusCode: number;
    response?: string;
    exceptionTypeName?: string;
    exceptionTypeFullName?: string;
    headers: any;
    data: any = {};

    constructor(
        message: string,
        statusCode: number,
        response?: any,
        headers?: any,
        innerException?: Error | null
    ) {
        const apiError = ApiException.getApiError(response);

        // Let have respone as string to show in toString
        if (!(response instanceof String || typeof response === "string"))
            response = JSON.stringify(response); 

        // Call super with build message
        super(ApiException.buildMessage(apiError, message, statusCode, response));
        Object.setPrototypeOf(this, ApiException.prototype);

        // Set properties
        this.statusCode = statusCode;
        this.response = response;
        this.headers = headers;

        // Try copy data from ApiError
        if (apiError) {
            Object.keys(apiError.data).forEach((key) => {
                if (apiError)
                    this.data[key] = apiError.data[key];
            });
            this.exceptionTypeName = apiError.typeName;
            this.exceptionTypeFullName = apiError.typeFullName;
        }

        if (innerException) {
            this.stack = innerException.stack;
        }
    }

    // Try to convert an ApiError to an ApiException. it usually come from unknown type
    public static fromApiError(apiError: any): ApiException {
        if (apiError)
            throw new Error("apiError can not be null!");

        var apiErrorObj = this.getApiError(apiError);
        return new ApiException(apiErrorObj?.message || "Unknown Error!", 500, apiError, null, null);
    }

    private static buildMessage(
        apiError: IApiError | null,
        message: string,
        statusCode: number,
        response?: string
    ): string {
        if (apiError)
            return apiError.message || '';

        return `${message}\n\nStatus: ${statusCode}\nResponse:\n${response?.substring(0, Math.min(512, response.length))}`;
    }

    override toString(): string {
        return `HTTP Response:\n\n${this.response}\n\n${super.toString()}`;
    }

    private static getApiError(apiError: any): IApiError | null {
        if (!apiError)
            return null;

        // Check if it's a string and try to parse it
        if (typeof apiError === "string") {
            try {
                apiError = JSON.parse(apiError);
            } catch {
                return null;
            }
        }

        // Check if it's a camelCase object by looking for typeName
        if (apiError.typeName) {
            const newApiError: IApiError = {
                data: apiError.data || {},
                typeName: apiError.typeName || null,
                typeFullName: apiError.typeFullName || null,
                message: apiError.message || null
            };
            return newApiError;
        }

        // Check if it's a PascalCase object by looking for TypeName
        if (apiError.TypeName) {
            const newApiError: IApiError = {
                data: apiError.Data || {},
                typeName: apiError.TypeName || null,
                typeFullName: apiError.TypeFullName || null,
                message: apiError.Message || null
            };
            return newApiError;
        }

        // Return null if not a valid PascalCase object
        return null;
    }
}

interface IApiError {
    data: { [key: string]: string | null };
    typeName?: string;
    typeFullName?: string;
    message?: string;
}
