CREATE TABLE [dbo].[Settings] (
    [settingsId]   INT CONSTRAINT [DF_Settings_settingId] DEFAULT ((1)) NOT NULL,
    [isProduction] BIT NOT NULL,
    CONSTRAINT [PK_Settings] PRIMARY KEY CLUSTERED ([settingsId] ASC)
);

