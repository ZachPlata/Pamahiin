using UnityEngine;

public enum SurfaceType
{
    Default,
    Wood,
    Concrete,
    Pavement,
    Grass,
    Carpet
}

[RequireComponent(typeof(AudioSource))]
public class PlayerFootstepAudio : MonoBehaviour
{
    [Header("Audio Clips (Add Multiple MP3s per Floor!)")]
    [Tooltip("Add 3 to 6 different wood step MP3s here.")]
    [SerializeField] private AudioClip[] woodSteps;

    [Tooltip("Add multiple concrete step MP3s here.")]
    [SerializeField] private AudioClip[] concreteSteps;

    [Tooltip("Add multiple pavement / asphalt step MP3s here.")]
    [SerializeField] private AudioClip[] pavementSteps;

    [Tooltip("Add multiple grass step MP3s here.")]
    [SerializeField] private AudioClip[] grassSteps;

    [Tooltip("Add multiple carpet / rug step MP3s here.")]
    [SerializeField] private AudioClip[] carpetSteps;

    [Tooltip("Fallback MP3 files if no specific floor is detected.")]
    [SerializeField] private AudioClip[] defaultSteps;

    [Header("Horror Accents (Optional)")]
    [Tooltip("Extra wood creak / floorboard groan MP3s that can rarely layer over steps.")]
    [SerializeField] private AudioClip[] woodCreakAccents;
    [Range(0f, 1f)]
    [SerializeField] private float creakChance = 0.12f; // 12% chance when stepping on wood

    [Header("Anti-Repetition Variation")]
    [Tooltip("Random volume range for each step.")]
    [Range(0f, 1f)]
    [SerializeField] private float minVolume = 0.5f;
    [Range(0f, 1f)]
    [SerializeField] private float maxVolume = 0.65f;

    [Tooltip("Random pitch shift range (1.0 is normal).")]
    [SerializeField] private float minPitch = 0.88f;
    [SerializeField] private float maxPitch = 1.12f;

    [Header("Movement Modifiers")]
    [Tooltip("Multiplier applied to volume when crouching.")]
    [SerializeField] private float crouchVolumeMultiplier = 0.4f;
    [Tooltip("Multiplier applied to volume when sprinting.")]
    [SerializeField] private float sprintVolumeMultiplier = 1.25f;

    [Header("Cadence & Cooldown")]
    [Tooltip("Minimum seconds between footsteps to prevent rapid machine-gun sounds.")]
    [SerializeField] private float minStepInterval = 0.22f;

    [Header("Detection Settings")]
    [Tooltip("Layers to check for floors (or leave Default/All).")]
    [SerializeField] private LayerMask floorLayerMask = ~0;

    private AudioSource audioSource;
    private PlayerController playerController;
    private AudioClip lastPlayedClip = null;
    private float lastStepTime = -1f;

    private void Awake()
    {
        audioSource = GetComponent<AudioSource>();
        playerController = GetComponent<PlayerController>();

        if (audioSource != null)
        {
            audioSource.playOnAwake = false;
        }

        contactFilter = new ContactFilter2D();
        contactFilter.SetLayerMask(floorLayerMask);
        contactFilter.useTriggers = true;
    }

    /// <summary>
    /// Call this from your Animation Event in Walking.anim!
    /// In the Animation Event Inspector, set Function to: PlayFootstep
    /// </summary>
    public void PlayFootstep()
    {
        if (audioSource == null) return;
        if (Time.time - lastStepTime < minStepInterval) return;
        lastStepTime = Time.time;

        SurfaceType surface = DetectCurrentSurface();
        AudioClip clip = GetVariedClip(surface);

        if (clip == null) return;

        // 1. Calculate randomized pitch to prevent robotic drone
        audioSource.pitch = Random.Range(minPitch, maxPitch);

        // 2. Calculate randomized volume
        float volume = Random.Range(minVolume, maxVolume);

        // 3. Scale volume based on player stance (crouching vs sprinting)
        if (playerController != null)
        {
            if (playerController.IsCrouching)
            {
                volume *= crouchVolumeMultiplier;
                audioSource.pitch *= 0.95f; // slightly deeper / softer
            }
            else if (Input.GetKey(KeyCode.LeftShift) && playerController.CurrentStamina > 0f)
            {
                volume *= sprintVolumeMultiplier;
                audioSource.pitch *= 1.05f; // slightly punchier
            }
        }

        // 4. Play the footstep
        audioSource.PlayOneShot(clip, Mathf.Clamp01(volume));

        // 5. Horror flavor: Rare extra wooden floorboard creak
        if (surface == SurfaceType.Wood && woodCreakAccents != null && woodCreakAccents.Length > 0)
        {
            if (Random.value < creakChance)
            {
                AudioClip creak = woodCreakAccents[Random.Range(0, woodCreakAccents.Length)];
                if (creak != null)
                {
                    audioSource.PlayOneShot(creak, volume * 0.7f);
                }
            }
        }
    }

