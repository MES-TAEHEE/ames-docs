using System.ComponentModel;
using System.Drawing.Drawing2D;
using AMES.Contracts.Dto;

namespace AMES.Pop.Common;

/// <summary>
/// Tiny bar chart used by INJ-02 dashboard + INJ-07 supervisor screen.
/// Renders 24 hourly buckets — green stack for Good, red stack for Defect.
/// </summary>
internal sealed class HourlyBarChart : Control
{
    private IReadOnlyList<HourlyOutputDto> _data = Array.Empty<HourlyOutputDto>();

    public HourlyBarChart()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint
               | ControlStyles.OptimizedDoubleBuffer
               | ControlStyles.ResizeRedraw
               | ControlStyles.UserPaint, true);
        BackColor = PopTheme.BgCard;
    }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public void SetData(IReadOnlyList<HourlyOutputDto> data)
    {
        _data = data ?? Array.Empty<HourlyOutputDto>();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.Clear(BackColor);
        if (_data.Count == 0) return;

        const int top = 6, bottom = 22, left = 6, right = 6;
        var plotH = Math.Max(20, Height - top - bottom);
        var plotW = Math.Max(40, Width  - left - right);
        var n = _data.Count;
        var slot = plotW / (float)n;
        var barW = Math.Max(2, slot * 0.7f);

        // Find the max for scaling.
        var maxVal = 1;
        foreach (var h in _data)
            if (h.GoodQty + h.DefectQty > maxVal) maxVal = h.GoodQty + h.DefectQty;

        var curHour = DateTime.Now.Hour;
        using var brGood   = new SolidBrush(Color.FromArgb(34, 197, 94));
        using var brDefect = new SolidBrush(Color.FromArgb(220, 38, 38));
        using var brFuture = new SolidBrush(Color.FromArgb(28, 38, 60));
        using var penFuture = new Pen(Color.FromArgb(80, 100, 130)) { DashStyle = DashStyle.Dash };
        using var fLabel  = new Font("Consolas", 7.5f);
        using var brLabel = new SolidBrush(PopTheme.AccentSoft);
        using var brNow   = new SolidBrush(PopTheme.TextWhite);

        var sf = new StringFormat { Alignment = StringAlignment.Center };

        for (var i = 0; i < n; i++)
        {
            var h = _data[i];
            var xCenter = left + slot * (i + 0.5f);
            var xLeft   = xCenter - barW / 2f;

            if (h.Hour > curHour)
            {
                // future hour — outline only
                g.DrawRectangle(penFuture, xLeft, top, barW, plotH);
            }
            else
            {
                var goodH   = plotH * h.GoodQty   / (float)maxVal;
                var defectH = plotH * h.DefectQty / (float)maxVal;
                // defect first (top), then good
                if (defectH > 0)
                    g.FillRectangle(brDefect, xLeft, top + plotH - goodH - defectH, barW, defectH);
                if (goodH > 0)
                    g.FillRectangle(brGood,   xLeft, top + plotH - goodH,           barW, goodH);
            }

            var label = h.Hour == curHour ? $"{h.Hour:00}*" : $"{h.Hour:00}";
            g.DrawString(label, fLabel, h.Hour == curHour ? brNow : brLabel,
                         new RectangleF(left + slot * i, Height - bottom + 4, slot, bottom - 4), sf);
        }
    }
}
