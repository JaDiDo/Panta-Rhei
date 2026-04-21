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
    [Dependency] private readonly SharedConsentSystem _consent = default!;
    [Dependency] private readonly ChatSystem _chatSystem = default!;
    [Dependency] private readonly SharedSuitSensorSystem _suitSensor = default!;

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
        if (user != uid)
            return;
        if (!args.CanInteract || !args.CanAccess)
            return;
        var container = _containerSystem.EnsureContainer<Container>(user, "vore_container");
        
        //only shows verb if there is atleast one prey in the stomach
        if (container.ContainedEntities.Count == 0)
            return;
        // Only show verb if at least one prey has consented to digestion
        var hasConsentingPrey = false;
        foreach (var prey in container.ContainedEntities){
            if (_consent.HasConsent(prey, "Digestable")){
                hasConsentingPrey = true;
                break;
            }
        }
        if (!hasConsentingPrey)
            return;

        //will list all prey in the stomach that have consented to digestion and give the option to digest them
        foreach (var prey in container.ContainedEntities){
            if (!_consent.HasConsent(prey, "Digestable"))
                continue;
            var preyName = Name(prey);
            args.Verbs.Add(new Verb
            {
                Text = $"Digest {preyName}",
                Category = VerbCategory.Interaction,
                Act = () => TryDigest(prey)
            });
        }

        //TODO stop digestion
        /*
        args.Verbs.Add(new Verb
            {
                Text = $"Digest {preyName}",
                Category = VerbCategory.Interaction,
                Act = () => TryDigest(prey)
            });
            */
    }


    /// <summary>
    /// for consent purposes a prey must leave no trace
    /// thats why several methods are called to hide the prey and make it look like they went cryo instead of being digested
    /// </summary>
    private void TryDigest(EntityUid prey)
    {
//TODO        _popupSystem.PopupEntity("You begin digesting your prey...", pred, pred);
            //before complete digestion
            TurnOffCords(prey);
            _popupSystem.PopupEntity("You are being digested!", prey, prey);
            //after complete digestion
            //TODO instead of health damage artificial health?
            HideDeath(prey);
            CryoAnnounce(prey);
            RemoveFromManifest(prey);
            ReopenJob(prey);
            RemovePrey(prey);
            //TODO remove "escape" message

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
    /// will turn off suit sensors so that the preys location wont be visible
    /// </summary>
    private void TurnOffCords(EntityUid prey){
        _suitSensor.SetAllSensors(prey, SuitSensorMode.SensorOff);
    }

//TODO the death notification should be hidden
//https://github.com/Floof-Station/Panta-Rhei/blob/f6cd5617727b34f1bb7b700c79279aa4d84c8b4e/Content.Shared/Mobs/Systems/MobStateSystem.cs
    private void HideDeath(EntityUid prey){
        // TODO
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

    // Find their job on this station
        if (TryComp<StationJobsComponent>(station.Value, out var stationJobs)){
            if (_stationJobs.TryGetPlayerJobs(station.Value, userId.Value, out var jobs, stationJobs))
            {
                foreach (var job in jobs)
                {
                    jobName = job; // take first job
                    break;
                }
            }
        }
    //No job means no announcement
    if (jobName == null)
        return;
    //TODO ADD ENTITY
        _chatSystem.DispatchStationAnnouncement(
            station.Value,
            Loc.GetString(
                "earlyleave-cryo-announcement",
                ("character", name),
                //("entity", ent.Owner),
                ("job", jobName)
            ),
            Loc.GetString("earlyleave-cryo-sender"),
            playDefaultSound: false
        );
    }

//TODO items should be gone to erase your tracks (alongside you?)
    private void RemovePrey(EntityUid prey)
    {
        //TODO needs to be cancelable
        //TODO readd it after testing
        //QueueDel(prey);
    }

}