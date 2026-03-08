using FFXIVClientStructs.FFXIV.Component.GUI;

namespace WukWaymark.Services
{
    internal class NaviMapStateReader
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
        public static unsafe bool TryReadMinimapState(
            AtkUnitBase* naviMap,
            out bool isLocked,
            out float rotation,
            out float zoom)
        {
            isLocked = false;
            rotation = 0f;
            zoom = 1f;

            // Component node 18 is shared by both IsLocked and Zoom
            var baseComponent = naviMap->GetComponentNodeById(18);
            if (baseComponent == null || baseComponent->Component == null)
                return false;

            // Read lock state
            var lockedComponent = (AtkComponentCheckBox*)baseComponent->Component->UldManager.SearchNodeById(4);
            if (lockedComponent == null)
                return false;
            isLocked = lockedComponent->IsChecked;

            // Read zoom (ScaleX of image node 6 inside the same component)
            var imageNode = (AtkImageNode*)baseComponent->Component->UldManager.SearchNodeById(6);
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
    }
}
