-- ════════════════════════════════════════════════════════════════════════
--  seed_inj_img_master_dev.sql
--  dev: BOM Master List 품목 기준 사출·감싸기 현장 마스터 — 구 seed_md_bop_inj_dev.sql·seed_md_bop_img_dev.sql 대체.
--
--  코어 = ASSY 의 유효 BOM 을 SUB 로만 내려가 품명이 CORE 로 시작하는 SUB(정확히 1개) — AMES.Data.Services.CoreItemResolver 와
--  같은 규칙의 SQL 판이다(CoreItemDbTests 가 둘의 일치를 검증). 2026-09-30 기준 코어 35종.
--  · MD_Mold      : 코어마다 'M-{코어품번}' (650T, 캐비티 1, 교체 30분, ACTIVE, CarType = 코어 차종)
--  · MD_MoldItem  : (금형, 코어, 색 CBK, 캐비티 1, LH/RH = 품명에 RH 있으면 RH, 레진 = 코어 BOM 첫 RESIN 자식)
--  · MD_MoldLine  : 차종별 INJ 라인 — NE1A→01, LQ2→02, LX3A→03, ME1A→04, MV1A→05, NQ5A·NX5A→06 (UPH 60, PrepTime 30)
--  · MD_Bop       : INJ 스테이션 × 코어 (A/10, 사이클 60초) · IMG 스테이션 × ASSY (A/20, 사이클 90초)
--                   IMG 라인은 05 까지라 NQ5A·NX5A 는 IMG-05.
--  · 이미 있는 행은 건너뛴다(재실행 안전). 운영 DB 용이 아니다.
--  적용: sqlcmd -f 65001 -I -b -i dist/seed_inj_img_master_dev.sql  (seed_md_item_bom_master_list · migrate_mold_master 뒤)
-- ════════════════════════════════════════════════════════════════════════
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
BEGIN TRAN;

-- 1) ASSY → 코어 (CoreItemResolver 규칙의 SQL 판)
DECLARE @Today date = CAST(SYSDATETIME() AS date);
IF OBJECT_ID('tempdb..#core') IS NOT NULL DROP TABLE #core;
;WITH eff AS (
    SELECT v.VersionID, v.EffFrom FROM dbo.MD_BomVersion v
    WHERE  v.Status = 'APPROVED' AND (v.EffFrom IS NULL OR v.EffFrom <= @Today) AND (v.EffTo IS NULL OR v.EffTo >= @Today)),
lines AS (
    SELECT b.ParentItemNo, b.CompItemNo,
           DENSE_RANK() OVER (PARTITION BY b.ParentItemNo ORDER BY e.EffFrom DESC, e.VersionID DESC) AS Rk
    FROM   dbo.MD_Bom b JOIN eff e ON e.VersionID = b.VersionID
    WHERE  ISNULL(b.ActiveFlag,1) = 1 AND b.ParentItemNo IS NOT NULL AND b.CompItemNo IS NOT NULL),
subs AS (SELECT l.ParentItemNo, l.CompItemNo FROM lines l JOIN dbo.MD_Item c ON c.ItemNo = l.CompItemNo WHERE l.Rk = 1 AND c.ItemType = 'SUB'),
walk AS (
    SELECT a.ItemNo AS Assy, s.CompItemNo AS Node, 1 AS Lvl, CAST('|' + a.ItemNo + '|' + s.CompItemNo + '|' AS varchar(400)) AS Path
    FROM   dbo.MD_Item a JOIN subs s ON s.ParentItemNo = a.ItemNo
    WHERE  a.ItemType = 'ASSY' AND ISNULL(a.ActiveFlag,1) = 1
    UNION ALL
    SELECT w.Assy, s.CompItemNo, w.Lvl + 1, CAST(w.Path + s.CompItemNo + '|' AS varchar(400))
    FROM   walk w JOIN subs s ON s.ParentItemNo = w.Node
    WHERE  w.Lvl < 8 AND w.Path NOT LIKE '%|' + s.CompItemNo + '|%'),
