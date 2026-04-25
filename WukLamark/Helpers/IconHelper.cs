using Dalamud.Interface.Textures.Internal;
using Dalamud.Interface.Textures.TextureWraps;
using System.Numerics;

namespace WukLamark.Helpers
{
    public static class IconHelper
    {
        public static Vector2? GetGameIconSize(uint iconId)
        {
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
            return texSize;
        }
        public static Vector2? GetCustomIconSize(string customIconName)
        {
            if (!Plugin.CustomIconService.TryGetCustomIcon(customIconName, out var tex) || tex == null)
                return null;

            var texSize = tex.Size;
            return texSize;
        }
    }
}
