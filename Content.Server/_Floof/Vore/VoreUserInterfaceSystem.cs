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
        SubscribeLocalEvent<VoreComponent, ComponentInit>(OnVoreInit);
        SubscribeNetworkEvent<VoreSettingsEvent>(OnVoreSettings);
    }

    private void OnVoreInit(EntityUid uid, VoreComponent component, ComponentInit args)
    {
        var uiComp = EnsureComp<UserInterfaceComponent>(uid);
        _uiSystem.SetUi((uid, uiComp), VoreUiKey.Key, new InterfaceData("VoreBoundUserInterface"));
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