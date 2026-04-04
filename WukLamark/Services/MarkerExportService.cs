using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using WukLamark.Models;

namespace WukLamark.Services;

/// <summary>
/// Payload wrapper used for import/export operations.
/// Contains both markers and optional group metadata.
/// </summary>
public class MarkerExportPayload
{
    public string Version { get; set; } = "1";
    public List<Marker> Waymarks { get; set; } = [];
    public List<MarkerGroup> Groups { get; set; } = [];
}

/// <summary>
/// Conflict information returned when an imported marker/group already exists in configuration.
/// </summary>
public class ImportConflict
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public bool IsGroup { get; init; }
}

/// <summary>
/// Result returned from ImportFromClipboard / ImportFromFile.
/// </summary>
public class ImportResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public MarkerExportPayload? Payload { get; init; }
    public List<ImportConflict> Conflicts { get; init; } = [];
}

/// <summary>
/// Service responsible for serializing and deserializing markers + groups
/// for export to the clipboard or file system, and import back.
/// Format: JSON → UTF-8 → Base64 string (clipboard-safe).
/// </summary>
public class MarkerExportService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        IncludeFields = true // Required to properly serialize Vector3 and Vector4 fields (X, Y, Z, W)
    };

    // ─── Export ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Encodes a single marker into a Base64 JSON string and places
    /// it on the system clipboard.
    /// </summary>
    public static void ExportToClipboard(Marker marker)
    {
        // Clone and strip fields we don't want to export
        var exportMarker = new Marker
        {
            Id = marker.Id,
            Name = marker.Name,
            Position = marker.Position,
            TerritoryId = marker.TerritoryId,
            WardId = marker.WardId,
            MapId = marker.MapId,
            WorldId = marker.WorldId,
            Color = marker.Color,
            CreatedAt = marker.CreatedAt,
            Notes = marker.Notes,
            Shape = marker.Shape,
            IconId = marker.IconId,
            VisibilityRadius = marker.VisibilityRadius,
            AppliesToAllWorlds = marker.AppliesToAllWorlds,
            Scope = marker.Scope,
            GroupId = null,           // Omit
            CharacterHash = null      // Omit
        };

        var payload = new MarkerExportPayload
        {
            Waymarks = [exportMarker],
            Groups = []
        };
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        ImGuiClipboard.Set(b64);
    }

    // ─── Import ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Reads a Base64 JSON string from the clipboard and deserialises it into a payload.
    /// Identifies any conflicting IDs already present in <paramref name="existingMarkers"/>
    /// and <paramref name="existingGroups"/>.
    /// </summary>
    public static ImportResult ImportFromClipboard(
        List<Marker> existingMarkers,
        List<MarkerGroup> existingGroups)
    {
        try
        {
            var b64 = ImGuiClipboard.Get()?.Trim();
            if (string.IsNullOrEmpty(b64))
                return new ImportResult { Success = false, ErrorMessage = "Clipboard is empty." };

            return Deserialize(b64, existingMarkers, existingGroups);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Import from clipboard failed.");
            return new ImportResult { Success = false, ErrorMessage = $"Failed to read clipboard: {ex.Message}" };
        }
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static ImportResult Deserialize(
        string input,
        List<Marker> existingMarkers,
        List<MarkerGroup> existingGroups,
        bool base64 = true)
    {
        string json;
        if (base64)
        {
            try
            {
                var bytes = Convert.FromBase64String(input);
                json = Encoding.UTF8.GetString(bytes);
            }
            catch
            {
                // Might already be plain JSON — try it directly
                json = input;
            }
        }
        else
        {
            json = input;
        }

        var payload = JsonSerializer.Deserialize<MarkerExportPayload>(json, JsonOptions);
        if (payload == null)
            return new ImportResult { Success = false, ErrorMessage = "Invalid or empty export payload." };

        // Detect conflicts (same GUID already in config)
        var existingMarkerIds = new HashSet<Guid>(existingMarkers.Select(w => w.Id));
        var existingGroupIds = new HashSet<Guid>(existingGroups.Select(g => g.Id));

        var conflicts = new List<ImportConflict>();
        foreach (var w in payload.Waymarks)
            if (existingMarkerIds.Contains(w.Id))
                conflicts.Add(new ImportConflict { Id = w.Id, Name = w.Name, IsGroup = false });

        foreach (var g in payload.Groups)
            if (existingGroupIds.Contains(g.Id))
                conflicts.Add(new ImportConflict { Id = g.Id, Name = g.Name, IsGroup = true });

        return new ImportResult { Success = true, Payload = payload, Conflicts = conflicts };
    }
}

/// <summary>
/// Thin wrapper around ImGui clipboard API to keep the service testable.
/// </summary>
internal static class ImGuiClipboard
{
    public static void Set(string text) => Dalamud.Bindings.ImGui.ImGui.SetClipboardText(text);
    public static string? Get() => Dalamud.Bindings.ImGui.ImGui.GetClipboardText();
}
