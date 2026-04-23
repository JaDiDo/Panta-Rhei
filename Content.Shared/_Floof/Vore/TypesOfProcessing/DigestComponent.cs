using System;
using Robust.Shared.GameObjects;
namespace Content.Shared._Floof.Vore;

[RegisterComponent]
public sealed partial class DigestComponent : Component
{
    public Dictionary<EntityUid, float> Health = new();
    public Dictionary<EntityUid, float> Timer = new();
    public HashSet<EntityUid> ActiveDigesting = new();
    public float Max = 100f;
}