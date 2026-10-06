-- ════════════════════════════════════════════════════════════════════════
--  seed_aps_dev.sql
--  dev: PP-APS 생산계획 데모 시드 — 운영 DB 용이 아니다.
--
--  선행: dist/migrate_aps.sql (BoxQty·MD_ApsLineStage 없으면 여기서 예외).
--  대상 품번은 리터럴이 아니라 MD_MoldItem(활성) 조인으로 정한다 — 품목 마스터가 바뀌어도
--  금형→품번 매핑만 살아 있으면 동작한다(개발 DB 실측: LQ2-DTMD/LQ2-DTRU/MEA-DTRCT/NEA-FUC 4금형 5품번).
--  통합 테스트 행(ITEST-%)은 대상에서 뺀다 — 공용 개발 DB 에서 테스트와 동시에 돌아도 테스트 데이터를 바꾸지 않게.
--  이 시드가 만든 행·채운 값은 행위자 'seed-aps' 로 남고, ID 가 있는 행은 APS 가 들어간 ID 를 쓴다
--  (LP-APS-INJ01 · LP-APS-INJ01-A/B · APS-SO-NN-K · BOPID 'PAPS…') — 정리할 때 이것으로 찾는다.
--
--  ① MD_WorkCenter.ProcessCode — 스키마 시드 INJECTION/WRAPPING/PAINTING 을 INJ/IMG/PNT 로 (리빌드 직후 DB 대비, 가드형)
--  ② (폐지 09-30) 품번별 캐비티는 APS 가 금형 CavityCount ÷ 활성 품번 수로 추정한다 — 시드 없음
--  ③ MD_Item.BoxQty — NULL 인 금형 품번만: 형제(같은 금형 활성 품번 2개 이상) 36, 단일 5 (dev 임의값)
--  ④ LINE-INJ-01 전용 2교대 패턴 LP-APS-INJ01 — A 0800-1600 · B 1600-2400 OPERATING(휴게 없음).
--     그 라인에 라인 전용 ACTIVE 패턴이 이미 있으면 건너뛴다(ReadDayCapacity 는 라인 전용을 전역보다 먼저 고른다).
--     나머지 INJ 라인은 전역 LP-INJ-001(주간 A / 야간 B)을 그대로 쓴다.
--  ⑤ MD_ApsLineStage — INJ 활성 라인 전부 OffsetDays 1 · UseStock 1 (없는 라인만)
--  ⑥ PP_CustomerOrder — 품번마다 Confirmed 수주 3건(APS-SO-NN-K, 납기 = 오늘 +2·+4·+6 근무일, ShippedQty 0),
--     고객 = MD_Customer 첫 활성 행(CustomerID 순). 근무일 = Scheduling.WorkdayCalendar 규칙(그 날짜 SYS_FactoryCalendar
--     행이 있으면 하나라도 WORKDAY/SPECIAL 일 때만, 행이 없으면 토·일만 휴일). SoNumber 와 (품번, 순번 K) 로 가드 —
--     재실행해도 납기는 처음 값 유지, 금형 품번이 늘어 번호가 밀려도 같은 품번에 두 번 넣지 않는다.
--  ⑦ MD_Bop 보강 — 금형 품번에 INJ StepSeq 10 @ LINE-INJ-01 첫 스테이션, A 라우팅(INJ→IMG) 품번에 IMG StepSeq 20 @
--     LINE-IMG-01 첫 스테이션이 없으면 추가 (seed_md_bop_inj_dev.sql 방식, (품번, 스테이션) 가드)
--  ⑧ APS 가동 시간 패턴 설정(10-06) — 사출 라인 행 PatternID ← 라인 전용 ACTIVE 패턴, DEFAULT_PATTERN ← 전역 ACTIVE 패턴 (비어 있을 때만)
--
--  재실행 안전(기존 값·행은 덮지 않는다). 적용:
--    sqlcmd -S 192.168.1.100,1433 -U ames_app -P !Dev2026 -d AMES_DEV -f 65001 -I -b -i dist/seed_aps_dev.sql
-- ════════════════════════════════════════════════════════════════════════
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

