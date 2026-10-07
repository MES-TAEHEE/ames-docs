using AMES.Data.Aps.Contracts;

namespace AMES.Data.Aps;

/// <summary>
/// 교대 목록 ↔ 주/야 호환(스펙 §1·§3). PlanDay = 첫 교대, PlanNight = 나머지 합. 구 실행 JSON(PlanShifts 없음)은 Day→첫 교대·Night→둘째 교대로 복원하고
/// 교대가 하나면 합산한다. 저장된 교대 코드가 현재 패턴에 없으면(패턴 변경 뒤 재현) 그 수량은 첫 교대에 합산해 합을 보존한다. 순수 함수 — 정본 테스트 ApsShiftCompatTests.
/// </summary>
public static class ApsShiftCompat
{
    public static (double Day, double Night) Derive(IReadOnlyList<ShiftQty> shifts)
        => shifts.Count == 0 ? (0, 0) : (shifts[0].Qty, shifts.Skip(1).Sum(s => s.Qty));

    public static List<ShiftQty> Restore(double day, double night, IReadOnlyList<ShiftHours> shifts)
    {
        var list = shifts.Select(s => new ShiftQty(s.Code, 0)).ToList();
        if (list.Count == 0) return list;
        list[0] = list[0] with { Qty = day + (list.Count == 1 ? night : 0) };
        if (list.Count > 1) list[1] = list[1] with { Qty = night };
        return list;
    }

    public static List<ShiftQty> Align(IReadOnlyList<ShiftQty>? stored, double day, double night, IReadOnlyList<ShiftHours> shifts)
    {
        if (stored is not { Count: > 0 }) return Restore(day, night, shifts);
        var list = shifts.Select(s => new ShiftQty(s.Code, 0)).ToList();
        if (list.Count == 0) return list;
        double orphan = 0;
        foreach (var s in stored)
        {
            var i = list.FindIndex(x => string.Equals(x.Code, s.Code, StringComparison.OrdinalIgnoreCase));
            if (i >= 0) list[i] = list[i] with { Qty = list[i].Qty + s.Qty }; else orphan += s.Qty;
        }
        if (orphan != 0) list[0] = list[0] with { Qty = list[0].Qty + orphan };
        return list;
    }
}
