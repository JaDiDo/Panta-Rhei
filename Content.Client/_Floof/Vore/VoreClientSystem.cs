using Content.Shared._Floof.Vore;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;
namespace Content.Client._Floof.Vore;

public sealed class VoreClientSystem : EntitySystem
{
    [Dependency] private readonly IUserInterfaceManager _ui = default!;

    public override void Initialize()
    {
        SubscribeNetworkEvent<OpenVoreMenuEvent>(OnOpenMenu);
    }

    /// <summary>
    /// once the event from server has been received open the menu 
    /// </summary>
    private void OnOpenMenu(OpenVoreMenuEvent ev)
    {
        var ui = _ui.GetUIController<VoreUIController>();
        ui.OpenMenu();
    }
}
