-- ════════════════════════════════════════════════════════════════════════
--  migrate_mnt_failure_downtime.sql
--  고장 ↔ 라인 비가동 연결 (10-16 사용자 결정)
--
--  ① PP_LineDowntimeLog.FailureID — 그 비가동을 연 고장(MNT_FailureRegister). 웹 등록 고장의 「라인 정지」가
--     비가동을 새로 만들면 채운다. 고장 → 비가동 연결 자체는 기존 MNT_FailureRegister.DowntimeID(안돈이 이미 채움)이고,
--     이 컬럼은 "그 고장이 만든 행"인지 가리는 소유 표시다 — 고장 해결·종료 때 닫고, 고장 삭제 때 지우고,
--     PP-DTL 에서는 안돈 행처럼 원인·비고만 고친다. 안돈 행·PP-DTL 웹 행은 NULL. FK 없음(원칙).
--  ② MNT_WorkOrder.DowntimeID 삭제 — 쓰는 코드·값 없음(10-16 두 DB 모두 0건). 작업지시 → 고장 → 비가동 순으로 따라간다.
--
--  ALTER ADD 라 감사 컬럼 뒤에 붙는다 — 적용 뒤 dist/migrate_audit_columns_last.sql 을 다시 돌린다
--  (rebuild_db.sh 는 그 스크립트가 맨 끝이라 따로 할 일 없음).
--  재실행 안전, 순서 무관. 신 Web 과 함께 적용한다(구 Web 은 두 컬럼을 읽지 않아 먼저 적용해도 동작한다).
--  적용:  sqlcmd -S <서버> -U ames_app -P <pw> -d AMES_DEV -C -f 65001 -I -b -l 30 -i dist\migrate_mnt_failure_downtime.sql
-- ════════════════════════════════════════════════════════════════════════
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF COL_LENGTH('dbo.PP_LineDowntimeLog', 'FailureID') IS NULL
BEGIN
    ALTER TABLE dbo.PP_LineDowntimeLog ADD FailureID INT NULL;
    PRINT 'PP_LineDowntimeLog.FailureID added';
END
ELSE PRINT 'PP_LineDowntimeLog.FailureID already exists';
GO

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties
               WHERE major_id = OBJECT_ID('dbo.PP_LineDowntimeLog') AND name = 'MS_Description'
                 AND minor_id = COLUMNPROPERTY(OBJECT_ID('dbo.PP_LineDowntimeLog'), 'FailureID', 'ColumnId'))
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'이 비가동을 연 고장 ID (MNT_FailureRegister — 웹 고장의 라인 정지, 안돈·PP-DTL 행은 NULL) · int',
         @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'PP_LineDowntimeLog',
         @level2type=N'COLUMN',@level2name=N'FailureID';
GO

IF COL_LENGTH('dbo.MNT_WorkOrder', 'DowntimeID') IS NOT NULL
BEGIN
    IF EXISTS (SELECT 1 FROM dbo.MNT_WorkOrder WHERE DowntimeID IS NOT NULL)
        THROW 51000, 'MNT_WorkOrder.DowntimeID has values - check before dropping.', 1;
    IF EXISTS (SELECT 1 FROM sys.extended_properties
               WHERE major_id = OBJECT_ID('dbo.MNT_WorkOrder') AND name = 'MS_Description'
                 AND minor_id = COLUMNPROPERTY(OBJECT_ID('dbo.MNT_WorkOrder'), 'DowntimeID', 'ColumnId'))
        EXEC sys.sp_dropextendedproperty @name=N'MS_Description',
             @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'MNT_WorkOrder',
             @level2type=N'COLUMN',@level2name=N'DowntimeID';
    ALTER TABLE dbo.MNT_WorkOrder DROP COLUMN DowntimeID;
    PRINT 'MNT_WorkOrder.DowntimeID dropped';
END
ELSE PRINT 'MNT_WorkOrder.DowntimeID already dropped';
GO

SELECT c.column_id AS Ord, c.name AS ColumnName, TYPE_NAME(c.user_type_id) AS TypeName, c.is_nullable AS Nullable
FROM   sys.columns c
WHERE  c.object_id = OBJECT_ID('dbo.PP_LineDowntimeLog')
ORDER  BY c.column_id;
GO
