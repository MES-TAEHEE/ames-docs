-- ╔══════════════════════════════════════════════════════════════════════╗
-- ║  Migration: MD_WorkCenter.LineID → DefaultLineID                        ║
-- ║             MD_Line.DefaultWCID → WCID                                  ║
-- ║  라인/작업장 상호 참조 컬럼명 정규화 (역할이 드러나도록 컬럼명 변경)    ║
-- ║  값·타입·인덱스·FK 변화 없음 (sp_rename 컬럼 rename 만 수행)            ║
-- ╚══════════════════════════════════════════════════════════════════════╝
SET XACT_ABORT ON;
GO

-- MD_WorkCenter: LineID → DefaultLineID (해당 작업장이 속한 기본 라인)
IF COL_LENGTH('dbo.MD_WorkCenter', 'LineID') IS NOT NULL
   AND COL_LENGTH('dbo.MD_WorkCenter', 'DefaultLineID') IS NULL
    EXEC sp_rename 'dbo.MD_WorkCenter.LineID', 'DefaultLineID', 'COLUMN';
GO

-- MD_Line: DefaultWCID → WCID (해당 라인의 기본 작업장)
IF COL_LENGTH('dbo.MD_Line', 'DefaultWCID') IS NOT NULL
   AND COL_LENGTH('dbo.MD_Line', 'WCID') IS NULL
    EXEC sp_rename 'dbo.MD_Line.DefaultWCID', 'WCID', 'COLUMN';
GO

PRINT 'MD_WorkCenter.DefaultLineID / MD_Line.WCID 컬럼명 변경 완료.';
GO
