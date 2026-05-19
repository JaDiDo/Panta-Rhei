using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Containers;
using Content.Shared.Body.Components;
using Content.Shared.DoAfter;
using Content.Shared.Popups;
using Content.Shared.FloofStation;
using Content.Shared._Floof.Vore;
using Content.Shared._Shitmed.Body.Components;
using Content.Shared._DV.CosmicCult.Components;
using Content.Server.Atmos.Components;
using Content.Shared.Body.Events;
using Content.Shared._Common.Consent;
using Content.Shared.Verbs;
using Content.Shared.Polymorph;
using Content.Shared.Destructible;
using Content.Shared.Movement.Pulling.Components;
using Robust.Shared.Configuration;
using Content.Shared._DV.Carrying;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Robust.Server.Player;
using Content.Shared.Flash.Components;
using Robust.Server.Audio;
using Content.Shared.Medical.SuitSensors;
using Content.Shared.Medical.SuitSensor;
using Content.Shared.Damage.Systems;
namespace Content.Server._Floof.Vore;

public sealed class VoreSystem : EntitySystem
{
    [Dependency] private readonly SharedConsentSystem _consentSystem = default!;
    [Dependency] private readonly SharedContainerSystem _containerSystem = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfterSystem = default!;
    [Dependency] private readonly SharedPopupSystem _popupSystem = default!;
    [Dependency] private readonly AudioSystem _audioSystem = default!;
    [Dependency] private readonly CarryingSystem _carryingSystem = default!;
    [Dependency] private readonly MobStateSystem _mobStateSystem = default!;
    [Dependency] private readonly SharedSuitSensorSystem _suitSensorSystem = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;

    public static readonly ProtoId<ConsentTogglePrototype> isPred = "PredVore";
    public static readonly ProtoId<ConsentTogglePrototype> isPrey = "PreyVore";
    public static readonly ProtoId<ConsentTogglePrototype> isDigest = "Digestable";
    
    private readonly HashSet<EntityUid> _pendingConsentUpdates = new();
    private readonly HashSet<EntityUid> _pendingImmunityUpdates = new();

    public override void Initialize()
    {
        SubscribeLocalEvent<ConsentComponent, ComponentStartup>(OnConsentStartup);
        SubscribeLocalEvent<ConsentComponent, EntityConsentToggleUpdatedEvent>(OnConsentUpdated);

        SubscribeLocalEvent<VoreComponent, GetVerbsEvent<Verb>>(OnGetVerbs);
        SubscribeLocalEvent<VoreComponent, OnVoreDoAfter>(OnVoreDoAfter);
        SubscribeLocalEvent<VoreComponent, EntRemovedFromContainerMessage>(OnVoreRemovedFromContainer);
        SubscribeLocalEvent<VoreComponent, BeingGibbedEvent>(OnGibbedRemoveContent);
        SubscribeLocalEvent<VoreComponent, DestructionEventArgs>(OnDestroyedRemoveContent);
        SubscribeLocalEvent<VoreComponent, PolymorphedEvent>(OnPolymorphedTransferContent);
        SubscribeLocalEvent<VoreImmunityTrackerComponent, MobStateChangedEvent>(OnPreyMobStateChanged);
        SubscribeLocalEvent<VoreImmunityTrackerComponent, BeforeDamageChangedEvent>(OnBeforeDamageChanged);
    }

    /// <summary>
    /// To get the most recent values for consent and current container
    /// </summary>
    public override void Update(float frameTime){
        base.Update(frameTime);

        // processing of consent updates
        foreach (var uid in _pendingConsentUpdates){
            if (!HasComp<ConsentComponent>(uid))
                continue;
            ApplyVoreConsent(uid);
        }
        _pendingConsentUpdates.Clear();

        // processing of immunity updates
        foreach (var uid in _pendingImmunityUpdates){
            RemoveStomachImmunities(uid);
        }
        _pendingImmunityUpdates.Clear();
    }