cores AS (
    SELECT DISTINCT w.Assy, w.Node
    FROM   walk w JOIN dbo.MD_Item i ON i.ItemNo = w.Node
    WHERE  LTRIM(i.ItemName) LIKE 'CORE%')
SELECT c.Assy, c.Node AS CoreItemNo
INTO   #core
FROM   cores c
WHERE  (SELECT COUNT(*) FROM cores x WHERE x.Assy = c.Assy) = 1;
DECLARE @nMap int = (SELECT COUNT(*) FROM #core), @nAssy int = (SELECT COUNT(*) FROM dbo.MD_Item WHERE ItemType = 'ASSY' AND ISNULL(ActiveFlag,1) = 1),
        @nCore int = (SELECT COUNT(DISTINCT CoreItemNo) FROM #core);
PRINT CONCAT(N'§1 ASSY→코어 매핑: ', @nMap, N' 건 (ASSY ', @nAssy, N' 중), 코어 ', @nCore, N' 종');

-- 2) 차종 → 라인 번호
IF OBJECT_ID('tempdb..#line') IS NOT NULL DROP TABLE #line;
CREATE TABLE #line (CarType varchar(20) COLLATE DATABASE_DEFAULT PRIMARY KEY, InjNo int NOT NULL, ImgNo int NOT NULL);   -- tempdb 콜레이션이 DB 와 다른 서버(복제본) 대비
INSERT INTO #line VALUES ('NE1A',1,1), ('LQ2',2,2), ('LX3A',3,3), ('ME1A',4,4), ('MV1A',5,5), ('NQ5A',6,5), ('NX5A',6,5);

IF OBJECT_ID('tempdb..#mold') IS NOT NULL DROP TABLE #mold;
SELECT DISTINCT c.CoreItemNo, ci.ItemName AS CoreName, ISNULL(ci.CarType, a.CarType) AS CarType,
       CAST('M-' + c.CoreItemNo AS varchar(20)) COLLATE DATABASE_DEFAULT AS MoldID,
       CAST('LINE-INJ-' + RIGHT('0' + CAST(l.InjNo AS varchar(2)), 2) AS varchar(20)) COLLATE DATABASE_DEFAULT AS InjLine,
       CAST('ST-INJ-'   + RIGHT('0' + CAST(l.InjNo AS varchar(2)), 2) AS varchar(20)) COLLATE DATABASE_DEFAULT AS InjStation
INTO   #mold
FROM   #core c
JOIN   dbo.MD_Item ci ON ci.ItemNo = c.CoreItemNo
JOIN   dbo.MD_Item a  ON a.ItemNo  = c.Assy
JOIN   #line l ON l.CarType = ISNULL(ci.CarType, a.CarType);
IF EXISTS (SELECT 1 FROM #mold WHERE LEN(MoldID) > 20) THROW 50120, N'MoldID 20자 초과', 1;
IF EXISTS (SELECT 1 FROM #mold m WHERE NOT EXISTS (SELECT 1 FROM dbo.MD_Station s WHERE s.StationCode = m.InjStation))
    THROW 50121, N'INJ 스테이션 없음 — MD_Station 시드 확인', 1;

-- 3) 금형·매핑·라인
INSERT INTO dbo.MD_Mold (MoldID, MoldName, RatedShots, CurrentShots, CavityCount, Tonnage, Status, CarType, MoldChangeMin, CumulativeShots, AssyInjResultFlag, CreatedBy, CreatedTS)
SELECT m.MoldID, LEFT(m.CoreName, 50), 500000, 0, 1, 650, 'ACTIVE', m.CarType, 30, 0, 0, 'SEED-DEMO', SYSDATETIME()
FROM   #mold m WHERE NOT EXISTS (SELECT 1 FROM dbo.MD_Mold x WHERE x.MoldID = m.MoldID);
PRINT CONCAT(N'§3 MD_Mold: ', @@ROWCOUNT);

INSERT INTO dbo.MD_MoldItem (MoldID, ItemNo, Color, CavitySeq, CavityPos, CavityCount, ResinItemNo, MoldCategory, ActiveFlag, CreatedBy, CreatedTS)
SELECT m.MoldID, m.CoreItemNo, 'CBK', 1, CASE WHEN m.CoreName LIKE '%RH%' THEN 'RH' ELSE 'LH' END, 1, r.ItemNo, 'INJECTION', 1, 'SEED-DEMO', SYSDATETIME()
FROM   #mold m
OUTER APPLY (SELECT TOP 1 c.ItemNo FROM dbo.MD_Bom b JOIN dbo.MD_Item c ON c.ItemNo = b.CompItemNo
             WHERE b.ParentItemNo = m.CoreItemNo AND c.ItemCategory = 'RESIN' ORDER BY b.QtyPer DESC) r
WHERE  NOT EXISTS (SELECT 1 FROM dbo.MD_MoldItem x WHERE x.MoldID = m.MoldID AND x.ItemNo = m.CoreItemNo);
PRINT CONCAT(N'§3 MD_MoldItem: ', @@ROWCOUNT);

INSERT INTO dbo.MD_MoldLine (LineCode, MoldID, UPH, PrepTime, CreatedBy, CreatedTS)
SELECT m.InjLine, m.MoldID, 60, 30, 'SEED-DEMO', SYSDATETIME()
FROM   #mold m WHERE NOT EXISTS (SELECT 1 FROM dbo.MD_MoldLine x WHERE x.LineCode = m.InjLine AND x.MoldID = m.MoldID);
PRINT CONCAT(N'§3 MD_MoldLine: ', @@ROWCOUNT);

-- 4) BOP — INJ 스테이션 × 코어, IMG 스테이션 × ASSY
INSERT INTO dbo.MD_Bop (BOPID, ItemNo, RoutingType, StepSeq, StationCode, StdCycleTime, ActiveFlag, CreatedBy, CreatedTS)
SELECT 'P' + LEFT(REPLACE(CONVERT(varchar(36), NEWID()), '-', ''), 23), m.CoreItemNo, 'A', 10, m.InjStation, 60, 1, 'SEED-DEMO', SYSDATETIME()
FROM   #mold m WHERE NOT EXISTS (SELECT 1 FROM dbo.MD_Bop b WHERE b.ItemNo = m.CoreItemNo AND b.StationCode = m.InjStation);
PRINT CONCAT(N'§4 MD_Bop INJ(코어): ', @@ROWCOUNT);

INSERT INTO dbo.MD_Bop (BOPID, ItemNo, RoutingType, StepSeq, StationCode, StdCycleTime, ActiveFlag, CreatedBy, CreatedTS)
SELECT 'P' + LEFT(REPLACE(CONVERT(varchar(36), NEWID()), '-', ''), 23), c.Assy, 'A', 20,
       ('ST-IMG-' + RIGHT('0' + CAST(l.ImgNo AS varchar(2)), 2)) COLLATE DATABASE_DEFAULT, 90, 1, 'SEED-DEMO', SYSDATETIME()
FROM   #core c JOIN dbo.MD_Item a ON a.ItemNo = c.Assy JOIN #line l ON l.CarType = a.CarType
WHERE  NOT EXISTS (SELECT 1 FROM dbo.MD_Bop b WHERE b.ItemNo = c.Assy AND b.StationCode = ('ST-IMG-' + RIGHT('0' + CAST(l.ImgNo AS varchar(2)), 2)) COLLATE DATABASE_DEFAULT);
PRINT CONCAT(N'§4 MD_Bop IMG(완제품): ', @@ROWCOUNT);

COMMIT;

-- 확인
SELECT l.CarType, l.InjNo, l.ImgNo,
       (SELECT COUNT(*) FROM #mold m WHERE m.CarType = l.CarType) AS Molds,
       (SELECT COUNT(*) FROM #core c JOIN dbo.MD_Item a ON a.ItemNo = c.Assy WHERE a.CarType = l.CarType) AS Assys
FROM   #line l ORDER BY l.InjNo, l.CarType;
GO
