using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Seamless auto-teleport trigger placed at stair landings (Main Floor <-> Basement).
/// Preserves player held items/state via PlayerController.TeleportTo and prevents rapid re-triggering.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class StairTeleporter : MonoBehaviour
{
    [Header("Stair Teleport Target")]
    [SerializeField] private StairTeleporter destinationTeleporter;
    [SerializeField] private Transform arrivalSpawnPoint;
    [SerializeField] private string destinationFloorLabel = "Basement";

    [Header("Transition Timing")]
    [SerializeField] private float fadeDuration = 0.28f;
    [SerializeField] private float arrivalCooldownSeconds = 1.25f;

    [Header("Backup Mid-Stair Node")]
    [SerializeField] private bool spawnMidSpriteBackupNode = true;
    [SerializeField, HideInInspector] private bool isBackupNode = false;

    private static readonly Dictionary<ulong, float> playerCooldowns = new Dictionary<ulong, float>();
    private static readonly HashSet<ulong> activeTeleportPlayers = new HashSet<ulong>();
    private static float screenFadeAlpha = 0f;
    private static Texture2D fadeTexture;
    private static int activeFadeRequests = 0;

    public static readonly List<StairTeleporter> All = new List<StairTeleporter>();
    public StairTeleporter Destination => destinationTeleporter;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        playerCooldowns.Clear();
        activeTeleportPlayers.Clear();
        screenFadeAlpha = 0f;
        activeFadeRequests = 0;
        All.Clear();
    }

    private void OnEnable()
    {
        if (!isBackupNode && !All.Contains(this))
        {
            All.Add(this);
        }
    }

    private void OnDisable()
    {
        All.Remove(this);
    }

    private void Awake()
    {
        Collider2D col = GetComponent<Collider2D>();
        if (col != null)
        {
            col.isTrigger = true;
        }
    }

    private void Start()
    {
        EnsureMidSpriteBackupNode();
    }

    public void Configure(StairTeleporter destination, Transform spawnPoint, string floorLabel)
    {
        destinationTeleporter = destination;
        arrivalSpawnPoint = spawnPoint;
        destinationFloorLabel = floorLabel;

        SyncExistingMidBackupNode();
    }

    public void ConfigureAsBackupNode(
        StairTeleporter destination,
        Transform spawnPoint,
        string floorLabel,
        float fade,
        float cooldown)
    {
        isBackupNode = true;
        spawnMidSpriteBackupNode = false;
        All.Remove(this);
        destinationTeleporter = destination;
        arrivalSpawnPoint = spawnPoint;
        destinationFloorLabel = floorLabel;
        fadeDuration = fade;
        arrivalCooldownSeconds = cooldown;
    }

    public void EnsureMidSpriteBackupNode()
    {
        if (isBackupNode || !spawnMidSpriteBackupNode || destinationTeleporter == null) return;

        Transform existing = transform.Find("MidStairTeleporterNode");
        if (existing != null)
        {
            SyncExistingMidBackupNode();
            return;
        }

        var midGo = new GameObject("MidStairTeleporterNode");
        midGo.layer = gameObject.layer;
        midGo.transform.SetParent(transform, false);
        midGo.transform.localPosition = Vector3.zero;
        midGo.transform.localRotation = Quaternion.identity;
        midGo.transform.localScale = Vector3.one;

        var midCol = midGo.AddComponent<BoxCollider2D>();
        midCol.isTrigger = true;

        var parentBox = GetComponent<BoxCollider2D>();
        if (parentBox != null)
        {
            midCol.offset = parentBox.offset;
            midCol.size = new Vector2(
                Mathf.Max(0.25f, parentBox.size.x * 0.4f),
                Mathf.Max(0.25f, parentBox.size.y * 0.8f));
        }
        else if (TryGetComponent<SpriteRenderer>(out var sr) && sr.sprite != null)
        {
            Bounds b = sr.sprite.bounds;
            midCol.offset = b.center;
            midCol.size = new Vector2(
                Mathf.Max(0.25f, b.size.x * 0.4f),
                Mathf.Max(0.25f, b.size.y * 0.8f));
        }
        else
        {
            midCol.size = new Vector2(0.5f, 0.4f);
        }

        var midTp = midGo.AddComponent<StairTeleporter>();
        midTp.ConfigureAsBackupNode(
            destinationTeleporter,
            arrivalSpawnPoint,
            destinationFloorLabel,
            fadeDuration,
            arrivalCooldownSeconds);
    }

    private void SyncExistingMidBackupNode()
    {
        Transform existing = transform.Find("MidStairTeleporterNode");
        if (existing == null) return;

        var midTp = existing.GetComponent<StairTeleporter>();
        if (midTp != null)
        {
            midTp.ConfigureAsBackupNode(
                destinationTeleporter,
                arrivalSpawnPoint,
                destinationFloorLabel,
                fadeDuration,
                arrivalCooldownSeconds);
        }
    }

    public Vector3 GetArrivalPosition()
    {
        return arrivalSpawnPoint != null ? arrivalSpawnPoint.position : transform.position;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        TryTriggerTeleport(other);
    }

    private void TryTriggerTeleport(Collider2D other)
    {
        if (destinationTeleporter == null) return;

        PlayerController player = other.GetComponentInParent<PlayerController>();
        if (player == null || (!player.IsAlive && !player.IsGhostSpectator)) return;

        // Only the owner triggers teleports in a networked session; allow offline/unspawned testing.
        bool networked = Unity.Netcode.NetworkManager.Singleton != null
            && Unity.Netcode.NetworkManager.Singleton.IsListening
            && player.IsSpawned;
        if (networked && !player.IsOwner) return;

        ulong id = player.OwnerClientId;
        if (activeTeleportPlayers.Contains(id)) return;
        if (playerCooldowns.TryGetValue(id, out float blockedUntil) && Time.unscaledTime < blockedUntil)
        {
            return;
        }

        // Prevent immediate ping-pong at either end
        activeTeleportPlayers.Add(id);
        playerCooldowns[id] = Time.unscaledTime + arrivalCooldownSeconds;
        StartCoroutine(TeleportRoutine(player, id));
    }

    private IEnumerator TeleportRoutine(PlayerController player, ulong playerId)
    {
        activeFadeRequests++;
        if (player != null && player.IsAlive)
        {
            GameAudioManager.PlayAt("stair_footsteps", player.transform.position, 0.8f, 1.0f, Random.Range(0.95f, 1.05f), 0.45f, 3.2f, 0.1f);
        }

        float halfFade = Mathf.Max(0.05f, fadeDuration * 0.45f);
        float t = 0f;
        while (t < halfFade)
        {
            t += Time.unscaledDeltaTime;
            screenFadeAlpha = Mathf.Clamp01(t / halfFade);
            yield return null;
        }
        screenFadeAlpha = 1f;

        if (player != null && (player.IsAlive || player.IsGhostSpectator))
        {
            Vector3 destPos = destinationTeleporter.GetArrivalPosition();
            destPos.z = player.transform.position.z;
            playerCooldowns[playerId] = Time.unscaledTime + arrivalCooldownSeconds;
            player.TeleportTo(destPos);

            TutorialTooltipManager.TriggerCustomHint(
                $"ENTERING {destinationFloorLabel.ToUpper()}",
                "STAIRS",
                $"Moved to {destinationFloorLabel}.",
                $"stairs_transition_{destinationFloorLabel.Replace(" ", "")}",
                2.0f
            );
        }

        yield return new WaitForSecondsRealtime(0.05f);

        t = 0f;
        while (t < halfFade)
        {
            t += Time.unscaledDeltaTime;
            screenFadeAlpha = 1f - Mathf.Clamp01(t / halfFade);
            yield return null;
        }

        activeTeleportPlayers.Remove(playerId);
        activeFadeRequests = Mathf.Max(0, activeFadeRequests - 1);
        if (activeFadeRequests == 0)
        {
            screenFadeAlpha = 0f;
        }
    }

    private void OnGUI()
    {
        if (screenFadeAlpha <= 0.005f) return;

        if (fadeTexture == null)
        {
            fadeTexture = new Texture2D(1, 1);
            fadeTexture.SetPixel(0, 0, Color.black);
            fadeTexture.Apply();
        }

        Color prev = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, screenFadeAlpha);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), fadeTexture);
        GUI.color = prev;
    }

    private void OnDestroy()
    {
        if (activeFadeRequests == 0)
        {
            screenFadeAlpha = 0f;
        }
    }
}
