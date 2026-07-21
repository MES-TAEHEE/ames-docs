-- =============================================================================
-- migrate_linetimepattern_flags.sql
-- MD_LineTimePattern 에 분단위(1440) 플래그 컬럼 추가.
--   OperatingFlag CHAR(1440) : 각 분(0~1439)의 가동여부 (SEGMENT_STATE.Attribute1 의 ':' 앞 값)
--   SegmentFlag   CHAR(1440) : 각 분의 구간유형        (SEGMENT_STATE.Attribute1 의 ':' 뒤 값)
-- 기본값: '0' 1440자. 밴드 편집(세그먼트 변경) 시 백엔드가 재생성.
-- (SQL Server ADD COLUMN 은 물리적으로 끝에 추가되나, 컬럼 접근은 이름 기반이라 무관)
-- =============================================================================
SET NOCOUNT ON;

IF COL_LENGTH('dbo.MD_LineTimePattern','OperatingFlag') IS NULL
    ALTER TABLE dbo.MD_LineTimePattern
        ADD [OperatingFlag] CHAR(1440) NOT NULL
            CONSTRAINT DF_MD_LineTimePattern_OperatingFlag DEFAULT REPLICATE('0', 1440);
GO

IF COL_LENGTH('dbo.MD_LineTimePattern','SegmentFlag') IS NULL
    ALTER TABLE dbo.MD_LineTimePattern
        ADD [SegmentFlag] CHAR(1440) NOT NULL
            CONSTRAINT DF_MD_LineTimePattern_SegmentFlag DEFAULT REPLICATE('0', 1440);
GO

PRINT N'✓ MD_LineTimePattern.OperatingFlag / SegmentFlag CHAR(1440) 추가';
GO
