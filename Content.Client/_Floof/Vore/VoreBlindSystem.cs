using Content.Shared._Floof.Vore;
using Robust.Shared.GameStates;
//temp for testing purposes
using Content.Shared.Eye.Blinding.Components;

namespace Content.Server._Floof.Vore;

public sealed class VoreBlindSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<DevouredComponent, ComponentStartup>(OnDevouredStart);
        SubscribeLocalEvent<DevouredComponent, ComponentShutdown>(OnDevouredEnd);
    }

    private void OnDevouredStart(EntityUid uid, DevouredComponent comp, ComponentStartup args)
    {
        EnsureComp<TemporaryBlindnessComponent>(uid);
    }

    private void OnDevouredEnd(EntityUid uid, DevouredComponent comp, ComponentShutdown args)
    {
        RemComp<TemporaryBlindnessComponent>(uid);
    }
}