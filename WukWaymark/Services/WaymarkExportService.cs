using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using WukWaymark.Models;

namespace WukWaymark.Services;

/// <summary>
/// Payload wrapper used for import/export operations.
/// Contains both waymarks and optional group metadata.
/// </summary>
public class WaymarkExportPayload
{
    public string Version { get; set; } = "1";
    public List<Waymark> Waymarks { get; set; } = [];
    public List<WaymarkGroup> Groups { get; set; } = [];
}

/// <summary>
/// Conflict information returned when an imported waymark/group already exists in configuration.
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
    public WaymarkExportPayload? Payload { get; init; }
    public List<ImportConflict> Conflicts { get; init; } = [];
}

/// <summary>
/// Service responsible for serializing and deserializing waymarks + groups
/// for export to the clipboard or file system, and import back.
/// Format: JSON → UTF-8 → Base64 string (clipboard-safe).
/// </summary>
public class WaymarkExportService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        IncludeFields = true // Required to properly serialize Vector3 and Vector4 fields (X, Y, Z, W)
    };

    // ─── Export ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Encodes a single waymark into a Base64 JSON string and places
    /// it on the system clipboard.
    /// </summary>
    public static void ExportToClipboard(Waymark waymark)
    {
        // Clone and strip fields we don't want to export
        var exportWaymark = new Waymark
        {
            Id = waymark.Id,
            Name = waymark.Name,
            Position = waymark.Position,
            TerritoryId = waymark.TerritoryId,
            MapId = waymark.MapId,
            WorldId = waymark.WorldId,
            Color = waymark.Color,
            CreatedAt = waymark.CreatedAt,
            Notes = waymark.Notes,
            Shape = waymark.Shape,
            IconId = waymark.IconId,
            VisibilityRadius = waymark.VisibilityRadius,
            Scope = waymark.Scope,
            GroupId = null,           // Omit
            CharacterHash = null      // Omit
        };

        var payload = new WaymarkExportPayload
        {
            Waymarks = [exportWaymark],
            Groups = []
        };
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        ImGuiClipboard.Set(b64);
    }

    // ─── Import ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Reads a Base64 JSON string from the clipboard and deserialises it into a payload.
    /// Identifies any conflicting IDs already present in <paramref name="existingWaymarks"/>
    /// and <paramref name="existingGroups"/>.
    /// </summary>
    public static ImportResult ImportFromClipboard(
        List<Waymark> existingWaymarks,
        List<WaymarkGroup> existingGroups)
    {
        try
        {
            var b64 = ImGuiClipboard.Get()?.Trim();
            if (string.IsNullOrEmpty(b64))
                return new ImportResult { Success = false, ErrorMessage = "Clipboard is empty." };

            return Deserialize(b64, existingWaymarks, existingGroups);
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
        List<Waymark> existingWaymarks,
        List<WaymarkGroup> existingGroups,
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

        var payload = JsonSerializer.Deserialize<WaymarkExportPayload>(json, JsonOptions);
        if (payload == null)
            return new ImportResult { Success = false, ErrorMessage = "Invalid or empty export payload." };

        // Detect conflicts (same GUID already in config)
        var existingWaymarkIds = new HashSet<Guid>(existingWaymarks.Select(w => w.Id));
        var existingGroupIds = new HashSet<Guid>(existingGroups.Select(g => g.Id));

        var conflicts = new List<ImportConflict>();
        foreach (var w in payload.Waymarks)
            if (existingWaymarkIds.Contains(w.Id))
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
