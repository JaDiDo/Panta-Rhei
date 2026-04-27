using Robust.Shared.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Network;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Content.Shared._Floof.Vore;
using Content.Shared.Mind.Components;
using Content.Shared._Common.Consent;
using Content.Server.Mind;
using Content.Server.Station.Components;
using Content.Server.Station.Systems;
using Content.Server.StationRecords.Systems;
using Content.Shared.StationRecords;
using Content.Server.Chat.Systems;
using Content.Shared.Medical.SuitSensors;
using Content.Shared.Medical.SuitSensor;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Content.Server.Nutrition.EntitySystems;
//using Content.Server.Power.Components;
//using Content.Server.Power.EntitySystems;
//using Content.Shared.PowerCell.Components;
namespace Content.Server._Floof.Vore;

public sealed class DigestSystem : EntitySystem
{
    [Dependency] private readonly SharedContainerSystem _containerSystem = default!;
    [Dependency] private readonly SharedPopupSystem _popupSystem = default!;
    [Dependency] private readonly MindSystem _mind = default!;
    [Dependency] private readonly StationSystem _station = default!;
    [Dependency] private readonly StationRecordsSystem _stationRecords = default!;
    [Dependency] private readonly StationJobsSystem _stationJobs = default!;
    [Dependency] private readonly SharedConsentSystem _consentSystem = default!;
    [Dependency] private readonly ChatSystem _chatSystem = default!;
    [Dependency] private readonly SharedSuitSensorSystem _suitSensorSystem = default!;
    [Dependency] private readonly HungerSystem _hunger = default!;
    //[Dependency] private readonly BatterySystem _battery = default!;


    public override void Initialize()
    {
        SubscribeLocalEvent<DigestComponent, GetVerbsEvent<Verb>>(OnGetVerbs);
    }

    /// <summary>
    /// creates a verb only showing up if the pred has any content in their stomach
    /// and only shows if at least one prey has consented to being digested
    /// </summary>
    private void OnGetVerbs(EntityUid uid, DigestComponent comp, GetVerbsEvent<Verb> args)
    {
        var user = args.User;

        //prevents self interaction
        if (user != uid)
            return;
        
        // only when reachable & interactable
        if (!args.CanInteract || !args.CanAccess)
            return;
        
        if (!_containerSystem.TryGetContainer(uid, "vore_container", out var container))
            return;
        
        //only shows verb if there is atleast one prey in the stomach
        if (container.ContainedEntities.Count == 0)
            return;

        //goes through all prey inside the stomach
        foreach (var prey in container.ContainedEntities){
            var preyName = Name(prey);
            
            //only shows verb if the prey has consented to being digested
            if (_consentSystem.HasConsent(prey, "Digestable") && !comp.ActiveDigesting.Contains(prey)){
                args.Verbs.Add(new Verb
                {
                    Text = $"Digest {preyName}",
                    Category = VerbCategory.Interaction,
                    Act = () => TryDigest(prey)
                });
            }

            //only shows up if the prey is currently being digested
            else if (comp.ActiveDigesting.Contains(prey)){
                args.Verbs.Add(new Verb
                {
                    Text = $"Stop digesting {preyName}",
                    Category = VerbCategory.Interaction,
                    Act = () => StopDigest(user, prey)
                });
            }
        }            
    }

    /// <summary>
    /// main method of digestion, will start the digestion process and apply the required effects to the prey and predator
    /// also turns off suit sensors to prevent any possible interaction with them during digestion
    /// </summary>
    private void TryDigest(EntityUid prey){
        if (!_containerSystem.TryGetContainingContainer(prey, out var container))
            return;
        var pred = container.Owner;
        if (!TryComp<DigestComponent>(pred, out var comp)) 
            return;

        _popupSystem.PopupEntity("You begin digesting your prey...", pred, pred);
        _popupSystem.PopupEntity("You are being digested!", prey, prey, PopupType.LargeCaution);
        
        //used to track the digestion progress and the active digestion status of the prey
        comp.Health.TryAdd(prey, comp.Max);
        comp.ActiveDigesting.Add(prey);
        comp.Timer[prey] = 0f;

        //as a way of prevention of any interaction with the active digestion scene
        _suitSensorSystem.SetAllSensors(prey, SuitSensorMode.SensorOff);
    }

    /// <summary>
    /// Will stop the active digestion of a prey inside of the container
    /// </summary>
    private void StopDigest(EntityUid pred, EntityUid prey){
        if (!TryComp<DigestComponent>(pred, out var comp))
            return;
        comp.ActiveDigesting.Remove(prey);
        comp.Timer[prey] = 0f;

        _popupSystem.PopupEntity("Digestion stopped.", pred, pred);
        _popupSystem.PopupEntity("Digestion stopped.", prey, prey);
    }

