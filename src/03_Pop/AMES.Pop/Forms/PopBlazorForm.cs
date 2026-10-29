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
    private System.Threading.Timer? _offlineTimer;
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
            AppConfig.Current.PrinterMaxFailures, LogDispatch, new LabelWatermarkStore(), new LabelPrintLedger()));
        services.AddSingleton<ScannerService>(_ => new ScannerService(LogScanner));
        services.AddSingleton<IdleWatcher>(_ => new IdleWatcher(
            TimeSpan.FromMinutes(AppConfig.Current.IdleLogoutMinutes),
            TimeSpan.FromSeconds(AppConfig.Current.IdleWarnSeconds)));

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
        WireOfflineSync(provider);

        BlazorHost.ActionRequested += OnAction;
        FormClosing += (_, _) => BlazorHost.ActionRequested -= OnAction;
        FormClosing += (_, _) => CloseSessionOnExit(provider);
    }

    // 창을 닫는 것도 로그아웃이다 — 그대로 두면 PR_PopSession 행이 만료 시각까지 열린 채 남는다.
    // AppRoot 의 로그아웃 처리는 던지고 잊는 작업이라 프로세스가 먼저 끝나므로 여기서 기다려 끝낸다(최대 3초,
    // 못 끝내면 저널에 올려 다음 실행의 복구가 닫는다). 라벨 워터마크도 로그아웃과 같이 남긴다.
    private void CloseSessionOnExit(IServiceProvider provider)
    {
        var state = provider.GetRequiredService<AppState>();
        var s = state.Session ?? state.PendingSession;
        if (s is null) return;
        _labelTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        const string reason = "APP_CLOSE";
        if (s.SessionId < 0 || PopDb.IsOffline)
        {
            OfflineAuth.JournalSessionClose(s.SessionId, reason, s.EmployeeNo);
            return;
        }
        var work = Task.Run(() =>
        {
            try { provider.GetRequiredService<LabelDispatcher>().Stop(); } catch { }
            PopServices.Sessions.CloseSession(s.SessionId, reason, s.OperatorId);
        });
        try
        {
            if (!work.Wait(TimeSpan.FromSeconds(3)))
                OfflineAuth.JournalSessionClose(s.SessionId, reason, s.EmployeeNo);
        }
        catch (AggregateException ex)
        {
            PopLog.Append("session", "PopSession", $"close session {s.SessionId} ({reason}) failed: {ex.InnerException?.Message}");
            OfflineAuth.JournalSessionClose(s.SessionId, reason, s.EmployeeNo);
        }
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
            // DB 가 끊긴 것으로 알려진 동안은 쉰다 — 매초 접속을 시도하며 로그만 쌓이고, 오프라인에서 뽑은 라벨은 화면이 직접 뽑는다.
            if (PopDb.IsOffline) return;
            try { dispatcher.Tick(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[LabelDispatcher] tick failed: {ex}"); }
        }, null, Timeout.Infinite, Timeout.Infinite);

        state.OnChange += () =>
        {
            // 모듈은 로그인 시 선택 라인에서 결정된다 — INJ 세션에서만 발행.
            if (state.Session is { } s && state.ModuleCode == "INJ")
            {
                dispatcher.Start(s.LineId, s.TerminalId, s.EmployeeNo);
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

    // 오프라인 로그인 사본 갱신·저널 재생은 화면 수명과 무관하다 — 로그인 화면에서도, 어느 작업 화면에서도 돈다.
    // 첫 틱을 바로 돌려, 처음 켠 터미널이 사본을 곧바로 받는다.
    private void WireOfflineSync(IServiceProvider provider)
    {
        // 연결 상태 검사 — 지난번 끊김이면 바로 오프라인으로 시작하고, 화면과 무관하게 검사 주기마다 확인한다
        PopDb.Start();
        var state  = provider.GetRequiredService<AppState>();
        var toasts = provider.GetRequiredService<ToastService>();
        OfflineWork.Notice += (text, isError) => { if (isError) toasts.Warn(text); else toasts.Ok(text); };
        _offlineTimer = new System.Threading.Timer(_ =>
        {
            try { OfflineAuth.Tick(state); }
            catch (Exception ex) { PopLog.Append("offline", "OfflineAuth", $"tick failed: {ex.Message}"); }
        }, null, 2000, 5000);
    }

    private static void LogDispatch(string msg) => PopLog.Append("dispatch", "LabelDispatcher", msg);
    private static void LogScanner(string msg)  => PopLog.Append("scanner",  "Scanner",         msg);

    protected override void Dispose(bool disposing)
    {
        // 진행 중인 콜백을 기다리지 않는다 — 종료가 최대 5초 늘어지는 것보다,
        // 미완 선점이 스테일 회수(60초)로 복구되는 편이 낫다.
        if (disposing)
        {
            _labelTimer?.Dispose();
            _offlineTimer?.Dispose();
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
