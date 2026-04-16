using System;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization;
using Content.Shared.DoAfter;
namespace Content.Shared._Floof.Vore;

[RegisterComponent]
public sealed partial class VoreComponent : Component{}
[Serializable, NetSerializable]
public sealed partial class OnVoreDoAfter : SimpleDoAfterEvent{}