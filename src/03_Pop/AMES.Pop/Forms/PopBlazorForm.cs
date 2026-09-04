using AMES.Pop.Common;
using AMES.Pop.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebView.WindowsForms;
using Microsoft.Extensions.DependencyInjection;

namespace AMES.Pop.Forms;

/// <summary>
/// WinForms shell that hosts the entire Blazor POP UI for the life of the
/// application. Session state lives in <see cref="AppState"/> (a singleton
/// inside the WebView's DI container), so login / logout never needs to
/// destroy and recreate this form — Login.razor signs in, AppState changes,
/// the router moves to /injmain or /imgmain, logout signs out and goes back to
/// /login.
/// </summary>
public class PopBlazorForm : PopForm
{
    private readonly BlazorWebView _webView;
    private System.Threading.Timer? _labelTimer;
    private SerialScannerReader?    _scanner;

    public PopBlazorForm()
    {
        Text = "A-MES POP";

        var services = new ServiceCollection();
        services.AddWindowsFormsBlazorWebView();
#if DEBUG
        services.AddBlazorWebViewDeveloperTools();
#endif
        services.AddSingleton<AppState>();
        services.AddSingleton<ToastService>();
        services.AddSingleton<ConfirmService>();
        services.AddSingleton<LabelDispatcher>(_ => new LabelDispatcher(
            new RepoInjLotClaimStore(), new ZplLabelSink(),
            AppConfig.Current.PrinterMaxFailures, LogDispatch));
        services.AddSingleton<ScannerService>(_ => new ScannerService(LogScanner));

        var provider = services.BuildServiceProvider();

        _webView = new BlazorWebView
        {
            Dock      = DockStyle.Fill,
            HostPage  = "wwwroot/index.html",
            StartPath = "/login",
            Services  = provider,
        };
        _webView.RootComponents.Add(new RootComponent(
            selector:      "#app",
            componentType: typeof(AppRoot),
            parameters:    new Dictionary<string, object?>()));

        Controls.Add(_webView);

        WireLabelDispatcher(provider);
        WireScanner(provider);

        BlazorHost.ActionRequested += OnAction;
        FormClosing += (_, _) => BlazorHost.ActionRequested -= OnAction;
    }

    // 라벨 발행은 화면 수명과 무관해야 한다 — 로그인 동안 계속, 어느 화면이든.
    // WinForms 타이머를 쓰면 안 된다: Tick() 이 DB + TCP 를 동기로 타는데
    // 프린터 연결 타임아웃이 2초라 UI 스레드가 그만큼 얼어붙는다.
    private void WireLabelDispatcher(IServiceProvider provider)
    {
        var pollMs = AppConfig.Current.PrinterPollMs;
        if (pollMs <= 0) return;

        var state      = provider.GetRequiredService<AppState>();
        var toasts     = provider.GetRequiredService<ToastService>();
        var dispatcher = provider.GetRequiredService<LabelDispatcher>();

        dispatcher.OnStopped += () => toasts.Bad(PopLang.T("LabelAutoDispatchStopped"));

        // 백그라운드 타이머 콜백에서 예외가 새어 나가면 프로세스가 죽는다.
        _labelTimer = new System.Threading.Timer(_ =>
        {
            try { dispatcher.Tick(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[LabelDispatcher] tick failed: {ex}"); }
        }, null, Timeout.Infinite, Timeout.Infinite);

        state.OnChange += () =>
        {
            // 모듈은 로그인 시 선택 라인에서 결정된다 — INJ 세션에서만 발행.
            if (state.Session is { } s && state.ModuleCode == "INJ")
            {
                dispatcher.Start(s.LineId, s.TerminalId);
                _labelTimer.Change(pollMs, pollMs);
            }
            else
            {
                _labelTimer.Change(Timeout.Infinite, Timeout.Infinite);
                dispatcher.Stop();
            }
        };
    }

    // 스캐너는 로그인과 무관하게 앱 수명 동안 포트를 잡는다. 로그인 전 스캔은
    // 구독자가 없어 그냥 버려진다 — 화면이 구독 여부로 수신을 결정한다.
    private void WireScanner(IServiceProvider provider)
    {
        var portName = AppConfig.Current.ScannerPortName;
        if (portName.Length == 0) return;

        var service = provider.GetRequiredService<ScannerService>();
        service.IsEnabled = true;

        _scanner = new SerialScannerReader(portName, AppConfig.Current.ScannerReconnectMs,
                                           service.Publish, LogScanner);
        _scanner.ConnectionChanged += service.SetConnected;
        _scanner.Start();
    }

    // 무인 루프라 토스트로 못 알리는 실패가 대부분이고, Debug.WriteLine 은
    // Release 에서 사라진다 — 프린터/DB/스캐너 장애 사후 추적에는 파일이 필요하다.
    // 라벨 .zpl 과 같은 폴더에 남긴다: 현장에서 한 곳만 보면 된다.
    private static void LogDispatch(string msg) => AppendLog("dispatch", "LabelDispatcher", msg);
    private static void LogScanner(string msg)  => AppendLog("scanner",  "Scanner",         msg);

    private static void AppendLog(string file, string tag, string msg)
    {
        System.Diagnostics.Debug.WriteLine($"[{tag}] {msg}");
        try
        {
            var dir = AppConfig.Current.PrinterOutputDir;
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, $"{file}-{DateTime.Now:yyyyMMdd}.log"),
                               $"{DateTime.Now:HH:mm:ss} {msg}{Environment.NewLine}");
        }
        catch { /* 로깅 실패가 발행·수신을 막아서는 안 된다 */ }
    }

    protected override void Dispose(bool disposing)
    {
        // 진행 중인 콜백을 기다리지 않는다 — 종료가 최대 5초 늘어지는 것보다,
        // 미완 선점이 스테일 회수(60초)로 복구되는 편이 낫다.
        if (disposing)
        {
            _labelTimer?.Dispose();
            _scanner?.Dispose();
        }
        base.Dispose(disposing);
    }

    /// <summary>
    /// Action channel from Razor pages. Most navigation stays inside the
    /// WebView via NavigationManager — only 'shell-exit' bubbles up here
    /// to close the form (used by an explicit Exit button if we add one).
    /// </summary>
    protected virtual Task OnAction(string action)
    {
        if (action == "shell-exit")
            BeginInvoke(() => Close());
        return Task.CompletedTask;
    }
}
