using Content.Shared.Physics;
using Content.Shared.Tag;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.Weapons.Hitscan.Components;

/// <summary>
/// A basic raycast system that will shoot in a straight line when triggered.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class HitscanBasicRaycastComponent : Component
{
    /// <summary>
    /// Maximum distance the raycast will travel before giving up. Reflections will reset the distance traveled
    /// </summary>
    [DataField]
    public float MaxDistance = 20.0f;

    /// <summary>
    /// The collision mask the hitscan ray uses to collide with other objects. See the enum for more information
    /// </summary>
    [DataField]
    public CollisionGroup CollisionMask = CollisionGroup.Opaque;

    #region Starlight
    /// <summary>
    /// Minimum distance the raycast must travel before colliding with entities that don't have NotArmedCollideWith tags.
    /// </summary>
    [DataField]
    public float MinDistance = 0.0f;

    /// <summary>
    /// What tags entities need to have for the raycast to collide with them prior to its minimum distance.
    /// </summary>
    [DataField]
    public ProtoId<TagPrototype>[] NotArmedCollideWith = new ProtoId<TagPrototype>[]
    {
        "Wall", "Window", "Airlock", "BulletUnarmedCollide"
    };
    #endregion
}
