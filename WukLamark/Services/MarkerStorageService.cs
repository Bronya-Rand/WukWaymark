using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Dalamud.Plugin.Services;
using WukLamark.Models;
using WukLamark.Store;

namespace WukLamark.Services;

/// <summary>
/// Service responsible for managing map marker, group, and template persistence.
///
/// Architecture (v2):
/// - Each marker, group, and template is stored as an individual {GUID}.json file
///   in its own directory (markers/, groups/, templates/).
/// - Uses <see cref="EntityFileStore{T}"/> backed by Dalamud's <see cref="IReliableFileStorage"/>
///   for atomic writes with automatic DB-backed corruption recovery.
/// - Personal vs. shared scoping is determined at runtime by comparing each entity's
///   CharacterHash against the currently logged-in character. All entities live in the
///   same flat directories — no separate personal/shared file paths.
/// - Group membership is stored on the group (MarkerGroup.MarkerIds), not on the marker.
///   An in-memory reverse lookup (markerToGroupMap) is built at load time for efficient
///   "which group does this marker belong to?" queries.
///
/// Legacy migration:
/// - On first load, <see cref="LegacyStorageMigrator"/> automatically detects old-format
///   files (*_waymarks.json, shared_waymarks.json) and migrates them to individual files.
/// </summary>
public sealed class MarkerStorageService
{
    private readonly string pluginConfigDir;
    private readonly EntityFileStore<Marker> markerStore;
    private readonly EntityFileStore<MarkerGroup> groupStore;
    private readonly EntityFileStore<MarkerTemplate> templateStore;

    /// <summary>
    /// Hashed identifier for the currently logged-in character.
    /// Null when no character is logged in.
    /// </summary>
    public string? CurrentCharacterHash { get; private set; }

    // Holds the currently visible markers and groups based on the
    // current character hash and scopes.
    private List<Marker>? cachedVisibleMarkers;
    private List<MarkerGroup>? cachedVisibleGroups;
    private bool cacheInvalidated = true;

    /// <summary>
    /// Reverse lookup: markerId → groupId.
    /// Built from all groups' MarkerIds lists at load time.
    /// </summary>
    private Dictionary<Guid, Guid> markerToGroupMap = [];

    public MarkerStorageService(string pluginConfigDir, IReliableFileStorage reliableFileStorage)
    {
        this.pluginConfigDir = pluginConfigDir;

        // Each store manages a subdirectory of the plugin config dir
        markerStore = new EntityFileStore<Marker>(
            reliableFileStorage,
            Path.Combine(pluginConfigDir, "markers"),
            m => m.Id,
            "Marker");

        groupStore = new EntityFileStore<MarkerGroup>(
            reliableFileStorage,
            Path.Combine(pluginConfigDir, "groups"),
            g => g.Id,
            "Group");

        templateStore = new EntityFileStore<MarkerTemplate>(
            reliableFileStorage,
            Path.Combine(pluginConfigDir, "templates"),
            t => t.Id,
            "Template");

        var migrated = MigrateFromLegacyIfNeeded();

        if (migrated)
        {
            // Caches were populated during migration via Save(), just build derived state
            RebuildMarkerToGroupMap();
            InvalidateCache();
        }
        else
        {
            // Normal startup: load all entities from their individual files on disk
            LoadAll();
        }
    }

    #region Query Methods

    /// <summary>
    /// Loads all markers, groups, and templates from their respective directories.
    /// </summary>
    private void LoadAll()
    {
        markerStore.LoadAll();
        groupStore.LoadAll();
        templateStore.LoadAll();
        RebuildMarkerToGroupMap();
        InvalidateCache();
    }

    /// <summary>
    /// Marks the current caches as stale.
    /// </summary>
    private void InvalidateCache()
    {
        cacheInvalidated = true;
        cachedVisibleMarkers = null;
        cachedVisibleGroups = null;
    }

