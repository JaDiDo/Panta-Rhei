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

    public override void Initialize()
    {
        SubscribeLocalEvent<DigestComponent, GetVerbsEvent<Verb>>(OnGetVerbs);
    }

    /// <summary>
    /// creates a verb only showing up if the pred has any content in their stomach
    /// only shows if at least one prey has consented to being digested
    /// </summary>
    private void OnGetVerbs(EntityUid uid, DigestComponent comp, GetVerbsEvent<Verb> args)
    {
        var user = args.User;
        if (user != uid)
            return;

        if (!args.CanInteract || !args.CanAccess)
            return;
        var container = _containerSystem.EnsureContainer<Container>(user, "vore_container");
        if (container.ContainedEntities.Count == 0)
            return;

        // Only show verb if at least one prey has consented to digestion
        var hasConsentingPrey = false;
        foreach (var prey in container.ContainedEntities)
        {
            if (_consent.HasConsent(prey, "Digestable"))
            {
                hasConsentingPrey = true;
                break;
            }
        }

        if (!hasConsentingPrey)
            return;

    foreach (var prey in container.ContainedEntities)
    {
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
    }


    /// <summary>
    /// for consent purposes a prey must leave no trace
    /// also job slots should be opened hence treating it like going cryo
    /// only digests prey that have consented to the Digestable toggle
    /// </summary>
    private void TryDigest(EntityUid prey)
    {
//TODO        _popupSystem.PopupEntity("You begin digesting your prey...", pred, pred);
            TurnOffCords(prey);
            _popupSystem.PopupEntity("You are being digested!", prey, prey);
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
    private void RemoveFromManifest(EntityUid prey)
    {
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

//TODO cords should be auto turned off
    private void TurnOffCords(EntityUid prey)
    {
        //     DisableCord(cord);
    }

//TODO the death notification should be hidden
//https://github.com/Floof-Station/Panta-Rhei/blob/f6cd5617727b34f1bb7b700c79279aa4d84c8b4e/Content.Shared/Mobs/Systems/MobStateSystem.cs
    private void HideDeath(EntityUid prey)
    {
        // TODO
    }

//TODO might need a delay to not interrupt the scene)
    /// <summary>
    /// Will classify you as going cryo to the station and announce it as such
    /// to avoid people reporting you as missing
    /// </summary>
    private void CryoAnnounce(EntityUid prey)
    {
        var station = _station.GetOwningStation(prey);
        if (station == null)
            return;
        var name = Name(prey);
    //TODO ADD JOB AND ENTITY
        _chatSystem.DispatchStationAnnouncement(
            station.Value,
            Loc.GetString(
                "earlyleave-cryo-announcement",
                ("character", name),
                //("entity", ent.Owner),
                //TODO JOB
                ("job", "Unknown")
            ),
            Loc.GetString("earlyleave-cryo-sender"),
            playDefaultSound: false
        );
    }

//TODO items should be gone to erase your tracks (alongside you?)
    private void RemovePrey(EntityUid prey)
    {
        QueueDel(prey);
    }

}