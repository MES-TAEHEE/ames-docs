using AMES.Data.Repositories;
using AMES.Data.Services;

namespace AMES.Web.Services;

/// <summary>PP-003 에서 WO 를 만들 수 없는 이유 하나. Code = 리소스 키 접미사(PP.PlanConfirm.Why.{Code}), Detail = 품번·라인·공정 같은 구체 값, Href = 고칠 수 있는 화면(검색어 포함).</summary>
public sealed record WoBlocker(string Code, string Detail, string Href);

/// <summary>
/// 수주 한 줄이 왜 WO 로 만들어지지 않는지 서버 규칙(PpRepository 의 RejectNo* 와 같은 판정)대로 미리 알려 준다.
/// 화면 Status 열이 이 목록을 그대로 보이고, 각 이유는 그 값을 고치는 MD 화면으로 연결한다.
/// 읽기 전용 — 아무것도 쓰지 않는다.
/// </summary>
public static class WoBlockers
{
    public const string NoItem = "NoItem", NoRouting = "NoRouting", NoCore = "NoCore", NoLotPrefix = "NoLotPrefix",
                        NoLine = "NoLine", NoMold = "NoMold", Error = "Error";

    static string Items(string q)  => "/md/fd/items?q="  + Uri.EscapeDataString(q);
    static string Bom(string q)    => "/md/fd/bom?q="    + Uri.EscapeDataString(q);
    static string Bop(string q)    => "/md/rp/bop?q="    + Uri.EscapeDataString(q);
    static string Line(string q)   => "/md/rp/line?q="   + Uri.EscapeDataString(q);
    const string MoldPage = "/md/re/mold";

    public static List<WoBlocker> Diagnose(PpRepository.PlanLineRow r, WorkOrderRepository wos, MasterDataRepository md)
    {
        var list = new List<WoBlocker>();
        if (!r.ItemExists) { list.Add(new(NoItem, r.ItemNo, Items(r.ItemNo))); return list; }
        if (r.RoutingType is null) { list.Add(new(NoRouting, r.ItemNo, Items(r.ItemNo))); return list; }

        List<WorkOrderRepository.RoutingStepPreview> template;
        try { template = wos.PreviewRouting(r.ItemNo, r.RoutingType); }
        catch (Exception ex) { list.Add(new(Error, ex.Message, Items(r.ItemNo))); return list; }

        foreach (var s in template)
        {
            if (s.NoCore) { Add(list, new(NoCore, $"{s.ProcessCode} ← {r.ItemNo}", Bom(r.ItemNo))); continue; }
            if (!s.LineRequired) continue;
            if (s.OnlyNoPrefixLines)
            {
                var lines = string.Join(", ", s.NoPrefixLines ?? []);
                Add(list, new(NoLotPrefix, $"{s.ProcessCode}: {lines}", Line((s.NoPrefixLines ?? []).FirstOrDefault() ?? "")));
                continue;
            }
            var line = s.BopLineId ?? s.Candidates.FirstOrDefault()?.LineId;
            if (string.IsNullOrEmpty(line)) { Add(list, new(NoLine, $"{s.ProcessCode} ← {r.ItemNo}", Bop(r.ItemNo))); continue; }
            if (MoldResolver.NeedsMold(s.ProcessCode))
            {
                List<MoldResolver.MoldCandidate> cands;
                try { cands = md.ListMoldCandidates(s.ItemNo, line); } catch { cands = []; }
                if (cands.Count == 0) Add(list, new(NoMold, $"{s.ItemNo} · {line}", MoldPage));
            }
        }
        return list;
    }

    static void Add(List<WoBlocker> list, WoBlocker b)
    {
        if (!list.Any(x => x.Code == b.Code && x.Detail == b.Detail)) list.Add(b);
    }
}
