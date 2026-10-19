-- ════════════════════════════════════════════════════════════════════════
--  test_pp_pop_reset_dev.sql  (개발 DB 전용 — 운영 금지)
--  PP(생산계획) → POP(INJ·IMG) 흐름 시험용 초기화 + 시험 데이터 생성.
--
--  무엇을 하나 (전체가 트랜잭션 하나, Mode=ROLLBACK 이면 결과만 보고 되돌린다)
--   §1 품번 선택  : 라우팅 A(INJ → IMG) 완제품 중 ① 1캐비티 금형 코어의 완제품 1개 ② 2캐비티 금형(LH/RH)
--                  코어의 완제품 LH·RH 1쌍 — 마스터가 가장 완전한 후보를 자동으로 고르되 두 금형은 다른 사출 라인을 우선한다(아래 :setvar 로 금형 고정 가능).
--   §2 마스터 점검: 고객·라우팅·BOM 코어·금형/UPH·BOP 스테이션·사이클·라인/스테이션/LotPrefix·가동 시간 패턴·
--                  공장 달력·SYS_Config·교대·APS 기본 패턴·불량코드/원인·REWORK 라인·작업자·라인 슈퍼바이저·IMG 라벨 항목.
--                  FAIL 이 하나라도 있으면 THROW(아무것도 바꾸지 않음), WARN 은 목록으로만 보인다.
--                  자동 보정 세 가지: ① 선택 금형의 MD_MoldItem.Color 가 2자 검정 'BK' 면 'CBK' 로 바꾼다(InjAgent 는 PLC 금형
--                  문자열 끝 3자를 색상으로 읽는다 — 10-14 사용자 결정 black = CBK; 그 밖의 3자 아닌 색상은 자동 선택에서 뒤로 미루고
--                  고를 수밖에 없으면 FAIL) ② 선택 금형 코어의 PalletQty 가 비면 그 코어를 쓰는 완제품 PalletQty 의 최소값(없으면 8)
--                  으로 채운다(seed_aps_dev.sql ③ 규칙) ③ 선택 라인에 활성 슈퍼바이저가 없으면 슈퍼바이저가 있는 첫 라인의 것을
--                  복사한다(없으면 안돈이 OPEN 에서 못 벗어난다). 모두 트랜잭션 안이라 FAIL·ROLLBACK 이면 되돌아간다.
--   §3 초기화    : PP 계획 데이터 전부(예측·일별계획·공급계획·수주·WO·단계·WO/MC 슬롯·APS 실행·MRP·구매요청) +
--                  생산 데이터(INJ/IMG/FINAL LOT·사출/감싸기 LOT·실적·불량·접수·안돈·비가동·안돈 고장·빈 LotSeq).
--                  남기는 것: PM 슬롯, 창고·FG·QC·PNT·MNT 데이터, POP 세션/인증 로그, 금형 샷·교체 이력.
--   §4 생성      : 실행일(DB 날짜) 기준 — 주간 예측(PP_Forecast, SRM_WEEKLY, 이번 주 월요일부터 8주) +
--                  확정 수주(PP_CustomerOrder Confirmed, 품번마다 3건, 납기 = 오늘 뒤 1·2·3 번째 근무일 — 실행일부터 2~3일 안에
--                  소화할 수주, 수량 = 적입수량 × 2/3/4). 생산 마감(납기 − 버퍼 근무일)은 이미 지난 날이라 PP-003 은 오늘부터
--                  납기까지 Late 로 배치한다 — 바로 POP 에 계획이 뜨게 하려는 것. 재실행하면 §3 가 직전 시험 데이터를 지우므로 날짜만 다시 잡힌다.
--
--  이 뒤의 시험 순서(화면): PP-001 예측 확인 → PP-002 수주 확인(이미 Confirmed) → PP-003 선택 → WO 일괄 생성 →
--   PP-LSB 보드 확인(적용) → POP INJ 라인 로그인 → 수동 라벨 생성·스캔 확정·불량·안돈 → POP IMG 라인 Core 스캔 → 판정.
--
--  실행:
--    sqlcmd -S <서버> -d AMES_DEV -U ames_app -P <pw> -C -f 65001 -I -b -v Mode=ROLLBACK -i tools\test_pp_pop\test_pp_pop_reset_dev.sql
--    → 점검·건수 확인 후 -v Mode=COMMIT
-- ════════════════════════════════════════════════════════════════════════
:setvar Mold1 ""
:setvar Mold2 ""
-- Mold1 = 1캐비티 금형 ID, Mold2 = 2캐비티 금형 ID. 빈 값이면 자동 선택. (sqlcmd -v 는 :setvar 보다 우선순위가 낮아 여기서 고친다)

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
IF '$(Mode)' NOT IN ('ROLLBACK', 'COMMIT') THROW 50200, N'Mode must be ROLLBACK or COMMIT (sqlcmd -v Mode=...)', 1;
PRINT CONCAT(N'Target: ', @@SERVERNAME, N' / ', DB_NAME(), N' / Mode=$(Mode) / DB time ', CONVERT(varchar(19), SYSDATETIME(), 120));

DECLARE @Actor varchar(20) = 'TEST-PPPOP';
DECLARE @Today date = CAST(SYSDATETIME() AS date);
DECLARE @Now datetime2(7) = SYSDATETIME();
DECLARE @Mold1 varchar(20) = NULLIF(LTRIM(RTRIM('$(Mold1)')), '');
DECLARE @Mold2 varchar(20) = NULLIF(LTRIM(RTRIM('$(Mold2)')), '');

BEGIN TRAN;

-- ─────────────────────────────────────────────────────────────────────
-- §1 품번 선택
-- ─────────────────────────────────────────────────────────────────────
-- 1-a) ASSY → 코어 (AMES.Data.Services.CoreItemResolver 규칙의 SQL 판 — seed_inj_img_master_dev 와 동일)
IF OBJECT_ID('tempdb..#core') IS NOT NULL DROP TABLE #core;
CREATE TABLE #core (Assy varchar(20) COLLATE DATABASE_DEFAULT NOT NULL, CoreItemNo varchar(20) COLLATE DATABASE_DEFAULT NOT NULL, NCores int NOT NULL);
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
    WHERE  a.ItemType = 'ASSY' AND ISNULL(a.ActiveFlag,1) = 1 AND a.RoutingType = 'A'
    UNION ALL
    SELECT w.Assy, s.CompItemNo, w.Lvl + 1, CAST(w.Path + s.CompItemNo + '|' AS varchar(400))
    FROM   walk w JOIN subs s ON s.ParentItemNo = w.Node
    WHERE  w.Lvl < 8 AND w.Path NOT LIKE '%|' + s.CompItemNo + '|%'),
cores AS (SELECT DISTINCT w.Assy, w.Node FROM walk w JOIN dbo.MD_Item i ON i.ItemNo = w.Node WHERE LTRIM(i.ItemName) LIKE 'CORE%')
INSERT INTO #core (Assy, CoreItemNo, NCores)
SELECT c.Assy, c.Node, (SELECT COUNT(*) FROM cores x WHERE x.Assy = c.Assy) FROM cores c;

