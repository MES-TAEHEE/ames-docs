namespace AMES.Data.Services.Srm;

/// <summary>
/// 서현이화 공장 고객 3곳(SAV·GEO·AUB). 업로드 화면의 고객 선택은 이 세 곳으로만 제한한다(고객이 고정이라 품번 매핑은 두지 않는다).
/// </summary>
public static class PlantCustomers
{
    public static readonly IReadOnlyList<string> Ids = new[] { "CUS-SAV", "CUS-GEO", "CUS-AUB" };

    public static bool Contains(string? customerId) =>
        customerId is not null && Ids.Contains(customerId, StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// SRM 연동 설정(공통코드 SW_POSYNC_SOURCE · SW_DPSYNC_SOURCE)의 설명란 "CORCD=…;BIZCD=…;VENDCD=…;PURC_ORG=…" 에서
/// 협력사 코드(VENDCD)를 읽어 업로드 파일의 업체 코드와 대조한다. 설정이 없으면 대조할 수 없으므로 건너뛴다(경고만).
/// </summary>
public static class SrmVendorCheck
{
    /// <summary>설명란 한 줄에서 VENDCD 값. 없으면 null.</summary>
    public static string? VendCdOf(string? description)
    {
        if (string.IsNullOrWhiteSpace(description)) return null;
        foreach (var part in description.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0) continue;
            if (!part[..eq].Trim().Equals("VENDCD", StringComparison.OrdinalIgnoreCase)) continue;
            var v = part[(eq + 1)..].Trim();
            return v.Length == 0 ? null : v;
        }
        return null;
    }

    /// <summary>고객에 설정된 활성 SRM 소스들의 VENDCD 목록(중복 제거). 빈 목록이면 대조 불가.</summary>
    public static IReadOnlyList<string> ConfiguredVendors(IEnumerable<string?> descriptions) =>
        descriptions.Select(VendCdOf).Where(v => v is not null)
                    .Select(v => v!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>샘플 파일에 넣을 업체 코드: 고객 설정의 첫 VENDCD, 없으면 자리표시자(대조 대상 아님).</summary>
    public static string PickVendor(IReadOnlyList<string> configured, string placeholder) =>
        configured.Count > 0 ? configured[0] : placeholder;

    public enum Verdict { NotConfigured, Match, Mismatch }

    /// <summary>파일의 업체 코드가 고객의 협력사 코드와 맞는지. 파일 코드가 비면 대조하지 않는다(NotConfigured 로 취급).</summary>
    public static Verdict Judge(string? fileVendor, IReadOnlyList<string> configured)
    {
        if (configured.Count == 0 || string.IsNullOrWhiteSpace(fileVendor)) return Verdict.NotConfigured;
        return configured.Contains(fileVendor.Trim(), StringComparer.OrdinalIgnoreCase) ? Verdict.Match : Verdict.Mismatch;
    }
}
