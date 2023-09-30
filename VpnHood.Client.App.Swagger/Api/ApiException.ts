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
        if (!(response instanceof String)) response = JSON.stringify(response);
        super(ApiException.buildMessage(message, statusCode, response));
        Object.setPrototypeOf(this, ApiException.prototype);

        this.statusCode = statusCode;
        this.response = response;
        this.headers = headers;

        let serverException: ServerException | null = ServerException.tryParse(response);
        if (serverException) {
            Object.keys(serverException.Data).forEach((key) => {
                if (serverException)
                    this.data[key] = serverException.Data[key];
            });
            this.exceptionTypeName = serverException.TypeName;
            this.exceptionTypeFullName = serverException.TypeFullName;
        }

        if (innerException) {
            this.stack = innerException.stack;
        }
    }

    private static buildMessage(
        message: string,
        statusCode: number,
        response?: string
    ): string {
        let serverException = ServerException.tryParse(response);
        if (serverException)
            return serverException.Message || '';

        return `${message}\n\nStatus: ${statusCode}\nResponse:\n${response?.substring(0, Math.min(512, response.length))}`;
    }

    override toString(): string {
        return `HTTP Response:\n\n${this.response}\n\n${super.toString()}`;
    }
}

class ServerException {
    Data!: { [key: string]: string | null };
    TypeName?: string;
    TypeFullName?: string;
    Message?: string;

    public static tryParse(value: string | undefined): ServerException | null {
        if (!value)
            return null;

        try {
            let serverException: ServerException = JSON.parse(value);
            return serverException.TypeName ? serverException : null;
        } catch {
            return null;
        }
    }
}