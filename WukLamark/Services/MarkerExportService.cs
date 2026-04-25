using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json;
using WukLamark.Models;

namespace WukLamark.Services;

/// <summary>
/// Legacy payload wrapper used for import/export operations.
/// Contains both markers and optional group metadata.
/// </summary>
internal sealed class LegacyMarkerExportPayload
{
    public string Version { get; set; } = "1";
    public List<Marker> Waymarks { get; set; } = [];
    public List<MarkerGroup> Groups { get; set; } = [];
}
internal sealed class LegacyShareEntryV1
{
    public Guid? SourceId { get; set; }
    public string Name { get; set; } = "Unnamed Location";
    public Vector3 Position { get; set; }
    public ushort TerritoryId { get; set; }
    public sbyte WardId { get; set; }
    public uint MapId { get; set; }
    public uint WorldId { get; set; }
    public bool AppliesToAllWorlds { get; set; }
    public uint? IconId { get; set; }
    public MarkerShape Shape { get; set; } = MarkerShape.Circle;
    public Vector4 Color { get; set; } = new(1.0f, 0.8f, 0.0f, 1.0f);
}
internal sealed class LegacySharePayloadV1
{
    public string Version { get; set; } = "1";
    public string Type { get; set; } = "Share";
    public List<LegacyShareEntryV1> Markers { get; set; } = [];
}

