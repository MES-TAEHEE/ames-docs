-- ════════════════════════════════════════════════════════════════════════
--  migrate_mnt_pm_lead_days.sql
--  PM 「도래」·「예정」 기준 일수 SYS_Config.MNT_PM_LEAD_DAYS (INT, 기본 7) — 10-16 사용자 결정
--
--  PM 일정 상태는 저장값이 아니라 예정일로 계산한다(AMES.Data.Services.PmRules): 지났으면 지연, 오늘 + N일 이내면 도래.
--  같은 N 으로 PM 작업지시의 「예정」을 가른다 — 착수 전이고 그 PM 의 예정일이 오늘 + N일보다 뒤면
--  MNT-007 기본 목록·KPI, MNT-009 열린 작업지시, MNT-001 설비의 열린 작업지시 수에서 뺀다.
--  행이 없거나 값이 0~365 정수가 아니면 7 로 본다 — 이 마이그레이션은 SYS-009(Maintenance)에서 고칠 수 있게 행만 만든다.
--  순서 무관, 재실행 안전(이미 있으면 값을 건드리지 않는다).
--  적용:  sqlcmd -S <서버> -U ames_app -P <pw> -d AMES_DEV -C -f 65001 -I -b -i dist\migrate_mnt_pm_lead_days.sql
-- ════════════════════════════════════════════════════════════════════════
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.SYS_Config WHERE ConfigKey = 'MNT_PM_LEAD_DAYS')
BEGIN
    INSERT INTO dbo.SYS_Config
        (ConfigKey, ConfigType, Category, ConfigValue, CodeName, Unit, UsedByModulesJSON, SortOrder, IsActive, CreatedBy)
    VALUES
        ('MNT_PM_LEAD_DAYS', 'INT', 'Maintenance', N'7',
         N'PM due window — days before the due date (MNT-005/007/009/010)', 'day', NULL, 30, 1, 'migration');
    PRINT 'SYS_Config MNT_PM_LEAD_DAYS inserted (7)';
END
ELSE
    PRINT 'SYS_Config MNT_PM_LEAD_DAYS already exists';
GO

SELECT ConfigID, ConfigKey, ConfigType, Category, ConfigValue, Unit, SortOrder
FROM   dbo.SYS_Config WHERE ConfigKey = 'MNT_PM_LEAD_DAYS';
GO
