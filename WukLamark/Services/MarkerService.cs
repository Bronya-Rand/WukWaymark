using FFXIVClientStructs.FFXIV.Client.Game;
using System;
using System.Collections.Generic;
using System.Text;
using WukLamark.Models;
using WukLamark.Utils;

namespace WukLamark.Services;

/// <summary>
/// Service class containing business logic for marker operations.
/// Handles marker creation, deletion, undo, and persistence.
/// </summary>
public class MarkerService(Configuration configuration, MarkerStorageService storageService)
{
    private readonly Configuration configuration = configuration;
    private readonly MarkerStorageService storageService = storageService;

    /// <summary>
    /// Undo buffer for recently deleted markers.
    /// </summary>
    private readonly LinkedList<Marker> deletedMarkers = new();

    /// <summary>Maximum number of deletions to remember for undo.</summary>
    private const int MaxUndoHistory = 10;

    /// <summary>Whether there are any deletions that can be undone.</summary>
    public bool CanUndo => deletedMarkers.Count > 0;

    /// <summary>Number of deletions available for undo.</summary>
    public int UndoCount => deletedMarkers.Count;

    /// <summary>
    /// Saves the player's current location as a new map marker.
    /// 
    /// This method:
    /// 1. Validates the player is logged in
    /// 2. Retrieves current location (position, territory, map, world)
    /// 3. Creates a marker with auto-generated name and color
    /// 4. Persists the marker to storage
    /// 5. Provides user feedback via chat message
    /// 
    /// Validation errors are reported to the player via error messages.
    /// </summary>
    /// <param name="group">Optional group to assign the marker to.</param>
    /// <param name="scope">The scope of the marker (Personal or Shared).</param>
    /// <returns>The created marker if successful, null if validation failed</returns>
    public unsafe Marker? SaveCurrentLocation(
        MarkerGroup? group = null,
        MarkerScope scope = MarkerScope.Personal,
        bool crossworld = false)
    {
        // Verify player is logged in
        if (!Plugin.ClientState.IsLoggedIn) return null;

        var player = Plugin.ObjectTable.LocalPlayer;
        if (player == null)
            return null;

        // Get current territory ID
        var territoryId = Plugin.ClientState.TerritoryType;
        if (territoryId == 0)
        {
            Plugin.ChatGui.PrintError("[WukLamark] Unable to determine current location.");
            return null;
        }

        var housingManager = HousingManager.Instance();
        if (housingManager == null)
        {
            Plugin.ChatGui.PrintError("[WukLamark] Unable to access housing manager for location data.");
            return null;
        }
        var wardId = housingManager->GetCurrentWard();

        // Look up the map ID from the territory data
        var mapId = Plugin.ClientState.MapId;
        if (mapId == 0)
        {
            Plugin.ChatGui.PrintError("[WukLamark] Unable to determine current map.");
            return null;
        }

        // Get current world ID (used to differentiate markers across data centers)
        var currentWorldId = player.CurrentWorld.RowId;
        if (currentWorldId == 0)
        {
            Plugin.ChatGui.PrintError("[WukLamark] Unable to determine current world.");
            return null;
        }

        // Create a new marker with current location data
        var totalCount = storageService.PersonalMarkers.Count + storageService.SharedMarkers.Count;
        var marker = new Marker
        {
            Position = player.Position,
            TerritoryId = territoryId,
            MapId = mapId,
            WorldId = currentWorldId,
            WardId = wardId,
            Name = $"Marker {totalCount + 1}",
            Color = Colors.GetNextColor(totalCount),
            Shape = configuration.DefaultWaymarkShape,
            CreatedAt = DateTime.Now,
            GroupId = group?.Id,
            Scope = scope,
            CharacterHash = storageService.CurrentCharacterHash, // Set creator for both personal and shared
            IsReadOnly = group?.IsReadOnly ?? false, // Use group's read-only status if available
            AppliesToAllWorlds = crossworld
        };

        // Persist to correct storage based on scope
        if (scope == MarkerScope.Shared)
        {
            storageService.SharedMarkers.Add(marker);
            storageService.SaveSharedMarkers();
        }
        else
        {
            storageService.PersonalMarkers.Add(marker);
            storageService.SavePersonalMarkers();
        }

        // Provide user feedback
        if (group != null)
        {
            Plugin.ChatGui.Print($"[WukLamark] Saved marker '{marker.Name}' at current location in group '{group.Name}'.");
        }
        else
        {
            Plugin.ChatGui.Print($"[WukLamark] Saved marker '{marker.Name}' at current location.");
        }
        Plugin.Log.Information($"Saved marker: {marker.Name} at {marker.Position} (Territory: {territoryId}, Map: {mapId}, Scope: {scope})");

        return marker;
    }

