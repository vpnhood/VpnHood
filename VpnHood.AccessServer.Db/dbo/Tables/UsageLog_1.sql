CREATE TABLE [dbo].[UsageLog] (
    [usageLogId]           BIGINT           IDENTITY (1, 1) NOT NULL,
    [accessTokenId]        UNIQUEIDENTIFIER NOT NULL,
    [clientId]             UNIQUEIDENTIFIER NOT NULL,
    [clientIp]             VARCHAR (20)     NOT NULL,
    [clientVersion]        VARCHAR (20)     NULL,
    [sentTraffic]          BIGINT           NOT NULL,
    [receivedTraffic]      BIGINT           NOT NULL,
    [cycleSentTraffic]     BIGINT           NOT NULL,
    [cycleReceivedTraffic] BIGINT           NOT NULL,
    [totalSentTraffic]     BIGINT           NOT NULL,
    [totalReceivedTraffic] BIGINT           NOT NULL,
    [createdTime]          DATETIME         CONSTRAINT [DF_UsageLog_createdTime] DEFAULT (getdate()) NOT NULL,
    CONSTRAINT [PK_UsageLog] PRIMARY KEY CLUSTERED ([usageLogId] ASC)
);

