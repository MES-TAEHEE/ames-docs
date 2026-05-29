namespace AMES.Pop.Common;

/// <summary>
/// Shared color + font palette for every POP screen.
///
/// Font sizes are tuned for the production target:
///   15" industrial touch panel · 1920×1080 · 5-metre readability.
/// This means body text in the 12-14pt range, big numbers 40-90pt.
/// </summary>
internal static class PopTheme
{
    // ── Palette (matches the VOL05 L3 dark mockup) ────────────────────────────
    public static readonly Color BgOuter    = Color.Black;
    public static readonly Color BgTopBar   = Color.FromArgb(12, 26, 46);
    public static readonly Color BgCard     = Color.FromArgb(10, 22, 40);    // #0A1628
    public static readonly Color BgBadge    = Color.FromArgb(15, 35, 65);
    public static readonly Color BgInfo     = Color.FromArgb(8, 18, 32);
    public static readonly Color BgKey      = Color.FromArgb(28, 38, 60);
    public static readonly Color BgKeyOk    = Color.FromArgb(13, 184, 122);  // #0DB87A
    public static readonly Color BgKeyDel   = Color.FromArgb(120, 30, 30);
    public static readonly Color BgKeyHover = Color.FromArgb(37, 99, 235);   // mod

    public static readonly Color Accent     = Color.FromArgb(147, 197, 253); // mod-tint
    public static readonly Color AccentSoft = Color.FromArgb(147, 197, 253, 200);
    public static readonly Color AccentDeep = Color.FromArgb(37, 99, 235);   // mod
    public static readonly Color Border     = Color.FromArgb(37, 99, 235, 100);
    public static readonly Color TextWhite  = Color.White;
    public static readonly Color TextDim    = Color.FromArgb(150, 184, 195);
    public static readonly Color TextOk     = Color.FromArgb(93, 255, 196);
    public static readonly Color TextFail   = Color.FromArgb(252, 165, 165);

    // ── Fonts ───────────────────────────────────────────────────────────────
    // Tuned for 1920×1080 industrial touch. Bumped ~1.4× over the dialog-sized
    // first draft so labels are legible from arm's length at the panel.
    public static readonly Font Logo        = new("Segoe UI", 28F, FontStyle.Bold);
    public static readonly Font TitleBig    = new("Segoe UI", 36F, FontStyle.Bold);
    public static readonly Font TitleMid    = new("Segoe UI", 18F, FontStyle.Bold);
    public static readonly Font Body        = new("Segoe UI", 12F);
    public static readonly Font BodySmall   = new("Segoe UI", 11F);
    public static readonly Font BodyBold    = new("Segoe UI", 13F, FontStyle.Bold);
    public static readonly Font Mono        = new("Consolas", 13F);
    public static readonly Font MonoBold    = new("Consolas", 13F, FontStyle.Bold);
    public static readonly Font KeyDigit    = new("Segoe UI", 28F, FontStyle.Bold);
    public static readonly Font InfoCaption = new("Consolas", 11F, FontStyle.Bold);
    public static readonly Font InfoValue   = new("Segoe UI", 16F, FontStyle.Bold);

    // Display-class fonts for the giant numbers in dashboards / entry screens.
    public static readonly Font DisplayBig   = new("Segoe UI", 48F, FontStyle.Bold);
    public static readonly Font DisplayHuge  = new("Segoe UI", 72F, FontStyle.Bold);
    public static readonly Font DisplayMega  = new("Segoe UI", 96F, FontStyle.Bold);
    public static readonly Font GaugeValue   = new("Segoe UI", 60F, FontStyle.Bold);
    public static readonly Font BigButton    = new("Segoe UI", 16F, FontStyle.Bold);
}