/// <summary>
/// Conflict information returned when an imported marker/group already exists in configuration.
/// </summary>
public class ImportConflict
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;

    // The name of the imported marker that caused the conflict 
    public string ImportedName { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
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
public sealed class MarkerExportService
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
                SourceId = marker.Id,
                Name = marker.Name,
                Position = marker.Position,
                TerritoryId = marker.TerritoryId,
                WardId = marker.WardId,
                MapId = marker.MapId,
                WorldId = marker.WorldId,
                Icon = new MarkerIcon
                {
                    GameIconId = marker.Icon.GameIconId,
                    Shape = marker.Icon.Shape,
                    Color = marker.Icon.Color,
                },
                AppliesToAllWorlds = marker.AppliesToAllWorlds
            });
        }
        var payload = new MarkerExportPayload
        {
            Markers = markersMinimal,
        };
        var json = JsonSerializer.Serialize(payload, JsonOptions);

        byte[] compressedData;
        using (var compressedStream = new MemoryStream())
        {
            using (var gzipStream = new GZipStream(compressedStream, CompressionLevel.Optimal))
            {
                var jsonBytes = Encoding.UTF8.GetBytes(json);
                gzipStream.Write(jsonBytes, 0, jsonBytes.Length);
            }
            compressedData = compressedStream.ToArray();
        }

        var b64 = Convert.ToBase64String(compressedData);
        ImGuiClipboard.Set(b64);
    }

    // ─── Import ──────────────────────────────────────────────────────────────
    private static bool TryDecodeBase64(string input, out string json)
    {
        try
        {
            var bytes = Convert.FromBase64String(input);

            // Check if the data is GZip compressed or not (V1/V2+)
            if (bytes.Length >= 2 && bytes[0] == 0x1F && bytes[1] == 0x8B)
            {
                using (var compressedStream = new MemoryStream(bytes))
                using (var gzipStream = new GZipStream(compressedStream, CompressionMode.Decompress))
                using (var decompressedStream = new MemoryStream())
                {
                    gzipStream.CopyTo(decompressedStream);
                    bytes = decompressedStream.ToArray();
                }
            }

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

        if (hasMarkers) return PayloadKind.Share;
        if (hasWaymarks) return PayloadKind.Legacy;

        return PayloadKind.Unknown;
    }
    private static Marker? FindDuplicate(MarkerShareEntry imported, List<Marker> existingMarkers)
    {
        if (imported.SourceId is Guid sourceId)
        {
            var match = existingMarkers.FirstOrDefault(m => m.Id == sourceId);
            if (match != null)
                return match;
        }

        return existingMarkers.FirstOrDefault(m =>
        m.Name == imported.Name &&
        m.TerritoryId == imported.TerritoryId &&
        m.WardId == imported.WardId &&
        m.MapId == imported.MapId &&
        m.AppliesToAllWorlds == imported.AppliesToAllWorlds &&
        (imported.AppliesToAllWorlds || m.WorldId == imported.WorldId) && // If applies to all worlds, ignore world ID in matching
        m.Icon.GameIconId == imported.Icon.GameIconId &&
        m.Icon.Shape == imported.Icon.Shape &&
        Vector3.DistanceSquared(m.Position, imported.Position) < 0.01f); // Allow small position differences  
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

    private static MarkerExportPayload? DeserializeSharePayload(string json)
    {
        try
        {
            // If current schema, return as-is
            var payload = JsonSerializer.Deserialize<MarkerExportPayload>(json, JsonOptions);
            if (payload != null && payload.Markers.Count > 0 && payload.Markers.All(m => m.Icon != null))
                return payload;

        }
        catch (JsonException)
        {
            // Try legacy schemas
            var payload = DeserializeSharePayloadCompat(json);
            if (payload != null)
                return payload;
        }
        return null;
    }

    private static MarkerExportPayload? DeserializeSharePayloadCompat(string json)
    {
        // V1 schema: Flat Icon Fields
        var legacyV1Payload = JsonSerializer.Deserialize<LegacySharePayloadV1>(json, JsonOptions);
        if (legacyV1Payload != null)
        {
            return new MarkerExportPayload
            {
                Version = legacyV1Payload.Version,
                Type = "Share",
                Markers = legacyV1Payload.Markers.Select(m => new MarkerShareEntry
                {
                    SourceId = m.SourceId,
                    Name = m.Name,
                    Position = m.Position,
                    TerritoryId = m.TerritoryId,
                    WardId = m.WardId,
                    MapId = m.MapId,
                    WorldId = m.WorldId,
                    AppliesToAllWorlds = m.AppliesToAllWorlds,
                    Icon = new MarkerIcon
                    {
                        GameIconId = m.IconId ?? 0, // Default to 0 if null
                        Shape = m.Shape,
                        Color = m.Color
                    }
                }).ToList()
            };
        }
        return null;
    }
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
                var payload = DeserializeSharePayload(json);
                if (payload == null)
                    return new ImportResult { Success = false, ErrorMessage = "Invalid or empty JSON data." };

                var conflicts = payload.Markers
                    .Select(imported =>
                    {
                        var existing = FindDuplicate(imported, existingMarkers);
                        if (existing == null) return null;

                        return new ImportConflict
                        {
                            Id = existing.Id,
                            Name = existing.Name,
                            ImportedName = imported.Name,
                            Reason = GetConflictReason(imported, existing)
                        };
                    })
                    .Where(c => c != null)
                    .DistinctBy(c => c!.Id) // Keep only one conflict per existing marker ID
                    .Select(c => c!)
                    .ToList();

                return new ImportResult { Success = true, Payload = payload, Conflicts = conflicts };
            case PayloadKind.Legacy:
                var legacyPayload = JsonSerializer.Deserialize<LegacyMarkerExportPayload>(json, JsonOptions);
                if (legacyPayload == null)
                    return new ImportResult { Success = false, ErrorMessage = "Invalid or empty JSON data." };

                // Detect conflicts (same GUID already in config)
                var existingMarkerIds = new HashSet<Guid>(existingMarkers.Select(w => w.Id));

                var legacyConflicts = legacyPayload.Waymarks
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
                        Icon = w.Icon,
                        AppliesToAllWorlds = w.AppliesToAllWorlds
                    }).ToList(),
                };

                return new ImportResult { Success = true, Payload = convertedPayload, Conflicts = legacyConflicts };
            default:
                return new ImportResult { Success = false, ErrorMessage = "Unrecognized payload format." };
        }
    }
    private static string GetConflictReason(MarkerShareEntry imported, Marker existing)
    {
        if (imported.SourceId is Guid sourceId && existing.Id == sourceId)
            return "Same marker ID";

        return "Same marker signature (name, position, location, settings)";
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
