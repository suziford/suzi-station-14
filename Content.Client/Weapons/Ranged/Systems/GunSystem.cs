// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Linq;
using System.Numerics;
using Content.Client.Animations;
using Content.Client.Clickable;
using Content.Client.DisplacementMap;
using Content.Client.Items;
using Content.Client.Weapons.Ranged.Components;
using Content.Shared._Goobstation.Heretic.Components;
using Content.Shared._Starlight.CCVar;
using Content.Shared._Starlight.Combat.Ranged.Pierce;
using Content.Shared._Starlight.Effects;
using Content.Shared._Starlight.Utility;
using Content.Shared._Starlight.Weapons.Hitscan.Events;
using Content.Shared.Camera;
using Content.Shared.CCVar;
using Content.Shared.CombatMode;
using Content.Shared.Damage;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Physics;
using Content.Shared.Weapons.Hitscan.Components;
using Content.Shared.Mech.Components; // Goobstation
using Content.Shared.Weapons.Ranged;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Client.Animations;
using Robust.Client.ComponentTrees;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.Player;
using Robust.Client.State;
using Robust.Shared.Animations;
using Robust.Shared.Audio;
using Robust.Shared.Configuration;
using Robust.Shared.Graphics;
using Robust.Shared.Input;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using SharedGunSystem = Content.Shared.Weapons.Ranged.Systems.SharedGunSystem;
using TimedDespawnComponent = Robust.Shared.Spawners.TimedDespawnComponent;

namespace Content.Client.Weapons.Ranged.Systems;

public sealed partial class GunSystem : SharedGunSystem
{
    [Dependency] private readonly AnimationPlayerSystem _animPlayer = default!;
    [Dependency] private readonly IEyeManager _eyeManager = default!;
    [Dependency] private readonly IInputManager _inputManager = default!;
    [Dependency] private readonly InputSystem _inputSystem = default!;
    [Dependency] private readonly IOverlayManager _overlayManager = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IStateManager _state = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly SharedCameraRecoilSystem _recoil = default!;
    [Dependency] private readonly SharedMapSystem _maps = default!;
    [Dependency] private readonly SharedTransformSystem _xform = default!;
    [Dependency] private readonly SpriteSystem _sprite = default!;
    [Dependency] private readonly SpriteTreeSystem _spriteTree = default!;
    [Dependency] private readonly ClickableSystem _clickable = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    // Starlight
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly DisplacementMapSystem _displacement = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    public static readonly EntProtoId HitscanProto = "HitscanEffect";
    public const string ImpactProto = "ImpactEffect";
    public const string BulletHoleProto = "BulletHoleEffect";
    public const string SparksProto = "ImpactSparksEffect";
    private DisplacementEffect _displacementEffect = null!;
    private static readonly ProtoId<DisplacementEffect> _displacementEffectId = "ImpactDisplacement";
    private bool _tracesEnabled = true;
    private bool _holesEnabled = true;
    private bool _sparksEnabled = true;
    private GunTargetEntityComparer _comparer = default!;

    public override void Shutdown()
    {
        base.Shutdown();
        _cfg.UnsubValueChanged(StarlightCCVars.TracesEnabled, OnTracesEnabledChanged);
        _cfg.UnsubValueChanged(StarlightCCVars.HolesEnabled, OnHolesEnabledChanged);
        _cfg.UnsubValueChanged(StarlightCCVars.SparksEnabled, OnSparksEnabledChanged);
    }

    private void OnTracesEnabledChanged(bool tracesEnabled) => _tracesEnabled = tracesEnabled;
    private void OnHolesEnabledChanged(bool holesEnabled) => _holesEnabled = holesEnabled;
    private void OnSparksEnabledChanged(bool sparksEnabled) => _sparksEnabled = sparksEnabled;

    public bool SpreadOverlay
    {
        get => _spreadOverlay;
        set
        {
            if (_spreadOverlay == value)
                return;

            _spreadOverlay = value;

            if (_spreadOverlay)
            {
                _overlayManager.AddOverlay(new GunSpreadOverlay(
                    EntityManager,
                    _eyeManager,
                    Timing,
                    _inputManager,
                    _player,
                    this,
                    TransformSystem));
            }
            else
            {
                _overlayManager.RemoveOverlay<GunSpreadOverlay>();
            }
        }
    }

