-- ════════════════════════════════════════════════════════════════════════
--  migrate_mnt_pm_execution_class.sql
--  MNT_PMExecution(PM 실행 이력)에 화면 표시용 스냅샷 컬럼 추가
--
--  · 스냅샷 컬럼은 PMScheduleID 다음에 MNT_PMSchedule 과 같은 순서로 둔다: PMPlanNumber → EquipID → PMClass → PMType
--    PMClass VARCHAR(10) 은 MNT_PMSchedule.PMClass 와 같은 형(공통코드 PM_CLASS: EQUIP 설비 / MAINT 보전)이며
--    MNT-005(EQUIP)·MNT-010(MAINT) 달력이 이 값으로 완료 이력을 나눠 보인다. 넷 다 완료 시점의 일정 스냅샷(일정이 삭제·수정돼도 이력은 남는다)
--  · DueDate DATE          — 그 실행이 이행한 예정일(지연 여부 표시용)
--  · LaborMinutes INT      — 소요 시간. 활성 작업지시 없이 완료한 PM 은 여기에만 남는다
--  · 처음 적용할 때 기존 행은 MNT_PMSchedule·MNT_WorkOrder 로 백필(DueDate 는 알 수 없어 NULL)
--  · 조회 인덱스 IX_MNT_PMExecution_Class_Completed (PMClass, CompletedAt)
--  · 컬럼 순서를 맞추기 위해 테이블을 재생성한다(참조 FK 없음, IDENTITY 보존). 재실행 안전 — 컬럼이 없거나
--    순서가 다르면(초판은 PMClass 를 PMScheduleID 바로 다음에 뒀다) 기존 값을 그대로 옮기며 다시 재생성한다.
--
--  적용:  sqlcmd -S 192.168.1.100 -U ames_app -P !Dev2026 -d AMES_DEV -C -f 65001 -I -b -i dist/migrate_mnt_pm_execution_class.sql
-- ════════════════════════════════════════════════════════════════════════
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

-- ── 1) 스냅샷 컬럼 (PMScheduleID 다음, MNT_PMSchedule 순서) ─────────────────
IF COL_LENGTH('dbo.MNT_PMExecution', 'PMClass') IS NULL
   OR COLUMNPROPERTY(OBJECT_ID('dbo.MNT_PMExecution'), 'PMPlanNumber', 'ColumnId') <> 3
   OR COLUMNPROPERTY(OBJECT_ID('dbo.MNT_PMExecution'), 'PMClass',      'ColumnId') <> 5
