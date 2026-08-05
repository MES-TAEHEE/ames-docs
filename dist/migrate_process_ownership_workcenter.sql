-- ╔══════════════════════════════════════════════════════════════════════╗
-- ║  Migration: 공정(ProcessCode) 소유권을 WorkCenter 로 이전               ║
-- ║  계층: WorkCenter(공정 소유) → Line(WCID NOT NULL, 공정 상속)           ║
-- ║        → Station(공정 없음, 라인→작업장에서 상속)                       ║
-- ║  · MD_WorkCenter.ProcessCode : NOT NULL 로 승격                        ║
-- ║  · MD_Line.WCID              : 백필 후 NOT NULL 로 승격                 ║
-- ║  · MD_Line.ProcessCode       : 제거 (WC 에서 상속)                     ║
-- ║  · MD_Station.ProcessCode    : 제거 (라인→WC 에서 상속)                ║
-- ╚══════════════════════════════════════════════════════════════════════╝
SET XACT_ABORT ON;
GO

-- [1] 공정별 WorkCenter 보장(없으면 생성) — MD_Line.ProcessCode 를 원천으로
IF COL_LENGTH('dbo.MD_Line', 'ProcessCode') IS NOT NULL
BEGIN
    INSERT INTO dbo.MD_WorkCenter (WCID, WCName, ProcessCode, ActiveFlag, CreatedBy, CreatedTS)
    SELECT DISTINCT 'WC-' + l.ProcessCode, l.ProcessCode + ' WorkCenter', l.ProcessCode, 1, 'system', SYSDATETIME()
    FROM   dbo.MD_Line l
    WHERE  l.ProcessCode IS NOT NULL
      AND  NOT EXISTS (SELECT 1 FROM dbo.MD_WorkCenter wc WHERE wc.ProcessCode = l.ProcessCode);

    -- [2] 라인 WCID 백필: 공정이 일치하는 작업장으로
    UPDATE l
       SET WCID = (SELECT TOP 1 wc.WCID FROM dbo.MD_WorkCenter wc
                    WHERE wc.ProcessCode = l.ProcessCode ORDER BY wc.WCID)
    FROM dbo.MD_Line l
    WHERE l.WCID IS NULL AND l.ProcessCode IS NOT NULL;
END
GO

-- [3] 잔여(공정 미지정) 라인: 첫 작업장으로 폴백 배정 — WCID NOT NULL 충족용(구조 우선)
UPDATE l
   SET WCID = (SELECT TOP 1 wc.WCID FROM dbo.MD_WorkCenter wc ORDER BY wc.WCID)
FROM dbo.MD_Line l
WHERE l.WCID IS NULL;
GO

-- [4] MD_WorkCenter.ProcessCode → NOT NULL (공정 소유의 원천)
ALTER TABLE dbo.MD_WorkCenter ALTER COLUMN [ProcessCode] VARCHAR(10) NOT NULL;
GO

-- [5] MD_Line.WCID → NOT NULL (모든 라인은 반드시 작업장 소속)
ALTER TABLE dbo.MD_Line ALTER COLUMN [WCID] VARCHAR(20) NOT NULL;
GO

-- [6] 중복 공정 컬럼 제거 (WC 에서 상속으로 대체)
IF COL_LENGTH('dbo.MD_Line', 'ProcessCode') IS NOT NULL
    ALTER TABLE dbo.MD_Line DROP COLUMN [ProcessCode];
GO
IF COL_LENGTH('dbo.MD_Station', 'ProcessCode') IS NOT NULL
    ALTER TABLE dbo.MD_Station DROP COLUMN [ProcessCode];
GO

PRINT '공정 소유권 WorkCenter 이전 완료: WC.ProcessCode NOT NULL / Line.WCID NOT NULL / Line.ProcessCode·Station.ProcessCode 제거.';
GO
