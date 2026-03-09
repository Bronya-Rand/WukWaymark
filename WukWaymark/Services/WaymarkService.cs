using System;
using System.Collections.Generic;
using System.Text;
using WukWaymark.Models;
using WukWaymark.Utils;

namespace WukWaymark.Services;

/// <summary>
/// Service class containing business logic for waymark operations.
/// Handles waymark creation, deletion, undo, and persistence.
/// </summary>
public class WaymarkService(Configuration configuration, WaymarkStorageService storageService)
{
    private readonly Configuration configuration = configuration;
    private readonly WaymarkStorageService storageService = storageService;

    /// <summary>
    /// Undo buffer for recently deleted waymarks.
    /// </summary>
    private readonly LinkedList<Waymark> deletedWaymarks = new();

    /// <summary>Maximum number of deletions to remember for undo.</summary>
    private const int MaxUndoHistory = 10;

    /// <summary>Whether there are any deletions that can be undone.</summary>
    public bool CanUndo => deletedWaymarks.Count > 0;

    /// <summary>Number of deletions available for undo.</summary>
    public int UndoCount => deletedWaymarks.Count;

    /// <summary>
    /// Saves the player's current location as a new waymark.
    /// 
    /// This method:
    /// 1. Validates the player is logged in
    /// 2. Retrieves current location (position, territory, map, world)
    /// 3. Creates a waymark with auto-generated name and color
    /// 4. Persists the waymark to configuration
    /// 5. Provides user feedback via chat message
    /// 
    /// Validation errors are reported to the player via error messages.
    /// </summary>
    /// <param name="group">Optional group to assign the waymark to.</param>
    /// <param name="scope">The scope of the waymark (Personal or Shared).</param>
    /// <returns>The created waymark if successful, null if validation failed</returns>
    public Waymark? SaveCurrentLocation(WaymarkGroup? group = null, WaymarkScope scope = WaymarkScope.Personal)
    {
        // Verify player is logged in
        var player = Plugin.ObjectTable.LocalPlayer;
        if (player == null)
            return null;

        // Get current territory ID
        var territoryId = Plugin.ClientState.TerritoryType;
        if (territoryId == 0)
        {
            Plugin.ChatGui.PrintError("[WukWaymark] Unable to determine current location.");
            return null;
        }

        // Look up the map ID from the territory data
        var mapId = Plugin.ClientState.MapId;
        if (mapId == 0)
        {
            Plugin.ChatGui.PrintError("[WukWaymark] Unable to determine current map.");
            return null;
        }

        // Get current world ID (used to differentiate waymarks across data centers)
        var currentWorldId = Plugin.ObjectTable.LocalPlayer?.CurrentWorld.RowId ?? 0;
        if (currentWorldId == 0)
        {
            Plugin.ChatGui.PrintError("[WukWaymark] Unable to determine current world.");
            return null;
        }

        // Create a new waymark with current location data
        var totalCount = storageService.PersonalWaymarks.Count + storageService.SharedWaymarks.Count;
        var waymark = new Waymark
        {
            Position = player.Position,
            TerritoryId = territoryId,
            MapId = mapId,
            WorldId = currentWorldId,
            Name = $"Waymark {totalCount + 1}",
            Color = Colors.GetNextColor(totalCount),
            Shape = configuration.DefaultWaymarkShape,
            CreatedAt = DateTime.Now,
            GroupId = group?.Id,
            Scope = scope,
            CharacterHash = storageService.CurrentCharacterHash, // Set creator for both personal and shared
            IsReadOnly = group?.IsReadOnly ?? false // Use group's read-only status if available
        };

        // Persist to correct storage based on scope
        if (scope == WaymarkScope.Shared)
        {
            storageService.SharedWaymarks.Add(waymark);
            storageService.SaveSharedWaymarks();
        }
        else
        {
            storageService.PersonalWaymarks.Add(waymark);
            storageService.SavePersonalWaymarks();
        }

        // Provide user feedback
        if (group != null)
        {
            Plugin.ChatGui.Print($"[WukWaymark] Saved waymark '{waymark.Name}' at current location in group '{group.Name}'.");
        }
        else
        {
            Plugin.ChatGui.Print($"[WukWaymark] Saved waymark '{waymark.Name}' at current location.");
        }
        Plugin.Log.Information($"Saved waymark: {waymark.Name} at {waymark.Position} (Territory: {territoryId}, Map: {mapId}, Scope: {scope})");

        return waymark;
    }

