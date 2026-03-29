using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using System;
using System.Collections.Generic;
using System.Numerics;
using WukLamark.Services;

namespace WukLamark.Windows.Sections.Modals;

public class ImportConflictModal
{
    private bool isOpen = false;
    private ImportResult? pendingImport = null;
    private Dictionary<Guid, bool> importConflictChoices = [];

    public Action<ImportResult, Dictionary<Guid, bool>, bool>? OnApplyImport { get; set; }

    public void Open(ImportResult result)
    {
        pendingImport = result;
        importConflictChoices.Clear();
        isOpen = true;
    }

    public void Draw()
    {
        if (!isOpen || pendingImport == null) return;

        ImGui.OpenPopup("Import Conflicts##WWImportModal");

        var center = ImGui.GetMainViewport().GetCenter();
        ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new Vector2(480, 0), ImGuiCond.Always);

        using var importConflictModal = ImRaii.PopupModal("Import Conflicts##WWImportModal", ref isOpen, ImGuiWindowFlags.AlwaysAutoResize);
        if (importConflictModal)
        {
            ImGui.TextColored(new Vector4(1f, 0.8f, 0.2f, 1f), "Some items already exist in your collection.");
            ImGui.Text("Choose how to handle each conflict:");
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            // Conflict options
            foreach (var conflict in pendingImport.Conflicts)
            {
                var overwrite = importConflictChoices.TryGetValue(conflict.Id, out var v) && v;
                var label = conflict.IsGroup ? $" {conflict.Name} (Group)" : $" {conflict.Name} (Waymark)";
                if (ImGui.Checkbox($"Overwrite: {label}###import_{conflict.Id}", ref overwrite))
                    importConflictChoices[conflict.Id] = overwrite;
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            // Center buttons
            var buttonWidth = 120f;
            var spacing = 10f;
            var totalWidth = (buttonWidth * 3) + spacing;
            var windowWidth = ImGui.GetContentRegionAvail().X;
            var padding = (windowWidth - totalWidth) / 2;

            if (padding > 0)
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + padding);

            if (ImGui.Button("Overwrite All###ImportOverwriteAll", new Vector2(buttonWidth, 0)))
            {
                OnApplyImport?.Invoke(pendingImport, importConflictChoices, true);
                pendingImport = null;
                isOpen = false;
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Apply Choices###ImportApplySelection", new Vector2(buttonWidth, 0)))
            {
                OnApplyImport?.Invoke(pendingImport!, importConflictChoices, false);
                pendingImport = null;
                isOpen = false;
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel###ImportCancel", new Vector2(buttonWidth, 0)))
            {
                pendingImport = null;
                isOpen = false;
                ImGui.CloseCurrentPopup();
            }
        }
    }
}
