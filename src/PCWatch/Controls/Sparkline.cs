using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace PCWatch.Controls
{
    public sealed class Sparkline : FrameworkElement
    {
        public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register("Values", typeof(IList<double>), typeof(Sparkline), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register("Stroke", typeof(Brush), typeof(Sparkline), new FrameworkPropertyMetadata(Brushes.CornflowerBlue, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty FillProperty = DependencyProperty.Register("Fill", typeof(Brush), typeof(Sparkline), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register("Maximum", typeof(double), typeof(Sparkline), new FrameworkPropertyMetadata(100.0, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty ShowGridProperty = DependencyProperty.Register("ShowGrid", typeof(bool), typeof(Sparkline), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

        public IList<double> Values { get { return (IList<double>)GetValue(ValuesProperty); } set { SetValue(ValuesProperty, value); } }
        public Brush Stroke { get { return (Brush)GetValue(StrokeProperty); } set { SetValue(StrokeProperty, value); } }
        public Brush Fill { get { return (Brush)GetValue(FillProperty); } set { SetValue(FillProperty, value); } }
        public double Maximum { get { return (double)GetValue(MaximumProperty); } set { SetValue(MaximumProperty, value); } }
        public bool ShowGrid { get { return (bool)GetValue(ShowGridProperty); } set { SetValue(ShowGridProperty, value); } }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            double w = ActualWidth, h = ActualHeight;
            if (w < 10 || h < 10) return;
            if (ShowGrid)
            {
                Pen grid = new Pen(new SolidColorBrush(Color.FromArgb(30, 150, 170, 205)), 1);
                grid.Freeze();
                for (int i = 1; i < 4; i++) dc.DrawLine(grid, new Point(0, h * i / 4), new Point(w, h * i / 4));
            }
            IList<double> values = Values;
            if (values == null || values.Count < 2) return;
            double max = Maximum;
            if (max <= 0)
            {
                max = 1;
                foreach (double value in values) max = Math.Max(max, value);
                max *= 1.15;
            }
            int count = values.Count;
            Point[] points = new Point[count];
            for (int i = 0; i < count; i++)
            {
                double value = Double.IsNaN(values[i]) || Double.IsInfinity(values[i]) ? 0 : Math.Max(0, Math.Min(max, values[i]));
                points[i] = new Point(i * w / (count - 1), h - 3 - value / max * (h - 8));
            }
            StreamGeometry line = new StreamGeometry();
            using (StreamGeometryContext context = line.Open())
            {
                context.BeginFigure(points[0], false, false);
                context.PolyLineTo(points, true, true);
            }
            line.Freeze();
            if (Fill != null)
            {
                StreamGeometry area = new StreamGeometry();
                using (StreamGeometryContext context = area.Open())
                {
                    context.BeginFigure(new Point(points[0].X, h), true, true);
                    context.PolyLineTo(points, true, true);
                    context.LineTo(new Point(points[count - 1].X, h), true, true);
                }
                area.Freeze();
                dc.DrawGeometry(Fill, null, area);
            }
            Pen pen = new Pen(Stroke, 2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            dc.DrawGeometry(null, pen, line);
            dc.DrawEllipse(Stroke, null, points[count - 1], 3, 3);
        }
    }
}
