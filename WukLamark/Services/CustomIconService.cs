using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using System;
using System.Collections.Generic;
using System.IO;

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

        public IReadOnlyList<CustomIconInfo> AvailableIcons { get; private set; } = [];
        private const string CustomIconDirectoryName = "CustomIcons";
        private const float MaxIconSize = 64f;

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
            if (!reloadRequested) return;
            reloadRequested = false;
            LoadCustomIconsOnFrameworkThread();
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
            fileWatcher.Renamed += (_, _) => { reloadDebounceTimer?.Stop(); reloadDebounceTimer?.Start(); };
            fileWatcher.EnableRaisingEvents = true;
        }
        private void LoadCustomIconsOnFrameworkThread()
        {

            CreateCustomIconDirectoryIfNotExists();

            var files = Directory.EnumerateFiles(CustomIconDirectory, "*.png", SearchOption.TopDirectoryOnly);
            var icons = new List<CustomIconInfo>();
            foreach (var file in files)
            {
                try
                {
                    if (TryGetCustomIcon(file, out var texture) && texture != null)
                    {
                        icons.Add(new CustomIconInfo
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

            AvailableIcons = icons;
            IsLoaded = true;
            Plugin.Log.Info($"Loaded {AvailableIcons.Count} custom icons from '{CustomIconDirectory}'.");
        }
        public bool TryGetCustomIcon(string? fileNameOrPath, out IDalamudTextureWrap? texture)
        {
            texture = null;
            if (fileNameOrPath.IsNullOrWhitespace()) return false;

            // Accept either a full path or just the file name
            var candidatePath = Path.IsPathRooted(fileNameOrPath)
                ? fileNameOrPath
                : Path.Combine(CustomIconDirectory, fileNameOrPath);

            var fullPath = Path.GetFullPath(candidatePath);
            var fullCustomDir = Path.GetFullPath(CustomIconDirectory);

            // Ensure the path is within the custom icon directory
            if (!fullPath.StartsWith(fullCustomDir, StringComparison.OrdinalIgnoreCase)) return false;

            if (!File.Exists(fullPath) || !fullPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) return false;

            Plugin.Log.Verbose($"Attempting to load custom icon from '{fullPath}'.");
            var wrapTex = Plugin.TextureProvider.GetFromFile(fullPath);
            var wrap = wrapTex.GetWrapOrDefault();

            // Sometimes on startup, TextureProvider may not be ready
            var retryCount = 0;
            while (retryCount < 3 && wrap == null)
                wrap = wrapTex.GetWrapOrDefault();
            if (wrap == null) return false;

            texture = wrap;
            return true;
        }
        public void ReloadCustomIcons()
        {

            AvailableIcons = [];
            IsLoaded = false;
            reloadRequested = true;

        }
        public void Dispose()
        {
            Plugin.Framework.Update -= OnFrameworkUpdate;
            fileWatcher?.Dispose();
            reloadDebounceTimer?.Dispose();
            AvailableIcons = [];
            GC.SuppressFinalize(this);
        }
    }
}
