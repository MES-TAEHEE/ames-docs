using AMES.Data.Aps;
using AMES.Data.Aps.Contracts;
using AMES.Data.Connection;
using AMES.Data.Repositories;
using Xunit;
using static AMES.Data.Tests.AmesDevDb;

namespace AMES.Data.Tests.Aps;

/// <summary>
/// ApsRepository.BuildBundle — AMES 마스터·수주·실적·슬롯 → PlanBundle (스펙 §4·§5). AMES_DEV 통합 테스트, DB 미기동 시 skip.
/// 전제: migrate_aps.sql 적용, 개발 시드 라인 LINE-INJ-01 / LINE-INJ-02 / LINE-IMG-01 · 스테이션 ST-INJ-01 / ST-IMG-01, 라우팅 A = INJ→IMG(2단계).
/// 기준 주는 +414일 뒤 월요일(PlanScheduleTests·MoldPlanningTests 의 +400일 주와 겹치지 않음). 같은 라인을 쓰므로 같은 컬렉션으로 직렬화한다.
/// 공유 개발 DB 라서 실제 품번(개발 시드 수주의 지연분이 첫날에 들어온다)도 번들에 섞인다 — 개수 단언은 ITEST-APS- 행으로 한정한다.
/// 시드: 완제품 ITEST-APS-FG(BoxQty 20, BOP INJ/IMG 스테이션) · 형제 ITEST-APS-SIB(BoxQty NULL, IMG BOP 만) · RoutingType 없는 ITEST-APS-NORT,
/// 금형 ITEST-APS-M(CavityCount 2, LINE-INJ-01 UPH 120) · LINE-INJ-01 전용 3교대 패턴 ITEST-APS-PAT(A 08~16 · B 16~24 · C 00~08) ·
/// Review Focus 1 용 ITEST-APS-TWO(BoxQty 10, IMG BOP 만, 금형 ITEST-APS-M2 CavityCount 1 이 LINE-INJ-01(PrepTime 40, UPH 120)·LINE-INJ-02(PrepTime 5, UPH 90) 두 곳에 배정) ·
/// 수주 8건 · Released WO 1건(INJ 완료 50 / IMG 완료 30, D1 INJ 주간 슬롯 60 · D2 IMG 슬롯 60) · 직전 근무일 실적(IMG 12 + 역분개 −1, INJ 20) · 완제품 재고 25(통합재고 WH_Inventory 15 + 10, Qty 0 행 제외, 위치 무관).
/// 정리는 이 클래스가 만든 행만(키 접두어 + CreatedBy = ITEST-APS) 지운다.
/// </summary>
[Collection("AMES_DEV plan week")]
public class ApsRepositoryTests
{
    const string Fg     = "ITEST-APS-FG";
    const string Sib    = "ITEST-APS-SIB";
    const string NoRt   = "ITEST-APS-NORT";
    const string Two    = "ITEST-APS-TWO";   // Review Focus 1: 활성 금형이 사출 라인 두 곳에 배정된 품번
    const string Tie    = "ITEST-APS-TIE";   // Review Focus 1 동률: 두 라인 교체 시간이 같다 → LineCode Ordinal
    const string Bp     = "ITEST-APS-BP";    // BOM 규칙 부모(자기 금형 없음)
    const string Ch     = "ITEST-APS-CH";    // BOM 자식 QtyPer 1.5
    const string Ch2    = "ITEST-APS-CH2";   // BOM 자식 QtyPer 2 · ScrapPct 10
    const string NoItem = "ITEST-APS-NOITEM";
    const string Rc     = "ITEST-APS-RC";    // 라우팅 C(INJ 가 마지막 라인 단계)
    const string NoL    = "ITEST-APS-NOL";   // 라우팅 A, BOP 없음 → 완제품 라인 미배정(IMG 라인 여러 개)
    const string NmL    = "ITEST-APS-NML";   // 활성 금형은 있으나 BOP INJ·MD_MoldLine 없음 → 사출 라인 미정
    const string NUp    = "ITEST-APS-NUP";   // BOP INJ 라인의 MD_MoldLine UPH 0
    const string Bx     = "ITEST-APS-BX";    // BoxQty 24, 금형·BOM 없음 — 아래 Bxl 의 앞자리
    const string Bxl    = "ITEST-APS-BXL";   // BoxQty NULL, 금형·BOM 없음
    const string Off    = "ITEST-APS-OFF";   // 비활성 품번(등록 슬롯만)
    const string Mold   = "ITEST-APS-M";
    const string Mold2  = "ITEST-APS-M2";
    const string Mold3  = "ITEST-APS-M3";
    const string Mold4  = "ITEST-APS-M4";
    const string Mold5  = "ITEST-APS-M5";
    const string Mold6  = "ITEST-APS-M6";
    const string Mold7  = "ITEST-APS-M7";
    const string Mold8  = "ITEST-APS-M8";   // 캐비티 3 에 품번 2개(같은 색) → 비대칭 분할
    const string Mold9  = "ITEST-APS-M9";   // 캐비티 2 에 색상 2종(NNB·YGU) → 색상마다 따로 찍는다
    const string OdA    = "ITEST-APS-ODA";
    const string OdB    = "ITEST-APS-ODB";
    const string ClN    = "ITEST-APS-CLN";   // Mold9 NNB
    const string ClY    = "ITEST-APS-CLY";   // Mold9 YGU
    const string Mat    = "ITEST-APS-MAT";   // ItemType MATERIAL(RoutingType 없음, 재고 있음) — 계획 대상 아님, 경고도 없음
    const string Bp2    = "ITEST-APS-BP2";   // 다단계 BOM 부모: Bp2 → Mid(QtyPer 2, 금형 없는 SUB) → Ch(QtyPer 1.5, 금형 M3) / Bp2 → Mid2(SUB, 아래에 금형 없음)
    const string Mid    = "ITEST-APS-MID";
    const string Mid2   = "ITEST-APS-MID2";  // InjFlag 1 인데 활성 금형 없음 → 경고
    const string NoInj  = "ITEST-APS-NOINJ"; // 활성 금형(M8)은 있지만 InjFlag 0 → 사출품 아님(서브조립로 취급) + 경고
    const string Pat    = "ITEST-APS-PAT";
    const string Pat2   = "ITEST-APS-PAT2";  // 비활성 — PP_LineSchedule.PatternID 로만 쓰인다(A 08:00~15:00 7h / B 16~24 8h)
    const string CapLine  = "ITEST-APS-L0";   // DailyCap 0/NULL 완제품 라인
    const string LineInj  = "LINE-INJ-01";
    const string LineInj2 = "LINE-INJ-02";   // 개발 시드 라인(dist/AMES_Schema.sql MD_Line)
    const string LineImg  = "LINE-IMG-01";
    const string Actor = "ITEST-APS";
    const string TestLine = "ITEST-APS-LN";

    static readonly DateTime D0   = NextMonday(DateTime.Today.AddDays(414));
    static readonly DateTime D1   = D0.AddDays(1);
    static readonly DateTime D2   = D0.AddDays(2);
    static readonly DateTime D3   = D0.AddDays(3);
    static readonly DateTime Sat  = D0.AddDays(5);
    static readonly DateTime Prev = D0.AddDays(-3);   // 직전 근무일 = 금요일 (그 주에 SYS_FactoryCalendar 행이 없다고 가정)
    static readonly DateOnly Base = DateOnly.FromDateTime(D0);

    static DateTime NextMonday(DateTime d) => d.Date.AddDays(((int)DayOfWeek.Monday - (int)d.DayOfWeek + 7) % 7);
    static string Iso(DateTime d) => d.ToString("yyyy-MM-dd");
    static bool IsTest(string partNo) => partNo.StartsWith("ITEST-APS-", StringComparison.Ordinal);

    /// <summary>개발 DB 라우팅 A 의 활성 단계(StepSeq 순) — Notes #7 실측 "INJ,IMG" 가 아니면 완제품 라인 판정이 달라지므로 skip 한다.</summary>
    static string RoutingA(AmesConnectionFactory f) => Routing(f, "A");

    static string Routing(AmesConnectionFactory f, string type) => Scalar(f, """
        SELECT STRING_AGG(ProcessCode, ',') WITHIN GROUP (ORDER BY StepSeq)
        FROM   dbo.MD_RoutingStep WHERE RoutingType = @T AND ISNULL(ActiveFlag,1) = 1;
        """, ("@T", type)) as string ?? "";

    internal static void Seed(AmesConnectionFactory f)
    {
        Cleanup(f);
        Exec(f, """
            INSERT INTO dbo.MD_Item (ItemNo, ItemName, RoutingType, SafetyStock, PGN, ALC, CarType, BoxQty, ActiveFlag, CreatedBy)
            VALUES (@FG,  N'ITEST APS finished',   'A',  10, 'Q019', '8146', 'HL', 20,   1, @By),
                   (@SIB, N'ITEST APS sibling',    'A',  0,  'Q019', '8146', 'HL', NULL, 1, @By),
                   (@NR,  N'ITEST APS no routing', NULL, 0,  NULL,   NULL,   NULL, NULL, 1, @By),
                   (@TWO, N'ITEST APS two lines',  'A',  0,  NULL,   NULL,   NULL, 10,   1, @By);
            INSERT INTO dbo.MD_Mold (MoldID, MoldName, CavityCount, Status, MoldChangeMin, CreatedBy)
            VALUES (@M,  N'ITEST APS mold',   2, 'AVAILABLE', 20, @By),
                   (@M2, N'ITEST APS mold 2', 1, 'AVAILABLE', 30, @By);
            INSERT INTO dbo.MD_MoldItem (MoldID, ItemNo, Color, CavitySeq, CavityPos, CavityCount, MoldCategory, ActiveFlag, CreatedBy)
            VALUES (@M,  @FG,  'CBK', 1, 'LH', 2,    'INJECTION', 1, @By),
                   (@M,  @SIB, 'CBK', 2, 'RH', 2, 'INJECTION', 1, @By),
                   (@M2, @TWO, 'CBK', 1, 'LH', 1,    'INJECTION', 1, @By);
            INSERT INTO dbo.MD_MoldLine (LineCode, MoldID, UPH, PrepTime, CreatedBy)
            VALUES (@LI,  @M,  120, 10, @By),
                   (@LI,  @M2, 120, 40, @By),     -- Review Focus 1: 교체 시간 40 vs 5 → LINE-INJ-02 가 뽑힌다
                   (@LI2, @M2, 90,  5,  @By);
            INSERT INTO dbo.MD_Bop (BOPID, ItemNo, RoutingType, StepSeq, StationCode, ActiveFlag, CreatedBy)
            VALUES ('ITEST-APS-BOP-10', @FG,  'A', 10, 'ST-INJ-01', 1, @By),
                   ('ITEST-APS-BOP-20', @FG,  'A', 20, 'ST-IMG-01', 1, @By),
                   ('ITEST-APS-BOP-30', @SIB, 'A', 20, 'ST-IMG-01', 1, @By),
                   ('ITEST-APS-BOP-40', @TWO, 'A', 20, 'ST-IMG-01', 1, @By);   -- INJ 스테이션 없음 → MD_MoldLine 교체 최소 라인
            INSERT INTO dbo.WH_Inventory (LotNo, UnitType, PartNo, LocationNo, Qty, ReceivedAt)
            VALUES ('ITEST-APS-LOT-FG1', 'PART', @FG, NULL, 15, SYSDATETIME()),        -- 위치 무관: NULL 위치·완제품 위치 모두 합산
                   ('ITEST-APS-LOT-FG2', 'PART', @FG, 'FG-A-01', 10, SYSDATETIME()),
                   ('ITEST-APS-LOT-FG3', 'PART', @FG, NULL, 0, SYSDATETIME());          -- Qty 0 은 제외
            INSERT INTO dbo.PP_CustomerOrder (SoNumber, SoLineNo, ItemNo, OrderQty, ShippedQty, RequestedDeliveryDate, Status, CreatedBy)
            VALUES ('ITEST-APS-SO1', 1, @FG,  100, 0,  @D1,  'Confirmed', @By),
                   ('ITEST-APS-SO2', 1, @FG,  50,  0,  @D1,  'Open',      @By),
                   ('ITEST-APS-SO3', 1, @SIB, 30,  10, @D0,  'Confirmed', @By),
                   ('ITEST-APS-SO4', 1, @NR,  5,   0,  @D0,  'Confirmed', @By),
                   ('ITEST-APS-SO5', 1, @FG,  40,  0,  @Sat, 'Confirmed', @By),
                   ('ITEST-APS-SO6', 1, @FG,  9,   0,  NULL, 'Confirmed', @By),
                   ('ITEST-APS-SO7', 1, @NI,  7,   0,  @D0,  'Confirmed', @By),
                   ('ITEST-APS-SO8', 1, @TWO, 20,  0,  @D1,  'Confirmed', @By);
            """, ("@FG", Fg), ("@SIB", Sib), ("@NR", NoRt), ("@TWO", Two), ("@NI", NoItem), ("@M", Mold), ("@M2", Mold2),
                 ("@LI", LineInj), ("@LI2", LineInj2), ("@By", Actor), ("@D0", D0), ("@D1", D1), ("@Sat", Sat));
        SeedPattern(f);

        int wo = (int)Scalar(f, """
            INSERT INTO dbo.PP_WorkOrder (WoNumber, ItemNo, OrderQty, OpenQty, RoutingType, Status, CreatedBy, CreatedTS)
            OUTPUT INSERTED.WoID
            VALUES ('ITEST-APS-WO1', @FG, 60, 60, 'A', 'Released', @By, SYSDATETIME());
            """, ("@FG", Fg), ("@By", Actor))!;
        Exec(f, """
            INSERT INTO dbo.PP_WorkOrderRouting (WoID, StepSeq, ProcessCode, LineID, Status, CompletedQty, CreatedBy, CreatedTS)
            VALUES (@W, 1, 'INJ', @LI, 'In Progress', 50, @By, SYSDATETIME()),
                   (@W, 2, 'IMG', @LM, 'In Progress', 30, @By, SYSDATETIME());
            INSERT INTO dbo.PP_LineSchedule (LineID, ScheduleDate, WoID, StartMin, EndMin, PlannedQty, EntryType, Status, CreatedBy, CreatedTS)
            VALUES (@LI, @D1, @W, 480, 540, 60, 'WO', 'DRAFT', @By, SYSDATETIME()),
                   (@LM, @D2, @W, 480, 600, 60, 'WO', 'DRAFT', @By, SYSDATETIME());
            INSERT INTO dbo.PR_ProductionResult (WoID, LineID, ProcessCode, GoodQty, ProdDate, EntryAt, CreatedBy, CreatedTS)
            VALUES (@W, @LM, 'IMG', 12, @Prev, @Prev, @By, SYSDATETIME()),
                   (@W, @LM, 'IMG', -1, @Prev, @Prev, @By, SYSDATETIME()),
                   (@W, @LI, 'INJ', 20, @Prev, @Prev, @By, SYSDATETIME());
            """, ("@W", wo), ("@LI", LineInj), ("@LM", LineImg), ("@D1", D1), ("@D2", D2), ("@Prev", Prev), ("@By", Actor));
    }

