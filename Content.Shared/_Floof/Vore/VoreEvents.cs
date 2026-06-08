using Robust.Shared.Serialization;

namespace Content.Shared._Floof.Vore;

[Serializable, NetSerializable]
public sealed class VoreSettingsEvent : EntityEventArgs
{
    public bool AllowPred;
    public bool AllowPrey;
    /*public bool Digest;
    public bool AllowSound;

    public int R;
    public int G;
    public int B;
    public int DigestSpeed;
    */
}
[Serializable, NetSerializable]
public sealed class OpenVoreMenuEvent : EntityEventArgs {}