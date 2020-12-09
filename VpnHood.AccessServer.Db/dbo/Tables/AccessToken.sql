CREATE TABLE [dbo].[AccessToken] (
    [accessTokenId]   UNIQUEIDENTIFIER CONSTRAINT [DF_AccessToken_tokenId] DEFAULT (newid()) NOT NULL,
    [accessTokenName] VARCHAR (50)     NULL,
    [supportId]       INT              IDENTITY (1, 1) NOT NULL,
    [secret]          BINARY (16)      CONSTRAINT [DF_AccessToken_secret] DEFAULT (Crypt_Gen_Random((16))) NOT NULL,
    [serverEndPoint]  VARCHAR (20)     NOT NULL,
    [maxTraffic]      BIGINT           CONSTRAINT [DF_AccessToken_maxTransferByteCount] DEFAULT ((0)) NOT NULL,
    [lifetime]        INT              CONSTRAINT [DF_AccessToken_lifetime] DEFAULT ((0)) NOT NULL,
    [maxClient]       INT              CONSTRAINT [DF_AccessToken_maxClientCount] DEFAULT ((0)) NOT NULL,
    [startTime]       DATETIME         NULL,
    [endTime]         DATETIME         NULL,
    [isPublic]        BIT              CONSTRAINT [DF_AccessToken_isPublic] DEFAULT ((0)) NOT NULL,
    CONSTRAINT [PK_AccessToken] PRIMARY KEY CLUSTERED ([accessTokenId] ASC),
    CONSTRAINT [FK_AccessToken_serverEndPoint] FOREIGN KEY ([serverEndPoint]) REFERENCES [dbo].[Certificate] ([serverEndPoint])
);




GO





GO





GO



GO
CREATE UNIQUE NONCLUSTERED INDEX [IX_supportId]
    ON [dbo].[AccessToken]([supportId] ASC);


GO
EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'/NoFK', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'AccessToken', @level2type = N'COLUMN', @level2name = N'supportId';

