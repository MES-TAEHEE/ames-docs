-- ╔══════════════════════════════════════════════════════════════════════╗
-- ║  Migration: MD_Bop  ProcessCode/WorkCenterID → StationCode            ║
-- ║  BOP 라우팅의 공정 참조를 MD_Station.StationCode 로 일원화             ║
-- ╚══════════════════════════════════════════════════════════════════════╝
SET XACT_ABORT ON;
GO

-- 1) StationCode 컬럼 추가 (없을 때만)
IF COL_LENGTH('dbo.MD_Bop', 'StationCode') IS NULL
    ALTER TABLE dbo.MD_Bop ADD [StationCode] VARCHAR(20) NULL;
GO

-- 2) 기존 데이터 보정(선택): ProcessCode 로 매핑 가능한 Station 이 유일하면 승계
--    (동일 ProcessCode 를 가진 Station 이 1개일 때만 안전하게 채움)
IF COL_LENGTH('dbo.MD_Bop', 'ProcessCode') IS NOT NULL
BEGIN
    UPDATE b
    SET    b.StationCode = s.StationCode
    FROM   dbo.MD_Bop b
    JOIN   (
        SELECT ProcessCode, MIN(StationCode) AS StationCode
        FROM   dbo.MD_Station
        WHERE  ProcessCode IS NOT NULL
        GROUP  BY ProcessCode
        HAVING COUNT(*) = 1
    ) s ON s.ProcessCode = b.ProcessCode
    WHERE  b.StationCode IS NULL;
END
GO

-- 3) 구 컬럼 제거
IF COL_LENGTH('dbo.MD_Bop', 'ProcessCode') IS NOT NULL
    ALTER TABLE dbo.MD_Bop DROP COLUMN [ProcessCode];
GO
IF COL_LENGTH('dbo.MD_Bop', 'WorkCenterID') IS NOT NULL
    ALTER TABLE dbo.MD_Bop DROP COLUMN [WorkCenterID];
GO

PRINT 'MD_Bop: ProcessCode/WorkCenterID 제거, StationCode 추가 완료.';
GO
