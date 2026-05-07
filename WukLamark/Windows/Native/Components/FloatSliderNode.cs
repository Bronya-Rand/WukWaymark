using KamiToolKit;

namespace WukLamark.Windows.Native.Components;

/// <summary>
/// Because XIV doesn't natively have a Float slider, we make our own.
/// Borrowed from V+.
/// </summary>
public class FloatSliderNode 
{
    public required float MinValue { get; init;  }
    public required float MaxValue { get; init;  }
    public required float Value { get; init;  }
    public required int DecimalPlaces  { get; init;  }
    public required float StepSpeed { get; init;  }
}
