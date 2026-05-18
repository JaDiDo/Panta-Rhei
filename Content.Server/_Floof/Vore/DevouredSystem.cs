using Content.Server.Atmos.Components;
using Content.Shared._Shitmed.Body.Components;
using Content.Shared._DV.CosmicCult.Components;
using Content.Shared._Floof.Vore;
using Content.Shared.Body.Events;
using Content.Shared.Damage.Systems;
using Content.Shared.Flash.Components;
using Content.Shared.Medical.SuitSensor;
using Content.Shared.Medical.SuitSensors;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Containers;
using Content.Shared.Flash.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Popups;
namespace Content.Server._Floof.Vore;

public sealed class VoreImmunitySystem : EntitySystem
{
    [Dependency] private readonly SharedContainerSystem _containerSystem = default!;
    [Dependency] private readonly MobStateSystem _mobStateSystem = default!;
    [Dependency] private readonly SharedSuitSensorSystem _suitSensorSystem = default!;
    [Dependency] private readonly SharedPopupSystem _popupSystem = default!;
    
    private readonly HashSet<EntityUid> _pendingImmunityUpdates = new();

    public override void Initialize()
    {
        SubscribeLocalEvent<VoreComponent, EntRemovedFromContainerMessage>(OnPreyRemovedFromContainer);
        
        SubscribeLocalEvent<DevouredComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<DevouredComponent, MobStateChangedEvent>(OnPreyMobStateChanged);
        SubscribeLocalEvent<DevouredComponent, BeforeDamageChangedEvent>(OnBeforeDamageChanged);
    }
/*
    public void QueuePreyForImmunityRemoval(EntityUid prey){
        _pendingImmunityUpdates.Add(prey);
    }
*/
    private void OnStartup(EntityUid uid, DevouredComponent comp, ComponentStartup args){
        ApplyStomachImmunities(uid);
    }

    public override void Update(float frameTime){
        base.Update(frameTime);

        foreach (var uid in _pendingImmunityUpdates)
        {
            RemoveStomachImmunities(uid);
        }

        _pendingImmunityUpdates.Clear();
    }

    /// <summary>
    /// responsible for removing components and immunities
    /// </summary>
    private void OnPreyRemovedFromContainer(EntityUid uid, VoreComponent comp, EntRemovedFromContainerMessage args){
        if (TryComp<DevouredComponent>(args.Entity, out _)){
            _pendingImmunityUpdates.Add(args.Entity);
        }
    }

    /// <summary>
    /// in case the prey died/crit they need to be ejected from ALL vorecontainers
    /// this way a para wont accidentally stumble on a scene and the corpse wont rot
    /// </summary>
    private void OnPreyMobStateChanged(EntityUid uid, DevouredComponent comp, ref MobStateChangedEvent args){
        if (args.NewMobState != MobState.Dead && args.NewMobState != MobState.Critical)
            return;

        if (!TryComp<VoreComponent>(uid, out var vore))
            return;

        while (_containerSystem.TryGetContainingContainer(uid, out var container) && container.ID == vore.ContainerId){
            _containerSystem.Remove(uid, container);
        }
    }

    /// <summary>
    /// will nullify any damage when you are inside a vorecontainer for consent purposes
    /// </summary>
    private void OnBeforeDamageChanged(EntityUid uid, DevouredComponent comp, ref BeforeDamageChangedEvent args){
        if (!IsInVoreContainer(uid))
            return;
        args.Cancelled = true;
    }

    /// <summary>
    /// checks if an entity is inside a vore container
    /// </summary>
    /// <returns>
    /// true if the entity is inside a vore container
    /// </returns>
    private bool IsInVoreContainer(EntityUid uid){
        if (!TryComp<VoreComponent>(uid, out var comp))
            return false;
        return _containerSystem.TryGetContainingContainer(uid, out var container) &&
               container.ID == comp.ContainerId;
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

        if (!TryComp<DevouredComponent>(prey, out var tracker))
            return;

        if (!HasComp<PressureImmunityComponent>(prey)){
            EnsureComp<PressureImmunityComponent>(prey);
            tracker.AddedPressure = true;
        }

        if (!HasComp<BreathingImmunityComponent>(prey)){
            EnsureComp<BreathingImmunityComponent>(prey);
            tracker.AddedBreathing = true;
        }

        if (!HasComp<TemperatureImmunityComponent>(prey)){
            EnsureComp<TemperatureImmunityComponent>(prey);
            tracker.AddedTemperature = true;
        }

        if (!HasComp<FlashImmunityComponent>(prey)){
            EnsureComp<FlashImmunityComponent>(prey);
            tracker.AddedFlash = true;
        }
        _suitSensorSystem.SetAllSensors(prey, SuitSensorMode.SensorOff);
    }

    /// <summary>
    /// the removal of the devouredcomponent and immunities after leaving a container
    /// to avoid intentional and accidental exploitation
    /// </summary>
    private void RemoveStomachImmunities(EntityUid prey){
        // if still in a container skip alltogether for example release from multi vore
        if (IsInVoreContainer(prey))
            return;
        if (!TryComp<DevouredComponent>(prey, out var tracker))
            return;

        if (tracker.AddedPressure){
            RemComp<PressureImmunityComponent>(prey);
            tracker.AddedPressure = false;
        }
        if (tracker.AddedBreathing){
            RemComp<BreathingImmunityComponent>(prey);
            tracker.AddedBreathing = false;
        }
        if (tracker.AddedTemperature){
            RemComp<TemperatureImmunityComponent>(prey);
            tracker.AddedTemperature = false;
        }

        if (tracker.AddedFlash){
            RemComp<FlashImmunityComponent>(prey);
            tracker.AddedFlash = false;
        }
        _suitSensorSystem.SetAllSensors(prey, SuitSensorMode.SensorCords);
        RemComp<DevouredComponent>(prey);
    }
}