using System;
using System.Windows.Media;
using PCWatch.Core;

namespace PCWatch.ViewModels
{
    public sealed class ProcessItemViewModel : ObservableObject
    {
        private ImageSource _icon;
        public ProcessSample Sample { get; private set; }
        public ProcessItemViewModel(ProcessSample sample) { Sample = sample; }
        public int Id { get { return Sample.Id; } }
        public string Name { get { return Sample.Name; } }
        public string Title { get { return String.IsNullOrWhiteSpace(Sample.Title) ? Sample.Name : Sample.Title; } }
        public string Path { get { return Sample.Path; } }
        public string CpuText { get { return RussianFormat.Percent(Sample.CpuUsage); } }
        public string MemoryText { get { return RussianFormat.Number(Sample.MemoryMB) + " МБ"; } }
        public string DiskText { get { return RussianFormat.BytesPerSec(Sample.DiskBytesPerSec); } }
        public string GpuText { get { return RussianFormat.Percent(Sample.GpuUsage); } }
        public string FaultsText { get { return RussianFormat.Rate(Sample.PageFaultsPerSec); } }
        public string RespondingText { get { return !Sample.Responding.HasValue ? "Фоновый" : Sample.Responding.Value ? "Отвечает" : "Не отвечает"; } }
        public Brush RespondingBrush { get { return Sample.Responding.HasValue && !Sample.Responding.Value ? Brushes.IndianRed : Brushes.SlateGray; } }
        public string Details { get { return "PID " + Id + (String.IsNullOrWhiteSpace(Path) ? "" : " · " + Path); } }
        public string IconKey { get { return String.IsNullOrWhiteSpace(Path) ? "name:" + KnownApplications.Normalize(Name) : "path:" + Path; } }
        public string ResolvedPath { get { return String.IsNullOrWhiteSpace(Path) ? KnownApplications.ResolveExecutablePath(Name) : Path; } }
        public int WindowCount { get { return Sample.Windows == null ? 0 : Sample.Windows.Count; } }
        public string WindowText { get { return Sample.IsForeground ? "Активное" : WindowCount > 0 ? "Окон: " + WindowCount : "Нет окон"; } }
        public ImageSource Icon { get { return _icon; } set { Set(ref _icon, value, "Icon"); } }

        public void Update(ProcessSample sample)
        {
            bool changed = Math.Abs(Sample.CpuUsage - sample.CpuUsage) >= 0.05
                || Math.Abs(Sample.MemoryMB - sample.MemoryMB) >= 0.5
                || Math.Abs(Sample.DiskBytesPerSec - sample.DiskBytesPerSec) >= 4096
                || Math.Abs(Sample.PageFaultsPerSec - sample.PageFaultsPerSec) >= 0.5
                || Math.Abs(Sample.GpuUsage - sample.GpuUsage) >= 0.1
                || Sample.StartTime != sample.StartTime
                || Sample.ThreadCount != sample.ThreadCount || Sample.HandleCount != sample.HandleCount
                || Sample.Responding != sample.Responding || Sample.IsForeground != sample.IsForeground
                || WindowsChanged(Sample.Windows, sample.Windows)
                || !String.Equals(Sample.Path, sample.Path, StringComparison.OrdinalIgnoreCase)
                || !String.Equals(Sample.Title, sample.Title, StringComparison.Ordinal);
            Sample = sample;
            if (changed) Raise("");
        }

        private static bool WindowsChanged(System.Collections.Generic.IList<WindowDescriptor> oldWindows, System.Collections.Generic.IList<WindowDescriptor> newWindows)
        {
            if (oldWindows.Count != newWindows.Count) return true;
            for (int i = 0; i < oldWindows.Count; i++)
                if (oldWindows[i].Handle != newWindows[i].Handle || oldWindows[i].IsMinimized != newWindows[i].IsMinimized || !String.Equals(oldWindows[i].Title, newWindows[i].Title, StringComparison.Ordinal)) return true;
            return false;
        }

        public bool Matches(string text)
        {
            if (String.IsNullOrWhiteSpace(text)) return true;
            return (Title != null && Title.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
                || (Name != null && Name.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
                || Id.ToString().Contains(text);
        }
    }
}
