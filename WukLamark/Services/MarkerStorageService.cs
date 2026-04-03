using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WukLamark.Models;

namespace WukLamark.Services;

/// <summary>
/// Service responsible for managing map marker persistence across characters.
/// 
/// Architecture:
/// - Personal markers/groups are stored in character-specific JSON files
/// - Shared markers/groups are stored in a shared JSON file accessible to all characters
/// - The CharacterHash is a truncated SHA-256 of the character's content ID — no raw IDs stored
/// </summary>
public class MarkerStorageService
{
    private readonly string pluginConfigDir;
    private readonly string sharedMarkersPath;
    private string? personalMarkersPath;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        IncludeFields = true
    };

    /// <summary>
    /// Hashed identifier for the currently logged-in character.
    /// Computed once on login, used to filter personal markers.
    /// Null when no character is logged in.
    /// </summary>
    public string? CurrentCharacterHash { get; private set; }

    /// <summary>
    /// In-memory cache of shared markers to avoid constant disk reads.
    /// </summary>
    public List<Marker> SharedMarkers { get; private set; } = [];

    /// <summary>
    /// In-memory cache of shared groups.
    /// </summary>
    public List<MarkerGroup> SharedGroups { get; private set; } = [];

    /// <summary>
    /// In-memory cache of personal markers for the current character.
    /// </summary>
    public List<Marker> PersonalMarkers { get; private set; } = [];

    /// <summary>
    /// In-memory cache of personal groups for the current character.
    /// </summary>
    public List<MarkerGroup> PersonalGroups { get; private set; } = [];

    // Cached combined lists to avoid per-frame allocations
    private List<Marker>? cachedVisibleMarkers;
    private List<MarkerGroup>? cachedVisibleGroups;
    private bool cacheInvalidated = true;

    public MarkerStorageService(string pluginConfigDir)
    {
        this.pluginConfigDir = pluginConfigDir;
        // Note: file names remain historic 'waymarks' for compatibility with existing data
        sharedMarkersPath = Path.Combine(pluginConfigDir, "shared_waymarks.json");
        LoadSharedMarkers();
    }

    /// <summary>
    /// Invalidates the visible markers/groups cache.
    /// </summary>
    private void InvalidateCache()
    {
        cacheInvalidated = true;
    }

    private void CheckMarkerCache()
    {
        if (cacheInvalidated || cachedVisibleMarkers == null || cachedVisibleGroups == null)
        {
            cachedVisibleMarkers = [.. PersonalMarkers, .. SharedMarkers];
            cachedVisibleGroups = [.. PersonalGroups, .. SharedGroups];
            cacheInvalidated = false;
        }
    }

    /// <summary>
    /// Computes and caches the character hash from the player's content ID.
    /// Should be called on character login / territory change when player is available.
    /// </summary>
    /// <param name="contentId">The character's content ID.</param>
    public void SetCharacterHash(ulong contentId)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(contentId.ToString()));
        CurrentCharacterHash = Convert.ToHexString(bytes)[..16];
        // Personal file name retains legacy 'waymarks' suffix for backward compatibility
        personalMarkersPath = Path.Combine(pluginConfigDir, $"{CurrentCharacterHash}_waymarks.json");
        LoadPersonalMarkers();
        Plugin.Log.Debug($"Character hash set for current character.");
    }

    /// <summary>
    /// Clears the character hash (e.g., on logout).
    /// </summary>
    public void ClearCharacterHash()
    {
        CurrentCharacterHash = null;
        personalMarkersPath = null;
        PersonalMarkers.Clear();
        PersonalGroups.Clear();
        InvalidateCache();
    }

    /// <summary>
    /// Gets the number of markers that have been shared and created by the current character.
    /// </summary>
    /// <remarks>Ensure that the current character hash and marker cache is set appropriately before calling this method.
    /// Only markers associated with the current character are included in the count.</remarks>
    /// <returns>The number of shared markers created by the character identified by the current character hash.</returns>
    public int GetSharedCreatedMarkersCount()
    {
        CheckMarkerCache();
        var count = cachedVisibleMarkers!.Count(w => w.CharacterHash == CurrentCharacterHash && w.Scope == MarkerScope.Shared);
        return count;
    }

    /// <summary>
    /// Returns all markers that should be visible to the current session.
    /// </summary>
    /// <remarks>
    /// Returns a merged list of personal markers for the current character and shared markers.
    /// </remarks>
    public List<Marker> GetVisibleMarkers()
    {
        CheckMarkerCache();
        return cachedVisibleMarkers!;
    }

    /// <summary>
    /// Returns all groups that should be visible to the current session.
    /// </summary>
    /// <remarks>
    /// This merges:
    /// - Personal groups for the current character
    /// - Shared groups
    /// Results are cached to avoid per-frame allocations.
    /// </remarks>
    public List<MarkerGroup> GetVisibleGroups()
    {
        CheckMarkerCache();
        return cachedVisibleGroups!;
    }

    /// <summary>
    /// Loads shared markers and groups into memory from the shared JSON file.
    /// </summary>
    public void LoadSharedMarkers()
    {
        if (!File.Exists(sharedMarkersPath))
        {
            SharedMarkers = [];
            SharedGroups = [];
            InvalidateCache();
            return;
        }

        try
        {
            var json = File.ReadAllText(sharedMarkersPath);
            var data = JsonSerializer.Deserialize<PlayerMarkerData>(json, JsonOptions);

            if (data != null)
            {
                // Data model keeps property named 'Waymarks' for historical JSON compatibility
                SharedMarkers = data.Waymarks;
                SharedGroups = data.Groups;
            }
            else
            {
                SharedMarkers = [];
                SharedGroups = [];
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"Failed to load shared markers: {ex.Message}");
            SharedMarkers = [];
            SharedGroups = [];
        }

        InvalidateCache();
    }

    /// <summary>
    /// Saves the in-memory shared markers and groups out to the JSON file.
    /// </summary>
    public void SaveSharedMarkers()
    {
        try
        {
            var data = new PlayerMarkerData
            {
                // Keep 'Waymarks' property for JSON schema compatibility
                Waymarks = SharedMarkers,
                Groups = SharedGroups
            };

            var json = JsonSerializer.Serialize(data, JsonOptions);
            File.WriteAllText(sharedMarkersPath, json);
            InvalidateCache();
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"Failed to save shared markers: {ex.Message}");
        }
    }

    public void EraseCreatedSharedMarkers()
    {
        if (CurrentCharacterHash == null)
        {
            Plugin.Log.Warning("Cannot erase created shared markers: no character hash set.");
            return;
        }
        SharedMarkers.RemoveAll(w => w.CharacterHash == CurrentCharacterHash);
        SaveSharedMarkers();
    }

    /// <summary>
    /// Loads personal markers and groups for the current character.
    /// </summary>
    public void LoadPersonalMarkers()
    {
        if (string.IsNullOrEmpty(personalMarkersPath) || !File.Exists(personalMarkersPath))
        {
            PersonalMarkers = [];
            PersonalGroups = [];
            InvalidateCache();
            return;
        }

        try
        {
            var json = File.ReadAllText(personalMarkersPath);
            var data = JsonSerializer.Deserialize<PlayerMarkerData>(json, JsonOptions);

            if (data != null)
            {
                // Keep property names consistent with existing JSON schema
                PersonalMarkers = data.Waymarks;
                PersonalGroups = data.Groups;
            }
            else
            {
                PersonalMarkers = [];
                PersonalGroups = [];
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"Failed to load personal markers: {ex.Message}");
            PersonalMarkers = [];
            PersonalGroups = [];
        }

        InvalidateCache();
    }

    /// <summary>
    /// Saves the in-memory personal markers and groups for the current character.
    /// </summary>
    public void SavePersonalMarkers()
    {
        if (string.IsNullOrEmpty(personalMarkersPath))
        {
            Plugin.Log.Warning("Cannot save personal markers: no character hash set.");
            return;
        }

        try
        {
            var data = new PlayerMarkerData
            {
                Waymarks = PersonalMarkers,
                Groups = PersonalGroups
            };

            var json = JsonSerializer.Serialize(data, JsonOptions);
            File.WriteAllText(personalMarkersPath, json);
            InvalidateCache();
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"Failed to save personal markers: {ex.Message}");
        }
    }
}
