using Content.Client._Floof.Vore;
using Content.Shared.UserInterface;
using Robust.Client.GameObjects;

namespace Content.Client._Floof.Vore;

public sealed class VoreMenuBoundUi : BoundUserInterface
{
    private VoreMenu? _menu;

    public VoreMenuBoundUi(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        _menu = this.CreateWindow<VoreMenu>();
        _menu.Open();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        // State updates will be handled here
    }

    protected override void Close()
    {
        _menu?.Close();
        _menu = null;
    }
}