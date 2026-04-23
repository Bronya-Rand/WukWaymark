using Dalamud.Interface.Textures.Internal;
using Dalamud.Interface.Textures.TextureWraps;
using System.Collections.Generic;
using System.Numerics;

namespace WukLamark.Helpers
{
    public static class IconHelper
    {
        private static Dictionary<uint, Vector2> IconSizeCache = [];
        public static Vector2? GetIconSize(uint iconId)
        {
            // Search cache first
            if (IconSizeCache.TryGetValue(iconId, out var cachedSize))
                return cachedSize;

            IDalamudTextureWrap? tex;
            try
            {
                tex = Plugin.TextureProvider.GetFromGameIcon(iconId).GetWrapOrEmpty();
            }
            catch (IconNotFoundException)
            {
                return null;
            }
            var texSize = new Vector2(tex.Width, tex.Height);

            // Cache the size for future lookups
            IconSizeCache[iconId] = texSize;
            tex.Dispose();
            return texSize;
        }
    }
}