    /// <summary>
    /// gives the mob vore component when they updated their consent to be pred or prey
    /// in order to avoid giving every mob it one by one, timer needed to get the recent change 
    /// </summary>
    private void OnConsentUpdated(EntityUid uid, ConsentComponent comp, EntityConsentToggleUpdatedEvent args){
        // only if the updated toggle is prey, pred or digest
        if (args.ConsentToggleProtoId != isPred && 
        args.ConsentToggleProtoId != isPrey &&
        args.ConsentToggleProtoId != isDigest)
            return;
        _pendingConsentUpdates.Add(uid);
    }

    /// <summary>
    /// same principle as OnConsentUpdated but without the need for checking consent change
    /// </summary>
    private void OnConsentStartup(EntityUid uid, ConsentComponent comp, ComponentStartup args){
        _pendingConsentUpdates.Add(uid);
    }

    /// <summary>
    /// gives a mob the vore component if they have selected either pred or prey consent and removes it if they have neither
    /// </summary>
    private void ApplyVoreConsent(EntityUid uid){
        var hasPred = _consentSystem.HasConsent(uid, isPred);
        var hasPrey = _consentSystem.HasConsent(uid, isPrey);
        
        /* in case consent turned off remove the user from all containers and/or remove all prey*/
        if (!hasPrey && IsInVoreContainer(uid) &&
        _containerSystem.TryGetContainingContainer(uid, out var container)){
            _containerSystem.Remove(uid, container);
        }
        if(!hasPred){
            TryReleasePrey(uid, EnsureComp<VoreComponent>(uid));
        }

        //give the mob the needed component to be able to see the verbs
        if (hasPred || hasPrey){
            EnsureComp<VoreComponent>(uid);
        }
        else{
            RemComp<VoreComponent>(uid);
            
        }

        if (_consentSystem.HasConsent(uid, isDigest)){
            EnsureComp<DigestComponent>(uid);
        }
        else{
            RemComp<DigestComponent>(uid);
        }
    }

    /// <summary>
    /// creates verbs inside the interaction menu for yourself and other mobs controlled by players 
    /// only show up when the consent has been selected on both sides
    /// </summary>
    private void OnGetVerbs(EntityUid uid, VoreComponent comp, GetVerbsEvent<Verb> args){
        // using command to turn on/off verb components
        if (!_cfg.GetCVar(VoreCVars.VoreEnabled))
            return;
        
        // only when reachable & interactable
        if (!args.CanInteract || !args.CanAccess)
            return;

        BuildVoreContainerVerbs(uid, comp, args);
        EntitySystem.Get<DigestSystem>().BuildDigestVerbs(uid, comp, args);
    }

    /// <summary>
    /// handles the verbs that control the container such as inserting/removing
    /// </summary>
    private void BuildVoreContainerVerbs(EntityUid uid, VoreComponent comp, GetVerbsEvent<Verb> args){
        var user = args.User;
        var target = args.Target;
        // no self activation, only there to remove your own prey and not have other intervene or have others see that you have prey
        if (user == target){
            var container = _containerSystem.EnsureContainer<Container>(target, comp.ContainerId);
            if (container.ContainedEntities.Count > 0){
                args.Verbs.Add(new Verb
                {
                    Text = "Remove Prey",
                    Category = VerbCategory.Vore,
                    Act = () =>TryReleasePrey(target, comp)
                });
            }
            return;
        }

        // 1. devour (pred → prey)
        if (IsDevourable(user, target)){
            args.Verbs.Add(new Verb
            {
                Text = "Devour",
                Category = VerbCategory.Vore,
                Act = () => TryVore(user, target)
            });
        }
        // 2. insert self (prey → pred)
        if (IsDevourable(target, user)){
                args.Verbs.Add(new Verb
                {
                    Text = "Insert Self",
                    Category = VerbCategory.Vore,
                    Act = () => TryVore(target, user)
                });
        }
        // 3. insert someone else if you pull or carry them
        EntityUid? carried = null;
        if (TryComp<CarryingComponent>(user, out var carrying) && carrying.Carried != default)
            carried  = carrying.Carried;
        else if (TryComp<PullerComponent>(user, out var puller) && puller.Pulling is EntityUid pulling)
            carried  = pulling;
        
        if (carried != null && carried is EntityUid prey && prey != target){
            // making sure the user has either prey or pred on to be able to see the verb
            if (TryComp<VoreComponent>(user, out _) && IsDevourable(target, prey)){
                args.Verbs.Add(new Verb
                {
                    Text = $"Insert {Name(prey)}",
                    Category = VerbCategory.Vore,
                    Act = () => TryVore(target, prey)
                });
            }
        }
    }

