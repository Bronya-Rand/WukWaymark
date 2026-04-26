using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Interface.ImGuiNotification;

namespace WukLamark.Utils
{
    public sealed class ResultNotifications
    {
        public static SeString BuildChatSuccessMessage(string message)
        {
            var builder = new SeStringBuilder()
                .AddUiForeground(45)
                .AddText(message)
                .AddUiForegroundOff();
            return builder.Build();
        }
        public static SeString BuildChatErrorMessage(string message)
        {
            var builder = new SeStringBuilder()
                .AddUiForeground(17)
                .AddText(message)
                .AddUiForegroundOff();
            return builder.Build();
        }
        public static Notification BuildDalamudSuccessMessage(string message) =>
            new()
            { Content = message, Type = NotificationType.Success };
    }
}