-- ── ① 워크센터 공정 코드 정규화 ─────────────────────────────────────
UPDATE wc
SET    wc.ProcessCode = v.NewCode, wc.ModifiedBy = 'seed-aps', wc.ModifiedTS = SYSDATETIME()
FROM   dbo.MD_WorkCenter wc
JOIN   (VALUES ('INJECTION', 'INJ'), ('WRAPPING', 'IMG'), ('PAINTING', 'PNT')) v (OldCode, NewCode)
       ON v.OldCode = wc.ProcessCode;
PRINT CONCAT(N'① MD_WorkCenter.ProcessCode normalized: ', @@ROWCOUNT);
GO

-- ── ② (폐지) 품번 캐비티 — 시드 없음 ───────────────────────────────

-- ── ③ 박스 수량 ─────────────────────────────────────────────────────
UPDATE i
SET    i.BoxQty = CASE WHEN sib.n >= 2 THEN 36 ELSE 5 END,
       i.ModifiedBy = 'seed-aps', i.ModifiedTS = SYSDATETIME()
FROM   dbo.MD_Item i
JOIN   (SELECT mi.ItemNo, MAX(cnt.n) AS n
        FROM   dbo.MD_MoldItem mi
        JOIN   (SELECT MoldID, COUNT(*) AS n FROM dbo.MD_MoldItem WHERE ActiveFlag = 1 GROUP BY MoldID) cnt ON cnt.MoldID = mi.MoldID
        WHERE  mi.ActiveFlag = 1 AND mi.MoldID NOT LIKE 'ITEST-%' AND mi.ItemNo NOT LIKE 'ITEST-%'
        GROUP  BY mi.ItemNo) sib ON sib.ItemNo = i.ItemNo
WHERE  i.BoxQty IS NULL;
PRINT CONCAT(N'③ MD_Item.BoxQty filled: ', @@ROWCOUNT);
GO

-- ── ④ LINE-INJ-01 전용 2교대 패턴 ───────────────────────────────────
-- 분 플래그 축은 하루 분(0~1439)이 아니라 WORK_SHIFT 창을 SortOrder 순(A 08-16 · B 16-24 · C 00-08)으로 이어 붙인 것이다
-- (MD 화면 MasterDataRepository.RebuildMinuteFlags 와 기존 패턴 행이 이 축) → A·B 가동 = 앞 960자 '1', C = 뒤 480자 '0'.
-- ShiftPattern 은 공통코드 SHIFT_PATTERN 값이다(MD_Line.ShiftPattern 의 '2-SHIFT' 와 다른 어휘).
IF EXISTS (SELECT 1 FROM dbo.MD_LineTimePattern WHERE PatternID = 'LP-APS-INJ01')
    PRINT N'④ LP-APS-INJ01 exists already — skipped';
ELSE IF EXISTS (SELECT 1 FROM dbo.MD_LineTimePattern p
                WHERE  p.LineID = 'LINE-INJ-01' AND ISNULL(p.Status, 'ACTIVE') = 'ACTIVE')
    PRINT N'④ LINE-INJ-01 has a line-specific ACTIVE pattern already — LP-APS-INJ01 skipped';
ELSE IF NOT EXISTS (SELECT 1 FROM dbo.MD_Line WHERE LineID = 'LINE-INJ-01')
    PRINT N'④ WARNING: LINE-INJ-01 not in MD_Line — pattern skipped';
ELSE
BEGIN
    BEGIN TRAN;
    INSERT INTO dbo.MD_LineTimePattern
        (PatternID, LineID, PatternName, DayType, ShiftPattern, EffectiveFrom, EffectiveTo,
         TotalOperatingMin, TotalPlannedDownMin, Status, CreatedBy,
         OperatingFlag, SegmentFlag)
    VALUES
        ('LP-APS-INJ01', 'LINE-INJ-01', N'APS dev 2교대 (A 08-16 · B 16-24)', 'WORKDAY', 'SHIFT2_SCHEDULE', NULL, NULL,
         960, 0, 'ACTIVE', 'seed-aps',
         REPLICATE('1', 960) + REPLICATE('0', 480), REPLICATE('1', 960) + REPLICATE('0', 480));
    INSERT INTO dbo.MD_LineTimeSegment
        (SegmentID, PatternID, SeqNo, StartMin, EndMin, SegmentState, ReasonCode, ShiftCode, Description, CreatedBy)
    VALUES
        ('LP-APS-INJ01-A', 'LP-APS-INJ01', 1, 480, 960,  'OPERATING', NULL, 'A', N'주간 A 08:00-16:00', 'seed-aps'),
        ('LP-APS-INJ01-B', 'LP-APS-INJ01', 2, 960, 1440, 'OPERATING', NULL, 'B', N'야간 B 16:00-24:00', 'seed-aps');
    COMMIT;
    PRINT N'④ LP-APS-INJ01 pattern + 2 segments inserted for LINE-INJ-01';
