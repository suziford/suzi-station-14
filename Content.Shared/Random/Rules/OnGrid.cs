// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Content.Shared.Random.Rules;

/// <summary>
/// Returns true if the entity is standing on any grid. Used by the Starlight audio rules (OnGrid, OnStationGrid),
/// which the sound port referenced without ever adding the rule itself.
/// </summary>
public sealed partial class OnGridRule : RulesRule
{
    public override bool Check(EntityManager entManager, EntityUid uid)
    {
        var onGrid = entManager.TryGetComponent(uid, out TransformComponent? xform) && xform.GridUid != null;
        return onGrid != Inverted;
    }
}