-- 1-b) 후보표: 완제품 × 코어 × 금형 + 마스터 완전성 점수
IF OBJECT_ID('tempdb..#cand') IS NOT NULL DROP TABLE #cand;
CREATE TABLE #cand (
    Assy       varchar(20) COLLATE DATABASE_DEFAULT NOT NULL,
    CoreItemNo varchar(20) COLLATE DATABASE_DEFAULT NOT NULL,
    MoldID     varchar(20) COLLATE DATABASE_DEFAULT NOT NULL,
    CavityCount int NOT NULL,
    InjStation varchar(20) COLLATE DATABASE_DEFAULT NULL,
    InjLine    varchar(20) COLLATE DATABASE_DEFAULT NULL,
    InjCycle   decimal(8,2) NULL,
    ImgStation varchar(20) COLLATE DATABASE_DEFAULT NULL,
    ImgLine    varchar(20) COLLATE DATABASE_DEFAULT NULL,
    ImgCycle   decimal(8,2) NULL,
    Uph        decimal(10,4) NULL,
    MoldCode   varchar(20) COLLATE DATABASE_DEFAULT NULL,
    Color      varchar(10) COLLATE DATABASE_DEFAULT NULL,
    Score      int NOT NULL);
INSERT INTO #cand
SELECT c.Assy, c.CoreItemNo, mi.MoldID, m.CavityCount,
       bi.StationCode, bi.LineID, bi.StdCycleTime,
       bm.StationCode, bm.LineID, bm.StdCycleTime,
       ml.UPH, m.MoldCodeClean, mi.Color,
         (CASE WHEN a.PalletQty IS NOT NULL THEN 1 ELSE 0 END)
       + (CASE WHEN a.PGN IS NOT NULL AND a.ALC IS NOT NULL THEN 1 ELSE 0 END)
       + (CASE WHEN bi.StdCycleTime IS NOT NULL THEN 1 ELSE 0 END)
       + (CASE WHEN bm.StdCycleTime IS NOT NULL THEN 1 ELSE 0 END)
       + (CASE WHEN ml.UPH > 0 THEN 1 ELSE 0 END)
       + (CASE WHEN ci.InjFlag = 1 THEN 1 ELSE 0 END)
       + (CASE WHEN EXISTS (SELECT 1 FROM dbo.MD_LineSupervisor s WHERE s.LineID = bi.LineID AND s.ActiveFlag = 1) THEN 1 ELSE 0 END)
       + (CASE WHEN EXISTS (SELECT 1 FROM dbo.MD_LineSupervisor s WHERE s.LineID = bm.LineID AND s.ActiveFlag = 1) THEN 1 ELSE 0 END)
FROM   #core c
JOIN   dbo.MD_Item a  ON a.ItemNo  = c.Assy
JOIN   dbo.MD_Item ci ON ci.ItemNo = c.CoreItemNo
JOIN   dbo.MD_MoldItem mi ON mi.ItemNo = c.CoreItemNo AND mi.ActiveFlag = 1
JOIN   dbo.MD_Mold m      ON m.MoldID = mi.MoldID
OUTER APPLY (SELECT TOP 1 b.StationCode, st.LineID, b.StdCycleTime
             FROM dbo.MD_Bop b JOIN dbo.MD_Station st ON st.StationCode = b.StationCode
             JOIN dbo.MD_Line l ON l.LineID = st.LineID JOIN dbo.MD_WorkCenter wc ON wc.WCID = l.WCID
             WHERE b.ItemNo = c.CoreItemNo AND b.RoutingType = 'A' AND ISNULL(b.ActiveFlag,1) = 1 AND wc.ProcessCode = 'INJ'
               AND ISNULL(l.Status,'ACTIVE') <> 'INACTIVE'
             ORDER BY b.StepSeq) bi
OUTER APPLY (SELECT TOP 1 b.StationCode, st.LineID, b.StdCycleTime
             FROM dbo.MD_Bop b JOIN dbo.MD_Station st ON st.StationCode = b.StationCode
             JOIN dbo.MD_Line l ON l.LineID = st.LineID JOIN dbo.MD_WorkCenter wc ON wc.WCID = l.WCID
             WHERE b.ItemNo = c.Assy AND b.RoutingType = 'A' AND ISNULL(b.ActiveFlag,1) = 1 AND wc.ProcessCode = 'IMG'
               AND ISNULL(l.Status,'ACTIVE') <> 'INACTIVE'
             ORDER BY b.StepSeq) bm
LEFT JOIN dbo.MD_MoldLine ml ON ml.MoldID = mi.MoldID AND ml.LineCode = bi.LineID
WHERE  c.NCores = 1 AND m.Status = 'AVAILABLE';