    /// <summary>
    /// used for after selecting to insert into someone or devour
    /// will create a slow popup and warning to give both sides time to react on it
    /// </summary>
    private void TryVore(EntityUid user, EntityUid target){

        //slow loading bar to avoid instant vore with warning pop ups
        var doAfterArgs = new DoAfterArgs(EntityManager, user, 5f, new OnVoreDoAfter(), user, target: target, used: user)
        {
            BreakOnMove = true,
            BreakOnDamage = true,      
        };
        if (!_doAfterSystem.TryStartDoAfter(doAfterArgs))
            return;
        _popupSystem.PopupEntity($"You are devouring someone!", user, user);
        _popupSystem.PopupEntity($"You are being devoured!", target, target, PopupType.LargeCaution);
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

        var pred = uid;
        var container = _containerSystem.EnsureContainer<Container>(pred, comp.ContainerId);
        
        var count = 0;
        //only counts entities with bodies meaning no items
        foreach (var e in container.ContainedEntities){
            if (HasComp<BodyComponent>(e))
                count++;
        }
        //as a way to prevent too many entities to be devoured
        if (count >= args.MaxPrey){
            _popupSystem.PopupEntity("You are too full to swallow more prey.", pred, pred);
            return;
        }

        //makes sure prey will be dropped from bags and hands
        EnsureEntityFree(pred, prey, comp);

        //gulp sound only for both entities involved
        if (comp.SoundDevour != null){
            if (_playerManager.TryGetSessionByEntity(uid, out var predSession))
                _audioSystem.PlayGlobal(comp.SoundDevour, predSession);
            if (_playerManager.TryGetSessionByEntity(prey, out var preySession))
                _audioSystem.PlayGlobal(comp.SoundDevour, preySession);
        }

        //moves prey inside the person
        _containerSystem.Insert(prey, container);
        
        /*make the prey immune to space+temp+breathing to avoid consent concerns from outside influence
        gets removed after escaping or being forcefully ejected by pred*/
        ApplyStomachImmunities(prey);
    }

    /// <summary>
    /// makes sure the prey is not inside any other container such as 
    /// bags or being carried by someone before being inserted into the pred
    /// </summary>
    private void EnsureEntityFree(EntityUid pred, EntityUid prey, VoreComponent comp){
         //check if the prey is already inside a container and remove them (for example bags)
        if (_containerSystem.TryGetContainingContainer(prey, out var currentContainer)){
            if (currentContainer.ID != comp.ContainerId)
                _containerSystem.Remove(prey, currentContainer);
        }

        //in case prey is being carried by pred, someone else or is holding the prey drop them
        // 1. pred carrying prey
        if (TryComp<CarryingComponent>(pred, out var predCarrying) &&
            predCarrying.Carried == prey)
            _carryingSystem.DropCarried(pred, prey);
        // 2. prey carrying pred
        if (TryComp<CarryingComponent>(prey, out var preyCarrying) &&
        preyCarrying.Carried == pred)
            _carryingSystem.DropCarried(prey, pred);
        // 3. prey being carried by someone else
        if (TryComp<BeingCarriedComponent>(prey, out var preyBeingCarried) &&
        preyBeingCarried.Carrier != pred)
            _carryingSystem.DropCarried(preyBeingCarried.Carrier, prey);
    }

