using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace WukLamark.Helpers
{
    /// <summary>
    /// Helper class providing convenient methods for interacting with the in-game map system.
    /// </summary>
    internal class MapHelper
    {
        private static readonly int TOOLTIP_NOTE_MAX_LINES = 2;
        private static readonly int TOOLTIP_NOTE_MAX_LINE_LENGTH = 40;
        private const string ELLIPSIS = "...";

        /// <summary>
        /// Opens the map corresponding to the specified map identifier.
        /// This displays the full area map for the given map, centered on the player.
        /// </summary>
        /// <param name="mapId">The unique identifier of the map to open (from TerritoryType.Map.RowId)</param>
        public static unsafe void OpenMap(uint mapId) => AgentMap.Instance()->OpenMapByMapId(mapId);

        /// <summary>
        /// Flags a specific map location and opens the map at that position.
        /// 
        /// This creates a temporary flag marker on the map at the specified coordinates
        /// and opens the map UI centered on that location.
        /// </summary>
        /// <param name="position">The world coordinates of the location to flag (X, Y, Z)</param>
        /// <param name="title">Optional custom title to display on the flagged location. If null, no title is shown.</param>
        public static unsafe void FlagMapLocation(Vector3 position, uint territoryId, uint mapId, string? title = null)
        {
            var agent = AgentMap.Instance();
            agent->SetFlagMapMarker(territoryId, mapId, position);
            agent->OpenMap(mapId, territoryId, title, MapType.FlagMarker);
        }
        public static string FormatMapTooltipNotes(string? markerNotes)
        {
            if (markerNotes.IsNullOrEmpty()) return string.Empty;

            var words = markerNotes.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) return string.Empty;

            var wrappedLines = WrapWords(words);
            if (wrappedLines.Count <= TOOLTIP_NOTE_MAX_LINES)
                return string.Join("\n", wrappedLines);

            var visibleLines = wrappedLines.GetRange(0, TOOLTIP_NOTE_MAX_LINES);
            var lastLine = visibleLines[^1];

            var maxLastLineLength = TOOLTIP_NOTE_MAX_LINE_LENGTH - ELLIPSIS.Length;
            if (lastLine.Length > maxLastLineLength)
                lastLine = lastLine[..maxLastLineLength];

            visibleLines[^1] = $"{lastLine.TrimEnd()}{ELLIPSIS}";
            return string.Join("\n", visibleLines);
        }
        private static List<string> WrapWords(string[] words)
        {
            var lines = new List<string>();
            var currentLine = new StringBuilder();

            foreach (var word in words)
            {
                if (currentLine.Length == 0)
                {
                    if (word.Length <= TOOLTIP_NOTE_MAX_LINE_LENGTH)
                        currentLine.Append(word);
                    else
                    {
                        // Hard-break long words
                        var index = 0;
                        while (index < word.Length)
                        {
                            var len = Math.Min(TOOLTIP_NOTE_MAX_LINE_LENGTH, word.Length - index);
                            lines.Add(word.Substring(index, len));
                            index += len;
                        }
                    }
                    continue;
                }

                if (currentLine.Length + 1 + word.Length <= TOOLTIP_NOTE_MAX_LINE_LENGTH)
                    currentLine.Append(' ').Append(word);
                else
                {
                    lines.Add(currentLine.ToString());
                    currentLine.Clear();

                    if (word.Length <= TOOLTIP_NOTE_MAX_LINE_LENGTH)
                        currentLine.Append(word);
                    else
                    {
                        var index = 0;
                        while (index < word.Length)
                        {
                            var len = Math.Min(TOOLTIP_NOTE_MAX_LINE_LENGTH, word.Length - index);
                            lines.Add(word.Substring(index, len));
                            index += len;
                        }
                    }
                }
            }

            if (currentLine.Length > 0)
                lines.Add(currentLine.ToString());

            return lines;
        }
    }
}
