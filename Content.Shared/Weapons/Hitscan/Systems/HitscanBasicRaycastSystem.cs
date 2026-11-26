using System.Linq;
using System.Numerics;
using Content.Shared._Starlight.Weapons.Cover.Systems;
using Content.Shared._Starlight.Weapons.Hitscan.Events;
using Content.Shared.Administration.Logs;
using Content.Shared.Database;
using Content.Shared.Mech.Components;
using Content.Shared.Tag;
using Content.Shared.Weapons.Hitscan.Components;
using Content.Shared.Weapons.Hitscan.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
using Robust.Shared.Random;

namespace Content.Shared.Weapons.Hitscan.Systems;

public sealed partial class HitscanBasicRaycastSystem : EntitySystem
{
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly ISharedAdminLogManager _log = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly TagSystem _tag = default!;
    [Dependency] private readonly IRobustRandom _rand = default!;
    [Dependency] private readonly SharedProjectileCoverSystem _cover = default!;

    private EntityQuery<HitscanBasicVisualsComponent> _visualsQuery;

    public override void Initialize()
    {
        base.Initialize();

        _visualsQuery = GetEntityQuery<HitscanBasicVisualsComponent>();

        SubscribeLocalEvent<HitscanBasicRaycastComponent, HitscanTraceEvent>(OnHitscanFired);
    }

    private void OnHitscanFired(Entity<HitscanBasicRaycastComponent> ent, ref HitscanTraceEvent args)
    {
        var shooter = args.Shooter ?? args.Gun;
        // Starlight start - handle the shooter being the mech, not the pilot
        if (TryComp<MechPilotComponent>(shooter, out var pilotA))
            shooter = pilotA.Mech;
        // Starlight end

        var mapCords = _transform.ToMapCoordinates(args.FromCoordinates);
        var toMap = args.ToCoordinates.IsValid(EntityManager)
            ? _transform.ToMapCoordinates(args.ToCoordinates)
            : new MapCoordinates(mapCords.Position + args.ShotDirection * ent.Comp.MaxDistance, mapCords.MapId);
        var pointer = (toMap.Position - mapCords.Position).Length();

        var ray = new CollisionRay(mapCords.Position, args.ShotDirection, (int) ent.Comp.CollisionMask);
        var rayCastResults = _physics.IntersectRay(mapCords.MapId, ray, ent.Comp.MaxDistance, shooter, false).ToList();

        var lagEv = new HitscanLagCompensationEvent(args.Shooter, shooter, mapCords.MapId, mapCords.Position,
            args.ShotDirection, ent.Comp.MaxDistance, (int) ent.Comp.CollisionMask, rayCastResults);
        RaiseLocalEvent(ref lagEv);

        var result = SelectHit(ent, shooter, mapCords, args.ShotDirection, pointer, args.Target, rayCastResults, args.PredictionSeed);

        var distanceTried = result?.Distance ?? ent.Comp.MaxDistance;

        var isRoot = false;
        if (args.OutputTrace is null)
        {
            args.OutputTrace = new List<HitscanTrace>();
            isRoot = true;
        }

        args.OutputTrace.Add(GenerateTraceStep(args.FromCoordinates, distanceTried, args.ShotDirection.ToAngle(), result?.HitEntity));

        // Admin logging
        if (result?.HitEntity != null)
        {
            _log.Add(LogType.HitScanHit,
                $"{ToPrettyString(shooter):user} hit {ToPrettyString(result.Value.HitEntity):target}"
                + $" using {ToPrettyString(args.Gun):entity}.");
        }

        var data = new HitscanRaycastFiredData
        {
            ShotDirection = args.ShotDirection,
            Gun = args.Gun,
            Shooter = args.Shooter,
            HitEntity = result?.HitEntity,
            OutputTrace = args.OutputTrace,
            HitPosition = result?.HitPos,
            PredictionSeed = args.PredictionSeed,
        };

        var attemptEvent = new AttemptHitscanRaycastFiredEvent { Data = data };
        RaiseLocalEvent(ent, ref attemptEvent);

        if (attemptEvent.Cancelled)
        {
            if (isRoot)
                FireEffects(ent, args.OutputTrace, args.Shooter, args.Gun, args.PredictionSeed);
            return;
        }

        var hitEvent = new HitscanRaycastFiredEvent { Data = data };
        RaiseLocalEvent(ent, ref hitEvent);

        if (isRoot)
            FireEffects(ent, args.OutputTrace, args.Shooter, args.Gun, args.PredictionSeed);
    }

    private HitscanTrace GenerateTraceStep(EntityCoordinates fromCoordinates, float distance, Angle shotAngle, EntityUid? entity = null)
    {
        var fromXform = Transform(fromCoordinates.EntityId);

        var gridUid = fromXform.GridUid;
        if (gridUid != fromCoordinates.EntityId && TryComp(gridUid, out TransformComponent? gridXform))
        {
            var (_, gridRot, gridInvMatrix) = _transform.GetWorldPositionRotationInvMatrix(gridXform);
            var map = _transform.ToMapCoordinates(fromCoordinates);
            fromCoordinates = new EntityCoordinates(gridUid.Value, Vector2.Transform(map.Position, gridInvMatrix));
            shotAngle -= gridRot;
        }
        else
        {
            shotAngle -= _transform.GetWorldRotation(fromXform);
        }

        var shotVec = shotAngle.ToVec().Normalized();

        return new HitscanTrace
        {
            Angle = shotAngle,
            Distance = distance,
            // We don't draw muzzle or travel effects if we're at point-blank range, just impact effects
            MuzzleCoordinates = distance > 1f ? GetNetCoordinates(fromCoordinates.Offset(shotVec / 2)) : null,
            TravelCoordinates = distance > 1f ? GetNetCoordinates(fromCoordinates.Offset(shotVec * (distance + 0.5f) / 2)) : null,
            ImpactCoordinates = GetNetCoordinates(fromCoordinates.Offset(shotVec * distance)),
            ImpactedEnt = GetNetEntity(entity),
        };
    }

    private void FireEffects(EntityUid hitscan, List<HitscanTrace> traces, EntityUid? shooter, EntityUid gun, int? predictionSeed)
    {
        if (!_net.IsServer)
            return;

        if (!_visualsQuery.TryComp(hitscan, out var visuals))
            return;

        var hitscanEvent = new SharedGunSystem.HitscanEvent
        {
            MuzzleFlash = visuals.MuzzleFlash,
            TravelFlash = visuals.TravelFlash,
            ImpactFlash = visuals.ImpactFlash,
            Bullet = visuals.Bullet,
            Speed = visuals.Speed,
            Traces = traces,
            Shooter = GetNetEntity(shooter),
            Gun = GetNetEntity(gun),
            PredictionSeed = predictionSeed,
            Prototype = MetaData(hitscan).EntityPrototype?.ID,
        };

        var filter = Filter.Empty();
        var sampledPositions = traces
            .Select(x => GetCoordinates(x.MuzzleCoordinates ?? x.ImpactCoordinates))
            .Where(x => x.IsValid(EntityManager))
            .ToList();
        foreach (var pos in sampledPositions)
            filter.Merge(Filter.Pvs(pos, entityMan: EntityManager));

        RaiseNetworkEvent(hitscanEvent, filter);
    }
}
