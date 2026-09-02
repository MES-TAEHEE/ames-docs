/* ============================================================
   migrate_pp_lineschedule_pm.sql
   PP_LineSchedule 에 예방보전(PM) 밴드 등록/MNT 연계용 컬럼 추가
   - 가드형(재실행 안전), 수동 배포용
   - EntryType : 스케줄 행 구분 ('WO' 작업지시 배치 / 'PM' 예방보전 밴드), 기존 행은 'WO'
   - Title     : PM 밴드 표시명
   - RefType   : MNT 연계 종류 ('PMSCH' MNT_PMSchedule / 'MNTWO' MNT_WorkOrder)
   - RefID     : 연계 대상 PK (MNT_PMSchedule.PMScheduleID 등)
   - 접속: sqlcmd -S "localhost\MSSQLSERVER01" -U ames_app -P '!Dev2026' -d AMES_DEV -i dist\migrate_pp_lineschedule_pm.sql
   ============================================================ */
SET NOCOUNT ON;

IF COL_LENGTH('dbo.PP_LineSchedule', 'EntryType') IS NULL
BEGIN
    ALTER TABLE dbo.PP_LineSchedule
        ADD EntryType VARCHAR(10) NOT NULL
            CONSTRAINT DF_PP_LineSchedule_EntryType DEFAULT 'WO';
    PRINT 'PP_LineSchedule.EntryType 컬럼 추가 (기존 행 = WO)';
END
ELSE
    PRINT 'PP_LineSchedule.EntryType 이미 존재 — 스킵';

IF COL_LENGTH('dbo.PP_LineSchedule', 'Title') IS NULL
BEGIN
    ALTER TABLE dbo.PP_LineSchedule ADD Title NVARCHAR(100) NULL;
    PRINT 'PP_LineSchedule.Title 컬럼 추가';
END
ELSE
    PRINT 'PP_LineSchedule.Title 이미 존재 — 스킵';

IF COL_LENGTH('dbo.PP_LineSchedule', 'RefType') IS NULL
BEGIN
    ALTER TABLE dbo.PP_LineSchedule ADD RefType VARCHAR(10) NULL;
    PRINT 'PP_LineSchedule.RefType 컬럼 추가';
END
ELSE
    PRINT 'PP_LineSchedule.RefType 이미 존재 — 스킵';

IF COL_LENGTH('dbo.PP_LineSchedule', 'RefID') IS NULL
BEGIN
    ALTER TABLE dbo.PP_LineSchedule ADD RefID INT NULL;
    PRINT 'PP_LineSchedule.RefID 컬럼 추가';
END
ELSE
    PRINT 'PP_LineSchedule.RefID 이미 존재 — 스킵';
GO

/* ============================================================
   Part 2 : 컬럼 순서 정렬 (테이블 재구성)
   - ALTER ADD 는 컬럼을 테이블 끝(감사 컬럼 뒤)에 붙임 → 컨벤션 위반
     (업무 컬럼은 감사 컬럼 앞: migrate_column_order.sql 참조)
   - EntryType 이 CreatedBy 뒤(=끝)에 있을 때만 재구성 (멱등)
   - PP_LineSchedule 은 참조 FK 없음·PK(ScheduleID IDENTITY)만 → 재구성 안전
   - 감사 tail 은 DB 컨벤션(ModifiedBy → ModifiedTS) 유지
   ============================================================ */
IF EXISTS (
    SELECT 1
    FROM   sys.columns et
    JOIN   sys.columns cb ON cb.object_id = et.object_id AND cb.name = 'CreatedBy'
    WHERE  et.object_id = OBJECT_ID(N'dbo.PP_LineSchedule')
      AND  et.name = 'EntryType'
      AND  et.column_id > cb.column_id          -- EntryType 이 감사 컬럼 뒤 → 잘못된 순서
)
BEGIN
    CREATE TABLE dbo.PP_LineSchedule_new (
        [ScheduleID]    INT IDENTITY      NOT NULL,
        [LineID]        VARCHAR(20)           NULL,
        [ScheduleDate]  DATE                  NULL,
        [WoID]          INT                   NULL,
        [StartMin]      SMALLINT              NULL,
        [EndMin]        SMALLINT              NULL,
        [PlannedQty]    DECIMAL(14,3)         NULL,
        [PatternID]     VARCHAR(20)           NULL,
        [EntryType]     VARCHAR(10)       NOT NULL CONSTRAINT DF_PP_LineSchedule_EntryType_new DEFAULT 'WO',
        [Title]         NVARCHAR(100)         NULL,
        [RefType]       VARCHAR(10)           NULL,
        [RefID]         INT                   NULL,
        [Status]        VARCHAR(20)           NULL,
        [PublishedAt]   DATETIME2             NULL,
        [PublishedBy]   NVARCHAR(450)         NULL,
        [CreatedBy]     VARCHAR(50)       NOT NULL,
        [CreatedTS]     DATETIME2             NULL DEFAULT SYSDATETIME(),
        [ModifiedBy]    NVARCHAR(450)         NULL,
        [ModifiedTS]    DATETIME2             NULL,
        CONSTRAINT PK_PP_LineSchedule_new PRIMARY KEY CLUSTERED ([ScheduleID])
    );

    SET IDENTITY_INSERT dbo.PP_LineSchedule_new ON;

    INSERT INTO dbo.PP_LineSchedule_new
        (ScheduleID, LineID, ScheduleDate, WoID, StartMin, EndMin, PlannedQty, PatternID,
         EntryType, Title, RefType, RefID, Status, PublishedAt, PublishedBy,
         CreatedBy, CreatedTS, ModifiedBy, ModifiedTS)
    SELECT
         ScheduleID, LineID, ScheduleDate, WoID, StartMin, EndMin, PlannedQty, PatternID,
         EntryType, Title, RefType, RefID, Status, PublishedAt, PublishedBy,
         CreatedBy, CreatedTS, ModifiedBy, ModifiedTS
    FROM dbo.PP_LineSchedule;

    SET IDENTITY_INSERT dbo.PP_LineSchedule_new OFF;

    DROP TABLE dbo.PP_LineSchedule;
    EXEC sp_rename N'dbo.PP_LineSchedule_new',                  N'PP_LineSchedule',                 N'OBJECT';
    EXEC sp_rename N'dbo.PK_PP_LineSchedule_new',               N'PK_PP_LineSchedule',              N'OBJECT';
    EXEC sp_rename N'dbo.DF_PP_LineSchedule_EntryType_new',     N'DF_PP_LineSchedule_EntryType',    N'OBJECT';

    PRINT '✓ Part2 : PP_LineSchedule 컬럼 순서 재구성 완료 (업무 컬럼 → 감사 컬럼)';
END
ELSE
    PRINT '– Part2 : PP_LineSchedule 이미 올바른 순서 (스킵)';
GO
