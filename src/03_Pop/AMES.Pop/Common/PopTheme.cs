namespace AMES.Pop.Common;

/// <summary>
/// Shared color + font palette for every POP screen (INJ-01 … INJ-08, IMG-*, PNT-*, QC-*).
/// Centralised so a future "high-contrast outdoor" theme is a one-line swap.
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
    public static readonly Color AccentSoft = Color.FromArgb(147, 197, 253, 160);
    public static readonly Color AccentDeep = Color.FromArgb(37, 99, 235);   // mod
    public static readonly Color Border     = Color.FromArgb(37, 99, 235, 100);
    public static readonly Color TextWhite  = Color.White;
    public static readonly Color TextDim    = Color.FromArgb(120, 154, 165);
    public static readonly Color TextOk     = Color.FromArgb(93, 255, 196);
    public static readonly Color TextFail   = Color.FromArgb(252, 165, 165);

    // ── Fonts (kept centralised so DPI overrides land in one place) ──────────
    public static readonly Font Logo      = new("Segoe UI",   20F, FontStyle.Bold);
    public static readonly Font TitleBig  = new("Segoe UI",   22F, FontStyle.Bold);
    public static readonly Font TitleMid  = new("Segoe UI",   13F, FontStyle.Bold);
    public static readonly Font Body      = new("Segoe UI",   10F);
    public static readonly Font BodySmall = new("Segoe UI",    9F);
    public static readonly Font BodyBold  = new("Segoe UI",   10F, FontStyle.Bold);
    public static readonly Font Mono      = new("Consolas",   10F);
    public static readonly Font MonoBold  = new("Consolas",   10F, FontStyle.Bold);
    public static readonly Font KeyDigit  = new("Segoe UI",   18F, FontStyle.Bold);
    public static readonly Font InfoCaption = new("Consolas",  8F, FontStyle.Bold);
    public static readonly Font InfoValue   = new("Segoe UI", 12F, FontStyle.Bold);
}
