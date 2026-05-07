using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit;
using KamiToolKit.Nodes;
using KamiToolKit.Premade.Node;

namespace WukLamark.Windows.Native;

public sealed class WukLamarkConfigAddon : NativeAddon
{
    private readonly Plugin plugin;
    private readonly Configuration configuration;
    
    private const float itemSpacing = 5.0f;

    [SetsRequiredMembers]
    public WukLamarkConfigAddon(Plugin plugin)
    {
        Title = "WukLamark Settings";
        InternalName = "WukLamarkNativeSettings";
        
        this.plugin = plugin;
        configuration = plugin.Configuration;
    }
    protected override unsafe void OnSetup(AtkUnitBase* addon, Span<AtkValue> atkValueSpan)
    {
        base.OnSetup(addon, atkValueSpan);
        
        SetWindowSize(WindowConstants.ConfigWindowMaxSize);
        
        AddNode(new VerticalListNode
        {
            Size = ContentSize,
            Position = ContentStartPosition,
            FitWidth = true,
            InitialNodes = AddBaseInitialNodes()
        });
    }

    private ICollection<NodeBase> AddBaseInitialNodes()
    {
        ICollection<NodeBase> nodes = [];

        #region Map Marker Display Settings
        
        var markerDisplayCategory = new CategoryTextNode
        {
            String = "Map Marker Display Settings"
        };
        nodes.Add(markerDisplayCategory);

        var markerMapEnableCheckbox = new CheckboxNode
        {
            Size = new Vector2(18f, 18f),
            String = "Enable Marker Display on Map",
            IsChecked = configuration.WaymarksMapEnabled,
            OnClick = newValue =>
            {
                configuration.WaymarksMapEnabled = newValue;
                configuration.Save();
            }
        };
        nodes.Add(markerMapEnableCheckbox);

        var markerMinimapEnableCheckbox = new CheckboxNode
        {
            Size = new Vector2(18f, 18f),
            String = "Enable Marker Display on Minimap",
            IsChecked = configuration.WaymarksMinimapEnabled,
            OnClick = newValue =>
            {
                configuration.WaymarksMinimapEnabled = newValue;
                configuration.Save();
            }
        };
        nodes.Add(markerMinimapEnableCheckbox);
        
        // TODO: Implement FloatSliderNode
        // var markerSizeSlider = new SliderNode
        // {
        //     Range = new Range(1, 24),
        //     
        // }

        var mapEdgeCheckbox = new CheckboxNode
        {
            Size = new Vector2(18f, 18f),
            String = "Fade Markers on Map Edge",
            IsChecked = configuration.FadeWaymarkOnMapEdge,
            OnClick = newValue =>
            {
                configuration.FadeWaymarkOnMapEdge = newValue;
                configuration.Save();
            }
        };
        nodes.Add(mapEdgeCheckbox);
        
        var minimapEdgeCheckbox = new CheckboxNode
        {
            Size = new Vector2(18f, 18f),
            String = "Fade Markers on Minimap Edge",
            IsChecked = configuration.FadeWaymarkOnMinimapEdge,
            OnClick = newValue =>
            {
                configuration.FadeWaymarkOnMinimapEdge = newValue;
                configuration.Save();
            }
        };
        nodes.Add(minimapEdgeCheckbox);
        
        // TODO: Implement Opacity Slider

        var showToolTipsCheckbox = new CheckboxNode
        {
            Size = new Vector2(18f, 18f),
            String = "Show Tooltips on Hover",
            IsChecked = configuration.ShowWaymarkTooltips,
            OnClick = newValue =>
            {
                configuration.ShowWaymarkTooltips = newValue;
                configuration.Save();
            }
        };
        nodes.Add(showToolTipsCheckbox);

        var experimentalCategory = new CategoryTextNode
        {
            String = "Experimental"
        };
        nodes.Add(experimentalCategory);

        var useKamiToolkitCheckbox = new CheckboxNode
        {
            Size = new Vector2(18f, 18f),
            String = "Use Native (KamiToolkit)",
            IsChecked = configuration.UseKTK,
            OnClick = newValue =>
            {
                configuration.UseKTK = newValue;
                configuration.Save();
                plugin.MapOverlayController.RemoveAllMarkers();
            }
        };
        nodes.Add(useKamiToolkitCheckbox);
        
        #endregion
        
        return nodes;
    }
}