    /// <summary>
    /// Picks a random clip while guaranteeing it never plays the exact same clip twice in a row.
    /// </summary>
    private AudioClip GetVariedClip(SurfaceType surface)
    {
        AudioClip[] clips = surface switch
        {
            SurfaceType.Wood => woodSteps,
            SurfaceType.Concrete => concreteSteps,
            SurfaceType.Pavement => pavementSteps,
            SurfaceType.Grass => grassSteps,
            SurfaceType.Carpet => carpetSteps,
            _ => (concreteSteps != null && concreteSteps.Length > 0) ? concreteSteps : defaultSteps
        };

        if ((clips == null || clips.Length == 0) && concreteSteps != null && concreteSteps.Length > 0)
        {
            clips = concreteSteps;
        }
        else if ((clips == null || clips.Length == 0) && defaultSteps != null && defaultSteps.Length > 0)
        {
            clips = defaultSteps;
        }

        // Additional resilient fallback to any available step clips with concrete prioritized
        if (clips == null || clips.Length == 0)
        {
            if (concreteSteps != null && concreteSteps.Length > 0) clips = concreteSteps;
            else if (woodSteps != null && woodSteps.Length > 0) clips = woodSteps;
            else if (carpetSteps != null && carpetSteps.Length > 0) clips = carpetSteps;
            else if (grassSteps != null && grassSteps.Length > 0) clips = grassSteps;
            else if (pavementSteps != null && pavementSteps.Length > 0) clips = pavementSteps;
        }

        if (clips == null || clips.Length == 0) return null;

        // If only 1 clip exists, we have no choice but to return it
        if (clips.Length == 1)
        {
            lastPlayedClip = clips[0];
            return clips[0];
        }

        // Anti-repetition: Pick a clip that is NOT the one played immediately before
        AudioClip selectedClip = null;
        int attempts = 0;
        do
        {
            selectedClip = clips[Random.Range(0, clips.Length)];
            attempts++;
        }
        while (selectedClip == lastPlayedClip && attempts < 10);

        lastPlayedClip = selectedClip;
        return selectedClip;
    }

    private readonly Collider2D[] overlapResults = new Collider2D[16];
    private ContactFilter2D contactFilter;

    private SurfaceType DetectCurrentSurface()
    {
        // Check all 2D Trigger/Colliders directly beneath or around player
        int count = Physics2D.OverlapPoint(transform.position, contactFilter, overlapResults);

        SurfaceType bestSurface = SurfaceType.Default;
        int highestPriority = int.MinValue;
        bool foundSpecificSurface = false;
        bool isInsideRoof = false;

        for (int i = 0; i < count; i++)
        {
            Collider2D hit = overlapResults[i];
            if (hit == null) continue;

            // 1. Check if it has a FloorSurface script with Priority
            if (hit.TryGetComponent<FloorSurface>(out var floor))
            {
                if (floor.priority >= highestPriority)
                {
                    highestPriority = floor.priority;
                    bestSurface = floor.surfaceType;
                    foundSpecificSurface = true;
                }
            }
            // 2. Check Tags safely (without throwing if tag is not registered in TagManager)
            else if (!foundSpecificSurface)
            {
                string objTag = hit.gameObject.tag;
                if (objTag == "Wood") { bestSurface = SurfaceType.Wood; foundSpecificSurface = true; }
                else if (objTag == "Concrete") { bestSurface = SurfaceType.Concrete; foundSpecificSurface = true; }
                else if (objTag == "Pavement") { bestSurface = SurfaceType.Pavement; foundSpecificSurface = true; }
                else if (objTag == "Grass") { bestSurface = SurfaceType.Grass; foundSpecificSurface = true; }
                else if (objTag == "Carpet") { bestSurface = SurfaceType.Carpet; foundSpecificSurface = true; }
            }

            // Check if player is inside the building roof trigger
            if (hit.GetComponent<RoofFader>() != null)
            {
                isInsideRoof = true;
            }
        }

        if (foundSpecificSurface)
        {
            return bestSurface;
        }

        // Default to Concrete inside the house and as general fallback unless overridden by a FloorSurface
        if (isInsideRoof)
        {
            return SurfaceType.Concrete;
        }

        return SurfaceType.Concrete;
    }
}
