using Content.Shared._Floof.Vore;
using Content.Shared.Mind.Components;
using Content.Shared.Verbs;
using Content.Shared.UserInterface;
using Content.Shared._Common.Consent;
using Robust.Shared.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server._Floof.Vore;

public sealed partial class VoreMenuSystem : EntitySystem
{
    [Dependency] private readonly SharedContainerSystem _containerSystem = default!;
    [Dependency] private readonly SharedConsentSystem _consentSystem = default!;
    [Dependency] private readonly SharedUserInterfaceSystem _uiSystem = default!;

    public static readonly ProtoId<ConsentTogglePrototype> isPred = "PredVore";
    public static readonly ProtoId<ConsentTogglePrototype> isPrey = "PreyVore";

    public override void Initialize()
    {
        SubscribeLocalEvent<VoreMenuComponent, GetVerbsEvent<Verb>>(OnGetVerbs);
        SubscribeLocalEvent<VoreMenuComponent, BoundUIOpenedEvent>(OnUIOpened);
    }

    private void OnGetVerbs(EntityUid uid, VoreMenuComponent comp, GetVerbsEvent<Verb> args)
    {
        var user = args.User;

        // Only show verb for self
        if (user != uid)
            return;

        if (!args.CanInteract || !args.CanAccess)
            return;

        // Check if user has either pred or prey consent enabled
        if (!_consentSystem.HasConsent(user, isPrey) &&
            !_consentSystem.HasConsent(user, isPred))
            return;
        args.Verbs.Add(new Verb
        {
            Text = "Vore Menu",
            Act = () => OpenVoreMenu(user)
        });
    }

    private void OpenVoreMenu(EntityUid user)
    {
        _uiSystem.TryOpenUi(user, VoreMenuUiKey.Key, user);
    }

    private void OnUIOpened(Entity<VoreMenuComponent> ent, ref BoundUIOpenedEvent args)
    {
        // Send initial state to client
    }
}

public enum VoreMenuUiKey : byte
{
    Key
}