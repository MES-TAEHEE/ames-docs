using Microsoft.Extensions.Configuration;

namespace AMES.Pop.Common;

/// <summary>
/// Loads appsettings.json once at startup.
/// Process-wide singleton — fetched via AppConfig.Current.
/// </summary>
public sealed class AppConfig
{
    public string ConnectionString { get; }
    public string DefaultShift { get; }

    /// <summary>화면 표시 언어: "ko"(기본) 또는 "en". PopLang 초기값.</summary>
    public string Language { get; }

    /// <summary>라벨 ZPL 프린터 설정 — Mode: Tcp(네트워크) · Spooler(Windows 큐, USB) · File(기본값).</summary>
    public string PrinterMode      { get; }
    public string PrinterHost      { get; }
    public int    PrinterPort      { get; }
    /// <summary>라벨(.zpl)과 POP 로그(*.log)를 함께 쓰는 폴더 — 기본값은 실행 파일 옆 labels 폴더.</summary>
    public string PrinterOutputDir { get; }
    /// <summary>Spooler 모드 전용 — Windows 에 등록된 프린터 이름.</summary>
    public string PrinterName      { get; }

    /// <summary>라벨 자동 발행 폴링 주기(ms). 0 이하면 자동 발행 비활성.</summary>
    public int PrinterPollMs { get; }

    /// <summary>연속 출력 실패가 이 횟수에 도달하면 자동 발행을 멈춘다.</summary>
    public int PrinterMaxFailures { get; }

    /// <summary>시리얼 스캐너 COM 포트. 비어 있으면 시리얼 스캐너 비활성(HID 만).</summary>
    public string ScannerPortName { get; }

    /// <summary>포트 열기 실패·끊김 후 재시도 간격(ms).</summary>
    public int ScannerReconnectMs { get; }

    /// <summary>무작업이 이만큼 이어지면 로그인 화면으로 되돌린다(분). 0 이하면 비활성.</summary>
    public int IdleLogoutMinutes { get; }

    /// <summary>자동 로그아웃 몇 초 전에 카운트다운 경고를 띄울지.</summary>
    public int IdleWarnSeconds { get; }

    /// <summary>개발용 — true 면 전체 화면(테두리 없음·최대화) 대신 크기 조절 가능한 일반 창으로 연다.</summary>
    public bool DevWindowed { get; }

    /// <summary>
    /// DB 연결 대기(초). 0 이하·없음이면 접속문자열의 Connect Timeout 을 그대로 쓴다. DB 가 응답하지 않을 때
    /// 오프라인으로 판정되기까지 걸리는 시간이 이 값이다. 원격 서버 + Encrypt=True 는 TLS 핸드셰이크가 5초를 넘길 수 있어
    /// 너무 낮추면 정상 DB 도 끊긴 것으로 본다 — 같은 PC·사내망 DB 에서만 낮출 것.
    /// </summary>
    public int DbConnectTimeoutSeconds { get; }

    /// <summary>오프라인 대비 데이터 보관 범위 — 로그아웃 시점 기준 과거 N일.</summary>
    public int OfflinePastDays { get; }

    /// <summary>오프라인 대비 데이터 보관 범위 — 로그아웃 시점 기준 미래 N일(계획·작업지시).</summary>
    public int OfflineFutureDays { get; }

    /// <summary>오프라인 로그인용 직원 사본의 유효 기간(일). 지나면 오프라인 로그인을 받지 않는다.</summary>
    public int OfflineCacheMaxAgeDays { get; }
    /// <summary>오프라인 사본·큐 파일 폴더. 비어 있으면 %LocalAppData%\AMES\Pop.</summary>
    public string OfflineDir { get; }

    /// <summary>로그인해 있는 동안 작업 사본을 새로 받는 주기(분). 로그아웃 때는 이 주기와 무관하게 받는다.</summary>
    public int OfflineRefreshMinutes { get; }

    /// <summary>오프라인에서 새 LOT 번호로 쓰려고 라인·날짜마다 미리 확보해 두는 순번 수. 안 쓴 번호는 결번이 된다.</summary>
    public int OfflineLotBlockSize { get; }

    /// <summary>내일 날짜의 번호 구간은 이 시각(시) 이후에만 확보한다 — 자정을 넘기는 정전만 대비하고, 평소엔 결번을 만들지 않는다.</summary>
    public int OfflineTomorrowBlockFromHour { get; }

    /// <summary>복구 뒤 서버가 "없는 LOT"이라고 한 건을 충돌로 보기 전에 다시 시도하는 시간(분) — 다른 PC 의 에이전트가 아직 못 올린 LOT 일 수 있다.</summary>
    public int OfflineNotFoundRetryMinutes { get; }

