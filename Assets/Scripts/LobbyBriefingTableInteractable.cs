using UnityEngine;

/// <summary>
/// Interactable Mission & Equipment Table inside the Safe House Lobby.
/// Pressing E or clicking it opens the Journal directly to the Lobby/Multiplayer/Loadout tab.
/// </summary>
public class LobbyBriefingTableInteractable : MonoBehaviour, IInteractable
{
    [SerializeField] private string interactPrompt = "Open Safe House Briefing & Loadout [E]";

    public void Interact()
    {
        var journal = Object.FindAnyObjectByType<JournalController>();
        if (journal != null)
        {
            journal.OpenToLobbyTab();
        }
    }

    public string GetInteractText() => interactPrompt;

    public bool CanDrag() => false;
    public void OnDragBegin(ulong clientId) { }
    public void OnDragUpdate(Vector2 targetPos) { }
    public void OnDragEnd(ulong clientId) { }
}

