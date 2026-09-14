using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Central coordinator for paranormal activity in the house.
/// Tracks EMF interaction events, ambient temperatures, and ghost presence.
/// </summary>
public class ParanormalManager : MonoBehaviour
{
    public static ParanormalManager Instance { get; private set; }

    [System.Serializable]
    public class ParanormalEvent
    {
        public Vector2 position;
        public int emfLevel; // 2 = door/switch, 3 = throw, 4 = manifest, 5 = evidence
        public float expireTime;

        public bool IsExpired => Time.time > expireTime;
    }

    [Header("Temperature Settings")]
    [SerializeField] private float baseHouseTemp = 27.0f;
    [SerializeField] private float favoriteRoomTemp = 1.0f;
    [SerializeField] private float freezingTemp = -10.0f;
    [SerializeField] private float ghostCoolingRadius = 6.0f;

    private readonly List<ParanormalEvent> activeEvents = new List<ParanormalEvent>();

    // Ghost anchor/favorite room tracking
    private Collider2D favoriteRoomCollider;
    private bool hasFavoriteRoom = false;
    private bool hasFreezingEvidence = false;
    private bool hasEmf5Evidence = false;
    private Transform activeGhostTransform;
    private GhostHandler activeGhostHandler;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Update()
    {
        // Prune expired paranormal events
        for (int i = activeEvents.Count - 1; i >= 0; i--)
        {
            if (activeEvents[i].IsExpired)
            {
                activeEvents.RemoveAt(i);
            }
        }
    }

    /// <summary>
    /// Register a paranormal event (e.g. ghost moved a door, threw an object, or manifested).
    /// </summary>
    public void RegisterEvent(Vector2 position, int emfLevel, float duration = 20f)
    {
        // 25% chance to upgrade standard interactions to EMF 5 if the ghost has EMF 5 evidence
        if (hasEmf5Evidence && (emfLevel == 2 || emfLevel == 3))
        {
            if (Random.value <= 0.25f)
            {
                emfLevel = 5;
            }
        }

        activeEvents.Add(new ParanormalEvent
        {
            position = position,
            emfLevel = Mathf.Clamp(emfLevel, 2, 5),
            expireTime = Time.time + duration
        });
    }

    /// <summary>
    /// Returns a global activity level (0-10) for the Truck Dashboard based on recent events.
    /// </summary>
    public int GetGlobalActivityLevel()
    {
        int total = 0;
        foreach (var ev in activeEvents)
        {
            total += ev.emfLevel;
        }
        
        // Add random ambient noise or scale it
        int level = Mathf.Clamp(total, 0, 10);
        return level;
    }

    /// <summary>
    /// Checks for nearby paranormal events and returns the highest EMF reading and direction.
    /// </summary>
    public int GetHighestEmfAt(Vector2 checkPosition, float radius, out Vector2 closestSourceDirection, out float distanceToSource)
    {
        int highestLevel = 1; // 1 is default ambient baseline
        closestSourceDirection = Vector2.zero;
        distanceToSource = float.MaxValue;
        float minDistanceForHighest = float.MaxValue;

        bool isHunting = activeGhostHandler != null && activeGhostHandler.CurrentState != GhostHandlerState.Dormant;
        bool isInsideGhostRoom = favoriteRoomCollider != null && favoriteRoomCollider.OverlapPoint(checkPosition);

        if (!isHunting && !isInsideGhostRoom)
        {
            return 1;
        }

        // Also check if ghost is currently actively hunting/manifesting near the player
        if (activeGhostTransform != null && isHunting)
        {
            float distToGhost = Vector2.Distance(checkPosition, activeGhostTransform.position);
            if (distToGhost <= radius)
            {
                highestLevel = hasEmf5Evidence ? 5 : 4;
                closestSourceDirection = ((Vector2)activeGhostTransform.position - checkPosition).normalized;
                distanceToSource = distToGhost;
                minDistanceForHighest = distToGhost;
            }
        }
        else if (activeGhostTransform != null && !isHunting)
        {
            // Passive EMF 2 or 3 from the dormant ghost roaming the room
            float distToGhost = Vector2.Distance(checkPosition, activeGhostTransform.position);
            if (distToGhost <= radius)
            {
                if (2 > highestLevel)
                {
                    highestLevel = 2; // Baseline EMF 2 for the ghost's presence
                    closestSourceDirection = ((Vector2)activeGhostTransform.position - checkPosition).normalized;
                    distanceToSource = distToGhost;
                    minDistanceForHighest = distToGhost;
                }
            }
        }

        // Only scan active events if inside the ghost room OR hunting
        if (isInsideGhostRoom || isHunting)
        {
            foreach (var ev in activeEvents)
            {
                float dist = Vector2.Distance(checkPosition, ev.position);
                if (dist <= radius)
                {
                    if (ev.emfLevel > highestLevel || (ev.emfLevel == highestLevel && dist < minDistanceForHighest))
                    {
                        highestLevel = ev.emfLevel;
                        minDistanceForHighest = dist;
                        closestSourceDirection = (ev.position - checkPosition).normalized;
                        distanceToSource = dist;
                    }
                }
            }
        }

        return highestLevel;
    }

