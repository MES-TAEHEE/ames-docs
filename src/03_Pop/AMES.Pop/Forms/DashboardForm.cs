using AMES.Contracts.Dto;
using AMES.Data.Connection;
using AMES.Data.Repositories;
using AMES.Pop.Common;

namespace AMES.Pop.Forms;

/// <summary>
/// Placeholder for INJ-02 Dashboard.
/// Tomorrow this gets replaced by the real production overview screen.
/// For now it just confirms the session is live and offers a Logout button
/// so we can iterate on the login flow without a half-finished dashboard
/// blocking testing.
/// </summary>
public sealed class DashboardForm : Form
{
    private readonly PopSessionDto _session;

    public DashboardForm(PopSessionDto session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));

        Text            = "A-MES POP · Dashboard (placeholder)";
        ClientSize      = new Size(720, 480);
        BackColor       = Color.Black;
        ForeColor       = Color.White;
        Font            = new Font("Segoe UI", 10F);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        StartPosition   = FormStartPosition.CenterScreen;
        MaximizeBox     = false;

        var card = new Panel {
            Location  = new Point(40, 40),
            Size      = new Size(640, 400),
            BackColor = Color.FromArgb(10, 22, 40),
        };
        card.Paint += (_, e) => {
            using var pen = new Pen(Color.FromArgb(37, 99, 235, 100));
            e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
        };

        var title = new Label {
            Text      = "Logged In",
            Font      = new Font("Segoe UI", 22F, FontStyle.Bold),
            ForeColor = Color.FromArgb(93, 255, 196),
            AutoSize  = true,
            Location  = new Point(28, 24),
        };
        var subtitle = new Label {
            Text      = "INJ-02 Dashboard will replace this view.",
            Font      = new Font("Segoe UI", 9F),
            ForeColor = Color.FromArgb(120, 154, 165),
            AutoSize  = true,
            Location  = new Point(30, 70),
        };

        var info = new Label {
            Font      = new Font("Consolas", 11F),
            ForeColor = Color.FromArgb(147, 197, 253),
            AutoSize  = false,
            Size      = new Size(580, 200),
            Location  = new Point(28, 110),
            TextAlign = ContentAlignment.TopLeft,
            Text =
                $"Session #{session.SessionId}\r\n" +
                $"Operator    : {session.EmployeeName}  ({session.EmployeeNo})\r\n" +
                $"Terminal    : {session.TerminalId}\r\n" +
                $"Line / Shift: {session.LineId}  /  {session.ShiftCode}\r\n" +
                $"Auth method : {session.AuthMethod}\r\n" +
                $"Started at  : {session.StartedAt:yyyy-MM-dd HH:mm:ss}\r\n" +
                $"Expires at  : {session.ExpiresAt:yyyy-MM-dd HH:mm:ss}",
        };

        var btnLogout = new Button {
            Text      = "▶  LOGOUT",
            Font      = new Font("Segoe UI", 11F, FontStyle.Bold),
            BackColor = Color.FromArgb(234, 88, 12),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Size      = new Size(580, 44),
            Location  = new Point(28, 332),
            Cursor    = Cursors.Hand,
        };
        btnLogout.FlatAppearance.BorderSize = 0;
        btnLogout.Click += (_, _) => Logout();

        card.Controls.AddRange([title, subtitle, info, btnLogout]);
        Controls.Add(card);
    }

    private void Logout()
    {
        try
        {
            var factory  = new AmesConnectionFactory(AppConfig.Current.ConnectionString);
            var sessions = new PopSessionRepository(factory);
            sessions.CloseSession(_session.SessionId, "ManualLogout");
        }
        catch
        {
            // Best-effort: even if the DB is offline we still want to return
            // to the login screen so the operator isn't trapped.
        }
        Close();
    }
}
