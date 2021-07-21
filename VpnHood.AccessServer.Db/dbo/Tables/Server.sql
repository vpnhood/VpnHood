CREATE TABLE [dbo].[Server] (
    [serverId]         INT            IDENTITY (1, 1) NOT NULL,
    [externaServerlId] CHAR (40)      NOT NULL,
    [description]      NVARCHAR (255) NOT NULL,
    [createdTime]      DATETIME       CONSTRAINT [DF_Server_createdTime] DEFAULT (getdate()) NOT NULL,
    [lastStatusTime]   DATETIME       CONSTRAINT [DF_Server_lastStatusTime] DEFAULT (getdate()) NOT NULL,
    [lastSessionCount] INT            CONSTRAINT [DF_Server_lastSessionCount] DEFAULT ((0)) NOT NULL,
    CONSTRAINT [PK_VpnServers] PRIMARY KEY CLUSTERED ([serverId] ASC)
);

