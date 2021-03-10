﻿





CREATE VIEW [dbo].[UsageLogView]
AS
with T(usageLogId) as
(
	select max(usageLogId) from UsageLog group by clientId
)
SELECT TOP 10000 SUBSTRING(convert(nvarchar(50), accessTokenId), 1, 4) AS accessTokenId
	, SUBSTRING(convert(nvarchar(50),clientId), 1, 4) AS clientId 
	, clientIp --
	, clientVersion --
    , dbo.Convert_formatTraffic(U.sentTraffic) AS sentTraffic --
    , dbo.Convert_formatTraffic(U.receivedTraffic) AS receivedTraffic --
	, dbo.Convert_formatTraffic(U.sentTraffic + U.receivedTraffic) AS traffic --
    , dbo.Convert_formatTraffic(U.cycleSentTraffic) AS cycleSentTraffic --
    , dbo.Convert_formatTraffic(U.cycleReceivedTraffic) AS cycleReceivedTraffic --
	, dbo.Convert_formatTraffic(U.cycleSentTraffic + U.cycleReceivedTraffic) AS cycleTraffic --
    , dbo.Convert_formatTraffic(U.totalSentTraffic) AS totalSentTraffic --
    , dbo.Convert_formatTraffic(U.totalReceivedTraffic) AS totalReceivedTraffic --
	, dbo.Convert_formatTraffic(U.totalSentTraffic + U.totalReceivedTraffic) AS totalTraffic --
	, CONVERT(NVARCHAR, dbo.Convert_toLocalTime(createdTime), 100) as createdTime,
	U.usageLogId
  FROM  dbo.UsageLog AS U
	inner join T on U.usageLogId=T.usageLogId order by U.usageLogId desc