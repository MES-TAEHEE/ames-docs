-- ════════════════════════════════════════════════════════════════════════
--  seed_andon_dev.sql
--  MD_LineSupervisor 개발 시드 — 모든 활성 라인의 슈퍼바이저 = W001 (seed_md_worker_dev.sql)
--
--  운영 금지. 운영은 Web 등록 화면(개발 예정)에서 라인별로 넣는다.
--  전제: migrate_andon_workflow.sql 적용 후. 재실행 안전(라인·사번 기준 UPSERT).
--  적용:  sqlcmd -S 192.168.1.100,1433 -U ames_app -P !Dev2026 -d AMES_DEV -f 65001 -i dist/seed_andon_dev.sql
-- ════════════════════════════════════════════════════════════════════════
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF OBJECT_ID('dbo.MD_LineSupervisor', 'U') IS NULL
BEGIN
    RAISERROR('MD_LineSupervisor 가 없습니다. dist/migrate_andon_workflow.sql 을 먼저 적용하세요.', 16, 1);
    RETURN;
END
GO

MERGE dbo.MD_LineSupervisor AS tgt
USING (SELECT LineID, 'W001' AS WorkerNo FROM dbo.MD_Line WHERE COALESCE(Status, 'ACTIVE') <> 'INACTIVE') AS src
ON tgt.LineID = src.LineID AND tgt.WorkerNo = src.WorkerNo
WHEN MATCHED AND tgt.ActiveFlag = 0 THEN
    UPDATE SET ActiveFlag = 1, ModifiedBy = 'seed', ModifiedTS = SYSDATETIME()
WHEN NOT MATCHED THEN
    INSERT (LineID, WorkerNo, ActiveFlag, CreatedBy, CreatedTS)
    VALUES (src.LineID, src.WorkerNo, 1, 'seed', SYSDATETIME());
GO

SELECT LineID, WorkerNo, ActiveFlag FROM dbo.MD_LineSupervisor ORDER BY LineID, WorkerNo;
GO
