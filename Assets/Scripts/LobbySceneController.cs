using System.Collections;
using UnityEngine;

/// <summary>
/// Interactable Mission & Equipment Table inside the Safe House Lobby.
/// Pressing E or clicking it opens the Journal directly to the Lobby/Multiplayer/Loadout tab.
/// </summary>
public class LobbyBriefingTableInteractable : MonoBehaviour, IInteractable
{
    public void Interact()
    {
        var journal = Object.FindAnyObjectByType<JournalController>();
        if (journal != null)
        {
            journal.OpenToLobbyTab();
        }
    }

    public string GetInteractText() => "Open Safe House Briefing & Loadout [E]";

    public bool CanDrag() => false;
    public void OnDragBegin(ulong clientId) { }
    public void OnDragUpdate(Vector2 targetPos) { }
    public void OnDragEnd(ulong clientId) { }
}

/// <summary>
/// Builds and manages the physical 2D Safe House Lobby Room in LobbyScene so players can walk around,
/// interact with the Briefing Table or press J to open the Journal, configure Map/Difficulty/Multiplayer/Equipment,
/// and launch into the playable investigation map.
/// </summary>
public class LobbySceneController : MonoBehaviour
{
    private static Sprite cachedWhiteSquareSprite;

    private void Awake()
    {
        ProgressionAndLoadoutManager.EnsureInstance();
        BuildSafeHouseLobbyEnvironment();
    }

    private void Start()
    {
        StartCoroutine(PositionPlayersAndShowWelcomeRoutine());
    }

