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
using Content.Shared._Shitmed.Body.Components;
using Content.Shared._DV.CosmicCult.Components;
using Content.Server.Atmos.Components;

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
        SubscribeLocalEvent<VoreComponent, EntRemovedFromContainerMessage>(OnVoreRemovedFromContainer);

    }

    /// <summary>
    /// creates verbs inside the interaction menu for yourself and other mobs controlled by players 
    /// only show up when the consent has been selected on both sides
    /// </summary>
    private void OnGetVerbs(GetVerbsEvent<Verb> args){
        var user = args.User;
        var target = args.Target;
        
        // no self activation, only there to remove your own prey and not have other intervene or have others see that you have prey
        if (user == target){
            var container = _containerSystem.EnsureContainer<Container>(target, "vore_container");
            if (container.ContainedEntities.Count > 0){
                args.Verbs.Add(new Verb
                {
                    Text = "Remove Prey",
                    Act = () => OnTryReleasePrey(target)
                });
            }
            return;
        }

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
        
    /// <summary>
    /// used for after selecting to insert into someone or devour
    /// will create a slow popup and warning to give both sides time to react on it
    /// </summary>
    private void OnTryVore(EntityUid user, EntityUid target){
        //currently needed as a lack of component
        //TODO remove after its added on getverbs
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

    /// <summary>
    /// moving the player inside the artificial storage
    /// will also give buffs such as space immunity for the target
    /// </summary>
    private void OnVoreDoAfter(EntityUid uid, VoreComponent comp, OnVoreDoAfter args){
        //handles canceled events
        if (args.Cancelled || args.Handled)
            return;
        if (args.Target is not EntityUid prey)
            return;

        //moves prey inside the person
        var container = _containerSystem.EnsureContainer<Container>(args.User, "vore_container");
        _containerSystem.Insert(prey, container);
        
        /*make the prey immune to space+temp+breathing to avoid consent concerns from outside influence
        gets removed after escaping or being forcefully ejected by pred*/
        EnsureComp<PressureImmunityComponent>(prey);
        EnsureComp<BreathingImmunityComponent>(prey);
        EnsureComp<TemperatureImmunityComponent >(prey);
    }

    /// <summary>
    /// for when the pred removes the prey from their container
    /// will remove the buffs such as space immunity for the target
    /// </summary>
    private void OnTryReleasePrey(EntityUid pred){
        var container = _containerSystem.EnsureContainer<Container>(pred, "vore_container");
        var preyList = new List<EntityUid>(container.ContainedEntities);
        //remove everything from people to items
        foreach (var prey in preyList){
            _containerSystem.Remove(prey, container);
            // remove the given immunity components
            RemComp<PressureImmunityComponent>(prey);
            RemComp<BreathingImmunityComponent>(prey);
            RemComp<TemperatureImmunityComponent>(prey);
            _popupSystem.PopupEntity("You have been released!", prey, prey);
        }
        _popupSystem.PopupEntity("You release your prey.", pred, pred);
    }

    /// <summary>
    /// in case the prey chose to escape themself
    /// will remove the buffs such as space immunity for the target
    /// </summary>
    private void OnVoreRemovedFromContainer(EntityUid uid, VoreComponent comp, EntRemovedFromContainerMessage args){
        if (args.Container.ID != "vore_container")
            return;

        // remove the given immunity components
        var prey = args.Entity;
        RemComp<PressureImmunityComponent>(prey);
        RemComp<BreathingImmunityComponent>(prey);
        RemComp<TemperatureImmunityComponent>(prey);
        _popupSystem.PopupEntity("You struggle free!", prey, prey);
        _popupSystem.PopupEntity("Your prey escaped!", uid, uid);
    }
}