END
GO

-- ── ⑤ 사출 라인 선행일 ──────────────────────────────────────────────
INSERT INTO dbo.MD_ApsLineStage (LineID, OffsetDays, UseStock, Note, CreatedBy)
SELECT l.LineID, 1, 1, N'dev seed — 사출 선행일 1일', 'seed-aps'
FROM   dbo.MD_Line l
JOIN   dbo.MD_WorkCenter wc ON wc.WCID = l.WCID
WHERE  wc.ProcessCode = 'INJ'
  AND  ISNULL(l.Status, 'ACTIVE') = 'ACTIVE'
  AND  NOT EXISTS (SELECT 1 FROM dbo.MD_ApsLineStage s WHERE s.LineID = l.LineID);
PRINT CONCAT(N'⑤ MD_ApsLineStage rows inserted: ', @@ROWCOUNT);
GO

-- ── ⑥ 확정 수주 ─────────────────────────────────────────────────────
DECLARE @Today date = CAST(SYSDATETIME() AS date);
DECLARE @Cust  varchar(20) = (SELECT TOP 1 CustomerID FROM dbo.MD_Customer
                              WHERE ISNULL(Status, 'ACTIVE') = 'ACTIVE' ORDER BY CustomerID);
IF @Cust IS NULL
    PRINT N'⑥ WARNING: no active customer in MD_Customer — orders skipped';
ELSE
BEGIN
    DECLARE @Work TABLE (Seq int IDENTITY(1,1) PRIMARY KEY, D date NOT NULL);
    DECLARE @d date = @Today, @guard int = 0, @isWork bit;
    WHILE @guard < 60 AND (SELECT COUNT(*) FROM @Work) < 6
    BEGIN
        SET @d = DATEADD(day, 1, @d);
        SET @guard += 1;
        IF EXISTS (SELECT 1 FROM dbo.SYS_FactoryCalendar WHERE CalendarDate = @d)
            SET @isWork = CASE WHEN EXISTS (SELECT 1 FROM dbo.SYS_FactoryCalendar
                                            WHERE CalendarDate = @d AND DayType IN ('WORKDAY', 'SPECIAL'))
                               THEN 1 ELSE 0 END;
        ELSE
            SET @isWork = CASE WHEN ((DATEPART(weekday, @d) + @@DATEFIRST - 2) % 7) + 1 IN (6, 7)   -- 1=월 … 7=일, DATEFIRST 무관
                               THEN 0 ELSE 1 END;
        IF @isWork = 1 INSERT @Work (D) VALUES (@d);
    END

    ;WITH n AS (
        SELECT it.ItemNo, ROW_NUMBER() OVER (ORDER BY it.ItemNo) AS rn
        FROM  (SELECT DISTINCT mi.ItemNo
               FROM   dbo.MD_MoldItem mi
               JOIN   dbo.MD_Item i ON i.ItemNo = mi.ItemNo
               WHERE  mi.ActiveFlag = 1 AND ISNULL(i.ActiveFlag, 1) = 1
                 AND  mi.MoldID NOT LIKE 'ITEST-%' AND mi.ItemNo NOT LIKE 'ITEST-%') it
    ),
    due AS (
        SELECT k, Seq, Qty FROM (VALUES (1, 2, 120), (2, 4, 180), (3, 6, 150)) v (k, Seq, Qty)
    ),
    src AS (
        SELECT 'APS-SO-' + RIGHT('00' + CAST(n.rn AS varchar(3)), 2) + '-' + CAST(due.k AS varchar(1)) AS SoNumber,
               due.k, n.ItemNo, due.Qty, w.D AS DueDate
        FROM   n
        CROSS JOIN due
        JOIN   @Work w ON w.Seq = due.Seq
    )
    INSERT INTO dbo.PP_CustomerOrder
        (SoNumber, SoLineNo, CustomerID, ItemNo, OrderQty, ShippedQty, OrderDate, RequestedDeliveryDate, PromisedDate, Status, CreatedBy)
    SELECT src.SoNumber, 1, @Cust, src.ItemNo, src.Qty, 0, @Today, src.DueDate, src.DueDate, 'Confirmed', 'seed-aps'
    FROM   src
    WHERE  NOT EXISTS (SELECT 1 FROM dbo.PP_CustomerOrder o WHERE o.SoNumber = src.SoNumber)
      AND  NOT EXISTS (SELECT 1 FROM dbo.PP_CustomerOrder o
                       WHERE  o.ItemNo = src.ItemNo AND o.SoNumber LIKE 'APS-SO-%-' + CAST(src.k AS varchar(1)));
    PRINT CONCAT(N'⑥ PP_CustomerOrder APS-SO-* rows inserted for ', @Cust, N': ', @@ROWCOUNT);
