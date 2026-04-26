using Dalamud.Interface.Textures;
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
    }

    public sealed class CustomIconService : IDisposable
    {
        private FileSystemWatcher? fileWatcher;
        private System.Timers.Timer? reloadDebounceTimer;
        private volatile bool reloadRequested = true;

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
                    var texture = GetCustomIconOrNull(file);
                    if (texture != null)
                    {
                        newIcons.Add(new CustomIconInfo
                        {
                            FileName = Path.GetFileName(file)
                        });
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.Error($"Failed to load custom icon '{file}': {ex.Message}");
                }
            }

            AvailableIcons = newIcons;
            IsLoaded = true;

            Plugin.Log.Info($"Loaded {AvailableIcons.Count} custom icons from '{CustomIconDirectory}'.");
        }
        private ISharedImmediateTexture? GetCustomIconOrNull(string? fileName)
        {
            var fullPath = GetCustomIconPath(fileName);
            if (fullPath.IsNullOrEmpty()) return null;

            var wrapTex = Plugin.TextureProvider.GetFromFile(fullPath);
            return wrapTex;
        }
        private string? GetCustomIconPath(string? fileName)
        {
            if (fileName.IsNullOrWhitespace()) return null;
            var candidatePath = Path.Combine(CustomIconDirectory, fileName);
            var fullPath = Path.GetFullPath(candidatePath);
            var fullCustomDir = Path.GetFullPath(CustomIconDirectory);

            // Ensure the path is within the custom icon directory
            if (!fullPath.StartsWith(fullCustomDir, StringComparison.OrdinalIgnoreCase)) return null;

            // Ensure the file exists and is a PNG
            if (!File.Exists(fullPath) || !fullPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) return null;
            return fullPath;
        }
        public IDalamudTextureWrap GetWrapOrEmpty(string fileName) => Plugin.TextureProvider.GetFromFile(GetCustomIconPath(fileName) ?? string.Empty).GetWrapOrEmpty();
        public (bool success, string? error) SavePNGToCustomIconsDir(string fullSrcIconPath)
        {
            if (fullSrcIconPath.IsNullOrEmpty()) return (false, "No file selected for upload.");
            if (!File.Exists(fullSrcIconPath))
                return (false, $"File '{fullSrcIconPath}' does not exist.");
            if (!fullSrcIconPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                return (false, "Only PNG files are supported for custom icons.");

            // Check if file doesn't already exist in the custom icon directory
            var destFilePath = Path.Combine(CustomIconDirectory, Path.GetFileName(fullSrcIconPath));
            if (File.Exists(destFilePath))
                return (false, $"A file named '{Path.GetFileName(fullSrcIconPath)}' already exists in the custom icon directory.");

            // Check if the file is loadable
            var testWrapLoadable = Plugin.TextureProvider.GetFromFile(fullSrcIconPath).RentAsync().Result;
            if (testWrapLoadable == null)
                return (false, "The selected PNG file could not be loaded. It may be corrupted or in an unsupported format.");
            testWrapLoadable.Dispose();

            File.Copy(fullSrcIconPath, destFilePath);
            return (true, null);
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

            AvailableIcons = [];
            GC.SuppressFinalize(this);
        }
    }
}