-- 1-c) 금형 선택: 캐비티마다(코어마다) 완제품이 있어야 하고, 코어별 최고 점수 합이 큰 금형 → 동률이면 MoldID 순.
--      두 금형은 **다른 사출 라인**을 우선한다(시험이 두 라인에 걸치게 — 한쪽이 고정돼 있으면 그 라인과 다른 쪽을 고른다).
--      첫 실행이 복사한 슈퍼바이저 때문에 재실행 때 점수가 같아져 같은 라인의 금형으로 몰리던 것을 막는다.
IF OBJECT_ID('tempdb..#moldScore') IS NOT NULL DROP TABLE #moldScore;
--      ColorOk = 코어 매핑 색상이 모두 3자이거나 자동 보정되는 'BK' — 아니면 InjAgent 가 LOT 를 못 만들므로 맨 뒤로 미룬다.
SELECT MoldID, CavityCount, COUNT(DISTINCT CoreItemNo) AS Cores, SUM(BestScore) AS Score, MIN(InjLine) AS InjLine, MIN(ColorOk) AS ColorOk
INTO   #moldScore
FROM   (SELECT MoldID, CavityCount, CoreItemNo, MAX(Score) AS BestScore, MIN(InjLine) AS InjLine,
               MIN(CASE WHEN LEN(ISNULL(Color, '')) = 3 OR RTRIM(Color) = 'BK' THEN 1 ELSE 0 END) AS ColorOk
        FROM #cand GROUP BY MoldID, CavityCount, CoreItemNo) x
GROUP  BY MoldID, CavityCount;

DECLARE @Line1 varchar(20) = (SELECT TOP 1 InjLine FROM #moldScore WHERE MoldID = @Mold1);
DECLARE @Line2 varchar(20) = (SELECT TOP 1 InjLine FROM #moldScore WHERE MoldID = @Mold2);
IF @Mold1 IS NULL
    SELECT TOP 1 @Mold1 = MoldID, @Line1 = InjLine FROM #moldScore WHERE CavityCount = 1 AND Cores = 1
    ORDER BY ColorOk DESC, CASE WHEN @Line2 IS NOT NULL AND InjLine = @Line2 THEN 1 ELSE 0 END, Score DESC, MoldID;
IF @Mold2 IS NULL
    SELECT TOP 1 @Mold2 = MoldID, @Line2 = InjLine FROM #moldScore WHERE CavityCount = 2 AND Cores = 2
    ORDER BY ColorOk DESC, CASE WHEN @Line1 IS NOT NULL AND InjLine = @Line1 THEN 1 ELSE 0 END, Score DESC, MoldID;
IF @Mold1 IS NULL THROW 50201, N'No 1-cavity mold candidate - a routing-A finished item core (BOM, name CORE*) needs an active MD_MoldItem on an AVAILABLE mold with CavityCount 1', 1;
IF @Mold2 IS NULL THROW 50202, N'No 2-cavity mold candidate - both cores need a routing-A finished item', 1;
IF NOT EXISTS (SELECT 1 FROM #moldScore WHERE MoldID = @Mold1 AND CavityCount = 1 AND Cores = 1) THROW 50203, N'Mold1 is not a 1-cavity candidate (check mold status / core mapping)', 1;
IF NOT EXISTS (SELECT 1 FROM #moldScore WHERE MoldID = @Mold2 AND CavityCount = 2 AND Cores = 2) THROW 50204, N'Mold2 is not a 2-cavity candidate (both cores need a finished item)', 1;

-- 1-d) 자동 보정 ①: 선택 금형의 2자 검정 'BK' → 'CBK' (PK = MoldID·ItemNo 라 색상만 바꿔도 겹치지 않는다)
UPDATE dbo.MD_MoldItem SET Color = 'CBK', ModifiedBy = @Actor, ModifiedTS = @Now
WHERE  MoldID IN (@Mold1, @Mold2) AND RTRIM(Color) = 'BK';
DECLARE @ColorFixed int = @@ROWCOUNT;
UPDATE #cand SET Color = 'CBK' WHERE MoldID IN (@Mold1, @Mold2) AND RTRIM(Color) = 'BK';

-- 1-d') 자동 보정 ②: 선택 금형 코어의 PalletQty(APS 포장 단위)가 비면 그 코어를 쓰는 완제품 PalletQty 의 최소값, 완제품에도 없으면 8
--       (seed_aps_dev.sql ③ 과 같은 규칙 — 완제품 수주가 적입수량 배수로 오므로 코어도 같은 단위면 APS 포장 올림이 자투리를 안 만든다)
UPDATE ci SET PalletQty = COALESCE(pp.PalletQty, 8), ModifiedBy = @Actor, ModifiedTS = @Now
FROM   dbo.MD_Item ci
OUTER APPLY (SELECT MIN(a.PalletQty) AS PalletQty FROM #core c JOIN dbo.MD_Item a ON a.ItemNo = c.Assy
             WHERE c.CoreItemNo = ci.ItemNo AND a.PalletQty > 0) pp
WHERE  ci.ItemNo IN (SELECT ItemNo FROM dbo.MD_MoldItem WHERE MoldID IN (@Mold1, @Mold2) AND ActiveFlag = 1)
  AND  ISNULL(ci.PalletQty, 0) <= 0;
DECLARE @PalletFixed int = @@ROWCOUNT;

-- 1-e) 코어마다 완제품 1개 (점수 → 품번순)
IF OBJECT_ID('tempdb..#pick') IS NOT NULL DROP TABLE #pick;
SELECT Kind, Assy, CoreItemNo, MoldID, CavityCount, InjStation, InjLine, InjCycle, ImgStation, ImgLine, ImgCycle, Uph, MoldCode, Color,
       MoldCode + Color AS PlcMoldString
INTO   #pick
FROM   (SELECT CASE WHEN c.MoldID = @Mold1 THEN 'CAV1' ELSE 'CAV2' END AS Kind, c.*,
               ROW_NUMBER() OVER (PARTITION BY c.MoldID, c.CoreItemNo ORDER BY c.Score DESC, c.Assy) AS Rn
        FROM   #cand c WHERE c.MoldID IN (@Mold1, @Mold2)) x
WHERE  Rn = 1;

PRINT N'== 1) Selected items';
SELECT p.Kind, p.MoldID, p.CavityCount, p.PlcMoldString, p.CoreItemNo, p.Assy, a.ItemName, a.CarType, a.PalletQty, p.InjLine, p.InjStation, p.InjCycle, p.Uph, p.ImgLine, p.ImgStation, p.ImgCycle
FROM   #pick p JOIN dbo.MD_Item a ON a.ItemNo = p.Assy ORDER BY p.Kind, p.Assy;

-- 선택 라인 (INJ·IMG)
IF OBJECT_ID('tempdb..#line') IS NOT NULL DROP TABLE #line;
SELECT DISTINCT LineID, ProcCode INTO #line FROM (SELECT InjLine AS LineID, 'INJ' AS ProcCode FROM #pick UNION ALL SELECT ImgLine, 'IMG' FROM #pick) x WHERE LineID IS NOT NULL;

-- ─────────────────────────────────────────────────────────────────────
-- §2 마스터 점검 — FAIL 은 중단, WARN 은 안내
-- ─────────────────────────────────────────────────────────────────────
IF OBJECT_ID('tempdb..#chk') IS NOT NULL DROP TABLE #chk;
CREATE TABLE #chk (Seq int IDENTITY(1,1), Level varchar(4) NOT NULL, Area nvarchar(30) NOT NULL, Msg nvarchar(400) NOT NULL);

-- 고객 (수주 귀속 규칙 = seed_pp_customer_order_dev 와 같다)
IF OBJECT_ID('tempdb..#cust') IS NOT NULL DROP TABLE #cust;
CREATE TABLE #cust (CarType varchar(10) COLLATE DATABASE_DEFAULT PRIMARY KEY, CustomerID varchar(20) COLLATE DATABASE_DEFAULT NOT NULL);
INSERT INTO #cust VALUES ('NE1A','CUS-SAV'), ('ME1A','CUS-SAV'), ('MV1A','CUS-SAV'), ('NQ5A','CUS-GEO'), ('LQ2','CUS-GEO'), ('LX3A','CUS-AUB'), ('NX5A','CUS-AUB');
INSERT INTO #chk SELECT 'FAIL', N'Customer', CONCAT(N'MD_Customer ', c.CustomerID, N' missing or inactive - seed_md_customer_seoyon.sql')
FROM   (SELECT DISTINCT cu.CustomerID FROM #pick p JOIN dbo.MD_Item a ON a.ItemNo = p.Assy JOIN #cust cu ON cu.CarType = a.CarType) c
WHERE  NOT EXISTS (SELECT 1 FROM dbo.MD_Customer m WHERE m.CustomerID = c.CustomerID AND ISNULL(m.Status,'ACTIVE') = 'ACTIVE');
INSERT INTO #chk SELECT 'FAIL', N'Customer', CONCAT(p.Assy, N': no customer mapping for car type ', ISNULL(a.CarType,'(NULL)'), N' (add to #cust)')
FROM   #pick p JOIN dbo.MD_Item a ON a.ItemNo = p.Assy WHERE NOT EXISTS (SELECT 1 FROM #cust cu WHERE cu.CarType = a.CarType);

-- 라우팅 A
IF NOT EXISTS (SELECT 1 FROM dbo.MD_RoutingStep WHERE RoutingType = 'A' AND ProcessCode = 'INJ' AND ActiveFlag = 1)
   OR NOT EXISTS (SELECT 1 FROM dbo.MD_RoutingStep WHERE RoutingType = 'A' AND ProcessCode = 'IMG' AND ActiveFlag = 1)
    INSERT INTO #chk VALUES ('FAIL', N'Routing', N'MD_RoutingStep routing A lacks INJ/IMG steps - seed_md_routing_step.sql / MD-029');

-- 완제품·코어
INSERT INTO #chk SELECT 'FAIL', N'Item', CONCAT(p.Assy, N': not an active ASSY with RoutingType A')
FROM   #pick p JOIN dbo.MD_Item a ON a.ItemNo = p.Assy WHERE a.ItemType <> 'ASSY' OR a.RoutingType <> 'A' OR ISNULL(a.ActiveFlag,1) <> 1;
INSERT INTO #chk SELECT 'WARN', N'Item', CONCAT(p.Assy, N': PalletQty is NULL - APS pack unit falls back to ROUND_TO, test order qty uses 20 (MD-003)')
FROM   #pick p JOIN dbo.MD_Item a ON a.ItemNo = p.Assy WHERE a.PalletQty IS NULL;
INSERT INTO #chk SELECT 'WARN', N'Item', CONCAT(p.Assy, N': PGN/ALC/MountPos empty - IMG label S token / mount position will be blank (MD-003)')
FROM   #pick p JOIN dbo.MD_Item a ON a.ItemNo = p.Assy WHERE a.PGN IS NULL OR a.ALC IS NULL OR a.MountPos IS NULL;
INSERT INTO #chk SELECT 'WARN', N'Item', CONCAT(p.Assy, N': DefaultUOM ', ISNULL(a.DefaultUOM,'(NULL)'), N' not in MD_Uom - qty decimals fall back to default')
FROM   #pick p JOIN dbo.MD_Item a ON a.ItemNo = p.Assy WHERE NOT EXISTS (SELECT 1 FROM dbo.MD_Uom u WHERE u.UomCode = a.DefaultUOM);
IF @PalletFixed > 0
    INSERT INTO #chk SELECT 'OK', N'Item', CONCAT(N'Core PalletQty was empty - set ', STRING_AGG(CONCAT(ci.ItemNo, N'=', ci.PalletQty), N', '), N' (finished-item minimum, else 8; check in MD-003)')
    FROM   dbo.MD_Item ci WHERE ci.ItemNo IN (SELECT ItemNo FROM dbo.MD_MoldItem WHERE MoldID IN (@Mold1, @Mold2) AND ActiveFlag = 1)
      AND  ci.ModifiedBy = @Actor AND ci.ModifiedTS = @Now;
INSERT INTO #chk SELECT 'WARN', N'Item', CONCAT(p.CoreItemNo, N': core PalletQty is NULL - APS treats the pack unit as 1 (MD-003 / dev: seed_aps_dev.sql section 3)')
FROM   #pick p JOIN dbo.MD_Item ci ON ci.ItemNo = p.CoreItemNo WHERE ISNULL(ci.PalletQty, 0) <= 0;
INSERT INTO #chk SELECT 'WARN', N'Item', CONCAT(p.CoreItemNo, N': core InjFlag=0 - fine for PP-003, but PP-APS BOM rule will not treat it as injection (MD-003)')
FROM   #pick p JOIN dbo.MD_Item ci ON ci.ItemNo = p.CoreItemNo WHERE ci.InjFlag = 0;
INSERT INTO #chk SELECT 'FAIL', N'Item', CONCAT(p.CoreItemNo, N': core item is inactive')
FROM   #pick p JOIN dbo.MD_Item ci ON ci.ItemNo = p.CoreItemNo WHERE ISNULL(ci.ActiveFlag,1) <> 1;

-- 금형 (InjAgent PlcCodec.SplitMoldColor 는 PLC 금형 문자열 끝 3자를 색상으로 읽는다 — 3자가 아니면 어떤 문자열도 매칭되지 않아 LOT 가 안 생긴다)
IF @ColorFixed > 0
    INSERT INTO #chk VALUES ('OK', N'Mold', CONCAT(N'Selected molds had 2-char black ''BK'' - changed to ''CBK'' (', @ColorFixed, N' MD_MoldItem rows, check in MD-007)'));
INSERT INTO #chk SELECT 'FAIL', N'Mold', CONCAT(p.MoldID, N' / ', p.CoreItemNo, N': MD_MoldItem.Color ''', p.Color, N''' is not 3 chars - InjAgent reads the last 3 chars of the PLC mold string as color, so no LOT can be created (MD-007; black = CBK)')
FROM   #pick p WHERE LEN(ISNULL(p.Color, '')) <> 3;
INSERT INTO #chk SELECT 'FAIL', N'Mold', CONCAT(p.CoreItemNo, N': mold ', p.MoldID, N' has no MD_MoldLine (UPH>0) on INJ BOP line ', p.InjLine, N' - MD-007 line assignment')
FROM   #pick p WHERE p.InjLine IS NOT NULL AND ISNULL(p.Uph,0) <= 0;
INSERT INTO #chk SELECT 'WARN', N'Mold', CONCAT(p.MoldID, N': MoldChangeMin is NULL - mold change (MC) planned as 0 min')
FROM   (SELECT DISTINCT MoldID FROM #pick) p JOIN dbo.MD_Mold m ON m.MoldID = p.MoldID WHERE ISNULL(m.MoldChangeMin,0) <= 0;

-- BOP (INJ 스테이션 × 코어, IMG 스테이션 × 완제품)
INSERT INTO #chk SELECT 'FAIL', N'BOP', CONCAT(p.CoreItemNo, N': no routing-A BOP on a station of an active INJ line - MD-005 (dev: seed_inj_img_master_dev.sql)')
FROM   #pick p WHERE p.InjStation IS NULL;
INSERT INTO #chk SELECT 'FAIL', N'BOP', CONCAT(p.Assy, N': no routing-A BOP on a station of an active IMG line - MD-005 (dev: seed_inj_img_master_dev.sql)')
FROM   #pick p WHERE p.ImgStation IS NULL;
INSERT INTO #chk SELECT 'WARN', N'BOP', CONCAT(p.CoreItemNo, N': INJ BOP StdCycleTime is NULL - PP-003 places the INJ step by mold UPH, but PP-004 std cycle will be blank')
FROM   #pick p WHERE p.InjStation IS NOT NULL AND p.InjCycle IS NULL;
INSERT INTO #chk SELECT 'FAIL', N'BOP', CONCAT(p.Assy, N': IMG BOP StdCycleTime is NULL - finished-step capacity (cycle x operating min) cannot be computed')
FROM   #pick p WHERE p.ImgStation IS NOT NULL AND p.ImgCycle IS NULL;
INSERT INTO #chk SELECT 'WARN', N'BOP', CONCAT(p.Assy, N': IMG BOP exists on several stations - PP-003 uses the lowest StepSeq, ', p.ImgStation)
FROM   #pick p WHERE (SELECT COUNT(DISTINCT b.StationCode) FROM dbo.MD_Bop b WHERE b.ItemNo = p.Assy AND b.RoutingType = 'A' AND ISNULL(b.ActiveFlag,1) = 1) > 1;

-- 라인·스테이션·LotPrefix
INSERT INTO #chk SELECT 'FAIL', N'Line', CONCAT(l.LineID, N': MD_Line missing/inactive or WC ProcessCode is not ', l.ProcCode)
FROM   #line l WHERE NOT EXISTS (SELECT 1 FROM dbo.MD_Line x JOIN dbo.MD_WorkCenter wc ON wc.WCID = x.WCID
                                 WHERE x.LineID = l.LineID AND ISNULL(x.Status,'ACTIVE') = 'ACTIVE' AND wc.ProcessCode = l.ProcCode);
INSERT INTO #chk SELECT 'FAIL', N'Line', CONCAT(l.LineID, N': LotPrefix is NULL - lot numbering will throw (migrate_lotno_rule / MD-002)')
FROM   #line l JOIN dbo.MD_Line x ON x.LineID = l.LineID WHERE NULLIF(RTRIM(x.LotPrefix), '') IS NULL;
INSERT INTO #chk SELECT 'FAIL', N'Line', CONCAT(l.LineID, N': no active MD_Station - POP login cannot pick a station')
FROM   #line l WHERE NOT EXISTS (SELECT 1 FROM dbo.MD_Station s WHERE s.LineID = l.LineID AND ISNULL(s.Status,'ACTIVE') = 'ACTIVE');
INSERT INTO #chk SELECT 'WARN', N'Line', CONCAT(l.LineID, N': no MD_Equipment - injection board / OEE / MNT screens show no equipment for this line')
FROM   #line l WHERE NOT EXISTS (SELECT 1 FROM dbo.MD_Equipment e WHERE e.LineID = l.LineID);

-- 가동 시간 패턴 (PP-LSB·PP-003 능력: 라인 전용 ACTIVE → 전역 ACTIVE, OPERATING 세그먼트 필요)
INSERT INTO #chk SELECT 'FAIL', N'Pattern', CONCAT(l.LineID, N': no applicable ACTIVE line time pattern (line-specific or global, in effect, with OPERATING segments) - MD-028')
FROM   #line l WHERE NOT EXISTS (
    SELECT 1 FROM dbo.MD_LineTimePattern p
    WHERE  (p.LineID = l.LineID OR p.LineID IS NULL) AND p.Status = 'ACTIVE'
      AND  (p.EffectiveFrom IS NULL OR p.EffectiveFrom <= @Today) AND (p.EffectiveTo IS NULL OR p.EffectiveTo >= @Today)
      AND  EXISTS (SELECT 1 FROM dbo.MD_LineTimeSegment s WHERE s.PatternID = p.PatternID AND s.SegmentState = 'OPERATING' AND s.EndMin > s.StartMin));
INSERT INTO #chk SELECT 'WARN', N'Pattern', CONCAT(p.PatternID, N': segments do not sum to 1440 min (', SUM(s.EndMin - s.StartMin), N') - open and save in MD-028')
FROM   dbo.MD_LineTimePattern p JOIN dbo.MD_LineTimeSegment s ON s.PatternID = p.PatternID
WHERE  p.Status = 'ACTIVE' AND (p.LineID IS NULL OR p.LineID IN (SELECT LineID FROM #line))
GROUP  BY p.PatternID HAVING SUM(s.EndMin - s.StartMin) <> 1440;

-- 공장 달력 (근무일 판정 = WorkdayCalendar: WORKDAY·SPECIAL 행이 근무일, 행이 없는 날은 토·일만 휴일)
IF OBJECT_ID('tempdb..#cal') IS NOT NULL DROP TABLE #cal;
CREATE TABLE #cal (D date PRIMARY KEY, IsWork bit NOT NULL, HasRow bit NOT NULL);
;WITH n AS (SELECT TOP 110 ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) - 41 AS i FROM sys.all_objects)   -- 오늘 −40 ~ +69 일
INSERT INTO #cal (D, IsWork, HasRow)
SELECT d.D,
       CASE WHEN EXISTS (SELECT 1 FROM dbo.SYS_FactoryCalendar c WHERE c.CalendarDate = d.D)
            THEN CASE WHEN EXISTS (SELECT 1 FROM dbo.SYS_FactoryCalendar c WHERE c.CalendarDate = d.D AND c.DayType IN ('WORKDAY','SPECIAL')) THEN 1 ELSE 0 END
            ELSE CASE WHEN DATEDIFF(day, '19000101', d.D) % 7 IN (5, 6) THEN 0 ELSE 1 END END,
       CASE WHEN EXISTS (SELECT 1 FROM dbo.SYS_FactoryCalendar c WHERE c.CalendarDate = d.D) THEN 1 ELSE 0 END
FROM   (SELECT DATEADD(day, i, @Today) AS D FROM n) d;
DECLARE @noRow int = (SELECT COUNT(*) FROM #cal WHERE HasRow = 0 AND D >= @Today AND D < DATEADD(day, 30, @Today));
IF @noRow > 0
    INSERT INTO #chk VALUES ('WARN', N'Calendar', CONCAT(N'SYS_FactoryCalendar has no row for ', @noRow, N' of the next 30 days - holidays count as workdays (EXEC dbo.SP_SYS_FactoryCalendar_Fill @Months=3 / SYS-005)'));
IF (SELECT COUNT(*) FROM #cal WHERE IsWork = 1 AND D > @Today) < 20
    INSERT INTO #chk VALUES ('FAIL', N'Calendar', N'Fewer than 20 workdays in the next 70 days - cannot place due dates');

-- SYS_Config · 교대 · 전기일
DECLARE @BufferRaw nvarchar(500) = (SELECT TOP 1 ConfigValue FROM dbo.SYS_Config WHERE ConfigKey = 'PP_PROD_BUFFER_WORKDAYS');
DECLARE @Buffer int = CASE WHEN TRY_CAST(@BufferRaw AS int) >= 1 THEN CAST(@BufferRaw AS int) ELSE 3 END;   -- PpRepository.BufferWorkdaysDefault 와 같은 규칙
IF @BufferRaw IS NULL
    INSERT INTO #chk VALUES ('WARN', N'Config', N'SYS_Config PP_PROD_BUFFER_WORKDAYS missing - default 3 workdays (migrate_wo_prod_deadline.sql)');
IF NOT EXISTS (SELECT 1 FROM dbo.SYS_Config WHERE ConfigKey = 'DAY_CUTOFF_TIME')
    INSERT INTO #chk VALUES ('WARN', N'Config', N'SYS_Config DAY_CUTOFF_TIME missing - production day boundary falls back to midnight (migrate_day_cutoff_config.sql)');
IF NOT EXISTS (SELECT 1 FROM dbo.MD_CodeItem WHERE GroupCode = 'WORK_SHIFT' AND UseFlag = 1 AND Attribute1 LIKE '____-____')
    INSERT INTO #chk VALUES ('WARN', N'Config', N'Common code WORK_SHIFT has no Attribute1 window (HHMM-HHMM) - result ShiftCode stays NULL (MD-030)');

-- APS (PP-003 에는 필요 없고 PP-APS 조회 조건)
DECLARE @DefPat nvarchar(200) = (SELECT TOP 1 Attribute1 FROM dbo.MD_CodeItem WHERE GroupCode = 'APS_SETTING' AND CodeValue = 'DEFAULT_PATTERN' AND UseFlag = 1);
INSERT INTO #chk SELECT 'WARN', N'APS', CONCAT(l.LineID, N': no APS time pattern - neither MD_ApsLineStage.PatternID nor APS_SETTING.DEFAULT_PATTERN (now ''', ISNULL(@DefPat,''), N''') is an ACTIVE pattern, so PP-APS query is blocked')
FROM   #line l WHERE l.ProcCode = 'INJ'
  AND  NOT EXISTS (SELECT 1 FROM dbo.MD_ApsLineStage s JOIN dbo.MD_LineTimePattern p ON p.PatternID = s.PatternID AND p.Status = 'ACTIVE' WHERE s.LineID = l.LineID)
  AND  NOT EXISTS (SELECT 1 FROM dbo.MD_LineTimePattern p WHERE p.PatternID = @DefPat AND p.Status = 'ACTIVE');

-- POP 부속: 불량코드·원인·REWORK·작업자·슈퍼바이저
INSERT INTO #chk SELECT 'WARN', N'POP', CONCAT(N'No active MD_DefectCode for ', pc.ProcCode, N' (or common) - defect popup has nothing to pick (MD-013)')
FROM   (SELECT 'INJ' AS ProcCode UNION ALL SELECT 'IMG') pc
WHERE  NOT EXISTS (SELECT 1 FROM dbo.MD_DefectCode d WHERE d.ActiveFlag = 1 AND (d.ProcessCode = pc.ProcCode OR d.ProcessCode IS NULL));
IF NOT EXISTS (SELECT 1 FROM dbo.MD_DefectCause WHERE ActiveFlag = 1)
    INSERT INTO #chk VALUES ('WARN', N'POP', N'No active MD_DefectCause - REWORK decision buttons are disabled (MD-013 / dev: seed_rework_dev.sql)');
IF NOT EXISTS (SELECT 1 FROM dbo.MD_Line l JOIN dbo.MD_WorkCenter wc ON wc.WCID = l.WCID WHERE wc.ProcessCode = 'RWK' AND ISNULL(l.Status,'ACTIVE') = 'ACTIVE'
               AND EXISTS (SELECT 1 FROM dbo.MD_Station s WHERE s.LineID = l.LineID))
    INSERT INTO #chk VALUES ('WARN', N'POP', N'No RWK line (WC ProcessCode RWK) with a station - REWORK login impossible (migrate_lot_defect_rework.sql)');
IF NOT EXISTS (SELECT 1 FROM dbo.MD_Worker WHERE ActiveFlag = 1 AND PinHash IS NOT NULL)
   AND NOT EXISTS (SELECT 1 FROM dbo.SYS_UserProfile WHERE EmployeeNo IS NOT NULL AND PinHash IS NOT NULL AND AccountStatus = 'ACTIVE')
    INSERT INTO #chk VALUES ('WARN', N'POP', N'No active worker / web account with a PIN - POP PIN login impossible (MD-032 / dev: seed_md_worker_dev.sql)');

-- 슈퍼바이저: 선택 라인에 없으면 복사(자동 보정) — 없으면 안돈이 OPEN 에서 못 벗어난다
DECLARE @SupSrc varchar(20) = (SELECT TOP 1 LineID FROM dbo.MD_LineSupervisor WHERE ActiveFlag = 1 ORDER BY LineID);
DECLARE @SupCopied int = 0;
INSERT INTO dbo.MD_LineSupervisor (LineID, EmployeeNo, ActiveFlag, CreatedBy, CreatedTS)
SELECT l.LineID, s.EmployeeNo, 1, @Actor, @Now
FROM   #line l CROSS JOIN dbo.MD_LineSupervisor s
WHERE  s.LineID = @SupSrc AND s.ActiveFlag = 1
  AND  NOT EXISTS (SELECT 1 FROM dbo.MD_LineSupervisor x WHERE x.LineID = l.LineID AND x.ActiveFlag = 1)
  AND  NOT EXISTS (SELECT 1 FROM dbo.MD_LineSupervisor x WHERE x.LineID = l.LineID AND x.EmployeeNo = s.EmployeeNo);
SET @SupCopied = @@ROWCOUNT;
IF @SupCopied > 0
    INSERT INTO #chk VALUES ('OK', N'POP', CONCAT(N'Selected lines had no active supervisor - copied supervisors of ', @SupSrc, N' (', @SupCopied, N' rows, check in MD-033)'));
INSERT INTO #chk SELECT 'WARN', N'POP', CONCAT(l.LineID, N': no active line supervisor - andon cannot leave OPEN (MD-033 / dev: seed_andon_dev.sql)')
FROM   #line l WHERE NOT EXISTS (SELECT 1 FROM dbo.MD_LineSupervisor s WHERE s.LineID = l.LineID AND s.ActiveFlag = 1);

-- FG 재고 (PP-003 순수요 기준이 수주량보다 작아진다)
INSERT INTO #chk SELECT 'WARN', N'Stock', CONCAT(p.Assy, N': FG area stock ', CAST(SUM(f.Qty) AS int), N' - PP-003 net-requirement basis would reduce WO qty by that much (use the order-qty basis)')
FROM   #pick p JOIN dbo.WH_Inventory f ON f.PartNo COLLATE DATABASE_DEFAULT = p.Assy
JOIN   dbo.MD_Location fl ON fl.LocationID COLLATE DATABASE_DEFAULT = f.LocationNo COLLATE DATABASE_DEFAULT
WHERE  f.Qty > 0 AND UPPER(fl.AreaCode) = 'FG_AREA' GROUP BY p.Assy;

IF NOT EXISTS (SELECT 1 FROM #chk WHERE Level IN ('FAIL','WARN'))
    INSERT INTO #chk VALUES ('OK', N'All', N'Master check passed');

PRINT N'== 2) Master check';
SELECT Level, Area, Msg FROM #chk ORDER BY CASE Level WHEN 'FAIL' THEN 0 WHEN 'WARN' THEN 1 ELSE 2 END, Seq;
IF EXISTS (SELECT 1 FROM #chk WHERE Level = 'FAIL')
    THROW 50210, N'Master check FAIL - fix the items above and rerun (nothing was changed)', 1;

-- ─────────────────────────────────────────────────────────────────────
-- §3 초기화 (PP 계획 + 생산 데이터 전부 — 창고·FG·QC·PNT·MNT 는 남긴다)
-- ─────────────────────────────────────────────────────────────────────
PRINT N'== 3) Reset';
DECLARE @n int;

-- 3-a) 생산 LOT: INJ/IMG/FINAL 공정 LOT + WO 에 묶인 LOT + 사출/감싸기 LOT 행이 있는 LOT (창고 LOT WH/LOCAL/CKD/RELEASE 는 제외)
IF OBJECT_ID('tempdb..#lot') IS NOT NULL DROP TABLE #lot;
SELECT l.LotID INTO #lot FROM dbo.tbl_Lot l
WHERE  l.ProcessCode IN ('INJ','IMG','FINAL') OR l.WoID IS NOT NULL
   OR  EXISTS (SELECT 1 FROM dbo.PR_InjLot x WHERE x.LotID = l.LotID) OR EXISTS (SELECT 1 FROM dbo.PR_ImgLot x WHERE x.LotID = l.LotID);
DELETE FROM dbo.PR_RobotInspection WHERE LotID IN (SELECT LotID FROM #lot);       SET @n = @@ROWCOUNT; PRINT CONCAT(N'  PR_RobotInspection: ', @n);
DELETE FROM dbo.PR_DefectDetail;                                                    SET @n = @@ROWCOUNT; PRINT CONCAT(N'  PR_DefectDetail: ', @n);
DELETE FROM dbo.PR_ProductionResult;                                                SET @n = @@ROWCOUNT; PRINT CONCAT(N'  PR_ProductionResult: ', @n);
DELETE FROM dbo.PR_ImgLot;                                                          SET @n = @@ROWCOUNT; PRINT CONCAT(N'  PR_ImgLot: ', @n);
DELETE FROM dbo.PR_InjLot;                                                          SET @n = @@ROWCOUNT; PRINT CONCAT(N'  PR_InjLot: ', @n);
UPDATE dbo.FG_CustomerReturn SET LotID = NULL WHERE LotID IN (SELECT LotID FROM #lot); SET @n = @@ROWCOUNT; PRINT CONCAT(N'  FG_CustomerReturn.LotID cleared: ', @n);
UPDATE dbo.tbl_Lot SET ParentLotID = NULL WHERE ParentLotID IN (SELECT LotID FROM #lot) AND LotID NOT IN (SELECT LotID FROM #lot);
                                                                                    SET @n = @@ROWCOUNT; PRINT CONCAT(N'  tbl_Lot parent links cleared: ', @n);
DELETE FROM dbo.tbl_Lot WHERE LotID IN (SELECT LotID FROM #lot);                  SET @n = @@ROWCOUNT; PRINT CONCAT(N'  tbl_Lot(INJ/IMG/FINAL): ', @n);
DELETE FROM dbo.SYS_LotSeq WHERE NOT EXISTS (SELECT 1 FROM dbo.tbl_Lot l WHERE l.LotCode LIKE RTRIM(SYS_LotSeq.Header) + '%');
                                                                                    SET @n = @@ROWCOUNT; PRINT CONCAT(N'  SYS_LotSeq (headers with no remaining lot): ', @n);

-- 3-b) 안돈·비가동·안돈 고장
DELETE FROM dbo.PR_AndonPush;                                                       SET @n = @@ROWCOUNT; PRINT CONCAT(N'  PR_AndonPush: ', @n);
DELETE FROM dbo.PR_AndonDeptCall;                                                   SET @n = @@ROWCOUNT; PRINT CONCAT(N'  PR_AndonDeptCall: ', @n);
DELETE FROM dbo.PR_AndonCall;                                                       SET @n = @@ROWCOUNT; PRINT CONCAT(N'  PR_AndonCall: ', @n);
DELETE FROM dbo.PP_LineDowntimeLog;                                                 SET @n = @@ROWCOUNT; PRINT CONCAT(N'  PP_LineDowntimeLog: ', @n);
IF OBJECT_ID('tempdb..#fail') IS NOT NULL DROP TABLE #fail;
SELECT FailureID INTO #fail FROM dbo.MNT_FailureRegister WHERE Source = 'ANDON';
UPDATE dbo.MNT_EquipmentStatus SET LastFailureID = NULL WHERE LastFailureID IN (SELECT FailureID FROM #fail);
DELETE FROM dbo.MNT_FailureAction WHERE FailureID IN (SELECT FailureID FROM #fail);  SET @n = @@ROWCOUNT; PRINT CONCAT(N'  MNT_FailureAction(ANDON): ', @n);
DELETE FROM dbo.MNT_FailureRegister WHERE FailureID IN (SELECT FailureID FROM #fail); SET @n = @@ROWCOUNT; PRINT CONCAT(N'  MNT_FailureRegister(ANDON): ', @n);

-- 3-c) WO 와 자식 (슬롯은 WO·MC 만 — PM 은 보전 일정이라 남긴다)
DELETE FROM dbo.PP_LineSchedule WHERE EntryType IN ('WO','MC');                    SET @n = @@ROWCOUNT; PRINT CONCAT(N'  PP_LineSchedule (WO+MC): ', @n);
DELETE FROM dbo.PR_WoAcceptance;                                                    SET @n = @@ROWCOUNT; PRINT CONCAT(N'  PR_WoAcceptance: ', @n);
DELETE FROM dbo.PP_MaterialReservation;                                             SET @n = @@ROWCOUNT; PRINT CONCAT(N'  PP_MaterialReservation: ', @n);
DELETE FROM dbo.PP_ApsRunWo;                                                        SET @n = @@ROWCOUNT; PRINT CONCAT(N'  PP_ApsRunWo: ', @n);
DELETE FROM dbo.PP_ApsPlanLineShift;                                                SET @n = @@ROWCOUNT; PRINT CONCAT(N'  PP_ApsPlanLineShift: ', @n);
DELETE FROM dbo.PP_ApsPlanLine;                                                     SET @n = @@ROWCOUNT; PRINT CONCAT(N'  PP_ApsPlanLine: ', @n);
DELETE FROM dbo.PP_ApsRun;                                                          SET @n = @@ROWCOUNT; PRINT CONCAT(N'  PP_ApsRun: ', @n);
DELETE FROM dbo.PP_MRPResultWo;                                                     SET @n = @@ROWCOUNT; PRINT CONCAT(N'  PP_MRPResultWo: ', @n);
DELETE FROM dbo.PP_MRPResult;                                                       SET @n = @@ROWCOUNT; PRINT CONCAT(N'  PP_MRPResult: ', @n);
DELETE FROM dbo.PP_MRPLog;                                                          SET @n = @@ROWCOUNT; PRINT CONCAT(N'  PP_MRPLog: ', @n);
DELETE FROM dbo.PP_PRSendLog;                                                       SET @n = @@ROWCOUNT; PRINT CONCAT(N'  PP_PRSendLog: ', @n);
DELETE FROM dbo.PP_PurchaseRequest;                                                 SET @n = @@ROWCOUNT; PRINT CONCAT(N'  PP_PurchaseRequest: ', @n);
DELETE FROM dbo.PP_WorkOrderRouting;                                                SET @n = @@ROWCOUNT; PRINT CONCAT(N'  PP_WorkOrderRouting: ', @n);
DELETE FROM dbo.PP_WorkOrder;                                                       SET @n = @@ROWCOUNT; PRINT CONCAT(N'  PP_WorkOrder: ', @n);

-- 3-d) 수요: 수주·예측·일별계획·공급계획
DELETE FROM dbo.PP_CustomerOrder;                                                   SET @n = @@ROWCOUNT; PRINT CONCAT(N'  PP_CustomerOrder: ', @n);
DELETE FROM dbo.PP_ForecastHistory;                                                 SET @n = @@ROWCOUNT; PRINT CONCAT(N'  PP_ForecastHistory: ', @n);
DELETE FROM dbo.PP_Forecast;                                                        SET @n = @@ROWCOUNT; PRINT CONCAT(N'  PP_Forecast: ', @n);
DELETE FROM dbo.PP_DemandPlan;                                                      SET @n = @@ROWCOUNT; PRINT CONCAT(N'  PP_DemandPlan: ', @n);
DELETE FROM dbo.PP_DemandPlanBatch;                                                 SET @n = @@ROWCOUNT; PRINT CONCAT(N'  PP_DemandPlanBatch: ', @n);
DELETE FROM dbo.PP_SupplyPlanDetail;                                                SET @n = @@ROWCOUNT; PRINT CONCAT(N'  PP_SupplyPlanDetail: ', @n);
DELETE FROM dbo.PP_SupplyPlan;                                                      SET @n = @@ROWCOUNT; PRINT CONCAT(N'  PP_SupplyPlan: ', @n);

-- ─────────────────────────────────────────────────────────────────────
-- §4 생성 — 실행일 기준
-- ─────────────────────────────────────────────────────────────────────
PRINT N'== 4) Seed';
-- 근무일 번호: N = 오늘 뒤 n 번째 근무일 (0 이하 = 오늘 이전 근무일, 생산 마감 예상치 계산용)
IF OBJECT_ID('tempdb..#wd') IS NOT NULL DROP TABLE #wd;
SELECT ROW_NUMBER() OVER (ORDER BY D) - (SELECT COUNT(*) FROM #cal c WHERE c.IsWork = 1 AND c.D <= @Today) AS N, D
INTO   #wd FROM #cal WHERE IsWork = 1;

-- 4-a) 확정 수주: 품번마다 3건 — 납기 = 오늘 뒤 1 / 2 / 3 번째 근무일(실행일부터 2~3일치), 생산 마감 예상 = 납기 − 버퍼 근무일(과거),
--      수량 = 적입수량(없으면 20) × 2 / 3 / 4, SoNumber = 'T' + yyMMdd + '-' + 순번3, 주문일 = 오늘
IF OBJECT_ID('tempdb..#so') IS NOT NULL DROP TABLE #so;
SELECT ROW_NUMBER() OVER (ORDER BY r.R, p.Kind, p.Assy) AS Rn, p.Assy, cu.CustomerID, r.R,
       ISNULL(a.PalletQty, 20) * (r.R + 1) AS Qty,
       (SELECT D FROM #wd WHERE N = r.R) AS Due,
       (SELECT D FROM #wd WHERE N = r.R - @Buffer) AS Deadline
INTO   #so
FROM   #pick p JOIN dbo.MD_Item a ON a.ItemNo = p.Assy JOIN #cust cu ON cu.CarType = a.CarType
CROSS JOIN (SELECT 1 AS R UNION ALL SELECT 2 UNION ALL SELECT 3) r;
IF EXISTS (SELECT 1 FROM #so WHERE Due IS NULL) THROW 50220, N'Due date calculation failed - not enough workdays within 70 days', 1;

INSERT INTO dbo.PP_CustomerOrder (SoNumber, SoLineNo, CustomerID, ItemNo, OrderQty, ShippedQty, OrderDate, RequestedDeliveryDate, PromisedDate, Status, CreatedBy, CreatedTS)
SELECT 'T' + FORMAT(@Today, 'yyMMdd') + '-' + RIGHT('000' + CAST(s.Rn AS varchar(3)), 3), 10, s.CustomerID, s.Assy, s.Qty, 0, @Today, s.Due, s.Due, 'Confirmed', @Actor, @Now
FROM   #so s ORDER BY s.Rn;
PRINT CONCAT(N'  PP_CustomerOrder: ', @@ROWCOUNT, N' rows (due = next 3 workdays; production buffer ', @Buffer, N' workdays -> PP-003 places them as Late from today)');

-- 4-b) 주간 예측: 이번 주 월요일부터 8주, 고객×품번×주차 1행(SRM_WEEKLY 업로드와 같은 모양),
--      수량 = 그 주 납기 수주 합, 수주 없는 주는 적입수량 × 2. ForecastMonth = 주 시작일의 1일(PP-001 월별 목록·주간 탭 모두 보인다).
DECLARE @Mon date = DATEADD(day, -(DATEDIFF(day, '19000101', @Today) % 7), @Today);
DECLARE @Batch varchar(20) = 'TEST-' + FORMAT(@Today, 'yyyyMMdd');
;WITH w AS (SELECT 0 AS i UNION ALL SELECT i + 1 FROM w WHERE i < 7)
INSERT INTO dbo.PP_Forecast (ForecastBatch, CustomerID, ItemNo, ForecastMonth, ForecastQty, Confidence, Source, ImportedAt, ImportedBy,
                             WeekStartDate, WeekLabel, BaseInv, PartName, Unit, CreatedBy, CreatedTS)
SELECT @Batch, cu.CustomerID, p.Assy, DATEFROMPARTS(YEAR(wk.WS), MONTH(wk.WS), 1),
       ISNULL((SELECT SUM(s.Qty) FROM #so s WHERE s.Assy = p.Assy AND s.Due >= wk.WS AND s.Due < DATEADD(day, 7, wk.WS)), ISNULL(a.PalletQty, 20) * 2),
       'FIRM', 'SRM_WEEKLY', @Now, @Actor,
       wk.WS, '[' + FORMAT(wk.WS, 'MM') + '/' + CAST((DAY(wk.WS) - 1) / 7 + 1 AS varchar(1)) + 'W]', 0, LEFT(a.ItemName, 100), a.DefaultUOM, @Actor, @Now
FROM   #pick p JOIN dbo.MD_Item a ON a.ItemNo = p.Assy JOIN #cust cu ON cu.CarType = a.CarType
CROSS JOIN (SELECT DATEADD(week, i, @Mon) AS WS FROM w) wk
ORDER  BY cu.CustomerID, p.Assy, wk.WS;
PRINT CONCAT(N'  PP_Forecast: ', @@ROWCOUNT, N' rows (batch ', @Batch, N', 8 weeks from ', CONVERT(varchar(10), @Mon, 120), N')');

-- ─────────────────────────────────────────────────────────────────────
-- §5 요약
-- ─────────────────────────────────────────────────────────────────────
PRINT N'== 5) Created orders';
SELECT o.SoNumber, o.CustomerID, o.ItemNo, p.Kind, p.MoldID, CAST(o.OrderQty AS int) AS Qty, o.RequestedDeliveryDate AS Due, s.Deadline AS ProdDeadlineExpected, p.InjLine, p.ImgLine
FROM   dbo.PP_CustomerOrder o JOIN #pick p ON p.Assy = o.ItemNo JOIN #so s ON s.Assy = o.ItemNo AND s.Due = o.RequestedDeliveryDate
WHERE  o.CreatedBy = @Actor ORDER BY o.RequestedDeliveryDate, o.SoNumber;

SELECT f.ItemNo, f.WeekStartDate, f.WeekLabel, CAST(f.ForecastQty AS int) AS Qty FROM dbo.PP_Forecast f WHERE f.ForecastBatch = @Batch ORDER BY f.ItemNo, f.WeekStartDate;

DECLARE @lines nvarchar(200) = (SELECT STRING_AGG(LineID, ', ') FROM #line);
PRINT CONCAT(N'Next: PP-003 batch-create WOs for these orders on the order-qty basis -> PP-LSB board -> POP login (lines ', @lines, N') -> manual label / scan confirm -> IMG core scan / judge.');
DECLARE @apsDays int = (SELECT DATEDIFF(day, @Today, MAX(Due)) + 1 FROM #so);
IF @apsDays > 5 PRINT CONCAT(N'PP-APS: the default 5-day horizon ends before the last due date - set Days to at least ', @apsDays, N' (orders are read only when due <= base date + days).');
DECLARE @plc nvarchar(400) = (SELECT STRING_AGG(x.S, N' | ') FROM (SELECT DISTINCT p.InjLine + N': ' + p.PlcMoldString AS S FROM #pick p) x);
PRINT CONCAT(N'InjAgent: send to PLC mold register (5330) per line -> ', @plc, N' (appsettings LineId must match the line).');

IF '$(Mode)' = 'COMMIT'
BEGIN COMMIT;   PRINT N'COMMIT - applied'; END
ELSE
BEGIN ROLLBACK; PRINT N'ROLLBACK - dry run, nothing applied'; END;
GO