    /// <summary>
    /// LINE-INJ-01 전용 3교대 패턴 ITEST-APS-PAT(A 08~16 · B 16~24 · C 00~08)를 만들고 APS 설정(기본 패턴 + LINE-INJ-01/02 라인 지정)으로 잡는다.
    /// 2026-10-06 부터 APS 는 패턴 설정이 없는 사출 라인이 나오면 조회를 막으므로 번들을 만드는 테스트는 Seed 를 거치지 않아도 이것을 부른다.
    /// 설정의 원래 값은 ApsPatternConfig 가 기억했다가 Cleanup 에서 되돌린다.
    /// </summary>
    static void SeedPattern(AmesConnectionFactory f)
    {
        Exec(f, """
            IF NOT EXISTS (SELECT 1 FROM dbo.MD_LineTimePattern WHERE PatternID = @P)
            BEGIN
                INSERT INTO dbo.MD_LineTimePattern (PatternID, LineID, PatternName, Status, CreatedBy)
                VALUES (@P, @LI, N'ITEST APS 3-shift', 'ACTIVE', @By);
                INSERT INTO dbo.MD_LineTimeSegment (SegmentID, PatternID, SeqNo, StartMin, EndMin, SegmentState, ShiftCode, CreatedBy)
                VALUES ('ITEST-APS-SEG-A', @P, 1, 480, 960,  'OPERATING', 'A', @By),
                       ('ITEST-APS-SEG-B', @P, 2, 960, 1440, 'OPERATING', 'B', @By),
                       ('ITEST-APS-SEG-C', @P, 3, 0,   480,  'OPERATING', 'C', @By);
            END
            """, ("@P", Pat), ("@LI", LineInj), ("@By", Actor));
        ApsPatternConfig.Apply(f, Pat, (LineInj, Pat), (LineInj2, Pat));
    }

    /// <summary>Review Focus 1 동률: 금형 ITEST-APS-M4(MoldChangeMin 7)가 LINE-INJ-01(PrepTime 7, UPH 100)·LINE-INJ-02(PrepTime NULL → MoldChangeMin 7, UPH 80) 에 배정.
    /// NULL 을 0 으로 읽는 구현이면 LINE-INJ-02 가 뽑히므로 COALESCE 가운데 항을 가려낸다.</summary>
    static void SeedTie(AmesConnectionFactory f) => Exec(f, """
        INSERT INTO dbo.MD_Item (ItemNo, ItemName, RoutingType, SafetyStock, BoxQty, ActiveFlag, CreatedBy)
        VALUES (@T, N'ITEST APS tie', 'A', 0, 5, 1, @By);
        INSERT INTO dbo.MD_Mold (MoldID, MoldName, CavityCount, Status, MoldChangeMin, CreatedBy)
        VALUES (@M4, N'ITEST APS mold 4', 1, 'AVAILABLE', 7, @By);
        INSERT INTO dbo.MD_MoldItem (MoldID, ItemNo, Color, CavitySeq, CavityPos, CavityCount, MoldCategory, ActiveFlag, CreatedBy)
        VALUES (@M4, @T, 'CBK', 1, 'LH', 1, 'INJECTION', 1, @By);
        INSERT INTO dbo.MD_MoldLine (LineCode, MoldID, UPH, PrepTime, CreatedBy)
        VALUES (@LI2, @M4, 80, NULL, @By), (@LI, @M4, 100, 7, @By);
        INSERT INTO dbo.MD_Bop (BOPID, ItemNo, RoutingType, StepSeq, StationCode, ActiveFlag, CreatedBy)
        VALUES ('ITEST-APS-BOP-50', @T, 'A', 20, 'ST-IMG-01', 1, @By);
        INSERT INTO dbo.PP_CustomerOrder (SoNumber, SoLineNo, ItemNo, OrderQty, ShippedQty, RequestedDeliveryDate, Status, CreatedBy)
        VALUES ('ITEST-APS-SO9', 1, @T, 10, 0, @D1, 'Confirmed', @By);
        """, ("@T", Tie), ("@M4", Mold4), ("@LI", LineInj), ("@LI2", LineInj2), ("@D1", D1), ("@By", Actor));

    /// <summary>
    /// BOM 규칙: 부모 ITEST-APS-BP(자기 금형 없음, IMG BOP) · 유효 버전 BV1(EffFrom D0−1) 자식 CH(QtyPer 1.5)·CH2(QtyPer 2, ScrapPct 10),
    /// 더 오래된 APPROVED 버전 BV0(CH QtyPer 9)은 EffectiveBomSql 이 버린다. 금형 ITEST-APS-M3(CavityCount 2) 에 CH·CH2 가 한 캐비티씩, LINE-INJ-02 UPH 100.
    /// 실적: BP IMG 직전 근무일 11 · 기준일 4, CH INJ 직전 근무일 20 · 기준일 8. CH 창고 재고 OnHand 110 − Reserved 10.
    /// </summary>
    static void SeedBomRule(AmesConnectionFactory f)
    {
        Exec(f, """
            INSERT INTO dbo.MD_Item (ItemNo, ItemName, ItemType, InjFlag, RoutingType, SafetyStock, BoxQty, ActiveFlag, CreatedBy)
            VALUES (@BP,  N'ITEST APS bom parent', 'ASSY', 0, 'A',  0, 12,   1, @By),
                   (@CH,  N'ITEST APS bom child',  'SUB',  1, NULL, 0, 50,   1, @By),
                   (@CH2, N'ITEST APS bom child2', 'SUB',  1, NULL, 0, NULL, 1, @By);
            INSERT INTO dbo.MD_Mold (MoldID, MoldName, CavityCount, Status, MoldChangeMin, CreatedBy)
            VALUES (@M3, N'ITEST APS mold 3', 2, 'AVAILABLE', 15, @By);
            INSERT INTO dbo.MD_MoldItem (MoldID, ItemNo, Color, CavitySeq, CavityPos, CavityCount, MoldCategory, ActiveFlag, CreatedBy)
            VALUES (@M3, @CH,  'CBK', 1, 'LH', 2, 'INJECTION', 1, @By),
                   (@M3, @CH2, 'CBK', 2, 'RH', 2, 'INJECTION', 1, @By);
            INSERT INTO dbo.MD_MoldLine (LineCode, MoldID, UPH, PrepTime, CreatedBy)
            VALUES (@LI2, @M3, 100, 5, @By);
            INSERT INTO dbo.MD_Bop (BOPID, ItemNo, RoutingType, StepSeq, StationCode, ActiveFlag, CreatedBy)
            VALUES ('ITEST-APS-BOP-60', @BP, 'A', 20, 'ST-IMG-01', 1, @By);
            INSERT INTO dbo.MD_BomVersion (VersionID, RootItemNo, VersionNo, EffFrom, EffTo, Status, CreatedBy)
            VALUES ('ITEST-APS-BV0', @BP, '0', @Old,  NULL, 'APPROVED', @By),
                   ('ITEST-APS-BV1', @BP, '1', @Eff,  NULL, 'APPROVED', @By);
            INSERT INTO dbo.MD_Bom (BOMID, ParentItemNo, CompItemNo, QtyPer, ScrapPct, VersionID, ActiveFlag, CreatedBy)
            VALUES ('ITEST-APS-BOM0', @BP, @CH,  9,   0,  'ITEST-APS-BV0', 1, @By),
                   ('ITEST-APS-BOM1', @BP, @CH,  1.5, 0,  'ITEST-APS-BV1', 1, @By),
                   ('ITEST-APS-BOM2', @BP, @CH2, 2,   10, 'ITEST-APS-BV1', 1, @By);
            INSERT INTO dbo.PP_CustomerOrder (SoNumber, SoLineNo, ItemNo, OrderQty, ShippedQty, RequestedDeliveryDate, Status, CreatedBy)
            VALUES ('ITEST-APS-SO10', 1, @BP, 30, 0, @D1, 'Confirmed', @By);
            INSERT INTO dbo.WH_Inventory (LotNo, UnitType, PartNo, LocationNo, Qty, ReceivedAt)
            VALUES ('ITEST-APS-LOT-CH1', 'PART', @CH, NULL, 100, SYSDATETIME());
            """, ("@BP", Bp), ("@CH", Ch), ("@CH2", Ch2), ("@M3", Mold3), ("@LI2", LineInj2), ("@By", Actor),
                 ("@Old", D0.AddDays(-30)), ("@Eff", D0.AddDays(-1)), ("@D1", D1));
        int wBp = NewWo(f, "ITEST-APS-WO2", Bp);
        int wCh = NewWo(f, "ITEST-APS-WO3", Ch);
        Exec(f, """
            INSERT INTO dbo.PR_ProductionResult (WoID, LineID, ProcessCode, GoodQty, ProdDate, EntryAt, CreatedBy, CreatedTS)
            VALUES (@WB, @LM,  'IMG', 11, @Prev, @Prev, @By, SYSDATETIME()),
                   (@WB, @LM,  'IMG', 4,  @D0,   @D0,   @By, SYSDATETIME()),
                   (@WC, @LI2, 'INJ', 20, @Prev, @Prev, @By, SYSDATETIME()),
                   (@WC, @LI2, 'INJ', 8,  @D0,   @D0,   @By, SYSDATETIME());
            """, ("@WB", wBp), ("@WC", wCh), ("@LM", LineImg), ("@LI2", LineInj2), ("@Prev", Prev), ("@D0", D0), ("@By", Actor));
    }

    static int NewWo(AmesConnectionFactory f, string woNumber, string item, string routingType = "A", string status = "Released") => (int)Scalar(f, """
        INSERT INTO dbo.PP_WorkOrder (WoNumber, ItemNo, OrderQty, OpenQty, RoutingType, Status, CreatedBy, CreatedTS)
        OUTPUT INSERTED.WoID
        VALUES (@No, @I, 10, 10, @RT, @St, @By, SYSDATETIME());
        """, ("@No", woNumber), ("@I", item), ("@RT", routingType), ("@St", status), ("@By", Actor))!;

    static void Slot(AmesConnectionFactory f, int woId, string line, DateTime date, int startMin, int endMin, decimal qty, string? patternId = null) => Exec(f, """
        INSERT INTO dbo.PP_LineSchedule (LineID, ScheduleDate, WoID, StartMin, EndMin, PlannedQty, PatternID, EntryType, Status, CreatedBy, CreatedTS)
        VALUES (@L, @D, @W, @S, @E, @Q, @P, 'WO', 'DRAFT', @By, SYSDATETIME());
        """, ("@L", line), ("@D", date), ("@W", woId), ("@S", startMin), ("@E", endMin), ("@Q", qty), ("@P", (object?)patternId ?? DBNull.Value), ("@By", Actor));

    static int Wo1(AmesConnectionFactory f) =>
        (int)Scalar(f, "SELECT WoID FROM dbo.PP_WorkOrder WHERE WoNumber = 'ITEST-APS-WO1' AND CreatedBy = @By;", ("@By", Actor))!;

    /// <summary>수주 1건(Confirmed, D1) — 품번이 수요 후보가 되게 한다.</summary>
    static void Order(AmesConnectionFactory f, string soNumber, string item, decimal qty) => Exec(f, """
        INSERT INTO dbo.PP_CustomerOrder (SoNumber, SoLineNo, ItemNo, OrderQty, ShippedQty, RequestedDeliveryDate, Status, CreatedBy)
        VALUES (@No, 1, @I, @Q, 0, @D1, 'Confirmed', @By);
        """, ("@No", soNumber), ("@I", item), ("@Q", qty), ("@D1", D1), ("@By", Actor));

    /// <summary>경고 경로 품번 4개 — NOL(BOP 없음), NML(금형 M6 은 있으나 라인 배정·BOP INJ 없음), NUP(BOP INJ + M7 UPH 0), BX(금형·BOM 없음).</summary>
    static void SeedWarningCases(AmesConnectionFactory f)
    {
        Exec(f, """
            INSERT INTO dbo.MD_Item (ItemNo, ItemName, RoutingType, SafetyStock, BoxQty, ActiveFlag, CreatedBy)
            VALUES (@NOL, N'ITEST APS no line',     'A', 0, 5, 1, @By),
                   (@NML, N'ITEST APS no mold line', 'A', 0, 5, 1, @By),
                   (@NUP, N'ITEST APS zero uph',     'A', 0, 5, 1, @By);
            INSERT INTO dbo.MD_Mold (MoldID, MoldName, CavityCount, Status, MoldChangeMin, CreatedBy)
            VALUES (@M6, N'ITEST APS mold 6', 1, 'AVAILABLE', 10, @By),
                   (@M7, N'ITEST APS mold 7', 1, 'AVAILABLE', 10, @By);
            INSERT INTO dbo.MD_MoldItem (MoldID, ItemNo, Color, CavitySeq, CavityPos, CavityCount, MoldCategory, ActiveFlag, CreatedBy)
            VALUES (@M6, @NML, 'CBK', 1, 'LH', 1, 'INJECTION', 1, @By),
                   (@M7, @NUP, 'CBK', 1, 'LH', 1, 'INJECTION', 1, @By);
            INSERT INTO dbo.MD_MoldLine (LineCode, MoldID, UPH, PrepTime, CreatedBy)
            VALUES (@LI, @M7, 0, 5, @By);
            INSERT INTO dbo.MD_Bop (BOPID, ItemNo, RoutingType, StepSeq, StationCode, ActiveFlag, CreatedBy)
            VALUES ('ITEST-APS-BOP-80', @NML, 'A', 20, 'ST-IMG-01', 1, @By),
                   ('ITEST-APS-BOP-81', @NUP, 'A', 10, 'ST-INJ-01', 1, @By),
                   ('ITEST-APS-BOP-82', @NUP, 'A', 20, 'ST-IMG-01', 1, @By);
            """, ("@NOL", NoL), ("@NML", NmL), ("@NUP", NUp), ("@M6", Mold6), ("@M7", Mold7), ("@LI", LineInj), ("@By", Actor));
        Order(f, "ITEST-APS-SO12", NoL, 5);
        Order(f, "ITEST-APS-SO13", NmL, 5);
        Order(f, "ITEST-APS-SO14", NUp, 5);
    }