    /// <summary>
    /// Deletes a waymark, pushing it onto the undo stack first.
    /// </summary>
    /// <param name="waymark">The waymark to delete.</param>
    public void DeleteWaymark(Waymark waymark)
    {
        // Validate permissions before allowing deletion
        if (waymark.IsReadOnly)
        {
            Plugin.ChatGui.PrintError($"[WukWaymark] Waymark '{waymark.Name}' is read-only and cannot be deleted.");
            return;
        }
        if (waymark.Scope == WaymarkScope.Personal && waymark.CharacterHash != storageService.CurrentCharacterHash)
        {
            Plugin.ChatGui.PrintError($"[WukWaymark] You do not have permission to delete waymark '{waymark.Name}'.");
            return;
        }

        // Push to undo buffer (LIFO - most recent at front)
        if (deletedWaymarks.Count >= MaxUndoHistory)
        {
            // Remove oldest entry (at end)
            deletedWaymarks.RemoveLast();
        }

        // Add newest entry at front
        deletedWaymarks.AddFirst(waymark);

        // Remove from whichever storage contains it
        var removedFromPersonal = storageService.PersonalWaymarks.Remove(waymark);
        var removedFromShared = storageService.SharedWaymarks.Remove(waymark);

        if (removedFromPersonal)
            storageService.SavePersonalWaymarks();
        if (removedFromShared)
            storageService.SaveSharedWaymarks();

        Plugin.Log.Information($"Deleted waymark: {waymark.Name} (undo available)");
    }

    /// <summary>
    /// Undoes the most recent waymark deletion, restoring the waymark.
    /// </summary>
    /// <returns>The restored waymark, or null if nothing to undo.</returns>
    public Waymark? UndoDelete()
    {
        if (!CanUndo)
            return null;

        // Remove from front (most recent)
        var waymark = deletedWaymarks.First!.Value;
        deletedWaymarks.RemoveFirst();

        if (waymark.Scope == WaymarkScope.Shared)
        {
            storageService.SharedWaymarks.Add(waymark);
            storageService.SaveSharedWaymarks();
        }
        else
        {
            storageService.PersonalWaymarks.Add(waymark);
            storageService.SavePersonalWaymarks();
        }

        Plugin.ChatGui.Print($"[WukWaymark] Restored waymark '{waymark.Name}'.");
        Plugin.Log.Information($"Undo delete: restored waymark '{waymark.Name}' (Scope: {waymark.Scope})");

        return waymark;
    }

    /// <summary>
    /// Finds a group by name (case-insensitive).
    /// </summary>
    /// <param name="name">The group name to search for.</param>
    /// <returns>The matching group, or null if not found.</returns>
    public WaymarkGroup? FindGroupByName(string name)
    {
        var allGroups = storageService.GetVisibleGroups();
        foreach (var group in allGroups)
        {
            if (group.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return group;
        }
        return null;
    }

    /// <summary>
    /// Checks if the current user has permission to add a waymark to the specified group.
    /// </summary>
    public bool CanAddWaymarkToGroup(WaymarkGroup group)
    {
        if (group.Scope == WaymarkScope.Personal) return true;
        if (!group.IsReadOnly) return true;

        var currentHash = storageService.CurrentCharacterHash;
        return group.CreatorHash != null && currentHash != null && group.CreatorHash == currentHash;
    }

    /// <summary>
    /// Returns a list of all group names that the user can append to, for error messages.
    /// </summary>
    public string GetGroupNamesList()
    {
        var allGroups = storageService.GetVisibleGroups();

        if (allGroups.Count == 0)
            return "(no available groups exist)";

        var output = new StringBuilder();
        // Format as a bullet list with each name on a new line
        foreach (var group in allGroups)
        {
            if (group.IsReadOnly) continue;
            output.AppendLine($"- {group.Name}");
        }

        return output.ToString().TrimEnd();
    }
}
