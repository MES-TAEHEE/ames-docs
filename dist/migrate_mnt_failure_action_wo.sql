-- ════════════════════════════════════════════════════════════════════════
--  migrate_mnt_failure_action_wo.sql
--  고장 조치 이력에 그 조치를 만든 보전 작업지시 — MNT_FailureAction.WorkOrderID (10-14)
--
--  부분 완료(PARTIAL)·후속 조치 필요(FOLLOWUP)로 작업지시를 완료하면 고장의 WorkOrderID 가 후속 작업지시로
--  옮겨져, 원래 작업지시 상세(MNT-007)에서 그 고장이 사라졌다. 조치 행에 작업지시를 남겨 두면
--  MntRepository.ListFailuresForWo 가 "현재 연결" 또는 "이 작업지시로 남긴 조치가 있는 고장" 으로 찾는다
--  (고장번호 문자열 SourceRefID 와 달리 번호를 바꿔도 끊기지 않는다).
--  PARTIAL·FOLLOWUP = 원래 작업지시, REPAIRED = 완료한 작업지시, 안돈 ARRIVED·ACK = NULL. FK 없음(원칙).
--
--  기존 행은 NULL(10-14 두 DB 모두 PARTIAL·FOLLOWUP 0건 — 되살릴 연결 없음).
--  ALTER ADD 라 감사 컬럼 뒤에 붙는다 — 적용 뒤 dist/migrate_audit_columns_last.sql 을 다시 돌린다
--  (rebuild_db.sh 는 그 스크립트가 맨 끝이라 따로 할 일 없음).
--  재실행 안전, 순서 무관.
--  적용:  sqlcmd -S <서버> -U ames_app -P <pw> -d AMES_DEV -C -f 65001 -I -b -l 30 -i dist\migrate_mnt_failure_action_wo.sql
-- ════════════════════════════════════════════════════════════════════════
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF COL_LENGTH('dbo.MNT_FailureAction', 'WorkOrderID') IS NULL
BEGIN
    ALTER TABLE dbo.MNT_FailureAction ADD WorkOrderID INT NULL;
    PRINT 'MNT_FailureAction.WorkOrderID added';
END
ELSE PRINT 'MNT_FailureAction.WorkOrderID already exists';
GO

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties
               WHERE major_id = OBJECT_ID('dbo.MNT_FailureAction') AND name = 'MS_Description'
                 AND minor_id = COLUMNPROPERTY(OBJECT_ID('dbo.MNT_FailureAction'), 'WorkOrderID', 'ColumnId'))
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'조치를 남긴 보전 작업지시 ID (MNT_WorkOrder, 안돈 도착·ACK 는 NULL) · int',
         @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'MNT_FailureAction',
         @level2type=N'COLUMN',@level2name=N'WorkOrderID';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_MNT_FailureAction_WorkOrderID' AND object_id = OBJECT_ID('dbo.MNT_FailureAction'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_MNT_FailureAction_WorkOrderID ON dbo.MNT_FailureAction (WorkOrderID) INCLUDE (FailureID);
    PRINT 'IX_MNT_FailureAction_WorkOrderID created';
END
ELSE PRINT 'IX_MNT_FailureAction_WorkOrderID already exists';
GO

SELECT c.column_id AS Ord, c.name AS ColumnName, TYPE_NAME(c.user_type_id) AS TypeName, c.is_nullable AS Nullable
FROM   sys.columns c
WHERE  c.object_id = OBJECT_ID('dbo.MNT_FailureAction')
ORDER  BY c.column_id;
GO
