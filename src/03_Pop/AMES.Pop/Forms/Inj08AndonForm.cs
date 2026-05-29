using AMES.Contracts.Auth;
using AMES.Contracts.Dto;
using AMES.Contracts.Enums;
using AMES.Pop.Common;

namespace AMES.Pop.Forms;

/// <summary>
/// INJ-08 Andon — manual emergency-stop trigger / acknowledge / resume.
/// Full-screen red. Operator can RAISE; only Supervisor PIN can ACK + RESUME.
/// Stack-light + siren are stubbed (DB row only); the real PLC bridge will
/// pick the PR_AndonCall row up via the SYS Andon Controller path.
/// </summary>
public sealed class Inj08AndonForm : PopForm
{
    private readonly PopSessionDto _session;
    private readonly Label _lblBigState, _lblCause, _lblElapsed;
    private readonly Button _btnRaise, _btnAck, _btnResume;
    private readonly TextBox _txtReason;
    private readonly System.Windows.Forms.Timer _elapsedTimer;
    private int _openAndonId;
    private DateTime _triggeredAt;

    public Inj08AndonForm(PopSessionDto session)
    {
        _session = session;
        Text      = "A-MES POP · INJ-08 ANDON";
        BackColor = Color.FromArgb(35, 6, 6);   // dark red overrides the default black

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, BackColor = BackColor,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));    // banner
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 35));     // big state text
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 260));   // cause + elapsed
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));    // reason / supervisor action
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 130));   // nav

        // banner
        var banner = new Label
        {
            Text = "🚨  ANDON  ·  EMERGENCY  ·  LINE " + session.LineId,
            Font = new Font("Segoe UI", 26f, FontStyle.Bold),
            ForeColor = Color.White, BackColor = Color.FromArgb(190, 30, 30),
            Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
        };
        root.Controls.Add(banner, 0, 0);

        _lblBigState = new Label
        {
            Text = "READY", Font = new Font("Segoe UI", 140f, FontStyle.Bold),
            ForeColor = Color.White, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Transparent,
        };
        root.Controls.Add(_lblBigState, 0, 1);

        var causeCard = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(60, 12, 12), Margin = new Padding(40, 6, 40, 6), Padding = new Padding(24) };
        var causeStack = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, BackColor = Color.Transparent };
        causeStack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
        causeStack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        causeStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        causeStack.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        causeStack.Controls.Add(new Label
        {
            Text = "▼ CAUSE", Font = PopTheme.MonoBold, ForeColor = Color.White, AutoSize = true,
            Margin = new Padding(0, 0, 0, 6),
        }, 0, 0);
        causeStack.Controls.Add(new Label
        {
            Text = "▼ ELAPSED", Font = PopTheme.MonoBold, ForeColor = Color.White, AutoSize = true,
            Margin = new Padding(0, 0, 0, 6), Anchor = AnchorStyles.Right,
        }, 1, 0);
        _lblCause = new Label
        {
            Text = "(no andon open)", Font = new Font("Segoe UI", 24f, FontStyle.Bold),
            ForeColor = Color.White, AutoSize = false, Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
        };
        causeStack.Controls.Add(_lblCause, 0, 1);
        _lblElapsed = new Label
        {
            Text = "00:00", Font = new Font("Segoe UI", 60f, FontStyle.Bold),
            ForeColor = Color.White, AutoSize = false, Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight,
        };
        causeStack.Controls.Add(_lblElapsed, 1, 1);
        causeCard.Controls.Add(causeStack);
        root.Controls.Add(causeCard, 0, 2);

        // Supervisor reason area
        var supCard = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(50, 10, 10), Margin = new Padding(40, 6, 40, 6), Padding = new Padding(24) };
        var supStack = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Color.Transparent };
        supStack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        supStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        supStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        supStack.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        supStack.Controls.Add(new Label
        {
            Text = "▼ SUPERVISOR ACK NOTES  (Ack & Resume require supervisor PIN entered at the line)",
            Font = PopTheme.MonoBold, ForeColor = Color.White, AutoSize = true,
        }, 0, 0);
        supStack.Controls.Add(new Label
        {
            Text = "Reason code  ·  free-text corrective action  ·  goes to PR_AndonCall + audit log",
            Font = PopTheme.BodySmall, ForeColor = Color.FromArgb(220, 200, 200), AutoSize = true,
            Margin = new Padding(0, 4, 0, 6),
        }, 0, 1);
        _txtReason = new TextBox
        {
            Multiline = true, Dock = DockStyle.Fill,
            BackColor = Color.Black, ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Consolas", 11f),
        };
        supStack.Controls.Add(_txtReason, 0, 2);
        supCard.Controls.Add(supStack);
        root.Controls.Add(supCard, 0, 3);

        // Nav row
        var nav = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, BackColor = Color.FromArgb(190, 30, 30),
            Padding = new Padding(16, 10, 16, 10),
        };
        for (var i = 0; i < 4; i++) nav.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        nav.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        nav.Controls.Add(PopShell.BigButton("← Back",                   PopTheme.BgKey, Color.White, (_, _) => Close()),  0, 0);
        _btnRaise  = PopShell.BigButton("🚨 RAISE ANDON",                 Color.FromArgb(120, 10, 10), Color.White, (_, _) => Raise());
        _btnAck    = PopShell.BigButton("🔓 ACK (supervisor PIN)",        Color.FromArgb(170, 120, 20), Color.White, (_, _) => Acknowledge());
        _btnResume = PopShell.BigButton("▶ RESUME PRODUCTION",            PopTheme.BgKeyOk, Color.White, (_, _) => Resume());
        _btnAck.Enabled = false; _btnResume.Enabled = false;
        nav.Controls.Add(_btnRaise,  1, 0);
        nav.Controls.Add(_btnAck,    2, 0);
        nav.Controls.Add(_btnResume, 3, 0);
        root.Controls.Add(nav, 0, 4);

        Controls.Add(root);

        _elapsedTimer = new System.Windows.Forms.Timer { Interval = 1_000 };
        _elapsedTimer.Tick += (_, _) =>
        {
            if (_openAndonId == 0) { _lblElapsed.Text = "00:00"; return; }
            var t = DateTime.Now - _triggeredAt;
            _lblElapsed.Text = $"{(int)t.TotalMinutes:00}:{t.Seconds:00}";
        };
        _elapsedTimer.Start();
    }

    private void Raise()
    {
        if (_openAndonId != 0) return;
        var equipId = PopServices.Equipment.GetPrimaryForLine(_session.LineId)?.EquipId;
        try
        {
            _openAndonId = PopServices.Andon.Raise(
                _session.LineId, equipId,
                triggerSource: "MANUAL", ruleId: "INJ-08-MANUAL", severity: "HIGH",
                _session.EmployeeNo);
            _triggeredAt = DateTime.Now;

            // Halt the equipment
            if (!string.IsNullOrEmpty(equipId))
                PopServices.Equipment.LogStatus(equipId, _session.LineId, "STOP", "ANDON", null, _session.EmployeeNo);

            _lblBigState.Text = "STOPPED";
            _lblBigState.ForeColor = Color.White;
            _lblCause.Text = $"Manual andon by {_session.EmployeeName}  ·  AndonID #{_openAndonId}";
            _btnRaise.Enabled = false; _btnAck.Enabled = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Raise failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void Acknowledge()
    {
        if (_openAndonId == 0) return;
        var (supId, supName) = PromptSupervisorPin();
        if (supId is null) return;
        try
        {
            var reason = string.IsNullOrWhiteSpace(_txtReason.Text)
                ? "Acknowledged at line"
                : _txtReason.Text.Trim();
            PopServices.Andon.Acknowledge(_openAndonId, supId,
                reasonCode: "LINE-CHK", correctiveAction: reason);
            _lblBigState.Text = "ACKED";
            _lblCause.Text   += $"   ·   ack by {supName}";
            _btnAck.Enabled = false; _btnResume.Enabled = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Ack failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void Resume()
    {
        if (_openAndonId == 0) return;
        try
        {
            PopServices.Andon.Resume(_openAndonId);
            var equipId = PopServices.Equipment.GetPrimaryForLine(_session.LineId)?.EquipId;
            if (!string.IsNullOrEmpty(equipId))
                PopServices.Equipment.LogStatus(equipId, _session.LineId, "RUN", "RESUMED", null, _session.EmployeeNo);
            _lblBigState.Text = "READY";
            _openAndonId = 0;
            _btnRaise.Enabled = true; _btnResume.Enabled = false;
            MessageBox.Show(this, "Production resumed.", "Resume", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Resume failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// Crude PIN modal — accepts S001 supervisor for now. Real impl will reuse
    /// PopAuthService.Login() in a "verify only, no session" mode.
    /// </summary>
    private (string? UserId, string Name) PromptSupervisorPin()
    {
        using var dlg = new Form
        {
            Text = "Supervisor PIN", Size = new Size(360, 200),
            FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent,
            BackColor = PopTheme.BgCard, ForeColor = PopTheme.TextWhite,
            MaximizeBox = false, MinimizeBox = false,
        };
        var lbl = new Label { Text = "Enter supervisor PIN (default S001 / 9999):", AutoSize = true, Location = new Point(20, 20) };
        var tx  = new TextBox { Location = new Point(20, 50), Size = new Size(300, 28), PasswordChar = '●', Font = new Font("Segoe UI", 16f, FontStyle.Bold) };
        var ok  = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(170, 100), Width = 70 };
        var cn  = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(250, 100), Width = 70 };
        dlg.Controls.AddRange(new Control[] { lbl, tx, ok, cn });
        dlg.AcceptButton = ok; dlg.CancelButton = cn;
        if (dlg.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(tx.Text))
            return (null, "");

        var outcome = PopServices.PopAuth.Login(new LoginRequest
        {
            AttemptedId = "S001",
            Pin         = tx.Text.Trim(),
            Method      = AuthMethod.Pin,
            TerminalId  = _session.TerminalId,
            LineId      = _session.LineId,
            ShiftCode   = _session.ShiftCode,
        });
        if (!outcome.IsSuccess)
        {
            MessageBox.Show(this, "Bad supervisor PIN.", "Auth", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return (null, "");
        }
        // close that throwaway session immediately
        try { PopServices.Sessions.CloseSession(outcome.Session!.SessionId, "AndonAck"); } catch { }
        return (outcome.Session!.OperatorId, outcome.Session!.EmployeeName);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _elapsedTimer.Dispose();
        base.Dispose(disposing);
    }
}