END
GO

-- ── ⑦ BOP 보강 ──────────────────────────────────────────────────────
DECLARE @StInj varchar(20) = (SELECT TOP 1 StationCode FROM dbo.MD_Station WHERE LineID = 'LINE-INJ-01' AND ISNULL(Status, 'ACTIVE') = 'ACTIVE' ORDER BY OrderSeq, StationCode);
DECLARE @StImg varchar(20) = (SELECT TOP 1 StationCode FROM dbo.MD_Station WHERE LineID = 'LINE-IMG-01' AND ISNULL(Status, 'ACTIVE') = 'ACTIVE' ORDER BY OrderSeq, StationCode);
IF @StInj IS NULL PRINT N'⑦ WARNING: LINE-INJ-01 has no station — INJ BOP skipped';
IF @StImg IS NULL PRINT N'⑦ WARNING: LINE-IMG-01 has no station — IMG BOP skipped (seed_md_bop_img_dev.sql makes ST-IMG-01)';

;WITH it AS (
    SELECT DISTINCT mi.ItemNo, i.RoutingType
    FROM   dbo.MD_MoldItem mi
    JOIN   dbo.MD_Item i ON i.ItemNo = mi.ItemNo
    WHERE  mi.ActiveFlag = 1 AND ISNULL(i.ActiveFlag, 1) = 1 AND i.RoutingType IS NOT NULL
      AND  mi.MoldID NOT LIKE 'ITEST-%' AND mi.ItemNo NOT LIKE 'ITEST-%'
),
src AS (
    SELECT it.ItemNo, it.RoutingType, 10 AS StepSeq, @StInj AS StationCode FROM it WHERE @StInj IS NOT NULL
    UNION ALL
    SELECT it.ItemNo, it.RoutingType, 20,          @StImg               FROM it WHERE @StImg IS NOT NULL AND it.RoutingType = 'A'
)
INSERT INTO dbo.MD_Bop (BOPID, ItemNo, RoutingType, StepSeq, StationCode, ActiveFlag, CreatedBy)
SELECT 'PAPS' + LEFT(REPLACE(CONVERT(varchar(36), NEWID()), '-', ''), 20),
       src.ItemNo, src.RoutingType, src.StepSeq, src.StationCode, 1, 'seed-aps'
FROM   src
WHERE  NOT EXISTS (SELECT 1 FROM dbo.MD_Bop b WHERE b.ItemNo = src.ItemNo AND b.StationCode = src.StationCode);
PRINT CONCAT(N'⑦ MD_Bop rows inserted: ', @@ROWCOUNT);
GO

