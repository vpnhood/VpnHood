export class ApiException extends Error {
    statusCode: number;
    response?: string;
    exceptionTypeName?: string;
    exceptionTypeFullName?: string;
    headers: any;
    data: any;

    constructor(
        message: string,
        statusCode: number,
        response?: string,
        headers?: any,
        innerException?: Error
    ) {
        super(ApiException.buildMessage(message, statusCode, response));
        Object.setPrototypeOf(this, ApiException.prototype);

        this.statusCode = statusCode;
        this.response = response;
        this.headers = headers;

        let serverException: ServerException = ServerException.tryParse(response);
        if (serverException != null) {
            Object.keys(serverException.data).forEach((key) => {
                this[key] = serverException.data[key];
            });
            this.response = JSON.stringify(serverException, null, 2);
            this.exceptionTypeName = serverException.typeName;
            this.exceptionTypeFullName = serverException.typeFullName;
        }

        if (innerException) {
            this.stack = innerException.stack;
        }
    }

    private static buildMessage(
        message: string,
        statusCode: number,
        response: string | null
    ): string {
        let serverException = ServerException.tryParse(response);
        if (serverException != null)
            return serverException.message || '';

        return `${message}\n\nStatus: ${statusCode}\nResponse:\n${response?.substring(0, Math.min(512, response.length))}`;
    }

    toString(): string {
        return `HTTP Response:\n\n${this.response}\n\n${super.toString()}`;
    }
}

class ServerException {
    data!: { [key: string]: string | null };
    typeName?: string;
    typeFullName?: string;
    message?: string;

    public static tryParse(value: string): ServerException | null {
        if (!value)
            return null;

        try {
            let outServerException: ServerException = JSON.parse(value);
            return outServerException.typeName != null ? outServerException : null;
        } catch {
            return null;
        }
    }
}