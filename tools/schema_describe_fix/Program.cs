using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace AMES.Tools.SchemaDescribeFix;

/// <summary>
/// Replaces the algorithmic MS_Description comments with real semantic
/// Korean descriptions:
///
///   1. For MD_* tables — parse VOL13_MD*.html (per-entity table designs)
///      to extract authored column definitions.
///   2. For non-MD tables (PR_/PP_/WH_/PNT_/QC_/FG_/MNT_/SYS_) — apply
///      a domain dictionary keyed by table prefix + exact column name
///      (well-known MES concepts: WoID, LotID, EquipID, AndonID …),
///      falling back to a humanised generator with type info.
///
/// Idempotent: uses sp_updateextendedproperty since rows already exist
/// from the prior schema_audit_fix run.
/// </summary>
internal static class Program
{
    private const string Cs =
        "Server=localhost;Database=AMES_DEV;User Id=ames_app;Password=!Dev2026;" +
        "TrustServerCertificate=True;Encrypt=True;Connect Timeout=5;";

    private static readonly string DocRoot =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static int Main()
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.WriteLine("[describe-fix] connecting ...");
        using var conn = new SqlConnection(Cs);
        conn.Open();

        // ── Phase 1: parse VOL13 MD descriptions ────────────────────────
        var vol13 = ParseAllVol13(DocRoot);
        Console.WriteLine($"[describe-fix] VOL13 parsed: {vol13.Count} (table,column) descriptions");

        // ── Phase 2: walk every column in every user table ──────────────
        var columns = ReadAllColumns(conn);
        Console.WriteLine($"[describe-fix] DB has {columns.Count} columns to describe");

