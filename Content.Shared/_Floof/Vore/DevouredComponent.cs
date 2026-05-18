using Robust.Shared.GameObjects;

namespace Content.Shared._Floof.Vore;

[RegisterComponent]
public sealed partial class DevouredComponent : Component
{
    public bool AddedPressure;
    public bool AddedBreathing;
    public bool AddedTemperature;
    public bool AddedFlash;
}