    /// <summary>
    /// activates the requiered methods after digestion to prevent 
    /// any consent breaches from the preys existence
    /// </summary>
    private void FinishDigest(EntityUid prey){
        CryoAnnounce(prey);
        RemoveFromManifest(prey);
        ReopenJob(prey);
        if (_containerSystem.TryGetContainingContainer(prey, out var container) &&
        TryComp<DigestComponent>(container.Owner, out var comp)){
            comp.ActiveDigesting.Remove(prey);
            comp.Health.Remove(prey);
            comp.Timer.Remove(prey);
            comp.DigestPopupStage.Remove(prey);
            if (TryComp<VoreComponent>(prey, out var preyComp))
                preyComp.IntentionalRelease = true;
            _popupSystem.PopupEntity($"You feel lighter as you feel your belly shrinks down in size", container.Owner, container.Owner);
        }   
        QueueDel(prey);
    }

    /// <summary>
    /// will remove the prey from the manifest so people wont report him as missing from their job
    /// </summary>
    private void RemoveFromManifest(EntityUid prey){
        var station = _station.GetOwningStation(prey);
        if (station == null || !TryComp<StationRecordsComponent>(station, out var stationRecords))
            return;

        var name = Name(prey);
        var recordId = _stationRecords.GetRecordByName(station.Value, name);
        if (recordId != null)
        {
            var key = new StationRecordKey(recordId.Value, station.Value);
            _stationRecords.RemoveRecord(key, stationRecords);
        }
    }
    
    /// <summary>
    /// will reopen a job slot so the stations performance wont be affected
    /// </summary>
    private void ReopenJob(EntityUid prey)
    {
        if (!_mind.TryGetMind(prey, out var mindId, out var mindComp))
            return;
        var userId = mindComp.UserId;
        if (userId == null)
            return;
        // Add back the job slots for all stations
        foreach (var station in _station.GetStationsSet())
        {
            if (!TryComp<StationJobsComponent>(station, out var stationJobs))
                continue;
            if (!_stationJobs.TryGetPlayerJobs(station, userId.Value, out var jobs, stationJobs))
                continue;
            foreach (var job in jobs)
            {
                _stationJobs.TryAdjustJobSlot(station, job, 1, clamp: true);
            }
            _stationJobs.TryRemovePlayerJobs(station, userId.Value, stationJobs);
        }
    }

    /// <summary>
    /// Will classify you as going cryo to the station and announce it as such
    /// to avoid people reporting you as missing
    /// has been requested after a vote that won by 68 percent
    /// </summary>
//TODO might need a delay to not interrupt the scene)
    private void CryoAnnounce(EntityUid prey){
        var station = _station.GetOwningStation(prey);
        if (station == null)
            return;
        var name = Name(prey);
        if (!_mind.TryGetMind(prey, out var mindId, out var mindComp))
        return;

        var userId = mindComp.UserId;
        if (userId == null)
            return;

        string? jobName = null;

        if (TryComp<StationJobsComponent>(station.Value, out var stationJobs)){
            if (_stationJobs.TryGetPlayerJobs(station.Value, userId.Value, out var jobs, stationJobs))
            {
                foreach (var job in jobs)
                {
                    jobName = job; 
                    break;
                }
            }
        }

        
        //No job means no announcement
        if (jobName == null)
            return;

        _chatSystem.DispatchStationAnnouncement(
            station.Value,
            Loc.GetString(
                "earlyleave-cryo-announcement",
                ("character", name),
                ("entity", GetNetEntity(prey)),
                ("job", jobName)
            ),
            Loc.GetString("earlyleave-cryo-sender"),
            playDefaultSound: false
        );
    }


