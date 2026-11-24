using System.Windows.Media;
using PCWatch.Core;

namespace PCWatch.ViewModels
{
    public sealed class RecommendationItemViewModel : ObservableObject
    {
        private static readonly Brush SafeBrush = new SolidColorBrush(Color.FromRgb(77, 219, 160));
        private static readonly Brush ManualBrush = new SolidColorBrush(Color.FromRgb(255, 185, 104));
        public RecommendationItemViewModel(RecommendationItem item, ProcessItemViewModel process)
        {
            Item = item;
            ProcessRow = process;
        }
        public RecommendationItem Item { get; private set; }
        public ProcessItemViewModel ProcessRow { get; private set; }
        public int ProcessId { get { return Item.Process.Id; } }
        public string Title { get { return ProcessRow == null ? Item.Process.Name : ProcessRow.Title; } }
        public string Name { get { return Item.Process.Name; } }
        public string Category { get { return Item.Category; } }
        public string Reason { get { return Item.Reason; } }
        public string Impact { get { return Item.Impact; } }
        public int Score { get { return Item.Score; } }
        public bool IsSafeCandidate { get { return Item.IsSafeCandidate; } }
        public ImageSource Icon { get { return ProcessRow == null ? null : ProcessRow.Icon; } }
        public string MemoryText { get { return RussianFormat.Number(Item.Process.MemoryMB) + " МБ"; } }
        public Brush CategoryBrush { get { return IsSafeCandidate ? SafeBrush : ManualBrush; } }
        public void RefreshIcon() { Raise("Icon"); }
    }
}
