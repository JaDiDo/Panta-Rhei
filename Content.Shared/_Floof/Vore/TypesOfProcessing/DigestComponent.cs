using System;
using Robust.Shared.GameObjects;
namespace Content.Shared._Floof.Vore;

[RegisterComponent]
public sealed partial class DigestComponent : Component
{
    public Dictionary<EntityUid, float> Health = new();
    public Dictionary<EntityUid, float> Timer = new();
    public HashSet<EntityUid> ActiveDigesting = new();
    //todo change to 100, did 10 for testing
    public float Max = 10f;
}