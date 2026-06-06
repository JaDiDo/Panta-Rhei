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
    }

    private void OnVoreInit(EntityUid uid, VoreComponent component, ComponentInit args)
    {
        var uiComp = EnsureComp<UserInterfaceComponent>(uid);
        _uiSystem.SetUi((uid, uiComp), VoreUiKey.Key, new InterfaceData("VoreBoundUserInterface"));
    }
}
