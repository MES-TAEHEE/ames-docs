-- ════════════════════════════════════════════════════════════════════════
--  migrate_employee_no_rename.sql
--  MD_Worker.WorkerNo · MD_LineSupervisor.WorkerNo → EmployeeNo
--
--  사번 컬럼 이름을 SYS_UserProfile.EmployeeNo 와 같게 맞춘다(형은 원래부터 VARCHAR(20) 으로 동일).
--  POP 로그인·안돈 슈퍼바이저 검증·MD-032/033 화면이 이 이름을 쓴다.
--  · MD_Worker: WorkerNo → EmployeeNo, WorkerName → EmployeeName(NVARCHAR(50), SYS_UserProfile.EmployeeName 과 동일) + 고유 인덱스 UQ_MD_Worker_WorkerNo → UQ_MD_Worker_EmployeeNo
--  · MD_LineSupervisor: 컬럼 rename (PK (LineID, EmployeeNo) 는 sp_rename 으로 그대로 유지)
--  데이터 변경 없음. 순서 무관(migrate_md_worker.sql · migrate_andon_workflow.sql 뒤), 재실행 안전.
--
--  적용:  sqlcmd -S 192.168.1.100 -U ames_app -P !Dev2026 -d AMES_DEV -C -f 65001 -I -b -i dist/migrate_employee_no_rename.sql
-- ════════════════════════════════════════════════════════════════════════
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

-- ── 1) MD_Worker ─────────────────────────────────────────────────────────
IF COL_LENGTH('dbo.MD_Worker', 'WorkerNo') IS NOT NULL AND COL_LENGTH('dbo.MD_Worker', 'EmployeeNo') IS NULL
BEGIN
    EXEC sp_rename N'dbo.MD_Worker.WorkerNo', N'EmployeeNo', N'COLUMN';
    PRINT 'MD_Worker.WorkerNo -> EmployeeNo';
END
ELSE
    PRINT 'MD_Worker.EmployeeNo already in place';

IF COL_LENGTH('dbo.MD_Worker', 'WorkerName') IS NOT NULL AND COL_LENGTH('dbo.MD_Worker', 'EmployeeName') IS NULL
BEGIN
    EXEC sp_rename N'dbo.MD_Worker.WorkerName', N'EmployeeName', N'COLUMN';
    PRINT 'MD_Worker.WorkerName -> EmployeeName';
END
ELSE
    PRINT 'MD_Worker.EmployeeName already in place';

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_MD_Worker_WorkerNo' AND object_id = OBJECT_ID('dbo.MD_Worker'))
BEGIN
    EXEC sp_rename N'dbo.MD_Worker.UQ_MD_Worker_WorkerNo', N'UQ_MD_Worker_EmployeeNo', N'INDEX';
    PRINT 'UQ_MD_Worker_WorkerNo -> UQ_MD_Worker_EmployeeNo';
END
ELSE IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_MD_Worker_EmployeeNo' AND object_id = OBJECT_ID('dbo.MD_Worker'))
BEGIN
    CREATE UNIQUE INDEX UQ_MD_Worker_EmployeeNo ON dbo.MD_Worker ([EmployeeNo]);
    PRINT 'UQ_MD_Worker_EmployeeNo created';
END
ELSE
    PRINT 'UQ_MD_Worker_EmployeeNo already exists';
GO

-- ── 2) MD_LineSupervisor ─────────────────────────────────────────────────
IF OBJECT_ID('dbo.MD_LineSupervisor', 'U') IS NULL
    RAISERROR('MD_LineSupervisor 가 없습니다. dist/migrate_andon_workflow.sql 을 먼저 적용하세요.', 16, 1);
ELSE IF COL_LENGTH('dbo.MD_LineSupervisor', 'WorkerNo') IS NOT NULL AND COL_LENGTH('dbo.MD_LineSupervisor', 'EmployeeNo') IS NULL
BEGIN
    EXEC sp_rename N'dbo.MD_LineSupervisor.WorkerNo', N'EmployeeNo', N'COLUMN';
    PRINT 'MD_LineSupervisor.WorkerNo -> EmployeeNo';
END
ELSE
    PRINT 'MD_LineSupervisor.EmployeeNo already in place';
GO

-- ── 확인 ─────────────────────────────────────────────────────────────────
SELECT 'MD_Worker' AS Tbl, c.column_id, c.name FROM sys.columns c WHERE c.object_id = OBJECT_ID('dbo.MD_Worker') AND c.name IN ('WorkerNo', 'EmployeeNo', 'WorkerName', 'EmployeeName')
UNION ALL
SELECT 'MD_LineSupervisor', c.column_id, c.name FROM sys.columns c WHERE c.object_id = OBJECT_ID('dbo.MD_LineSupervisor') AND c.name IN ('WorkerNo', 'EmployeeNo');
SELECT name AS IndexName FROM sys.indexes WHERE object_id IN (OBJECT_ID('dbo.MD_Worker'), OBJECT_ID('dbo.MD_LineSupervisor')) AND name IS NOT NULL ORDER BY name;
GO
