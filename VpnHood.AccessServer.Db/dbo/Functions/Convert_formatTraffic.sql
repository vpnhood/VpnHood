CREATE FUNCTION [dbo].[Convert_formatTraffic] (@traffic AS BIGINT)
RETURNS TSTRING
BEGIN
	IF (@traffic>1000000000) RETURN CONCAT(FORMAT(@traffic / 1000000000.0, 'N2'), ' GB')
	ELSE IF (@traffic>1000000) RETURN CONCAT(FORMAT(@traffic / 1000000.0, 'N0'), ' MB')
	ELSE IF (@traffic>1000) RETURN CONCAT(FORMAT(@traffic / 1000.0, 'N0'), ' KB');
	ELSE IF (@traffic>0) RETURN CONCAT(@traffic , ' B');
	RETURN @traffic;

END;