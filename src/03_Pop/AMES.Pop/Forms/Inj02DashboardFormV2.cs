using AMES.Contracts.Dto;
using AMES.Pop.Common;

namespace AMES.Pop.Forms;

/// <summary>
/// INJ-02 Dashboard, image-backed variant.
///
/// Instead of laying out every card / gauge / chart with WinForms controls,
/// the entire static design (cards, gradients, LED glow, dashed borders,
/// 'A-MES' logo, section headers, chart frame) is baked into a 1920×1080
/// PNG generated from the L3 mockup HTML at <c>tools/mockup_pngs/inj02.html</c>.
/// The form then overlays a handful of transparent labels at known
/// coordinates to fill in only the *dynamic* values (WO number, qty,
/// gauges, equipment state). Refresh ticks update those labels.
///
/// Coordinates below are in 1920×1080 design space and recalculated for the
/// actual client size each layout pass — so the form scales correctly on
/// any panel size.
/// </summary>
public sealed class Inj02DashboardFormV2 : PopForm
{
    private const int DesignW = 1920;
    private const int DesignH = 1080;

    private readonly PopSessionDto _session;
    private readonly System.Windows.Forms.Timer _refreshTimer;
    private readonly System.Windows.Forms.Timer _clockTimer;

    private readonly PictureBox _bg;

    // overlay labels — positioned in design coords by DoLayout()
    private readonly Label _lblTerminal, _lblOperator, _lblClock, _lblRunBadge;
    private readonly Label _lblWoHeader;
    private readonly Label _lblWoValue, _lblWoTarget, _lblWoPct;
    private readonly Label _lblDDay, _lblCycleTime;
    private readonly Label _lblEqState, _lblEqUptime;
    private readonly Label _lblDefectV, _lblMoldV, _lblOeeV;
    private readonly Label _lblMoldSub;

    // overlay rectangles for which the position is normalised
    private readonly List<(Control C, RectangleF Frac, ContentAlignment Align)> _slots = new();

