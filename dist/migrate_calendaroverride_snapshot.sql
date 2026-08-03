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
