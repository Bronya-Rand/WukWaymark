using Dalamud.Game.NativeWrapper;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System;

namespace WukWaymark.Services
{
    internal class MinimapService
    {
        public static unsafe bool? IsMinimapLocked(AtkUnitBasePtr naviMap)
        {
            try
            {
                var addonBase = (AtkUnitBase*)naviMap.Address;
                var baseComponent = addonBase->GetComponentByNodeId(18);

                var lockedComponent = (AtkComponentCheckBox*)baseComponent->GetNodeById(4);
                if (lockedComponent == null) return null;

                return lockedComponent->IsChecked;
            }
            catch (Exception e)
            {
                Plugin.Log.Verbose(e, "Error reading minimap lock state");
                return null;
            }
        }
        public static unsafe float? GetMinimapRotation(AtkUnitBasePtr naviMap)
        {
            try
            {
                var addonBase = (AtkUnitBase*)naviMap.Address;
                var rotationNode = addonBase->GetNodeById(8);
                if (rotationNode == null) return null;

                return rotationNode->GetRotation();
            }
            catch (Exception e)
            {
                Plugin.Log.Verbose(e, "Error reading minimap rotation");
                return null;
            }
        }
        public static unsafe float? GetMinimapZoom(AtkUnitBasePtr naviMap)
        {
            try
            {
                var addonBase = (AtkUnitBase*)naviMap.Address;
                var baseComponent = addonBase->GetComponentByNodeId(18);
                if (baseComponent == null) return null;

                var imageNode = baseComponent->GetImageNodeById(6);
                if (imageNode == null) return null;

                return imageNode->ScaleX;
            }
            catch (Exception e)
            {
                Plugin.Log.Verbose(e, "Error reading minimap zoom level");
                return null;
            }
        }
    }
}
