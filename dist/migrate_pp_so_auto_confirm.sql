-- ════════════════════════════════════════════════════════════════════════
--  migrate_pp_so_auto_confirm.sql
--  PP-002 수주 자동 확정 플래그 SYS_Config.PP_SO_AUTO_CONFIRM (bool 'true'/'false', 기본 false)
--
--  켜져 있으면 새로 들어오는 수주(PP-002 엑셀 업로드·API 가져오기·AMES.Api PO Sync 워커 — 모두
--  PpRepository.UpsertCustomerOrders)가 Open 을 거치지 않고 저장 시점에 Confirmed 가 된다.
--  토글은 PP-002 화면(승인 권한)에서 켜고 끄며, 켤 때 그 시점의 Open 수주 전부를 같은 트랜잭션으로 확정한다.
--  SYS-009 시스템 설정(PP)에도 보인다. 읽는 곳: AMES.Data.Services.SoAutoConfirm — 행이 없거나 비활성이면 꺼짐.
--
--  이 마이그레이션은 기본 행만 만든다. 화면에서 켜면 행이 없어도 만들어 쓰므로 없어도 동작하지만,
--  SYS-009 목록에 처음부터 보이게 하려면 적용한다. 순서 무관, 재실행 안전.
--  적용:  sqlcmd -S 192.168.0.132,1433 -U ames_app -P !Dev2026 -d AMES_DEV -f 65001 -I -b -i dist/migrate_pp_so_auto_confirm.sql
-- ════════════════════════════════════════════════════════════════════════
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.SYS_Config WHERE ConfigKey = 'PP_SO_AUTO_CONFIRM')
BEGIN
    INSERT INTO dbo.SYS_Config
        (ConfigKey, ConfigType, Category, ConfigValue, CodeName, Unit, UsedByModulesJSON, SortOrder, IsActive, CreatedBy)
    VALUES
        ('PP_SO_AUTO_CONFIRM', 'bool', 'PP', N'false',
         N'Auto-confirm imported customer orders (PP-002)', NULL, NULL, 20, 1, 'migration');
    PRINT 'SYS_Config PP_SO_AUTO_CONFIRM inserted (false)';
END
ELSE
    PRINT 'SYS_Config PP_SO_AUTO_CONFIRM already exists';
GO

SELECT ConfigID, ConfigKey, ConfigType, Category, ConfigValue, IsActive
FROM   dbo.SYS_Config WHERE ConfigKey = 'PP_SO_AUTO_CONFIRM';
GO
