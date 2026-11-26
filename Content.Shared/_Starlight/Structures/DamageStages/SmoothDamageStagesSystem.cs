using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;

namespace Content.Shared._Starlight.Structures.DamageStages;

public sealed partial class SmoothDamageStagesSystem : EntitySystem
{
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SmoothDamageStagesComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<SmoothDamageStagesComponent, DamageChangedEvent>(OnDamageChanged);
    }

    private void OnMapInit(Entity<SmoothDamageStagesComponent> ent, ref MapInitEvent args)
        => UpdateStage(ent);

    private void OnDamageChanged(Entity<SmoothDamageStagesComponent> ent, ref DamageChangedEvent args)
        => UpdateStage(ent);

    private void UpdateStage(Entity<SmoothDamageStagesComponent> ent)
    {
        if (!TryComp<DamageableComponent>(ent, out var damageable))
            return;

        var totalDamage = damageable.TotalDamage;

        var stage = 0;
        foreach (var threshold in ent.Comp.Thresholds)
        {
            if (totalDamage < threshold)
                break;

            stage++;
        }

        _appearance.SetData(ent, SmoothDamageStageVisuals.Stage, stage);
    }
}
