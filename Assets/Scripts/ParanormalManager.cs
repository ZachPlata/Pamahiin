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
    [SerializeField] private float baseHouseTemp = 20.0f;
    [SerializeField] private float favoriteRoomTemp = 5.0f;
    [SerializeField] private float freezingTemp = -4.0f;
    [SerializeField] private float ghostCoolingRadius = 6.0f;

    private readonly List<ParanormalEvent> activeEvents = new List<ParanormalEvent>();

    // Ghost anchor/favorite room tracking
    private Vector2 favoriteRoomCenter = Vector2.zero;
    private float favoriteRoomRadius = 8.0f;
    private bool hasFavoriteRoom = false;
    private bool hasFreezingEvidence = false;
    private Transform activeGhostTransform;

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
        activeEvents.Add(new ParanormalEvent
        {
            position = position,
            emfLevel = Mathf.Clamp(emfLevel, 2, 5),
            expireTime = Time.time + duration
        });
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

        // Also check if ghost is currently actively manifesting near the player (EMF 4)
        if (activeGhostTransform != null)
        {
            float distToGhost = Vector2.Distance(checkPosition, activeGhostTransform.position);
            if (distToGhost <= radius)
            {
                highestLevel = 4;
                closestSourceDirection = ((Vector2)activeGhostTransform.position - checkPosition).normalized;
                distanceToSource = distToGhost;
                minDistanceForHighest = distToGhost;
            }
        }

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

        return highestLevel;
    }

    /// <summary>
    /// Samples the localized temperature at a world coordinate.
    /// Incorporates baseline temperature, ghost favorite room, and ghost proximity cooling.
    /// </summary>
    public float GetTemperatureAt(Vector2 worldPosition)
    {
        float temp = baseHouseTemp;

        // Influence of ghost's favorite room
        if (hasFavoriteRoom)
        {
            float distToRoom = Vector2.Distance(worldPosition, favoriteRoomCenter);
            if (distToRoom < favoriteRoomRadius)
            {
                float t = 1f - (distToRoom / favoriteRoomRadius);
                float targetRoomTemp = hasFreezingEvidence ? freezingTemp : favoriteRoomTemp;
                temp = Mathf.Lerp(temp, targetRoomTemp, t);
            }
        }

        // Additional localized cold aura directly around the roaming ghost
        if (activeGhostTransform != null)
        {
            float distToGhost = Vector2.Distance(worldPosition, activeGhostTransform.position);
            if (distToGhost < ghostCoolingRadius)
            {
                float t = 1f - (distToGhost / ghostCoolingRadius);
                float coldSpot = hasFreezingEvidence ? freezingTemp - 1f : favoriteRoomTemp - 2f;
                temp = Mathf.Min(temp, Mathf.Lerp(temp, coldSpot, t));
            }
        }

        // Add subtle sensor noise (+/- 0.3 C)
        float noise = (Mathf.PerlinNoise(worldPosition.x * 0.5f, Time.time * 0.2f) - 0.5f) * 0.6f;
        return temp + noise;
    }

    public void SetGhostInfo(Transform ghostTransform, Vector2 favRoomCenter, float favRoomRadius, bool freezingEvidence)
    {
        activeGhostTransform = ghostTransform;
        favoriteRoomCenter = favRoomCenter;
        favoriteRoomRadius = favRoomRadius;
        hasFavoriteRoom = true;
        hasFreezingEvidence = freezingEvidence;
    }

    public void ClearGhostInfo()
    {
        activeGhostTransform = null;
        hasFavoriteRoom = false;
    }
}
