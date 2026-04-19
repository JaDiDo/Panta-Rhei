using Robust.Shared.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Network;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Content.Shared._Floof.Vore;
using Content.Shared.Mind.Components;
using Content.Server.Bed.Cryostorage;
using Content.Shared.Bed.Cryostorage;
using Content.Server.Mind;
using Content.Server.Station.Systems;
using Content.Server.StationRecords.Systems;
using Content.Shared.StationRecords;
namespace Content.Server._Floof.Vore;

public sealed class DigestSystem : EntitySystem
{
    [Dependency] private readonly SharedContainerSystem _containerSystem = default!;
    [Dependency] private readonly SharedPopupSystem _popupSystem = default!;
    [Dependency] private readonly CryostorageSystem _cryo = default!;
    [Dependency] private readonly MindSystem _mind = default!;
    [Dependency] private readonly StationSystem _station = default!;
    [Dependency] private readonly StationRecordsSystem _stationRecords = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<DigestComponent, GetVerbsEvent<Verb>>(OnGetVerbs);
    }

    /// <summary>
    /// 
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
        args.Verbs.Add(new Verb
        {
            Text = "Digest",
            Act = () => TryDigest(user)
        });
    }

    /// <summary>
    /// 
    /// </summary>
    private void TryDigest(EntityUid pred)
    {
        _popupSystem.PopupEntity("You begin digesting your prey...", pred, pred);
        var container = _containerSystem.EnsureContainer<Container>(pred, "vore_container");
        foreach (var prey in container.ContainedEntities){
            TurnOffCords(prey);
            _popupSystem.PopupEntity("You are being digested!", prey, prey);
            HideDeath(prey);
            RemoveFromManifest(prey);
            ReopenJob(prey);
            RemovePrey(prey);
        }
    }

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

//TODO your job should be opened
//https://github.com/Floof-Station/Panta-Rhei/blob/f6cd5617727b34f1bb7b700c79279aa4d84c8b4e/Content.Server/Bed/Cryostorage/CryostorageSystem.cs#L239
    private void ReopenJob(EntityUid prey){}

//TODO cords should be auto turned off
    private void TurnOffCords(EntityUid prey){}

//TODO the death notification should be hidden
//https://github.com/Floof-Station/Panta-Rhei/blob/f6cd5617727b34f1bb7b700c79279aa4d84c8b4e/Content.Shared/Mobs/Systems/MobStateSystem.cs
    private void HideDeath(EntityUid prey){}

//TODO items should be gone to erase your tracks (alongside you?)
    private void RemovePrey(EntityUid prey){}
}