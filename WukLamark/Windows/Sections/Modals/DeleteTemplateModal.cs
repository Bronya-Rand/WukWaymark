using System;
using System.Collections.Generic;
using WukLamark.Models;

namespace WukLamark.Windows.Sections.Modals
{
    public sealed class DeleteTemplateModal
    {
        private readonly DeleteConfirmationModal<MarkerTemplate> confirmationModal = new("Delete Template?##WWDeleteTemplateModal")
        {
            SecondaryText = "This action cannot be undone.",
            NoValidTooltip = "No valid templates to delete."
        };

        public Action<List<MarkerTemplate>>? OnConfirmDelete
        {
            get => confirmationModal.OnConfirmDelete;
            set => confirmationModal.OnConfirmDelete = value;
        }

        public DeleteTemplateModal(Plugin plugin)
        {
            confirmationModal.CanDelete = (template, servicePlugin) =>
            {
                if (template.DefaultScope != MarkerScope.Personal) return true;
                if (template.CharacterHash == null || servicePlugin.MarkerStorageService.CurrentCharacterHash == null)
                    return false;

                return template.CharacterHash == servicePlugin.MarkerStorageService.CurrentCharacterHash;
            };

            confirmationModal.PrimaryText = templates => templates.Count == 1
                ? $"Are you sure you want to delete the template \"{templates[0].Name}\"?"
                : $"Are you sure you want to delete these {templates.Count} templates?";
        }

        public void Open(List<MarkerTemplate> templates) => confirmationModal.Open(templates);
        public void Draw(Plugin plugin) => confirmationModal.Draw(plugin);
    }
}