    public Inj02DashboardFormV2(PopSessionDto session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        Text = "A-MES POP · INJ-02 Dashboard";

        // background image fills the whole client — Stretch so the design
        // scales to whatever panel size we land on.
        _bg = new PictureBox
        {
            Dock      = DockStyle.Fill,
            SizeMode  = PictureBoxSizeMode.StretchImage,
            BackColor = Color.Black,
            Image     = LoadBackgroundImage(),
        };
        Controls.Add(_bg);

        // create overlay labels (all parented to _bg so transparency works)
        _lblTerminal  = MakeOverlay($"INJ-02  ·  {_session.TerminalId}  ·  {_session.LineId}",
                                    "Consolas",  16f, FontStyle.Bold,
                                    Color.FromArgb(190, 220, 250),
                                    align: ContentAlignment.MiddleLeft);
        _lblOperator  = MakeOverlay($"{_session.EmployeeName} · {_session.ShiftCode}",
                                    "Segoe UI", 14f, FontStyle.Bold,
                                    Color.FromArgb(220, 230, 240),
                                    align: ContentAlignment.MiddleRight);
        _lblClock     = MakeOverlay(DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                                    "Consolas", 14f, FontStyle.Regular,
                                    Color.FromArgb(190, 210, 230),
                                    align: ContentAlignment.MiddleRight);
        _lblRunBadge  = MakeOverlay("● RUN", "Segoe UI", 14f, FontStyle.Bold,
                                    Color.FromArgb(93, 255, 196),
                                    align: ContentAlignment.MiddleLeft);

        _lblWoHeader  = MakeOverlay("▼ WO PROGRESS · (loading) ...",
                                    "Consolas", 14f, FontStyle.Bold,
                                    Color.FromArgb(147, 197, 253),
                                    align: ContentAlignment.MiddleLeft);

        _lblWoValue   = MakeOverlay("—",  "Bahnschrift Condensed", 92f, FontStyle.Bold,
                                    Color.FromArgb(93, 255, 196),
                                    align: ContentAlignment.BottomLeft);
        _lblWoTarget  = MakeOverlay("/  —  EA", "Bahnschrift Condensed", 48f, FontStyle.Bold,
                                    Color.FromArgb(170, 180, 190),
                                    align: ContentAlignment.BottomLeft);
        _lblWoPct     = MakeOverlay("0%", "Segoe UI", 32f, FontStyle.Bold,
                                    Color.FromArgb(147, 197, 253),
                                    align: ContentAlignment.MiddleRight);

        _lblDDay      = MakeOverlay("—", "Segoe UI", 18f, FontStyle.Bold,
                                    Color.White, align: ContentAlignment.MiddleLeft);
        _lblCycleTime = MakeOverlay("—", "Segoe UI", 18f, FontStyle.Bold,
                                    Color.White, align: ContentAlignment.MiddleLeft);

        _lblEqState   = MakeOverlay("—", "Bahnschrift Condensed", 56f, FontStyle.Bold,
                                    Color.FromArgb(93, 255, 196),
                                    align: ContentAlignment.MiddleCenter);
        _lblEqUptime  = MakeOverlay("Uptime —", "Consolas", 14f, FontStyle.Regular,
                                    Color.FromArgb(180, 190, 200),
                                    align: ContentAlignment.MiddleCenter);

        _lblDefectV   = MakeOverlay("0.0%", "Bahnschrift Condensed", 78f, FontStyle.Bold,
                                    Color.FromArgb(93, 255, 196),
                                    align: ContentAlignment.MiddleCenter);
        _lblMoldV     = MakeOverlay("—",     "Bahnschrift Condensed", 78f, FontStyle.Bold,
                                    Color.FromArgb(253, 224, 71),
                                    align: ContentAlignment.MiddleCenter);
        _lblOeeV      = MakeOverlay("—",     "Bahnschrift Condensed", 78f, FontStyle.Bold,
                                    Color.FromArgb(93, 255, 196),
                                    align: ContentAlignment.MiddleCenter);
        _lblMoldSub   = MakeOverlay("",      "Consolas", 12f, FontStyle.Regular,
                                    Color.FromArgb(150, 160, 170),
                                    align: ContentAlignment.MiddleCenter);

        // ── slot placements in design coords (x, y, w, h), pixel-measured
        //    from the rendered Backgrounds/inj02.png. Convert to fractions
        //    for resolution-independence.
        AddSlot(_lblTerminal,  170, 70, 700, 60, ContentAlignment.MiddleLeft);
        AddSlot(_lblOperator, 1370, 80, 410, 30, ContentAlignment.MiddleRight);
        AddSlot(_lblClock,    1660,110, 230, 30, ContentAlignment.MiddleRight);
        AddSlot(_lblRunBadge, 1390, 80,  90, 30, ContentAlignment.MiddleLeft);

        AddSlot(_lblWoHeader,   110,200, 900, 40, ContentAlignment.MiddleLeft);
        AddSlot(_lblWoValue,    110,260, 290,120, ContentAlignment.BottomLeft);
        AddSlot(_lblWoTarget,   400,290, 260, 90, ContentAlignment.BottomLeft);
        AddSlot(_lblWoPct,      900,300, 200, 70, ContentAlignment.MiddleRight);

        AddSlot(_lblDDay,       110,440, 300, 40, ContentAlignment.MiddleLeft);
        AddSlot(_lblCycleTime,  430,440, 300, 40, ContentAlignment.MiddleLeft);

        AddSlot(_lblEqState,   1180,400, 700, 80, ContentAlignment.MiddleCenter);
        AddSlot(_lblEqUptime,  1180,480, 700, 30, ContentAlignment.MiddleCenter);

        AddSlot(_lblDefectV,    100,610, 540,100, ContentAlignment.MiddleCenter);
        AddSlot(_lblMoldV,      700,610, 540,100, ContentAlignment.MiddleCenter);
        AddSlot(_lblOeeV,      1290,610, 540,100, ContentAlignment.MiddleCenter);
        AddSlot(_lblMoldSub,    700,720, 540, 30, ContentAlignment.MiddleCenter);

        // Bottom nav bar — minimal until we render mockups for those too.
        var nav = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom, Height = 110, ColumnCount = 7, RowCount = 1,
            BackColor = Color.FromArgb(8, 12, 22), Padding = new Padding(16, 12, 16, 12),
        };
        for (var i = 0; i < 7; i++) nav.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 7));
        nav.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        nav.Controls.Add(PopShell.BigButton("▶  INJ-03\nWO Confirm",  PopTheme.BgKey,    Color.White, (_, _) => Open<Inj03WoConfirmForm>(),         fontSize: 16f), 0, 0);
        nav.Controls.Add(PopShell.BigButton("▶  INJ-04\nProd Entry",  PopTheme.AccentDeep, Color.White, (_, _) => Open<Inj04ProductionEntryForm>(), fontSize: 16f), 1, 0);
        nav.Controls.Add(PopShell.BigButton("▶  INJ-05\nDefect",      Color.FromArgb(180, 70, 30), Color.White, (_, _) => Open<Inj05DefectForm>(),  fontSize: 16f), 2, 0);
        nav.Controls.Add(PopShell.BigButton("▶  INJ-06\nMold",        Color.FromArgb(170, 120, 20),Color.White, (_, _) => Open<Inj06MoldChangeForm>(),fontSize: 16f), 3, 0);
        nav.Controls.Add(PopShell.BigButton("▶  INJ-07\nStatus",      PopTheme.BgKey,    Color.White, (_, _) => Open<Inj07ProdStatusForm>(),       fontSize: 16f), 4, 0);
        nav.Controls.Add(PopShell.BigButton("🚨  INJ-08\nANDON",      Color.FromArgb(180, 30, 30), Color.White, (_, _) => Open<Inj08AndonForm>(),  fontSize: 16f), 5, 0);
        nav.Controls.Add(PopShell.BigButton("◀  LOGOUT",              Color.FromArgb(60, 60, 60),  Color.White, (_, _) => ConfirmLogout(),         fontSize: 16f), 6, 0);
        Controls.Add(nav);
        nav.BringToFront();

        // re-layout overlays on every resize
        _bg.Resize  += (_, _) => DoLayout();
        Shown       += (_, _) => DoLayout();
        Resize      += (_, _) => DoLayout();

        FormClosing += (_, _) => CloseSessionBestEffort();

        _clockTimer   = new System.Windows.Forms.Timer { Interval = 1_000 };
        _refreshTimer = new System.Windows.Forms.Timer { Interval = 5_000 };
        _clockTimer  .Tick += (_, _) => _lblClock.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        _refreshTimer.Tick += (_, _) => Refresh_();
        _clockTimer.Start();
        _refreshTimer.Start();
        Refresh_();
    }

    private void AddSlot(Control c, int x, int y, int w, int h, ContentAlignment align)
    {
        var frac = new RectangleF(x / (float)DesignW, y / (float)DesignH,
                                  w / (float)DesignW, h / (float)DesignH);
        _slots.Add((c, frac, align));
    }

    private void DoLayout()
    {
        var bw = _bg.ClientSize.Width;
        var bh = _bg.ClientSize.Height;
        if (bw <= 0 || bh <= 0) return;

        // PictureBox.StretchImage scales the image to the client rect; in our
        // 16:9 case + Maximized form the client is already ~16:9, so slot
        // fractions land on the correct pixel.
        foreach (var (c, frac, _) in _slots)
        {
            c.Location = new Point((int)(frac.X * bw), (int)(frac.Y * bh));
            c.Size     = new Size ((int)(frac.Width * bw), (int)(frac.Height * bh));
        }
    }

    private Label MakeOverlay(string text, string family, float size, FontStyle style,
                              Color fg, ContentAlignment align)
    {
        var lbl = new Label
        {
            Text      = text,
            Font      = new Font(family, size, style),
            ForeColor = fg,
            BackColor = Color.Transparent,
            AutoSize  = false,
            TextAlign = align,
            UseMnemonic = false,
        };
        // Add to the PictureBox so transparency draws against the image,
        // not against the form's solid back colour.
        _bg.Controls.Add(lbl);
        return lbl;
    }

    private static Image LoadBackgroundImage()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Backgrounds", "inj02.png");
        if (!File.Exists(path))
            throw new FileNotFoundException("Inj02 background PNG missing", path);
        // Use Image.FromFile via FileStream so the file isn't locked.
        using var fs = File.OpenRead(path);
        return Image.FromStream(fs);
    }

    // ── refresh ────────────────────────────────────────────────────────────
    private void Refresh_()
    {
        try
        {
            var wo = PopServices.WorkOrders.GetActiveForTerminal(_session.LineId, _session.TerminalId)
                  ?? PopServices.WorkOrders.ListForLine(_session.LineId).FirstOrDefault();

            if (wo is not null)
            {
                _lblWoHeader.Text = $"▼ WO PROGRESS · {wo.WoNumber} · {wo.ItemNameEn ?? wo.ItemName}";
                _lblWoValue.Text  = $"{wo.CompletedQty:0}";
                _lblWoTarget.Text = $"/ {wo.OrderQty:0} EA";
                _lblWoPct.Text    = $"{wo.ProgressPct:0}%";

                _lblDDay.Text     = wo.DueDate is null
                                  ? "D-Day  —"
                                  : $"D-Day  D-{wo.DaysToDue}  ({wo.DueDate:MM/dd})";
                _lblCycleTime.Text = $"Cycle  {(wo.RecipeId is null ? "—" : PopServices.Master.GetRecipeCycleTime(wo.RecipeId)?.ToString() ?? "—")} sec";

                if (!string.IsNullOrEmpty(wo.MoldId))
                {
                    var mold = PopServices.Molds.GetById(wo.MoldId);
                    if (mold is not null)
                    {
                        _lblMoldV.Text   = $"{mold.LifePct:0}%";
                        _lblMoldSub.Text = $"{mold.CurrentShots:N0} / {mold.RatedShots:N0}";
                        _lblMoldV.ForeColor = mold.Severity switch
                        {
                            "warn"  => Color.FromArgb(253, 224, 71),
                            "alert" => Color.FromArgb(252, 165, 165),
                            "hard"  => Color.FromArgb(252, 165, 165),
                            _       => Color.FromArgb(93, 255, 196),
                        };
                    }
                }

                var (good, defect) = PopServices.Defects.GetWoTotals(wo.WoId);
                var pct = (good + defect) == 0 ? 0.0 : defect * 100.0 / (good + defect);
                _lblDefectV.Text      = $"{pct:0.0}%";
                _lblDefectV.ForeColor = pct >= 5 ? Color.FromArgb(252, 165, 165)
                                       : pct >= 4 ? Color.FromArgb(253, 224, 71)
                                       : Color.FromArgb(93, 255, 196);
            }
            else
            {
                _lblWoHeader.Text = "▼ WO PROGRESS · (no active WO)";
                _lblWoValue.Text  = "—";
                _lblWoTarget.Text = "/  —  EA";
                _lblWoPct.Text    = "0%";
            }

            var eq = PopServices.Equipment.GetPrimaryForLine(_session.LineId);
            if (eq is not null)
            {
                _lblEqState.Text       = eq.Status?.ToUpperInvariant() ?? "OFFLINE";
                _lblEqState.ForeColor  = (eq.Status ?? "").ToUpperInvariant() switch
                {
                    "RUN"   => Color.FromArgb(93, 255, 196),
                    "IDLE"  => Color.FromArgb(168, 200, 240),
                    "STOP"  => Color.FromArgb(252, 165, 165),
                    "ALARM" => Color.FromArgb(252, 165, 165),
                    _       => Color.FromArgb(180, 190, 200),
                };
                _lblEqUptime.Text = $"Uptime {(eq.Uptime is null ? "—" : $"{(int)eq.Uptime.Value.TotalHours:00}:{eq.Uptime.Value.Minutes:00}:{eq.Uptime.Value.Seconds:00}")}";
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Inj02V2 refresh] {ex.Message}");
        }
    }

    // ── nav helpers ─────────────────────────────────────────────────────
    private void Open<TForm>() where TForm : Form
    {
        Form? f = null;
        try
        {
            var ctor = typeof(TForm).GetConstructor(new[] { typeof(PopSessionDto) });
            f = ctor != null ? (Form)ctor.Invoke(new object[] { _session }) : Activator.CreateInstance<TForm>();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Cannot open screen", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        if (f is null) return;
        f.ShowDialog(this);
        Refresh_();
    }

    private void ConfirmLogout()
    {
        var r = MessageBox.Show(this, "Log out and return to login?", "Logout",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
        if (r == DialogResult.Yes) Close();
    }

    private void CloseSessionBestEffort()
    {
        try { PopServices.Sessions.CloseSession(_session.SessionId, "ManualLogout", _session.OperatorId); }
        catch { }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _refreshTimer.Dispose(); _clockTimer.Dispose(); }
        base.Dispose(disposing);
    }
}
