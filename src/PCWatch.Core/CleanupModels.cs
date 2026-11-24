using System;
using System.Collections.Generic;

namespace PCWatch.Core
{
    public enum CleanupSafety
    {
        Safe,
        Review,
        Manual
    }

    public enum CleanupKind
    {
        Files,
        RecycleBin,
        WindowsOld,
        Hibernation
    }

    public sealed class CleanupItem
    {
        public string Name { get; set; }
        public IList<string> Paths { get; set; }
        public long Size { get; set; }
        public int FileCount { get; set; }
        public TimeSpan? MinAge { get; set; }
        public string Detail { get; set; }
        public bool IsDownload { get; set; }
        public CleanupKind Kind { get; set; }
        public bool SelectedByDefault { get; set; }

        public CleanupItem()
        {
            Name = "";
            Paths = new List<string>();
            Detail = "";
        }
    }

    public sealed class CleanupCategory
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Note { get; set; }
        public CleanupSafety Safety { get; set; }
        public bool Blocked { get; set; }
        public IList<CleanupItem> Items { get; set; }

        public CleanupCategory()
        {
            Id = "";
            Title = "";
            Note = "";
            Items = new List<CleanupItem>();
        }

        public long Size
        {
            get
            {
                long total = 0;
                foreach (CleanupItem item in Items) total += item.Size;
                return total;
            }
        }
    }

    public sealed class CleanupProgress
    {
        public double Fraction { get; set; }
        public string Message { get; set; }
        public CleanupProgress() { Message = ""; }
    }

    public sealed class CleanupResult
    {
        public long FreedBytes { get; set; }
        public long RecycledBytes { get; set; }
        public int DeletedFiles { get; set; }
        public int SkippedFiles { get; set; }
    }
}