    /// <summary>
    /// Samples the localized temperature at a world coordinate.
    /// Incorporates baseline temperature, ghost favorite room, and ghost proximity cooling.
    /// </summary>
    public float GetTemperatureAt(Vector2 worldPosition)
    {
        float temp = baseHouseTemp;
        bool isHunting = activeGhostHandler != null && activeGhostHandler.CurrentState != GhostHandlerState.Dormant;
        bool isInsideFavoriteRoom = favoriteRoomCollider != null && favoriteRoomCollider.OverlapPoint(worldPosition);

        // Influence of ghost's favorite room
        if (hasFavoriteRoom && isInsideFavoriteRoom)
        {
            float targetRoomTemp = hasFreezingEvidence ? freezingTemp : favoriteRoomTemp;
            temp = targetRoomTemp;
        }
        else
        {
            // Check if inside ANY other GhostRoomMarker
            Collider2D[] cols = Physics2D.OverlapPointAll(worldPosition);
            foreach (var col in cols)
            {
                if (col.GetComponent<GhostRoomMarker>() != null)
                {
                    temp = 14.0f; // Lower than 27C but above 10C
                    break;
                }
            }
        }

        // Additional localized cold aura directly around the roaming ghost during hunts
        if (activeGhostTransform != null && isHunting)
        {
            float distToGhost = Vector2.Distance(worldPosition, activeGhostTransform.position);
            if (distToGhost < ghostCoolingRadius)
            {
                float t = 1f - (distToGhost / ghostCoolingRadius);
                float coldSpot = hasFreezingEvidence ? freezingTemp : favoriteRoomTemp;
                float newTemp = Mathf.Lerp(temp, coldSpot, t);
                if (!hasFreezingEvidence)
                {
                    newTemp = Mathf.Max(1.0f, newTemp);
                }
                else
                {
                    newTemp = Mathf.Max(-10.0f, newTemp);
                }
                temp = Mathf.Min(temp, newTemp);
            }
        }

        // Add subtle sensor noise (+/- 0.3 C)
        float noise = (Mathf.PerlinNoise(worldPosition.x * 0.5f, Time.time * 0.2f) - 0.5f) * 0.6f;
        return temp + noise;
    }

    public void SetGhostInfo(Transform ghostTransform, Collider2D roomCollider, bool freezingEvidence, bool emf5Evidence, GhostHandler handler)
    {
        activeGhostTransform = ghostTransform;
        favoriteRoomCollider = roomCollider;
        hasFavoriteRoom = roomCollider != null;
        hasFreezingEvidence = freezingEvidence;
        hasEmf5Evidence = emf5Evidence;
        activeGhostHandler = handler;
    }

    public void ClearGhostInfo()
    {
        activeGhostTransform = null;
        hasFavoriteRoom = false;
        activeGhostHandler = null;
    }
}