    /// <summary>
    /// for when the pred removes the prey from their container
    /// will remove the buffs such as space immunity for the target
    /// </summary>
    private void TryReleasePrey(EntityUid pred, VoreComponent comp){
        var container = _containerSystem.EnsureContainer<Container>(pred, comp.ContainerId);
        var preyList = new List<EntityUid>(container.ContainedEntities);
        
        //to avoid unnecessary popups
        if (preyList.Count == 0)
            return;
        
        //remove everything from people to items
        foreach (var prey in preyList){
            // in case pred intentionally releases the prey to avoid escape popups
            if (TryComp<VoreComponent>(prey, out var preyComp))
                preyComp.IntentionalRelease = true;

            _containerSystem.Remove(prey, container);
            _pendingImmunityUpdates.Add(prey);
            _popupSystem.PopupEntity("You have been released!", prey, prey);
        }
        _popupSystem.PopupEntity("You release your prey.", pred, pred);
    }

    /// <summary>
    /// in case the prey chose to escape themself
    /// will remove the buffs such as space immunity for the target
    /// </summary>
    private void OnVoreRemovedFromContainer(EntityUid uid, VoreComponent comp, EntRemovedFromContainerMessage args){
        if (args.Container.ID != comp.ContainerId)
            return;

        var prey = args.Entity;

        // Check if this was an intentional release by the pred (not a self-escape)
        if (TryComp<VoreComponent>(prey, out var preyComp) && preyComp.IntentionalRelease){
            preyComp.IntentionalRelease = false;
            return;
        }

        _pendingImmunityUpdates.Add(prey);
        _popupSystem.PopupEntity("You struggle free!", prey, prey);
        _popupSystem.PopupEntity("Your prey escaped!", uid, uid);
    }

    /// <summary>
    /// in case the user gets gibbed need content emptied including prey+items
    /// </summary>
    private void OnGibbedRemoveContent(EntityUid uid, VoreComponent comp, BeingGibbedEvent args){
        TryReleasePrey(uid, comp);
    }

    /// <summary>
    /// in case the user gets destroyed through for example singulo or gibbing
    /// </summary>
    private void OnDestroyedRemoveContent(EntityUid uid, VoreComponent comp, DestructionEventArgs args){
        TryReleasePrey(uid, comp);
    }

    /// <summary>
    /// in case of polymorp scenarios such as kitsune release all the content
    /// </summary>
    private void OnPolymorphedTransferContent(EntityUid uid, VoreComponent comp, PolymorphedEvent args){   
        TryReleasePrey(uid, comp);
    }

    /// <summary>
    /// in case the prey died/crit they need to be ejected from ALL vorecontainers
    /// this way a para wont accidentally stumble on a scene and the corpse wont rot
    /// </summary>
    private void OnPreyMobStateChanged(EntityUid uid, VoreImmunityTrackerComponent comp, ref MobStateChangedEvent args){
        if (args.NewMobState != MobState.Dead && args.NewMobState != MobState.Critical)
            return;
        if (!TryComp<VoreComponent>(uid, out var vore))
            return;
        while (_containerSystem.TryGetContainingContainer(uid, out var container) &&
        container.ID == vore.ContainerId){
            TryReleasePrey(container.Owner, vore);
        }
    }

    /// <summary>
    /// will nullify any damage except for airloss and bloodloss to avoid exploits
    /// when you are inside a vorecontainer for consent purposes
    /// </summary>
    private void OnBeforeDamageChanged(EntityUid uid, VoreImmunityTrackerComponent comp, ref BeforeDamageChangedEvent args){
        /*double check making sure they are inside the container
        should prevent possible exploitation of the system*/
        if (!IsInVoreContainer(uid))
            return;
        var damageTypes = new List<string>(args.Damage.DamageDict.Keys);
        foreach (var type in damageTypes){
            if (type != "Airloss" && type != "Bloodloss"){
                args.Damage.DamageDict[type] = 0;
            }
        }
    }
    
