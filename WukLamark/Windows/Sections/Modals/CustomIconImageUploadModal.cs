using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ImGuiFileDialog;
using System;
using System.Linq;
using System.Numerics;

namespace WukLamark.Windows.Sections.Modals
{
    public class CustomIconImageUploadModal
    {
        private bool isOpen = false;
        private bool shouldOpenFileDialog = false;
        private static readonly FileDialogManager FileDialogManager = new();

        public Action<string>? OnImageUpload { get; set; }

        public void Open()
        {
            isOpen = true;
            shouldOpenFileDialog = true;
        }
        public void Draw()
        {
            if (!isOpen) return;
            // Draw the file dialog
            FileDialogManager.Draw();

            var center = ImGui.GetMainViewport().GetCenter();
            ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));

            // Draw the modal window once
            if (shouldOpenFileDialog)
            {
                FileDialogManager.OpenFileDialog("Upload Custom Icon", "PNG File (.png){.png}",
                (selected, selectedPath) =>
                {
                    if (!selected)
                        return;

                    var imagePath = selectedPath.First();
                    OnImageUpload?.Invoke(imagePath);
                }, 1);
                shouldOpenFileDialog = false;
            }
        }
    }
}
