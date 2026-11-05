using System.Collections;
using UnityEngine;

/// <summary>
/// Manages player positioning and lobby HUD notifications in LobbyScene.
/// Map geometry and props are placed directly in the scene hierarchy so they can be modified manually in the Editor.
/// </summary>
public class LobbySceneController : MonoBehaviour
{
    [Header("Spawn Settings")]
    [SerializeField] private Transform defaultSpawnPoint;

    private void Awake()
    {
        ProgressionAndLoadoutManager.EnsureInstance();
    }

    private void Start()
    {
        StartCoroutine(PositionPlayersAndShowWelcomeRoutine());
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
}
