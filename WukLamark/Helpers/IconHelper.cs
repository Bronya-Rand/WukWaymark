using Dalamud.Interface.Textures.Internal;
using Dalamud.Interface.Textures.TextureWraps;
using System.Collections.Generic;
using System.Numerics;

namespace WukLamark.Helpers
{
    public static class IconHelper
    {
        private static readonly Dictionary<uint, Vector2> IconSizeCache = [];
        public static Vector2? GetIconSize(uint iconId)
        {
            // Search cache first
            if (IconSizeCache.TryGetValue(iconId, out var cachedSize))
                return cachedSize;

            IDalamudTextureWrap? tex;
            try
            {
                tex = Plugin.TextureProvider.GetFromGameIcon(iconId).GetWrapOrDefault();
            }
            catch (IconNotFoundException)
            {
                return null;
            }
            if (tex == null) return null;

            var texSize = tex.Size;
            tex.Dispose();

            // Cache the size for future lookups
            IconSizeCache[iconId] = texSize;
            return texSize;
        }
    }
}
