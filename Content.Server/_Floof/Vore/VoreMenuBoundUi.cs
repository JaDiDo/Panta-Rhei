using Content.Shared._Floof.Vore;
using Content.Shared.UserInterface;
using Robust.Shared.GameObjects;

namespace Content.Server._Floof.Vore;

public sealed class VoreMenuBoundUi : BoundUserInterface
{
    public VoreMenuBoundUi(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        // This will be expanded when the actual UI is created
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        // Handle state updates from server
    }
}