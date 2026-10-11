using UnityEngine;

/// <summary>
/// Attach this component to floor colliders/triggers to designate what sound plays when walking on it.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class FloorSurface : MonoBehaviour
{
    [Tooltip("The surface material for this area.")]
    [SerializeField] public SurfaceType surfaceType = SurfaceType.Wood;

    [Tooltip("If two floor zones overlap (e.g. a Concrete path on top of Grass), the one with higher priority wins.")]
    [SerializeField] public int priority = 0;

    private void Awake()
    {
        EnsureTrigger();
    }

    private void Reset()
    {
        EnsureTrigger();
    }

    private void EnsureTrigger()
    {
        var col = GetComponent<Collider2D>();
        if (col != null)
        {
            col.isTrigger = true;
        }
    }
}
