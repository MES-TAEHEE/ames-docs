/* ------------------------------------------------------------------
   migrate_pp_wo_source.sql  (2026-11-16)

   작업지시(PP_WorkOrder)가 어떤 계획에서 만들어졌는지 남긴다 — 계획부터 실행(슬롯·실적)까지 한 줄로 이어 보이게 하는 기준(사용자 결정, 재무 자료로도 쓴다).
     SourceType  PLAN003 = PP-003 일괄 생성 · APS = PP-APS WO 생성 · MANUAL = PP-004 수동 발행 · NULL = 기록 없는 옛 WO
     SourceRef   PLAN003 = 그 일괄 생성 묶음 번호(PC-yyyyMMdd-HHmmss, 한 번에 만든 WO 가 같은 값) · APS = 실행 번호(PP_ApsRun.RunID) · MANUAL = NULL
   · 옛 WO 는 PP_ApsRunWo 로 이어지는 것만 APS 로 채운다(나머지는 NULL — 경로를 알 수 없다).
   · 새 컬럼이라 적용 뒤 migrate_audit_columns_last.sql 을 다시 돌린다. 재실행 안전. 적용: sqlcmd -f 65001 -I -b
   · 마이그레이션 → 신 Web·Api 순서(신 코드가 이 컬럼에 쓴다; 구 코드는 컬럼을 몰라 그대로 동작).
   ------------------------------------------------------------------ */
SET NOCOUNT ON;
GO
IF COL_LENGTH('dbo.PP_WorkOrder', 'SourceType') IS NULL
    ALTER TABLE dbo.PP_WorkOrder ADD SourceType varchar(10) COLLATE DATABASE_DEFAULT NULL;
IF COL_LENGTH('dbo.PP_WorkOrder', 'SourceRef') IS NULL
    ALTER TABLE dbo.PP_WorkOrder ADD SourceRef varchar(30) COLLATE DATABASE_DEFAULT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID('dbo.PP_WorkOrder') AND name = N'MS_Description'
               AND minor_id = COLUMNPROPERTY(OBJECT_ID('dbo.PP_WorkOrder'), 'SourceType', 'ColumnId'))
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'생성 경로 PLAN003/APS/MANUAL · varchar(10)',
         @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'PP_WorkOrder', @level2type = N'COLUMN', @level2name = N'SourceType';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID('dbo.PP_WorkOrder') AND name = N'MS_Description'
               AND minor_id = COLUMNPROPERTY(OBJECT_ID('dbo.PP_WorkOrder'), 'SourceRef', 'ColumnId'))
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'생성 근거 번호(PP-003 묶음 번호 · APS 실행 번호) · varchar(30)',
         @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'PP_WorkOrder', @level2type = N'COLUMN', @level2name = N'SourceRef';
GO
-- 옛 WO 백필: APS 실행으로 만든 것만 안다(가장 먼저 만든 실행)
UPDATE w SET SourceType = 'APS', SourceRef = CAST(x.RunID AS varchar(30))
FROM dbo.PP_WorkOrder w
JOIN (SELECT WoID, MIN(RunID) AS RunID FROM dbo.PP_ApsRunWo WHERE WoID IS NOT NULL GROUP BY WoID) x ON x.WoID = w.WoID
WHERE w.SourceType IS NULL;
PRINT CONCAT('APS 로 백필: ', @@ROWCOUNT, '건');
GO
SELECT ISNULL(SourceType, '(NULL)') AS SourceType, COUNT(*) AS WoCount FROM dbo.PP_WorkOrder GROUP BY SourceType;
GO
