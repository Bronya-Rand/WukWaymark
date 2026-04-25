using System.Collections.Generic;
using System.Linq;
using WukLamark.Migration.PMarkerData;
using WukLamark.Models;

namespace WukLamark.Services
{
    public sealed class MarkerMigrationService
    {
        private readonly List<IMigratePlayerMarkerData> playerDataMigrations =
        [
            new MigratePlayerMarkerDataV1()
        ];

        public int CurrentPlayerDataSchemaVersion => playerDataMigrations.Count == 0 ? 0 : playerDataMigrations[^1].ToVersion;

        public bool MigratePlayerMarkerData(PlayerMarkerData data)
        {
            var changed = false;

            while (true)
            {
                var migration = playerDataMigrations
                    .OrderBy(m => m.FromVersion)
                    .FirstOrDefault(m => m.FromVersion == data.SchemaVersion);

                if (migration == null) break;

                changed |= migration.ApplyMigration(data);
                data.SchemaVersion = migration.ToVersion;
                changed = true;
            }

            return changed;
        }
    }
}
