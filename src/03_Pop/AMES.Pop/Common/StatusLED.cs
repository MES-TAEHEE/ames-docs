using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace AMES.Pop.Common;

/// <summary>
/// Solid-coloured circle used as an equipment state indicator on INJ-02 (and
/// other dashboards). The Control's own BackColor is fixed to the card colour
/// so the surrounding rectangle blends in — the lamp colour itself lives in
/// <see cref="LampColor"/> and is drawn as an antialiased ellipse.
///
/// (Was originally a Panel with BackColor=LampColor + FillEllipse over the
/// top, which rendered as a plain square when both colours matched.)
/// </summary>
internal sealed class StatusLED : Control
{
    private Color _lampColor = PopTheme.TextOk;

    public StatusLED()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint
               | ControlStyles.OptimizedDoubleBuffer
               | ControlStyles.ResizeRedraw
               | ControlStyles.UserPaint
               | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = PopTheme.BgCard;
    }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color LampColor
    {
        get => _lampColor;
        set { _lampColor = value; Invalidate(); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);

        // Inset by 2 so the antialiased edge doesn't get clipped on the right/bottom.
        var d   = Math.Min(Width, Height) - 4;
        var x   = (Width  - d) / 2;
        var y   = (Height - d) / 2;
        var rect = new Rectangle(x, y, d, d);

        // soft glow halo (15% larger, low opacity)
        using (var halo = new SolidBrush(Color.FromArgb(80, _lampColor)))
        {
            var grow = d / 6;
            g.FillEllipse(halo, rect.X - grow, rect.Y - grow, rect.Width + grow * 2, rect.Height + grow * 2);
        }

        using var fill = new SolidBrush(_lampColor);
        g.FillEllipse(fill, rect);
    }
}
