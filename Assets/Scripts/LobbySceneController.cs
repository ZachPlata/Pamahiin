using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages player positioning, lobby HUD notifications, and 16-bit SNES weathered dust particles in LobbyScene.
/// Map geometry and props are placed directly in the scene hierarchy so they can be modified manually in the Editor.
/// </summary>
public class LobbySceneController : MonoBehaviour
{
    [Header("Spawn Settings")]
    [SerializeField] private Transform defaultSpawnPoint;

    [Header("16-Bit Weathered Dust Atmosphere")]
    [SerializeField] private Sprite dustMoteSprite;
    [SerializeField] private int floatingDustCount = 36;
    [SerializeField] private int groundDustClusterCount = 48;

    private struct FloatingDustMote
    {
        public Transform transform;
        public SpriteRenderer renderer;
        public Vector3 basePos;
        public float speed;
        public float phase;
        public float driftRadius;
        public float baseAlpha;
    }

    private readonly List<FloatingDustMote> activeDustMotes = new List<FloatingDustMote>();
    private static Sprite fallbackPixelDustSprite;

    private void Awake()
    {
        ProgressionAndLoadoutManager.EnsureInstance();
        SpawnWeatheredLobbyDust();
    }

    private void Start()
    {
        StartCoroutine(PositionPlayersAndShowWelcomeRoutine());
    }

    private void Update()
    {
        float t = Time.time;
        for (int i = 0; i < activeDustMotes.Count; i++)
        {
            var mote = activeDustMotes[i];
            if (mote.transform == null) continue;

            // Snap movement to 16-bit pixel grid steps (1/100th world unit) for authentic SNES feel
            float ox = Mathf.Sin(t * mote.speed + mote.phase) * mote.driftRadius;
            float oy = Mathf.Cos(t * (mote.speed * 0.75f) + mote.phase * 1.3f) * (mote.driftRadius * 0.65f);
            Vector3 rawPos = mote.basePos + new Vector3(ox, oy, 0f);
            rawPos.x = Mathf.Round(rawPos.x * 200f) / 200f;
            rawPos.y = Mathf.Round(rawPos.y * 200f) / 200f;
            mote.transform.position = rawPos;

            if (mote.renderer != null)
            {
                float pulse = 0.65f + 0.35f * Mathf.Sin(t * (mote.speed * 1.8f) + mote.phase);
                Color c = mote.renderer.color;
                c.a = mote.baseAlpha * pulse;
                mote.renderer.color = c;
            }
        }
    }

