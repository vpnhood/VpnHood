CREATE PROC dbo.Init_productionSet
    @serverName TSTRING = NULL, @isProduction BIT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF (@serverName IS NULL OR  @serverName <> @@SERVERNAME)
    BEGIN
        DECLARE @msg TSTRING = 'Invalid Server Name! ServerName muset set to: ' + @@SERVERNAME;
        THROW 55001, @msg, 1;
    END;

    -- update production if serverName is confirmed
    IF (@isProduction IS NOT NULL)
        UPDATE  dbo.Settings
           SET  isProduction = @isProduction
         WHERE  1 = 1;

    IF (@@ROWCOUNT = 0)
        INSERT INTO dbo.Settings (isProduction)
        VALUES (@isProduction);
END;