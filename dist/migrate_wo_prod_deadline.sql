-- ════════════════════════════════════════════════════════════════════════
--  migrate_wo_prod_deadline.sql
--  PP_WorkOrder.ProdDeadline + SYS_Config PP_PROD_BUFFER_WORKDAYS
--
--  PP-003 이 WO 를 만들 때 "생산 마감일 = 납기 − 버퍼 근무일" 을 계산해 박아 둔다.
--  설정·달력을 나중에 바꿔도 이미 발행된 WO 의 마감일이 흔들리지 않도록 스냅샷으로
--  저장한다(IMG 라벨의 CustomerCode 와 같은 이유). 버퍼 일수는 SYS_Config 에서 읽는다.
--
--  컬럼 추가 + 설정 행 시드뿐 — 적용 순서 무관, 재실행 안전(멱등).
--  적용:  sqlcmd -S 98.95.142.192,1433 -U ames_app -P !Dev2026 -d AMES_DEV -f 65001 -b -i dist/migrate_wo_prod_deadline.sql
-- ════════════════════════════════════════════════════════════════════════
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF COL_LENGTH('dbo.PP_WorkOrder', 'ProdDeadline') IS NULL
    ALTER TABLE dbo.PP_WorkOrder ADD ProdDeadline DATE NULL;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.SYS_Config WHERE ConfigKey = 'PP_PROD_BUFFER_WORKDAYS')
    INSERT INTO dbo.SYS_Config (ConfigKey, ConfigType, Category, ConfigValue, CodeName, Unit, UsedByModulesJSON, SortOrder, IsActive, CreatedBy)
    VALUES (N'PP_PROD_BUFFER_WORKDAYS', N'INT', N'PP', N'3', N'Production deadline buffer (workdays before due)', N'day', NULL, 10, 1, 'admin@ames.local');
GO

PRINT 'migrate_wo_prod_deadline: done';
GO