    /// <summary>
    /// the prey needs to have certain components such as pressure immunity
    /// for consent purposes -> having others avoid stumbling on scenarios
    /// </summary>
    private void ApplyStomachImmunities(EntityUid prey){
        /*double check making sure they are inside the container
        should prevent possible exploitation of the system*/
        if (!IsInVoreContainer(prey))
           return;

        var tracker = EnsureComp<VoreImmunityTrackerComponent>(prey);
        if (!HasComp<PressureImmunityComponent>(prey))
        {
            EnsureComp<PressureImmunityComponent>(prey);
            tracker.AddedPressure = true;
        }

        if (!HasComp<BreathingImmunityComponent>(prey))
        {
            EnsureComp<BreathingImmunityComponent>(prey);
            tracker.AddedBreathing = true;
        }

        if (!HasComp<TemperatureImmunityComponent>(prey))
        {
            EnsureComp<TemperatureImmunityComponent>(prey);
            tracker.AddedTemperature = true;
        }
        if (!HasComp<FlashImmunityComponent>(prey))
        {
            EnsureComp<FlashImmunityComponent>(prey);
            tracker.AddedFlash = true;
        }
        _suitSensorSystem.SetAllSensors(prey, SuitSensorMode.SensorOff);
    }

    /// <summary>
    /// the removal of the components after leaving a container
    /// to avoid intentional and accidental exploitation
    /// </summary>
    private void RemoveStomachImmunities(EntityUid prey){
        if (!TryComp<VoreImmunityTrackerComponent>(prey, out var tracker))
            return;
        // if still in a container skip alltogether for example release from multi vore
        if (IsInVoreContainer(prey))
            return;

        if (tracker.AddedPressure)
            RemComp<PressureImmunityComponent>(prey);
        if (tracker.AddedBreathing)
            RemComp<BreathingImmunityComponent>(prey);
        if (tracker.AddedTemperature)
            RemComp<TemperatureImmunityComponent>(prey);
        if (tracker.AddedFlash)
            RemComp<FlashImmunityComponent>(prey);
        
        _suitSensorSystem.SetAllSensors(prey, SuitSensorMode.SensorCords);
        RemComp<VoreImmunityTrackerComponent>(prey);
    }

    /// <summary>
    /// making sure all the consent toggles and issues are resolved before entering container
    /// </summary>
    /// <returns>
    /// true if the entity is allowed to be eaten
    /// </returns>
    private bool IsDevourable(EntityUid user, EntityUid target){
        if (user == target)
            return false;
        if (!_playerManager.TryGetSessionByEntity(user, out _) || !_playerManager.TryGetSessionByEntity(target, out _))
            return false;
        if (!HasComp<BodyComponent>(user) || !HasComp<BodyComponent>(target))
            return false;
        if (!IsValidContainment(user, target))
            return false;
        if (!_consentSystem.HasConsent(user, isPred) || !_consentSystem.HasConsent(target, isPrey))
            return false;
        if (_mobStateSystem.IsDead(target) || _mobStateSystem.IsCritical(target))
            return false;
        
        return true;
    }

    /// <summary>
    /// helper method to check if an entity is inside a vore container 
    /// </summary>
    /// <returns>
    /// true if the entity is inside any vore container
    /// </returns>
    private bool IsInVoreContainer(EntityUid uid){
        if (!TryComp<VoreComponent>(uid, out var comp))
            return false;
        return _containerSystem.TryGetContainingContainer(uid, out var container) &&
           container.ID == comp.ContainerId;
    }

    /// <summary>
    /// checks if prey is inside a vore container to only allow vore in the same container
    /// </summary>
    /// <returns>
    /// false if only one is in a vore container or if both are inside another container
    /// </returns>
    private bool IsValidContainment(EntityUid user, EntityUid target){
        var userInVore = IsInVoreContainer(user);
        var targetInVore = IsInVoreContainer(target);

        // one in vore, one not → invalid
        if (userInVore != targetInVore)
            return false;

        // both in vore → must be same stomach instance
        if (userInVore)
        {
            _containerSystem.TryGetContainingContainer(user, out var userContainer);
            _containerSystem.TryGetContainingContainer(target, out var targetContainer);

            if (userContainer!.Owner != targetContainer!.Owner)
                return false;
        }

        return true;
    }
}