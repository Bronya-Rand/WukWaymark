using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using System;
using System.Numerics;
using WukLamark.Utils;

namespace WukLamark.Windows.Sections.Modals
{
    public class KTKExperimentalModal
    {
        private bool isOpen = false;

        public Action? OnConfirm { get; set; }
        public void Open()
        {
            isOpen = true;
        }
        public void Draw()
        {
            if (!isOpen) return;

            ImGui.OpenPopup("Enable Native/KamiToolkit?##WWKtkConfirmation");

            var center = ImGui.GetMainViewport().GetCenter();
            ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));

            using var enableKtkModal = ImRaii.PopupModal("Enable Native/KamiToolkit?##WWKtkConfirmation", ref isOpen, ImGuiWindowFlags.AlwaysAutoResize);
            if (enableKtkModal)
            {
                ImWuk.CenteredTextColored(new Vector4(1.0f, 0.4f, 0.4f, 1.0f), "Enabling Native/KamiToolkit is experimental and comes with a reduced feature set.");
                ImGui.TextWrapped("Enabling Native/KamiToolkit will:");
                ImGui.BulletText("Remove the ability to display markers on the map with a custom shape.");
                ImGui.BulletText("Remove the ability to display marker icons with a custom tint color.");
                ImGui.Spacing();
                ImWuk.CenteredText("Native/KamiToolkit support is only supported on the map. Minimap will remain rendered using ImGui.");

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                // Center buttons
                var buttonWidth = 120f;
                var spacing = 10f;
                var totalWidth = (buttonWidth * 2) + spacing;
                var windowWidth = ImGui.GetContentRegionAvail().X;
                var padding = (windowWidth - totalWidth) / 2;

                if (padding > 0)
                    ImGui.SetCursorPosX(ImGui.GetCursorPosX() + padding);

                if (ImGui.Button("Enable Anyway", new Vector2(buttonWidth, 0)))
                {
                    OnConfirm?.Invoke();
                    isOpen = false;
                    ImGui.CloseCurrentPopup();
                }
                ImGui.SameLine();
                if (ImGui.Button("Cancel", new Vector2(buttonWidth, 0)))
                {
                    isOpen = false;
                    ImGui.CloseCurrentPopup();
                }
            }
        }
    }
}
