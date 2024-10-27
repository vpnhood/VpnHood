export class ApiException extends Error {
    statusCode: number;
    response: string | null;
    exceptionTypeName: string | null = null;
    exceptionTypeFullName: string | null = null;
    headers: unknown;
    data: { [key: string]: unknown } = {};

    constructor(
        message: string,
        statusCode: number,
        response?: unknown,
        headers?: unknown,
        innerException?: Error | null
    ) {
        const apiError = ApiException.getApiError(response);

        // Let have response as string to show in toString
        const responseStr: string | null = response instanceof String || typeof response === 'string'
            ? response.toString() : JSON.stringify(response);

        // Call super with build message
        super(ApiException.buildMessage(apiError, message, statusCode, responseStr));

        // Set properties
        this.statusCode = statusCode;
        this.response = responseStr;
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

    // Try to convert an ApiError to an ApiException. it usually comes from unknown type
    public static fromApiError(apiError: unknown): ApiException {
        if (apiError)
            throw new Error('apiError can not be null!');

        const apiErrorObj = this.getApiError(apiError);
        return new ApiException(apiErrorObj?.message || 'Unknown Error!', 500, apiError, null, null);
    }

    private static buildMessage(
        apiError: IApiErrorCamel | null,
        message: string,
        statusCode: number,
        response: string | null
    ): string {
        if (apiError)
            return apiError.message || '';

        return `${message}\n\nStatus: ${statusCode}\nResponse:\n${response?.substring(0, Math.min(512, response.length))}`;
    }

    override toString(): string {
        return `HTTP Response:\n\n${this.response}\n\n${super.toString()}`;
    }

    private static getApiError(apiError: unknown): IApiErrorCamel | null {
        if (!apiError)
            return null;

        // Check if it's a string and try to parse it
        if (typeof apiError === 'string') {
            try {
                apiError = JSON.parse(apiError);
            } catch {
                return null;
            }
        }

        // Check if it's a camelCase object by looking for typeName
        const apiErrorCamel: IApiErrorCamel = apiError as IApiErrorCamel;
        if (apiErrorCamel.typeName) {
            return {
                data: apiErrorCamel.data || {},
                typeName: apiErrorCamel.typeName || null,
                typeFullName: apiErrorCamel.typeFullName || null,
                message: apiErrorCamel.message || null
            };
        }

        // Check if it's a PascalCase object by looking for TypeName
        const apiErrorPascal: IApiErrorPascal = apiError as IApiErrorPascal;
        if (apiErrorPascal.TypeName) {
            return {
                data: apiErrorPascal.Data || {},
                typeName: apiErrorPascal.TypeName || null,
                typeFullName: apiErrorPascal.TypeFullName || null,
                message: apiErrorPascal.Message || null
            };
        }

        // Return null if not a valid PascalCase object
        return null;
    }
}

interface IApiErrorCamel {
    data: { [key: string]: unknown };
    typeName: string | null;
    typeFullName: string | null;
    message: string | null;
}
interface IApiErrorPascal {
    Data: { [key: string]: unknown };
    TypeName: string | null;
    TypeFullName: string | null;
    Message: string | null;
}