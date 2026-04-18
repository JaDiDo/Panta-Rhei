using System;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization;
using Content.Shared.DoAfter;
namespace Content.Shared._Floof.Vore;

[RegisterComponent]
public sealed partial class VoreComponent : Component{}
[Serializable, NetSerializable]
public sealed partial class OnVoreDoAfter : SimpleDoAfterEvent{
    /// <summary>
    /// Maximum number of prey this entity can hold.
    /// </summary>
    [DataField("maxPrey")]
    public int MaxPrey = 2;
    public OnVoreDoAfter(int maxPrey)
    {
        MaxPrey = maxPrey;
    }
}
[RegisterComponent]
public sealed partial class VoreImmunityTrackerComponent : Component
{
    public bool AddedPressure;
    public bool AddedBreathing;
    public bool AddedTemperature;
}