    /// <summary>
    /// Rebuilds visibility caches if they've been invalidated.
    /// </summary>
    /// <remarks>
    /// Filters entities based on scope + current character hash:
    /// <para>
    /// - Shared entities: always visible
    /// </para>
    /// <para>
    /// - Personal entities: only visible when CharacterHash matches current
    /// </para>
    /// </remarks>
    private void RebuildCacheIfNeeded()
    {
        if (!cacheInvalidated && cachedVisibleMarkers != null && cachedVisibleGroups != null)
            return;

        cachedVisibleMarkers = markerStore.Items
            .Where(m => m.Scope == MarkerScope.Shared ||
                        (m.Scope == MarkerScope.Personal && m.CharacterHash == CurrentCharacterHash))
            .ToList();

        cachedVisibleGroups = groupStore.Items
            .Where(g => g.Scope == MarkerScope.Shared ||
                        (g.Scope == MarkerScope.Personal && g.CreatorHash == CurrentCharacterHash))
            .ToList();

        cacheInvalidated = false;
    }

    /// <summary>
    /// Rebuilds the map from all groups' MarkerIds lists.
    /// </summary>
    /// <remarks>
    /// Each marker can only be in one group — if a marker appears in 
    /// multiple groups' MarkerIds (shouldn't happen), the last one wins.
    /// </remarks>
    private void RebuildMarkerToGroupMap()
    {
        markerToGroupMap = [];
        foreach (var group in groupStore.Items)
        {
            foreach (var markerId in group.MarkerIds)
            {
                markerToGroupMap[markerId] = group.Id;
            }
        }
    }

    /// <summary>
    /// Returns all markers visible to the current session
    /// (shared + personal for the logged-in character).
    /// </summary>
    public List<Marker> GetVisibleMarkers()
    {
        RebuildCacheIfNeeded();
        return cachedVisibleMarkers!;
    }

    /// <summary>
    /// Returns all groups visible to the current session.
    /// </summary>
    public List<MarkerGroup> GetVisibleGroups()
    {
        RebuildCacheIfNeeded();
        return cachedVisibleGroups!;
    }

    /// <summary>
    /// Returns all templates for the current character (personal-only).
    /// </summary>
    public List<MarkerTemplate> GetTemplates()
    {
        return templateStore.Items
            .Where(t => t.CharacterHash == CurrentCharacterHash)
            .ToList();
    }

    /// <summary>
    /// Returns the total count of all loaded markers (across all characters/scopes).
    /// </summary>
    public int GetTotalMarkerCount() => markerStore.Items.Count;

    /// <summary>
    /// Gets the group ID that a marker belongs to, or null if ungrouped.
    /// </summary>
    public Guid? GetGroupIdForMarker(Guid markerId) =>
        markerToGroupMap.TryGetValue(markerId, out var groupId) ? groupId : null;

    /// <summary>
    /// Returns all visible markers that belong to a specific group.
    /// Filters from the visible markers cache.
    /// </summary>
    public List<Marker> GetMarkersInGroup(Guid groupId)
    {
        RebuildCacheIfNeeded();
        var group = groupStore.FindById(groupId);
        if (group == null) return [];

        // Use the group's MarkerIds to find matching visible markers.
        var memberIds = new HashSet<Guid>(group.MarkerIds);
        return cachedVisibleMarkers!.Where(m => memberIds.Contains(m.Id)).ToList();
    }

    /// <summary>
    /// Returns all visible markers that are not in any group.
    /// </summary>
    public List<Marker> GetUngroupedMarkers()
    {
        RebuildCacheIfNeeded();
        return cachedVisibleMarkers!.Where(m => !markerToGroupMap.ContainsKey(m.Id)).ToList();
    }

    #endregion
    #region Marker CRUD

    /// <summary>
    /// Saves a marker to disk (create or update) and updates the in-memory cache.
    /// </summary>
    public void SaveMarker(Marker marker)
    {
        markerStore.Save(marker);
        InvalidateCache();
    }

    /// <summary>
    /// Deletes a marker from disk and removes it from any group's MarkerIds.
    /// </summary>
    public void DeleteMarker(Guid markerId)
    {
        // Remove from any group that contains this marker
        var groupId = GetGroupIdForMarker(markerId);
        if (groupId.HasValue)
        {
            var group = groupStore.FindById(groupId.Value);
            if (group != null)
            {
                group.MarkerIds.Remove(markerId);
                groupStore.Save(group);
            }
        }

        // Remove from map
        markerToGroupMap.Remove(markerId);

        // Delete the marker file + remove from cache
        markerStore.Delete(markerId);
        InvalidateCache();
    }

