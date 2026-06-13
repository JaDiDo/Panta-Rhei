using Content.Shared._Floof.Vore;
namespace Content.Server._Floof.Vore;

public sealed class VoreSettingsNetworkSystem : EntitySystem
{
    public override void Initialize()
    {
        SubscribeNetworkEvent<VoreSettingsEvent>(OnVoreSettingsChanged);
    }

    private void OnVoreSettingsChanged(VoreSettingsEvent ev, EntitySessionEventArgs args)
    {
        if (args.SenderSession?.AttachedEntity is not { } uid)
            return;
        RaiseLocalEvent(uid, ev);
    }
}