using UnityEngine;

/// <summary>
/// Interactable Ritual Exorcism Circle in Bay 4 of WarehouseTutorialScene.
/// Teaches the guided exorcism step before heading to Bay 5 for Journal deduction & Jeepney extraction.
/// </summary>
public class TutorialExorcismSpotInteractable : MonoBehaviour, IInteractable
{
    [SerializeField] private string interactPrompt = "Perform Guided Exorcism Ritual [E]";
    [SerializeField] private SpriteRenderer circleRenderer;
    private bool completed = false;

    public void Interact()
    {
        if (completed) return;
        var mgr = WarehouseTutorialManager.Instance;
        if (mgr == null) return;

        // Require surviving the simulated hunt & taking pills first (or allow if already at exorcism step)
        if (!mgr.IsStepCompleted(WarehouseTutorialManager.STEP_B4_SURVIVE_HUNT))
        {
            mgr.EmitHint("Survive the simulated hunt and use Sanity Pills before performing the exorcism ritual!");
            return;
        }

        completed = true;
        if (circleRenderer != null)
        {
            circleRenderer.color = new Color(1f, 0.35f, 0.25f, 1f);
        }

        GameAudioManager.PlayUIClick();
        mgr.TriggerGuidedExorcismStep();
    }

    public string GetInteractText() => completed ? "Entity Banished" : interactPrompt;

    public bool CanDrag() => false;
    public void OnDragBegin(ulong clientId) { }
    public void OnDragUpdate(Vector2 targetPos) { }
    public void OnDragEnd(ulong clientId) { }
}
