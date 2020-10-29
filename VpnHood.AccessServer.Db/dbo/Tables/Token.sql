CREATE TABLE [dbo].[Token] (
    [tokenId]        UNIQUEIDENTIFIER CONSTRAINT [DF_Token_tokenId] DEFAULT (newid()) NOT NULL,
    [tokenName]      VARCHAR (50)     NOT NULL,
    [supportId]      INT              IDENTITY (1, 1) NOT NULL,
    [secret]         BINARY (16)      CONSTRAINT [DF_Token_secret] DEFAULT (Crypt_Gen_Random((16))) NOT NULL,
    [dnsName]        NVARCHAR (50)    NOT NULL,
    [serverEndPoint] VARCHAR (20)     NULL,
    [maxTraffic]     BIGINT           CONSTRAINT [DF_Token_maxTransferByteCount] DEFAULT ((0)) NOT NULL,
    [lifetime]       INT              CONSTRAINT [DF_Token_lifetime] DEFAULT ((0)) NOT NULL,
    [maxClient]      INT              CONSTRAINT [DF_Token_maxClientCount] DEFAULT ((0)) NOT NULL,
    [startTime]      DATETIME         NULL,
    [endTime]        DATETIME         NULL,
    [isPublic]       BIT              CONSTRAINT [DF_Token_isPublic] DEFAULT ((0)) NOT NULL,
    CONSTRAINT [PK_Token] PRIMARY KEY CLUSTERED ([tokenId] ASC)
);


GO
CREATE UNIQUE NONCLUSTERED INDEX [IX_supportId]
    ON [dbo].[Token]([supportId] ASC);


GO
EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'/NoFK', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Token', @level2type = N'COLUMN', @level2name = N'supportId';