    private bool _spreadOverlay;

    public override void Initialize()
    {
        base.Initialize();
        UpdatesOutsidePrediction = true;
        SubscribeLocalEvent<AmmoCounterComponent, ItemStatusCollectMessage>(OnAmmoCounterCollect);
        SubscribeAllEvent<MuzzleFlashEvent>(OnMuzzleFlash);

        // Plays animated effects on the client.
        SubscribeAllEvent<HitscanEvent>(OnHitscan); // Trauma - all instead of network, predicted shots raise it locally

        // Starlight
        _cfg.OnValueChanged(StarlightCCVars.TracesEnabled, OnTracesEnabledChanged, true);
        _cfg.OnValueChanged(StarlightCCVars.HolesEnabled, OnHolesEnabledChanged, true);
        _cfg.OnValueChanged(StarlightCCVars.SparksEnabled, OnSparksEnabledChanged, true);
        InitializePrediction();
        _displacementEffect = _proto.Index(_displacementEffectId);

        InitializeMagazineVisuals();
        InitializeSpentAmmo();

        _comparer = new GunTargetEntityComparer();
    }


    private void OnMuzzleFlash(MuzzleFlashEvent args)
    {
        var gunUid = GetEntity(args.Uid);

        CreateEffect(gunUid, args, gunUid);
    }

    private void OnHitscan(HitscanEvent ev)
    {
        var delay = 0f;
        var first = TryConsumePredictedHitscan(ev);

        if (first == 0 && IsOwnPredictedShot(ev))
            PlayPredictedImpactSound(ev);
        for (var i = first; i < ev.Traces.Count; i++)
            delay = FireEffect(ev, delay, ev.Traces[i]);
    }

    private float FireEffect(HitscanEvent visuals, float delay, HitscanTrace trace, PredictedHitscanEffects? predicted = null)
    {
        _recordingEffects = predicted;
        var length = visuals.Speed > 0f ? trace.Distance / (visuals.Speed / 5000f) : 0f;
        if (trace.MuzzleCoordinates is { } muzzleCoordinates)
        {
            if (visuals.MuzzleFlash is { } muzzle && (_tracesEnabled || visuals.Bullet is null))
                RenderFlash(muzzleCoordinates, trace.Angle, muzzle, 1f, false, false, length, delay);

            if (visuals.Bullet is { } bullet)
                RenderBullet(muzzleCoordinates, trace.Angle, bullet, trace.Distance - 1.5f, length, delay);
        }
        if (visuals.TravelFlash is { } travel && trace.TravelCoordinates is { } travelCoordinates && (_tracesEnabled || visuals.Bullet is null))
            RenderFlash(travelCoordinates, trace.Angle, travel, trace.Distance - 1.5f, true, false, length, delay);
        delay += length;
        _recordingEffects = null;

        if ((visuals.ImpactFlash is not null || trace.ImpactedEnt is not null) && (_tracesEnabled || visuals.Bullet is null))
            Timer.Spawn((int) delay, () =>
            {
                if (predicted?.Cancelled == true)
                    return;

                _recordingEffects = predicted;
                if (visuals.ImpactFlash is { } impact)
                    RenderFlash(trace.ImpactCoordinates, trace.Angle, impact, 1f, false, true, length, delay);

                if (trace.ImpactedEnt is { } netEnt && GetEntity(netEnt) is EntityUid ent && Exists(ent))
                    RenderDisplacements(GetCoordinates(trace.ImpactCoordinates), trace.Angle, ent);
                _recordingEffects = null;
            });
        return delay;
    }

