using AMES.Data.Services.DemandPlan;

namespace AMES.Web.Services;

/// <summary>
/// 업로드 샘플 양식 4종(예측 ZMM30004 · 일별 구매계획 MM30011 · 구매오더 MM31006 · 일별 발주 MM31007).
/// 네 샘플은 같은 샘플 데이터(SampleData)에서 만들어 서로 일치한다 — 발주량은 구매오더의 납기별 합 = 일별 발주 = 구매계획 P/O.
/// 열 구조는 각 파서가 읽는 그대로다(파서는 MM31006·MM31007 은 열 머리글 이름, ZMM30004·MM30011 은 고정 위치).
/// </summary>
public static class SrmSamples
{
    /// <summary>샘플 품목(개발 DB 실제 품목 — FRT CORE LH/RH). 업체 코드는 샘플 값이다(실제 협력사 코드는 고객 설정의 VENDCD).</summary>
    public static readonly (string PartNo, string PartName, string Unit)[] Items =
    {
        ("D0111-PI010", "FRT CORE,LH", "EA"),
        ("D0121-PI010", "FRT CORE,RH", "EA"),
    };

    public const string SampleVendor = "SAMPLE";
    public const string SampleVendorName = "Sample Vendor";
    public const string SampleCarType = "NE1A";
    public const string SampleLoc = "Prod Loc_P1";

    /// <summary>품목 i · 날짜의 발주량(=예정량=P/O). 주말 0. 날짜 번호 기준이라 기간을 옮겨도 같은 날은 같은 값.</summary>
    public static decimal Qty(int item, DateOnly day)
    {
        if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) return 0;
        return 240 + 10 * ((day.DayNumber + item) % 3);
    }

    // ── 일별 발주 MM31007 ───────────────────────────────────────
    static readonly string[] DailyPoFixed = { "NO", "업체", "업체명", "구매오더유형", "차종", "PART NO", "품명", "저장위치", "고객사", "발주단위", "적입량" };

    /// <summary>MM31007: 날짜 머리글(MM-dd)이 발주량·납품량 두 열을 덮는다(병합 — 두 열 모두 같은 값을 적는다).</summary>
    public static byte[] DailyPo(DateOnly from, int days, string vendorCode = SampleVendor)
    {
        var header = new List<string>(DailyPoFixed);
        var label = new List<string>(Enumerable.Repeat("", DailyPoFixed.Length));
        for (int k = 0; k < days; k++)
        {
            var md = from.AddDays(k).ToString("MM-dd");
            header.Add(md); header.Add(md);
            label.Add("발주량"); label.Add("납품량");
        }

        var rows = new List<IReadOnlyList<object?>> { label.Cast<object?>().ToList() };
        for (int i = 0; i < Items.Length; i++)
        {
            var (partNo, partName, unit) = Items[i];
            var row = new List<object?> { i + 1, vendorCode, SampleVendorName, "생산용 발주", SampleCarType, partNo, partName, SampleLoc, "", unit, null };
            for (int k = 0; k < days; k++)
            {
                var q = Qty(i, from.AddDays(k));
                row.Add(q == 0 ? null : q);   // 발주량
                row.Add(null);                 // 납품량(샘플은 아직 납품 전)
            }
            rows.Add(row);
        }
        return XlsxWriter.Build("Sheet1", header, rows, 14);
    }

    // ── 구매오더 MM31006 ────────────────────────────────────────
    static readonly string[] PoHeader =
    {
        "NO", "업체", "업체명", "발주일자", "발주번호", "납기일자", "구매오더유형", "차종", "PART NO", "품명", "저장위치",
        "발주단위", "적입량", "발주량", "납품수량", "불량수량", "입하(합격)수량", "입하잔량", "입고수량", "납품완료",
        "구매조직", "구매그룹", "고객사 수주정보", "원산지확인서", "유/무상구분", "자재그룹", "구매오더 기타 정보", "최종등록일시",
    };

    /// <summary>MM31006: 품목 × 납기일마다 발주 한 줄(발주번호 = 샘플 접두어 9000000001 + 순번-라인). 발주량 합 = MM31007 발주량.</summary>
    public static byte[] PurchaseOrders(DateOnly orderDate, DateOnly from, int days, string vendorCode = SampleVendor)
    {
        var rows = new List<IReadOnlyList<object?>>();
        int seq = 0;
        for (int i = 0; i < Items.Length; i++)
        {
            var (partNo, partName, unit) = Items[i];
            for (int k = 0; k < days; k++)
            {
                var due = from.AddDays(k);
                var q = Qty(i, due);
                if (q == 0) continue;
                seq++;
                rows.Add(new List<object?>
                {
                    seq, vendorCode, SampleVendorName, orderDate.ToString("yyyy-MM-dd"), $"9000000001-{seq * 10}",
                    due.ToString("yyyy-MM-dd"), "생산용 발주", SampleCarType, partNo, partName, SampleLoc, unit,
                    null, q, 0, 0, 0, q, 0, 0, "", "", "", "", "", "", "", "",
                });
            }
        }
        return XlsxWriter.Build("Sheet1", PoHeader, rows, 16);
    }

    // ── 일별 구매계획 MM30011 ───────────────────────────────────
    /// <summary>MM30011 — 현재 DailyPlanGrid 가 읽는 격자(고정 12열 + Sub total 2 + 날짜마다 2열, 2행 라벨). 예정량 = P/O.</summary>
    public static byte[] DailyPlan(DateOnly from, int days, string vendorCode = SampleVendor)
    {
        int width = DailyPlanGrid.FirstDateCol + days * 2;
        var header = new List<string> { "NO", "Vendor Code", "Vendor Name", "Car type", "PART NO", "PART NAME", "Unit", "Packing Qty.", "D-1", "Basic Inv", "Current Inv", "Open Order Qty", "Sub total", "" };
        for (int k = 0; k < days; k++) { header.Add(from.AddDays(k).ToString("yyyy-MM-dd")); header.Add(""); }

        var label = new List<object?>(width);
        label.AddRange(Enumerable.Repeat<object?>("", DailyPlanGrid.FixedCols));
        label.Add(""); label.Add("");
        for (int k = 0; k < days; k++) { label.Add("Scheduled Qty."); label.Add("P/O Qty."); }

        var rows = new List<IReadOnlyList<object?>> { label };
        for (int i = 0; i < Items.Length; i++)
        {
            var (partNo, partName, unit) = Items[i];
            var sched = new decimal[days];
            for (int k = 0; k < days; k++) sched[k] = Qty(i, from.AddDays(k));
            var row = new List<object?> { i + 1, vendorCode, SampleVendorName, SampleCarType, partNo, partName, unit, null, null, null, null, null, sched.Sum(), sched.Sum() };
            for (int k = 0; k < days; k++) { object? v = sched[k] == 0 ? null : sched[k]; row.Add(v); row.Add(v); }
            rows.Add(row);
        }
        return XlsxWriter.Build("Sheet1", header, rows, 16);
    }
}
