-- ============================================================
-- migrate_calendaroverride_snapshot.sql
-- PP_ProductionCalendarOverride 에 "발행 스냅샷" 컬럼 추가.
--   LineSchedule(PP-LSB) Publish 시점에 해당 라인+일자의 분단위 가동/구간유형을
--   MD_LineTimePattern 과 동일한 인코딩(CHAR(1440), SEGMENT_STATE.Attribute1 'op:seg')으로 굳혀 저장한다.
--   PM(예방보전) 구간은 SEGMENT_STATE_PM(Attribute1='0:9') 로 인코딩되어 SegmentFlag 에 반영된다.
-- 컬럼순서: MD_LineTimePattern 준용 (TotalOperatingMin → TotalPlannedDownMin → OperatingFlag → SegmentFlag)
-- 가드형(idempotent) — 재실행 안전.
-- 접속: sqlcmd -S "localhost\MSSQLSERVER01" -U ames_app -P '!Dev2026' -d AMES_DEV -i dist\migrate_calendaroverride_snapshot.sql
-- ============================================================
SET NOCOUNT ON;

IF COL_LENGTH('dbo.PP_ProductionCalendarOverride', 'TotalOperatingMin') IS NULL
BEGIN
    ALTER TABLE dbo.PP_ProductionCalendarOverride ADD [TotalOperatingMin] INT NULL;
    PRINT 'added PP_ProductionCalendarOverride.TotalOperatingMin';
END

IF COL_LENGTH('dbo.PP_ProductionCalendarOverride', 'TotalPlannedDownMin') IS NULL
BEGIN
    ALTER TABLE dbo.PP_ProductionCalendarOverride ADD [TotalPlannedDownMin] INT NULL;
    PRINT 'added PP_ProductionCalendarOverride.TotalPlannedDownMin';
END

IF COL_LENGTH('dbo.PP_ProductionCalendarOverride', 'OperatingFlag') IS NULL
BEGIN
    ALTER TABLE dbo.PP_ProductionCalendarOverride
        ADD [OperatingFlag] CHAR(1440) NOT NULL
            CONSTRAINT DF_PP_ProductionCalendarOverride_OperatingFlag DEFAULT REPLICATE('0',1440);
    PRINT 'added PP_ProductionCalendarOverride.OperatingFlag';
END

IF COL_LENGTH('dbo.PP_ProductionCalendarOverride', 'SegmentFlag') IS NULL
BEGIN
    ALTER TABLE dbo.PP_ProductionCalendarOverride
        ADD [SegmentFlag] CHAR(1440) NOT NULL
            CONSTRAINT DF_PP_ProductionCalendarOverride_SegmentFlag DEFAULT REPLICATE('0',1440);
    PRINT 'added PP_ProductionCalendarOverride.SegmentFlag';
END
GO

-- SEGMENT_STATE 에 PM(예방보전) 코드가 없으면 추가 (seed_md_code.sql 와 동일)
IF NOT EXISTS (SELECT 1 FROM dbo.MD_CodeItem WHERE GroupCode='SEGMENT_STATE' AND CodeValue='PM')
BEGIN
    INSERT INTO dbo.MD_CodeItem (CodeID, GroupCode, CodeValue, CodeName, CodeNameEn, ParentCodeID, SortOrder, Attribute1, UseFlag, Description, CreatedBy)
    VALUES ('SEGMENT_STATE_PM', 'SEGMENT_STATE', 'PM', N'예방 보전', N'Preventive Maintenance', NULL, 90, '0:9', 1, NULL, 'admin@ames.local');
    PRINT 'seeded SEGMENT_STATE_PM (0:9)';
END
GO

-- ============================================================
-- Part 2 — 컬럼 순서 재구성
--   Part 1 의 ALTER ADD 는 4컬럼을 감사컬럼 뒤(끝)에 붙인다 → "업무컬럼은 감사 tail 앞" 컨벤션 위반.
--   PP_LineSchedule 때(migrate_pp_lineschedule_pm.sql Part2)와 동일하게 테이블 재구성으로
--   PatternID 뒤에 재배치한다. 정의는 dist/AMES_Schema.sql 의 CREATE TABLE 과 일치시킨다.
--   전제: 이 테이블을 참조하는 FK·뷰·프로시저 없음.
--   가드: TotalOperatingMin 이 CreatedBy 보다 뒤에 있을 때만 수행 → 재실행/신규생성 DB 에서 무동작.
-- ============================================================
IF OBJECT_ID('dbo.PP_ProductionCalendarOverride','U') IS NOT NULL
   AND (SELECT column_id FROM sys.columns
        WHERE object_id=OBJECT_ID('dbo.PP_ProductionCalendarOverride') AND name='TotalOperatingMin')
     > (SELECT column_id FROM sys.columns
        WHERE object_id=OBJECT_ID('dbo.PP_ProductionCalendarOverride') AND name='CreatedBy')
