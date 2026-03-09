using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using WukWaymark.Helpers;
using WukWaymark.Models;

namespace WukWaymark.Services;

/// <summary>
/// Service responsible for managing waymark persistence across characters.
/// 
/// Architecture:
/// - Personal waymarks/groups are stored in obfuscated character-specific JSON files
/// - Shared waymarks/groups are stored in an obfuscated shared JSON file accessible to all characters
/// - The CharacterHash is a truncated SHA-256 of the character's content ID — no raw IDs stored
/// - All data is obfuscated using XOR cipher to prevent casual viewing
/// </summary>
public class WaymarkStorageService
{
    private readonly Configuration configuration;
    private readonly string pluginConfigDir;
    private readonly string sharedWaymarksPath;
    private string? personalWaymarksPath;

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

    public WaymarkStorageService(Configuration configuration, string pluginConfigDir)
    {
        this.configuration = configuration;
        this.pluginConfigDir = pluginConfigDir;
        sharedWaymarksPath = Path.Combine(pluginConfigDir, "shared_waymarks.json");
        LoadSharedWaymarks();
    }

    /// <summary>
    /// Computes and caches the character hash from the player's content ID and player name.
    /// Should be called on character login / territory change when player is available.
    /// </summary>
    /// <param name="contentId">The character's content ID.</param>
    /// <param name="playerName">The player's character name (unused but kept for future extensibility).</param>
    public void SetCharacterHash(ulong contentId, string playerName)
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
    }

    /// <summary>
    /// Returns all waymarks that should be visible to the current session.
    /// This merges:
    /// - Personal waymarks for the current character
    /// - Shared waymarks
    /// </summary>
    public List<Waymark> GetVisibleWaymarks()
    {
        var result = new List<Waymark>();
        result.AddRange(PersonalWaymarks);
        result.AddRange(SharedWaymarks);
        return result;
    }

    /// <summary>
    /// Returns all groups that should be visible to the current session.
    /// This merges:
    /// - Personal groups for the current character
    /// - Shared groups
    /// </summary>
    public List<WaymarkGroup> GetVisibleGroups()
    {
        var result = new List<WaymarkGroup>();
        result.AddRange(PersonalGroups);
        result.AddRange(SharedGroups);
        return result;
    }

    /// <summary>
    /// Loads shared waymarks and groups into memory from the shared JSON file.
    /// Uses obfuscation for privacy.
    /// </summary>
    public void LoadSharedWaymarks()
    {
        if (!File.Exists(sharedWaymarksPath))
        {
            SharedWaymarks = [];
            SharedGroups = [];
            return;
        }

        try
        {
            var obfuscatedData = File.ReadAllText(sharedWaymarksPath);
            var data = ObfuscationHelper.Deobfuscate<PlayerWaymarksData>(obfuscatedData);

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
    }

    /// <summary>
    /// Saves the in-memory shared waymarks and groups out to the JSON file.
    /// Uses obfuscation for privacy.
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

            var obfuscatedData = ObfuscationHelper.Obfuscate(data);
            File.WriteAllText(sharedWaymarksPath, obfuscatedData);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"Failed to save shared waymarks: {ex.Message}");
        }
    }

    /// <summary>
    /// Loads personal waymarks and groups for the current character.
    /// Uses obfuscation for privacy.
    /// </summary>
    public void LoadPersonalWaymarks()
    {
        if (string.IsNullOrEmpty(personalWaymarksPath) || !File.Exists(personalWaymarksPath))
        {
            PersonalWaymarks = [];
            PersonalGroups = [];
            return;
        }

        try
        {
            var obfuscatedData = File.ReadAllText(personalWaymarksPath);
            var data = ObfuscationHelper.Deobfuscate<PlayerWaymarksData>(obfuscatedData);

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
    }

    /// <summary>
    /// Saves the in-memory personal waymarks and groups for the current character.
    /// Uses obfuscation for privacy.
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

            var obfuscatedData = ObfuscationHelper.Obfuscate(data);
            File.WriteAllText(personalWaymarksPath, obfuscatedData);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"Failed to save personal waymarks: {ex.Message}");
        }
    }
}
