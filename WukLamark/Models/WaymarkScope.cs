namespace WukLamark.Models;

/// <summary>
/// Defines the persistence scope for a waymark.
/// </summary>
public enum WaymarkScope
{
    /// <summary>Visible only to the character that created it (keyed by hashed content ID).</summary>
    Personal,

    /// <summary>Visible to all characters on the same FFXIV installation.</summary>
    Shared
}