    private void SpawnWeatheredLobbyDust()
    {
        Sprite spriteToUse = dustMoteSprite != null ? dustMoteSprite : GetOrCreatePixelDustSprite();
        Transform container = new GameObject("LobbyWeatheredDust_16Bit").transform;
        container.SetParent(transform, false);

        Random.State oldState = Random.state;
        Random.InitState(1616);

        // 1. Static weathered ground dust specks & floor debris clusters
        for (int i = 0; i < groundDustClusterCount; i++)
        {
            GameObject speck = new GameObject($"GroundDust_{i}");
            speck.transform.SetParent(container, false);
            float rx = Random.Range(-1.08f, 1.08f);
            float ry = Random.Range(-0.68f, 0.68f);
            speck.transform.position = new Vector3(Mathf.Round(rx * 100f) / 100f, Mathf.Round(ry * 100f) / 100f, 0f);
            float scale = Random.value > 0.7f ? 1.5f : 1f;
            speck.transform.localScale = new Vector3(scale, scale, 1f);

            var sr = speck.AddComponent<SpriteRenderer>();
            sr.sprite = spriteToUse;
            sr.sortingOrder = 1;
            bool nearCorners = Mathf.Abs(rx) > 0.75f || Mathf.Abs(ry) > 0.48f;
            float alpha = nearCorners ? Random.Range(0.32f, 0.55f) : Random.Range(0.18f, 0.35f);
            sr.color = new Color(0.68f, 0.62f, 0.50f, alpha);
        }

        // 2. Drifting 16-bit dust motes illuminated by the room & overhead table spotlight
        for (int i = 0; i < floatingDustCount; i++)
        {
            GameObject moteObj = new GameObject($"FloatingDustMote_{i}");
            moteObj.transform.SetParent(container, false);

            // Bias half of the floating motes toward the warm central briefing table spotlight and armory rack
            Vector3 pos;
            bool inSpotlight = i < (floatingDustCount / 2);
            if (inSpotlight)
            {
                pos = new Vector3(Random.Range(-0.48f, 0.48f), Random.Range(-0.22f, 0.42f), 0f);
            }
            else
            {
                pos = new Vector3(Random.Range(-1.05f, 1.05f), Random.Range(-0.65f, 0.65f), 0f);
            }

            moteObj.transform.position = pos;
            var sr = moteObj.AddComponent<SpriteRenderer>();
            sr.sprite = spriteToUse;
            sr.sortingOrder = 4;

            float baseAlpha = inSpotlight ? Random.Range(0.45f, 0.78f) : Random.Range(0.22f, 0.45f);
            sr.color = inSpotlight
                ? new Color(1.0f, 0.90f, 0.62f, baseAlpha)
                : new Color(0.76f, 0.72f, 0.64f, baseAlpha);

            activeDustMotes.Add(new FloatingDustMote
            {
                transform = moteObj.transform,
                renderer = sr,
                basePos = pos,
                speed = Random.Range(0.45f, 1.15f),
                phase = Random.Range(0f, Mathf.PI * 2f),
                driftRadius = Random.Range(0.03f, 0.09f),
                baseAlpha = baseAlpha
            });
        }

        Random.state = oldState;
    }

    private static Sprite GetOrCreatePixelDustSprite()
    {
        if (fallbackPixelDustSprite != null) return fallbackPixelDustSprite;

        Texture2D tex = new Texture2D(4, 4, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };

        Color clear = new Color(0f, 0f, 0f, 0f);
        Color outline = new Color(0.18f, 0.14f, 0.10f, 0.65f);
        Color core = new Color(0.95f, 0.88f, 0.70f, 1f);

        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 4; x++)
                tex.SetPixel(x, y, clear);

        tex.SetPixel(1, 1, core);
        tex.SetPixel(2, 1, core);
        tex.SetPixel(1, 2, core);
        tex.SetPixel(2, 2, outline);
        tex.Apply();

        fallbackPixelDustSprite = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 100f);
        return fallbackPixelDustSprite;
    }

    private IEnumerator PositionPlayersAndShowWelcomeRoutine()
    {
        yield return new WaitForSeconds(0.15f);

        Transform spawnTrans = defaultSpawnPoint;
        if (spawnTrans == null)
        {
            var spawnObj = Object.FindAnyObjectByType<PlayerSpawnPoint>();
            if (spawnObj != null) spawnTrans = spawnObj.transform;
        }

        Vector3 basePos = spawnTrans != null ? spawnTrans.position : new Vector3(0f, -0.35f, 0f);

        // Position all spawned players inside the Safe House Lobby room
        int idx = 0;
        foreach (var player in PlayerController.AllPlayers)
        {
            if (player != null)
            {
                Vector3 spawnOffset = basePos + new Vector3((idx % 2 == 0 ? -0.2f : 0.2f), (idx / 2) * 0.2f, 0f);
                player.TeleportTo(spawnOffset);
                player.RestoreSanity(100f);
                idx++;
            }
        }

        TutorialTooltipManager.TriggerCustomHint(
            "SAFE HOUSE LOBBY",
            "[J] / [E]",
            "Press [J] or Interact [E] with the Briefing Table to set Map, Difficulty, Party Code & Equipment Loadout.",
            "lobby_welcome_hint",
            8f);
    }
}