    #endregion
    #region Group CRUD

    /// <summary>
    /// Saves a group to disk (create or update) and updates the in-memory cache.
    /// </summary>
    public void SaveGroup(MarkerGroup group)
    {
        groupStore.Save(group);
        // Rebuild the reverse map in case the MarkerIds list changed
        RebuildMarkerToGroupMap();
        InvalidateCache();
    }

    /// <summary>
    /// Deletes a group from disk. Does NOT delete the markers in the group. 
    /// The caller decides whether to delete child markers or keep them.
    /// </summary>
    public void DeleteGroup(Guid groupId)
    {
        // Remove all reverse-lookup entries pointing to this group
        var markerIdsToUngroup = markerToGroupMap
            .Where(kvp => kvp.Value == groupId)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var markerId in markerIdsToUngroup)
            markerToGroupMap.Remove(markerId);

        // Delete the group file
        groupStore.Delete(groupId);
        InvalidateCache();
    }

    #endregion

    #region Group Membership Management

    /// <summary>
    /// Adds a marker to a group's MarkerIds list and saves the group.
    /// If the marker is already in another group, it's removed from the old one first.
    /// </summary>
    public void AddMarkerToGroup(Guid markerId, Guid groupId)
    {
        // Remove from current group if any
        var currentGroupId = GetGroupIdForMarker(markerId);
        if (currentGroupId.HasValue && currentGroupId.Value != groupId)
            RemoveMarkerFromGroup(markerId, currentGroupId.Value);

        // Add to new group
        var group = groupStore.FindById(groupId);
        if (group == null)
        {
            Plugin.Log.Warning($"Cannot add marker to group '{groupId}': group not found.");
            return;
        }

        if (!group.MarkerIds.Contains(markerId))
        {
            group.MarkerIds.Add(markerId);
            groupStore.Save(group);
        }

        // Update reverse lookup
        markerToGroupMap[markerId] = groupId;
        InvalidateCache();
    }

    /// <summary>
    /// Removes a marker from a specific group's MarkerIds list and saves the group.
    /// </summary>
    public void RemoveMarkerFromGroup(Guid markerId, Guid groupId)
    {
        var group = groupStore.FindById(groupId);
        if (group == null) return;

        group.MarkerIds.Remove(markerId);
        groupStore.Save(group);
        markerToGroupMap.Remove(markerId);
        InvalidateCache();
    }

    /// <summary>
    /// Moves a marker from one group to another (or to/from ungrouped).
    /// Handles all combinations: ungrouped → group, group → ungrouped, group → group.
    /// </summary>
    /// <param name="markerId">The marker to move.</param>
    /// <param name="newGroupId">
    /// The target group ID, or null to make the marker ungrouped.
    /// </param>
    public void MoveMarkerToGroup(Guid markerId, Guid? newGroupId)
    {
        var currentGroupId = GetGroupIdForMarker(markerId);

        // No change needed
        if (currentGroupId == newGroupId)
            return;

        // Remove from old group (if any)
        if (currentGroupId.HasValue)
        {
            RemoveMarkerFromGroup(markerId, currentGroupId.Value);
        }

        // Add to new group (if any — null means ungrouped)
        if (newGroupId.HasValue)
        {
            AddMarkerToGroup(markerId, newGroupId.Value);
        }
    }

    #endregion
    #region Template CRUD

    /// <summary>
    /// Saves a template to disk (create or update).
    /// </summary>
    public void SaveTemplate(MarkerTemplate template)
    {
        templateStore.Save(template);
    }

    /// <summary>
    /// Deletes a template from disk.
    /// </summary>
    public void DeleteTemplate(Guid templateId)
    {
        templateStore.Delete(templateId);
    }

    #endregion

    #region Lookup Helpers

    /// <summary>
    /// Finds a visible group by exact name (case-insensitive).
    /// </summary>
    public MarkerGroup? FindGroupByName(string name)
    {
        return GetVisibleGroups()
            .FirstOrDefault(g => g.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Finds a visible group by its GUID.
    /// </summary>
    public MarkerGroup? FindGroupById(Guid id) => groupStore.FindById(id);

    /// <summary>
    /// Finds a template by its GUID.
    /// </summary>
    public MarkerTemplate? FindTemplateById(Guid id) => templateStore.FindById(id);

    /// <summary>
    /// Finds a template by exact name (case-insensitive), scoped to the current character.
    /// </summary>
    public MarkerTemplate? FindTemplateByName(string name)
    {
        return GetTemplates()
            .FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    #endregion

    #region Character Hash Management

    /// <summary>
    /// Computes and caches the character hash from the player's content ID.
    /// </summary>
    /// <remarks>
    /// Visibility caches are rebuilt to show/hide personal markers for the 
    /// newly logged-in character.
    /// </remarks>
    public void SetCharacterHash(ulong contentId)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(contentId.ToString()));
        CurrentCharacterHash = Convert.ToHexString(bytes)[..16];
        InvalidateCache();
        Plugin.Log.Debug("Character hash set for current character.");
    }

    /// <summary>
    /// Clears the character hash (e.g., on logout).
    /// After clearing, only shared entities will be visible.
    /// </summary>
    public void ClearCharacterHash()
    {
        CurrentCharacterHash = null;
        InvalidateCache();
    }

    #endregion

    #region Marker Management

    /// <summary>
    /// Gets the number of shared markers created by the current character.
    /// </summary>
    public int GetSharedCreatedMarkersCount()
    {
        RebuildCacheIfNeeded();
        return cachedVisibleMarkers!.Count(m =>
            m.CharacterHash == CurrentCharacterHash &&
            m.Scope == MarkerScope.Shared);
    }

    /// <summary>
    /// Deletes all shared markers created by the current character.
    /// Used by the "Erase All" function in ConfigWindow.
    /// </summary>
    public void EraseCreatedSharedMarkers()
    {
        if (CurrentCharacterHash == null)
        {
            Plugin.Log.Warning("Cannot erase created shared markers: no character hash set.");
            return;
        }

        var toDelete = markerStore.Items
            .Where(m => m.CharacterHash == CurrentCharacterHash && m.Scope == MarkerScope.Shared)
            .Select(m => m.Id)
            .ToList();

        foreach (var id in toDelete)
            DeleteMarker(id);
    }

    /// <summary>
    /// Deletes all personal markers for the current character.
    /// Used by the "Erase All" function in ConfigWindow.
    /// </summary>
    public void ErasePersonalMarkers()
    {
        if (CurrentCharacterHash == null)
        {
            Plugin.Log.Warning("Cannot erase personal markers: no character hash set.");
            return;
        }

        var toDelete = markerStore.Items
            .Where(m => m.CharacterHash == CurrentCharacterHash && m.Scope == MarkerScope.Personal)
            .Select(m => m.Id)
            .ToList();

        foreach (var id in toDelete)
            DeleteMarker(id);
    }

    #endregion

    #region Scope Management

    /// <summary>
    /// Changes a marker's scope and updates its CharacterHash accordingly.
    /// </summary>
    public void ChangeMarkerScope(Marker marker, MarkerScope newScope)
    {
        marker.Scope = newScope;
        marker.CharacterHash = CurrentCharacterHash;
        SaveMarker(marker);
    }

    /// <summary>
    /// Changes a group's scope and migrates all child markers to the new scope.
    /// </summary>
    public void ChangeGroupScope(MarkerGroup group, MarkerScope newScope)
    {
        group.Scope = newScope;
        group.CreatorHash = CurrentCharacterHash;
        SaveGroup(group);

        // Migrate all child markers to the same scope
        var childMarkers = GetMarkersInGroup(group.Id);
        foreach (var marker in childMarkers)
        {
            ChangeMarkerScope(marker, newScope);
        }
    }

    #endregion
    #region Migration

    /// <summary>
    /// Checks for and migrates legacy monolithic storage files to the new per-entity format.
    /// </summary>
    /// <returns>True if migration was performed, false if skipped.</returns>
    private bool MigrateFromLegacyIfNeeded()
    {
        var migrator = new LegacyStorageMigrator(pluginConfigDir, markerStore, groupStore);
        return migrator.MigrateIfNeeded();
    }
    #endregion
}
