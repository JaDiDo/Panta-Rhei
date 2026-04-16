using Content.Shared._Common.Consent;
using Content.Shared.Verbs;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Content.Shared.Body.Components;
using Content.Shared.Mind.Components;
using Robust.Shared.Containers;
using Content.Shared.DoAfter;
using Content.Shared.Popups;
using Content.Shared.FloofStation;
using Robust.Shared.Serialization;
using Content.Shared._Floof.Vore;

namespace Content.Server._Floof.Vore;

public sealed class VoreSystem : EntitySystem
{
    [Dependency] private readonly SharedConsentSystem _consentSystem = default!;
    [Dependency] private readonly SharedContainerSystem _containerSystem = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfterSystem = default!;
    [Dependency] private readonly SharedPopupSystem _popupSystem = default!;
    
    public static readonly ProtoId<ConsentTogglePrototype> isPred = "PredVore";
    public static readonly ProtoId<ConsentTogglePrototype> isPrey = "PreyVore";

    public override void Initialize()
    {
        SubscribeLocalEvent<GetVerbsEvent<Verb>>(OnGetVerbs);
        SubscribeLocalEvent<VoreComponent, OnVoreDoAfter>(OnVoreDoAfter);
    }

    private void OnGetVerbs(GetVerbsEvent<Verb> args){
        var user = args.User;
        var target = args.Target;
        
        // no self activation
        if (user == target)
            return;

        // only when reachable & interactable
        if (!args.CanInteract || !args.CanAccess)
            return;

        // to avoid feeding yourself to items
        if (!EntityManager.HasComponent<BodyComponent>(target))
            return;
        
        // to avoid empty mind NPCs
        if (!EntityManager.TryGetComponent<MindContainerComponent>(target, out var mindContainer) ||
            mindContainer.Mind == null)
            return;

        // devour (pred → prey)
        if (_consentSystem.HasConsent(user, isPrey)
            && _consentSystem.HasConsent(target, isPred)){
            args.Verbs.Add(new Verb
            {
                Text = "Devour",
                Act = () => OnTryVore(user, target)
            });
        }

        // insert self (prey → pred)
        if (_consentSystem.HasConsent(user, isPred)
            && _consentSystem.HasConsent(target, isPrey)){
            args.Verbs.Add(new Verb
            {
                Text = "Insert Self",
                Act = () => OnTryVore(target, user)
            });
        }
    }

    private void OnTryVore(EntityUid user, EntityUid target){
        EnsureComp<VoreComponent>(user);

        //slow loading bar to avoid instant vore with warning pop ups
        var doAfterArgs = new DoAfterArgs(EntityManager, user, 5f, new OnVoreDoAfter(), user, target: target, used: user)
        {
            BreakOnMove = true,
            BreakOnDamage = true,      
        };
        _popupSystem.PopupEntity($"You are devouring someone!", user, user);
        _popupSystem.PopupEntity($"You are being devoured!", target, target);
        _doAfterSystem.TryStartDoAfter(doAfterArgs);
    }

    
    private void OnVoreDoAfter(EntityUid uid, VoreComponent comp, OnVoreDoAfter args){
        //handles canceled events
        if (args.Cancelled || args.Handled)
            return;
        if (args.Target is not EntityUid target)
            return;
        //moves inside the person
        var container = _containerSystem.EnsureContainer<Container>(args.User, "vore_container");
        _containerSystem.Insert(target, container);
    }
}