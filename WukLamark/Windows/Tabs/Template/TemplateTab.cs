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

            deleteMarkerTemplateModal = new DeleteTemplateModal
            {
                OnConfirmDelete = templates =>
                {
                    foreach (var template in templates)
                    {
                        plugin.MarkerStorageService.DeleteTemplate(template.Id);
                    }
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
            if (template.Id == Guid.Empty)
            {
                var existing = plugin.Configuration.DefaultTemplate;
                var oldGroupId = existing.GroupId;

                existing.Name = result.Name;
                existing.DefaultIcon = result.Icon;
                existing.DefaultScope = result.Scope;
                existing.DefaultAppliesToAllWorlds = result.AppliesToAllWorlds;
                existing.GroupId = result.GroupId;

                if (oldGroupId != result.GroupId)
                    plugin.MarkerStorageService.UpdateMarkersTemplateGroup(existing);

                plugin.Configuration.Save();
            }
            else
            {
                var existing = plugin.MarkerStorageService.FindTemplateById(template.Id);
                if (existing != null)
                {
                    var oldGroupId = existing.GroupId;

                    existing.Name = result.Name;
                    existing.DefaultIcon = result.Icon;
                    existing.DefaultScope = result.Scope;
                    existing.DefaultAppliesToAllWorlds = result.AppliesToAllWorlds;
                    existing.GroupId = result.GroupId;

                    plugin.MarkerStorageService.SaveTemplate(existing);

                    if (oldGroupId != result.GroupId)
                        plugin.MarkerStorageService.UpdateMarkersTemplateGroup(existing);
                }
            }
        }
        private void HandleMarkerTemplateSave(MarkerTemplate template, bool isEditing)
        {
            Plugin.Log.Debug($"Saving template: {template.Name} (ID: {template.Id}) - IsEditing: {isEditing}");
            if (isEditing)
            {
                if (template.Id == Guid.Empty)
                {
                    var existing = plugin.Configuration.DefaultTemplate;
                    var oldGroupId = existing.GroupId;

                    existing.Name = template.Name;
                    existing.DefaultScope = template.DefaultScope;
                    existing.DefaultIcon = template.DefaultIcon;
                    existing.DefaultAppliesToAllWorlds = template.DefaultAppliesToAllWorlds;
                    existing.GroupId = template.GroupId;

                    // If group changed, update all markers using this template
                    if (oldGroupId != template.GroupId)
                    {
                        plugin.MarkerStorageService.UpdateMarkersTemplateGroup(template);
                    }
                    plugin.Configuration.Save();
                }
                else
                {
                    // Find the existing template
                    var existing = plugin.MarkerStorageService.FindTemplateById(template.Id);
                    if (existing != null)
                    {
                        var oldGroupId = existing.GroupId;

                        existing.Name = template.Name;
                        existing.DefaultScope = template.DefaultScope;
                        existing.DefaultIcon = template.DefaultIcon;
                        existing.DefaultAppliesToAllWorlds = template.DefaultAppliesToAllWorlds;
                        existing.GroupId = template.GroupId;

                        plugin.MarkerStorageService.SaveTemplate(existing);

                        // If group changed, update all markers using this template
                        if (oldGroupId != template.GroupId)
                        {
                            plugin.MarkerStorageService.UpdateMarkersTemplateGroup(template);
                        }
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
