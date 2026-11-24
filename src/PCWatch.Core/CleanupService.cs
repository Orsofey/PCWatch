using System;
using System.Collections.Generic;
using System.Threading;

namespace PCWatch.Core
{
    public static class CleanupService
    {
        public static CleanupResult Clean(IList<CleanupItem> items, bool recycleDownloads, CancellationToken token, IProgress<CleanupProgress> progress)
        {
            CleanupResult result = new CleanupResult();
            if (items == null || items.Count == 0) return result;
            for (int index = 0; index < items.Count; index++)
            {
                token.ThrowIfCancellationRequested();
                CleanupItem item = items[index];
                if (progress != null) progress.Report(new CleanupProgress { Fraction = (double)index / items.Count, Message = "Очистка: " + item.Name });
                if (item.Kind == CleanupKind.RecycleBin)
                {
                    if (NativeCleanup.EmptyRecycleBin()) result.FreedBytes += item.Size;
                    else result.SkippedFiles += Math.Max(1, item.FileCount);
                    continue;
                }
                if (item.Kind != CleanupKind.Files) continue;
                foreach (string path in item.Paths)
                {
                    token.ThrowIfCancellationRequested();
                    if (item.IsDownload && recycleDownloads)
                    {
                        long recycled;
                        if (FileOps.RecycleFile(path, out recycled))
                        {
                            result.RecycledBytes += recycled;
                            result.DeletedFiles++;
                        }
                        else result.SkippedFiles++;
                        continue;
                    }
                    FileOps.DeleteResult deleted = FileOps.Delete(path, item.MinAge, token);
                    result.FreedBytes += deleted.Freed;
                    result.DeletedFiles += deleted.Files;
                    result.SkippedFiles += deleted.Failed;
                }
            }
            if (progress != null) progress.Report(new CleanupProgress { Fraction = 1.0, Message = "Готово" });
            return result;
        }
    }
}