    private void RenderDisplacements(EntityCoordinates coords, Angle angle, EntityUid target)
    {
        if (!TryComp<SpriteComponent>(target, out var sprite)
            || !TryComp(coords.EntityId, out TransformComponent? relativeXform))
            return;

        if (!sprite.AllLayers.TryFirstOrDefault(x => (x.ActualRsi ?? x.Rsi) != null && x.RsiState != null, out var layer))
            return;

        if (layer.PixelSize.X != 32 || layer.PixelSize.Y != 32)
            return;

        var ent = Spawn(ImpactProto, coords);
        RecordPredictedEffect(ent);
        var spriteComp = Comp<SpriteComponent>(ent);

        var xform = Transform(ent);
        var targetWorldRot = angle + _xform.GetWorldRotation(relativeXform);
        var delta = targetWorldRot - _xform.GetWorldRotation(xform);
        _xform.SetLocalRotationNoLerp(ent, xform.LocalRotation + delta, xform);

        _sprite.LayerSetRsi((ent, spriteComp), "unshaded", (layer!.ActualRsi ?? layer.Rsi)!);
        _sprite.LayerSetRsiState((ent, spriteComp), "unshaded", layer.RsiState);
        spriteComp["unshaded"].Visible = true;
        _displacement.TryAddDisplacement(_displacementEffect.Displacement, (ent, spriteComp), 0, "unshaded", out _);

        if (_holesEnabled && !HasComp<MobStateComponent>(target))
        {
            var radians = MathF.PI / 180f * (float) angle.Degrees;
            var holeCoords = coords.Offset(new Vector2(MathF.Cos(radians), MathF.Sin(radians)) * _random.NextFloat(0f, 0.5f));
            RecordPredictedEffect(Spawn(BulletHoleProto, holeCoords));
        }

        if (_sparksEnabled
            && TryComp<PierceableComponent>(target, out var pierceable)
            && pierceable.Level >= PierceLevel.Metal)
            RecordPredictedEffect(Spawn(SparksProto, coords));
    }

    private void RenderBullet(NetCoordinates coordinates, Angle angle, ExtendedSpriteSpecifier sprite, float distance, float length, float delay)
    {
        if (sprite.Sprite is not SpriteSpecifier.Rsi rsi)
            return;

        var coords = GetCoordinates(coordinates);

        if (!TryComp(coords.EntityId, out TransformComponent? relativeXform))
            return;

        var ent = Spawn(HitscanProto, coords);
        RecordPredictedEffect(ent);
        var spriteComp = Comp<SpriteComponent>(ent);
        var spriteEnt = (ent, spriteComp);

        var xform = Transform(ent);
        var targetWorldRot = angle + _xform.GetWorldRotation(relativeXform);
        var delta = targetWorldRot - _xform.GetWorldRotation(xform);
        _xform.SetLocalRotationNoLerp(ent, xform.LocalRotation + delta, xform);

        spriteComp[EffectLayers.Unshaded].AutoAnimated = false;
        spriteComp[EffectLayers.Unshaded].Visible = true;
        _sprite.LayerSetSprite(spriteEnt, EffectLayers.Unshaded, rsi);
        _sprite.LayerSetRsiState(spriteEnt, EffectLayers.Unshaded, rsi.RsiState);
        _sprite.SetOffset(spriteEnt, new Vector2(1f, 0f));
        _sprite.SetRotation(spriteEnt, 1.5708f);
        _sprite.SetColor(spriteEnt, sprite.SpriteColor);
        _sprite.SetVisible(spriteEnt, delay == 0);

        var time = delay + length;

        var despawn = Comp<TimedDespawnComponent>(ent);
        despawn.Lifetime = (time / 1000) + 1000;

        if (delay != 0)
            Timer.Spawn((int) delay, () =>
            {
                if (TryComp(ent, out spriteComp))
                    _sprite.SetVisible((ent, spriteComp), true);
            });

        Timer.Spawn((int) time, () =>
        {
            if (TryComp(ent, out spriteComp))
                _sprite.SetVisible((ent, spriteComp), false);
        });

        var anim = new Animation()
        {
            Length = TimeSpan.FromMilliseconds(time),
            AnimationTracks =
            {
                new AnimationTrackComponentProperty()
                {
                    ComponentType = typeof(SpriteComponent),
                    Property = nameof(SpriteComponent.Offset),
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(new Vector2(1f, 0f), delay / 1000),
                        new AnimationTrackProperty.KeyFrame(new Vector2(distance + 1.0f, 0f), time / 1000),
                        new AnimationTrackProperty.KeyFrame(new Vector2(distance + 1.0f, 0f), (time + 1000) / 1000),
                    },
                    InterpolationMode = AnimationInterpolationMode.Linear
                }
            }
        };

