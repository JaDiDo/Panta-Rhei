using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using Content.Shared.Medical.SuitSensor;

namespace Content.Shared._Floof.Vore;

[RegisterComponent]
public sealed partial class PreyComponent : Component
{
    public Dictionary<EntityUid, float> Health = new();
    // the max health of the prey used for digestion and slow regeneration
    public float Max = 100f;
    public HashSet<EntityUid> ActiveDigesting = new();
    public Dictionary<EntityUid, float> Timer = new();
    // the stage of digestion, used for the popup
    public Dictionary<EntityUid, int> DigestPopupStage = new();
}

[RegisterComponent, NetworkedComponent]
public sealed partial class DevouredComponent : Component
{
    public bool AddedPressure;
    public bool AddedBreathing;
    public bool AddedTemperature;
    public bool AddedRadiation;
    public bool AddedFlash;

    [DataField("originalSensorModes")]
    public Dictionary<EntityUid, SuitSensorMode> OriginalSensorModes = new(); 
}