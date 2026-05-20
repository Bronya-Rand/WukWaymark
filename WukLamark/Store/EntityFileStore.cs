using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;

namespace WukLamark.Store;

/// <summary>
/// Generic file-backed store that manages per-entity JSON files in a directory.
/// Uses Dalamud's <see cref="IReliableFileStorage"/> for atomic writes with
/// automatic DB-backed corruption recovery.
/// </summary>
/// <remarks>
/// <para>
/// The <paramref name="getId"/> function extracts the GUID from an entity, which
/// determines the filename ({guid}.json). The store does not enforce uniqueness on
/// any other field — that's the responsibility of the caller.
/// </para>
/// </remarks>
/// <typeparam name="T">Entity type to store. Must be a class serializable by System.Text.Json.</typeparam>
internal sealed class EntityFileStore<T> where T : class
{
    private readonly IReliableFileStorage reliableFileStorage;
    private readonly string directory;
    private readonly Func<T, Guid> getId;
    private readonly string storeName; // For logging clarity (e.g., "Marker", "Group", "Template")

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        IncludeFields = true, // Required for Vector3/Vector4 fields (X, Y, Z, W)
    };

    /// <summary>
    /// Tracks in-progress async save tasks per file path.
    /// </summary>
    private readonly Dictionary<string, Task> fileSavingTasks = [];

    /// <summary>
    /// In-memory cache of all loaded entities. Rebuilt on <see cref="LoadAll"/>,
    /// updated incrementally on <see cref="Save"/> and <see cref="Delete"/>.
    /// </summary>
    public List<T> Items { get; private set; } = [];

    /// <summary>
    /// Creates a new <see cref="EntityFileStore{T}"/>.
    /// </summary>
    /// <param name="reliableFileStorage">Dalamud's reliable file storage service.</param>
    /// <param name="directory">
    /// Absolute path to the directory where entity files are stored.
    /// Created automatically if it doesn't exist.
    /// </param>
    /// <param name="getId">Function that extracts the GUID from an entity instance.</param>
    /// <param name="storeName">Human-readable name for logging (e.g., "Marker", "Group").</param>
    public EntityFileStore(IReliableFileStorage reliableFileStorage, string directory, Func<T, Guid> getId, string storeName)
    {
        this.reliableFileStorage = reliableFileStorage;
        this.directory = directory;
        this.getId = getId;
        this.storeName = storeName;

        // Ensure the storage directory exists on construction
        if (!Directory.Exists(directory))
            Directory.CreateDirectory(directory);
    }

    /// <summary>
    /// Scans the directory for all JSON files and deserializes each into the in-memory cache.
    /// </summary>
    /// <remarks>
    /// Uses <see cref="IReliableFileStorage.ReadAllTextAsync(string, System.Action{string})"/>
    /// with a deserializing reader callback. If deserialization fails (e.g., corrupt JSON),
    /// IReliableFileStorage automatically retries with its internal DB backup before surfacing
    /// the error. Files that fail both reads are logged and skipped.
    /// </remarks>
    public void LoadAll()
    {
        Items.Clear();

        if (!Directory.Exists(directory))
            return;

        var files = Directory.GetFiles(directory, "*.json");
        Plugin.Log.Info($"[{storeName}Store] Loading {files.Length} file(s) from '{directory}'...");

        foreach (var filePath in files)
        {
            try
            {
                T? entity = null;

                // Use the reader-callback overload: if the reader throws (e.g., bad JSON),
                // IReliableFileStorage will automatically retry with its DB backup copy.
                reliableFileStorage.ReadAllTextAsync(filePath, json =>
                {
                    entity = JsonSerializer.Deserialize<T>(json, SerializerOptions) ?? throw new JsonException("Deserialized to null");
                }).GetAwaiter().GetResult();

                if (entity != null)
                    Items.Add(entity);
            }
            catch (FileNotFoundException)
            {
                // File was deleted between Directory.GetFiles() and read — skip silently
                Plugin.Log.Debug($"[{storeName}Store] File disappeared before read: '{Path.GetFileName(filePath)}'");
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"[{storeName}Store] Failed to load '{Path.GetFileName(filePath)}': {ex.Message}");
            }
        }

        Plugin.Log.Info($"[{storeName}Store] Loaded {Items.Count} of {files.Length} entities.");
    }

    /// <summary>
    /// Saves a single entity to disk as {id}.json and updates the in-memory cache.
    /// </summary>
    /// <remarks>
    /// Writes are async via <see cref="IReliableFileStorage.WriteAllTextAsync"/>.
    /// If a previous write to the same file is still in progress, the save is deferred
    /// to the next framework tick using <see cref="IFramework.RunOnTick"/>.
    /// </remarks>
    /// <param name="entity">The entity to save. Must not be null.</param>
    public void Save(T entity)
    {
        if (entity == null)
        {
            Plugin.Log.Error($"[{storeName}Store] Cannot save a null entity.");
            return;
        }

        var id = getId(entity);
        var path = GetFilePath(id);

        try
        {
            var json = JsonSerializer.Serialize(entity, SerializerOptions);

            // Check if there's an existing save task for this file path.
            if (fileSavingTasks.TryGetValue(path, out var existingTask))
            {
                if (existingTask.IsCompleted)
                    fileSavingTasks[path] = reliableFileStorage.WriteAllTextAsync(path, json);
                else if (existingTask.IsFaulted)
                {
                    Plugin.Log.Warning($"[{storeName}Store] Previous save for '{id}' faulted: {existingTask.Exception?.Message}. Retrying...");
                    fileSavingTasks[path] = reliableFileStorage.WriteAllTextAsync(path, json);
                }
                else
                {
                    // Previous save still running — defer to next framework tick
                    Plugin.Log.Debug($"[{storeName}Store] Save for '{id}' in progress, deferring to next tick.");
                    Plugin.Framework.RunOnTick(() => Save(entity));
                    return; // Don't update cache
                }
            }
            else
                fileSavingTasks[path] = reliableFileStorage.WriteAllTextAsync(path, json);

            // Update in-memory cache: replace existing or add new
            var existingIndex = Items.FindIndex(item => getId(item) == id);
            if (existingIndex >= 0)
                Items[existingIndex] = entity;
            else
                Items.Add(entity);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"[{storeName}Store] Failed to save entity '{id}': {ex.Message}");
        }
    }

    /// <summary>
    /// Deletes an entity from disk and removes it from the in-memory cache.
    /// </summary>
    /// <remarks>
    /// <see cref="IReliableFileStorage"/> lacks delete functionality, thus 
    /// treat the request as a file deletion.
    /// </remarks>
    /// <param name="id">The GUID of the entity to delete.</param>
    /// <returns>True if the entity was found and removed, false if it didn't exist.</returns>
    public bool Delete(Guid id)
    {
        var path = GetFilePath(id);

        // Remove from cache
        var removed = Items.RemoveAll(item => getId(item) == id) > 0;

        // Remove from disk
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
                Plugin.Log.Debug($"[{storeName}Store] Deleted file '{Path.GetFileName(path)}'.");
            }

            // Clean up any pending save tasks for this file
            fileSavingTasks.Remove(path);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"[{storeName}Store] Failed to delete file for entity '{id}': {ex.Message}");
        }

        return removed;
    }

    /// <summary>
    /// Finds an entity in the in-memory cache by its GUID.
    /// </summary>
    /// <param name="id">The GUID to search for.</param>
    /// <returns>The entity if found, null otherwise.</returns>
    public T? FindById(Guid id) => Items.Find(item => getId(item) == id);

    /// <summary>
    /// Checks whether an entity with the given GUID exists in the in-memory cache.
    /// </summary>
    public bool Contains(Guid id) => Items.Exists(item => getId(item) == id);

    /// <summary>
    /// Gets the full file path for an entity by its GUID.
    /// </summary>
    private string GetFilePath(Guid id) => Path.Combine(directory, $"{id}.json");
}
