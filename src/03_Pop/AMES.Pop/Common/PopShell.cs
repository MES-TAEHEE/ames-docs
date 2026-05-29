using AMES.Contracts.Dto;

namespace AMES.Pop.Common;

/// <summary>
/// Reusable top-bar control shared by every post-login POP screen.
/// Renders A-MES logo · screen id · operator name · shift · clock.
/// </summary>
internal static class PopShell
{
    public static Panel BuildTopBar(string screenCode, PopSessionDto session)
    {
        var bar = new Panel
        {
            Dock      = DockStyle.Top,
            Height    = 80,
            BackColor = PopTheme.BgTopBar,
        };
        bar.Paint += (_, e) =>
        {
            using var pen = new Pen(PopTheme.Border);
            e.Graphics.DrawLine(pen, 0, bar.Height - 1, bar.Width, bar.Height - 1);
        };

        var grid = new TableLayoutPanel
        {
            Dock        = DockStyle.Fill,
            ColumnCount = 3,
            RowCount    = 1,
            Padding     = new Padding(28, 10, 28, 10),
            BackColor   = Color.Transparent,
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.RowStyles   .Add(new RowStyle   (SizeType.Percent, 100));

        var logo = new Label
        {
            Text      = "A-MES",
            Font      = PopTheme.Logo,
            ForeColor = PopTheme.Accent,
            AutoSize  = true,
            Anchor    = AnchorStyles.Left,
            Margin    = new Padding(0, 6, 28, 0),
        };
        var info = new Label
        {
            Text      = $"{screenCode}  ·  {session.TerminalId}  ·  {session.LineId}",
            Font      = PopTheme.MonoBold,
            ForeColor = PopTheme.AccentSoft,
            AutoSize  = true,
            Anchor    = AnchorStyles.Left,
            Margin    = new Padding(0, 22, 0, 0),
        };

        var right = new TableLayoutPanel
        {
            ColumnCount = 1, RowCount = 2,
            AutoSize    = true,
            BackColor   = Color.Transparent,
            Anchor      = AnchorStyles.Right,
            Margin      = new Padding(0),
        };
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        right.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        right.Controls.Add(new Label
        {
            Text      = $"{session.EmployeeName}  ·  {session.ShiftCode}",
            Font      = PopTheme.BodyBold,
            ForeColor = PopTheme.TextWhite,
            AutoSize  = true,
            Anchor    = AnchorStyles.Right,
            Margin    = new Padding(0),
        }, 0, 0);

        var clock = new Label
        {
            Name      = "lblClock",
            Text      = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
            Font      = PopTheme.Mono,
            ForeColor = PopTheme.TextDim,
            AutoSize  = true,
            Anchor    = AnchorStyles.Right,
            Margin    = new Padding(0, 2, 0, 0),
        };
        right.Controls.Add(clock, 0, 1);

        grid.Controls.Add(logo,  0, 0);
        grid.Controls.Add(info,  1, 0);
        grid.Controls.Add(right, 2, 0);
        bar.Controls.Add(grid);
        return bar;
    }

    /// <summary>Big rectangular POP button — used everywhere outside the login keypad.</summary>
    public static Button BigButton(string text, Color bg, Color fg, EventHandler onClick,
                                   int height = 84, float fontSize = 16f)
    {
        var b = new Button
        {
            Text      = text,
            Font      = new Font("Segoe UI", fontSize, FontStyle.Bold),
            ForeColor = fg,
            BackColor = bg,
            FlatStyle = FlatStyle.Flat,
            Dock      = DockStyle.Fill,
            Margin    = new Padding(6),
            Cursor    = Cursors.Hand,
            Height    = height,
            TabStop   = false,
        };
        b.FlatAppearance.BorderColor        = PopTheme.Border;
        b.FlatAppearance.BorderSize         = 1;
        b.FlatAppearance.MouseOverBackColor = ControlPaint.Light(bg, 0.15f);
        b.Click += onClick;
        return b;
    }

    /// <summary>Section header inside a card (e.g. "▼ WO PROGRESS").</summary>
    public static Label SectionHeader(string text) => new()
    {
        Text      = text,
        Font      = PopTheme.MonoBold,
        ForeColor = PopTheme.Accent,
        AutoSize  = true,
        Margin    = new Padding(4, 0, 0, 6),
    };
}