    private IEnumerator PositionPlayersAndShowWelcomeRoutine()
    {
        yield return new WaitForSeconds(0.15f);

        // Position all spawned players inside the Safe House Lobby room
        int idx = 0;
        foreach (var player in PlayerController.AllPlayers)
        {
            if (player != null)
            {
                Vector3 spawnOffset = new Vector3((idx % 2 == 0 ? -0.35f : 0.35f), -0.35f + (idx * 0.2f), 0f);
                player.transform.position = spawnOffset;
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

    private static Sprite GetOrCreateWhiteSprite()
    {
        if (cachedWhiteSquareSprite != null) return cachedWhiteSquareSprite;

        Texture2D tex = new Texture2D(16, 16, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        Color[] pixels = new Color[16 * 16];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.white;
        tex.SetPixels(pixels);
        tex.Apply();

        cachedWhiteSquareSprite = Sprite.Create(tex, new Rect(0, 0, 16, 16), new Vector2(0.5f, 0.5f), 16f);
        return cachedWhiteSquareSprite;
    }

    private void BuildSafeHouseLobbyEnvironment()
    {
        Sprite square = GetOrCreateWhiteSprite();
        int wallLayer = LayerMask.NameToLayer("Walls");
        if (wallLayer < 0) wallLayer = 0;
        int interactLayer = LayerMask.NameToLayer("Interactable");
        if (interactLayer < 0) interactLayer = 8;

        Transform root = new GameObject("SafeHouseLobbyRoom").transform;
        root.SetParent(transform, false);

        // Camera note: Main Camera orthographic size is ~0.3 in GameMapScene, so player scale is ~0.12.
        // Make the Safe House room 2.4m wide x 1.6m tall so it matches the player & camera scale.
        float roomWidth = 2.4f;
        float roomHeight = 1.6f;
        float wallThickness = 0.12f;

        // 1. Main Wooden Floor
        CreateSpriteBox(root, "LobbyFloor", Vector3.zero, new Vector3(roomWidth, roomHeight, 1f),
            new Color(0.18f, 0.14f, 0.11f, 1f), -10, square);

        // 2. Inner Rug / Carpet under Briefing Table
        CreateSpriteBox(root, "CenterRug", new Vector3(0f, 0.05f, 0f), new Vector3(1.1f, 0.7f, 1f),
            new Color(0.32f, 0.14f, 0.12f, 1f), -9, square);

        // 3. Perimeter Walls (Layer: Walls, with BoxCollider2D)
        CreateWall(root, "Wall_North", new Vector3(0f, roomHeight * 0.5f, 0f), new Vector3(roomWidth + wallThickness, wallThickness, 1f), wallLayer, square);
        CreateWall(root, "Wall_South", new Vector3(0f, -roomHeight * 0.5f, 0f), new Vector3(roomWidth + wallThickness, wallThickness, 1f), wallLayer, square);
        CreateWall(root, "Wall_West", new Vector3(-roomWidth * 0.5f, 0f, 0f), new Vector3(wallThickness, roomHeight + wallThickness, 1f), wallLayer, square);
        CreateWall(root, "Wall_East", new Vector3(roomWidth * 0.5f, 0f, 0f), new Vector3(wallThickness, roomHeight + wallThickness, 1f), wallLayer, square);

        // 4. SafeZone covering the entire Lobby so Sanity never drains
        GameObject safeZoneObj = new GameObject("LobbySafeZone");
        safeZoneObj.transform.SetParent(root, false);
        BoxCollider2D safeCol = safeZoneObj.AddComponent<BoxCollider2D>();
        safeCol.isTrigger = true;
        safeCol.size = new Vector2(roomWidth * 2f, roomHeight * 2f);
        safeZoneObj.AddComponent<SafeZone>();

        // 5. Center Mission Briefing Table (Interactable: opens Journal to Lobby Tab)
        GameObject tableObj = CreateSpriteBox(root, "MissionBriefingTable", new Vector3(0f, 0.15f, 0f),
            new Vector3(0.55f, 0.32f, 1f), new Color(0.38f, 0.25f, 0.15f, 1f), 0, square);
        tableObj.layer = interactLayer;
        BoxCollider2D tableSolidCol = tableObj.AddComponent<BoxCollider2D>();
        tableSolidCol.isTrigger = false;
        tableObj.AddComponent<LobbyBriefingTableInteractable>();

        // Map & Journal Props on top of the Briefing Table
        CreateSpriteBox(tableObj.transform, "MapBlueprint", new Vector3(-0.12f, 0.02f, 0f),
            new Vector3(0.38f, 0.55f, 1f), new Color(0.85f, 0.80f, 0.68f, 1f), 1, square);
        CreateSpriteBox(tableObj.transform, "ContractJournalProp", new Vector3(0.22f, -0.05f, 0f),
            new Vector3(0.24f, 0.42f, 1f), new Color(0.55f, 0.22f, 0.16f, 1f), 1, square);

        // 6. Equipment Shelf / Armory Rack along West Wall (Also Interactable to open Loadout)
        GameObject shelfObj = CreateSpriteBox(root, "EquipmentArmoryRack", new Vector3(-0.92f, 0.15f, 0f),
            new Vector3(0.22f, 0.85f, 1f), new Color(0.22f, 0.24f, 0.27f, 1f), 0, square);
        shelfObj.layer = interactLayer;
        shelfObj.AddComponent<BoxCollider2D>();
        shelfObj.AddComponent<LobbyBriefingTableInteractable>();

        // 7. Contract Corkboard along North Wall (Also Interactable)
        GameObject boardObj = CreateSpriteBox(root, "ContractCorkboard", new Vector3(0f, roomHeight * 0.5f - 0.08f, 0f),
            new Vector3(0.75f, 0.08f, 1f), new Color(0.52f, 0.36f, 0.20f, 1f), 1, square);
        boardObj.layer = interactLayer;
        boardObj.AddComponent<BoxCollider2D>();
        boardObj.AddComponent<LobbyBriefingTableInteractable>();
    }

    private static GameObject CreateSpriteBox(Transform parent, string name, Vector3 localPos, Vector3 localScale, Color color, int sortingOrder, Sprite sprite)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = localScale;

        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.color = color;
        sr.sortingOrder = sortingOrder;
        return go;
    }

    private static GameObject CreateWall(Transform parent, string name, Vector3 localPos, Vector3 localScale, int layer, Sprite sprite)
    {
        GameObject wall = CreateSpriteBox(parent, name, localPos, localScale, new Color(0.11f, 0.09f, 0.08f, 1f), 2, sprite);
        wall.layer = layer;
        BoxCollider2D col = wall.AddComponent<BoxCollider2D>();
        col.isTrigger = false;
        return wall;
    }
}

