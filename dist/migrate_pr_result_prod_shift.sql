-- ════════════════════════════════════════════════════════════════════════
--  migrate_pr_result_prod_shift.sql
--  PR_ProductionResult.ProdDate / ShiftCode — 실적의 전기일·교대
--
--  POP 이 실적을 INSERT 할 때 공통코드로 판정해 박아 둔다 (AMES.Data.Services.ProdCalendar).
--    DAY_CUTOFF / TIME  Attribute1 'HH:mm'  — 이 시각 전 실적은 전날 생산분
--    WORK_SHIFT / A,B,C Attribute1 'HHMM-HHMM' — SortOrder 순 첫 매치, 2400 = 자정
--  기존 행은 EntryAt 기준으로 같은 규칙으로 백필한다. 어느 교대 창에도 안 걸리면 NULL.
--
--  스키마 변경: 컬럼·인덱스 추가만. 순서 무관, 재실행 안전.
--  적용:  sqlcmd -S 192.168.1.100,1433 -U ames_app -P !Dev2026 -d AMES_DEV -f 65001 -i dist/migrate_pr_result_prod_shift.sql
-- ════════════════════════════════════════════════════════════════════════
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF COL_LENGTH('dbo.PR_ProductionResult', 'ProdDate') IS NULL
BEGIN
    ALTER TABLE dbo.PR_ProductionResult ADD [ProdDate] DATE NULL;
    PRINT 'PR_ProductionResult.ProdDate added';
END
ELSE
    PRINT 'PR_ProductionResult.ProdDate already exists';

IF COL_LENGTH('dbo.PR_ProductionResult', 'ShiftCode') IS NULL
BEGIN
    ALTER TABLE dbo.PR_ProductionResult ADD [ShiftCode] VARCHAR(10) NULL;  -- MD_CodeItem WORK_SHIFT.CodeValue
    PRINT 'PR_ProductionResult.ShiftCode added';
END
ELSE
    PRINT 'PR_ProductionResult.ShiftCode already exists';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PR_ProductionResult_ProdDate' AND object_id = OBJECT_ID('dbo.PR_ProductionResult'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_PR_ProductionResult_ProdDate
        ON dbo.PR_ProductionResult (ProdDate, LineID) INCLUDE (ShiftCode, GoodQty);
    PRINT 'IX_PR_ProductionResult_ProdDate created';
END
GO

-- ── 백필: ProdDate 가 비어 있는 기존 실적 ──────────────────────────────
DECLARE @cutoffMin INT = 0;
DECLARE @attr NVARCHAR(40);
SELECT TOP 1 @attr = LTRIM(RTRIM(Attribute1)) FROM dbo.MD_CodeItem
WHERE  GroupCode = 'DAY_CUTOFF' AND CodeValue = 'TIME' AND ISNULL(UseFlag,1) = 1;

IF @attr IS NOT NULL
BEGIN
    IF CHARINDEX(':', @attr) > 0 AND TRY_CAST(@attr AS TIME) IS NOT NULL
        SET @cutoffMin = DATEDIFF(MINUTE, CAST('00:00' AS TIME), CAST(@attr AS TIME));
    ELSE IF LEN(@attr) = 4 AND TRY_CAST(@attr AS INT) IS NOT NULL
         AND TRY_CAST(LEFT(@attr,2) AS INT) BETWEEN 0 AND 23 AND TRY_CAST(RIGHT(@attr,2) AS INT) BETWEEN 0 AND 59
        SET @cutoffMin = CAST(LEFT(@attr,2) AS INT) * 60 + CAST(RIGHT(@attr,2) AS INT);
END

DECLARE @shift TABLE (Code VARCHAR(20), StartMin INT, EndMin INT, Sort INT);
INSERT INTO @shift (Code, StartMin, EndMin, Sort)
SELECT CodeValue,
       CAST(SUBSTRING(a, 1, 2) AS INT) * 60 + CAST(SUBSTRING(a, 3, 2) AS INT),
       CAST(SUBSTRING(a, 6, 2) AS INT) * 60 + CAST(SUBSTRING(a, 8, 2) AS INT),
       ISNULL(SortOrder, 0)
FROM   dbo.MD_CodeItem
CROSS  APPLY (SELECT LTRIM(RTRIM(Attribute1)) AS a) x
WHERE  GroupCode = 'WORK_SHIFT' AND ISNULL(UseFlag,1) = 1
  AND  a LIKE '[0-9][0-9][0-9][0-9]-[0-9][0-9][0-9][0-9]';

UPDATE r
SET    ProdDate  = CASE WHEN x.m < @cutoffMin THEN DATEADD(DAY, -1, CAST(r.EntryAt AS DATE))
                        ELSE CAST(r.EntryAt AS DATE) END,
       ShiftCode = (SELECT TOP 1 s.Code FROM @shift s
                    WHERE (s.StartMin < s.EndMin AND x.m >= s.StartMin AND x.m <  s.EndMin)
                       OR (s.StartMin > s.EndMin AND (x.m >= s.StartMin OR x.m < s.EndMin))
                    ORDER BY s.Sort, s.Code)
FROM   dbo.PR_ProductionResult r
CROSS  APPLY (SELECT DATEDIFF(MINUTE, CAST(CAST(r.EntryAt AS DATE) AS DATETIME2), r.EntryAt) AS m) x
WHERE  r.ProdDate IS NULL AND r.EntryAt IS NOT NULL;

DECLARE @rows INT = @@ROWCOUNT, @shiftCount INT;
SELECT @shiftCount = COUNT(*) FROM @shift;
PRINT CONCAT('backfilled rows: ', @rows, ' (cutoff ', @cutoffMin, ' min, shifts ', @shiftCount, ')');
GO

SELECT c.name, t.name AS type_name, c.max_length, c.is_nullable
FROM   sys.columns c
JOIN   sys.types   t ON t.user_type_id = c.user_type_id
WHERE  c.object_id = OBJECT_ID('dbo.PR_ProductionResult') AND c.name IN ('ProdDate','ShiftCode')
ORDER  BY c.column_id;
GO