    /// <summary>
    /// 출력 폴더 결정. 비어 있거나 없으면 실행 파일 폴더의 labels — 작업 폴더(바로가기의 시작 위치)에 따라 달라지지 않도록.
    /// 상대 경로는 실행 파일 폴더 기준, 절대 경로·%VAR% 는 그대로 쓴다.
    /// </summary>
    public static string ResolveOutputDir(string? configured, string baseDir)
    {
        var value = Environment.ExpandEnvironmentVariables((configured ?? "").Trim());
        if (value.Length == 0) value = "labels";
        return Path.IsPathRooted(value) ? value : Path.Combine(baseDir, value);
    }

    private static readonly Lazy<AppConfig> _instance = new(Load);
    public static AppConfig Current => _instance.Value;

    private AppConfig(IConfigurationRoot root)
    {
        ConnectionString = root.GetConnectionString("AMES")
            ?? throw new InvalidOperationException("ConnectionStrings:AMES is missing in appsettings.json");
        DefaultShift = root["PopTerminal:DefaultShift"] ?? "A";   // 공통코드 WORK_SHIFT 기준

        DevWindowed  = bool.TryParse(root["PopTerminal:DevWindowed"], out var dw) && dw;

        DbConnectTimeoutSeconds = int.TryParse(root["PopTerminal:Db:ConnectTimeoutSeconds"], out var cts) && cts > 0 ? cts : 0;
        OfflinePastDays        = int.TryParse(root["PopTerminal:Offline:PastDays"],        out var opd) && opd >= 0 ? opd : 7;
        OfflineFutureDays      = int.TryParse(root["PopTerminal:Offline:FutureDays"],      out var ofd) && ofd >= 0 ? ofd : 7;
        OfflineCacheMaxAgeDays = int.TryParse(root["PopTerminal:Offline:CacheMaxAgeDays"], out var oma) && oma > 0 ? oma : 7;
        OfflineDir             = root["PopTerminal:Offline:Dir"] ?? "";
        OfflineRefreshMinutes  = int.TryParse(root["PopTerminal:Offline:RefreshMinutes"],  out var orm) && orm > 0 ? orm : 10;
        OfflineLotBlockSize    = int.TryParse(root["PopTerminal:Offline:LotBlockSize"],    out var olb) && olb > 0 ? olb : 20;
        OfflineTomorrowBlockFromHour = int.TryParse(root["PopTerminal:Offline:TomorrowBlockFromHour"], out var otb) && otb is >= 0 and <= 24 ? otb : 22;
        OfflineNotFoundRetryMinutes  = int.TryParse(root["PopTerminal:Offline:NotFoundRetryMinutes"],  out var onr) && onr >= 0 ? onr : 30;

        Language     = (root["PopTerminal:Language"] ?? "ko").ToLowerInvariant() == "en" ? "en" : "ko";

        PrinterMode      = root["PopTerminal:Printer:Mode"]      ?? "File";
        PrinterHost      = root["PopTerminal:Printer:Host"]      ?? "127.0.0.1";
        PrinterPort      = int.TryParse(root["PopTerminal:Printer:Port"], out var pp) ? pp : 9100;
        PrinterOutputDir = ResolveOutputDir(root["PopTerminal:Printer:OutputDir"], AppContext.BaseDirectory);
        PrinterName      = root["PopTerminal:Printer:Name"]      ?? "";
        PrinterPollMs      = int.TryParse(root["PopTerminal:Printer:PollMs"], out var pms) ? pms : 1000;
        PrinterMaxFailures = int.TryParse(root["PopTerminal:Printer:MaxFailures"], out var pmf) ? pmf : 3;

        ScannerPortName    = (root["PopTerminal:Scanner:PortName"] ?? string.Empty).Trim();
        ScannerReconnectMs = int.TryParse(root["PopTerminal:Scanner:ReconnectMs"], out var srm) && srm > 0 ? srm : 3000;

        // 값이 이상해도 여기서 막지 않는다 — IdleWatcher 가 경고 창을 유휴 시간에 맞춰 접는다.
        IdleLogoutMinutes = int.TryParse(root["PopTerminal:IdleLogoutMinutes"], out var ilm) ? ilm : 30;
        IdleWarnSeconds   = int.TryParse(root["PopTerminal:IdleWarnSeconds"],   out var iws) ? iws : 60;
    }

    private static AppConfig Load()
    {
        var basePath = Path.GetDirectoryName(AppContext.BaseDirectory)!;

        // Same environment split as AMES.Web: base appsettings.json holds terminal
        // settings, and appsettings.{env}.json overrides the connection string.
        // launchSettings sets DOTNET_ENVIRONMENT=Development for `dotnet run`;
        // a deployed terminal has no env var and falls back to Production.
        var env = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? "Production";

        var root = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .AddJsonFile($"appsettings.{env}.json", optional: true, reloadOnChange: false)
            .Build();
        return new AppConfig(root);
    }
}
