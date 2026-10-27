using System.Text.RegularExpressions;

namespace AMES.Data.Services;

/// <summary>
/// 연동 모니터(SYS_InterfaceMonitor)·알림 이력(SYS_NotificationHistory)을 세는 공용 기준(10-28) — SYS-006 인터페이스·SYS-008 알림·SYS-010 상태가
/// 같은 상태 집합·같은 시각 기준으로 센다. 전에는 화면마다 달라(SYS-010 = OK·UP 이 아닌 전부, SYS-006 = DOWN·ERROR·FAULT, SYS-008 = 최근 100건의 FAILED)
/// 같은 순간의 숫자가 어긋났다.
/// </summary>
public static class MonitorRules
{
    // 공통코드 IF_STATUS 값
    public static readonly IReadOnlySet<string> InterfaceOk   = Set("OK", "UP");
    public static readonly IReadOnlySet<string> InterfaceWarn = Set("WARN", "DEGRADED");
    public static readonly IReadOnlySet<string> InterfaceDown = Set("DOWN", "ERROR", "FAULT");

    /// <summary>알림 발송 실패 상태 — 발송기(NotifyRules)는 FAILED 를 쓰고, 옛 데이터·다른 경로의 ERROR 도 실패로 센다.</summary>
    public static readonly IReadOnlySet<string> NotificationFailed = Set("FAILED", "ERROR");
    public const string NotificationSent = "SENT";

    /// <summary>
    /// 정상(OK·UP)으로 기록된 연동도 마지막 성공 뒤 허용 간격(MaxGapMinutes)을 넘기면 경고, 두 배를 넘기면 다운으로 본다 —
    /// 연동이 멈추면 Worker 가 결과를 쓰지 않아 기록은 OK 로 남기 때문이다. 허용 간격이 없거나(0 이하) 성공 기록이 없으면 기록 그대로.
    /// 사용 안 함(IsEnabled 0) 연동은 시간으로 판정하지 않는다.
    /// </summary>
    public const int DownGapFactor = 2;

    public static string? EffectiveInterfaceStatus(string? status, DateTime? lastSync, int minutesSinceSync, int? maxGapMinutes, bool isEnabled = true)
    {
        if (!isEnabled || status is null || !InterfaceOk.Contains(status)) return status;
        if (lastSync is null || maxGapMinutes is not > 0) return status;
        if (minutesSinceSync > maxGapMinutes.Value * DownGapFactor) return "DOWN";
        if (minutesSinceSync > maxGapMinutes.Value) return "WARN";
        return status;
    }

    public static bool IsOk(string? s)   => s is not null && InterfaceOk.Contains(s);
    public static bool IsWarn(string? s) => s is not null && InterfaceWarn.Contains(s);
    public static bool IsDown(string? s) => s is not null && InterfaceDown.Contains(s);

    /// <summary>SQL IN 목록(상수라 매개변수가 아니어도 안전) — 예 <c>Status IN ({MonitorRules.SqlList(NotificationFailed)})</c>.</summary>
    public static string SqlList(IEnumerable<string> values) => string.Join(", ", values.Select(v => $"'{v}'"));

    static readonly Regex SecretParam = new("(token|key|secret|password|passwd|pwd|auth|sig|signature|credential)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// 연동 주소를 보일 때 비밀값을 가린다 — <c>user:pw@</c> 의 비밀번호와 이름에 token·key·secret·password·auth·sig 가 든 쿼리 값을 <c>***</c> 로.
    /// 화면·Api 응답 모두 이 값만 내보낸다(원문은 Worker 설정·공통코드에만 있다).
    /// </summary>
    public static string? MaskEndpoint(string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint)) return endpoint;
        var s = endpoint;
        // scheme://user:password@host → scheme://user:***@host
        s = Regex.Replace(s, @"^([a-zA-Z][a-zA-Z0-9+.\-]*://[^/:@\s]*):[^@/\s]*@", "$1:***@");
        var q = s.IndexOf('?');
        if (q < 0) return s;
        var frag = s.IndexOf('#', q);
        var query = frag < 0 ? s[(q + 1)..] : s[(q + 1)..frag];
        var parts = query.Split('&').Select(p =>
        {
            var eq = p.IndexOf('=');
            if (eq <= 0) return p;
            var name = Uri.UnescapeDataString(p[..eq]);
            return SecretParam.IsMatch(name) && eq < p.Length - 1 ? p[..(eq + 1)] + "***" : p;
        });
        return s[..(q + 1)] + string.Join("&", parts) + (frag < 0 ? "" : s[frag..]);
    }

    static IReadOnlySet<string> Set(params string[] v) => new HashSet<string>(v, StringComparer.OrdinalIgnoreCase);
}
