using AMES.Data.Connection;
using AMES.Data.Repositories;
using AMES.Pop.Common;

namespace AMES.Pop.Forms;

/// <summary>
/// Placeholder main form. Tomorrow this will become INJ-01 POP Login.
/// For now it just verifies the WinForms shell can connect to AMES_DEV.
/// </summary>
public sealed class MainForm : Form
{
    private readonly Button _btnTestDb;
    private readonly Label  _lblStatus;
    private readonly Label  _lblInfo;

    public MainForm()
    {
        Text          = "A-MES POP · Skeleton";
        ClientSize    = new Size(560, 360);
        BackColor     = Color.FromArgb(15, 31, 37);   // matches POP demo dark theme
        ForeColor     = Color.FromArgb(205, 221, 226);
        Font          = new Font("Segoe UI", 10F);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        StartPosition  = FormStartPosition.CenterScreen;
        MaximizeBox    = false;

        var title = new Label {
            Text      = "A-MES POP",
            Font      = new Font("Segoe UI", 24F, FontStyle.Bold),
            ForeColor = Color.FromArgb(251, 146, 60),
            AutoSize  = true,
            Location  = new Point(40, 28),
        };
        var subtitle = new Label {
            Text      = "Solution skeleton — WinForms shell verification",
            Font      = new Font("Segoe UI", 9F),
            ForeColor = Color.FromArgb(120, 154, 165),
            AutoSize  = true,
            Location  = new Point(42, 70),
        };

        _lblInfo = new Label {
            Text      = $"Station: {AppConfig.Current.StationId}    " +
                        $"Line: {AppConfig.Current.LineId}    " +
                        $"Shift: {AppConfig.Current.DefaultShift}",
            Font      = new Font("Consolas", 9F),
            ForeColor = Color.FromArgb(103, 232, 249),
            AutoSize  = true,
            Location  = new Point(42, 110),
        };

        _btnTestDb = new Button {
            Text      = "▶  TEST DB CONNECTION",
            Font      = new Font("Segoe UI", 11F, FontStyle.Bold),
            BackColor = Color.FromArgb(234, 88, 12),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Size      = new Size(480, 56),
            Location  = new Point(40, 160),
            Cursor    = Cursors.Hand,
        };
        _btnTestDb.FlatAppearance.BorderSize = 0;
        _btnTestDb.Click += OnTestDbClick;

        _lblStatus = new Label {
            Text      = "Click the button to verify connection to AMES_DEV.",
            Font      = new Font("Consolas", 10F),
            ForeColor = Color.FromArgb(120, 154, 165),
            AutoSize  = false,
            Size      = new Size(480, 100),
            Location  = new Point(40, 235),
            TextAlign = ContentAlignment.TopLeft,
        };

        Controls.AddRange([title, subtitle, _lblInfo, _btnTestDb, _lblStatus]);
    }

    private void OnTestDbClick(object? sender, EventArgs e)
    {
        _btnTestDb.Enabled = false;
        _lblStatus.Text = "Connecting...";
        Application.DoEvents();

        try
        {
            var factory = new AmesConnectionFactory(AppConfig.Current.ConnectionString);
            var repo    = new ItemRepository(factory);

            var sw    = System.Diagnostics.Stopwatch.StartNew();
            var count = repo.CountActive();
            sw.Stop();

            _lblStatus.ForeColor = Color.FromArgb(34, 197, 94);
            _lblStatus.Text =
                $"✓ Connection OK ({sw.ElapsedMilliseconds} ms)\r\n" +
                $"  MD_Item.ActiveFlag=1 → {count} rows\r\n\r\n" +
                $"Ready for tomorrow: build INJ-01 POP Login.";
        }
        catch (Exception ex)
        {
            _lblStatus.ForeColor = Color.FromArgb(239, 68, 68);
            _lblStatus.Text = $"✗ {ex.GetType().Name}\r\n  {ex.Message}";
        }
        finally
        {
            _btnTestDb.Enabled = true;
        }
    }
}
