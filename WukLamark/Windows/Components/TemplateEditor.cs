using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Utility;
using WukLamark.Models;
using WukLamark.Utils;

namespace WukLamark.Windows.Components;

/// <summary>
/// Unified template editor that can render as either an inline popup
/// or a centered modal dialog (context menu / "Create" button).
/// </summary>
/// <remarks>
/// <para>
/// <b>Popup mode:</b> The <i>caller</i> must call <see cref="ImGui.OpenPopup"/> with the
/// popup ID returned by <see cref="DrawAsPopup"/> because ImGui requires OpenPopup to be
/// invoked from the same window/ID-stack context where the popup is drawn.
/// </para>
/// <para>
/// <b>Modal mode:</b> The editor owns the <see cref="ImGui.OpenPopup"/> call.
/// </para>
/// </remarks>
internal sealed class TemplateEditor
{
    private readonly Plugin plugin;
    private readonly IconEditFields iconEditFields;

    #region Editing State

    private MarkerTemplate? editingTemplate;
    private bool isOpen;

    private string editingName = string.Empty;
    private Guid? editingGroupId;
    private MarkerScope editingScope = MarkerScope.Personal;
    private bool editingAppliesToAllWorlds;

    #endregion

    public Action<MarkerTemplate, bool>? OnSave { get; set; }

    public TemplateEditor(Plugin plugin)
    {
        this.plugin = plugin;
        iconEditFields = new IconEditFields(plugin);
    }

    /// <summary>
    /// Loads editing state from an existing template (edit mode) or resets to defaults (create mode).
    /// </summary>
    public void LoadFrom(MarkerTemplate? existingTemplate)
    {
        editingTemplate = existingTemplate;
        isOpen = true;

        if (existingTemplate != null)
        {
            editingName = existingTemplate.Name;
            editingGroupId = existingTemplate.GroupId;
            editingScope = existingTemplate.DefaultScope;
            editingAppliesToAllWorlds = existingTemplate.DefaultAppliesToAllWorlds;
            iconEditFields.LoadFrom(existingTemplate.DefaultIcon);
        }
        else
        {
            editingName = string.Empty;
            editingGroupId = null;
            editingScope = MarkerScope.Personal;
            editingAppliesToAllWorlds = false;
            iconEditFields.LoadFrom(new MarkerIcon
            {
                SourceType = MarkerIconType.Shape,
                Shape = MarkerShape.Circle,
                Color = Vector4.One,
                Size = 0.0f,
                UseShapeColor = false,
                VisibilityRadius = 0.0f,
                GameIconId = null,
                CustomIconName = null,
            });
        }
    }

    /// <summary>
    /// Renders the editor as an inline popup. The <b>caller</b> is responsible for calling
    /// <c>ImGui.OpenPopup(popupId)</c> from the correct ID-stack context.
    /// </summary>
    /// <param name="popupId">
    /// The popup ID string. Should be unique per template, e.g. <c>$"EditTemplatePopup##{template.Id}"</c>.
    /// </param>
    public void DrawAsPopup(string popupId)
    {
        if (!isOpen) return;

        using (var popup = ImRaii.Popup(popupId))
        {
            if (popup)
                DrawContent();
        }
    }

    /// <summary>
    /// Renders the editor as a centered modal dialog. The editor manages its own
    /// <see cref="ImGui.OpenPopup"/> call internally.
    /// </summary>
    public void DrawAsModal()
    {
        if (!isOpen) return;

        // Block editing of someone else's personal template
        if (editingTemplate != null)
        {
            var isCreator = editingTemplate.CharacterHash == plugin.MarkerStorageService.CurrentCharacterHash;
            if (editingTemplate.DefaultScope == MarkerScope.Personal && !isCreator)
            {
                Plugin.Log.Warning("Attempted to edit someone else's personal template. Action blocked.");
                Close();
                return;
            }
        }

        var isEditing = editingTemplate != null;
        var modalTitle = isEditing ? "Edit Template" : "Create Template";
        var modalId = $"{modalTitle}##WWTemplateEditorModal";

        ImGui.OpenPopup(modalId);

        var center = ImGui.GetMainViewport().GetCenter();
        ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));

        using var modal = ImRaii.PopupModal(modalId, ref isOpen, ImGuiWindowFlags.AlwaysAutoResize);
        if (modal)
            DrawContent();
    }

    /// <summary>
    /// Shared form content drawn inside either a popup or a modal.
    /// </summary>
    private void DrawContent()
    {
        var currentHash = plugin.MarkerStorageService.CurrentCharacterHash;
        var isCreator = editingTemplate != null && (editingTemplate.CharacterHash == currentHash || editingTemplate.Id == Guid.Empty);
        var isEditing = editingTemplate != null;
        var hasName = !editingName.IsNullOrEmpty();

        var canSave = (isEditing && editingTemplate!.DefaultScope == MarkerScope.Shared && hasName) ||
                      (isEditing && editingTemplate!.DefaultScope == MarkerScope.Personal && isCreator && hasName) ||
                      (!isEditing && hasName);

        // Name
        IconEditFields.DrawNameField("Template", ref editingName);

        // Icon fields (type, color, size, shape, icon picker, visibility radius)
        iconEditFields.Draw("Template", isEditing ? editingTemplate!.Name : "New Template");

        // Group picker
        var groups = plugin.MarkerStorageService.GetVisibleGroupsByScope(editingScope);
        editingGroupId = IconEditFields.DrawGroupPicker("Template", editingGroupId, groups, currentHash);

        // Scope picker
        // Excluded for the default template (always shared)
        if ((editingTemplate != null && editingTemplate.Id != Guid.Empty) || editingTemplate == null)
        {
            var scopeTooltip = !isCreator
                ? "Only the creator can change scope."
                : "Sets the visibility of the marker to other characters on the same PC.\nPersonal markers are only visible to you, while shared markers are visible to any character that logs in to FFXIV from this PC.";
            editingScope = IconEditFields.DrawScopePicker("Template", editingScope, false, scopeTooltip);
        }

        // Crossworld 
        ImGui.Checkbox("Visible Crossworld###TemplateAllWorlds", ref editingAppliesToAllWorlds);
        if (ImWuk.IsItemHoveredWhenDisabled())
            ImGui.SetTooltip("When enabled, this marker appears on matching maps in all worlds/data centers.");

        ImGui.Spacing();

        //  Save / Cancel 
        var buttonLabel = isEditing ? "Save" : "Create";
        using (ImRaii.Disabled(!canSave))
        {
            if (ImGui.Button($"{buttonLabel}###TemplateSaveButton"))
            {
                if (!string.IsNullOrWhiteSpace(editingName))
                {
                    OnSave?.Invoke(new MarkerTemplate
                    {
                        Id = editingTemplate?.Id ?? Guid.NewGuid(),
                        Name = editingName,
                        CharacterHash = editingTemplate?.CharacterHash ?? plugin.MarkerStorageService.CurrentCharacterHash,
                        DefaultScope = editingScope,
                        GroupId = editingGroupId,
                        DefaultAppliesToAllWorlds = editingAppliesToAllWorlds,
                        DefaultIcon = iconEditFields.ToMarkerIcon(),
                    }, isEditing);

                    Close();
                    ImGui.CloseCurrentPopup();
                }
            }
        }

        ImGui.SameLine();

        if (ImGui.Button("Cancel###TemplateCancelButton"))
        {
            Close();
            ImGui.CloseCurrentPopup();
        }
    }

    private void Close()
    {
        editingTemplate = null;
        isOpen = false;
    }
}
