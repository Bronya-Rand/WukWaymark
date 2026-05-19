using System;
using WukLamark.Models;

namespace WukLamark.Windows.Components;

/// <summary>
/// Represents the result of an editable marker, providing access to its identifying and descriptive properties.
/// </summary>
public interface IEditableMarkerResult
{
    string Name { get; }
    Guid? GroupId { get; }
    MarkerIcon Icon { get; }
    MarkerScope Scope { get; }
    bool AppliesToAllWorlds { get; }
}
