using System.Text;
using AMES.Data.Connection;
using AMES.Data.Repositories;
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
        var store   = new DbInjAgentStore(new InjLotRepository(factory), new InjCondRepository(factory));

        var pollers = cfg.Machines.Select(m => new MachinePoller(
            m,
            new ModbusMachineClient(m.ModbusIp, m.ModbusPort),
            new FEnetClient(m.FenetIp, m.FenetPort),
            store, MainForm.EnqueueLog)).ToList();

        Application.Run(new MainForm(pollers, cfg.PollingMs));
    }
}
