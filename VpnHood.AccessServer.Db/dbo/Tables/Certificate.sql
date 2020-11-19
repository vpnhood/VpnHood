CREATE TABLE [dbo].[Certificate] (
    [serverEndPoint] VARCHAR (20)    NOT NULL,
    [rawData]        VARBINARY (MAX) NOT NULL,
    CONSTRAINT [PK_Certificate] PRIMARY KEY CLUSTERED ([serverEndPoint] ASC)
);

