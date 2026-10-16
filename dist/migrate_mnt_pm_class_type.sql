-- ════════════════════════════════════════════════════════════════════════
--  migrate_mnt_pm_class_type.sql
--  MNT_PMSchedule.PMClass 컬럼 추가 + PMType 값 정리 + 공통코드 PM_CLASS · PM_TYPE
--
--  · PMClass VARCHAR(10) NULL — PMType 바로 앞. 값은 공통코드 PM_CLASS(EQUIP 설비 / MAINT 보전)
--  · PMType 은 공통코드 PM_TYPE(DAILY/WEEKLY/MONTHLY/QUARTERLY/ANNUALLY) 를 따르며,
--    기존 데이터의 ANNUAL 은 ANNUALLY 로 바꾼다
--  · 컬럼 순서를 맞추기 위해 테이블을 재생성한다(참조 FK 없음, IDENTITY 보존). 재실행 안전.
--
--  적용:  sqlcmd -S 192.168.1.100,1433 -U ames_app -P !Dev2026 -d AMES_DEV -f 65001 -i dist/migrate_mnt_pm_class_type.sql
-- ════════════════════════════════════════════════════════════════════════
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

-- ── 1) PMClass 컬럼 (PMType 앞) ───────────────────────────────────────────
IF COL_LENGTH('dbo.MNT_PMSchedule', 'PMClass') IS NULL
BEGIN
    BEGIN TRANSACTION;

    CREATE TABLE dbo.MNT_PMSchedule_new (
      [PMScheduleID]              INT IDENTITY         NOT NULL,
      [PMPlanNumber]              VARCHAR(30)              NULL,
      [EquipID]                   VARCHAR(20)              NULL,
      [PMClass]                   VARCHAR(10)              NULL,
      [PMType]                    VARCHAR(60)              NULL,
      [CycleBasis]                VARCHAR(10)              NULL,
      [CycleValue]                INT                      NULL,
      [LastPMDate]                DATE                     NULL,
      [NextDueDate]               DATE                     NULL,
      [ChecklistID]               VARCHAR(20)              NULL,
      [AssignedTechID]            NVARCHAR(450)            NULL,
      [Status]                    VARCHAR(10)              NULL,
      [ActiveWoID]                INT                      NULL,
      [CreatedBy]                 VARCHAR(50)          NOT NULL,
      [CreatedTS]                 DATETIME2                NULL CONSTRAINT DF_MNT_PMSchedule_CreatedTS DEFAULT SYSDATETIME(),
      [ModifiedBy]                NVARCHAR(450)            NULL,
      [ModifiedTS]                DATETIME2                NULL,
      CONSTRAINT PK_MNT_PMSchedule_new PRIMARY KEY CLUSTERED ([PMScheduleID])
    );

    SET IDENTITY_INSERT dbo.MNT_PMSchedule_new ON;
    INSERT INTO dbo.MNT_PMSchedule_new
        (PMScheduleID, PMPlanNumber, EquipID, PMType, CycleBasis, CycleValue, LastPMDate, NextDueDate,
         ChecklistID, AssignedTechID, Status, ActiveWoID, CreatedBy, CreatedTS, ModifiedBy, ModifiedTS)
    SELECT PMScheduleID, PMPlanNumber, EquipID, PMType, CycleBasis, CycleValue, LastPMDate, NextDueDate,
           ChecklistID, AssignedTechID, Status, ActiveWoID, CreatedBy, CreatedTS, ModifiedBy, ModifiedTS
    FROM   dbo.MNT_PMSchedule;
    SET IDENTITY_INSERT dbo.MNT_PMSchedule_new OFF;

    DROP TABLE dbo.MNT_PMSchedule;
    EXEC sp_rename N'dbo.MNT_PMSchedule_new', N'MNT_PMSchedule', N'OBJECT';
    EXEC sp_rename N'dbo.PK_MNT_PMSchedule_new', N'PK_MNT_PMSchedule', N'OBJECT';

    COMMIT TRANSACTION;
    PRINT 'MNT_PMSchedule.PMClass added (table rebuilt, column before PMType)';