    /// <summary>
    /// main update loop for digestion or healing progress till a prey is fully digested or healed
    /// also checks for any possible issues with the prey like deletion or being removed from the container and stops the digestion if any of those happen
    /// </summary>
    public override void Update(float frameTime){
        var query = EntityQueryEnumerator<DigestComponent>();
        // goes through all predators with a digest component
        while (query.MoveNext(out var pred, out var comp)){
            var remove = new List<EntityUid>();
            foreach (var prey in comp.Health.Keys){

                // timer for 1 second intervals
                    comp.Timer[prey] += frameTime;
                    if (comp.Timer[prey] < 1f)
                        continue;
                    comp.Timer[prey] -= 1f;

                //in case prey no longer exists
                if (!EntityManager.EntityExists(prey)){
                    remove.Add(prey);
                    continue;
                }

                // digestion path 
                if (comp.ActiveDigesting.Contains(prey)){
                    
                    // in case prey is removed from container stop digestion and go through regeneration path
                    // or in case consent is removed during digestion
                    if (!_containerSystem.TryGetContainingContainer(prey, out var container) ||
                    container.ID != "vore_container" ||
                    !_consentSystem.HasConsent(prey, "Digestable")){
                        comp.ActiveDigesting.Remove(prey);
                        comp.Timer[prey] = 0f;
                        continue;
                    }

                    // digestion process, reduces health of prey and increases hunger of predator every second
                    //also show a popup to the prey as a way of feedback
                    comp.Health[prey] -= 0.5f;
                    ShowDigestPopup(pred, prey, comp);
                    if (TryComp<HungerComponent>(container.Owner, out var hunger)){
                        /* for testing purposes
                        Console.WriteLine($"[Digest] {ToPrettyString(prey)}: {comp.Health[prey]}/{comp.Max}");
                        Console.WriteLine($"[Digest] Increasing hunger for {ToPrettyString(container.Owner)}. Current hunger: {_hunger.GetHunger(hunger)}");
                        */
                        _hunger.ModifyHunger(container.Owner, 1, hunger);
                    }
                    //TODOif (TryComp<BatteryComponent>(container.Owner, out var internalbattery)){
                    // _battery.SetCharge(container.Owner, internalbattery.CurrentCharge + 2, internalbattery);
                    //}

                    //once health reaches 0 finish digestion and remove prey from tracking
                    if (comp.Health[prey] <= 0){
                        FinishDigest(prey);
                        remove.Add(prey);
                    }
                    continue;
                }
                
                // regeneration path
                // fun fact principle is kinda like trophic level in ecology!
                else{

                    //if the prey is not being digested will regenerate health every second till it reaches max health or the hunger is too low
                    if (TryComp<HungerComponent>(prey, out var preyHunger)){
                        if (_hunger.GetHunger(preyHunger) > 50 && comp.Health[prey] < comp.Max){
                            /* for testing purposes
                            Console.WriteLine($"[Digest] {ToPrettyString(prey)}: {comp.Health[prey]}/{comp.Max}");
                            Console.WriteLine($"[Digest] {ToPrettyString(prey)} is regenerating. Hunger: {_hunger.GetHunger(preyHunger)}");
                            */
                            comp.Health[prey] += 0.1f;
                            _hunger.ModifyHunger(prey, -1f, preyHunger);
                            continue;
                        }
                    }

                    //TODO --- BATTERY-BASED HEALING ---
                    /*if (TryComp<BatteryComponent>(prey, out var preyBattery))
                    {
                        if (preyBattery.CurrentCharge > 50 && comp.Health[prey] < comp.Max)
                        {
                            comp.Health[prey] += 0.1f;
                            //_battery.SetCharge(prey, preyBattery.CurrentCharge - 1);
                        }
                    }*/
                }

                // safety check to remove any prey that might have been left in the tracking after digestion or deletion
                foreach (var p in remove){
                    comp.Health.Remove(p);
                    comp.Timer.Remove(p);
                    comp.ActiveDigesting.Remove(p); 
                    comp.DigestPopupStage.Remove(prey);
                }
            }
        }
    }

    /// <summary>
    /// shows a popup to the prey based on the state of digestion as a form of feedback
    /// </summary>
    private void ShowDigestPopup(EntityUid pred, EntityUid prey, DigestComponent comp){
        var health = comp.Health[prey];
        var max = comp.Max;
        var percent = health / max;

        int stage = 0;

        if (percent <= 0.10f)
            stage = 4;
        else if (percent <= 0.25f)
            stage = 3;
        else if (percent <= 0.50f)
            stage = 2;
        else if (percent <= 0.75f)
            stage = 1;

        if (stage == 0)
            return;

        // in case the stage has already been shown for the prey dont show it again
        if (comp.DigestPopupStage.TryGetValue(prey, out var lastStage) && lastStage >= stage)
            return;
        // Mark this stage as shown
        comp.DigestPopupStage[prey] = stage;

        string? message = stage switch{
            1 => "You feel your body softening inside the stomach.",
            2 => "It feels harder to stay conscious as your body melts.",
            3 => "You body begins to lose its shape.",
            4 => "You can barely remain conscious as your body is almost fully gone",
            _ => null
        };

        if (message != null)
            _popupSystem.PopupEntity(message, prey, prey);
    }
}