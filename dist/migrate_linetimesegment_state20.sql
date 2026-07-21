-- =============================================================================
-- migrate_linetimesegment_state20.sql
-- MD_LineTimeSegment.SegmentState VARCHAR(14) → VARCHAR(20) 확대.
-- SEGMENT_STATE 코드 'PLANNED_DOWNTIME'(16자)이 14자로 잘려 코드 조회·다국어 표시가
-- 실패하던 문제 해결. MD_CodeItem.CodeValue(VARCHAR20)와 크기 통일.
-- =============================================================================
SET NOCOUNT ON;

IF COL_LENGTH('dbo.MD_LineTimeSegment','SegmentState') IS NOT NULL
    ALTER TABLE dbo.MD_LineTimeSegment ALTER COLUMN [SegmentState] VARCHAR(20) NULL;
GO

-- 이전에 14자로 잘려 저장된 값 복구
UPDATE dbo.MD_LineTimeSegment SET SegmentState = 'PLANNED_DOWNTIME' WHERE SegmentState = 'PLANNED_DOWNTI';
GO

PRINT N'✓ MD_LineTimeSegment.SegmentState VARCHAR(20) + 잘린 값 복구';
GO
