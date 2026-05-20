using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Containers;
using Content.Shared.Body.Components;
using Content.Shared.DoAfter;
using Content.Shared.Popups;
using Content.Shared.FloofStation;
using Content.Shared._Floof.Vore;
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
using Robust.Server.Audio;
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
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;

    public static readonly ProtoId<ConsentTogglePrototype> isPred = "PredVore";
    public static readonly ProtoId<ConsentTogglePrototype> isPrey = "PreyVore";
    public static readonly ProtoId<ConsentTogglePrototype> isDigest = "Digestable";
    
    private readonly HashSet<EntityUid> _pendingConsentUpdates = new();

    public override void Initialize()
    {
        SubscribeLocalEvent<ConsentComponent, ComponentStartup>(OnConsentStartup);
        SubscribeLocalEvent<ConsentComponent, EntityConsentToggleUpdatedEvent>(OnConsentUpdated);

        SubscribeLocalEvent<VoreComponent, GetVerbsEvent<Verb>>(OnGetVerbs);
        SubscribeLocalEvent<VoreComponent, OnVoreDoAfter>(OnVoreDoAfter);
        SubscribeLocalEvent<VoreComponent, BeingGibbedEvent>(OnGibbedRemoveContent);
        SubscribeLocalEvent<VoreComponent, DestructionEventArgs>(OnDestroyedRemoveContent);
        SubscribeLocalEvent<VoreComponent, PolymorphedEvent>(OnPolymorphedTransferContent);
    }

    /// <summary>
    /// To get the most recent values of consent for the verbs
    /// </summary>
    public override void Update(float frameTime){
        base.Update(frameTime);
        foreach (var uid in _pendingConsentUpdates){
            if (!HasComp<ConsentComponent>(uid))
                continue;
            ApplyVoreConsent(uid);
        }
        _pendingConsentUpdates.Clear();
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
        
        /* in case prey is inside a container immediately release them when they turn off prey consent
        works as an emergency leave for the prey*/
        if (!hasPrey && IsInVoreContainer(uid) &&
        _containerSystem.TryGetContainingContainer(uid, out var container)){
            _containerSystem.Remove(uid, container);
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
        EntityManager.System<DigestSystem>().BuildDigestVerbs(uid, comp, args);
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
        // subscription implies has vorecomponent -> prey or pred turned on for consent
        EntityUid? carried = null;
        if (TryComp<CarryingComponent>(user, out var carrying) && carrying.Carried != default)
            carried  = carrying.Carried;
        else if (TryComp<PullerComponent>(user, out var puller) && puller.Pulling is EntityUid pulling)
            carried  = pulling;
        
        if (carried != null && carried is EntityUid prey && prey != target){
            if (IsDevourable(target, prey)){
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
        EnsureComp<DevouredComponent>(prey);
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
    /// only to handle the release of prey
    /// immunities will be removed inside the devouredsystem
    /// </summary>
    private void TryReleasePrey(EntityUid pred, VoreComponent comp){
        var container = _containerSystem.EnsureContainer<Container>(pred, comp.ContainerId);
        var preyList = new List<EntityUid>(container.ContainedEntities);
        //remove everything from people to items
        foreach (var prey in preyList){
            _containerSystem.Remove(prey, container);
            _popupSystem.PopupEntity("You have been released!", prey, prey);
        }
        _popupSystem.PopupEntity("You release your prey.", pred, pred);
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
    /// checks if an entity is inside a vore container
    /// </summary>
    /// <returns>
    /// true if the entity is inside any vore container
    /// </returns>
    private bool IsInVoreContainer(EntityUid uid){
        if(!TryComp<VoreComponent>(uid, out var comp))
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