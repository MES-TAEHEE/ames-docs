using System.Text;
using AMES.Data.Connection;
using AMES.Data.Repositories;
using AMES.Data.Services;
using AMES.InjAgent.Core;
using AMES.InjAgent.Plc;

namespace AMES.InjAgent;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        // 중복 실행 방지 — 이미 떠 있으면 알림 후 종료. using 으로 프로세스 수명 동안 소유 유지.
        using var single = new Mutex(initiallyOwned: true,
            "AMES.InjAgent.9F3C2A11-SingleInstance", out bool isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show("AMES.InjAgent is already running.", "Duplicate instance",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // FEnet 문자열 쓰기(CP949) 지원
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        ApplicationConfiguration.Initialize();

        AgentConfig cfg;
        try { cfg = AgentConfig.Current; }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "AMES.InjAgent configuration error",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var factory = new AmesConnectionFactory(cfg.ConnectionString);

        // LOT 번호 날짜·생성 시각은 DB 서버 기준(DbClock)이다. 설정하지 않으면 PC 시계를 그대로 쓴다.
        DbClock.Configure(factory);
        DbClock.Sync();
        MainForm.EnqueueLog(DbClock.IsSynced
            ? $"DB clock offset {DbClock.Offset.TotalSeconds:0.0}s (PC 시각 보정)"
            : "DB clock not synced — PC clock in use (DB 연결 후 자동 보정)");
        QueuingInjAgentStore store;
        try
        {
            // DB 가 끊기면 샷을 디스크 큐에 쌓고, 복구되면 Sync 가 DB 에 다시 넣는다(POP 과 번호 구간은 따로 받는다)
            store = new QueuingInjAgentStore(
                new DbInjAgentStore(new InjLotRepository(factory), new InjCondRepository(factory)),
                new OfflineStateFile(OfflineStateFile.DefaultPath),
                DbHealth.IsConnectionFailure,
                MainForm.EnqueueLog);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"오프라인 상태 파일을 읽지 못했습니다. 파일을 옮기거나 지운 뒤 다시 실행하세요.\n{ex.Message}",
                            "AMES.InjAgent", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var pollers = cfg.Machines.Select(m => new MachinePoller(
            m,
            new ModbusMachineClient(m.ModbusIp, m.ModbusPort),
            new FEnetClient(m.FenetIp, m.FenetPort),
            store, MainForm.EnqueueLog)).ToList();

        // 5초마다: 밀린 샷을 DB 에 넣고 번호 구간을 채운다. 폴러와 같은 잠금을 쓰므로 여기서 DB 가 멈춰도 폴러는 큐로 간다.
        var lineIds = cfg.Machines.Select(m => m.LineId).ToList();
        var syncThread = new Thread(() =>
        {
            while (true)
            {
                // DB 시각 보정은 10분마다. 장애 중에는 연결을 시도하지 않는다(이전 보정값을 쓴다).
                if (!DbHealth.IsDown) DbClock.EnsureSynced(TimeSpan.FromMinutes(10));
                try { store.Sync(lineIds); }
                catch (Exception ex) { MainForm.EnqueueLog($"offline sync failed: {ex.Message}"); }
                Thread.Sleep(5000);
            }
        }) { IsBackground = true, Name = "InjAgent-OfflineSync" };
        syncThread.Start();

        // 창이 뜬 뒤 한 번만 — 어느 DB 에 붙었는지 에이전트 로그 창에 남긴다(WinForms 라 콘솔은 보이지 않는다)
        EventHandler? onIdle = null;
        onIdle = (_, _) =>
        {
            Application.Idle -= onIdle;
            _ = Task.Run(() => MainForm.EnqueueLog($"DB: {factory.Describe()}"));
        };
        Application.Idle += onIdle;

        Application.Run(new MainForm(pollers, cfg.PollingMs));
    }
}
