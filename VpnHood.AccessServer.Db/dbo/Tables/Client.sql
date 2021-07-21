CREATE TABLE [dbo].[Client] (
    [clientId]    UNIQUEIDENTIFIER NOT NULL,
    [userAgent]   NVARCHAR (100)   NULL,
    [createdTime] DATETIME         CONSTRAINT [DF_Client_createdTime] DEFAULT (getdate()) NOT NULL,
    CONSTRAINT [PK_Client] PRIMARY KEY CLUSTERED ([clientId] ASC)
);

