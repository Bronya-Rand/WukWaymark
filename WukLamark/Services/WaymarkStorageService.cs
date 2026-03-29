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
/// Service responsible for managing waymark persistence across characters.
/// 
/// Architecture:
/// - Personal waymarks/groups are stored in character-specific JSON files
/// - Shared waymarks/groups are stored in a shared JSON file accessible to all characters
/// - The CharacterHash is a truncated SHA-256 of the character's content ID — no raw IDs stored
/// </summary>
public class WaymarkStorageService
{
    private readonly string pluginConfigDir;
    private readonly string sharedWaymarksPath;
    private string? personalWaymarksPath;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        IncludeFields = true
    };

    /// <summary>
    /// Hashed identifier for the currently logged-in character.
    /// Computed once on login, used to filter personal waymarks.
    /// Null when no character is logged in.
    /// </summary>
    public string? CurrentCharacterHash { get; private set; }

    /// <summary>
    /// In-memory cache of shared waymarks to avoid constant disk reads.
    /// </summary>
    public List<Waymark> SharedWaymarks { get; private set; } = [];

    /// <summary>
    /// In-memory cache of shared groups.
    /// </summary>
    public List<WaymarkGroup> SharedGroups { get; private set; } = [];

    /// <summary>
    /// In-memory cache of personal waymarks for the current character.
    /// </summary>
    public List<Waymark> PersonalWaymarks { get; private set; } = [];

    /// <summary>
    /// In-memory cache of personal groups for the current character.
    /// </summary>
    public List<WaymarkGroup> PersonalGroups { get; private set; } = [];

    // Cached combined lists to avoid per-frame allocations
    private List<Waymark>? cachedVisibleWaymarks;
    private List<WaymarkGroup>? cachedVisibleGroups;
    private bool cacheInvalidated = true;

    public WaymarkStorageService(string pluginConfigDir)
    {
        this.pluginConfigDir = pluginConfigDir;
        sharedWaymarksPath = Path.Combine(pluginConfigDir, "shared_waymarks.json");
        LoadSharedWaymarks();
    }

    /// <summary>
    /// Invalidates the visible waymarks/groups cache.
    /// </summary>
    private void InvalidateCache()
    {
        cacheInvalidated = true;
    }

    private void CheckWaymarkCache()
    {
        if (cacheInvalidated || cachedVisibleWaymarks == null || cachedVisibleGroups == null)
        {
            cachedVisibleWaymarks = [.. PersonalWaymarks, .. SharedWaymarks];
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
        personalWaymarksPath = Path.Combine(pluginConfigDir, $"{CurrentCharacterHash}_waymarks.json");
        LoadPersonalWaymarks();
        Plugin.Log.Debug($"Character hash set for current character.");
    }

    /// <summary>
    /// Clears the character hash (e.g., on logout).
    /// </summary>
    public void ClearCharacterHash()
    {
        CurrentCharacterHash = null;
        personalWaymarksPath = null;
        PersonalWaymarks.Clear();
        PersonalGroups.Clear();
        InvalidateCache();
    }

    /// <summary>
    /// Gets the number of waymarks that have been shared and created by the current character.
    /// </summary>
    /// <remarks>Ensure that the current character hash and waymark cache is set appropriately before calling this method.
    /// Only waymarks associated with the current character are included in the count.</remarks>
    /// <returns>The number of shared waymarks created by the character identified by the current character hash.</returns>
    public int GetSharedCreatedWaymarksCount()
    {
        CheckWaymarkCache();
        var sharedWaymarks = cachedVisibleWaymarks!.Count(w => w.CharacterHash == CurrentCharacterHash && w.Scope == WaymarkScope.Shared);
        return sharedWaymarks;
    }

    /// <summary>
    /// Returns all waymarks that should be visible to the current session.
    /// </summary>
    /// <remarks>
    /// Returns a merged list of personal waymarks for the current character and shared waymarks.
    /// </remarks>
    public List<Waymark> GetVisibleWaymarks()
    {
        CheckWaymarkCache();
        return cachedVisibleWaymarks!;
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
    public List<WaymarkGroup> GetVisibleGroups()
    {
        CheckWaymarkCache();
        return cachedVisibleGroups!;
    }

    /// <summary>
    /// Loads shared waymarks and groups into memory from the shared JSON file.
    /// </summary>
    public void LoadSharedWaymarks()
    {
        if (!File.Exists(sharedWaymarksPath))
        {
            SharedWaymarks = [];
            SharedGroups = [];
            InvalidateCache();
            return;
        }

        try
        {
            var json = File.ReadAllText(sharedWaymarksPath);
            var data = JsonSerializer.Deserialize<PlayerWaymarksData>(json, JsonOptions);

            if (data != null)
            {
                SharedWaymarks = data.Waymarks;
                SharedGroups = data.Groups;
            }
            else
            {
                SharedWaymarks = [];
                SharedGroups = [];
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"Failed to load shared waymarks: {ex.Message}");
            SharedWaymarks = [];
            SharedGroups = [];
        }

        InvalidateCache();
    }

    /// <summary>
    /// Saves the in-memory shared waymarks and groups out to the JSON file.
    /// </summary>
    public void SaveSharedWaymarks()
    {
        try
        {
            var data = new PlayerWaymarksData
            {
                Waymarks = SharedWaymarks,
                Groups = SharedGroups
            };

            var json = JsonSerializer.Serialize(data, JsonOptions);
            File.WriteAllText(sharedWaymarksPath, json);
            InvalidateCache();
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"Failed to save shared waymarks: {ex.Message}");
        }
    }

    public void EraseCreatedSharedWaymarks()
    {
        if (CurrentCharacterHash == null)
        {
            Plugin.Log.Warning("Cannot erase created shared waymarks: no character hash set.");
            return;
        }
        SharedWaymarks.RemoveAll(w => w.CharacterHash == CurrentCharacterHash);
        SaveSharedWaymarks();
    }

    /// <summary>
    /// Loads personal waymarks and groups for the current character.
    /// </summary>
    public void LoadPersonalWaymarks()
    {
        if (string.IsNullOrEmpty(personalWaymarksPath) || !File.Exists(personalWaymarksPath))
        {
            PersonalWaymarks = [];
            PersonalGroups = [];
            InvalidateCache();
            return;
        }

        try
        {
            var json = File.ReadAllText(personalWaymarksPath);
            var data = JsonSerializer.Deserialize<PlayerWaymarksData>(json, JsonOptions);

            if (data != null)
            {
                PersonalWaymarks = data.Waymarks;
                PersonalGroups = data.Groups;
            }
            else
            {
                PersonalWaymarks = [];
                PersonalGroups = [];
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"Failed to load personal waymarks: {ex.Message}");
            PersonalWaymarks = [];
            PersonalGroups = [];
        }

        InvalidateCache();
    }

    /// <summary>
    /// Saves the in-memory personal waymarks and groups for the current character.
    /// </summary>
    public void SavePersonalWaymarks()
    {
        if (string.IsNullOrEmpty(personalWaymarksPath))
        {
            Plugin.Log.Warning("Cannot save personal waymarks: no character hash set.");
            return;
        }

        try
        {
            var data = new PlayerWaymarksData
            {
                Waymarks = PersonalWaymarks,
                Groups = PersonalGroups
            };

            var json = JsonSerializer.Serialize(data, JsonOptions);
            File.WriteAllText(personalWaymarksPath, json);
            InvalidateCache();
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"Failed to save personal waymarks: {ex.Message}");
        }
    }
}