        int applied = 0;
        foreach (var c in columns)
        {
            string desc;
            // Prefer authored doc (MD module gets these)
            if (vol13.TryGetValue((c.Table, c.Column), out var fromDoc))
                desc = fromDoc + "  · " + c.SqlType;
            else
                desc = DomainDescribe(c.Table, c.Column, c.SqlType, c.IsPk, c.RefTable, c.RefColumn);

            UpdateProp(conn, c.Table, c.Column, desc);
            applied++;
        }
        Console.WriteLine($"[describe-fix] {applied} column comments updated");
        Console.WriteLine("[describe-fix] done.");
        return 0;
    }

    // ────────────────────────────────────────────────────────────────────
    //  Phase 1 — Parse VOL13_MD*.html
    // ────────────────────────────────────────────────────────────────────
    private static Dictionary<(string Tbl, string Col), string> ParseAllVol13(string root)
    {
        var dict = new Dictionary<(string, string), string>();
        foreach (var f in Directory.EnumerateFiles(root, "VOL13_MD*.html"))
        {
            // ignore the L3 hub pages (no col rows)
            if (f.Contains("_L3_") || f.EndsWith("Master_Data.html")) continue;
            var html = File.ReadAllText(f, Encoding.UTF8);

            // pull the table name from <span class="mc-val">MD_Item</span>
            var nameMatch = Regex.Match(html, """<span class="mc-val">(MD_[A-Za-z0-9_]+)</span>""");
            if (!nameMatch.Success) continue;
            var table = nameMatch.Groups[1].Value;

            // each row pattern:
            //   <td class="col">ItemNo</td>...<td>품목 코드 (...).</td>
            // grab columns + their last <td> per <tr> that contains class="col"
            var rowRx = new Regex("""
                <tr>\s*
                  <td\s+class="col">([^<]+)</td>\s*
                  <td[^>]*>[^<]*</td>\s*
                  <td[^>]*>[^<]*</td>\s*
                  <td[^>]*>(?:<[^>]+>[^<]*</[^>]+>|[^<]*)</td>\s*
                  <td[^>]*>[^<]*</td>\s*
                  <td[^>]*>([^<]*)</td>\s*
                </tr>
                """, RegexOptions.IgnorePatternWhitespace | RegexOptions.Singleline);

            foreach (Match m in rowRx.Matches(html))
            {
                var col  = m.Groups[1].Value.Trim();
                var desc = WebUtility.HtmlDecode(m.Groups[2].Value).Trim();
                // strip <code> tags from desc if any survived
                desc = Regex.Replace(desc, "<[^>]+>", "");
                if (col.Length == 0 || desc.Length == 0 || desc == "—") continue;
                dict[(table, col)] = desc;
            }
        }
        return dict;
    }

    // ────────────────────────────────────────────────────────────────────
    //  Phase 2 — Domain dictionary for non-MD tables
    // ────────────────────────────────────────────────────────────────────
    private static string DomainDescribe(string table, string col, string sqlType,
                                          bool isPk, string? refTable, string? refColumn)
    {
        // Audit columns — universal
        var auditDesc = col switch
        {
            "CreatedBy"   => "생성자 (User ID 또는 seed 마커)",
            "CreatedTS"   => "생성 시각",
            "ModifiedBy"  => "최종 수정자 (로그인 사용자 User ID)",
            "ModifiedTS"  => "최종 수정 시각",
            "ActiveFlag"  => "활성 플래그 (FALSE = 비활성/단종)",
            _             => null
        };
        if (auditDesc is not null) return $"{auditDesc} · {sqlType}";

        // PK marker takes precedence over name dictionary if the name is generic
        // (We still want a meaningful description; treat PK as a tag prefix)
        var pkTag = isPk ? "PK · " : "";

        // FK gets a target reference appended
        var fkSuffix = (refTable is not null && refColumn is not null)
            ? $" → {refTable}.{refColumn}"
            : "";

        // Domain dictionary — keyed by exact column name, with table-prefix
        // disambiguation for the most common identifier columns
        var modulePrefix = table.Split('_')[0];   // PR / PP / WH / PNT / QC / FG / MNT / SYS
        var moduleKo = modulePrefix switch
        {
            "PR"  => "생산 실적",
            "PP"  => "생산 계획",
            "WH"  => "창고",
            "PNT" => "도장",
            "QC"  => "품질",
            "FG"  => "완제품",
            "MNT" => "정비",
            "SYS" => "시스템",
            "MD"  => "마스터",
            _     => "이력"
        };

        string baseDesc = col switch
        {
            // Identifiers
            "WoID"           => $"작업지시 ID ({moduleKo})",
            "WoNumber"       => "작업지시 번호",
            "LotID"          => "Lot ID",
            "LotCode"        => "Lot 코드 (스캔용)",
            "LotNumber"      => "Lot 번호",
            "EquipID"        => "설비 ID",
            "LineID"         => "라인 ID",
            "WCID"           => "작업장 ID",
            "MoldID"         => "금형 ID",
            "JigID"          => "지그 ID",
            "ItemNo"         => "품목 번호",
            "ItemName"       => "품목명",
            "VendorID"       => "거래처 ID",
            "CustomerCode"   => "고객사 코드",
            "CustomerID"     => "고객사 ID",
            "OperatorID"     => "작업자 (AspNetUsers.Id)",
            "InspectorID"    => "검사자 (AspNetUsers.Id)",
            "ApproverID"     => "결재자 (AspNetUsers.Id)",
            "TerminalID"     => "단말기 ID (POP/PDA)",
            "SessionID"      => "POP 세션 ID",
            "AndonID"        => "안돈 콜 ID",
            "ResultID"       => "생산 실적 ID",
            "AcceptID"       => "WO 수락 ID",
            "InspectionID"   => "검사 ID",
            "InspectionNo"   => "검사 번호",
            "HoldID"         => "QC 보류 ID",
            "NcrID"          => "NCR(부적합) ID",
            "FailureID"      => "고장 등록 ID",
            "FailureNumber"  => "고장 번호",
            "WorkOrderID"    => "정비 WO ID",
            "PMScheduleID"   => "PM 일정 ID",
            "PMTemplateID"   => "PM 템플릿 ID",
            "PartNo"         => "예비품 번호",
            "PartName"       => "예비품 명",
            "StockID"        => "재고 ID",
            "StockNumber"    => "재고 번호 (바코드)",
            "ShipmentOrderID"=> "출하 지시 ID",
            "ShipOrderNumber"=> "출하 지시 번호",
            "PoID"           => "구매 발주 ID",
            "PoNumber"       => "구매 발주 번호",
            "VirtualLotID"   => "PNT 가상 Lot ID",
            "PlanID"         => "계획 ID",
            "ForecastID"     => "수요 예측 ID",
            "MrpRunID"       => "MRP 실행 ID",
            "ScheduleID"     => "스케줄 ID",
            "DowntimeID"     => "다운타임 ID",
            "BondSetupID"    => "본드 설정 ID",
            "MoldChangeID"   => "금형 교체 ID",
            "DefectID"       => "불량 상세 ID",
            "DefectCode"     => "불량 코드",
            "DefectName"     => "불량 명",
            "ReasonCode"     => "사유 코드",
            "CauseCode"      => "원인 코드",
            "RoleName"       => "역할 명",
            "RoleID"         => "역할 ID (AspNetRoles)",
            "UserID"         => "사용자 ID (AspNetUsers)",
            "EmployeeNo"     => "사원 번호",
            "EmployeeName"   => "사원 명",
            "ConfigKey"      => "설정 키",
            "ConfigValue"    => "설정 값",

            // Status / type / flag
            "Status"         => $"{moduleKo} 상태",
            "PrevStatus"     => "이전 상태",
            "PriorStatus"    => "이전 상태",
            "HoldFlag"       => "QC 보류 플래그",
            "Priority"       => "우선순위",
            "Severity"       => "중대도",
            "Verdict"        => "최종 판정 (Pass/Fail/Hold 등)",

            // Quantity
            "Qty"            => "수량",
            "OrderQty"       => "지시 수량",
            "GoodQty"        => "양품 수량",
            "DefectQty"      => "불량 수량",
            "CompletedQty"   => "완료 수량",
            "PlannedQty"     => "계획 수량",
            "RemainingQty"   => "잔여 수량",
            "LoadedQty"      => "투입 수량",
            "ConfirmedQty"   => "확정 수량",
            "PickedQty"      => "출고/피킹 수량",
            "ShippedQty"     => "출하 수량",
            "AllocatedQty"   => "할당 수량",
            "ReceiptQty"     => "입고 수량",

            // Time / date
            "StartTS"        => "시작 시각",
            "EndTS"          => "종료 시각",
            "StartedAt"      => "시작 시각",
            "CompletedAt"    => "완료 시각",
            "ClosedAt"       => "마감 시각",
            "ReleasedAt"     => "릴리즈 시각",
            "TriggeredAt"    => "발생 시각",
            "AckedAt"        => "확인 시각",
            "ResumedAt"      => "재개 시각",
            "DueDate"        => "납기일",
            "RequestedDeliveryDate" => "요청 납기일",
            "PromisedDate"   => "약속 납기일",
            "DurationMin"    => "지속 시간 (분)",
            "DowntimeSec"    => "다운타임 (초)",
            "DowntimeMin"    => "다운타임 (분)",
            "LaborMinutes"   => "투입 인시 (분)",
            "ShiftCode"      => "교대 코드 (A/B/C)",
            "EventTS"        => "이벤트 시각",
            "EventDate"      => "이벤트 일자",
            "ScheduleDate"   => "스케줄 일자",

            // Cost / rate
            "UnitCost"       => "단가",
            "UnitPrice"      => "단가",
            "OEE"            => "OEE 종합 (%)",
            "Availability"   => "가용도 (%)",
            "Performance"    => "성능 (%)",
            "Quality"        => "품질 (%)",

            // Generic
            "Comment"        => "비고",
            "Note"           => "비고",
            "Description"    => "설명",
            "Symptom"        => "증상 / 현상",
            "Source"         => "발생 출처 / 채널",
            "Channel"        => "채널",
            "Endpoint"       => "엔드포인트 URL/주소",
            "Protocol"       => "통신 프로토콜",
            "Result"         => "결과 (OK/FAIL 등)",
            "Reason"         => "사유",
            "Action"         => "조치",
            "Category"       => "분류",
            "Type"           => "유형",
            "Group"          => "그룹",
            "Plant"          => "사업장",
            "Location"       => "위치/적치 코드",
            "StorageLoc"     => "보관 위치",
            "Department"     => "부서",
            _                => null!
        };

        if (baseDesc is null)
        {
            // last-resort humanised name
            var human = Regex.Replace(col, "(?<!^)([A-Z][a-z])|(?<=[a-z0-9])([A-Z])", " $1$2");
            return $"{pkTag}{human}{fkSuffix} · {sqlType}";
        }
        return $"{pkTag}{baseDesc}{fkSuffix} · {sqlType}";
    }

    // ────────────────────────────────────────────────────────────────────
    //  DB helpers
    // ────────────────────────────────────────────────────────────────────
    private record ColInfo(string Table, string Column, string SqlType, bool IsPk,
                           string? RefTable, string? RefColumn);

    private static List<ColInfo> ReadAllColumns(SqlConnection conn)
    {
        const string sql = """
            SELECT  t.name AS T, c.name AS C,
                    TYPE_NAME(c.user_type_id) + CASE
                       WHEN TYPE_NAME(c.user_type_id) IN ('varchar','nvarchar','char','nchar')
                            AND c.max_length > 0
                            THEN '(' + CAST(CASE WHEN TYPE_NAME(c.user_type_id) IN ('nvarchar','nchar')
                                                  THEN c.max_length / 2 ELSE c.max_length END AS VARCHAR(10)) + ')'
                       WHEN TYPE_NAME(c.user_type_id) IN ('decimal','numeric')
                            THEN '(' + CAST(c.precision AS VARCHAR(10)) + ',' + CAST(c.scale AS VARCHAR(10)) + ')'
                       ELSE ''
                    END AS Ty,
                    CASE WHEN ic.column_id IS NOT NULL THEN 1 ELSE 0 END AS IsPk,
                    fk.RefTable, fk.RefColumn
            FROM    sys.tables t
            JOIN    sys.columns c ON c.object_id = t.object_id
            LEFT JOIN (
                SELECT i.object_id, ic.column_id
                FROM sys.indexes i
                JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
                WHERE i.is_primary_key=1
            ) ic ON ic.object_id=t.object_id AND ic.column_id=c.column_id
            LEFT JOIN (
                SELECT  fkc.parent_object_id, fkc.parent_column_id,
                        OBJECT_NAME(fkc.referenced_object_id) AS RefTable,
                        COL_NAME(fkc.referenced_object_id, fkc.referenced_column_id) AS RefColumn
                FROM    sys.foreign_key_columns fkc
            ) fk ON fk.parent_object_id=t.object_id AND fk.parent_column_id=c.column_id
            WHERE   t.is_ms_shipped=0
            ORDER BY t.name, c.column_id;
            """;
        using var cmd = new SqlCommand(sql, conn);
        using var rdr = cmd.ExecuteReader();
        var list = new List<ColInfo>();
        while (rdr.Read())
            list.Add(new ColInfo((string)rdr["T"], (string)rdr["C"], (string)rdr["Ty"],
                                  (int)rdr["IsPk"] == 1,
                                  rdr["RefTable"] as string, rdr["RefColumn"] as string));
        return list;
    }

    private static void UpdateProp(SqlConnection conn, string table, string col, string description)
    {
        // Prior tool already inserted rows → use sp_updateextendedproperty.
        // Fall back to sp_addextendedproperty if a row is missing.
        const string existsSql = """
            SELECT 1 FROM sys.extended_properties ep
            JOIN   sys.tables  t ON t.object_id = ep.major_id
            JOIN   sys.columns c ON c.object_id = ep.major_id AND c.column_id = ep.minor_id
            WHERE  ep.name='MS_Description' AND t.name=@T AND c.name=@C;
            """;
        bool exists;
        using (var cmd = new SqlCommand(existsSql, conn))
        {
            cmd.Parameters.AddWithValue("@T", table);
            cmd.Parameters.AddWithValue("@C", col);
            exists = cmd.ExecuteScalar() is not null;
        }
        var sp = exists ? "sp_updateextendedproperty" : "sp_addextendedproperty";
        var spSql = $"EXEC {sp} N'MS_Description', @V, 'SCHEMA', 'dbo', 'TABLE', @T, 'COLUMN', @C;";
        using var ex = new SqlCommand(spSql, conn);
        ex.Parameters.AddWithValue("@V", description);
        ex.Parameters.AddWithValue("@T", table);
        ex.Parameters.AddWithValue("@C", col);
        ex.ExecuteNonQuery();
    }
}
