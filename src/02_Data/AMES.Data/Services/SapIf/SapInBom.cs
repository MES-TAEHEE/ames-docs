using AMES.Data.Connection;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Services.SapIf;

/// <summary>
/// IF-M03 BOM 수신: EOS_IF.IF_MD_BOM_HDR 1행 + IF_MD_BOM_LINE N행(HDR_SEQ) → MD_BomVersion + MD_Bom 을 한 트랜잭션으로 반영한다.
/// 정의서 2.2 BOM 규칙: 헤더 버전의 기존 MD_Bom 라인 중 IF 라인에 없는 것은 ActiveFlag=0.
/// 정책(11-13): SAP 값이 우선이라 헤더·라인 모두 SAP 값으로 덮어쓴다. 버전 자체가 AMES 에만 있으면 그대로 둔다.
/// 가정: IF_ACTION='D'(헤더) 는 버전 Status 를 EXPIRED 로, 그 버전의 모든 라인을 ActiveFlag=0 으로 처리한다(삭제 없음).
/// MD-004 의 "승인 시 겹치는 기존 승인 버전 종료일 마감" 같은 화면 규칙은 거치지 않는다 — SAP 가 원천이라 EffFrom/EffTo 도 SAP 값 그대로다.
/// </summary>
internal static class SapInBom
{
    public const string LineEosTable = "IF_MD_BOM_LINE";

    public static readonly MasterSpec Version = new(
        "IF-M03", "BOM 버전", "IF_MD_BOM_HDR", "MD_BomVersion", "VersionID",
        ["RootItemNo", "VersionNo", "EffFrom", "EffTo", "ChangeType", "ChangeReason", "ApprovedTS", "Status"],
        "Status", "EXPIRED", []);

    public static readonly MasterSpec Line = new(
        "IF-M03", "BOM 라인", LineEosTable, "MD_Bom", "BOMID",
        ["ParentItemNo", "CompItemNo", "BOMLevel", "QtyPer", "UOM", "ScrapPct", "VersionID", "Position", "ActiveFlag"],
        "ActiveFlag", false, ["ActiveFlag"], SapFlag: false);

    public static (int Affected, char Kind, string Note) Apply(SqlConnection conn, SqlTransaction tx,
        IReadOnlyDictionary<string, object?> header, IReadOnlyList<Dictionary<string, object?>> lines)
    {
        var versionId = header["VersionID"];
        var (hKind, hNote) = SapInRunner.Compare(conn, tx, Version, header);
        int affected = SapInRunner.Apply(conn, tx, Version, header);

        // 헤더 비활성: 버전을 만료로 두고 라인을 모두 끈다.
        if (SapIfRules.IsDeactivate(header.GetValueOrDefault("IF_ACTION") as string))
        {
            int off = DeactivateMissing(conn, tx, versionId, []);
            return (affected + off, 'C', $"{hNote}; 라인 비활성 {off}건");
        }

        int created = 0, changed = 0, same = 0;
        var changes = new List<string>();   // 변경된 라인의 전·후 값(로그용, 최대 5건)
        foreach (var line in lines)
        {
            // 라인은 항상 업서트 — 비활성은 IF 에 빠진 것으로만 표현된다.
            var row = new Dictionary<string, object?>(line, StringComparer.OrdinalIgnoreCase) { ["IF_ACTION"] = "U" };
            var (k, lineNote) = SapInRunner.Compare(conn, tx, Line, row);
            affected += SapInRunner.Apply(conn, tx, Line, row);
            if (k == 'N') created++;
            else if (k == 'C')
            {
                changed++;
                if (changes.Count < 5) changes.Add($"[{row["BOMID"]} {lineNote.Replace("변경(SAP 우선) ", "")}]");
            }
            else same++;
        }

        var ids = lines.Select(l => l["BOMID"]?.ToString()).Where(x => x is not null).Select(x => x!).ToList();
        int deactivated = DeactivateMissing(conn, tx, versionId, ids);
        affected += deactivated;

        var kind = hKind == 'S' && (created + changed + deactivated) > 0 ? 'C' : hKind;
        var note = $"{hNote}; 라인 {lines.Count}건: 신규 {created}·변경 {changed}·동일 {same}·IF 에 없어 비활성 {deactivated}"
                 + (changes.Count > 0 ? " " + string.Join(" ", changes) + (changed > changes.Count ? " …" : "") : "");
        return (affected, kind, note);
    }

    /// <summary>그 버전의 라인 중 IF 라인(<paramref name="keepIds"/>)에 없고 아직 켜져 있는 것을 끈다.</summary>
    private static int DeactivateMissing(SqlConnection conn, SqlTransaction tx, object? versionId, IReadOnlyList<string> keepIds)
    {
        using var cmd = new SqlCommand { Connection = conn, Transaction = tx, CommandTimeout = 30 };
        ColumnParams.Add(cmd, "@V", "MD_Bom.VersionID", versionId);
        for (int i = 0; i < keepIds.Count; i++) ColumnParams.Add(cmd, "@b" + i, "MD_Bom.BOMID", keepIds[i]);
        var notIn = keepIds.Count == 0 ? "" : $" AND BOMID NOT IN ({string.Join(",", keepIds.Select((_, i) => "@b" + i))})";
        cmd.CommandText = $"""
            UPDATE dbo.MD_Bom
               SET ActiveFlag = 0, ModifiedBy = '{SapIfRules.Actor}', ModifiedTS = SYSDATETIME()
             WHERE VersionID = @V AND ISNULL(ActiveFlag, 1) = 1{notIn}
            """;
        return cmd.ExecuteNonQuery();
    }
}
