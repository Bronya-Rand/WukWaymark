using FFXIVClientStructs.FFXIV.Client.UI;

namespace WukWaymark.Services
{
    internal static unsafe class NaviMapStateReader
    {
        /// <summary>
        /// Reads all minimap state values in a single pass, performing each node lookup only once.
        /// Returns false if any required node is missing (no try/catch needed — callers guard on return value).
        /// </summary>
        /// <param name="naviMap">Pointer to the _NaviMap AtkUnitBase.</param>
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

            // Read zoom (ScaleX of image node 6 inside the same component)
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
