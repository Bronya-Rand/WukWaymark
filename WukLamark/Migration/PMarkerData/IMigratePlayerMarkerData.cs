using WukLamark.Models;

namespace WukLamark.Migration.PMarkerData
{
    internal interface IMigratePlayerMarkerData
    {
        int FromVersion { get; }
        int ToVersion { get; }

        /// <summary>
        /// Applies migration. Returns true if any changes were made.
        /// </summary>
        bool ApplyMigration(PlayerMarkerData data);
    }
}