    /// <summary>앞자리 쌍: BX(BoxQty 24)가 BXL(BoxQty NULL)의 앞자리. 둘 다 라우팅 A · IMG BOP 만 · 금형·BOM 없음(완제품 행만).</summary>
    static void SeedBoxPair(AmesConnectionFactory f)
    {
        Exec(f, """
            INSERT INTO dbo.MD_Item (ItemNo, ItemName, RoutingType, SafetyStock, BoxQty, ActiveFlag, CreatedBy)
            VALUES (@BX,  N'ITEST APS box 24',   'A', 0, 24,   1, @By),
                   (@BXL, N'ITEST APS box none', 'A', 0, NULL, 1, @By);
            INSERT INTO dbo.MD_Bop (BOPID, ItemNo, RoutingType, StepSeq, StationCode, ActiveFlag, CreatedBy)
            VALUES ('ITEST-APS-BOP-90', @BX,  'A', 20, 'ST-IMG-01', 1, @By),
                   ('ITEST-APS-BOP-91', @BXL, 'A', 20, 'ST-IMG-01', 1, @By);
            """, ("@BX", Bx), ("@BXL", Bxl), ("@By", Actor));
        Order(f, "ITEST-APS-SO15", Bx, 5);
        Order(f, "ITEST-APS-SO16", Bxl, 5);
    }

    /// <summary>ITEST-APS-FG 출고 1건 — 통합재고 거래 이력 WH_InventoryTransaction 의 OUT 행(TransactionTime = at 10:00, 위치 무관).</summary>
    static void SeedShipment(AmesConnectionFactory f, string tag, DateTime at, decimal qty) => Exec(f, """
        INSERT INTO dbo.WH_InventoryTransaction (TransactionTime, TransactionType, PartNo, LocationNo, LotNo, QtyChange, ReasonCode, SourceType, Note, CreatedBy, CreatedTS)
        VALUES (@At, 'OUT', @FG, NULL, 'ITEST-APS-' + @T, -@Q, 'ITEST', 'ITEST-APS', N'ITEST-APS shipment ' + @T, @By, SYSDATETIME());
        """, ("@T", tag), ("@FG", Fg), ("@Q", qty), ("@At", at.AddHours(10)), ("@By", Actor));

    internal static void Cleanup(AmesConnectionFactory f)
    {
        ApsPatternConfig.Restore(f);   // 테스트 패턴을 지우기 전에 — MD_ApsLineStage.PatternID FK
        Exec(f, """
            DELETE FROM dbo.PP_DemandPlan      WHERE Batch LIKE 'ITEST-APS-%';
            DELETE FROM dbo.PP_DemandPlanBatch WHERE Batch LIKE 'ITEST-APS-%';
            DELETE FROM dbo.PP_ApsRunWo    WHERE RunID IN (SELECT RunID FROM dbo.PP_ApsRun WHERE CreatedBy = @By);
            DELETE FROM dbo.PP_ApsPlanLine WHERE RunID IN (SELECT RunID FROM dbo.PP_ApsRun WHERE CreatedBy = @By);
            DELETE FROM dbo.PP_ApsRun      WHERE CreatedBy = @By;
            DELETE FROM dbo.MD_ApsLineStage WHERE LineID = @LN;
            DELETE FROM dbo.MD_Line         WHERE LineID = @LN;
            DELETE FROM dbo.WH_InventoryTransaction WHERE SourceType = 'ITEST-APS' AND CreatedBy = @By;
            DELETE s FROM dbo.PP_LineSchedule s JOIN dbo.PP_WorkOrder w ON w.WoID = s.WoID WHERE w.WoNumber LIKE 'ITEST-APS-WO%' AND w.CreatedBy = @By;
            DELETE r FROM dbo.PR_ProductionResult r JOIN dbo.PP_WorkOrder w ON w.WoID = r.WoID WHERE w.WoNumber LIKE 'ITEST-APS-WO%' AND w.CreatedBy = @By;
            DELETE r FROM dbo.PP_WorkOrderRouting r JOIN dbo.PP_WorkOrder w ON w.WoID = r.WoID WHERE w.WoNumber LIKE 'ITEST-APS-WO%' AND w.CreatedBy = @By;
            DELETE FROM dbo.PP_WorkOrder       WHERE WoNumber LIKE 'ITEST-APS-WO%' AND CreatedBy = @By;
            DELETE FROM dbo.PP_CustomerOrder   WHERE SoNumber LIKE 'ITEST-APS-SO%' AND CreatedBy = @By;
            DELETE FROM dbo.WH_Inventory       WHERE LotNo LIKE 'ITEST-APS-LOT-%';
            DELETE FROM dbo.MD_Bom             WHERE BOMID LIKE 'ITEST-APS-BOM%' AND CreatedBy = @By;
            DELETE FROM dbo.MD_BomVersion      WHERE VersionID LIKE 'ITEST-APS-BV%' AND CreatedBy = @By;
            DELETE FROM dbo.MD_Bop             WHERE BOPID LIKE 'ITEST-APS-BOP-%' AND CreatedBy = @By;
            DELETE FROM dbo.MD_MoldItem        WHERE MoldID IN (@M, @M2, @M3, @M4, @M5, @M6, @M7, @M8, @M9) AND CreatedBy = @By;
            DELETE FROM dbo.MD_MoldLine        WHERE MoldID IN (@M, @M2, @M3, @M4, @M5, @M6, @M7, @M8, @M9) AND CreatedBy = @By;
            DELETE FROM dbo.MD_Mold            WHERE MoldID IN (@M, @M2, @M3, @M4, @M5, @M6, @M7, @M8, @M9) AND CreatedBy = @By;
            DELETE FROM dbo.MD_Item            WHERE ItemNo IN (@FG, @SIB, @NR, @TWO, @TIE, @BP, @CH, @CH2, @RC, @NOL, @NML, @NUP, @BX, @BXL, @OFF, @ODA, @ODB, @CLN, @CLY, @MAT, @BP2, @MID, @MID2, @NOINJ) AND CreatedBy = @By;
            DELETE FROM dbo.MD_LineTimeSegment WHERE PatternID IN (@P, @P2) AND CreatedBy = @By;
            DELETE FROM dbo.MD_LineTimePattern WHERE PatternID IN (@P, @P2) AND CreatedBy = @By;
            DELETE FROM dbo.MD_Line            WHERE LineID = @CL AND CreatedBy = @By;
            """, ("@M", Mold), ("@M2", Mold2), ("@P", Pat), ("@M3", Mold3), ("@M4", Mold4), ("@M5", Mold5), ("@M6", Mold6), ("@M7", Mold7),
                 ("@M8", Mold8), ("@M9", Mold9), ("@P2", Pat2), ("@By", Actor), ("@CL", CapLine), ("@LN", TestLine),
                 ("@FG", Fg), ("@SIB", Sib), ("@NR", NoRt), ("@TWO", Two), ("@TIE", Tie), ("@BP", Bp), ("@CH", Ch), ("@CH2", Ch2),
                 ("@RC", Rc), ("@NOL", NoL), ("@NML", NmL), ("@NUP", NUp), ("@BX", Bx), ("@BXL", Bxl), ("@OFF", Off),
                 ("@ODA", OdA), ("@ODB", OdB), ("@CLN", ClN), ("@CLY", ClY), ("@MAT", Mat), ("@BP2", Bp2), ("@MID", Mid), ("@MID2", Mid2), ("@NOINJ", NoInj));
    }

