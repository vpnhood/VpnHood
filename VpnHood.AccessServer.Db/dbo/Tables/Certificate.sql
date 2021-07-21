CREATE TABLE [dbo].[Certificate] (
    [serverEndPoint] VARCHAR (20)    NOT NULL,
    [serverId]       INT             NULL,
    [rawData]        VARBINARY (MAX) NOT NULL,
    CONSTRAINT [PK_Certificate] PRIMARY KEY CLUSTERED ([serverEndPoint] ASC),
    CONSTRAINT [FK_Certificate_serverId] FOREIGN KEY ([serverId]) REFERENCES [dbo].[Server] ([serverId])
);



