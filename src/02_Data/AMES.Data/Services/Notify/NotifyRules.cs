namespace AMES.Data.Services.Notify;

/// <summary>
/// PUSH 알림 발송기(AMES.Api 「NOTIFY」 Worker)의 규칙 — 10-24 사용자 결정.
///   · 수신자는 사번. 규칙의 수신 역할(역할 ID)을 가진 활성 웹 사용자 + 규칙에 Operator(ROLE-OPERATOR)가 있으면 PIN 이 있는 활성 현장 작업자 전원
///   · 기기 = SYS_NotificationChannel 의 PUSH 행(주소 = FCM 토큰). 한 번 등록하면 세션이 끝나도 받는다 — 앱이 끄거나, 다른 사람이 같은 기기에 로그인하거나,
///     FCM 이 토큰을 무효로 알리거나, 계정이 사용 안 함이 되면 멈춘다
///   · 발송 이력 = SYS_NotificationHistory 기기마다 1행. 상태 PENDING → SENDING → SENT / FAILED(재시도) / QUIET(방해 금지 시간 — 보내지 않고 목록에만)
/// </summary>
public static class NotifyRules
{
    public const string MonitorCode = "NOTIFY";
    public const string GroupGlobal = "SW_" + MonitorCode;
    public const string Channel     = "PUSH";
    /// <summary>발송기가 쓰는 행위자(CreatedBy·ModifiedBy, varchar(20)).</summary>
    public const string Actor       = "PUSH-SVC";
    /// <summary>작업자를 규칙 수신자로 고르는 역할 — 활성 작업자는 이 역할을 가진 것으로 본다(사용자 결정 A).</summary>
    public const string WorkerRoleId = SystemRoles.OperatorId;

    public static class Status
    {
        public const string Pending = "PENDING";
        public const string Sending = "SENDING";
        public const string Sent    = "SENT";
        public const string Failed  = "FAILED";
        public const string Quiet   = "QUIET";
    }

    /// <summary>1차로 감지하는 이벤트 — SYS_NotificationRule.EventTypeCode 와 같은 값.
    /// OTD_AT_RISK·SAP_SYNC_FAIL·PLC_DOWN·DAILY_DIGEST 는 보낼 기준이 아직 없어 감지하지 않는다.</summary>
    public static class Events
    {
        public const string FailureOpen = "FAILURE_OPEN";
        public const string AndonRaised = "ANDON_RAISED";
        public const string QcHold      = "QC_HOLD";
        public const string PmOverdue   = "PM_OVERDUE";
        public const string LowStock    = "LOW_STOCK";
        public static readonly IReadOnlyList<string> All = [FailureOpen, AndonRaised, QcHold, PmOverdue, LowStock];
    }

    public const int SourceRefIdMax = 40;     // SYS_NotificationHistory.SourceRefID varchar(40)
    public const int TokenMax       = 255;    // SYS_NotificationChannel.Address varchar(255)

    /// <summary>방해 금지 시간 — 둘 다 있고 서로 다를 때만. 시작 &gt; 끝이면 자정을 넘는 구간(22:00~06:00). 끝 시각은 포함하지 않는다.</summary>
    public static bool InQuietHours(TimeSpan now, TimeSpan? start, TimeSpan? end)
    {
        if (start is not { } s || end is not { } e || s == e) return false;
        return s < e ? now >= s && now < e : now >= s || now < e;
    }

    /// <summary>날마다 한 번 보내는 상태형 이벤트(PM 지연·재고 부족)의 원천 번호 — 「번호@yyyyMMdd」, 40자에서 자른다.</summary>
    public static string DailyRef(string id, DateTime day)
    {
        var suffix = "@" + day.ToString("yyyyMMdd");
        var head = id.Length + suffix.Length > SourceRefIdMax ? id[..(SourceRefIdMax - suffix.Length)] : id;
        return head + suffix;
    }

    /// <summary>실패 뒤 다시 보내기까지 기다리는 분 — 2^재시도 횟수, 최대 30분.</summary>
    public static int RetryDelayMin(int retryCount) => Math.Min(30, 1 << Math.Clamp(retryCount, 0, 5));

    /// <summary>앱이 보내는 FCM 토큰 — 비어 있지 않고 255자 이하, 공백 없음.</summary>
    public static bool IsValidToken(string? token)
        => !string.IsNullOrWhiteSpace(token) && token.Length <= TokenMax && !token.Any(char.IsWhiteSpace);
}
