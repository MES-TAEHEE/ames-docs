using AMES.Data.Aps.Contracts;
using AMES.Data.Aps.Domain;

namespace AMES.Data.Aps;

/// <summary>PP-APS 조회 조건. 기준일 기본은 화면이 DbClock.Today 로 넣는다(스펙 §4.1).</summary>
public sealed record ApsQuery(string LineId, DateOnly BaseDate, int Days, string? CustomerId = null, bool IncludeOpen = false);

/// <summary>D-1(직전 근무일) 실적 표시 전용 — 계산에 넣지 않는다(스펙 §4.4). 같은 품번이 완제품·사출 행에 모두 있으면
/// 완제품 행 항목(Shipped 채움, Used 0)이 먼저, 사출 행 항목(Used 채움, Shipped 0)이 뒤에 온다.</summary>
public sealed record ApsActualInfo(string ItemNo, decimal Produced, decimal Shipped, decimal Used);

/// <summary>
/// 셀의 등록 계획을 만든 WO 1건(슬롯 단위) — 화면의 "WO" 줄·툴팁용(10-05 사용자 요청), 저장 JSON 에는 없다(복원은 현재 WO 를 다시 읽는다).
/// Kind = ApsRepository.KindAsm/KindInj, Date = ISO. Qty 는 행에 반영된 수량 — 부모 품번 슬롯을 자식 등록 계획으로 읽은 경우 슬롯 수량 × QtyPer 이고 SlotItemNo 가 부모다.
/// </summary>
/// <param name="Unplaced">true = 슬롯을 못 받은 APS WO(Shortfall, PP-LSB 미배치) — 사출 행은 PP_ApsRunWo 의 계획 행(품번·사출일), 완제품 행은 WO 의 ProdDeadline(공급일). 등록 계획(잠긴 칸)에는 안 들어간다.</param>
public sealed record ApsRegisteredWo(string Kind, string ItemNo, string LineId, string Date, int WoId, string WoNumber, double Qty, string SlotItemNo, bool Unplaced = false);

/// <summary>ApsRepository.BuildBundle 의 결과 묶음. Rules·Stages 는 Settings 와 활성 INJ/IMG/PNT 라인 전체(LineCd 키, OrdinalIgnoreCase)로 만든다.</summary>
public sealed record ApsBuild(PlanBundle Bundle, Settings Settings, ApsOptions Options, IReadOnlyList<ApsActualInfo> Actuals,
                              List<string> Warnings, IApsCalendar Calendar, ShiftRules Rules, StageRules Stages,
                              IReadOnlyList<ApsRegisteredWo>? RegisteredWoRows = null)
{
    /// <summary>셀마다의 등록 WO(없으면 빈 목록).</summary>
    public IReadOnlyList<ApsRegisteredWo> RegisteredWos => RegisteredWoRows ?? Array.Empty<ApsRegisteredWo>();
}
