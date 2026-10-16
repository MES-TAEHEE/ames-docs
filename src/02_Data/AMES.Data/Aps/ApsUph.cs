namespace AMES.Data.Aps;

/// <summary>
/// 유효 UPH(스펙 §4, 2026-10-07 사용자 결정 "UPH 통일") — 계획(ApsRepository)과 WO 생성(PpRepository.CreateApsWorkOrders)이 같은 값을 쓴다.
/// 품번 캐비티 = 금형 캐비티 ÷ 같은 금형·같은 색상의 활성 품번 수(내림, 최소 1). 유효 UPH = MD_MoldLine.UPH × 품번 캐비티 ÷ 금형 캐비티(캐비티 없으면 원값).
/// 순수 함수 — 정본 테스트 ApsUphTests.
/// </summary>
public static class ApsUph
{
    public static int PartCavity(int? moldCavity, int activeItems)
    {
        int items = Math.Max(1, activeItems);
        return Math.Max(1, (moldCavity ?? items) / items);
    }

    public static decimal Effective(decimal moldLineUph, int? moldCavity, int activeItems)
    {
        if (moldLineUph <= 0) return 0;
        return moldCavity is int mc && mc > 0 ? moldLineUph * PartCavity(moldCavity, activeItems) / mc : moldLineUph;
    }
}
