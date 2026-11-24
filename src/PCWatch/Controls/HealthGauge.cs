using System;
using System.Windows;
using System.Windows.Media;

namespace PCWatch.Controls
{
    public sealed class HealthGauge : FrameworkElement
    {
        public static readonly DependencyProperty ValueProperty = DependencyProperty.Register("Value", typeof(double), typeof(HealthGauge), new FrameworkPropertyMetadata(100.0, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty BrushProperty = DependencyProperty.Register("Brush", typeof(Brush), typeof(HealthGauge), new FrameworkPropertyMetadata(Brushes.MediumSeaGreen, FrameworkPropertyMetadataOptions.AffectsRender));
        public double Value { get { return (double)GetValue(ValueProperty); } set { SetValue(ValueProperty, value); } }
        public Brush Brush { get { return (Brush)GetValue(BrushProperty); } set { SetValue(BrushProperty, value); } }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            double size = Math.Min(ActualWidth, ActualHeight);
            if (size < 20) return;
            double thickness = 9;
            Point center = new Point(ActualWidth / 2, ActualHeight / 2);
            double radius = (size - thickness - 4) / 2;
            Pen track = new Pen(new SolidColorBrush(Color.FromRgb(36, 49, 68)), thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            Pen valuePen = new Pen(Brush, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            dc.DrawGeometry(null, track, Arc(center, radius, -220, 40));
            double value = Math.Max(0, Math.Min(100, Value));
            if (value > 0) dc.DrawGeometry(null, valuePen, Arc(center, radius, -220, -220 + 260 * value / 100));
        }

        private static StreamGeometry Arc(Point center, double radius, double start, double end)
        {
            StreamGeometry geometry = new StreamGeometry();
            Point a = PointAt(center, radius, start), b = PointAt(center, radius, end);
            using (StreamGeometryContext context = geometry.Open())
            {
                context.BeginFigure(a, false, false);
                context.ArcTo(b, new Size(radius, radius), 0, Math.Abs(end - start) > 180, end > start ? SweepDirection.Clockwise : SweepDirection.Counterclockwise, true, false);
            }
            geometry.Freeze();
            return geometry;
        }

        private static Point PointAt(Point center, double radius, double angle)
        {
            double radians = angle * Math.PI / 180;
            return new Point(center.X + radius * Math.Cos(radians), center.Y + radius * Math.Sin(radians));
        }
    }
}
