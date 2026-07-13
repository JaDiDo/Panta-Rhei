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
using Robust.Shared.Configuration;
using Content.Shared._DV.Carrying;
using Robust.Server.Player;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Audio.Systems;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Gibbing;
namespace Content.Server._Floof.Vore;

public sealed class PredSystem : EntitySystem
{
    [Dependency] private readonly SharedConsentSystem _consentSystem = default!;
    [Dependency] private readonly SharedContainerSystem _containerSystem = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfterSystem = default!;
    [Dependency] private readonly SharedPopupSystem _popupSystem = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly CarryingSystem _carryingSystem = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly MobStateSystem _mobStateSystem = default!;
    [Dependency] private readonly SharedAudioSystem _audioSystem = default!;
    [Dependency] private readonly DigestSystem _digestSystem = default!;

    public static readonly ProtoId<ConsentTogglePrototype> isPred = "PredVore";
    public static readonly ProtoId<ConsentTogglePrototype> isPrey = "PreyVore";
    public static readonly ProtoId<ConsentTogglePrototype> isDigest = "Digestable";
    
    public static readonly VerbCategory VoreGeneral =
        new("Vore", null);
    public static readonly VerbCategory VoreDigest =
        new("Digest", null);

    private readonly HashSet<EntityUid> _pendingConsentUpdates = new();

    public override void Initialize()
    {
        SubscribeLocalEvent<ConsentComponent, ComponentStartup>(OnConsentStartup);
        SubscribeLocalEvent<ConsentComponent, EntityConsentToggleUpdatedEvent>(OnConsentUpdated);

        SubscribeLocalEvent<BodyComponent, GetVerbsEvent<Verb>>(OnBodyGetVerbs);

        SubscribeLocalEvent<PredComponent, OnVoreDoAfter>(OnVoreDoAfter);
        SubscribeLocalEvent<PredComponent, BeingGibbedEvent>(OnGibbedRemoveContent);
        SubscribeLocalEvent<PredComponent, DestructionEventArgs>(OnDestroyedRemoveContent);
        SubscribeLocalEvent<PredComponent, PolymorphedEvent>(OnPolymorphedTransferContent);
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
    }