BEGIN
    DECLARE @seed INT = CAST(IDENT_CURRENT('dbo.PP_ProductionCalendarOverride') AS INT);

    BEGIN TRAN;

    CREATE TABLE dbo.PP_ProductionCalendarOverride_new (
      [OverrideID]                INT IDENTITY         NOT NULL,
      [OverrideDate]              DATE                     NULL,
      [LineID]                    VARCHAR(20)              NULL,
      [DayType]                   VARCHAR(20)              NULL,
      [PatternID]                 VARCHAR(20)              NULL,
      [TotalOperatingMin]         INT                      NULL,
      [TotalPlannedDownMin]       INT                      NULL,
      [OperatingFlag]             CHAR(1440)           NOT NULL CONSTRAINT DF_PP_ProductionCalendarOverride_OperatingFlag_new DEFAULT REPLICATE('0',1440),
      [SegmentFlag]               CHAR(1440)           NOT NULL CONSTRAINT DF_PP_ProductionCalendarOverride_SegmentFlag_new   DEFAULT REPLICATE('0',1440),
      [CapacityFactor]            DECIMAL(5,2)             NULL,
      [Reason]                    NVARCHAR(200)            NULL,
      [ApprovedBy]                NVARCHAR(450)            NULL,
      [ApprovedAt]                DATETIME2                NULL,
      [CreatedBy]                 VARCHAR(50)          NOT NULL,
      [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
      [ModifiedBy]                NVARCHAR(450)            NULL,
      [ModifiedTS]                DATETIME2                NULL,
      CONSTRAINT PK_PP_ProductionCalendarOverride_new PRIMARY KEY CLUSTERED ([OverrideID])
    );

    SET IDENTITY_INSERT dbo.PP_ProductionCalendarOverride_new ON;
    INSERT INTO dbo.PP_ProductionCalendarOverride_new
           (OverrideID, OverrideDate, LineID, DayType, PatternID,
            TotalOperatingMin, TotalPlannedDownMin, OperatingFlag, SegmentFlag,
            CapacityFactor, Reason, ApprovedBy, ApprovedAt,
            CreatedBy, CreatedTS, ModifiedBy, ModifiedTS)
    SELECT  OverrideID, OverrideDate, LineID, DayType, PatternID,
            TotalOperatingMin, TotalPlannedDownMin, OperatingFlag, SegmentFlag,
            CapacityFactor, Reason, ApprovedBy, ApprovedAt,
            CreatedBy, CreatedTS, ModifiedBy, ModifiedTS
    FROM    dbo.PP_ProductionCalendarOverride;
    SET IDENTITY_INSERT dbo.PP_ProductionCalendarOverride_new OFF;

    DROP TABLE dbo.PP_ProductionCalendarOverride;

    EXEC sp_rename 'dbo.PP_ProductionCalendarOverride_new', 'PP_ProductionCalendarOverride';
    EXEC sp_rename 'dbo.PK_PP_ProductionCalendarOverride_new', 'PK_PP_ProductionCalendarOverride', 'OBJECT';
    EXEC sp_rename 'dbo.DF_PP_ProductionCalendarOverride_OperatingFlag_new', 'DF_PP_ProductionCalendarOverride_OperatingFlag', 'OBJECT';
    EXEC sp_rename 'dbo.DF_PP_ProductionCalendarOverride_SegmentFlag_new',   'DF_PP_ProductionCalendarOverride_SegmentFlag',   'OBJECT';

    COMMIT;

    -- IDENTITY_INSERT 는 max(입력값)으로 seed 를 맞추지만, 빈 테이블이면 초기값으로 리셋되므로 명시 복원
    DECLARE @sql NVARCHAR(300) =
        N'DBCC CHECKIDENT (''dbo.PP_ProductionCalendarOverride'', RESEED, ' + CAST(@seed AS NVARCHAR(20)) + N') WITH NO_INFOMSGS;';
    EXEC sp_executesql @sql;

    PRINT 'rebuilt PP_ProductionCalendarOverride (column order restored)';
END
GO
