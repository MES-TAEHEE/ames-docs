-- ════════════════════════════════════════════════════════════════════════
--  migrate_day_cutoff_config.sql
--  전기일 기준 시각을 공통코드 DAY_CUTOFF/TIME → SYS_Config.DAY_CUTOFF_TIME 으로 이관
--
--  SYS-009 시스템 설정(Operations)에서 'HH:mm' 으로 관리한다(ConfigType 'TIME').
--  값은 기존 공통코드 Attribute1 을 'HH:mm' 으로 정규화해 옮기고, 없거나 읽을 수 없으면 07:00.
--  이관 후 공통코드 그룹 DAY_CUTOFF 와 그 항목을 지운다.
--  읽는 곳: AMES.Data.Services.ProdCalendar.ResolveNow(실적 ProdDate 판정) — 행이 없으면 자정 기준.
--
--  배포 순서: ① 이 마이그레이션 ② AMES.Web·AMES.Pop·AMES.Api 신버전.
--  구 바이너리는 공통코드를 읽으므로 그 사이 실적 ProdDate 는 자정 기준으로 저장된다(ProdDate 를 읽는 화면은 없다).
--  순서 무관하게 재실행 안전.
--  적용:  sqlcmd -S 192.168.1.100,1433 -U ames_app -P !Dev2026 -d AMES_DEV -f 65001 -I -b -i dist/migrate_day_cutoff_config.sql
-- ════════════════════════════════════════════════════════════════════════
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

BEGIN TRANSACTION;

IF NOT EXISTS (SELECT 1 FROM dbo.SYS_Config WHERE ConfigKey = 'DAY_CUTOFF_TIME')
BEGIN
    DECLARE @old nvarchar(200) =
        (SELECT TOP 1 LTRIM(RTRIM(Attribute1)) FROM dbo.MD_CodeItem
         WHERE GroupCode = 'DAY_CUTOFF' AND CodeValue = 'TIME' AND ISNULL(UseFlag, 1) = 1);
    -- 'H:mm'·'HH:mm'·'HHMM' → 'HH:mm', 그 밖은 07:00
    DECLARE @t time = TRY_CONVERT(time,
        CASE WHEN @old LIKE '[0-9][0-9][0-9][0-9]' THEN STUFF(@old, 3, 0, ':') ELSE @old END);
    DECLARE @val nvarchar(5) = COALESCE(CONVERT(char(5), @t, 108), N'07:00');

    INSERT INTO dbo.SYS_Config
        (ConfigKey, ConfigType, Category, ConfigValue, CodeName, Unit, UsedByModulesJSON, SortOrder, IsActive, CreatedBy)
    VALUES
        ('DAY_CUTOFF_TIME', 'TIME', 'Operations', @val,
         N'Production day cutoff — results before this time belong to the previous day', NULL, NULL, 15, 1, 'migration');
    PRINT CONCAT('SYS_Config DAY_CUTOFF_TIME inserted: ', @val);
END
ELSE
    PRINT 'SYS_Config DAY_CUTOFF_TIME already exists';

DELETE dbo.MD_CodeItem  WHERE GroupCode = 'DAY_CUTOFF';
PRINT CONCAT('MD_CodeItem DAY_CUTOFF deleted: ', @@ROWCOUNT);
DELETE dbo.MD_CodeGroup WHERE GroupCode = 'DAY_CUTOFF';
PRINT CONCAT('MD_CodeGroup DAY_CUTOFF deleted: ', @@ROWCOUNT);

COMMIT TRANSACTION;
GO

SELECT ConfigID, ConfigKey, ConfigType, Category, ConfigValue, CodeName, SortOrder, IsActive
FROM   dbo.SYS_Config WHERE ConfigKey = 'DAY_CUTOFF_TIME';
SELECT COUNT(*) AS RemainingDayCutoffCodes FROM dbo.MD_CodeItem WHERE GroupCode = 'DAY_CUTOFF';
GO
