-- =============================================================================
-- migrate_linetimepattern_shiftpattern.sql
-- MD_LineTimePattern.ShiftModel(VARCHAR12) → ShiftPattern(VARCHAR20) 로 변경.
-- SHIFT_PATTERN 공통코드 값(예: SHIFT2_SCHEDULE=14자)이 VARCHAR(12)에 잘리는 문제 해결.
-- MD_Calendar.ShiftPattern / MD_Line.ShiftPattern 과 컬럼명·크기 통일.
-- 가드: ShiftModel 이 있고 ShiftPattern 이 없을 때만 rename, 이후 항상 크기 보정.
-- =============================================================================
SET NOCOUNT ON;

IF COL_LENGTH('dbo.MD_LineTimePattern','ShiftModel') IS NOT NULL
   AND COL_LENGTH('dbo.MD_LineTimePattern','ShiftPattern') IS NULL
BEGIN
    EXEC sp_rename 'dbo.MD_LineTimePattern.ShiftModel', 'ShiftPattern', 'COLUMN';
    PRINT N'✓ ShiftModel → ShiftPattern 컬럼명 변경';
END
GO

IF COL_LENGTH('dbo.MD_LineTimePattern','ShiftPattern') IS NOT NULL
BEGIN
    ALTER TABLE dbo.MD_LineTimePattern ALTER COLUMN [ShiftPattern] VARCHAR(20) NULL;
    PRINT N'✓ ShiftPattern VARCHAR(20) 크기 보정';
END
GO
