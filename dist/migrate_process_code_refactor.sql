-- ╔══════════════════════════════════════════════════════════════════════╗
-- ║  Migration: 공정코드(ProcessCode) 단일화                              ║
-- ║  - MD_Line:      LineType 제거 → ProcessCode 추가 (PROCESS 공통코드)   ║
-- ║  - MD_WorkCenter:ProcessType 제거 → ProcessCode 추가 (LineID 다음)     ║
-- ║  - MD_Station:   StationType 제거 (ProcessCode 는 기존 유지)          ║
-- ║  ProcessCode 원천 = MD_Line. WorkCenter/Station 은 라인값을 복사.      ║
-- ╚══════════════════════════════════════════════════════════════════════╝
SET XACT_ABORT ON;
GO

-- ── 1) MD_Line : ProcessCode 추가 + LineType 값 승계 후 LineType 제거 ──
IF COL_LENGTH('dbo.MD_Line', 'ProcessCode') IS NULL
    ALTER TABLE dbo.MD_Line ADD [ProcessCode] VARCHAR(10) NULL;
GO
IF COL_LENGTH('dbo.MD_Line', 'LineType') IS NOT NULL
    UPDATE dbo.MD_Line SET ProcessCode = LEFT(LineType, 10) WHERE ProcessCode IS NULL AND LineType IS NOT NULL;
GO
IF COL_LENGTH('dbo.MD_Line', 'LineType') IS NOT NULL
    ALTER TABLE dbo.MD_Line DROP COLUMN [LineType];
GO

-- ── 2) MD_WorkCenter : ProcessCode 추가 + 라인값 복사 후 ProcessType 제거 ──
IF COL_LENGTH('dbo.MD_WorkCenter', 'ProcessCode') IS NULL
    ALTER TABLE dbo.MD_WorkCenter ADD [ProcessCode] VARCHAR(10) NULL;
GO
UPDATE wc
SET    wc.ProcessCode = l.ProcessCode
FROM   dbo.MD_WorkCenter wc
JOIN   dbo.MD_Line l ON l.LineID = wc.LineID;
GO
IF COL_LENGTH('dbo.MD_WorkCenter', 'ProcessType') IS NOT NULL
    ALTER TABLE dbo.MD_WorkCenter DROP COLUMN [ProcessType];
GO

-- ── 3) MD_Station : 라인값을 ProcessCode 로 복사 후 StationType 제거 ──
UPDATE st
SET    st.ProcessCode = l.ProcessCode
FROM   dbo.MD_Station st
JOIN   dbo.MD_Line l ON l.LineID = st.LineID;
GO
IF COL_LENGTH('dbo.MD_Station', 'StationType') IS NOT NULL
    ALTER TABLE dbo.MD_Station DROP COLUMN [StationType];
GO

PRINT '공정코드 단일화 완료: MD_Line/MD_WorkCenter ProcessCode 추가, StationType/LineType/ProcessType 제거.';
GO