BEGIN
    BEGIN TRANSACTION;

    CREATE TABLE dbo.MNT_PMExecution_new (
      [PMExecutionID]             INT IDENTITY         NOT NULL,
      [PMScheduleID]              INT                      NULL,  -- FK -> MNT_PMSchedule.PMScheduleID
      [PMPlanNumber]              VARCHAR(30)              NULL,  -- 일정 스냅샷 (이하 MNT_PMSchedule 과 같은 순서)
      [EquipID]                   VARCHAR(20)              NULL,  -- 일정 스냅샷 (FK -> MD_Equipment.EquipID)
      [PMClass]                   VARCHAR(10)              NULL,  -- 공통코드 PM_CLASS (EQUIP / MAINT) — 일정 스냅샷
      [PMType]                    VARCHAR(60)              NULL,  -- 공통코드 PM_TYPE — 일정 스냅샷
      [DueDate]                   DATE                     NULL,  -- 이행한 예정일
      [WorkOrderID]               INT                      NULL,  -- FK -> MNT_WorkOrder.WorkOrderID
      [CompletedAt]               DATETIME2                NULL,
      [LaborMinutes]              INT                      NULL,
      [TechnicianID]              NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
      [Result]                    VARCHAR(15)              NULL,  -- 공통코드 MWO_RESULT
      [ResultNote]                NVARCHAR(500)            NULL,
      [ChecklistResultsJSON]      NVARCHAR(MAX)            NULL,
      [CreatedBy]                 VARCHAR(50)          NOT NULL,
      [CreatedTS]                 DATETIME2                NULL CONSTRAINT DF_MNT_PMExecution_CreatedTS_new DEFAULT SYSDATETIME(),
      [ModifiedBy]                NVARCHAR(450)            NULL,
      [ModifiedTS]                DATETIME2                NULL,
      CONSTRAINT PK_MNT_PMExecution_new PRIMARY KEY CLUSTERED ([PMExecutionID])
    );

    -- 초판(PMClass 가 이미 있음)에서 재정렬할 때는 기존 스냅샷 값을 그대로 옮기고, 처음 적용할 때는 일정·작업지시로 백필한다
    DECLARE @hasSnap bit = CASE WHEN COL_LENGTH('dbo.MNT_PMExecution', 'PMClass') IS NULL THEN 0 ELSE 1 END;
    DECLARE @sql nvarchar(max) = N'
    SET IDENTITY_INSERT dbo.MNT_PMExecution_new ON;
    INSERT INTO dbo.MNT_PMExecution_new
        (PMExecutionID, PMScheduleID, PMPlanNumber, EquipID, PMClass, PMType, DueDate, WorkOrderID, CompletedAt, LaborMinutes,
         TechnicianID, Result, ResultNote, ChecklistResultsJSON, CreatedBy, CreatedTS, ModifiedBy, ModifiedTS)
    SELECT e.PMExecutionID, e.PMScheduleID, ' + CASE WHEN @hasSnap = 1
        THEN N'e.PMPlanNumber, e.EquipID, e.PMClass, e.PMType, e.DueDate, e.WorkOrderID, e.CompletedAt, e.LaborMinutes,'
        ELSE N's.PMPlanNumber, s.EquipID, s.PMClass, s.PMType, NULL, e.WorkOrderID, e.CompletedAt, w.LaborMinutes,' END + N'
           e.TechnicianID, e.Result, e.ResultNote, e.ChecklistResultsJSON, e.CreatedBy, e.CreatedTS, e.ModifiedBy, e.ModifiedTS
    FROM   dbo.MNT_PMExecution e
    LEFT JOIN dbo.MNT_PMSchedule s ON s.PMScheduleID = e.PMScheduleID
    LEFT JOIN dbo.MNT_WorkOrder  w ON w.WorkOrderID  = e.WorkOrderID;
    SET IDENTITY_INSERT dbo.MNT_PMExecution_new OFF;';
    EXEC sp_executesql @sql;

    DROP TABLE dbo.MNT_PMExecution;
    EXEC sp_rename N'dbo.MNT_PMExecution_new', N'MNT_PMExecution', N'OBJECT';
    EXEC sp_rename N'dbo.PK_MNT_PMExecution_new', N'PK_MNT_PMExecution', N'OBJECT';
    EXEC sp_rename N'dbo.DF_MNT_PMExecution_CreatedTS_new', N'DF_MNT_PMExecution_CreatedTS', N'OBJECT';

    COMMIT TRANSACTION;
    PRINT CASE WHEN @hasSnap = 1 THEN 'MNT_PMExecution rebuilt: snapshot columns reordered to MNT_PMSchedule order (PMPlanNumber, EquipID, PMClass, PMType)'
               ELSE 'MNT_PMExecution rebuilt: PMPlanNumber, EquipID, PMClass, PMType, DueDate, LaborMinutes added (backfilled from schedule / work order)' END;
END
ELSE
    PRINT 'MNT_PMExecution snapshot columns already in place';
GO

-- ── 2) 달력 조회 인덱스 ──────────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_MNT_PMExecution_Class_Completed' AND object_id = OBJECT_ID('dbo.MNT_PMExecution'))
BEGIN
    CREATE INDEX IX_MNT_PMExecution_Class_Completed ON dbo.MNT_PMExecution (PMClass, CompletedAt) INCLUDE (PMScheduleID, EquipID);
    PRINT 'IX_MNT_PMExecution_Class_Completed created';
END
ELSE
    PRINT 'IX_MNT_PMExecution_Class_Completed already exists';
GO

-- ── 확인 ─────────────────────────────────────────────────────────────────
SELECT c.column_id, c.name, t.name AS type_name, c.max_length
FROM   sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id
WHERE  c.object_id = OBJECT_ID('dbo.MNT_PMExecution')
ORDER  BY c.column_id;
SELECT COUNT(*) AS Executions, SUM(CASE WHEN PMClass IS NULL THEN 1 ELSE 0 END) AS NullClass FROM dbo.MNT_PMExecution;
GO
