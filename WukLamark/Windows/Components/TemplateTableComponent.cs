using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Utility;
using WukLamark.Models;
using WukLamark.Services;
using WukLamark.Utils;

namespace WukLamark.Windows.Components
{
    /// <summary>
    /// Displays a table of marker templates.
    /// </summary>
    internal class TemplateTableComponent
    {
        private readonly Plugin plugin;
        private readonly GameStateReaderService gameStateReaderService;

        // Separate editors for each hosting context to avoid state conflicts.
        private readonly TemplateEditor popupEditor;
        private readonly TemplateEditor modalEditor;

        #region Selection State
        private MarkerTemplate? pendingEditMarkerTemplate;
        private bool pendingEditPopupOpenRequested;

        private int selectionAnchorIndex = -1;
        public HashSet<Guid> SelectedMarkerTemplateIds { get; } = [];
        public bool IsMultiSelect => SelectedMarkerTemplateIds.Count > 1;
        #endregion
        public Action<MarkerTemplate, MarkerTemplateEditResult>? OnSaveRequested { get; set; }
        public Action<MarkerTemplate>? OnSaveLocationRequested { get; set; }
        public Action<List<MarkerTemplate>>? OnDeleteRequested { get; set; }
        public TemplateTableComponent(Plugin plugin, GameStateReaderService gameStateReaderService)
        {
            this.plugin = plugin;
            this.gameStateReaderService = gameStateReaderService;

            void HandleEditorSave(MarkerTemplate template, bool _)
            {
                if (OnSaveRequested == null) return;
                var result = new MarkerTemplateEditResult
                {
                    Name = template.Name,
                    GroupId = template.GroupId,
                    Scope = template.DefaultScope,
                    AppliesToAllWorlds = template.DefaultAppliesToAllWorlds,
                    Icon = template.DefaultIcon
                };
                OnSaveRequested.Invoke(template, result);
            }

            popupEditor = new TemplateEditor(plugin) { OnSave = HandleEditorSave };
            modalEditor = new TemplateEditor(plugin) { OnSave = HandleEditorSave };
        }
        public void Draw(List<MarkerTemplate> markerTemplates)
        {
            using var templateTableMode = ImRaii.Table("TemplateTable", 4,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY |
                ImGuiTableFlags.ScrollX | ImGuiTableFlags.Resizable);
            if (!templateTableMode) return;

            // Setup columns
            ImGui.TableSetupColumn("Marker", ImGuiTableColumnFlags.WidthFixed, 50);
            ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch, 200);
            ImGui.TableSetupColumn("Created", ImGuiTableColumnFlags.WidthFixed, 120);
            ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, 80);
            ImGui.TableHeadersRow();

            for (var i = 0; i < markerTemplates.Count; i++)
            {
                var template = markerTemplates[i];

                using (ImRaii.PushId(template.Id.ToString()))
                {
                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0);

                    var rowStart = ImGui.GetCursorScreenPos();
                    var rowHeight = Math.Max(ImGui.GetFrameHeight(), 20f * ImGuiHelpers.GlobalScale);

                    var isSelected = SelectedMarkerTemplateIds.Contains(template.Id);
                    var rowClicked = ImGui.Selectable($"##Trow_{template.Id}",
                        isSelected,
                        ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.AllowItemOverlap | ImGuiSelectableFlags.AllowDoubleClick,
                        new Vector2(0, rowHeight));

                    ImGui.SetItemAllowOverlap();

                    // Mouse interactions
                    if (ImGui.IsItemHovered())
                    {
                        // Double-click to edit via popup
                        if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                        {
                            GetPermissionForMarkerTemplate(template, out _, out var canEdit, out _);
                            if (canEdit)
                                HandleOnEditMarkerTemplate(template, isModal: false);
                        }
                        else if (ImGui.IsMouseReleased(ImGuiMouseButton.Right))
                        {
                            ImGui.OpenPopup($"TemplateContextMenu##{template.Id}");
                        }
                    }

                    // Context Menu
                    using (var popup = ImRaii.Popup($"TemplateContextMenu##{template.Id}"))
                    {
                        if (popup)
                            if (IsMultiSelect)
                                DrawMultiMarkerTemplateContextMenu(markerTemplates);
                            else
                                DrawMarkerTemplateContextMenu(template);
                    }

                    ImGui.SetCursorScreenPos(rowStart);

                    if (rowClicked)
                        HandleRowSelection(markerTemplates, i);

                    if (SelectedMarkerTemplateIds.Contains(template.Id) && IsMultiSelect)
                        ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(ImGuiCol.ButtonActive));

