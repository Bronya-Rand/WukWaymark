using FFXIVClientStructs.FFXIV.Client.UI;

namespace WukLamark.Services
{
    internal static unsafe class NaviMapStateReader
    {
        /// <summary>
        /// Reads all minimap state values in a single pass, performing each lookup once.
        /// Returns false if any required value is missing.
        /// </summary>
        /// <param name="naviMap">Pointer to _NaviMap.</param>
        /// <param name="isLocked">Whether the minimap rotation is locked.</param>
        /// <param name="rotation">Current minimap rotation in radians.</param>
        /// <param name="zoom">Current minimap zoom (ScaleX of the inner image node).</param>
        /// <returns>True if all values were read successfully, false if any node was missing.</returns>
        public static bool TryReadMinimapState(
            AddonNaviMap* naviMap,
            out bool isLocked,
            out float rotation,
            out float zoom)
        {
            isLocked = false;
            rotation = 0f;
            zoom = 1f;

            // Read lock state
            var lockedComponentCheckbox = naviMap->LockNorthCheckbox;
            if (lockedComponentCheckbox == null) return false;
            isLocked = lockedComponentCheckbox->IsChecked;

            // Read zoom 
            var imageNode = naviMap->MapImage;
            if (imageNode == null)
                return false;
            zoom = imageNode->AtkResNode.ScaleX;

            // Read rotation from node 8 (separate node)
            var rotationNode = naviMap->GetNodeById(8);
            if (rotationNode == null)
                return false;
            rotation = rotationNode->Rotation;

            return true;
        }

        public static bool IsUIFading()
        {
            var raptureAtkUnitManager = RaptureAtkUnitManager.Instance();
            return raptureAtkUnitManager != null && raptureAtkUnitManager->IsUiFading;
        }
    }
}
