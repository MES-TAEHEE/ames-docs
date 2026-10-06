namespace AMES.Data.Aps;

/// <summary>
/// 스펙 §4.4 — 현재 재고(FG_Inventory·WIP·WH_Inventory)를 기준일 아침 재고로 역산한다. 실사·롤포워드 없음.
/// 엔진 식 stock(첫날) = OpeningStock + SisProduced − Shipped + Defect 에서 SisProduced = Shipped = Defect = 0 으로 두고
/// OpeningStock 에 이 값을 넣는다. D-1 실적은 Display 로 따로 돌려주고 계산에 넣지 않는다. 정본 테스트 ActualsRulesTests.
/// </summary>
public static class ActualsRules
{
    /// <summary>Today* = 기준일 실적, Prev* = 직전 근무일 실적. 완제품은 Used = 0, 사출품은 Shipped = 0.</summary>
    public sealed record Snapshot(string ItemNo, decimal CurrentStock,
                                  decimal TodayProduced, decimal TodayShipped, decimal TodayUsed,
                                  decimal PrevProduced,  decimal PrevShipped,  decimal PrevUsed);

    /// <summary>기준일 아침 재고 = CurrentStock − TodayProduced + TodayShipped + TodayUsed.
    /// 생산은 SUM(GoodQty)(역분개 −1 행 포함 순생산)이라 불량을 따로 빼지 않는다 — 이중 차감 금지. 음수도 그대로 둔다(화면이 경고).</summary>
    public static double Opening(Snapshot s) => (double)(s.CurrentStock - s.TodayProduced + s.TodayShipped + s.TodayUsed);

    /// <summary>D-1 표시 전용.</summary>
    public static ApsActualInfo Display(Snapshot s) => new(s.ItemNo, s.PrevProduced, s.PrevShipped, s.PrevUsed);
}
