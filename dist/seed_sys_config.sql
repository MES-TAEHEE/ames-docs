-- ============================================================
-- Seed: SYS_Config
-- Regenerated: 2026-08-01 from live DB AMES_DEV (15 rows)
-- 정렬: Category, SortOrder, ConfigKey. ConfigID 는 IDENTITY(재삽입 시 새로 부여).
-- ============================================================

SET NOCOUNT ON;
GO

DELETE dbo.SYS_Config;
GO

INSERT INTO dbo.SYS_Config (ConfigKey, ConfigType, Category, ConfigValue, CodeName, Unit, UsedByModulesJSON, SortOrder, IsActive, CreatedBy) VALUES (N'SAP_SYNC_INTERVAL_MIN', N'INT', N'Interfaces', N'5', N'SAP sync interval', N'min', NULL, 80, 1, 'admin@ames.local');
INSERT INTO dbo.SYS_Config (ConfigKey, ConfigType, Category, ConfigValue, CodeName, Unit, UsedByModulesJSON, SortOrder, IsActive, CreatedBy) VALUES (N'PLC_HEARTBEAT_SEC', N'INT', N'Interfaces', N'10', N'PLC heartbeat', N'sec', NULL, 90, 1, 'admin@ames.local');
INSERT INTO dbo.SYS_Config (ConfigKey, ConfigType, Category, ConfigValue, CodeName, Unit, UsedByModulesJSON, SortOrder, IsActive, CreatedBy) VALUES (N'MAX_RETRY_COUNT', N'INT', N'Interfaces', N'3', N'Max retry per interface call', NULL, NULL, 140, 1, 'admin@ames.local');
INSERT INTO dbo.SYS_Config (ConfigKey, ConfigType, Category, ConfigValue, CodeName, Unit, UsedByModulesJSON, SortOrder, IsActive, CreatedBy) VALUES (N'OTD_TARGET_PCT', N'DECIMAL', N'KPI', N'95.0', N'On-time delivery target', N'%', NULL, 30, 1, 'admin@ames.local');
INSERT INTO dbo.SYS_Config (ConfigKey, ConfigType, Category, ConfigValue, CodeName, Unit, UsedByModulesJSON, SortOrder, IsActive, CreatedBy) VALUES (N'OEE_TARGET_PCT', N'DECIMAL', N'KPI', N'85.0', N'OEE target', N'%', NULL, 40, 1, 'admin@ames.local');
INSERT INTO dbo.SYS_Config (ConfigKey, ConfigType, Category, ConfigValue, CodeName, Unit, UsedByModulesJSON, SortOrder, IsActive, CreatedBy) VALUES (N'YIELD_TARGET_PCT', N'DECIMAL', N'KPI', N'99.0', N'Yield target', N'%', NULL, 50, 1, 'admin@ames.local');
INSERT INTO dbo.SYS_Config (ConfigKey, ConfigType, Category, ConfigValue, CodeName, Unit, UsedByModulesJSON, SortOrder, IsActive, CreatedBy) VALUES (N'PM_LOOKAHEAD_DAYS', N'INT', N'Maintenance', N'30', N'PM lookahead window', N'day', NULL, 20, 1, 'admin@ames.local');
INSERT INTO dbo.SYS_Config (ConfigKey, ConfigType, Category, ConfigValue, CodeName, Unit, UsedByModulesJSON, SortOrder, IsActive, CreatedBy) VALUES (N'ALERT_REPEAT_MINUTES', N'INT', N'Notifications', N'15', N'Repeat-alert interval', N'min', NULL, 60, 1, 'admin@ames.local');
INSERT INTO dbo.SYS_Config (ConfigKey, ConfigType, Category, ConfigValue, CodeName, Unit, UsedByModulesJSON, SortOrder, IsActive, CreatedBy) VALUES (N'DEFAULT_SHIFT_HOURS', N'DECIMAL', N'Operations', N'7.0', N'Net working hours per shift', N'hour', NULL, 10, 1, 'admin@ames.local');
INSERT INTO dbo.SYS_Config (ConfigKey, ConfigType, Category, ConfigValue, CodeName, Unit, UsedByModulesJSON, SortOrder, IsActive, CreatedBy) VALUES (N'PLANT_TIMEZONE', N'STRING', N'Operations', N'CT', N'Plant timezone', NULL, NULL, 120, 1, 'admin@ames.local');
INSERT INTO dbo.SYS_Config (ConfigKey, ConfigType, Category, ConfigValue, CodeName, Unit, UsedByModulesJSON, SortOrder, IsActive, CreatedBy) VALUES (N'SESSION_TIMEOUT_MIN', N'INT', N'Security', N'60', N'Session timeout', N'min', NULL, 100, 1, 'admin@ames.local');
INSERT INTO dbo.SYS_Config (ConfigKey, ConfigType, Category, ConfigValue, CodeName, Unit, UsedByModulesJSON, SortOrder, IsActive, CreatedBy) VALUES (N'PASSWORD_MIN_LEN', N'INT', N'Security', N'8', N'Minimum password length', NULL, NULL, 110, 1, 'admin@ames.local');
INSERT INTO dbo.SYS_Config (ConfigKey, ConfigType, Category, ConfigValue, CodeName, Unit, UsedByModulesJSON, SortOrder, IsActive, CreatedBy) VALUES (N'LOG_RETENTION_DAYS', N'INT', N'System', N'365', N'Audit log retention', N'day', NULL, 150, 1, 'admin@ames.local');
INSERT INTO dbo.SYS_Config (ConfigKey, ConfigType, Category, ConfigValue, CodeName, Unit, UsedByModulesJSON, SortOrder, IsActive, CreatedBy) VALUES (N'DASH_REFRESH_SEC', N'INT', N'UI', N'30', N'Dashboard refresh interval', N'sec', NULL, 70, 1, 'admin@ames.local');
INSERT INTO dbo.SYS_Config (ConfigKey, ConfigType, Category, ConfigValue, CodeName, Unit, UsedByModulesJSON, SortOrder, IsActive, CreatedBy) VALUES (N'LANGUAGE_DEFAULT', N'STRING', N'UI', N'en-US', N'Default UI language', NULL, NULL, 130, 1, 'admin@ames.local');
GO
