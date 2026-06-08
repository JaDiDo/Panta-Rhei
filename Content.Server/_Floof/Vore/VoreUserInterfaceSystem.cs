using Robust.Server.GameObjects;
using Content.Shared._Floof.Vore;
using Content.Server.UserInterface;
namespace Content.Server._Floof.Vore;

public sealed class VoreUserInterfaceSystem : EntitySystem
{
    [Dependency] private readonly UserInterfaceSystem _uiSystem = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<VoreSettingsEvent>(OnVoreSettings);
    }

    private void OnVoreSettings(VoreSettingsEvent ev, EntitySessionEventArgs args)
    {
        var uid = args.SenderSession.AttachedEntity;

        if (uid == null)
            return;

        if (!TryComp<VoreComponent>(uid.Value, out var comp))
            return;

        comp.AllowPred = ev.AllowPred;
        comp.AllowPrey = ev.AllowPrey;
    }

}