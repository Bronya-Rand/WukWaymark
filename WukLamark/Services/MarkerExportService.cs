using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using WukLamark.Models;

namespace WukLamark.Services;

/// <summary>
/// Legacy payload wrapper used for import/export operations.
/// Contains both markers and optional group metadata.
/// </summary>
public class LegacyMarkerExportPayload
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
    private enum PayloadKind
    {
        Unknown,
        Legacy,
        Share
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        IncludeFields = true // Required to properly serialize Vector3 and Vector4 fields (X, Y, Z, W)
    };

    // ─── Export ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Encodes markers into a Base64 JSON string and places
    /// it on the system clipboard.
    /// </summary>
    public static void ExportShareToClipboard(List<Marker> markers)
    {
        var markersMinimal = new List<MarkerShareEntry>(markers.Count);
        foreach (var marker in markers)
        {
            markersMinimal.Add(new MarkerShareEntry
            {
                Name = marker.Name,
                Position = marker.Position,
                TerritoryId = marker.TerritoryId,
                WardId = marker.WardId,
                MapId = marker.MapId,
                WorldId = marker.WorldId,
                Color = marker.Color,
                Shape = marker.Shape,
                IconId = marker.IconId,
                AppliesToAllWorlds = marker.AppliesToAllWorlds
            });
        }
        var payload = new MarkerExportPayload
        {
            Markers = markersMinimal,
        };
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        ImGuiClipboard.Set(b64);
    }

    // ─── Import ──────────────────────────────────────────────────────────────
    private static bool TryDecodeBase64(string input, out string json)
    {
        try
        {
            var bytes = Convert.FromBase64String(input);
            json = Encoding.UTF8.GetString(bytes);
            return true;
        }
        catch
        {
            json = input; // Might already be plain JSON
            return false;
        }
    }
    private static PayloadKind DetectPayloadKind(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
            return PayloadKind.Unknown;

        var root = doc.RootElement;

        var hasMarkers = root.TryGetProperty("Markers", out _);
        var hasWaymarks = root.TryGetProperty("Waymarks", out _);
        var hasType = root.TryGetProperty("Type", out _);

        if (hasMarkers && hasType && !hasWaymarks)
            return PayloadKind.Share;
        if (hasWaymarks && !hasType && !hasMarkers)
            return PayloadKind.Legacy;
        return PayloadKind.Unknown;
    }

    /// <summary>
    /// Reads a Base64 JSON string from the clipboard and deserialises it into a payload.
    /// Identifies any conflicting IDs already present in <paramref name="existingMarkers"/>.
    /// </summary>
    public static ImportResult ImportFromClipboard(
        List<Marker> existingMarkers)
    {
        try
        {
            var b64 = ImGuiClipboard.Get()?.Trim();
            if (string.IsNullOrEmpty(b64))
                return new ImportResult { Success = false, ErrorMessage = "Clipboard is empty." };

            return Deserialize(b64, existingMarkers);
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
        bool base64 = true)
    {
        var json = base64 ? (TryDecodeBase64(input, out var decodedJson) ? decodedJson : input) : input;

        PayloadKind kind;
        try
        {
            kind = DetectPayloadKind(json);

        }
        catch (Exception ex)
        {
            return new ImportResult { Success = false, ErrorMessage = $"Invalid JSON format: {ex.Message}" };
        }

        switch (kind)
        {
            case PayloadKind.Share:
                var payload = JsonSerializer.Deserialize<MarkerExportPayload>(json, JsonOptions);
                if (payload == null)
                    return new ImportResult { Success = false, ErrorMessage = "Invalid or empty JSON data." };

                return new ImportResult { Success = true, Payload = payload };
            case PayloadKind.Legacy:
                var legacyPayload = JsonSerializer.Deserialize<LegacyMarkerExportPayload>(json, JsonOptions);
                if (legacyPayload == null)
                    return new ImportResult { Success = false, ErrorMessage = "Invalid or empty JSON data." };

                // Detect conflicts (same GUID already in config)
                var existingMarkerIds = new HashSet<Guid>(existingMarkers.Select(w => w.Id));

                var conflicts = legacyPayload.Waymarks
                    .Where(w => existingMarkerIds.Contains(w.Id))
                    .Select(w => new ImportConflict { Id = w.Id, Name = w.Name })
                    .ToList();

                var convertedPayload = new MarkerExportPayload
                {
                    Version = legacyPayload.Version,
                    Markers = legacyPayload.Waymarks.Select(w => new MarkerShareEntry
                    {
                        Name = w.Name,
                        Position = w.Position,
                        TerritoryId = w.TerritoryId,
                        WardId = w.WardId,
                        MapId = w.MapId,
                        WorldId = w.WorldId,
                        Color = w.Color,
                        Shape = w.Shape,
                        IconId = w.IconId,
                        AppliesToAllWorlds = w.AppliesToAllWorlds
                    }).ToList(),
                };

                return new ImportResult { Success = true, Payload = convertedPayload, Conflicts = conflicts };
            default:
                return new ImportResult { Success = false, ErrorMessage = "Unrecognized payload format." };
        }
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