        _animPlayer.Play(ent, anim, "hitscan-effect");
    }

    private void RenderFlash(NetCoordinates coordinates, Angle angle, SpriteSpecifier sprite, float distance, bool travel, bool end, float length, float delay)
    {
        if (end) length = 0;
        var time = delay + length + 100;

        if (sprite is not SpriteSpecifier.Rsi rsi)
            return;

        var coords = GetCoordinates(coordinates);

        if (!TryComp(coords.EntityId, out TransformComponent? relativeXform))
            return;

        var ent = Spawn(HitscanProto, coords);
        RecordPredictedEffect(ent);
        var spriteEnt = (ent, Comp<SpriteComponent>(ent));

        var xform = Transform(ent);
        var targetWorldRot = angle + _xform.GetWorldRotation(relativeXform);
        var delta = targetWorldRot - _xform.GetWorldRotation(xform);
        _xform.SetLocalRotationNoLerp(ent, xform.LocalRotation + delta, xform);

        if (_sprite.TryGetLayer(spriteEnt, EffectLayers.Unshaded, out var unshaded, false))
            _sprite.LayerSetAutoAnimated(unshaded, false);

        _sprite.LayerSetSprite(spriteEnt, EffectLayers.Unshaded, rsi);
        _sprite.LayerSetRsiState(spriteEnt, EffectLayers.Unshaded, rsi.RsiState);
        if (travel)
        {
            _sprite.SetScale(spriteEnt, new Vector2(0.05f, 0.5f));
            _sprite.SetOffset(spriteEnt, new Vector2(distance * -0.5f, 0f));
        }
        else
            _sprite.SetScale(spriteEnt, new Vector2(1f, 0.5f));

        _sprite.LayerSetVisible(spriteEnt, EffectLayers.Unshaded, true);

        var despawn = Comp<TimedDespawnComponent>(ent);
        despawn.Lifetime = (time / 1000) + 1000;

        if (delay != 0)
            Timer.Spawn((int) delay, () =>
            {
                if (!Deleted(ent))
                    _sprite.SetVisible(spriteEnt, true);
            });

        Timer.Spawn((int) time, () =>
        {
            if (!Deleted(ent))
                _sprite.SetVisible(spriteEnt, false);
        });

        var anim = new Animation()
        {
            Length = TimeSpan.FromMilliseconds(time),
            AnimationTracks =
            {
                new AnimationTrackSpriteFlick()
                {
                    LayerKey = EffectLayers.Unshaded,
                    KeyFrames =
                    {
                        new AnimationTrackSpriteFlick.KeyFrame(rsi.RsiState, (time - 100) / 1000),
                    }
                }
            }
        };

        if (travel)
        {
            anim.AnimationTracks.Add(new AnimationTrackComponentProperty()
            {
                ComponentType = typeof(SpriteComponent),
                Property = nameof(SpriteComponent.Scale),
                KeyFrames =
                {
                    new AnimationTrackProperty.KeyFrame(new Vector2(0.05f, 0.5f), delay / 1000),
                    new AnimationTrackProperty.KeyFrame(new Vector2(distance, 0.5f), (time - 100) / 1000),
                    new AnimationTrackProperty.KeyFrame(new Vector2(distance, 0.5f), time / 1000),
                },
                InterpolationMode = AnimationInterpolationMode.Cubic
            });
            anim.AnimationTracks.Add(new AnimationTrackComponentProperty()
            {
                ComponentType = typeof(SpriteComponent),
                Property = nameof(SpriteComponent.Offset),
                KeyFrames =
                {
                    new AnimationTrackProperty.KeyFrame(new Vector2(distance * -0.5f, 0f), delay / 1000),
                    new AnimationTrackProperty.KeyFrame(new Vector2(0, 0f), (time - 100) / 1000),
                    new AnimationTrackProperty.KeyFrame(new Vector2(0, 0f), time / 1000),
                },
                InterpolationMode = AnimationInterpolationMode.Cubic
            });
        }

        _animPlayer.Play(ent, anim, "hitscan-effect");
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!Timing.IsFirstTimePredicted)
            return;

        var entityNull = _player.LocalEntity;

        if (entityNull == null || !TryComp<CombatModeComponent>(entityNull, out var combat) || !combat.IsInCombatMode)
        {
            return;
        }

        var entity = entityNull.Value;

        if (TryComp<MechPilotComponent>(entity, out var mechPilot)) // Goobstation
            entity = mechPilot.Mech;

        if (!TryGetGun(entity, out var gun))
        {
            return;
        }

        if (TryComp<EntropicPlumeAffectedComponent>(entity, out var affected) &&
            affected.NextAttack + TimeSpan.FromSeconds(0.1f) > Timing.CurTime) // Goobstation
            return;

        var useKey = gun.Comp.UseKey ? EngineKeyFunctions.Use : EngineKeyFunctions.UseSecondary;

        if (_inputSystem.CmdStates.GetState(useKey) != BoundKeyState.Down && !gun.Comp.BurstActivated)
        {
            if (gun.Comp.ShotCounter != 0)
                RaisePredictiveEvent(new RequestStopShootEvent { Gun = GetNetEntity(gun) });
            return;
        }

        if (gun.Comp.NextFire > Timing.CurTime)
            return;

        var mousePos = _eyeManager.PixelToMap(_inputManager.MouseScreenPosition);

        if (mousePos.MapId == MapId.Nullspace)
        {
            if (gun.Comp.ShotCounter != 0)
                RaisePredictiveEvent(new RequestStopShootEvent { Gun = GetNetEntity(gun) });

            return;
        }

        // Define target coordinates relative to gun entity, so that network latency on moving grids doesn't fuck up the target location.
        var target = GetBestTarget(_eyeManager.CurrentEye, mousePos);

        var coordinates = TransformSystem.ToCoordinates(entity, mousePos);

        Log.Debug($"Sending shoot request tick {Timing.CurTick} / {Timing.CurTime}");


        RaisePredictiveEvent(new RequestShootEvent
        {
            Target = target,
            Coordinates = GetNetCoordinates(coordinates),
            Gun = GetNetEntity(gun),
            Continuous = _cfg.GetCVar(CCVars.ControlHoldToAttackRanged),
            Tick = Timing.CurTick, // Starlight
        });
    }

    // Trauma - Shoot is predicted in shared now

    protected override void Recoil(EntityUid? user, Vector2 recoil, float recoilScalar) // Trauma - override shared virtual
    {
        if (!Timing.IsFirstTimePredicted || user == null || recoil == Vector2.Zero || recoilScalar == 0)
            return;

        _recoil.KickCamera(user.Value, recoil.Normalized() * 0.13f * recoilScalar); // Goob
    }

    protected override void OnClientPredictHitscan(Entity<GunComponent> gun, EntityUid shot, int seed, EntityCoordinates fromCoordinates, Vector2 direction, float pointer, EntityUid? user)
    {
        var fromMap = TransformSystem.ToMapCoordinates(fromCoordinates);
        PredictHitscan(gun, shot, seed, fromMap, direction, pointer, user);
    }

    protected override void Popup(string message, EntityUid? uid, EntityUid? user)
    {
        if (uid == null || user == null || !Timing.IsFirstTimePredicted)
            return;

        PopupSystem.PopupEntity(message, uid.Value, user.Value);
    }

    protected override void CreateEffect(EntityUid gunUid, MuzzleFlashEvent message, EntityUid? tracked = null)
    {
        if (!Timing.IsFirstTimePredicted)
            return;

        // EntityUid check added to stop throwing exceptions due to https://github.com/space-wizards/space-station-14/issues/28252
        // TODO: Check to see why invalid entities are firing effects.
        if (gunUid == EntityUid.Invalid)
        {
            Log.Debug($"Invalid Entity sent MuzzleFlashEvent (proto: {message.Prototype}, gun: {ToPrettyString(gunUid)})");
            return;
        }

        var gunXform = Transform(gunUid);
        var gridUid = gunXform.GridUid;
        EntityCoordinates coordinates;

        if (TryComp(gridUid, out MapGridComponent? mapGrid))
        {
            coordinates = new EntityCoordinates(gridUid.Value, _maps.LocalToGrid(gridUid.Value, mapGrid, gunXform.Coordinates));
        }
        else if (gunXform.MapUid != null)
        {
            coordinates = new EntityCoordinates(gunXform.MapUid.Value, TransformSystem.GetWorldPosition(gunXform));
        }
        else
        {
            return;
        }

        var ent = Spawn(message.Prototype, coordinates);
        TransformSystem.SetWorldRotationNoLerp(ent, message.Angle);

        if (tracked != null)
        {
            var track = EnsureComp<TrackUserComponent>(ent);
            track.User = tracked;
            track.Offset = Vector2.UnitX / 2f;
        }

        var lifetime = 0.4f;

        if (TryComp<TimedDespawnComponent>(gunUid, out var despawn))
        {
            lifetime = despawn.Lifetime;
        }

        var anim = new Animation()
        {
            Length = TimeSpan.FromSeconds(lifetime),
            AnimationTracks =
            {
                new AnimationTrackComponentProperty
                {
                    ComponentType = typeof(SpriteComponent),
                    Property = nameof(SpriteComponent.Color),
                    InterpolationMode = AnimationInterpolationMode.Linear,
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(Color.White.WithAlpha(1f), 0),
                        new AnimationTrackProperty.KeyFrame(Color.White.WithAlpha(0f), lifetime)
                    }
                }
            }
        };

        _animPlayer.Play(ent, anim, "muzzle-flash");
        if (!TryComp(gunUid, out PointLightComponent? light))
        {
            light = Factory.GetComponent<PointLightComponent>();
            light.NetSyncEnabled = false;
            AddComp(gunUid, light);
        }

        Lights.SetEnabled(gunUid, true, light);
        Lights.SetRadius(gunUid, 2f, light);
        Lights.SetColor(gunUid, Color.FromHex("#cc8e2b"), light);
        Lights.SetEnergy(gunUid, 5f, light);

        var animTwo = new Animation()
        {
            Length = TimeSpan.FromSeconds(lifetime),
            AnimationTracks =
            {
                new AnimationTrackComponentProperty
                {
                    ComponentType = typeof(PointLightComponent),
                    Property = nameof(PointLightComponent.Energy),
                    InterpolationMode = AnimationInterpolationMode.Linear,
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(5f, 0),
                        new AnimationTrackProperty.KeyFrame(0f, lifetime)
                    }
                },
                new AnimationTrackComponentProperty
                {
                    ComponentType = typeof(PointLightComponent),
                    Property = nameof(PointLightComponent.AnimatedEnable),
                    InterpolationMode = AnimationInterpolationMode.Linear,
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(true, 0),
                        new AnimationTrackProperty.KeyFrame(false, lifetime)
                    }
                }
            }
        };

        var uidPlayer = EnsureComp<AnimationPlayerComponent>(gunUid);

        _animPlayer.Stop(gunUid, uidPlayer, "muzzle-flash-light");
        _animPlayer.Play((gunUid, uidPlayer), animTwo, "muzzle-flash-light");
    }

    /// <remarks>We use our own sorting algorithm separate from the default for smarter configurability.</remarks>
    private NetEntity? GetBestTarget(IEye eye, MapCoordinates coordinates)
    {
        // Find all the entities intersecting our click
        var entities = _spriteTree.QueryAabb(coordinates.MapId, Box2.CenteredAround(coordinates.Position, new Vector2(1, 1)));

        // Check the entities against whether or not we can click them
        var foundEntities = new List<(EntityUid, bool, bool, int, uint, float, float)>(entities.Count);

        foreach (var entity in entities)
        {
            // Don't add the target if we can't shoot the target!
            if (!CheckFixtures(entity.Uid))
                continue;

            var entry = CheckTarget((entity.Uid, entity.Component, entity.Transform), eye, coordinates);
            foundEntities.Add(entry);
        }

        if (foundEntities.Count == 0)
            return null;

        // Do drawdepth & y-sorting. First index is the top-most sprite (opposite of normal render order).
        foundEntities.Sort(_comparer);
        var (target, alive, occluded, _, _, _, _) = foundEntities.FirstOrDefault();

        // Prevents us from just selecting a random target nearby our cursor. It must either be alive, or our cursor must be on top of it!
        if (!occluded && !alive)
            return null;

        return GetNetEntity(target);
    }

    private (EntityUid, bool, bool, int, uint, float, float) CheckTarget(Entity<SpriteComponent, TransformComponent> target, IEye eye, MapCoordinates coordinates)
    {
        var occluded = _clickable.CheckClick((target.Owner, null, target.Comp1, target.Comp2),
            coordinates.Position,
            eye,
            true,
            out var drawDepthClicked,
            out var renderOrder,
            out var bottom);

        var difference = (target.Comp2.Coordinates.Position - coordinates.Position).LengthSquared();

        return (target.Owner, _mobState.IsAlive(target.Owner), occluded, drawDepthClicked, renderOrder, bottom, difference);
    }

    /// <summary>
    /// This Comparer takes a list of Entities that we can hit and orders them by which target the player is probably trying to shoot.
    /// We organize based on these criteria in this order:
    /// alive means the entity has a MobState and is currently alive. We check it first since they typically shoot back.
    /// occluded is whether the cursor is above the sprite or just near it.
    /// depth is the order in which sprites are layered, bigger number means its rendered above others.
    /// renderOrder is used to indicate if a sprite should be visually more important, typically this value is 0.
    /// bottom indicates which sprite is visually the lowest on the screen and therefore typically above other sprites.
    /// distance indicates the distance from the entity's coordinates to our mouse.
    /// If all of those tie, then we organize by whichever entity has the highest EntityUid.
    /// </summary>
    private sealed class GunTargetEntityComparer : IComparer<(EntityUid clicked, bool alive, bool occluded, int depth, uint renderOrder, float bottom, float distance)>
    {
        public int Compare((EntityUid clicked, bool alive, bool occluded, int depth, uint renderOrder, float bottom, float distance) x,
            (EntityUid clicked, bool alive, bool occluded, int depth, uint renderOrder, float bottom, float distance) y)
        {
            var cmp = y.occluded.CompareTo(x.occluded);

            if (cmp != 0)
            {
                return cmp;
            }

            cmp = y.alive.CompareTo(x.alive);
            if (cmp != 0)
            {
                return cmp;
            }

            cmp = y.depth.CompareTo(x.depth);
            if (cmp != 0)
            {
                return cmp;
            }

            cmp = y.renderOrder.CompareTo(x.renderOrder);

            if (cmp != 0)
            {
                return cmp;
            }

            cmp = -y.bottom.CompareTo(x.bottom);

            if (cmp != 0)
            {
                return cmp;
            }

            cmp = -y.distance.CompareTo(x.distance);

            if (cmp != 0)
            {
                return cmp;
            }

            return y.clicked.CompareTo(x.clicked);
        }
    }

    private bool CheckFixtures(Entity<FixturesComponent?> entity)
    {
        if (!Resolve(entity, ref entity.Comp, false))
            return false;

        // TODO: Maybe also check that our cursor is intersecting a valid fixture?
        foreach (var fix in entity.Comp.Fixtures)
        {
            if (!fix.Value.Hard || (fix.Value.CollisionLayer & (int)CollisionGroup.BulletImpassable) == 0)
                continue;

            // Only need to check if we're hitting one fixture
            return true;
        }

        // If we cannot collide then we absolutely do not want to target it!
        return false;
    }

    // Trauma - PlayImpactSound is predicted in shared
}
