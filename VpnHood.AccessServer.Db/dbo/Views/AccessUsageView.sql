


CREATE VIEW [dbo].[AccessUsageView]
AS
SELECT TOP 1000 T.supportId, --
    dbo.Convert_formatTraffic(T.maxTraffic) AS maxTraffic, T.accessTokenId AS tokenId, T.accessTokenName AS tokenName, AU.clientIp, --
    dbo.Convert_formatTraffic(AU.cycleSentTraffic) AS sentTraffic, --
    dbo.Convert_formatTraffic(AU.cycleReceivedTraffic) AS receivedTraffic, --
	dbo.Convert_formatTraffic(AU.cycleSentTraffic + AU.cycleReceivedTraffic) AS traffic, --
    dbo.Convert_formatTraffic(AU.totalSentTraffic) AS totalSentTraffic, --
    dbo.Convert_formatTraffic(AU.totalReceivedTraffic) AS totalReceivedTraffic, --
	dbo.Convert_formatTraffic(AU.totalSentTraffic + AU.totalReceivedTraffic) AS totalTraffic --
  FROM  dbo.AccessUsage AS AU
        LEFT JOIN dbo.AccessToken AS T ON T.accessTokenId = AU.accessTokenId
		ORDER BY (AU.cycleSentTraffic + AU.cycleSentTraffic) DESC
GO
EXECUTE sp_addextendedproperty @name = N'MS_DiagramPaneCount', @value = 1, @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'VIEW', @level1name = N'AccessUsageView';
