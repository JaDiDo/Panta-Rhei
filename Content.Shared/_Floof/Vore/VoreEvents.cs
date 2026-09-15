using Robust.Shared.Serialization;

namespace Content.Shared._Floof.Vore;

[Serializable, NetSerializable]
public sealed class VoreSettingsEvent : EntityEventArgs
{
    public bool AllowSound { get; set; }
}
[Serializable, NetSerializable]
public sealed class OpenVoreMenuEvent : EntityEventArgs {}