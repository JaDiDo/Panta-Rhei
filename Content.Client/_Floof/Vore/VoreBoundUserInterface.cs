using Content.Shared._Floof.Vore;
using JetBrains.Annotations;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.XAML;
using Robust.Shared.GameObjects;

namespace Content.Client._Floof.Vore;

[UsedImplicitly]
public sealed class VoreBoundUserInterface : BoundUserInterface
{
    private VoreMenu? _menu;

    public VoreBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _menu = this.CreateWindowCenteredRight<VoreMenu>();
        _menu.SetEntity(Owner);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_menu != null)
            {
                _menu.Dispose();
                _menu = null;
            }
        }

        base.Dispose(disposing);
    }
}
