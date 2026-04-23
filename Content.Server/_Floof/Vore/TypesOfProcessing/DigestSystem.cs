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
        // TODO removed only their in case of items
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
        while (query.MoveNext(out var pred, out var comp)){
            var remove = new List<EntityUid>();
            foreach (var prey in comp.Health.Keys){


                if (!EntityManager.EntityExists(prey)){
                    remove.Add(prey);
                    continue;
                }

    // digestion 
    //Console.WriteLine($"[Digest] {ToPrettyString(prey)}: {comp.Health[prey]}/{comp.Max}");
    if (comp.ActiveDigesting.Contains(prey))
    {
        if (!_containerSystem.TryGetContainingContainer(prey, out var container) ||
            container.ID != "vore_container")
        {
            remove.Add(prey);
            continue;
        }

        comp.Timer[prey] += frameTime;

        if (comp.Timer[prey] < 1f)
            continue;

        comp.Timer[prey] -= 1f;
        comp.Health[prey]--;

        if (comp.Health[prey] <= 0)
        {
            FinishDigest(prey);
            remove.Add(prey);
        }
        continue;
    }

    // regeneration
    comp.Timer[prey] += frameTime;
    if (comp.Timer[prey] < 1f)
        continue;
    comp.Timer[prey] -= 1f;
    if (comp.Health[prey] < comp.Max)
    {
        comp.Health[prey]++;
        continue;
    }
            }

            foreach (var p in remove){
                comp.Health.Remove(p);
                comp.Timer.Remove(p);
                comp.ActiveDigesting.Remove(p); // safety cleanup
            }
        }
    }
}