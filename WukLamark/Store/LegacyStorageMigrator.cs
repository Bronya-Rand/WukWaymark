using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using WukLamark.Models;
using WukLamark.Services;

namespace WukLamark.Store;

/// <summary>
/// Handles migration from 
/// (<c>&lt;CHAR_HASH&gt;_waymarks.json</c> and <c>shared_waymarks.json</c>)
/// to (<c>markers/</c>, <c>groups/</c>).
/// </summary>
/// <remarks>
/// <para>
/// Migration steps:
/// <list type="number">
///   <item>Detect whether migration is needed (no <c>markers/</c> directory with files)</item>
///   <item>Find all legacy files in the plugin config directory</item>
///   <item>Deserialize each using <see cref="PlayerMarkerData"/> + run schema migrations (V0→V1)</item>
///   <item>Parse raw JSON to extract GroupId values (since the property is removed from the model)</item>
///   <item>Write individual marker files to <c>markers/</c></item>
///   <item>Build group <see cref="MarkerGroup.MarkerIds"/> from extracted GroupId mappings</item>
///   <item>Write individual group files to <c>groups/</c></item>
///   <item>Rename legacy files to <c>*.migrated.bak</c></item>
/// </list>
/// </para>
/// </remarks>
internal sealed class LegacyStorageMigrator
{
    private readonly string pluginConfigDir;
    private readonly EntityFileStore<Marker> markerStore;
    private readonly EntityFileStore<MarkerGroup> groupStore;
    private readonly MarkerMigrationService migrationService;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        IncludeFields = true,
    };

    // Legacy file patterns
    private const string SharedMarkersFileName = "shared_waymarks.json";
    private const string PersonalMarkersSuffix = "_waymarks.json";
    private const string MigratedBackupSuffix = ".migrated.bak";

    public LegacyStorageMigrator(
        string pluginConfigDir,
        EntityFileStore<Marker> markerStore,
        EntityFileStore<MarkerGroup> groupStore)
    {
        this.pluginConfigDir = pluginConfigDir;
        this.markerStore = markerStore;
        this.groupStore = groupStore;
        migrationService = new MarkerMigrationService();
    }

    /// <summary>
    /// Checks if legacy files exist and migrates them.
    /// </summary>
    /// <returns>True if migration was performed, false if skipped.</returns>
    public bool MigrateIfNeeded()
    {
        // If the markers directory already has files, assume migration is done.
        // This prevents re-migration if the user has already been using the new format.
        var markersDir = Path.Combine(pluginConfigDir, "markers");
        if (Directory.Exists(markersDir) && Directory.GetFiles(markersDir, "*.json").Length > 0)
        {
            Plugin.Log.Debug("[Migration] markers/ directory already contains files — skipping migration.");
            return false;
        }

        // Find all legacy files that need migration
        var legacyFiles = FindLegacyFiles();
        if (legacyFiles.Count == 0)
        {
            Plugin.Log.Debug("[Migration] No legacy files found — nothing to migrate.");
            return false;
        }

        Plugin.Log.Info($"[Migration] Found {legacyFiles.Count} legacy file(s) to migrate.");

        var totalMarkers = 0;
        var totalGroups = 0;

        foreach (var legacyFile in legacyFiles)
        {
            var (markers, groups) = MigrateSingleFile(legacyFile);
            totalMarkers += markers;
            totalGroups += groups;
        }

        Plugin.Log.Info($"[Migration] Complete — migrated {totalMarkers} marker(s) and {totalGroups} group(s) from {legacyFiles.Count} file(s).");
        return true;
    }

    /// <summary>
    /// Finds all legacy-format files in the plugin config directory.
    /// Matches <c>shared_waymarks.json</c> and <c>*_waymarks.json</c> (character-specific).
    /// Ignores files already renamed to <c>.migrated.bak</c>.
    /// </summary>
    private List<string> FindLegacyFiles()
    {
        var files = new List<string>();

        // Check for shared markers file
        var sharedPath = Path.Combine(pluginConfigDir, SharedMarkersFileName);
        if (File.Exists(sharedPath))
            files.Add(sharedPath);

        // Check for character-specific personal marker files.
        // These follow the naming pattern: <16-char-hex-hash>_waymarks.json
        foreach (var filePath in Directory.GetFiles(pluginConfigDir, $"*{PersonalMarkersSuffix}"))
        {
            var fileName = Path.GetFileName(filePath);

            // Skip the shared file (already handled above)
            if (fileName.Equals(SharedMarkersFileName, StringComparison.OrdinalIgnoreCase))
                continue;

            // Skip files that have already been backed up
            if (fileName.EndsWith(MigratedBackupSuffix, StringComparison.OrdinalIgnoreCase))
                continue;

            files.Add(filePath);
        }

        return files;
    }

    /// <summary>
    /// Migrates a file: deserializes, applies schema migrations,
    /// writes individual marker/group files, and renames the legacy file to .bak.
    /// </summary>
    /// <returns>Tuple of (markersWritten, groupsWritten).</returns>
    private (int markersWritten, int groupsWritten) MigrateSingleFile(string legacyFilePath)
    {
        var fileName = Path.GetFileName(legacyFilePath);
        Plugin.Log.Info($"[Migration] Migrating '{fileName}'...");

        string rawJson;
        try
        {
            rawJson = File.ReadAllText(legacyFilePath);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"[Migration] Failed to read legacy file '{fileName}': {ex.Message}");
            return (0, 0);
        }

        // Deserialize into PlayerMarkerData for schema migration
        PlayerMarkerData? data;
        try
        {
            data = JsonSerializer.Deserialize<PlayerMarkerData>(rawJson, JsonOptions);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"[Migration] Failed to deserialize legacy file '{fileName}': {ex.Message}");
            return (0, 0);
        }

        if (data == null)
        {
            Plugin.Log.Warning($"[Migration] Legacy file '{fileName}' deserialized to null — skipping.");
            return (0, 0);
        }

        // Apply any pending schema migrations (e.g., V0 → V1: legacy icon fields → MarkerIcon)
        var migrated = migrationService.MigratePlayerMarkerData(data);
        if (migrated)
            Plugin.Log.Info($"[Migration] Applied schema migration(s) to '{fileName}'.");

        // Step 2: Extract GroupId mappings from the raw JSON 
        var markerToGroupMap = ExtractGroupIdMappings(rawJson);

        // Write individual marker files 
        var markersWritten = 0;
        foreach (var marker in data.Markers)
        {
            try
            {
                marker.FileVersion = 1;
                markerStore.Save(marker);
                markersWritten++;
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"[Migration] Failed to write marker '{marker.Id}' ({marker.Name}): {ex.Message}");
            }
        }

        // Build group MarkerIds and write individual group files
        var groupsWritten = 0;
        foreach (var group in data.Groups)
        {
            try
            {
                // Populate MarkerIds by looking up which markers referenced this group
                group.MarkerIds = markerToGroupMap
                    .Where(kvp => kvp.Value == group.Id)
                    .Select(kvp => kvp.Key)
                    .ToList();

                group.FileVersion = 1;
                groupStore.Save(group);
                groupsWritten++;

                if (group.MarkerIds.Count > 0)
                    Plugin.Log.Debug($"[Migration] Group '{group.Name}' → {group.MarkerIds.Count} marker(s)");
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"[Migration] Failed to write group '{group.Id}' ({group.Name}): {ex.Message}");
            }
        }

        // Rename legacy file to 'X.migrated.bak'
        try
        {
            var backupPath = legacyFilePath + MigratedBackupSuffix;

            // If a backup already exists (e.g., from a previous partial migration), remove it
            if (File.Exists(backupPath))
                File.Delete(backupPath);

            File.Move(legacyFilePath, backupPath);
            Plugin.Log.Info($"[Migration] Renamed '{fileName}' → '{Path.GetFileName(backupPath)}'");
        }
        catch (Exception ex)
        {
            // Data has been migrated successfully, cleanup failed.
            Plugin.Log.Warning($"[Migration] Failed to rename legacy file '{fileName}': {ex.Message}");
        }

        Plugin.Log.Info($"[Migration] '{fileName}': {markersWritten} marker(s), {groupsWritten} group(s) migrated.");
        return (markersWritten, groupsWritten);
    }

    /// <summary>
    /// Parses the raw JSON of a legacy file to extract Marker ID → GroupId mappings.
    /// </summary>
    /// <returns>
    /// Dictionary mapping each marker's GUID to its group's GUID.
    /// Markers without a GroupId (ungrouped) are not included.
    /// </returns>
    private static Dictionary<Guid, Guid> ExtractGroupIdMappings(string rawJson)
    {
        var map = new Dictionary<Guid, Guid>();

        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;

            // The legacy format uses either "Markers" (current) or "Waymarks" (very old).
            // Try "Markers" first, fall back to "Waymarks".
            JsonElement markersArray;
            if (root.TryGetProperty("Markers", out markersArray) && markersArray.ValueKind == JsonValueKind.Array)
            {
                // Use "Markers" array
            }
            else if (root.TryGetProperty("Waymarks", out markersArray) && markersArray.ValueKind == JsonValueKind.Array)
            {
                // Fall back to legacy "Waymarks" array
            }
            else
            {
                return map; // No markers array found
            }

            foreach (var markerElement in markersArray.EnumerateArray())
            {
                // Extract the marker's Id
                if (!markerElement.TryGetProperty("Id", out var idProp))
                    continue;
                if (!Guid.TryParse(idProp.GetString(), out var markerId))
                    continue;

                // Extract GroupId (null for ungrouped markers)
                if (!markerElement.TryGetProperty("GroupId", out var groupIdProp))
                    continue;
                if (groupIdProp.ValueKind == JsonValueKind.Null)
                    continue;
                if (!Guid.TryParse(groupIdProp.GetString(), out var groupId))
                    continue;

                map[markerId] = groupId;
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning($"[Migration] Failed to extract GroupId mappings from raw JSON: {ex.Message}");
        }

        if (map.Count > 0)
            Plugin.Log.Debug($"[Migration] Extracted {map.Count} marker→group mapping(s) from raw JSON.");

        return map;
    }
}
