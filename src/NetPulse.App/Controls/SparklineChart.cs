using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace NetPulse.App.Controls;

public class SparklineChart : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty =
        DependencyProperty.Register(nameof(Values), typeof(IEnumerable<double>), typeof(SparklineChart),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LineBrushProperty =
        DependencyProperty.Register(nameof(LineBrush), typeof(Brush), typeof(SparklineChart),
            new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromRgb(59, 130, 246)), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ShowFillProperty =
        DependencyProperty.Register(nameof(ShowFill), typeof(bool), typeof(SparklineChart),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public IEnumerable<double>? Values
    {
        get => (IEnumerable<double>?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public Brush LineBrush
    {
        get => (Brush)GetValue(LineBrushProperty);
        set => SetValue(LineBrushProperty, value);
    }

    public bool ShowFill
    {
        get => (bool)GetValue(ShowFillProperty);
        set => SetValue(ShowFillProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        double w = ActualWidth;
        double h = ActualHeight;

        if (w <= 0 || h <= 0) return;

        // Draw background
        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(30, 15, 23, 42)), null, new Rect(0, 0, w, h));

        var list = Values?.ToList();
        if (list == null || list.Count < 2)
        {
            // Draw baseline placeholder line
            var grayPen = new Pen(new SolidColorBrush(Color.FromArgb(40, 148, 163, 184)), 1);
            dc.DrawLine(grayPen, new Point(0, h * 0.5), new Point(w, h * 0.5));
            return;
        }

        double minVal = list.Min();
        double maxVal = list.Max();
        if (Math.Abs(maxVal - minVal) < 0.001)
        {
            maxVal += 1.0;
            minVal = Math.Max(0, minVal - 1.0);
        }

        // Add 10% vertical padding
        double range = maxVal - minVal;
        double paddedMin = Math.Max(0, minVal - (range * 0.1));
        double paddedMax = maxVal + (range * 0.1);
        double effectiveRange = paddedMax - paddedMin;

        var points = new Point[list.Count];
        for (int i = 0; i < list.Count; i++)
        {
            double x = (double)i / (list.Count - 1) * w;
            double norm = (list[i] - paddedMin) / effectiveRange;
            double y = h - (norm * h);
            points[i] = new Point(x, Math.Clamp(y, 1, h - 1));
        }

        var streamGeom = new StreamGeometry();
        using (var ctx = streamGeom.Open())
        {
            ctx.BeginFigure(points[0], false, false);
            for (int i = 1; i < points.Length; i++)
            {
                ctx.LineTo(points[i], true, false);
            }
        }
        streamGeom.Freeze();

        // Optional Area Fill
        if (ShowFill && LineBrush is SolidColorBrush scb)
        {
            var fillGeom = new StreamGeometry();
            using (var ctx = fillGeom.Open())
            {
                ctx.BeginFigure(new Point(points[0].X, h), true, true);
                for (int i = 0; i < points.Length; i++)
                {
                    ctx.LineTo(points[i], true, false);
                }
                ctx.LineTo(new Point(points[^1].X, h), true, false);
            }
            fillGeom.Freeze();

            var grad = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(0, 1),
                GradientStops = new GradientStopCollection
                {
                    new GradientStop(Color.FromArgb(60, scb.Color.R, scb.Color.G, scb.Color.B), 0.0),
                    new GradientStop(Color.FromArgb(5, scb.Color.R, scb.Color.G, scb.Color.B), 1.0)
                }
            };
            dc.DrawGeometry(grad, null, fillGeom);
        }

        // Draw Stroke Line
        var strokePen = new Pen(LineBrush, 1.8) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        dc.DrawGeometry(null, strokePen, streamGeom);

        // Highlight latest point
        var lastPoint = points[^1];
        dc.DrawEllipse(LineBrush, new Pen(new SolidColorBrush(Color.FromRgb(15, 23, 42)), 1.5), lastPoint, 3.5, 3.5);
    }
}
