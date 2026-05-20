using System.Numerics;
using Dalamud.Interface.Colors;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Utility;

namespace WukLamark.Utils
{
    public sealed class ResultNotifications
    {
        #region ChatGui Message Builders
        private static Dalamud.Game.Text.SeStringHandling.SeString BuildChatSuccessMessage(string message, bool omitPluginName = false)
        {
            var successColor = ImGuiColors.SuccessForeground;

            var builder = new Lumina.Text.SeStringBuilder()
                .PushColorRgba(DalamudVector4ToLuminaVector4(successColor))
                .Append(omitPluginName ? message : $"[WukLamark] {message}")
                .PopColor()
                .ToReadOnlySeString();
            return builder.ToDalamudString();
        }
        private static Dalamud.Game.Text.SeStringHandling.SeString BuildChatErrorMessage(string message, bool omitPluginName = false)
        {
            var errorColor = ImGuiColors.ErrorForeground;

            var builder = new Lumina.Text.SeStringBuilder()
                .PushColorRgba(DalamudVector4ToLuminaVector4(errorColor))
                .Append(omitPluginName ? message : $"[WukLamark] {message}")
                .PopColor()
                .ToReadOnlySeString();
            return builder.ToDalamudString();
        }
        private static Dalamud.Game.Text.SeStringHandling.SeString BuildChatWarningMessage(string message, bool omitPluginName = false)
        {
            var warningColor = ImGuiColors.WarningForeground;

            var builder = new Lumina.Text.SeStringBuilder()
                .PushColorRgba(DalamudVector4ToLuminaVector4(warningColor))
                .Append(omitPluginName ? message : $"[WukLamark] {message}")
                .PopColor()
                .ToReadOnlySeString();
            return builder.ToDalamudString();
        }
        #endregion
        #region Dalamud Notification Builders
        private static Notification BuildDalamudSuccessMessage(string message) =>
            new()
            { Content = message, Type = NotificationType.Success };
        private static Notification BuildDalamudErrorMessage(string message) =>
            new()
            { Content = message, Type = NotificationType.Error };
        private static Notification BuildDalamudWarningMessage(string message) =>
            new()
            { Content = message, Type = NotificationType.Warning };
        #endregion
        public static void SendSuccessMessage(string message, bool omitPluginName = false, bool sendBoth = false)
        {
            // Check login state
            var isLoggedIn = Plugin.ClientState.IsLoggedIn;

            // If sendBoth is true, send both a Dalamud notification and a chat message (if logged in)
            if (sendBoth)
            {
                var notification = BuildDalamudSuccessMessage(message);
                Plugin.NotificationManager.AddNotification(notification);

                if (isLoggedIn)
                {
                    var chatMessage = BuildChatSuccessMessage(message, omitPluginName);
                    Plugin.ChatGui.Print(chatMessage);
                }
                return;
            }

            // If logged in, use chat message; otherwise, use Dalamud notification
            if (isLoggedIn)
            {
                var chatMessage = BuildChatSuccessMessage(message, omitPluginName);
                Plugin.ChatGui.Print(chatMessage);

            }
            else
            {
                var notification = BuildDalamudSuccessMessage(message);
                Plugin.NotificationManager.AddNotification(notification);
            }
        }
        public static void SendErrorMessage(string message, bool omitPluginName = false)
        {
            var isLoggedIn = Plugin.ClientState.IsLoggedIn;
            if (isLoggedIn)
            {
                var chatMessage = BuildChatErrorMessage(message, omitPluginName);
                Plugin.ChatGui.Print(chatMessage);
            }
            else
            {
                var notification = BuildDalamudErrorMessage(message);
                Plugin.NotificationManager.AddNotification(notification);
            }

        }
        public static void SendWarningMessage(string message, bool omitPluginName = false)
        {
            var isLoggedIn = Plugin.ClientState.IsLoggedIn;
            if (isLoggedIn)
            {
                var chatMessage = BuildChatWarningMessage(message, omitPluginName);
                Plugin.ChatGui.Print(chatMessage);
            }
            else
            {
                var notification = BuildDalamudWarningMessage(message);
                Plugin.NotificationManager.AddNotification(notification);
            }
        }

        #region Helpers
        /// <summary>
        /// Converts a Dalamud ImGuiColors <see cref="Vector4"/> to a Lumina-compatible RGBA color struct.
        /// </summary>
        /// <param name="dalamudColor">The Dalamud color to convert.</param>
        /// <returns>A Vector4 of the same color in byte-form.</returns>
        private static Vector4 DalamudVector4ToLuminaVector4(Vector4 dalamudColor) =>
            new(
                (byte)(dalamudColor.X * 255),
                (byte)(dalamudColor.Y * 255),
                (byte)(dalamudColor.Z * 255),
                (byte)(dalamudColor.W * 255)
            );
        #endregion
    }
}
