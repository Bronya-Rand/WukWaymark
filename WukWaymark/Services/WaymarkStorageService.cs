using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WukWaymark.Models;

namespace WukWaymark.Services;

/// <summary>
/// Service responsible for managing waymark persistence across characters.
/// 
/// Architecture:
/// - Shared waymarks are stored in a separate JSON file accessible to all characters.
/// - Personal waymarks remain in Configuration.Waymarks but are tagged with a CharacterHash.
/// - The CharacterHash is a truncated SHA-256 of the character's content ID — no raw IDs stored.
/// </summary>
public class WaymarkStorageService
{
    private readonly Configuration configuration;
    private readonly string sharedWaymarksPath;

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

    public WaymarkStorageService(Configuration configuration, string pluginConfigDir)
    {
        this.configuration = configuration;
        sharedWaymarksPath = Path.Combine(pluginConfigDir, "shared_waymarks.json");
        LoadSharedWaymarks();
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
        Plugin.Log.Information($"Character hash set: {CurrentCharacterHash}");
    }

    /// <summary>
    /// Clears the character hash (e.g., on logout).
    /// </summary>
    public void ClearCharacterHash()
    {
        CurrentCharacterHash = null;
    }

    /// <summary>
    /// Returns all waymarks that should be visible to the current session.
    /// This merges:
    /// - Personal waymarks matching the current character hash
    /// - Shared waymarks (from config + shared file)
    /// </summary>
    public List<Waymark> GetVisibleWaymarks()
    {
        var result = new List<Waymark>();

        foreach (var waymark in configuration.Waymarks)
        {
            if (waymark.Scope == WaymarkScope.Shared)
            {
                result.Add(waymark);
            }
            else if (waymark.Scope == WaymarkScope.Personal
                && CurrentCharacterHash != null
                && waymark.CharacterHash == CurrentCharacterHash)
            {
                result.Add(waymark);
            }
        }

        // Also add the in-memory shared waymarks
        result.AddRange(SharedWaymarks);

        return result;
    }

    /// <summary>
    /// Loads shared waymarks into memory from the shared JSON file.
    /// </summary>
    public void LoadSharedWaymarks()
    {
        if (!File.Exists(sharedWaymarksPath))
        {
            SharedWaymarks = [];
            return;
        }

        try
        {
            var json = File.ReadAllText(sharedWaymarksPath);
            SharedWaymarks = JsonSerializer.Deserialize<List<Waymark>>(json) ?? [];
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"Failed to load shared waymarks: {ex.Message}");
            SharedWaymarks = [];
        }
    }

    /// <summary>
    /// Saves the in-memory shared waymarks out to the JSON file.
    /// </summary>
    public void SaveSharedWaymarks()
    {
        try
        {
            var json = JsonSerializer.Serialize(SharedWaymarks, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(sharedWaymarksPath, json);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"Failed to save shared waymarks: {ex.Message}");
        }
    }
}
