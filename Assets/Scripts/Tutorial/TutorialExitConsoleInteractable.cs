using UnityEngine;

/// <summary>
/// Interactable exit console in Bay 5 of WarehouseTutorialScene.
/// Pressing [E] completes the tutorial contract and saves completion.
/// </summary>
public class TutorialExitConsoleInteractable : MonoBehaviour, IInteractable
{
    [SerializeField] private string interactPrompt = "Complete Tutorial Contract [E]";

    public void Interact()
    {
        if (WarehouseTutorialManager.Instance != null)
        {
            WarehouseTutorialManager.Instance.CompleteTutorialContractStep();
        }
    }

    public string GetInteractText() => interactPrompt;

    public bool CanDrag() => false;
    public void OnDragBegin(ulong clientId) { }
    public void OnDragUpdate(Vector2 targetPos) { }
    public void OnDragEnd(ulong clientId) { }
}
