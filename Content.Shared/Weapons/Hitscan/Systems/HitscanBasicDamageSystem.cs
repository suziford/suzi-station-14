using Content.Shared.Damage.Systems;
using Content.Shared.Weapons.Hitscan.Components;
using Content.Shared.Weapons.Hitscan.Events;

namespace Content.Shared.Weapons.Hitscan.Systems;

public sealed partial class HitscanBasicDamageSystem : EntitySystem
{
    [Dependency] private readonly DamageableSystem _damage = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<HitscanBasicDamageComponent, HitscanRaycastFiredEvent>(OnHitscanHit);
    }

    private void OnHitscanHit(Entity<HitscanBasicDamageComponent> ent, ref HitscanRaycastFiredEvent args)
    {
        if (args.Data.HitEntity == null)
            return;

        var dmg = ent.Comp.Damage * _damage.UniversalHitscanDamageModifier * GetFalloffMultiplier(ent, args.Data);
        if (ent.Comp.ArmorPenetration != 0f)
            dmg.ArmorPenetration = ent.Comp.ArmorPenetration;

        if (!_damage.TryChangeDamage(
                args.Data.HitEntity.Value,
                dmg,
                out var damageDealt,
                ignoreResistances: ent.Comp.IgnoreResistances,
                origin: args.Data.Gun))
            return;

        var damageEvent = new HitscanDamageDealtEvent
        {
            Target = args.Data.HitEntity.Value,
            DamageDealt = damageDealt,
            Data = args.Data,
        };

        RaiseLocalEvent(ent, ref damageEvent);
    }
}
