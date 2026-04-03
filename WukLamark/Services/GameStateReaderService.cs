using Dalamud.Plugin.Services;
using System;

namespace WukLamark.Services
{
    public class GameStateReaderService : IDisposable
    {
        public bool IsInCombat { get; private set; }
        public bool IsInPvP { get; private set; }
        public bool IsLoggedIn { get; private set; }
        public GameStateReaderService()
        {
            Plugin.Framework.Update += OnFrameworkUpdate;
        }
        private void OnFrameworkUpdate(IFramework framework)
        {
            IsLoggedIn = Plugin.ClientState.IsLoggedIn;
            IsInPvP = Plugin.ClientState.IsPvPExcludingDen;
            IsInCombat = Plugin.ObjectTable.LocalPlayer != null && Plugin.ObjectTable.LocalPlayer.StatusFlags.HasFlag(Dalamud.Game.ClientState.Objects.Enums.StatusFlags.InCombat);
        }
        public bool DisableMarkerActions() => !IsLoggedIn || IsInPvP || IsInCombat;
        public void Dispose()
        {
            Plugin.Framework.Update -= OnFrameworkUpdate;
            GC.SuppressFinalize(this);
        }
    }
}
