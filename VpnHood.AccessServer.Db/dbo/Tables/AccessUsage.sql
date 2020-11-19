CREATE TABLE [dbo].[AccessUsage] (
    [accessTokenId]        UNIQUEIDENTIFIER NOT NULL,
    [clientIp]             VARCHAR (20)     NOT NULL,
    [sentTraffic]          BIGINT           CONSTRAINT [DF_AccessUsage_sentByteCount] DEFAULT ((0)) NOT NULL,
    [receivedTraffic]      BIGINT           CONSTRAINT [DF_AccessUsage_receivedByteCount] DEFAULT ((0)) NOT NULL,
    [totalSentTraffic]     BIGINT           CONSTRAINT [DF_AccessUsage_totalSentBytes] DEFAULT ((0)) NOT NULL,
    [totalReceivedTraffic] BIGINT           CONSTRAINT [DF_AccessUsage_totalRecievedBytes] DEFAULT ((0)) NOT NULL,
    CONSTRAINT [PK_AccessUsage] PRIMARY KEY CLUSTERED ([accessTokenId] ASC, [clientIp] ASC),
    CONSTRAINT [FK_AccessUsage_accessTokenId] FOREIGN KEY ([accessTokenId]) REFERENCES [dbo].[AccessToken] ([accessTokenId]) ON DELETE CASCADE ON UPDATE CASCADE
);


GO
ALTER TABLE [dbo].[AccessUsage] NOCHECK CONSTRAINT [FK_AccessUsage_accessTokenId];




GO





GO