                    DrawMarkerColumn(template);
                    DrawNameColumn(template);
                    DrawCreatedColumn(template);
                    DrawActionColumn(template);
                }
            }

            // Popup editor
            // Must be drawn inside the table's window scope so ImGui can find the popup.
            if (pendingEditMarkerTemplate != null)
            {
                var popupId = $"EditTemplatePopup##{pendingEditMarkerTemplate.Id}";

                // Open the popup from this context and load
                // the template data into the editor.
                if (pendingEditPopupOpenRequested)
                {
                    popupEditor.LoadFrom(pendingEditMarkerTemplate);
                    ImGui.OpenPopup(popupId);
                    pendingEditPopupOpenRequested = false;
                }

                popupEditor.DrawAsPopup(popupId);

                if (!ImGui.IsPopupOpen(popupId))
                    pendingEditMarkerTemplate = null;
            }

            // Modal editor 
            modalEditor.DrawAsModal();
        }
        private static void DrawMarkerColumn(MarkerTemplate template)
        {
            ImGui.TableSetColumnIndex(0);

            var colorU32 = ImGui.ColorConvertFloat4ToU32(template.DefaultIcon.Color);
            var globalScale = ImGuiHelpers.GlobalScale;

            MarkerRenderer.RenderMarker(
                ImGui.GetWindowDrawList(),
                ImGui.GetCursorScreenPos() + new Vector2(20f * globalScale, 10f * globalScale),
                template.DefaultIcon.Shape,
                8f * globalScale,
                colorU32,
                template.DefaultIcon.GameIconId,
                template.DefaultIcon.CustomIconName,
                template.DefaultIcon.UseShapeColor
            );
            ImGui.Dummy(new Vector2(40 * globalScale, 20 * globalScale));
        }
        private void DrawNameColumn(MarkerTemplate template)
        {
            ImGui.TableSetColumnIndex(1);

            using (ImRaii.PushFont(UiBuilder.IconFont))
            {
                var icon = template.DefaultScope == MarkerScope.Personal ? FontAwesomeIcon.EyeSlash
                    : template.DefaultScope == MarkerScope.Shared ? FontAwesomeIcon.Users : FontAwesomeIcon.Question;
                ImGui.TextDisabled(icon.ToIconString());
            }
            if (ImGui.IsItemHovered())
            {
                var tooltip = template.DefaultScope == MarkerScope.Personal ? "Personal Template" :
                              template.DefaultScope == MarkerScope.Shared ? "Shared Template" :
                              "Unknown Scope Template";
                ImGui.SetTooltip(tooltip);
            }
            ImGui.SameLine();
            ImGui.Text(template.Name);

            var count = plugin.MarkerStorageService.GetTotalMarkersUsingTemplate(template.Id);
            if (count > 0)
            {
                ImGui.SameLine();
                ImGui.TextDisabled($"({count})");
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip($"{count} marker{(count > 1 ? "s" : "")} using this template.");
            }

        }
        private static void DrawCreatedColumn(MarkerTemplate marker)
        {
            ImGui.TableSetColumnIndex(2);
            ImGui.Text(marker.CreatedAt.ToString("yyyy-MM-dd HH:mm"));
        }

        private void DrawActionColumn(MarkerTemplate template)
        {
            var isLoggedIn = gameStateReaderService.IsLoggedIn;
            var inPvP = gameStateReaderService.IsInPvP;
            var inCombat = gameStateReaderService.IsInCombat;
            var markersDisabled = gameStateReaderService.DisableMarkerActions();

            var isCreator = template.CharacterHash != null &&
                plugin.MarkerStorageService.CurrentCharacterHash != null &&
                template.CharacterHash == plugin.MarkerStorageService.CurrentCharacterHash;
            var canAdd = template.DefaultScope == MarkerScope.Personal && isCreator ||
                         template.DefaultScope == MarkerScope.Shared;

            ImGui.TableSetColumnIndex(3);

            using (ImRaii.Disabled(!canAdd || markersDisabled))
            {
                if (ImGuiComponents.IconButton(FontAwesomeIcon.MapPin))
                    OnSaveLocationRequested?.Invoke(template);
            }
            if (ImWuk.IsItemHoveredWhenDisabled())
            {
                var tooltip = !isLoggedIn ? "Log in to save markers!" :
                    inPvP ? "Saving markers is disabled in PvP zones." :
                    inCombat ? "Saving markers is disabled in combat." :
                    !isCreator && template.DefaultScope == MarkerScope.Personal ? "Only the template's creator can save markers using this template." :
                    "Save current location with this template applied.";
                ImGui.SetTooltip(tooltip);
            }
        }
        private void DrawMarkerTemplateContextMenu(MarkerTemplate template)
        {
            if (IsMultiSelect) return;

            GetPermissionForMarkerTemplate(template, out var _, out var canEdit, out var canDelete);

            using (ImRaii.Disabled(!canEdit))
                if (ImGui.MenuItem("Edit Template"))
                    HandleOnEditMarkerTemplate(template, isModal: true);

            if (!canEdit)
            {
                var tooltip = GetEditMarkerTemplateTooltip(template);
                if (ImWuk.IsItemHoveredWhenDisabled() && !tooltip.IsNullOrEmpty())
                    ImGui.SetTooltip(tooltip);
            }

            using (ImRaii.Disabled(!canDelete))
            {
                if (ImGui.MenuItem("Delete Template"))
                {
                    var temp = new List<MarkerTemplate> { template };
                    OnDeleteRequested?.Invoke(temp);
                }
            }
            if (!canDelete)
            {
                var tooltip = GetDeleteMarkerTemplateTooltipText(template);
                if (ImWuk.IsItemHoveredWhenDisabled() && !tooltip.IsNullOrEmpty())
                    ImGui.SetTooltip(tooltip);
            }
        }
        private void DrawMultiMarkerTemplateContextMenu(List<MarkerTemplate> markerTemplates)
        {
            if (!IsMultiSelect) return;

            var selectedTemplates = markerTemplates.FindAll(t => SelectedMarkerTemplateIds.Contains(t.Id));

            var deletableCount = 0;
            foreach (var m in selectedTemplates)
            {
                if (CanDeleteMarkerTemplate(m))
                    deletableCount++;
            }

            var deleteLabel = deletableCount > 0 && deletableCount < selectedTemplates.Count
            ? $"Delete {deletableCount} of {selectedTemplates.Count} Selected Templates"
            : "Delete Selected Templates";

            using (ImRaii.Disabled(deletableCount == 0))
            {
                if (ImGui.MenuItem(deleteLabel))
                    OnDeleteRequested?.Invoke(selectedTemplates);
            }
            if (deletableCount == 0 && ImWuk.IsItemHoveredWhenDisabled())
                ImGui.SetTooltip("None of the selected templates can be deleted due to ownership or read-only restrictions.");
        }
        private void HandleRowSelection(List<MarkerTemplate> markerTemplates, int clickedIndex)
        {
            var io = ImGui.GetIO();
            var clickedId = markerTemplates[clickedIndex].Id;

            // Shift actions.
            if (io.KeyShift && selectionAnchorIndex >= 0)
            {
                SelectedMarkerTemplateIds.Clear();

                var start = Math.Min(selectionAnchorIndex, clickedIndex);
                var end = Math.Max(selectionAnchorIndex, clickedIndex);

                for (var i = start; i <= end; i++)
                    SelectedMarkerTemplateIds.Add(markerTemplates[i].Id);
                return;
            }

            // Ctrl actions
            if (io.KeyCtrl)
            {
                if (!SelectedMarkerTemplateIds.Remove(clickedId))
                    SelectedMarkerTemplateIds.Add(clickedId);

                selectionAnchorIndex = clickedIndex;
                return;
            }

            SelectedMarkerTemplateIds.Clear();
            SelectedMarkerTemplateIds.Add(clickedId);
            selectionAnchorIndex = clickedIndex;
        }
        private void HandleOnEditMarkerTemplate(MarkerTemplate template, bool isModal)
        {
            if (isModal)
                modalEditor.LoadFrom(template);
            else
            {
                // Defer OpenPopup to the draw loop
                pendingEditMarkerTemplate = template;
                pendingEditPopupOpenRequested = true;
            }
        }

        private void GetPermissionForMarkerTemplate(MarkerTemplate template, out bool isCreator, out bool canEdit, out bool canDelete)
        {
            var currentPlayerHash = plugin.MarkerStorageService.CurrentCharacterHash;

            isCreator = template.CharacterHash != null &&
                currentPlayerHash != null &&
                template.CharacterHash == currentPlayerHash;

            canEdit = (template.DefaultScope == MarkerScope.Shared) ||
                (template.DefaultScope == MarkerScope.Personal && isCreator);

            if (template.Id == Guid.Empty)
            {
                isCreator = true;
                canEdit = true;
            }

            canDelete = canEdit && template.Id != Guid.Empty; // Prevent deleting Default Template
        }
        private static string GetEditMarkerTemplateTooltip(MarkerTemplate template)
        {
            return template.DefaultScope switch
            {
                MarkerScope.Personal => "Only the creator can edit this template.",
                _ => ""
            };
        }
        private static string GetDeleteMarkerTemplateTooltipText(MarkerTemplate template)
        {
            if (template.Id == Guid.Empty) return "The default template cannot be deleted.";

            return template.DefaultScope switch
            {
                MarkerScope.Personal => "Only the creator can delete this template.",
                _ => ""
            };
        }
        private bool CanDeleteMarkerTemplate(MarkerTemplate template)
        {
            GetPermissionForMarkerTemplate(template, out _, out var _, out var canDelete);
            return canDelete;
        }
    }
}
