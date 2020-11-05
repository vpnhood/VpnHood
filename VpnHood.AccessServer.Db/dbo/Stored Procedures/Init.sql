
CREATE PROC dbo.Init
AS
BEGIN
    SET NOCOUNT ON;

    -- check production
    DECLARE @isProduction BIT;
    SELECT TOP 1    @isProduction = isProduction
      FROM  dbo.Settings;

    IF (@isProduction IS NULL OR @isProduction = 1)
    BEGIN
        DECLARE @msg TSTRING = 'Could not Init in production! first call: Init_productionSet';
        THROW 55001, @msg, 1;
    END;

    DECLARE @tranCount INT = @@TRANCOUNT;
    IF (@tranCount = 0)
        BEGIN TRANSACTION;
    BEGIN TRY

        -- clear database records
        DECLARE @whereand TSTRING = N'AND o.name NOT IN ( ''sysdiagrams'')';
        EXEC sys.sp_MSforeachtable @command1 = N'ALTER TABLE ? NOCHECK CONSTRAINT ALL', @whereand = @whereand;
        EXEC sys.sp_MSforeachtable @command1 = N'DELETE FROM ?', @whereand = @whereand;

        -- restore isProduction
        INSERT INTO dbo.Settings (isProduction)
        VALUES (@isProduction);

        IF (@tranCount = 0) COMMIT;
    END TRY
    BEGIN CATCH
        IF (@tranCount = 0)
            ROLLBACK TRANSACTION;
        THROW;
    END CATCH;

END;