-- ── ⑧ APS 가동 시간 패턴 설정 (10-06) — 사출 라인 행의 PatternID 가 비어 있으면 그 라인 전용 ACTIVE 패턴(ID 순 첫 번째, LINE-INJ-01 은 ④ 의 LP-APS-INJ01),
--      APS_SETTING.DEFAULT_PATTERN 이 비어 있으면 전역(LineID NULL) ACTIVE 패턴 중 ID 순 첫 번째. 이 둘이 없는 사출 라인이 나오면 PP-APS 조회가 막힌다 ──
UPDATE s
SET    s.PatternID = (SELECT TOP 1 p.PatternID FROM dbo.MD_LineTimePattern p
                      WHERE  p.LineID = s.LineID AND ISNULL(p.Status, 'ACTIVE') = 'ACTIVE' ORDER BY p.PatternID),
       s.ModifiedBy = 'seed-aps', s.ModifiedTS = SYSDATETIME()
FROM   dbo.MD_ApsLineStage s
JOIN   dbo.MD_Line l        ON l.LineID = s.LineID
JOIN   dbo.MD_WorkCenter wc ON wc.WCID  = l.WCID
WHERE  wc.ProcessCode = 'INJ' AND s.PatternID IS NULL
  AND  EXISTS (SELECT 1 FROM dbo.MD_LineTimePattern p WHERE p.LineID = s.LineID AND ISNULL(p.Status, 'ACTIVE') = 'ACTIVE');
PRINT CONCAT(N'⑧ MD_ApsLineStage.PatternID filled from line-specific patterns: ', @@ROWCOUNT);

UPDATE c
SET    c.Attribute1 = (SELECT TOP 1 p.PatternID FROM dbo.MD_LineTimePattern p
                       WHERE  p.LineID IS NULL AND ISNULL(p.Status, 'ACTIVE') = 'ACTIVE' ORDER BY p.PatternID),
       c.ModifiedBy = 'seed-aps', c.ModifiedTS = SYSDATETIME()
FROM   dbo.MD_CodeItem c
WHERE  c.GroupCode = 'APS_SETTING' AND c.CodeValue = 'DEFAULT_PATTERN'
  AND  NULLIF(LTRIM(RTRIM(c.Attribute1)), '') IS NULL
  AND  EXISTS (SELECT 1 FROM dbo.MD_LineTimePattern p WHERE p.LineID IS NULL AND ISNULL(p.Status, 'ACTIVE') = 'ACTIVE');
PRINT CONCAT(N'⑧ APS_SETTING.DEFAULT_PATTERN filled from a global pattern: ', @@ROWCOUNT);
GO

-- ── 확인 ────────────────────────────────────────────────────────────
SELECT wc.WCID, wc.ProcessCode FROM dbo.MD_WorkCenter wc ORDER BY wc.WCID;
SELECT mi.MoldID, mi.ItemNo, m.CavityCount AS MoldCav, i.BoxQty, i.RoutingType
FROM   dbo.MD_MoldItem mi JOIN dbo.MD_Mold m ON m.MoldID = mi.MoldID JOIN dbo.MD_Item i ON i.ItemNo = mi.ItemNo
WHERE  mi.ActiveFlag = 1 ORDER BY mi.MoldID, mi.ItemNo;
SELECT p.PatternID, p.LineID, p.Status, s.SeqNo, s.StartMin, s.EndMin, s.SegmentState, s.ShiftCode
FROM   dbo.MD_LineTimePattern p LEFT JOIN dbo.MD_LineTimeSegment s ON s.PatternID = p.PatternID
WHERE  p.LineID = 'LINE-INJ-01' ORDER BY p.PatternID, s.SeqNo;
SELECT LineID, OffsetDays, UseStock, PatternID FROM dbo.MD_ApsLineStage ORDER BY LineID;
SELECT CodeValue, Attribute1 FROM dbo.MD_CodeItem WHERE GroupCode = 'APS_SETTING' AND CodeValue = 'DEFAULT_PATTERN';
SELECT SoNumber, ItemNo, OrderQty, RequestedDeliveryDate, DATENAME(weekday, RequestedDeliveryDate) AS Wd, Status, CustomerID
FROM   dbo.PP_CustomerOrder WHERE SoNumber LIKE 'APS-SO-%' ORDER BY SoNumber;
SELECT b.ItemNo, b.RoutingType, b.StepSeq, b.StationCode FROM dbo.MD_Bop b
WHERE  b.ItemNo IN (SELECT ItemNo FROM dbo.MD_MoldItem WHERE ActiveFlag = 1) ORDER BY b.ItemNo, b.StepSeq;
GO
