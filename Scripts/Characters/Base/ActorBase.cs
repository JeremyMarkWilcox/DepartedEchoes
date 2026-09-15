using Godot;

/// <summary>
/// Shared base for the Player and the combatants.
///
/// Exposes the body's own collision extents, which the camera uses as stable framing bounds.
/// These come from the collision shape rather than the visual mesh on purpose: attack anticipation
/// scales the visuals, defeat topples them, and reading bounds from any of that would make the
/// camera twitch on every cue.
/// </summary>
public partial class ActorBase : CharacterBody3D
{
    private float _collisionRadius = 0.5f;
    private float _collisionHalfHeight = 1.0f;
    private bool _extentsResolved;

    /// <summary>Horizontal half-extent of the body's collision shape.</summary>
    public float CollisionRadius
    {
        get
        {
            ResolveExtentsOnce();
            return _collisionRadius;
        }
    }

    /// <summary>Vertical half-extent of the body's collision shape, measured from its origin.</summary>
    public float CollisionHalfHeight
    {
        get
        {
            ResolveExtentsOnce();
            return _collisionHalfHeight;
        }
    }

    /// <summary>
    /// Reads the first direct CollisionShape3D child once and caches it. Only direct children are
    /// considered, so a hurtbox area's own shape cannot be mistaken for the body's. The shape is
    /// assumed to be centred on the body origin, which holds for every actor in the prototype.
    /// </summary>
    private void ResolveExtentsOnce()
    {
        if (_extentsResolved)
        {
            return;
        }

        _extentsResolved = true;

        foreach (Node child in GetChildren())
        {
            if (child is not CollisionShape3D collider)
            {
                continue;
            }

            switch (collider.Shape)
            {
                case CapsuleShape3D capsule:
                    _collisionRadius = capsule.Radius;
                    _collisionHalfHeight = capsule.Height * 0.5f;
                    return;

                case SphereShape3D sphere:
                    _collisionRadius = sphere.Radius;
                    _collisionHalfHeight = sphere.Radius;
                    return;

                case CylinderShape3D cylinder:
                    _collisionRadius = cylinder.Radius;
                    _collisionHalfHeight = cylinder.Height * 0.5f;
                    return;

                case BoxShape3D box:
                    _collisionRadius = Mathf.Max(box.Size.X, box.Size.Z) * 0.5f;
                    _collisionHalfHeight = box.Size.Y * 0.5f;
                    return;
            }
        }
    }
}
