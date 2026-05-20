using System;
using System.Collections.Generic;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using WukLamark.Models;
using WukLamark.Services;
using WukLamark.Windows.Components;
using WukLamark.Windows.Sections.Modals;

namespace WukLamark.Windows.Tabs.Template
{
    internal class TemplateTab
    {
        private readonly Plugin plugin;

        #region Components and Sections 
        private readonly TemplateTableComponent templateTableComponent;
        #endregion

        #region Modals
        private readonly TemplateEditor templateEditor;
        private readonly DeleteTemplateModal deleteMarkerTemplateModal;
        #endregion
        public TemplateTab(Plugin plugin, GameStateReaderService gameStateReaderService)
        {
            this.plugin = plugin;

            deleteMarkerTemplateModal = new DeleteTemplateModal(plugin)
            {
                OnConfirmDelete = templates =>
                {
                    foreach (var template in templates)
                        plugin.MarkerStorageService.DeleteTemplate(template.Id);
                }
            };

            templateEditor = new TemplateEditor(plugin)
            {
                OnSave = HandleMarkerTemplateSave
            };

            // Initialize components
            templateTableComponent = new TemplateTableComponent(plugin, gameStateReaderService)
            {
                OnDeleteRequested = templates =>
                {
                    deleteMarkerTemplateModal.Open(templates);
                },
                OnSaveRequested = HandleMarkerTemplatePopupSave,
                OnSaveLocationRequested = template =>
                {
                    plugin.MarkerService.SaveCurrentLocation(null, template.DefaultScope, template.DefaultAppliesToAllWorlds, template.Id, false);
                }
            };
        }
        public void Draw()
        {
            // Draw modals
            deleteMarkerTemplateModal.Draw(plugin);
            templateEditor.DrawAsModal();

            var templates = new List<MarkerTemplate> { plugin.Configuration.DefaultTemplate };
            templates.AddRange(plugin.MarkerStorageService.GetTemplates());

            DrawHeader();
            templateTableComponent.Draw(templates);
        }
        private void DrawHeader()
        {
            // "+ New Template" button
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Plus))
            {
                templateEditor.LoadFrom(null);
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Create New Template");
            }
        }
        private void HandleMarkerTemplatePopupSave(MarkerTemplate template, MarkerTemplateEditResult result)
        {
            var updatedTemplate = new MarkerTemplate
            {
                Id = template.Id,
                Name = result.Name,
                DefaultIcon = result.Icon,
                DefaultScope = result.Scope,
                DefaultAppliesToAllWorlds = result.AppliesToAllWorlds,
                GroupId = result.GroupId
            };

            HandleMarkerTemplateSave(updatedTemplate, true);
        }

        private void HandleMarkerTemplateSave(MarkerTemplate template, bool isEditing)
        {
            Plugin.Log.Debug($"Saving template: {template.Name} (ID: {template.Id}) - IsEditing: {isEditing}");
            if (isEditing)
            {
                var existing = template.Id == Guid.Empty
                    ? plugin.Configuration.DefaultTemplate
                    : plugin.MarkerStorageService.FindTemplateById(template.Id);

                if (existing != null)
                {
                    var oldGroupId = existing.GroupId;

                    existing.Name = template.Name;
                    existing.DefaultScope = template.DefaultScope;
                    existing.DefaultIcon = template.DefaultIcon;
                    existing.DefaultAppliesToAllWorlds = template.DefaultAppliesToAllWorlds;
                    existing.GroupId = template.GroupId;

                    if (template.Id == Guid.Empty)
                        plugin.Configuration.Save();
                    else
                        plugin.MarkerStorageService.SaveTemplate(existing);

                    // If group changed, update all markers using this template
                    if (oldGroupId != template.GroupId)
                    {
                        plugin.MarkerStorageService.UpdateMarkersTemplateGroup(existing);
                    }
                }
            }
            else
            {
                // Create new template
                var newTemplate = new MarkerTemplate
                {
                    Id = Guid.NewGuid(),
                    Name = template.Name,
                    CharacterHash = plugin.MarkerStorageService.CurrentCharacterHash,
                    DefaultIcon = template.DefaultIcon,
                    DefaultScope = template.DefaultScope,
                    DefaultAppliesToAllWorlds = template.DefaultAppliesToAllWorlds,
                    GroupId = template.GroupId
                };
                plugin.MarkerStorageService.SaveTemplate(newTemplate);
            }
        }
    }
}