END
ELSE
    PRINT 'MNT_PMSchedule.PMClass already exists';
GO

-- ── 2) PMType 값 정리: ANNUAL → ANNUALLY ─────────────────────────────────
UPDATE dbo.MNT_PMSchedule SET PMType = 'ANNUALLY' WHERE PMType = 'ANNUAL';
PRINT CONCAT('PMType ANNUAL -> ANNUALLY: ', @@ROWCOUNT, ' rows');
GO

-- ── 3) 공통코드 PM_CLASS · PM_TYPE ────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM dbo.MD_CodeGroup WHERE GroupCode = 'PM_CLASS')
    INSERT INTO dbo.MD_CodeGroup (GroupCode, GroupName, GroupNameEn, Description, UseFlag, CreatedBy)
    VALUES ('PM_CLASS', N'PM 분류', N'PM class', N'MNT_PMSchedule.PMClass', 1, 'admin@ames.local');
IF NOT EXISTS (SELECT 1 FROM dbo.MD_CodeGroup WHERE GroupCode = 'PM_TYPE')
    INSERT INTO dbo.MD_CodeGroup (GroupCode, GroupName, GroupNameEn, Description, UseFlag, CreatedBy)
    VALUES ('PM_TYPE', N'PM 유형', N'PM type', N'MNT_PMSchedule.PMType', 1, 'admin@ames.local');

MERGE dbo.MD_CodeItem AS tgt
USING (VALUES
  ('PM_CLASS_EQUIP',     'PM_CLASS', 'EQUIP',     N'설비', N'Equipment',   10),
  ('PM_CLASS_MAINT',     'PM_CLASS', 'MAINT',     N'보전', N'Maintenance', 20),
  ('PM_TYPE_DAILY',      'PM_TYPE',  'DAILY',     N'일간', N'Daily',       10),
  ('PM_TYPE_WEEKLY',     'PM_TYPE',  'WEEKLY',    N'주간', N'Weekly',      20),
  ('PM_TYPE_MONTHLY',    'PM_TYPE',  'MONTHLY',   N'월간', N'Monthly',     30),
  ('PM_TYPE_QUARTERLY',  'PM_TYPE',  'QUARTERLY', N'분기', N'Quarterly',   40),
  ('PM_TYPE_ANNUALLY',   'PM_TYPE',  'ANNUALLY',  N'연간', N'Annually',    50)
) AS src (CodeID, GroupCode, CodeValue, CodeName, CodeNameEn, SortOrder)
ON tgt.CodeID = src.CodeID
WHEN NOT MATCHED THEN
    INSERT (CodeID, GroupCode, CodeValue, CodeName, CodeNameEn, ParentCodeID, SortOrder, Attribute1, UseFlag, Description, CreatedBy)
    VALUES (src.CodeID, src.GroupCode, src.CodeValue, src.CodeName, src.CodeNameEn, NULL, src.SortOrder, NULL, 1, NULL, 'admin@ames.local');
PRINT CONCAT('MD_CodeItem PM_CLASS/PM_TYPE inserted: ', @@ROWCOUNT, ' rows');
GO

SELECT c.column_id, c.name, t.name AS type_name, c.max_length
FROM   sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id
WHERE  c.object_id = OBJECT_ID('dbo.MNT_PMSchedule') AND c.column_id BETWEEN 3 AND 5 ORDER BY c.column_id;
SELECT PMType, COUNT(*) AS cnt FROM dbo.MNT_PMSchedule GROUP BY PMType ORDER BY PMType;
SELECT GroupCode, CodeValue, CodeName, SortOrder FROM dbo.MD_CodeItem WHERE GroupCode IN ('PM_CLASS','PM_TYPE') ORDER BY GroupCode, SortOrder;
GO
