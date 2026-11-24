using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PCWatch.Controls
{
    public partial class MetricCard : UserControl
    {
        public static readonly DependencyProperty TitleProperty = DependencyProperty.Register("Title", typeof(string), typeof(MetricCard), new PropertyMetadata(""));
        public static readonly DependencyProperty ValueProperty = DependencyProperty.Register("Value", typeof(string), typeof(MetricCard), new PropertyMetadata("—"));
        public static readonly DependencyProperty SubtitleProperty = DependencyProperty.Register("Subtitle", typeof(string), typeof(MetricCard), new PropertyMetadata(""));
        public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register("Glyph", typeof(string), typeof(MetricCard), new PropertyMetadata(""));
        public static readonly DependencyProperty AccentProperty = DependencyProperty.Register("Accent", typeof(Brush), typeof(MetricCard), new PropertyMetadata(Brushes.CornflowerBlue));
        public static readonly DependencyProperty AreaFillProperty = DependencyProperty.Register("AreaFill", typeof(Brush), typeof(MetricCard), new PropertyMetadata(null));
        public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register("Values", typeof(IList<double>), typeof(MetricCard), new PropertyMetadata(null));
        public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register("Maximum", typeof(double), typeof(MetricCard), new PropertyMetadata(100.0));

        public MetricCard() { InitializeComponent(); }
        public string Title { get { return (string)GetValue(TitleProperty); } set { SetValue(TitleProperty, value); } }
        public string Value { get { return (string)GetValue(ValueProperty); } set { SetValue(ValueProperty, value); } }
        public string Subtitle { get { return (string)GetValue(SubtitleProperty); } set { SetValue(SubtitleProperty, value); } }
        public string Glyph { get { return (string)GetValue(GlyphProperty); } set { SetValue(GlyphProperty, value); } }
        public Brush Accent { get { return (Brush)GetValue(AccentProperty); } set { SetValue(AccentProperty, value); } }
        public Brush AreaFill { get { return (Brush)GetValue(AreaFillProperty); } set { SetValue(AreaFillProperty, value); } }
        public IList<double> Values { get { return (IList<double>)GetValue(ValuesProperty); } set { SetValue(ValuesProperty, value); } }
        public double Maximum { get { return (double)GetValue(MaximumProperty); } set { SetValue(MaximumProperty, value); } }
    }
}
