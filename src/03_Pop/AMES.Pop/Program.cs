using AMES.Pop.Forms;

namespace AMES.Pop;

/// <summary>
/// A-MES POP (shop-floor terminal) entry point.
/// </summary>
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
