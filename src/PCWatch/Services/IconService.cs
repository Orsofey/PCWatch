using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PCWatch.Core;

namespace PCWatch.Services
{
    public sealed class IconService : IDisposable
    {
        private readonly BlockingCollection<IconRequest> _queue = new BlockingCollection<IconRequest>();
        private readonly ConcurrentDictionary<string, ImageSource> _cache = new ConcurrentDictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, bool> _queued = new ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private readonly Task _worker;

        public event Action<string, ImageSource> IconReady;

        public IconService()
        {
            _worker = Task.Factory.StartNew(Loop, TaskCreationOptions.LongRunning);
        }

        public static string KeyFor(string path, string processName)
        {
            return String.IsNullOrWhiteSpace(path) ? "name:" + KnownApplications.Normalize(processName) : "path:" + path;
        }

        public ImageSource GetCached(string path, string processName)
        {
            ImageSource image;
            return _cache.TryGetValue(KeyFor(path, processName), out image) ? image : null;
        }

        public void Request(string path, string processName)
        {
            string key = KeyFor(path, processName);
            if (key == "name:" || _cache.ContainsKey(key) || !_queued.TryAdd(key, true)) return;
            try { _queue.Add(new IconRequest { Key = key, Path = path, Name = processName }); }
            catch (InvalidOperationException) { }
        }

        private void Loop()
        {
            ImageSource fallback = ToImageSource(SystemIcons.Application);
            foreach (IconRequest request in _queue.GetConsumingEnumerable())
            {
                ImageSource image = fallback;
                string resolvedPath = null;
                try
                {
                    resolvedPath = !String.IsNullOrWhiteSpace(request.Path) && File.Exists(request.Path)
                        ? request.Path : KnownApplications.ResolveExecutablePath(request.Name);
                    if (!String.IsNullOrWhiteSpace(resolvedPath) && File.Exists(resolvedPath))
                    {
                        using (Icon icon = Icon.ExtractAssociatedIcon(resolvedPath)) image = ToImageSource(icon);
                    }
                }
                catch { }
                image = image ?? fallback;
                _cache[request.Key] = image;
                Action<string, ImageSource> handler = IconReady;
                if (handler != null) handler(request.Key, image);
            }
        }

        private static ImageSource ToImageSource(Icon icon)
        {
            if (icon == null) return null;
            BitmapSource source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(32, 32));
            source.Freeze();
            return source;
        }

        public void Dispose()
        {
            _queue.CompleteAdding();
            if (!_worker.Wait(1000)) _queue.Dispose();
        }

        private sealed class IconRequest
        {
            public string Key;
            public string Path;
            public string Name;
        }
    }
}
