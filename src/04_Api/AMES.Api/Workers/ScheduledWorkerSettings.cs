namespace AMES.Api.Workers;

/// <summary>
/// 외부 API 연동 Worker 의 시간 설정 해석 — Worker 값(공통코드) → 공통 기본값(appsettings <c>ScheduledWorker</c>) → 내장 기본값.
/// 범위를 벗어난 값은 없는 것으로 보고 다음 단계로 넘어간다.
/// </summary>
public static class ScheduledWorkerSettings
{
    public const string Section = "ScheduledWorker";

    public const int DefaultTickSec         = 60;
    public const int DefaultStartupDelaySec = 10;
    public const int DefaultTimeoutSec      = 60;

    public static int TickSec(int? worker, IConfiguration cfg)
        => Pick(worker, Common(cfg, "TickSec"), DefaultTickSec, min: 1);

    public static int StartupDelaySec(int? worker, IConfiguration cfg)
        => Pick(worker, Common(cfg, "StartupDelaySec"), DefaultStartupDelaySec, min: 0);

    public static int TimeoutSec(int? worker, IConfiguration cfg)
        => Pick(worker, Common(cfg, "TimeoutSec"), DefaultTimeoutSec, min: 1);

    private static int Pick(int? worker, int? common, int builtIn, int min)
        => worker is { } w && w >= min ? w
         : common is { } c && c >= min ? c
         : builtIn;

    private static int? Common(IConfiguration cfg, string key)
        => int.TryParse(cfg[$"{Section}:{key}"], out var v) ? v : null;
}
