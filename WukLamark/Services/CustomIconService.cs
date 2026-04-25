using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace WukLamark.Services
{
    public sealed class CustomIconInfo
    {
        public string FileName { get; init; } = string.Empty;
        public IDalamudTextureWrap Texture { get; init; } = null!;
    }

    public sealed class CustomIconService : IDisposable
    {
        private FileSystemWatcher? fileWatcher;
        private System.Timers.Timer? reloadDebounceTimer;
        private volatile bool reloadRequested = true;
        private sealed class PendingIconLoad
        {
            public string FilePath { get; init; } = string.Empty;
            public int Retries { get; set; }
            public DateTime FirstAttemptUtc { get; init; } = DateTime.UtcNow;
            public DateTime NextAttemptUtc { get; set; } = DateTime.UtcNow;
        }
        private readonly Dictionary<string, PendingIconLoad> pendingIconLoads = [];
        private IReadOnlyList<CustomIconInfo> iconsToDispose = [];
        private const int MaxRetries = 20;
        private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(100);
        private static readonly TimeSpan MaxPendingAge = TimeSpan.FromSeconds(5);

        public IReadOnlyList<CustomIconInfo> AvailableIcons { get; private set; } = [];
        private const string CustomIconDirectoryName = "CustomIcons";

        public bool IsLoaded { get; private set; }
        public string CustomIconDirectory { get; }
        public CustomIconService(string pluginDirectory)
        {
            CustomIconDirectory = Path.Combine(pluginDirectory, CustomIconDirectoryName);
            CreateCustomIconDirectoryIfNotExists();
            InitializeFileWatcher();

            // Load on main thread
            Plugin.Framework.Update += OnFrameworkUpdate;
            reloadRequested = true;
        }
        private void OnFrameworkUpdate(IFramework _)
        {
            if (reloadRequested)
            {
                reloadRequested = false;
                LoadCustomIconsOnFrameworkThread();
            }

            ProcessPendingLoads();

            // Dispose old icons after processing pending loads
            if (iconsToDispose.Count > 0)
            {
                foreach (var icon in iconsToDispose)
                    icon.Texture.Dispose();
                iconsToDispose = [];
            }
        }
        private void CreateCustomIconDirectoryIfNotExists()
        {
            try
            {
                Directory.CreateDirectory(CustomIconDirectory);
            }
            catch (IOException)
            {
                Plugin.Log.Debug($"Directory '{CustomIconDirectory}' already exists. Skipping creation.");
            }
        }
        private void InitializeFileWatcher()
        {
            fileWatcher = new FileSystemWatcher(CustomIconDirectory, "*.png")
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.Size
            };

            reloadDebounceTimer = new System.Timers.Timer(500) { AutoReset = false };
            reloadDebounceTimer.Elapsed += (_, __) => reloadRequested = true;

            void scheduleReload(object? _, FileSystemEventArgs __) => reloadDebounceTimer.Start();
            fileWatcher.Created += scheduleReload;
            fileWatcher.Changed += scheduleReload;
            fileWatcher.Deleted += scheduleReload;
            fileWatcher.Renamed += scheduleReload;
            fileWatcher.EnableRaisingEvents = true;
        }
        private void LoadCustomIconsOnFrameworkThread()
        {
            CreateCustomIconDirectoryIfNotExists();

            var files = Directory.EnumerateFiles(CustomIconDirectory, "*.png", SearchOption.TopDirectoryOnly);
            var newIcons = new List<CustomIconInfo>();
            foreach (var file in files)
            {
                try
                {
                    var texture = GetCustomIconOrDefault(file);
                    if (texture != null)
                    {
                        newIcons.Add(new CustomIconInfo
                        {
                            FileName = Path.GetFileName(file),
                            Texture = texture
                        });
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.Error($"Failed to load custom icon '{file}': {ex.Message}");
                }
            }

            // If pending, don't clear existing icons yet
            if (newIcons.Count == 0 && pendingIconLoads.Count > 0 && AvailableIcons.Count > 0)
                return;

            var oldIcons = AvailableIcons;
            AvailableIcons = newIcons;
            IsLoaded = true;

            iconsToDispose = oldIcons;
            Plugin.Log.Info($"Loaded {AvailableIcons.Count} custom icons from '{CustomIconDirectory}'.");
        }
        private void ProcessPendingLoads()
        {
            if (pendingIconLoads.Count == 0) return;

            var now = DateTime.UtcNow;
            var toRemove = new List<string>();

            foreach (var kv in pendingIconLoads)
            {
                var pending = kv.Value;
                if (now < pending.NextAttemptUtc) continue;

                if (pending.Retries >= MaxRetries || (now - pending.FirstAttemptUtc) > MaxPendingAge)
                {
                    Plugin.Log.Warning($"Failed to load custom icon '{pending.FilePath}' after {pending.Retries} retries.");
                    toRemove.Add(kv.Key);
                    continue;
                }

                var request = Plugin.TextureProvider.GetFromFile(pending.FilePath);
                var wrap = request.GetWrapOrDefault();
                pending.Retries++;
                pending.NextAttemptUtc = now + RetryDelay;

                if (wrap == null) continue;

                AvailableIcons = [.. AvailableIcons.Where(i => !i.FileName.Equals(Path.GetFileName(pending.FilePath), StringComparison.OrdinalIgnoreCase)), new CustomIconInfo
                {
                    FileName = Path.GetFileName(pending.FilePath),
                    Texture = wrap
                }];

                toRemove.Add(kv.Key);
                Plugin.Log.Debug($"Successfully loaded custom icon '{pending.FilePath}' after {pending.Retries} retries.");
            }

            foreach (var key in toRemove)
                pendingIconLoads.Remove(key);
        }
        private IDalamudTextureWrap? GetCustomIconOrDefault(string? fileName)
        {
            if (fileName.IsNullOrWhitespace()) return null;

            // Accept either a full path or just the file name
            var candidatePath = Path.Combine(CustomIconDirectory, fileName);

            var fullPath = Path.GetFullPath(candidatePath);
            var fullCustomDir = Path.GetFullPath(CustomIconDirectory);

            // Ensure the path is within the custom icon directory
            if (!fullPath.StartsWith(fullCustomDir, StringComparison.OrdinalIgnoreCase)) return null;

            if (!File.Exists(fullPath) || !fullPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) return null;

            Plugin.Log.Verbose($"Attempting to load custom icon from '{fullPath}'.");
            var wrapTex = Plugin.TextureProvider.GetFromFile(fullPath);
            var wrap = wrapTex.GetWrapOrDefault();

            // Sometimes image loading takes a bit to complete if the size is big
            // Send to a pending load queue if it fails to load initially.
            if (wrap == null)
            {
                if (!pendingIconLoads.ContainsKey(fullPath))
                {
                    pendingIconLoads[fullPath] = new PendingIconLoad
                    {
                        FilePath = fullPath,
                        Retries = 1,
                        FirstAttemptUtc = DateTime.UtcNow,
                        NextAttemptUtc = DateTime.UtcNow + RetryDelay
                    };
                }
                return null;
            }
            return wrap;
        }
        public bool TryGetCustomIcon(string? fileName, out IDalamudTextureWrap? texture)
        {
            texture = GetCustomIconOrDefault(fileName);
            return texture != null;
        }
        public void ReloadCustomIcons()
        {
            IsLoaded = false;
            reloadRequested = true;

        }
        public void Dispose()
        {
            Plugin.Framework.Update -= OnFrameworkUpdate;
            fileWatcher?.Dispose();
            reloadDebounceTimer?.Dispose();

            foreach (var icon in AvailableIcons)
                icon.Texture.Dispose();

            AvailableIcons = [];
            GC.SuppressFinalize(this);
        }
    }
}
