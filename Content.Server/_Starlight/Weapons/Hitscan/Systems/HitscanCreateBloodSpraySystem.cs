using System.Linq;
using System.Numerics;
using Content.Server._Starlight.Weapons.Hitscan.Components;
using Content.Server.Decals;
using Content.Shared.Body.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Decals;
using Content.Shared.Weapons.Hitscan.Events;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Starlight.Weapons.Hitscan.Systems;

public sealed partial class HitscanCreateBloodSpraySystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly DecalSystem _decal = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solutionContainer = default!;

    private string[] _bloodDecals = Array.Empty<string>();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<HitscanCreateBloodSprayComponent, HitscanDamageDealtEvent>(OnHitscanHit);
        CacheDecals();
    }

    private void CacheDecals()
    {
        _bloodDecals = _proto.EnumeratePrototypes<DecalPrototype>()
            .Where(x => x.Tags.Contains("BloodSplatter"))
            .Select(x => x.ID)
            .ToArray();
    }

    private void OnHitscanHit(Entity<HitscanCreateBloodSprayComponent> ent, ref HitscanDamageDealtEvent args)
    {
        if (_bloodDecals.Length == 0 || args.Data.HitEntity is not { } hitEnt)
            return;

        if (!TryComp<BloodstreamComponent>(hitEnt, out var bloodstream))
            return;

        var data = args.Data;

        var gunShooingXform = Transform(args.Data.Gun);

        // Copied from HitscanBasicRaycastSystem
        var shotAngle = data.ShotDirection.ToAngle();
        if (TryComp(gunShooingXform.GridUid, out TransformComponent? gridXform))
        {
            var (_, gridRot, _) = _transform.GetWorldPositionRotationInvMatrix(gridXform);
            shotAngle -= gridRot;
        }
        else
        {
            return; // TODO: Add logic that actually works for off grid shots.
        }

        var distance = Math.Abs((Transform(hitEnt).Coordinates.Position - Transform(args.Data.Gun).Coordinates.Position).Length());
        var hitEntityCords = Transform(hitEnt).Coordinates;
        var color = Color.DarkRed;
        if (_solutionContainer.ResolveSolution(hitEnt, bloodstream.BloodSolutionName, ref bloodstream.BloodSolution, out var bloodSol))
            color = bloodSol.GetColor(_proto);
        var coords = hitEntityCords.Offset((shotAngle.ToVec() * ((distance / 5000.0f) + 1.3f)) + new Vector2(-0.5f, -0.5f));

        Timer.Spawn(200, () =>
        {
            if (_bloodDecals.Length == 0)
                return;
            _decal.TryAddDecal(_random.Pick(_bloodDecals), coords, out _, color, shotAngle + Angle.FromDegrees(-45), cleanable: true);
        });
    }
}
