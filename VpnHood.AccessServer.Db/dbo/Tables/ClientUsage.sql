CREATE TABLE [dbo].[ClientUsage] (
    [tokenId]              UNIQUEIDENTIFIER NOT NULL,
    [clientIp]             VARCHAR (20)     NOT NULL,
    [sentTraffic]          BIGINT           CONSTRAINT [DF_ClientUsage_sentByteCount] DEFAULT ((0)) NOT NULL,
    [receivedTraffic]      BIGINT           CONSTRAINT [DF_ClientUsage_receivedByteCount] DEFAULT ((0)) NOT NULL,
    [totalReceivedTraffic] BIGINT           CONSTRAINT [DF_ClientUsage_totalRecievedBytes] DEFAULT ((0)) NOT NULL,
    [totalSentTraffic]     BIGINT           CONSTRAINT [DF_ClientUsage_totalSentBytes] DEFAULT ((0)) NOT NULL,
    CONSTRAINT [PK_ClientUsage] PRIMARY KEY CLUSTERED ([tokenId] ASC, [clientIp] ASC),
    CONSTRAINT [FK_ClientUsage_tokenId] FOREIGN KEY ([tokenId]) REFERENCES [dbo].[Token] ([tokenId]) ON DELETE CASCADE ON UPDATE CASCADE
);


GO
ALTER TABLE [dbo].[ClientUsage] NOCHECK CONSTRAINT [FK_ClientUsage_tokenId];