    /// <summary>
    /// Deletes a map marker, pushing it onto the undo stack first.
    /// </summary>
    /// <param name="marker">The marker to delete.</param>
    public void DeleteMarker(Marker marker)
    {
        // Validate permissions before allowing deletion
        if (marker.IsReadOnly)
        {
            Plugin.ChatGui.PrintError($"[WukLamark] Marker '{marker.Name}' is read-only and cannot be deleted.");
            return;
        }
        if (marker.Scope == MarkerScope.Personal && marker.CharacterHash != storageService.CurrentCharacterHash)
        {
            Plugin.ChatGui.PrintError($"[WukLamark] You do not have permission to delete marker '{marker.Name}'.");
            return;
        }

        // Push to undo buffer (LIFO - most recent at front)
        if (deletedMarkers.Count >= MaxUndoHistory)
        {
            // Remove oldest entry (at end)
            deletedMarkers.RemoveLast();
        }

        // Add newest entry at front
        deletedMarkers.AddFirst(marker);

        // Remove from whichever storage contains it
        var removedFromPersonal = storageService.PersonalMarkers.Remove(marker);
        var removedFromShared = storageService.SharedMarkers.Remove(marker);

        if (removedFromPersonal)
            storageService.SavePersonalMarkers();
        if (removedFromShared)
            storageService.SaveSharedMarkers();

        Plugin.Log.Information($"Deleted marker: {marker.Name} (undo available)");
    }

    /// <summary>
    /// Undoes the most recent map marker deletion, restoring the map marker.
    /// </summary>
    /// <returns>The restored map marker, or null if nothing to undo.</returns>
    public Marker? UndoDelete()
    {
        if (!CanUndo)
            return null;

        // Remove from front (most recent)
        var marker = deletedMarkers.First!.Value;
        deletedMarkers.RemoveFirst();

        if (marker.Scope == MarkerScope.Shared)
        {
            storageService.SharedMarkers.Add(marker);
            storageService.SaveSharedMarkers();
        }
        else
        {
            storageService.PersonalMarkers.Add(marker);
            storageService.SavePersonalMarkers();
        }

        Plugin.ChatGui.Print($"[WukLamark] Restored map marker '{marker.Name}'.");
        Plugin.Log.Information($"Undo delete: restored map marker '{marker.Name}' (Scope: {marker.Scope})");

        return marker;
    }

    /// <summary>
    /// Finds a group by name (case-insensitive).
    /// </summary>
    /// <param name="name">The group name to search for.</param>
    /// <returns>The matching group, or null if not found.</returns>
    public MarkerGroup? FindGroupByName(string name)
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
    /// Checks if the current user has permission to add a map marker to the specified group.
    /// </summary>
    public bool CanAddMarkerToGroup(MarkerGroup group)
    {
        if (group.Scope == MarkerScope.Personal) return true;
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
            var isCreator = group.CreatorHash != null && storageService.CurrentCharacterHash != null && group.CreatorHash == storageService.CurrentCharacterHash;
            var canEdit = group.Scope == MarkerScope.Personal || !group.IsReadOnly || isCreator;

            if (canEdit)
                output.AppendLine($"- {group.Name}");
        }

        return output.ToString().TrimEnd();
    }
}
