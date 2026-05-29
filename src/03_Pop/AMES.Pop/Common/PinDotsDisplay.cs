using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace AMES.Pop.Common;

/// <summary>
/// Visual indicator for the in-progress PIN — N hollow dots that fill in
/// as digits are added. Drawn with GDI+ circles so we do not depend on
/// font glyph coverage / baselines (which broke the first attempt).
/// </summary>
internal sealed class PinDotsDisplay : Control
{
    private int _length = 4;
    private int _filled;

    public PinDotsDisplay()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint
               | ControlStyles.OptimizedDoubleBuffer
               | ControlStyles.ResizeRedraw
               | ControlStyles.UserPaint, true);
        BackColor = Color.Black;
        ForeColor = PopTheme.Accent;
    }

    /// <summary>Total dot count (digits expected in the PIN).</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    [Browsable(false)]
    public int Length
    {
        get => _length;
        set { _length = Math.Max(1, value); Invalidate(); }
    }

    /// <summary>How many dots are filled in (0..Length).</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    [Browsable(false)]
    public int Filled
    {
        get => _filled;
        set { _filled = Math.Clamp(value, 0, _length); Invalidate(); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);

        // Border
        using (var pen = new Pen(PopTheme.Border))
            g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);

        // Dot geometry — diameter = 60% of height, spaced evenly.
        var diameter = (int)(Height * 0.45);
        var gap      = diameter / 2;
        var totalW   = _length * diameter + (_length - 1) * gap;
        var startX   = (Width - totalW) / 2;
        var y        = (Height - diameter) / 2;

        using var fill = new SolidBrush(ForeColor);
        using var ring = new Pen(ForeColor, 2f);

        for (var i = 0; i < _length; i++)
        {
            var rect = new Rectangle(startX + i * (diameter + gap), y, diameter, diameter);
            if (i < _filled) g.FillEllipse(fill, rect);
            else             g.DrawEllipse(ring, rect);
        }
    }
}