    /// <summary>
    /// gives the mob vore component when they updated their consent to be pred or prey
    /// in order to avoid giving every mob it one by one, timer needed to get the recent change
    /// </summary>
    private void OnConsentUpdated(EntityUid uid, ConsentComponent comp, EntityConsentToggleUpdatedEvent args){
        // only if the updated toggle is prey or pred
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
    /// also handles container if consent is off but preds container is still full/prey is inside vore container 
    /// </summary>
    private void ApplyVoreConsent(EntityUid uid){
        var hasPred = _consentSystem.HasConsent(uid, isPred);
        var hasPrey = _consentSystem.HasConsent(uid, isPrey);
        //TODO var for digest

        //give the mob the needed component to be able to see the verbs
        if (hasPrey){
            EnsureComp<PreyComponent>(uid);
        }
        else{
            if (TryComp<PredComponent>(uid, out var comp)){
            /* in case prey is inside a container immediately release them when they turn off prey consent
            works as an emergency leave for the prey*/    
                var safety = 0;
                while (_containerSystem.TryGetContainingContainer(uid, out var container)  && container.ID == comp.ContainerId){
                    if (++safety > 10) 
                        break;
                    if (!_containerSystem.Remove(uid, container))
                        break;
                }                
                RemComp<PreyComponent>(uid);
            }
            
        }

        if (hasPred){
            var pred = EnsureComp<PredComponent>(uid);
            var container = _containerSystem.EnsureContainer<Container>(uid, pred.ContainerId);
        }
        else{
            if (TryComp<PredComponent>(uid, out var comp)){
                // same for pred release all current prey after turning off consent
                if (!hasPred){
                    if (_containerSystem.TryGetContainer(uid, comp.ContainerId, out var container)){
                        _containerSystem.EmptyContainer(container);
                        _containerSystem.ShutdownContainer(container);
                    }
                }
                RemComp<PredComponent>(uid);
            }
        }
    }

    /// <summary>
    /// creates verbs inside the interaction menu for yourself and other mobs controlled by players
    /// only show up when the consent has been selected on both sides
    /// </summary>
    private void OnBodyGetVerbs(EntityUid uid, BodyComponent comp, GetVerbsEvent<Verb> args){
        // using command to turn on/off verb components
        if (!_cfg.GetCVar(VoreCVars.VoreEnabled))
            return;
        // only when reachable & interactable
        if (!args.CanInteract || !args.CanAccess)
            return;

        /* TODO MAKE PREY AND PRED SEPERATION VERBS INSTEAD OF HAVING BOTH IN ONE*/
        var user = args.User;
        var target = args.Target;

        if (HasComp<PredComponent>(user) || HasComp<PreyComponent>(user)){
            BuildVoreContainerVerbs(user, args);
        }

        if (TryComp<PredComponent>(user, out var predComp) && user == target){
            BuildSelfInteractionVerbs(user, predComp, args);
            BuildDigestVerbs(user, predComp, args);
        }
    }

    /// <summary>
    /// handles the verbs that control self inspection not including the different voretypes
    /// </summary>
    public void BuildSelfInteractionVerbs(EntityUid uid, PredComponent comp, GetVerbsEvent<Verb> args){
        if (!_containerSystem.TryGetContainer(uid, comp.ContainerId, out var container))
            return;
        if (container.ContainedEntities.Count > 0){
            args.Verbs.Add(new Verb
            {
                Text = "Release all prey",
                Category = VoreGeneral,
                Act = () => TryReleaseAllPrey(uid, comp)
            });

            foreach (var prey in container.ContainedEntities){
                if (!HasComp<BodyComponent>(prey))
                    continue;
                var preyName = Name(prey);
                args.Verbs.Add(new Verb
                {
                    Text = $"Release {preyName}",
                    Category = VoreGeneral,
                    Act = () => TryReleasePrey(uid, comp, prey)
                });
            }
        }
    }


    /// <summary>
    /// handles the verbs that control the container such as inserting/removing
    /// </summary>
    private void BuildVoreContainerVerbs(EntityUid uid, GetVerbsEvent<Verb> args){
        var user = args.User;
        var target = args.Target;
        
        // 1. devour (pred → prey)
        if (IsDevourable(user, target)){
            args.Verbs.Add(new Verb
            {
                Text = "Devour",
                Category = VoreGeneral,
                Act = () => TryVore(user, target)
            });
        }
        // 2. insert self (prey → pred)
        if (IsDevourable(target, user)){
                args.Verbs.Add(new Verb
                {
                    Text = "Insert Self",
                    Category = VoreGeneral,
                    Act = () => TryVore(target, user)
                });
        }
        // 3. insert someone else if you pull or carry them
        // PredComponent implies consent to feed other
        if (HasComp<PredComponent>(user)){
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
                        Category = VoreGeneral,
                        Act = () => TryVore(target, prey)
                    });
                }
            }
        }
    }

    /// <summary>
    /// creates a verb only showing up if the pred has any content in their stomach
    /// and only shows if at least one prey has consented to being digested
    /// </summary>
    public void BuildDigestVerbs(EntityUid uid, PredComponent comp, GetVerbsEvent<Verb> args){
        if (!_containerSystem.TryGetContainer(uid, comp.ContainerId, out var container))
            return;
        if (!TryComp<PreyComponent>(uid, out var preyComp)){
            return;
        }
        foreach (var prey in container.ContainedEntities){
            var preyName = Name(prey);

            if (_consentSystem.HasConsent(prey, isDigest) && !preyComp.ActiveDigesting.Contains(prey)){
                args.Verbs.Add(new Verb
                {
                    Text = $"Digest {preyName}",
                    Category = VoreDigest,
                    Act = () => _digestSystem.TryDigest(prey)
                });
            }

            //only shows up if the prey is currently being digested
            else if (preyComp.ActiveDigesting.Contains(prey)){
                args.Verbs.Add(new Verb
                {
                    Text = $"Stop digesting {preyName}",
                    Category = VoreDigest,
                    Act = () => _digestSystem.StopDigest(uid, prey)
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
    private void OnVoreDoAfter(EntityUid uid, PredComponent comp, OnVoreDoAfter args){
        //handles canceled events
        if (args.Cancelled || args.Handled)
            return;
        if (args.Target is not EntityUid prey)
            return;

        var pred = uid;
        if (!_containerSystem.TryGetContainer(uid, comp.ContainerId, out var container))
            return;

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

        //gulp sound only for both entities involved
        if (comp.SoundDevour != null){
            if (_playerManager.TryGetSessionByEntity(pred, out var predSession))
                _audioSystem.PlayEntity(comp.SoundDevour, predSession, pred);
            if (_playerManager.TryGetSessionByEntity(prey, out var preySession))
                _audioSystem.PlayEntity(comp.SoundDevour, preySession, pred);
        }

        EnsureEntityFree(pred, prey, comp);
        _containerSystem.Insert(prey, container);
    }

    /// <summary>
    /// makes sure the prey is not inside any other container such as
    /// bags or being carried by someone before being inserted into the pred
    /// </summary>
    private void EnsureEntityFree(EntityUid pred, EntityUid prey, PredComponent comp){
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
    /// will remove only the listed prey from the preds stomach at once
    /// </summary>
    private void TryReleasePrey(EntityUid pred, PredComponent comp, EntityUid prey){
        if (!_containerSystem.TryGetContainer(pred, comp.ContainerId, out var container))
            return;
        _containerSystem.Remove(prey, container);

        _popupSystem.PopupEntity("You have been released!", prey, prey);
        _popupSystem.PopupEntity($"You release {Name(prey)}.", pred, pred);
    }

    /// <summary>
    /// will remove all prey from the preds stomach at once
    /// </summary>
    private void TryReleaseAllPrey(EntityUid pred, PredComponent comp){
        if (!_containerSystem.TryGetContainer(pred, comp.ContainerId, out var container))
            return;
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
    private void OnGibbedRemoveContent(EntityUid uid, PredComponent comp, BeingGibbedEvent args){
        TryReleaseAllPrey(uid, comp);
    }

    /// <summary>
    /// in case the user gets destroyed through for example singulo or gibbing
    /// </summary>
    private void OnDestroyedRemoveContent(EntityUid uid, PredComponent comp, DestructionEventArgs args){
        TryReleaseAllPrey(uid, comp);
    }

    /// <summary>
    /// in case of polymorp scenarios such as kitsune release all the content
    /// </summary>
    private void OnPolymorphedTransferContent(EntityUid uid, PredComponent comp, PolymorphedEvent args){
        TryReleaseAllPrey(uid, comp);
    }
 
    /// <summary>
    /// checks if an entity is inside a vore container
    /// </summary>
    /// <returns>
    /// true if the entity is inside a vore container
    /// </returns>
    private bool IsInVoreContainer(EntityUid uid){
        if (!_containerSystem.TryGetContainingContainer(uid, out var container))
            return false;

        return TryComp<PredComponent>(container.Owner, out var comp) &&
               container.ID == comp.ContainerId;
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
        if (!IsValidVoreInteraction(user, target))
            return false;
        if (!_consentSystem.HasConsent(user, isPred) || !_consentSystem.HasConsent(target, isPrey))
            return false;
        if (_mobStateSystem.IsDead(target) || _mobStateSystem.IsCritical(target))
            return false;
        
        return true;
    }

    /// <summary>
    /// checks if prey is inside a vore container to only allow vore in the same container
    /// </summary>
    /// <returns>
    /// false if only one is in a vore container or if both are inside another container
    /// </returns>
    private bool IsValidVoreInteraction(EntityUid user, EntityUid target){
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