    [SkippableFact]
    public void BuildBundle_assembly_line_builds_rows_edges_locked_cells_stock_and_warnings()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Skip.If(RoutingA(f!) != "INJ,IMG", "dev routing A is not INJ→IMG");
        Seed(f!);
        try
        {
            var b  = new ApsRepository(f!).BuildBundle(new ApsQuery(LineImg, Base, 5));
            var bd = b.Bundle;

            Assert.Equal(LineImg, bd.Line.LineCd);
            Assert.Equal(ApsRepository.LineTypeAssembly, bd.Line.Type);
            Assert.Equal(ApsRepository.SourceAmes, bd.Source);
            Assert.Equal(Iso(D0), bd.BaseDate);
            Assert.Equal(Enumerable.Range(0, 5).Select(i => Iso(D0.AddDays(i))).ToList(), bd.Dates);
            Assert.Equal(Iso(Prev), bd.PrevDate);
            Assert.Same(b.Warnings, bd.Warnings);
            Assert.All(bd.Assembly, r => Assert.Equal(LineImg, r.LineCd));   // 완제품 라인 모드: 다른 라인의 완제품은 없다

            // ── 완제품 행 ──
            var fgRow  = Assert.Single(bd.Assembly, r => r.PartNo == Fg);
            var sibRow = Assert.Single(bd.Assembly, r => r.PartNo == Sib);
            Assert.DoesNotContain(bd.Assembly, r => r.PartNo == NoRt);
            Assert.Equal(new double[] { 0, 100, 0, 0, 40 }, fgRow.Days.Select(d => d.Demand).ToArray());   // Open 50·NULL 납기 9 제외, 토요일 40 → 금요일
            Assert.Equal(new double[] { 20, 0, 0, 0, 0 },  sibRow.Days.Select(d => d.Demand).ToArray());  // 30 − 출하 10
            Assert.Equal(LineImg, fgRow.LineCd);
            Assert.Equal(("Q019", "8146", "HL", 10d), (fgRow.Pgn, fgRow.Alc, fgRow.Model, fgRow.SafetyStock));
            Assert.Equal("ITEST APS finished", fgRow.PartName);
            Assert.Equal(25d, fgRow.OpeningStock);              // 통합재고 15 + 10 (Qty 0 행 제외, 위치 무관), 기준일 실적·출고 0
            Assert.Equal((0d, 0d, 0d), (fgRow.SisProduced, fgRow.Shipped, fgRow.Defect));
            Assert.True(fgRow.Days[2].Locked);
            Assert.Equal(60d, fgRow.Days[2].Supply);            // D2 LINE-IMG-01 등록 슬롯
            Assert.False(fgRow.Days[1].Locked);
            Assert.All(fgRow.Days, d => Assert.Equal(0d, d.T));

            // ── 사출 행 ──
            var fgInj  = Assert.Single(bd.Injection, r => r.PartNo == Fg);
            var sibInj = Assert.Single(bd.Injection, r => r.PartNo == Sib);
            Assert.Equal(LineInj, fgInj.LineCd);
            Assert.Equal(LineInj, fgInj.Group);
            Assert.Equal(Mold, fgInj.MoldCode);
            Assert.Equal(60d, fgInj.Uph);                       // 120 × 1 ÷ 2
            Assert.Equal(1, fgInj.Cavity);
            Assert.Equal(20, fgInj.PackSize);
            Assert.Equal(new[] { Sib }, fgInj.SiblingPartNos);
            Assert.Equal(20d, fgInj.OpeningStock);              // WIP = INJ 50 − IMG 30
            Assert.True(fgInj.Days[1].Locked);
            Assert.Equal((60d, 0d), (fgInj.Days[1].PlanDay, fgInj.Days[1].PlanNight));   // StartMin 480 = A 교대 → 주간
            Assert.False(fgInj.Days[0].Locked);
            Assert.All(fgInj.Days, d => Assert.Equal(0d, d.Requirement));
            Assert.Equal(LineInj, sibInj.LineCd);               // BOP INJ 스테이션 없음 → MD_MoldLine 교체 최소 라인
            Assert.Equal(1, sibInj.Cavity);                     // NULL → floor(2 ÷ 2)
            Assert.Equal(1, sibInj.PackSize);                   // BoxQty NULL
            Assert.Equal(60d, sibInj.Uph);
            Assert.Equal(new[] { Fg }, sibInj.SiblingPartNos);
            Assert.Equal(0d, sibInj.OpeningStock);

            var testBom = bd.Bom.Where(e => IsTest(e.ParentPartNo)).ToList();
            Assert.Equal(3, testBom.Count);                     // Fg·Sib·Two 자기 간선(Two 는 LINE-INJ-02 사출 행 — 아래 Review Focus 1 테스트)
            Assert.Contains(new BomEdge(Fg, Fg, 1), testBom);
            Assert.Contains(new BomEdge(Sib, Sib, 1), testBom);
            Assert.Contains(new BomEdge(Two, Two, 1), testBom);

            // ── Settings ──
            var s = b.Settings;
            var imgShift = Assert.Single(s.LineShifts, l => l.LineCd == LineImg);
            var imgCap = Scalar(f!, "SELECT DailyCap FROM dbo.MD_Line WHERE LineID = @L;", ("@L", LineImg)) as int?;
            Assert.Equal(imgCap is > 0 ? imgCap : null, imgShift.DailyCap);   // NULL·0 이하는 "제한 없음"(경고) — 아래 DailyCap 테스트
            var injShift = Assert.Single(s.LineShifts, l => l.LineCd == LineInj);
            Assert.Equal((8d, 16d), (injShift.Day, injShift.Night));
            Assert.Null(injShift.DailyCap);
            Assert.DoesNotContain(s.ShiftExceptions, x => x.LineCd == LineInj || x.LineCd == LineInj2);
            Assert.Contains(new PackRule(Fg, 20), s.PackRules);
            Assert.Contains(new PackRule(Sib, 1), s.PackRules);
            Assert.Empty(s.UphRules);
            Assert.Equal(3, s.CoverTiers.Count);                // migrate_aps.sql 시드
            Assert.All(s.LineStages, st => Assert.Null(st.RootLine));   // MD_ApsLineStage 시드 유무에 기대지 않는다
            Assert.Equal(0, b.Stages.OffsetDays(LineImg, LineImg));      // 루트 라인 자신은 선행일 0
            Assert.True(b.Stages.OffsetDays(LineInj, LineImg) >= 0);     // MD_ApsLineStage 행 또는 INJ_OFFSET_DAYS 기본(1)

            // ── 등록 WO(셀의 "WO" 줄): 완제품 행은 완제품 라인 슬롯, 사출 행은 사출 라인 슬롯 — WO 번호·수량·슬롯 품번 ──
            int wo1 = Wo1(f!);
            Assert.Contains(b.RegisteredWos, w => w.Kind == ApsRepository.KindAsm && w.ItemNo == Fg && w.Date == Iso(D2) && w.LineId == LineImg
                                               && w.WoId == wo1 && w.WoNumber == "ITEST-APS-WO1" && w.Qty == 60 && w.SlotItemNo == Fg);
            Assert.Contains(b.RegisteredWos, w => w.Kind == ApsRepository.KindInj && w.ItemNo == Fg && w.Date == Iso(D1) && w.LineId == LineInj
                                               && w.WoId == wo1 && w.Qty == 60 && w.SlotItemNo == Fg);
            Assert.DoesNotContain(b.RegisteredWos, w => w.ItemNo == Sib);

            // ── 경고 ──
            Assert.Contains(b.Warnings, w => w.Contains(NoRt) && w.Contains("RoutingType"));
            Assert.Contains(b.Warnings, w => w.Contains(NoItem));
            Assert.Contains(b.Warnings, w => w.Contains("ITEST-APS-SO6") && w.Contains("납기"));
            Assert.DoesNotContain(b.Warnings, w => w.Contains("균등 분할") && (w.Contains(Fg) || w.Contains(Sib)));   // 캐비티 2 ÷ 품번 2 = 나누어떨어진다 — 경고 없음
            Assert.Equal(new[] { Fg }, sibInj.SiblingPartNos);
            Assert.Equal("CBK", sibInj.MoldColor);
            Assert.Contains(b.Warnings, w => w.Contains(Sib) && w.Contains("BoxQty"));
            Assert.Contains(b.Warnings, w => w.Contains("재고는 현재값 기준"));
            Assert.DoesNotContain(b.Warnings, w => w.Contains("UPH 가 없어") && w.Contains("ITEST-APS-"));
            Assert.DoesNotContain(b.Warnings, w => w.Contains("교대 시간 폴백") && w.Contains(LineInj));   // LINE-INJ-02 는 전역 패턴(Notes #6)이라 라인별로 본다

            // ── D-1 표시값: 완제품 항목 먼저, 사출 항목 뒤 ──
            Assert.Equal(new ApsActualInfo(Fg, 11, 0, 0),  b.Actuals.First(a => a.ItemNo == Fg));   // IMG 12 + (−1)
            Assert.Equal(new ApsActualInfo(Fg, 20, 0, 11), b.Actuals.Last(a => a.ItemNo == Fg));    // INJ 20, 사용 = 다음 단계(IMG) 11 × 1
            Assert.Equal(new ApsActualInfo(Sib, 0, 0, 0),  b.Actuals.First(a => a.ItemNo == Sib));
        }
        finally { Cleanup(f!); }
    }

    [SkippableFact]
    public void BuildBundle_injection_line_mode_keeps_that_lines_injection_rows_and_their_parents()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Skip.If(RoutingA(f!) != "INJ,IMG", "dev routing A is not INJ→IMG");
        Seed(f!);
        try
        {
            var b  = new ApsRepository(f!).BuildBundle(new ApsQuery(LineInj, Base, 5));
            var bd = b.Bundle;

            Assert.Equal(ApsRepository.LineTypeInjection, bd.Line.Type);
            Assert.All(bd.Injection, r => Assert.Equal(LineInj, r.LineCd));
            Assert.Equal(new[] { Fg, Sib }, bd.Injection.Where(r => IsTest(r.PartNo)).Select(r => r.PartNo).OrderBy(x => x, StringComparer.Ordinal).ToArray());
            Assert.DoesNotContain(bd.Injection, r => r.PartNo == Two);   // LINE-INJ-02 로 정해진 사출 행은 LINE-INJ-01 모드에서 빠진다(부모 완제품 행도)
            Assert.DoesNotContain(bd.Assembly,  r => r.PartNo == Two);
            Assert.Equal(new[] { Fg, Sib }, bd.Assembly.Where(r => IsTest(r.PartNo)).Select(r => r.PartNo).OrderBy(x => x, StringComparer.Ordinal).ToArray());
            Assert.Equal(2, bd.Bom.Count(e => IsTest(e.ParentPartNo)));
            Assert.Single(b.Settings.LineShifts, l => l.LineCd == LineInj);
            Assert.Single(b.Settings.LineShifts, l => l.LineCd == LineImg);
            Assert.Equal(60d, bd.Injection.Single(r => r.PartNo == Fg).Days[1].PlanDay);
        }
        finally { Cleanup(f!); }
    }

    /// <summary>
    /// 전체 라인 모드("-"): 완제품 라인을 걸러내지 않고 모든 완제품 라인의 행을 한 번들에 담되, 행마다 자기 라인이 남고 능력은 라인마다 따로 걸린다.
    /// 라우팅 C 품번(완제품 라인 = LINE-INJ-01)은 LINE-IMG-01 단독 모드에서는 빠지지만 전체 모드에서는 LINE-IMG-01 행들과 함께 들어온다.
    /// </summary>
    [SkippableFact]
    public void BuildBundle_all_lines_keeps_every_finished_line_row_with_its_own_line_and_capacity()
    {
        const string All = "-";
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Skip.If(RoutingA(f!) != "INJ,IMG", "dev routing A is not INJ→IMG");
        Skip.If(Routing(f!, "C") != "INJ", "dev routing C is not INJ-only");
        Seed(f!);
        try
        {
            Exec(f!, """
                INSERT INTO dbo.MD_Item (ItemNo, ItemName, RoutingType, SafetyStock, BoxQty, ActiveFlag, CreatedBy)
                VALUES (@RC, N'ITEST APS routing C', 'C', 0, 10, 1, @By);
                INSERT INTO dbo.MD_Mold (MoldID, MoldName, CavityCount, Status, MoldChangeMin, CreatedBy)
                VALUES (@M5, N'ITEST APS mold 5', 1, 'AVAILABLE', 10, @By);
                INSERT INTO dbo.MD_MoldItem (MoldID, ItemNo, Color, CavitySeq, CavityPos, CavityCount, MoldCategory, ActiveFlag, CreatedBy)
                VALUES (@M5, @RC, 'CBK', 1, 'LH', 1, 'INJECTION', 1, @By);
                INSERT INTO dbo.MD_MoldLine (LineCode, MoldID, UPH, PrepTime, CreatedBy)
                VALUES (@LI, @M5, 60, 5, @By);
                INSERT INTO dbo.MD_Bop (BOPID, ItemNo, RoutingType, StepSeq, StationCode, ActiveFlag, CreatedBy)
                VALUES ('ITEST-APS-BOP-70', @RC, 'C', 10, 'ST-INJ-01', 1, @By);
                INSERT INTO dbo.WH_Inventory (LotNo, UnitType, PartNo, Qty, ReceivedAt)
                VALUES ('ITEST-APS-LOT-RC1', 'PART', @RC, 40, SYSDATETIME());
                """, ("@RC", Rc), ("@M5", Mold5), ("@LI", LineInj), ("@By", Actor));
            var repo = new ApsRepository(f!);

            var all = repo.BuildBundle(new ApsQuery(All, Base, 5));
            var img = repo.BuildBundle(new ApsQuery(LineImg, Base, 5));

            Assert.Equal(All, all.Bundle.Line.LineCd);
            Assert.Equal(ApsRepository.LineTypeAssembly, all.Bundle.Line.Type);
            Assert.DoesNotContain(all.Warnings, w => w.Contains("활성 INJ/IMG/PNT 라인이 아니라"));

            // LINE-IMG-01 단독 모드의 행이 전부, 자기 라인 그대로 들어온다
            var imgItems = img.Bundle.Assembly.Where(r => IsTest(r.PartNo)).Select(r => r.PartNo).ToList();
            Assert.NotEmpty(imgItems);
            Assert.All(imgItems, p => Assert.Contains(all.Bundle.Assembly, r => r.PartNo == p && r.LineCd == LineImg));
            // 다른 완제품 라인(LINE-INJ-01, 라우팅 C)의 행도 같이 — 단독 모드에서는 빠지는 행이다
            Assert.Contains(all.Bundle.Assembly, r => r.PartNo == Rc && r.LineCd == LineInj);
            Assert.DoesNotContain(img.Bundle.Assembly, r => r.PartNo == Rc);
            // 사출 행은 사출 라인 두 곳 모두
            Assert.Contains(all.Bundle.Injection, r => r.PartNo == Fg  && r.LineCd == LineInj);
            Assert.Contains(all.Bundle.Injection, r => r.PartNo == Two && r.LineCd == LineInj2);

            // 능력은 라인마다 — 전체 표식에는 LineShift 도 DailyCap 경고도 없다
            Assert.Single(all.Settings.LineShifts, l => l.LineCd == LineImg);
            Assert.DoesNotContain(all.Settings.LineShifts, l => l.LineCd == All);
            Assert.DoesNotContain(all.Warnings, w => w.Contains($"라인 {All}"));
            Assert.Equal(img.Rules.AsmCapFor(LineImg, all.Bundle.Dates[0]), all.Rules.AsmCapFor(LineImg, all.Bundle.Dates[0]));
        }
        finally { Cleanup(f!); }
    }

    [SkippableFact]
    public void BuildBundle_picks_injection_line_with_minimum_change_time_when_mold_is_assigned_to_two_lines()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Skip.If(RoutingA(f!) != "INJ,IMG", "dev routing A is not INJ→IMG");
        Seed(f!);
        try
        {
            SeedTie(f!);
            var b  = new ApsRepository(f!).BuildBundle(new ApsQuery(LineImg, Base, 5));
            var bd = b.Bundle;

            // Review Focus 1: BOP INJ 스테이션 없음 → MD_MoldLine 중 COALESCE(PrepTime, MoldChangeMin, 0) 최소 라인 하나(LINE-INJ-01 40 vs LINE-INJ-02 5)
            var two = Assert.Single(bd.Injection, r => r.PartNo == Two);
            Assert.Equal(LineInj2, two.LineCd);
            Assert.Equal(LineInj2, two.Group);
            Assert.Equal(Mold2, two.MoldCode);
            Assert.Equal(90d, two.Uph);                          // 그 (금형, 라인) 의 UPH 90 × (캐비티 1 ÷ 품번 1) — LINE-INJ-01 의 120 이 아니다
            Assert.Equal(10, two.PackSize);
            Assert.Equal(new[] { LineInj, LineInj2 }, bd.Injection.Where(r => IsTest(r.PartNo)).Select(r => r.LineCd!).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToArray());
            // LINE-INJ-02 는 자동 해석으로는 +414일 주에 유효한 패턴이 없지만(전역 LP-INJ-001/002 유효기간 2026-11/12 까지) APS 설정의 라인 지정(ITEST-APS-PAT)이 유효기간과 무관하게 쓰인다 — 교대 시간 폴백은 더 이상 없다(2026-10-06)
            var inj2Shift = Assert.Single(b.Settings.LineShifts, l => l.LineCd == LineInj2);
            Assert.Equal((8d, 16d), (inj2Shift.Day, inj2Shift.Night));
            Assert.DoesNotContain(b.Warnings, w => w.Contains("교대 시간 폴백"));
            Assert.DoesNotContain(b.Settings.ShiftExceptions, x => x.LineCd == LineInj2);
            Assert.DoesNotContain(b.Warnings, w => w.Contains(Two) && w.Contains("사출 라인을 정할 수 없습니다"));

            // 동률(LINE-INJ-01 PrepTime 7 = LINE-INJ-02 PrepTime NULL → MoldChangeMin 7) → LineCode Ordinal 첫 라인, UPH 도 그 라인 것
            var tie = Assert.Single(bd.Injection, r => r.PartNo == Tie);
            Assert.Equal(LineInj, tie.LineCd);
            Assert.Equal(Mold4, tie.MoldCode);
            Assert.Equal(100d, tie.Uph);
        }
        finally { Cleanup(f!); }
    }

    [SkippableFact]
    public void BuildBundle_saturday_base_starts_monday_keeps_chosen_base_and_prev_friday()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Skip.If(RoutingA(f!) != "INJ,IMG", "dev routing A is not INJ→IMG");
        Seed(f!);
        try
        {
            var sat = DateOnly.FromDateTime(D0.AddDays(-2));    // Review Focus 2: 토요일 기준일(그 주에 SYS_FactoryCalendar 행 없음 → 토·일 휴무)
            var b = new ApsRepository(f!).BuildBundle(new ApsQuery(LineImg, sat, 3));

            Assert.Equal(Iso(D0.AddDays(-2)), b.Bundle.BaseDate);                   // 고른 날짜 그대로(근무일로 접지 않는다) — SaveRun 도 이 값을 저장
            Assert.Equal(new[] { Iso(D0), Iso(D1), Iso(D2) }, b.Bundle.Dates);      // Dates[0] = 다음 근무일(월)
            Assert.Equal(Iso(Prev), b.Bundle.PrevDate);                             // D-1 = 금요일(PrevWorkDate)
            Assert.Equal(new double[] { 0, 100, 0 }, b.Bundle.Assembly.Single(r => r.PartNo == Fg).Days.Select(d => d.Demand).ToArray());   // 토요일 납기 40 → 금요일은 3일 창 밖
            Assert.Equal(new ApsActualInfo(Fg, 11, 0, 0), b.Actuals.First(a => a.ItemNo == Fg));   // D-1 = 금요일 실적
            Assert.Contains(b.Warnings, w => w.Contains("재고는 현재값 기준"));
        }
        finally { Cleanup(f!); }
    }

    [SkippableFact]
    public void BuildBundle_backs_out_base_date_actuals_keeps_injection_only_plans_and_applies_demand_filters()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Skip.If(RoutingA(f!) != "INJ,IMG", "dev routing A is not INJ→IMG");
        Seed(f!);
        try
        {
            int wo = (int)Scalar(f!, "SELECT WoID FROM dbo.PP_WorkOrder WHERE WoNumber = 'ITEST-APS-WO1' AND CreatedBy = @By;", ("@By", Actor))!;
            Exec(f!, """
                INSERT INTO dbo.PR_ProductionResult (WoID, LineID, ProcessCode, GoodQty, ProdDate, EntryAt, CreatedBy, CreatedTS)
                VALUES (@W, @LM, 'IMG', 5, @D0, @D0, @By, SYSDATETIME()),
                       (@W, @LI, 'INJ', 7, @D0, @D0, @By, SYSDATETIME()),
                       (@W, @LI2, 'IMG', 99, @D0, @D0, @By, SYSDATETIME());   -- 완제품 실적은 공정·라인이 모두 맞아야 한다(IMG 인데 라인이 다름 → 무시)
                """, ("@W", wo), ("@LM", LineImg), ("@LI", LineInj), ("@LI2", LineInj2), ("@D0", D0), ("@By", Actor));
            SeedShipment(f!, "0", D0, 6);
            SeedShipment(f!, "1", Prev, 9);
            // 사출 라인 슬롯만 있는 품번(완제품 라인 슬롯·수요·재고 없음)도 등록 계획이라 행이 남는다(스펙 §4.2·§4.5)
            int woTwo = NewWo(f!, "ITEST-APS-WO4", Two);
            Exec(f!, """
                INSERT INTO dbo.PP_LineSchedule (LineID, ScheduleDate, WoID, StartMin, EndMin, PlannedQty, EntryType, Status, CreatedBy, CreatedTS)
                VALUES (@LI2, @D1, @W, 1000, 1100, 40, 'WO', 'DRAFT', @By, SYSDATETIME());
                """, ("@LI2", LineInj2), ("@D1", D1), ("@W", woTwo), ("@By", Actor));

            var repo = new ApsRepository(f!);
            var b = repo.BuildBundle(new ApsQuery(LineImg, Base, 5));

            var fgRow = b.Bundle.Assembly.Single(r => r.PartNo == Fg);
            Assert.Equal(26d, fgRow.OpeningStock);                                    // 현재 25 − 기준일 IMG 생산 5 + 기준일 상차 6
            Assert.Equal(18d, b.Bundle.Injection.Single(r => r.PartNo == Fg).OpeningStock);   // WIP 20 − 기준일 INJ 7 + 기준일 다음 단계(IMG) 5 × 1
            Assert.Equal(new ApsActualInfo(Fg, 11, 9, 0),  b.Actuals.First(a => a.ItemNo == Fg));   // 직전 근무일 상차 9
            Assert.Equal(new ApsActualInfo(Fg, 20, 0, 11), b.Actuals.Last(a => a.ItemNo == Fg));

            // 사출 라인 슬롯(LINE-INJ-02 D1) → 잠긴 칸. 주/야 판정은 그 날 패턴에 달려 있어(개발 DB 의 전역 패턴 유효기간) 합계만 본다
            var twoInj = b.Bundle.Injection.Single(r => r.PartNo == Two);
            Assert.True(twoInj.Days[1].Locked);
            Assert.Equal(40d, twoInj.Days[1].PlanDay + twoInj.Days[1].PlanNight);

            // IncludeOpen → Open 50 도 D1 에
            var open = repo.BuildBundle(new ApsQuery(LineImg, Base, 5, IncludeOpen: true));
            Assert.Equal(new double[] { 0, 150, 0, 0, 40 }, open.Bundle.Assembly.Single(r => r.PartNo == Fg).Days.Select(d => d.Demand).ToArray());

            // 고객 필터 → 수요 0. 재고가 있는 Fg 와 사출 슬롯이 있는 Two 는 남고, 수요·재고·등록 계획이 모두 없는 Sib 는 빠진다
            var cust = repo.BuildBundle(new ApsQuery(LineImg, Base, 5, CustomerId: "ITEST-NOCUST"));
            Assert.All(cust.Bundle.Assembly.Single(r => r.PartNo == Fg).Days, d => Assert.Equal(0d, d.Demand));
            Assert.Contains(cust.Bundle.Assembly, r => r.PartNo == Two);
            Assert.True(cust.Bundle.Injection.Single(r => r.PartNo == Two).Days[1].Locked);
            Assert.DoesNotContain(cust.Bundle.Assembly,  r => r.PartNo == Sib);
            Assert.DoesNotContain(cust.Bundle.Injection, r => r.PartNo == Sib);
        }
        finally { Cleanup(f!); }
    }

    [SkippableFact]
    public void BuildBundle_bom_rule_children_use_effective_bom_scrap_warehouse_stock_and_parent_actuals_times_qtyper()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Skip.If(RoutingA(f!) != "INJ,IMG", "dev routing A is not INJ→IMG");
        Seed(f!);
        try
        {
            SeedBomRule(f!);
            var b  = new ApsRepository(f!).BuildBundle(new ApsQuery(LineImg, Base, 5));
            var bd = b.Bundle;

            Assert.Single(bd.Assembly, r => r.PartNo == Bp);
            Assert.Contains(new BomEdge(Bp, Ch, 1.5), bd.Bom);                     // BV1(최신 유효 버전) — BV0 의 QtyPer 9 는 버린다
            Assert.Contains(new BomEdge(Bp, Ch2, 2.2), bd.Bom);                    // 2 × (1 + 10/100)
            Assert.Equal(2, bd.Bom.Count(e => e.ParentPartNo == Bp));
            Assert.DoesNotContain(b.Warnings, w => w.Contains(Bp) && w.Contains("사출 행이 없습니다"));

            var ch = Assert.Single(bd.Injection, r => r.PartNo == Ch);
            Assert.Equal((LineInj2, Mold3, 50d, 1, 50), (ch.LineCd, ch.MoldCode, ch.Uph, ch.Cavity, ch.PackSize));   // UPH 100 × 1 ÷ 2
            Assert.Equal(new[] { Ch2 }, ch.SiblingPartNos);
            Assert.Equal(98d, ch.OpeningStock);                                    // 창고 100 − 기준일 INJ 8 + 기준일 부모 생산 4 × 1.5
            var ch2 = Assert.Single(bd.Injection, r => r.PartNo == Ch2);
            Assert.Equal(8.8, ch2.OpeningStock, 6);                                // 창고 0 − 0 + 4 × 2.2
            Assert.Equal(1, ch2.PackSize);

            // 사용 = 다음 단계(부모 완제품 공정·라인) 실적 × QtyPer — QtyPer 가 소수여도 그대로 곱한다
            Assert.Equal(new ApsActualInfo(Ch,  20, 0, 16.5m), b.Actuals.Single(a => a.ItemNo == Ch));
            Assert.Equal(new ApsActualInfo(Ch2, 0,  0, 24.2m), b.Actuals.Single(a => a.ItemNo == Ch2));
            Assert.Equal(new ApsActualInfo(Bp,  11, 0, 0),     b.Actuals.Single(a => a.ItemNo == Bp));
            Assert.Contains(new PackRule(Bp, 12), b.Settings.PackRules);           // 완제품 행도 BoxQty 로 포장 규칙(스펙 §4.2 "품번마다")
            Assert.Contains(b.Warnings, w => w.Contains(Ch2) && w.Contains("BoxQty"));
        }
        finally { Cleanup(f!); }
    }

    [SkippableFact]
    public void BuildBundle_assembly_line_without_positive_daily_cap_is_uncapped_with_warning()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Cleanup(f!);
        SeedPattern(f!);
        try
        {
            Exec(f!, """
                INSERT INTO dbo.MD_Line (LineID, LineName, WCID, DailyCap, Status, CreatedBy)
                SELECT @CL, N'ITEST APS no cap', l.WCID, 0, 'ACTIVE', @By FROM dbo.MD_Line l WHERE l.LineID = @Src;
                """, ("@CL", CapLine), ("@By", Actor), ("@Src", LineImg));
            var repo = new ApsRepository(f!);

            foreach (var label in new[] { "zero", "null" })
            {
                if (label == "null") Exec(f!, "UPDATE dbo.MD_Line SET DailyCap = NULL WHERE LineID = @CL AND CreatedBy = @By;", ("@CL", CapLine), ("@By", Actor));
                var b = repo.BuildBundle(new ApsQuery(CapLine, Base, 3));

                Assert.Equal(ApsRepository.LineTypeAssembly, b.Bundle.Line.Type);
                var ls = Assert.Single(b.Settings.LineShifts, l => l.LineCd == CapLine);
                Assert.Null(ls.DailyCap);                                          // 0·NULL 을 능력 0 으로 넘기지 않는다
                Assert.Null(b.Rules.AsmCapFor(CapLine, b.Bundle.Dates[0]));        // → 제한 없음
                Assert.Contains(b.Warnings, w => w.Contains(CapLine) && w.Contains("DailyCap"));
            }
        }
        finally { Cleanup(f!); }
    }

    [SkippableFact]
    public void BuildBundle_warns_about_registered_slots_it_does_not_place()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Skip.If(RoutingA(f!) != "INJ,IMG", "dev routing A is not INJ→IMG");
        Seed(f!);
        try
        {
            int wo = Wo1(f!);
            Slot(f!, wo, "LINE-IMG-02", D3, 480, 540, 25);          // 완제품 라인(BOP)은 LINE-IMG-01 — 다른 라인 슬롯
            Slot(f!, wo, LineImg, D0.AddDays(-1), 480, 540, 11);   // 일요일(토요일 기준일 창 안의 휴무일)
            Exec(f!, "INSERT INTO dbo.MD_Item (ItemNo, ItemName, RoutingType, ActiveFlag, CreatedBy) VALUES (@I, N'ITEST APS inactive', 'A', 0, @By);",
                 ("@I", Off), ("@By", Actor));
            Slot(f!, NewWo(f!, "ITEST-APS-WO7", Off), LineImg, D2, 600, 660, 7);
            var repo = new ApsRepository(f!);

            var b = repo.BuildBundle(new ApsQuery(LineImg, Base, 5));
            var fgRow = b.Bundle.Assembly.Single(r => r.PartNo == Fg);
            Assert.Contains(b.Warnings, w => w.Contains($"등록 계획 {Fg} LINE-IMG-02 {Iso(D3)} 25개") && w.Contains("계획 라인이 아니라"));
            Assert.Equal((0d, false), (fgRow.Days[3].Supply, fgRow.Days[3].Locked));   // 다른 라인 슬롯은 부하·잠금으로 넣지 않는다
            Assert.Equal((60d, true), (fgRow.Days[2].Supply, fgRow.Days[2].Locked));   // 자기 라인 슬롯은 그대로
            Assert.DoesNotContain(b.Warnings, w => w.Contains($"등록 계획 {Fg} {LineImg}") || w.Contains($"등록 계획 {Fg} {LineInj}"));   // 반영한 슬롯은 경고 없음
            Assert.Contains(b.Warnings, w => w.Contains($"등록 계획 {Off} {LineImg} {Iso(D2)} 7개") && w.Contains("활성 품번"));
            Assert.DoesNotContain(b.Bundle.Assembly, r => r.PartNo == Off);

            var sat = repo.BuildBundle(new ApsQuery(LineImg, DateOnly.FromDateTime(D0.AddDays(-2)), 3));
            Assert.Contains(sat.Warnings, w => w.Contains($"등록 계획 {Fg} {LineImg} {Iso(D0.AddDays(-1))} 11개") && w.Contains("휴무일"));
            Assert.All(sat.Bundle.Assembly.Single(r => r.PartNo == Fg).Days, d => Assert.NotEqual(11d, d.Supply));
        }
        finally { Cleanup(f!); }
    }

    [SkippableFact]
    public void BuildBundle_routing_c_item_has_no_phantom_wip_and_uses_its_own_injection_output()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Skip.If(Routing(f!, "C") != "INJ", "dev routing C is not INJ-only");
        Cleanup(f!);
        SeedPattern(f!);
        try
        {
            Exec(f!, """
                INSERT INTO dbo.MD_Item (ItemNo, ItemName, RoutingType, SafetyStock, BoxQty, ActiveFlag, CreatedBy)
                VALUES (@RC, N'ITEST APS routing C', 'C', 0, 10, 1, @By);
                INSERT INTO dbo.MD_Mold (MoldID, MoldName, CavityCount, Status, MoldChangeMin, CreatedBy)
                VALUES (@M5, N'ITEST APS mold 5', 1, 'AVAILABLE', 10, @By);
                INSERT INTO dbo.MD_MoldItem (MoldID, ItemNo, Color, CavitySeq, CavityPos, CavityCount, MoldCategory, ActiveFlag, CreatedBy)
                VALUES (@M5, @RC, 'CBK', 1, 'LH', 1, 'INJECTION', 1, @By);
                INSERT INTO dbo.MD_MoldLine (LineCode, MoldID, UPH, PrepTime, CreatedBy)
                VALUES (@LI, @M5, 60, 5, @By);
                INSERT INTO dbo.MD_Bop (BOPID, ItemNo, RoutingType, StepSeq, StationCode, ActiveFlag, CreatedBy)
                VALUES ('ITEST-APS-BOP-70', @RC, 'C', 10, 'ST-INJ-01', 1, @By);
                INSERT INTO dbo.WH_Inventory (LotNo, UnitType, PartNo, Qty, ReceivedAt)
                VALUES ('ITEST-APS-LOT-RC1', 'PART', @RC, 40, SYSDATETIME());
                """, ("@RC", Rc), ("@M5", Mold5), ("@LI", LineInj), ("@By", Actor));
            Order(f!, "ITEST-APS-SO11", Rc, 10);
            int wo = NewWo(f!, "ITEST-APS-WO6", Rc, "C", "Completed");
            Exec(f!, """
                INSERT INTO dbo.PP_WorkOrderRouting (WoID, StepSeq, ProcessCode, LineID, Status, CompletedQty, CreatedBy, CreatedTS)
                VALUES (@W, 1, 'INJ', @LI, 'Closed', 500, @By, SYSDATETIME());   -- 다음 라인 단계 없음: 단계 차로 재면 누적 500 이 유령 재고가 된다
                INSERT INTO dbo.PR_ProductionResult (WoID, LineID, ProcessCode, GoodQty, ProdDate, EntryAt, CreatedBy, CreatedTS)
                VALUES (@W, @LI, 'INJ', 30, @Prev, @Prev, @By, SYSDATETIME()),
                       (@W, @LI, 'INJ', 12, @D0,   @D0,   @By, SYSDATETIME());
                """, ("@W", wo), ("@LI", LineInj), ("@Prev", Prev), ("@D0", D0), ("@By", Actor));

            var b = new ApsRepository(f!).BuildBundle(new ApsQuery(LineInj, Base, 5));

            var fgRow  = Assert.Single(b.Bundle.Assembly,  r => r.PartNo == Rc);
            var injRow = Assert.Single(b.Bundle.Injection, r => r.PartNo == Rc);
            Assert.Equal((LineInj, LineInj), (fgRow.LineCd, injRow.LineCd));
            Assert.Contains(new BomEdge(Rc, Rc, 1), b.Bundle.Bom);
            Assert.Equal(28d, fgRow.OpeningStock);    // FG 40 − 기준일 INJ(완제품 공정·라인) 12
            Assert.Equal(0d, injRow.OpeningStock);    // WIP 0 − 기준일 INJ 12 + 사용(완제품 라인 INJ 실적 그 자체) 12
            Assert.Equal(new ApsActualInfo(Rc, 30, 0, 0),  b.Actuals.First(a => a.ItemNo == Rc));
            Assert.Equal(new ApsActualInfo(Rc, 30, 0, 30), b.Actuals.Last(a => a.ItemNo == Rc));
        }
        finally { Cleanup(f!); }
    }

    [SkippableFact]
    public void BuildBundle_injection_slots_split_day_night_by_pattern_and_record_shift_exceptions()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Skip.If(RoutingA(f!) != "INJ,IMG", "dev routing A is not INJ→IMG");
        Seed(f!);
        try
        {
            Exec(f!, """
                INSERT INTO dbo.MD_LineTimePattern (PatternID, LineID, PatternName, Status, CreatedBy)
                VALUES (@P2, @LI, N'ITEST APS short day', 'INACTIVE', @By);
                INSERT INTO dbo.MD_LineTimeSegment (SegmentID, PatternID, SeqNo, StartMin, EndMin, SegmentState, ShiftCode, CreatedBy)
                VALUES ('ITEST-APS-SEG2-A', @P2, 1, 480, 900,  'OPERATING', 'A', @By),
                       ('ITEST-APS-SEG2-B', @P2, 2, 960, 1440, 'OPERATING', 'B', @By);
                """, ("@P2", Pat2), ("@LI", LineInj), ("@By", Actor));
            int wo = Wo1(f!);
            Slot(f!, wo, LineInj, D1, 1000, 1100, 45);          // ITEST-APS-PAT B 교대(16:00~24:00) → 야간
            Slot(f!, wo, LineInj, D3, 480, 520, 30, Pat2);      // 그 날 슬롯의 PatternID(PP-LSB 저장 패턴)는 APS 설정보다 뒤 — D3 도 ITEST-APS-PAT(사용자 결정 2026-10-06 (b))

            var b = new ApsRepository(f!).BuildBundle(new ApsQuery(LineImg, Base, 5));

            var fgInj = b.Bundle.Injection.Single(r => r.PartNo == Fg);
            Assert.Equal((60d, 45d), (fgInj.Days[1].PlanDay, fgInj.Days[1].PlanNight));
            Assert.Equal((30d, 0d), (fgInj.Days[3].PlanDay, fgInj.Days[3].PlanNight));
            var ls = Assert.Single(b.Settings.LineShifts, l => l.LineCd == LineInj);
            Assert.Equal((8d, 16d), (ls.Day, ls.Night));        // 5일 전부 ITEST-APS-PAT
            Assert.DoesNotContain(b.Settings.ShiftExceptions, x => x.LineCd == LineInj);
            Assert.DoesNotContain(b.Warnings, w => w.Contains("폴백"));
        }
        finally { Cleanup(f!); }
    }

    // ── APS 가동 시간 패턴 설정 (2026-10-06): 라인 지정 → 기본 패턴 순, 둘 다 없거나 지정 패턴이 없어졌거나 ACTIVE 가 아니면 조회 차단 ──

    [SkippableFact]
    public void BuildBundle_throws_naming_injection_lines_without_an_aps_pattern()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Skip.If(RoutingA(f!) != "INJ,IMG", "dev routing A is not INJ→IMG");
        Seed(f!);
        try
        {
            ApsPatternConfig.Apply(f!, null, (LineInj, null));   // 기본 패턴 비움 + LINE-INJ-01 라인 지정 없음(LINE-INJ-02 는 지정 유지)

            var ex = Assert.Throws<ApsConfigurationException>(() => new ApsRepository(f!).BuildBundle(new ApsQuery(LineImg, Base, 5)));

            Assert.Contains(LineInj, ex.Message);
            Assert.Contains(ex.Lines, l => l.Contains(LineInj));
            Assert.DoesNotContain(ex.Lines, l => l.Contains(LineInj2));
        }
        finally { Cleanup(f!); }
    }

    [SkippableFact]
    public void BuildBundle_falls_back_to_default_pattern_when_line_has_none_and_rejects_inactive_or_unknown_patterns()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Skip.If(RoutingA(f!) != "INJ,IMG", "dev routing A is not INJ→IMG");
        Seed(f!);
        try
        {
            Exec(f!, """
                INSERT INTO dbo.MD_LineTimePattern (PatternID, LineID, PatternName, Status, CreatedBy)
                VALUES (@P2, @LI, N'ITEST APS short day', 'INACTIVE', @By);
                INSERT INTO dbo.MD_LineTimeSegment (SegmentID, PatternID, SeqNo, StartMin, EndMin, SegmentState, ShiftCode, CreatedBy)
                VALUES ('ITEST-APS-SEG2-A', @P2, 1, 480, 900,  'OPERATING', 'A', @By),
                       ('ITEST-APS-SEG2-B', @P2, 2, 960, 1440, 'OPERATING', 'B', @By);
                """, ("@P2", Pat2), ("@LI", LineInj), ("@By", Actor));
            var repo = new ApsRepository(f!);

            // 라인 지정 없음 → 기본 패턴(ITEST-APS-PAT) — 전역 패턴이 아니어도 설정값이면 쓴다
            ApsPatternConfig.Apply(f!, Pat, (LineInj, null));
            var b = repo.BuildBundle(new ApsQuery(LineImg, Base, 5));
            var ls = Assert.Single(b.Settings.LineShifts, l => l.LineCd == LineInj);
            Assert.Equal((8d, 16d), (ls.Day, ls.Night));

            // 라인 지정이 INACTIVE 패턴 → 차단(기본 패턴이 있어도 라인 지정이 우선이라 폴백하지 않는다)
            ApsPatternConfig.Apply(f!, Pat, (LineInj, Pat2));
            var ex = Assert.Throws<ApsConfigurationException>(() => repo.BuildBundle(new ApsQuery(LineImg, Base, 5)));
            Assert.Contains(ex.Lines, l => l.Contains(LineInj) && l.Contains(Pat2) && l.Contains("INACTIVE"));

            // 기본 패턴이 없는 ID → 차단
            ApsPatternConfig.Apply(f!, "ITEST-APS-NOPE", (LineInj, null));
            ex = Assert.Throws<ApsConfigurationException>(() => repo.BuildBundle(new ApsQuery(LineImg, Base, 5)));
            Assert.Contains(ex.Lines, l => l.Contains(LineInj) && l.Contains("ITEST-APS-NOPE"));
        }
        finally { Cleanup(f!); }
    }

    [SkippableFact]
    public void BuildBundle_warning_paths_name_the_item_and_keep_or_drop_rows()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Skip.If(RoutingA(f!) != "INJ,IMG", "dev routing A is not INJ→IMG");
        Cleanup(f!);
        SeedPattern(f!);
        try
        {
            SeedWarningCases(f!);
            SeedBoxPair(f!);
            var repo = new ApsRepository(f!);
            Assert.True(repo.ListLines().Count(l => l.LineId.StartsWith("LINE-IMG-", StringComparison.Ordinal)) > 1);   // 전제: BOP 없으면 IMG 라인을 하나로 못 정한다

            var b = repo.BuildBundle(new ApsQuery(LineImg, Base, 5));

            // 완제품 라인 미배정(BOP 없음 + 후보 IMG 라인 여러 개) → 행 제외
            Assert.Contains(b.Warnings, w => w.Contains($"품번 {NoL}:") && w.Contains("라인이 배정되지 않았습니다"));
            Assert.DoesNotContain(b.Bundle.Assembly, r => r.PartNo == NoL);
            // ①② 모두 없음 → 완제품 행은 남고 사출 행 없음
            Assert.Contains(b.Warnings, w => w.Contains($"품번 {Bx}:") && w.Contains("사출 행이 없습니다"));
            Assert.Contains(b.Bundle.Assembly, r => r.PartNo == Bx);
            Assert.DoesNotContain(b.Bundle.Injection, r => r.PartNo == Bx);
            // 금형은 있으나 사출 라인을 못 정함 → 완제품 행은 남고 사출 행·간선 없음
            Assert.Contains(b.Warnings, w => w.Contains($"사출품 {NmL}:") && w.Contains("사출 라인을 정할 수 없습니다"));
            Assert.Contains(b.Bundle.Assembly, r => r.PartNo == NmL);
            Assert.DoesNotContain(b.Bundle.Injection, r => r.PartNo == NmL);
            Assert.DoesNotContain(b.Bundle.Bom, e => e.ChildPartNo == NmL);
            // UPH 0 → 사출 행은 있고 Uph 0 + 경고
            Assert.Contains(b.Warnings, w => w.Contains($"사출품 {NUp}:") && w.Contains("UPH 가 없어"));
            Assert.Equal(0d, b.Bundle.Injection.Single(r => r.PartNo == NUp).Uph);
        }
        finally { Cleanup(f!); }
    }

    [SkippableFact]
    public void BuildBundle_boxless_finished_rows_get_round_to_pack_rule_so_prefix_rules_do_not_leak()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Skip.If(RoutingA(f!) != "INJ,IMG", "dev routing A is not INJ→IMG");
        Cleanup(f!);
        SeedPattern(f!);
        try
        {
            SeedBoxPair(f!);

            var b = new ApsRepository(f!).BuildBundle(new ApsQuery(LineImg, Base, 5));

            var s = b.Settings;
            Assert.NotEqual(24, s.RoundTo);                     // 전제: RoundTo 가 24 면 새는지 가려낼 수 없다
            Assert.Contains(new PackRule(Bx, 24), s.PackRules);
            Assert.Contains(new PackRule(Bxl, s.RoundTo), s.PackRules);
            Assert.Equal(24, b.Rules.PackFor(Bx));
            Assert.Equal(s.RoundTo, b.Rules.PackFor(Bxl));      // 규칙이 없으면 가장 긴 앞자리 ITEST-APS-BX 의 24 를 물려받는다
        }
        finally { Cleanup(f!); }
    }

    [SkippableFact]
    public void BuildBundle_unknown_line_returns_empty_bundle_with_warning()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");

        var b = new ApsRepository(f!).BuildBundle(new ApsQuery("ITEST-NOLINE", Base, 3));

        Assert.Empty(b.Bundle.Assembly);
        Assert.Empty(b.Bundle.Injection);
        Assert.Equal("ITEST-NOLINE", b.Bundle.Line.LineCd);
        Assert.Equal(3, b.Bundle.Dates.Count);
        Assert.Contains(b.Warnings, w => w.Contains("ITEST-NOLINE"));
    }

    [SkippableFact]
    public void BuildBundle_adds_daily_plan_to_demand_and_can_be_switched_off()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Skip.If(RoutingA(f!) != "INJ,IMG", "dev routing A is not INJ→IMG");
        Seed(f!);
        try
        {
            Exec(f!, """
                INSERT INTO dbo.PP_DemandPlanBatch (Batch, CustomerID, Source, DateFrom, DateTo, ImportedBy, CreatedBy)
                VALUES ('ITEST-APS-DP', 'ITEST-APS-C', 'UPLOAD', @D1, @D1, @By, @By);
                INSERT INTO dbo.PP_DemandPlan (CustomerID, ItemNo, PlanDate, ScheduledQty, Batch, Source, CreatedBy)
                VALUES ('ITEST-APS-C', @FG, @D1, 35, 'ITEST-APS-DP', 'UPLOAD', @By);
                """, ("@FG", Fg), ("@D1", D1), ("@By", Actor));
            var repo = new ApsRepository(f!);

            var with    = repo.BuildBundle(new ApsQuery(LineImg, Base, 5));
            var without = repo.BuildBundle(new ApsQuery(LineImg, Base, 5, IncludeDailyPlan: false));

            var rowWith    = with.Bundle.Assembly.Single(r => r.PartNo == Fg);
            var rowWithout = without.Bundle.Assembly.Single(r => r.PartNo == Fg);
            Assert.Equal(rowWithout.Days[1].Demand + 35d, rowWith.Days[1].Demand);
            Assert.Equal(35d, with.PlanDemand![Fg][1]);
            Assert.True(without.PlanDemand is null || without.PlanDemand.Count == 0);
            for (int i = 0; i < 5; i++) if (i != 1) Assert.Equal(rowWithout.Days[i].Demand, rowWith.Days[i].Demand);
        }
        finally { Cleanup(f!); }
    }

    [SkippableFact]
    public void ListLines_returns_active_inj_img_pnt_lines_with_type()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");

        var lines = new ApsRepository(f!).ListLines();

        Assert.Equal(ApsRepository.LineTypeInjection, lines.Single(l => l.LineId == LineInj).Type);
        Assert.Equal(ApsRepository.LineTypeAssembly,  lines.Single(l => l.LineId == LineImg).Type);
        Assert.DoesNotContain(lines, l => l.LineId == "LINE-RWK-01");
        Assert.Equal(lines.Select(l => l.LineId).OrderBy(x => x, StringComparer.Ordinal), lines.Select(l => l.LineId));
    }

    [SkippableFact]
    public void ReadSettings_reads_migrated_codes_without_setting_warnings()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        using var conn = f!.OpenConnection();

        var p = ApsRepository.ReadSettings(conn, null);

        Assert.Equal(3, p.Settings.CoverTiers.Count);
        Assert.Equal(new[] { 100d, 20d, 0d }, p.Settings.CoverTiers.Select(t => t.MinDailyDemand).ToArray());
        Assert.DoesNotContain(p.Warnings, w => w.Contains(ApsSettingsLoader.GroupSetting));
        Assert.DoesNotContain(p.Warnings, w => w.Contains(ApsSettingsLoader.GroupCoverTier));
        Assert.All(p.Settings.LineStages, st => Assert.Null(st.RootLine));
    }

    static ApsRepository.ApsPlanLineRow L(string kind, string item, DateTime date, decimal demand = 0, decimal supply = 0, decimal req = 0,
                                          decimal day = 0, decimal night = 0, decimal stock = 0, bool locked = false, string status = "ok",
                                          bool sameItem = false)
        => new(0, 0, kind, item, kind == ApsRepository.KindInj ? LineInj : LineImg, DateOnly.FromDateTime(date),
               demand, supply, req, day, night, stock, locked, status, null, sameItem);

    static ApsRepository.ApsRunSave SaveOf(params ApsRepository.ApsPlanLineRow[] lines) => SaveOf(Base, lines);

    static ApsRepository.ApsRunSave SaveOf(DateOnly baseDate, params ApsRepository.ApsPlanLineRow[] lines)
        => new(new ApsQuery(LineImg, baseDate, 2, "C1", true), """{"roundTo":5}""", """{"baseDate":"x"}""", """{"summary":[]}""", lines, 2);

    [SkippableFact]
    public void SaveRun_then_LoadRun_returns_same_json_and_normalized_lines_in_order()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Cleanup(f!);
        try
        {
            var repo = new ApsRepository(f!);
            var save = SaveOf(
                L(ApsRepository.KindInj, "ITEST-APS-X", D0, req: 30, day: 20, night: 10, stock: 5, sameItem: true),
                L(ApsRepository.KindAsm, "ITEST-APS-X", D1, demand: 1.5m, supply: 2, stock: 3.25m, locked: true, status: "short"),
                L(ApsRepository.KindAsm, "ITEST-APS-X", D0, demand: 7));

            int runId = repo.SaveRun(save, Actor);

            var run = repo.LoadRun(runId);
            Assert.NotNull(run);
            Assert.Equal((runId, LineImg, Base, 2, "C1", true, ApsRepository.StatusSaved, 2, Actor),
                         (run!.Row.RunId, run.Row.LineId, run.Row.BaseDate, run.Row.Days, run.Row.CustomerId, run.Row.IncludeOpen, run.Row.Status, run.Row.WarningCount, run.Row.CreatedBy));
            Assert.Null(run.Row.ModifiedBy);
            Assert.Equal((save.SettingsJson, save.BundleJson, save.ResultJson), (run.SettingsJson, run.BundleJson, run.ResultJson));

            var lines = repo.ListPlanLines(runId);
            Assert.Equal(3, lines.Count);
            Assert.Equal(new[] { (ApsRepository.KindAsm, Base), (ApsRepository.KindAsm, Base.AddDays(1)), (ApsRepository.KindInj, Base) },
                         lines.Select(l => (l.Kind, l.PlanDate)).ToArray());
            var asm1 = lines[1];
            Assert.Equal((runId, "ITEST-APS-X", LineImg, 1.5m, 2m, 3.25m, true, "short"),
                         (asm1.RunId, asm1.ItemNo, asm1.LineId, asm1.Demand, asm1.Supply, asm1.Stock, asm1.Locked, asm1.Status));
            Assert.True(asm1.PlanLineId > 0);
            Assert.Null(asm1.WoId);
            Assert.False(asm1.SameItem);
            var inj = lines[2];
            Assert.Equal((LineInj, 30m, 20m, 10m, 5m), (inj.LineId, inj.Requirement, inj.PlanDay, inj.PlanNight, inj.Stock));
            Assert.True(inj.SameItem);                                   // 같은 품번 규칙 표시가 왕복한다 — 「WO 생성」 대상 판정의 근거

            // Review Focus 2: 토요일 기준일도 고른 날짜 그대로 저장한다(근무일로 접지 않는다)
            int satId = repo.SaveRun(SaveOf(Base.AddDays(-2), L(ApsRepository.KindAsm, "ITEST-APS-X", D0)), Actor);
            Assert.Equal(Base.AddDays(-2), repo.LoadRun(satId)!.Row.BaseDate);
        }
        finally { Cleanup(f!); }
    }

    [SkippableFact]
    public void SaveRun_rolls_back_whole_run_on_duplicate_kind_item_date()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Cleanup(f!);
        try
        {
            var repo = new ApsRepository(f!);
            var dup = SaveOf(L(ApsRepository.KindAsm, "ITEST-APS-X", D0), L(ApsRepository.KindAsm, "ITEST-APS-X", D0));

            var ex = Assert.Throws<Microsoft.Data.SqlClient.SqlException>(() => repo.SaveRun(dup, Actor));

            Assert.True(ex.Number is 2601 or 2627, $"unique index violation expected, got {ex.Number}");
            Assert.Equal(0, (int)Scalar(f!, "SELECT COUNT(*) FROM dbo.PP_ApsRun WHERE CreatedBy = @By;", ("@By", Actor))!);
        }
        finally { Cleanup(f!); }
    }

    [SkippableFact]
    public void ListRuns_returns_newest_first_and_respects_top()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Cleanup(f!);
        try
        {
            var repo = new ApsRepository(f!);
            int first  = repo.SaveRun(SaveOf(L(ApsRepository.KindAsm, "ITEST-APS-X", D0)), Actor);
            int second = repo.SaveRun(SaveOf(L(ApsRepository.KindAsm, "ITEST-APS-X", D0)), Actor);

            var mine = repo.ListRuns(50).Where(r => r.CreatedBy == Actor).ToList();
            Assert.Equal(new[] { second, first }, mine.Select(r => r.RunId).ToArray());
            Assert.Single(repo.ListRuns(1));
            Assert.Equal(second, repo.ListRuns(1)[0].RunId);
        }
        finally { Cleanup(f!); }
    }

    [SkippableFact]
    public void SaveRun_persists_IncludeDailyPlan_flag()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Seed(f!);
        try
        {
            var repo = new ApsRepository(f!);
            var b = repo.BuildBundle(new ApsQuery(LineImg, Base, 3, IncludeDailyPlan: false));
            var q = new ApsQuery(LineImg, Base, 3, IncludeDailyPlan: false);
            int id = repo.SaveRun(new ApsRepository.ApsRunSave(q, "{}", "{}", "{}", Array.Empty<ApsRepository.ApsPlanLineRow>(), 0), Actor);
            var run = repo.LoadRun(id);
            Assert.False(run.Row.IncludeDailyPlan);
            Assert.Contains(repo.ListRuns(50), r => r.RunId == id && !r.IncludeDailyPlan);
        }
        finally { Cleanup(f!); }
    }

    [SkippableFact]
    public void SaveLineStage_upserts_and_DeleteLineStage_removes()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Cleanup(f!);
        Exec(f!, """
            INSERT INTO dbo.MD_Line (LineID, LineName, WCID, DailyCap, Status, CreatedBy)
            SELECT @LN, N'ITEST APS line', l.WCID, 100, 'ACTIVE', @By FROM dbo.MD_Line l WHERE l.LineID = @Src;
            """, ("@LN", TestLine), ("@By", Actor), ("@Src", LineInj));
        try
        {
            var repo = new ApsRepository(f!);

            repo.SaveLineStage(TestLine, 2, true, "선행 2일", null, Actor);
            var row = Assert.Single(repo.ListLineStages(), s => s.LineId == TestLine);
            Assert.Equal(("ITEST APS line", ApsRepository.LineTypeInjection, 2, true, "선행 2일", Actor), (row.LineName, row.LineType, row.OffsetDays, row.UseStock, row.Note, row.CreatedBy));
            Assert.Null(row.PatternId);
            Assert.Null(row.ModifiedBy);
            Assert.Null(row.ModifiedTs);

            SeedPattern(f!);
            repo.SaveLineStage(TestLine, 3, false, null, Pat, "ITEST-APS2");
            row = Assert.Single(repo.ListLineStages(), s => s.LineId == TestLine);
            Assert.Equal((3, false, (string?)null, Pat, Actor, "ITEST-APS2"), (row.OffsetDays, row.UseStock, row.Note, row.PatternId, row.CreatedBy, row.ModifiedBy));
            Assert.NotNull(row.ModifiedTs);

            using (var conn = f!.OpenConnection())
            {
                var parsed = ApsRepository.ReadSettings(conn, null);
                Assert.Contains(parsed.Settings.LineStages, st => st.LineCd == TestLine && st.OffsetDays == 3 && !st.UseStock);
                Assert.Equal(Pat, parsed.EffectivePattern(TestLine));
            }

            repo.DeleteLineStage(TestLine);
            Assert.DoesNotContain(repo.ListLineStages(), s => s.LineId == TestLine);
        }
        finally { Cleanup(f!); }
    }

    /// 형제(동시 취출)의 단위 = 금형 × 색상(MD_MoldItem.Color): 같은 금형의 다른 색상은 형제가 아니고 캐비티도 나누지 않는다. "균등 분할" 경고는
    /// 캐비티가 품번 수로 나누어떨어지지 않을 때만. ItemType MATERIAL 은 재고가 있어도 후보가 아니며 경고도 내지 않는다.
    [SkippableFact]
    public void Siblings_are_grouped_by_mold_and_color_uneven_split_warns_and_material_items_are_ignored()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Skip.If(RoutingA(f!) != "INJ,IMG", "dev routing A is not INJ→IMG");
        Seed(f!);
        try
        {
            Exec(f!, """
                INSERT INTO dbo.MD_Item (ItemNo, ItemName, ItemType, RoutingType, SafetyStock, BoxQty, ActiveFlag, CreatedBy)
                VALUES (@ODA, N'ITEST APS odd A',   'SUB',      'A',  0, 10, 1, @By),
                       (@ODB, N'ITEST APS odd B',   'SUB',      'A',  0, 10, 1, @By),
                       (@CLN, N'ITEST APS NNB',     'SUB',      'A',  0, 10, 1, @By),
                       (@CLY, N'ITEST APS YGU',     'SUB',      'A',  0, 10, 1, @By),
                       (@MAT, N'ITEST APS screw',   'MATERIAL', NULL, 0, NULL, 1, @By);
                INSERT INTO dbo.MD_Mold (MoldID, MoldName, CavityCount, Status, MoldChangeMin, CreatedBy)
                VALUES (@M8, N'ITEST APS mold 8', 3, 'AVAILABLE', 10, @By),
                       (@M9, N'ITEST APS mold 9', 2, 'AVAILABLE', 10, @By);
                INSERT INTO dbo.MD_MoldItem (MoldID, ItemNo, Color, CavitySeq, CavityPos, CavityCount, MoldCategory, ActiveFlag, CreatedBy)
                VALUES (@M8, @ODA, 'CBK', 1, 'LH', 3, 'INJECTION', 1, @By),
                       (@M8, @ODB, 'CBK', 2, 'RH', 3, 'INJECTION', 1, @By),
                       (@M9, @CLN, 'NNB', 1, 'LH', 2, 'INJECTION', 1, @By),
                       (@M9, @CLY, 'YGU', 1, 'LH', 2, 'INJECTION', 1, @By);
                INSERT INTO dbo.MD_MoldLine (LineCode, MoldID, UPH, PrepTime, CreatedBy)
                VALUES (@LI, @M8, 120, 5, @By), (@LI, @M9, 120, 5, @By);
                INSERT INTO dbo.MD_Bop (BOPID, ItemNo, RoutingType, StepSeq, StationCode, ActiveFlag, CreatedBy)
                VALUES ('ITEST-APS-BOP-81', @ODA, 'A', 10, 'ST-INJ-01', 1, @By), ('ITEST-APS-BOP-82', @ODA, 'A', 20, 'ST-IMG-01', 1, @By),
                       ('ITEST-APS-BOP-83', @ODB, 'A', 10, 'ST-INJ-01', 1, @By), ('ITEST-APS-BOP-84', @ODB, 'A', 20, 'ST-IMG-01', 1, @By),
                       ('ITEST-APS-BOP-85', @CLN, 'A', 10, 'ST-INJ-01', 1, @By), ('ITEST-APS-BOP-86', @CLN, 'A', 20, 'ST-IMG-01', 1, @By),
                       ('ITEST-APS-BOP-87', @CLY, 'A', 10, 'ST-INJ-01', 1, @By), ('ITEST-APS-BOP-88', @CLY, 'A', 20, 'ST-IMG-01', 1, @By);
                INSERT INTO dbo.PP_CustomerOrder (SoNumber, SoLineNo, ItemNo, OrderQty, ShippedQty, RequestedDeliveryDate, Status, CreatedBy)
                VALUES ('ITEST-APS-SO81', 1, @ODA, 10, 0, @D1, 'Confirmed', @By), ('ITEST-APS-SO82', 1, @ODB, 10, 0, @D1, 'Confirmed', @By),
                       ('ITEST-APS-SO83', 1, @CLN, 10, 0, @D1, 'Confirmed', @By), ('ITEST-APS-SO84', 1, @CLY, 10, 0, @D1, 'Confirmed', @By);
                INSERT INTO dbo.WH_Inventory (LotNo, UnitType, PartNo, Qty, ReceivedAt)
                VALUES ('ITEST-APS-LOT-MAT1', 'PART', @MAT, 50, SYSDATETIME());
                """, ("@ODA", OdA), ("@ODB", OdB), ("@CLN", ClN), ("@CLY", ClY), ("@MAT", Mat), ("@M8", Mold8), ("@M9", Mold9),
                     ("@LI", LineInj), ("@D1", D1), ("@By", Actor));

            var b = new ApsRepository(f!).BuildBundle(new ApsQuery(LineImg, Base, 5));
            InjectionRow Inj(string p) => Assert.Single(b.Bundle.Injection, r => r.PartNo == p);

            // 색상 2종 금형: 서로 형제가 아니고 캐비티도 안 나눈다 — 각자 캐비티 2, UPH 120 × 2 ÷ 2
            var n = Inj(ClN); var y = Inj(ClY);
            Assert.Equal((Mold9, "NNB", 2, 120d), (n.MoldCode, n.MoldColor, n.Cavity, n.Uph));
            Assert.Equal((Mold9, "YGU", 2, 120d), (y.MoldCode, y.MoldColor, y.Cavity, y.Uph));
            Assert.Empty(n.SiblingPartNos); Assert.Empty(y.SiblingPartNos);
            Assert.NotEqual(n.MoldGroupKey(), y.MoldGroupKey());
            Assert.DoesNotContain(b.Warnings, w => w.Contains("균등 분할") && (w.Contains(ClN) || w.Contains(ClY)));

            // 비대칭(캐비티 3 ÷ 품번 2): 내림 1 + 경고
            var a = Inj(OdA); var o = Inj(OdB);
            Assert.Equal((1, 40d), (a.Cavity, a.Uph));                   // 120 × 1 ÷ 3
            Assert.Equal(new[] { OdB }, a.SiblingPartNos); Assert.Equal(new[] { OdA }, o.SiblingPartNos);
            Assert.Equal(a.MoldGroupKey(), o.MoldGroupKey());
            Assert.Contains(b.Warnings, w => w.Contains(OdA) && w.Contains("균등 분할"));
            Assert.Contains(b.Warnings, w => w.Contains(OdB) && w.Contains("균등 분할"));

            // MATERIAL: 후보 아님 · 경고 없음
            Assert.DoesNotContain(b.Bundle.Assembly, r => r.PartNo == Mat);
            Assert.DoesNotContain(b.Warnings, w => w.Contains(Mat));
        }
        finally { Cleanup(f!); }
    }

    /// 완제품 품번으로 잡힌 사출 라인 슬롯(코어 품번 WO 데모 모델)은 그 완제품의 BOM 사출 자식 등록 계획으로 읽는다 — 수량 × QtyPer(스크랩 포함),
    /// 자식마다 각각. 자식의 사출 라인과 다른 라인의 슬롯은 종전대로 "계획 라인이 아니라" 경고. (사용자 결정 10-02, 4-(a))
    [SkippableFact]
    public void Parent_item_slot_on_injection_line_is_registered_plan_of_its_bom_children_times_qtyper()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Skip.If(RoutingA(f!) != "INJ,IMG", "dev routing A is not INJ→IMG");
        Seed(f!);
        try
        {
            SeedBomRule(f!);
            int wBp = (int)Scalar(f!, "SELECT WoID FROM dbo.PP_WorkOrder WHERE WoNumber = 'ITEST-APS-WO2' AND CreatedBy = @By;", ("@By", Actor))!;
            Slot(f!, wBp, LineInj2, D1, 480, 540, 10);    // 자식 CH·CH2 의 사출 라인 → 자식 등록 계획
            Slot(f!, wBp, LineInj,  D2, 480, 540, 7);     // 자식 라인이 아닌 사출 라인 → 종전 경고

            var b  = new ApsRepository(f!).BuildBundle(new ApsQuery(LineImg, Base, 5));
            var ch  = Assert.Single(b.Bundle.Injection, r => r.PartNo == Ch);
            var ch2 = Assert.Single(b.Bundle.Injection, r => r.PartNo == Ch2);

            Assert.Equal(15d, ch.Days[1].PlanDay + ch.Days[1].PlanNight, 6);     // 10 × 1.5
            Assert.True(ch.Days[1].Locked);
            Assert.Equal(22d, ch2.Days[1].PlanDay + ch2.Days[1].PlanNight, 6);   // 10 × 2 × 1.1
            Assert.True(ch2.Days[1].Locked);
            Assert.False(ch.Days[2].Locked);                                      // LINE-INJ-01 슬롯은 자식 라인이 아니다
            Assert.False(b.Bundle.Assembly.Single(r => r.PartNo == Bp).Days[1].Locked);   // 완제품 행(IMG 라인)에는 들어가지 않는다
            // 부모 경유 슬롯은 자식 행의 등록 WO 로, 수량은 QtyPer 곱, 슬롯 품번은 부모
            Assert.Contains(b.RegisteredWos, w => w.Kind == ApsRepository.KindInj && w.ItemNo == Ch && w.Date == Iso(D1) && w.LineId == LineInj2
                                               && w.WoId == wBp && w.WoNumber == "ITEST-APS-WO2" && Math.Abs(w.Qty - 15) < 1e-6 && w.SlotItemNo == Bp);
            Assert.Contains(b.RegisteredWos, w => w.Kind == ApsRepository.KindInj && w.ItemNo == Ch2 && w.Date == Iso(D1) && Math.Abs(w.Qty - 22) < 1e-6 && w.SlotItemNo == Bp);
            Assert.DoesNotContain(b.RegisteredWos, w => w.Kind == ApsRepository.KindAsm && w.ItemNo == Bp);   // 완제품 행에는 IMG 라인 슬롯만 — 사출 라인 슬롯은 자식 몫

            Assert.DoesNotContain(b.Warnings, w => w.Contains("등록 계획 " + Bp + " " + LineInj2));
            Assert.Contains(b.Warnings, w => w.Contains("등록 계획 " + Bp + " " + LineInj + " ") && w.Contains("계획 라인이 아니라"));
        }
        finally { Cleanup(f!); }
    }

    /// BOM 규칙은 다단계: 사출품은 MD_Item.InjFlag = 1 로 구분한다(사용자 결정 10-05). InjFlag 0 인 자식(서브조립 — 마스터 리스트의 S-품번·PNL ASSY·MODULE)은
    /// 그 BOM 을 한 단계 더 내려가며 QtyPer(스크랩 포함)를 경로를 따라 곱하고, MATERIAL 은 내려가지 않는다. InjFlag 1 인데 활성 금형이 없으면 경고 + 사출 행 없음,
    /// 금형은 있는데 InjFlag 0 이면 사출품으로 보지 않고 경고.
    [SkippableFact]
    public void Bom_rule_walks_through_moldless_sub_assemblies_and_multiplies_qtyper()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Skip.If(RoutingA(f!) != "INJ,IMG", "dev routing A is not INJ→IMG");
        Seed(f!);
        try
        {
            SeedBomRule(f!);   // Ch(금형 M3, LINE-INJ-02 UPH 100) · Bp → Ch 1.5 / Ch2 2.2
            Exec(f!, """
                INSERT INTO dbo.MD_Item (ItemNo, ItemName, ItemType, InjFlag, RoutingType, SafetyStock, BoxQty, ActiveFlag, CreatedBy)
                VALUES (@BP2,   N'ITEST APS multi parent', 'ASSY',     0, 'A',  0, 12,   1, @By),
                       (@MID,   N'ITEST APS mid sub',      'SUB',      0, NULL, 0, NULL, 1, @By),
                       (@MID2,  N'ITEST APS mid sub 2',    'SUB',      1, NULL, 0, NULL, 1, @By),   -- 사출품 표시인데 금형 없음
                       (@NOINJ, N'ITEST APS not inj',      'SUB',      0, NULL, 0, 10,   1, @By),   -- 금형은 있는데 사출품 표시 아님
                       (@MAT,   N'ITEST APS screw',        'MATERIAL', 0, NULL, 0, NULL, 1, @By);   -- 금형 없는 MATERIAL 리프
                INSERT INTO dbo.MD_Mold (MoldID, MoldName, CavityCount, Status, MoldChangeMin, CreatedBy)
                VALUES (@M8, N'ITEST APS mold 8', 1, 'AVAILABLE', 10, @By);
                INSERT INTO dbo.MD_MoldItem (MoldID, ItemNo, Color, CavitySeq, CavityPos, CavityCount, MoldCategory, ActiveFlag, CreatedBy)
                VALUES (@M8, @NOINJ, 'CBK', 1, 'LH', 1, 'INJECTION', 1, @By);
                INSERT INTO dbo.MD_MoldLine (LineCode, MoldID, UPH, PrepTime, CreatedBy) VALUES (@LI2, @M8, 60, 5, @By);
                INSERT INTO dbo.MD_Bop (BOPID, ItemNo, RoutingType, StepSeq, StationCode, ActiveFlag, CreatedBy)
                VALUES ('ITEST-APS-BOP-91', @BP2, 'A', 20, 'ST-IMG-01', 1, @By);
                INSERT INTO dbo.MD_BomVersion (VersionID, RootItemNo, VersionNo, EffFrom, EffTo, Status, CreatedBy)
                VALUES ('ITEST-APS-BV2', @BP2,  '1', @Eff, NULL, 'APPROVED', @By),
                       ('ITEST-APS-BV3', @MID,  '1', @Eff, NULL, 'APPROVED', @By),
                       ('ITEST-APS-BV4', @MID2, '1', @Eff, NULL, 'APPROVED', @By);
                INSERT INTO dbo.MD_Bom (BOMID, ParentItemNo, CompItemNo, QtyPer, ScrapPct, VersionID, ActiveFlag, CreatedBy)
                VALUES ('ITEST-APS-BOM5', @BP2,  @MID,  2,   0,  'ITEST-APS-BV2', 1, @By),
                       ('ITEST-APS-BOM6', @BP2,  @MID2, 1,   0,  'ITEST-APS-BV2', 1, @By),
                       ('ITEST-APS-BOM7', @MID,  @CH,   1.5, 0,  'ITEST-APS-BV3', 1, @By),
                       ('ITEST-APS-BOM8', @MID,  @MAT,  3,   0,  'ITEST-APS-BV3', 1, @By),
                       ('ITEST-APS-BOM9', @MID2, @MAT,  1,   0,  'ITEST-APS-BV4', 1, @By),
                       ('ITEST-APS-BOM10', @BP2, @NOINJ, 1,  0,  'ITEST-APS-BV2', 1, @By);
                INSERT INTO dbo.PP_CustomerOrder (SoNumber, SoLineNo, ItemNo, OrderQty, ShippedQty, RequestedDeliveryDate, Status, CreatedBy)
                VALUES ('ITEST-APS-SO91', 1, @BP2, 10, 0, @D1, 'Confirmed', @By);
                """, ("@BP2", Bp2), ("@MID", Mid), ("@MID2", Mid2), ("@NOINJ", NoInj), ("@M8", Mold8), ("@LI2", LineInj2), ("@CH", Ch), ("@MAT", Mat),
                     ("@Eff", D0.AddDays(-1)), ("@D1", D1), ("@By", Actor));
            var b  = new ApsRepository(f!).BuildBundle(new ApsQuery(LineImg, Base, 5));
            var bd = b.Bundle;

            Assert.Single(bd.Assembly, r => r.PartNo == Bp2);
            Assert.Contains(new BomEdge(Bp2, Ch, 3.0), bd.Bom);                           // 2 × 1.5 — 중간 SUB 를 건너 완제품→사출품 간선
            Assert.Single(bd.Bom, e => e.ParentPartNo == Bp2);                             // Mid2 는 금형 없음, NoInj 는 InjFlag 0, MAT 는 자재
            Assert.DoesNotContain(bd.Injection, r => r.PartNo == Mid || r.PartNo == Mid2 || r.PartNo == NoInj || r.PartNo == Mat);
            Assert.DoesNotContain(b.Warnings, w => w.Contains(Bp2) && w.Contains("사출 행이 없습니다"));
            Assert.Contains(b.Warnings, w => w.Contains("사출품 " + Mid2 + "(") && w.Contains("활성 금형이 없"));      // InjFlag 1, 금형 없음
            Assert.Contains(b.Warnings, w => w.Contains("품번 " + NoInj + ":") && w.Contains("InjFlag"));         // 금형 있음, InjFlag 0
            Assert.DoesNotContain(b.Warnings, w => w.Contains(Mid + "(") || w.Contains(Mid + ":"));               // 서브조립(InjFlag 0, 금형 없음)은 조용히 내려간다

            // 사용량 = 완제품(IMG) 실적 × 경로 QtyPer — Bp2 실적은 없으니 Ch 사용량은 Bp(SeedBomRule) 몫만
            Assert.Equal(new ApsActualInfo(Ch, 20, 0, 16.5m), b.Actuals.Single(a => a.ItemNo == Ch));
        }
        finally { Cleanup(f!); }
    }

    /// 슬롯을 못 받은 APS WO(Shortfall — PP-LSB 미배치)는 셀의 Work Order 줄에 "미배치" 로 보인다(사용자 결정 2026-10-06 (b)):
    /// 사출 행은 PP_ApsRunWo 가 가리키는 계획 행(품번·사출일), 완제품 행은 WO 의 ProdDeadline(= 사출일 + 선행일 = 공급일).
    [SkippableFact]
    public void Unplaced_aps_work_orders_show_on_injection_and_finished_cells()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Skip.If(RoutingA(f!) != "INJ,IMG", "dev routing A is not INJ→IMG");
        Seed(f!);
        try
        {
            int run = (int)Scalar(f!, """
                INSERT INTO dbo.PP_ApsRun (LineID, BaseDate, Days, IncludeOpen, Status, SettingsJson, BundleJson, ResultJson, WarningCount, CreatedBy)
                OUTPUT INSERTED.RunID VALUES (@L, @D0, 5, 0, 'Released', '{}', '{}', '{}', 0, @By);
                """, ("@L", LineImg), ("@D0", D0), ("@By", Actor))!;
            int pl = (int)Scalar(f!, """
                INSERT INTO dbo.PP_ApsPlanLine (RunID, Kind, ItemNo, LineID, PlanDate, Demand, Supply, Requirement, PlanDay, PlanNight, Stock, Locked, Status, SameItem)
                OUTPUT INSERTED.PlanLineID VALUES (@R, 'INJ', @FG, @LI, @D1, 0, 0, 40, 40, 0, 0, 0, 'ok', 1);
                """, ("@R", run), ("@FG", Fg), ("@LI", LineInj), ("@D1", D1))!;
            int wo = (int)Scalar(f!, """
                INSERT INTO dbo.PP_WorkOrder (WoNumber, ItemNo, OrderQty, OpenQty, RoutingType, Status, ProdDeadline, CreatedBy, CreatedTS)
                OUTPUT INSERTED.WoID VALUES ('ITEST-APS-WO9', @FG, 40, 40, 'A', 'Released', @D2, @By, SYSDATETIME());
                """, ("@FG", Fg), ("@D2", D2), ("@By", Actor))!;
            Exec(f!, "INSERT INTO dbo.PP_ApsRunWo (RunID, PlanLineID, WoID, SoID, Qty, CreatedBy, CreatedTS) VALUES (@R, @P, @W, NULL, 40, @By, SYSDATETIME());",
                 ("@R", run), ("@P", pl), ("@W", wo), ("@By", Actor));

            var b = new ApsRepository(f!).BuildBundle(new ApsQuery(LineImg, Base, 5));

            Assert.Contains(b.RegisteredWos, w => w.Unplaced && w.Kind == ApsRepository.KindInj && w.ItemNo == Fg && w.Date == Iso(D1)
                                               && w.WoId == wo && w.WoNumber == "ITEST-APS-WO9" && w.Qty == 40 && w.LineId == LineInj);
            Assert.Contains(b.RegisteredWos, w => w.Unplaced && w.Kind == ApsRepository.KindAsm && w.ItemNo == Fg && w.Date == Iso(D2)
                                               && w.WoId == wo && w.Qty == 40);
            Assert.DoesNotContain(b.RegisteredWos, w => !w.Unplaced && w.WoId == wo);   // 슬롯이 없으니 배치분은 없다
            Assert.Equal(60d, b.Bundle.Injection.Single(r => r.PartNo == Fg).Days[1].PlanDay);   // 등록 계획(잠긴 칸)에는 안 들어간다 — 기존 WO1 슬롯 60 만
        }
        finally { Cleanup(f!); }
    }
}
