using AMES.Contracts.Dto;
using AMES.Pop.Common;

namespace AMES.Pop.Forms;

/// <summary>
/// Dev convenience: a modal popup showing every active SYS_UserProfile as a
/// big tap-button. Picking a row sets <see cref="SelectedEmployeeNo"/> /
/// <see cref="SelectedEmployeeName"/> and closes the dialog with DialogResult
/// .OK. LoginForm then uses that EmployeeNo as the AttemptedId so PIN-only
/// login works without typing a badge.
///
/// This is *not* an auth bypass — the picker only chooses an attempted id;
/// the PIN still has to match in PopAuthService.
/// </summary>
public sealed class UserPickerForm : Form
{
    public string? SelectedEmployeeNo   { get; private set; }
    public string? SelectedEmployeeName { get; private set; }

    public UserPickerForm()
    {
        Text            = "Select User";
        ClientSize      = new Size(640, 700);
        BackColor       = PopTheme.BgOuter;
        ForeColor       = PopTheme.TextWhite;
        Font            = PopTheme.Body;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition   = FormStartPosition.CenterParent;
        MaximizeBox     = false;
        MinimizeBox     = false;
        ShowInTaskbar   = false;
        KeyPreview      = true;
        AutoScaleMode   = AutoScaleMode.Dpi;
        DoubleBuffered  = true;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3,
            BackColor = PopTheme.BgOuter,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));   // header
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // user list
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));   // footer

        // header
        var header = new Panel { Dock = DockStyle.Fill, BackColor = PopTheme.BgTopBar };
        header.Paint += (_, e) =>
        {
            using var pen = new Pen(PopTheme.Border);
            e.Graphics.DrawLine(pen, 0, header.Height - 1, header.Width, header.Height - 1);
        };
        header.Controls.Add(new Label
        {
            Text = "👤  SELECT USER", Font = PopTheme.TitleMid, ForeColor = PopTheme.Accent,
            AutoSize = true, Location = new Point(24, 18),
        });
        root.Controls.Add(header, 0, 0);

        // user list — vertically scrolling stack of buttons
        var listPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, AutoScroll = true,
            BackColor = PopTheme.BgCard, Padding = new Padding(16, 12, 16, 12),
        };
        foreach (var profile in PopServices.Auth.ListAllProfiles())
            listPanel.Controls.Add(BuildUserButton(profile));
        root.Controls.Add(listPanel, 0, 1);

        // footer (Cancel)
        var footer = new Panel { Dock = DockStyle.Fill, BackColor = PopTheme.BgCard };
        var btnCancel = PopShell.BigButton("✕  Cancel",
            Color.FromArgb(60, 60, 60), Color.White,
            (_, _) => { DialogResult = DialogResult.Cancel; Close(); },
            fontSize: 16f);
        btnCancel.Dock   = DockStyle.None;
        btnCancel.Size   = new Size(220, 60);
        btnCancel.Anchor = AnchorStyles.None;
        btnCancel.Location = new Point((footer.Width - btnCancel.Width) / 2, 10);
        footer.Resize += (_, _) => btnCancel.Location = new Point((footer.Width - btnCancel.Width) / 2, 10);
        footer.Controls.Add(btnCancel);
        root.Controls.Add(footer, 0, 2);

        Controls.Add(root);

        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); } };
    }

    private Button BuildUserButton(EmployeeProfileDto profile)
    {
        var lines = string.IsNullOrWhiteSpace(profile.AssignedLinesJson)
            ? "(all lines)"
            : profile.AssignedLinesJson;
        // Compact representation: ["LINE-INJ-01","LINE-INJ-02"] → INJ-01, INJ-02
        var summary = lines
            .Replace("\"", "")
            .Replace("[",  "")
            .Replace("]",  "")
            .Replace("LINE-", "");

        var btn = new Button
        {
            Width  = 560,
            Height = 80,
            Margin = new Padding(0, 0, 0, 10),
            BackColor = PopTheme.BgKey,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font      = new Font("Segoe UI", 13f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            Cursor    = Cursors.Hand,
            TabStop   = false,
            Text      = $"   {profile.EmployeeNo,-6}  {profile.EmployeeName,-22}\n" +
                        $"   {summary,-30}  ·  shift {profile.DefaultShift ?? "—"}",
            Tag       = profile,
        };
        btn.FlatAppearance.BorderColor        = PopTheme.Border;
        btn.FlatAppearance.BorderSize         = 1;
        btn.FlatAppearance.MouseOverBackColor = ControlPaint.Light(PopTheme.BgKey, 0.2f);
        btn.Click += (_, _) =>
        {
            SelectedEmployeeNo   = profile.EmployeeNo;
            SelectedEmployeeName = profile.EmployeeName;
            DialogResult         = DialogResult.OK;
            Close();
        };
        return btn;
    }
}
