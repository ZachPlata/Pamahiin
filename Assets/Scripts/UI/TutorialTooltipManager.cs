using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public enum TutorialHintId
{
    ItemPickup,
    HousePower,
    Flashlight,
    EMFReader,
    SpiritBox,
    Thermometer,
    WritingBook,
    VideoCamera,
    RitualChalk,
    Crucifix,
    SanityPills,
    Movement,
    Journal,
    GhostHunt
}

public class TutorialTooltipManager : MonoBehaviour
{
    public static TutorialTooltipManager Instance { get; private set; }

    public struct TutorialHintData
    {
        public string uniqueId;
        public string title;
        public string keyBadge;
        public string description;
        public float duration;

        public TutorialHintData(string id, string title, string keyBadge, string desc, float duration = 5.5f)
        {
            this.uniqueId = id;
            this.title = title;
            this.keyBadge = keyBadge;
            this.description = desc;
            this.duration = duration;
        }
    }

    private static readonly Dictionary<TutorialHintId, TutorialHintData> Catalog = new Dictionary<TutorialHintId, TutorialHintData>()
    {
        {
            TutorialHintId.ItemPickup,
            new TutorialHintData(
                "ItemPickup",
                "PICKING UP ITEMS",
                "[E]",
                "Stand near an item, aim your cursor at it, and press [E] to pick it up into your inventory.",
                5.5f
            )
        },
        {
            TutorialHintId.HousePower,
            new TutorialHintData(
                "HousePower",
                "HOUSE LIGHTING",
                "[E]",
                "Flip the white wall switches to turn on room lights. Note: You must first restore power at the fuse box (gray breaker box on the wall) to enable all lights.",
                6.5f
            )
        },
        {
            TutorialHintId.Flashlight,
            new TutorialHintData(
                "Flashlight",
                "EQUIPMENT: FLASHLIGHT",
                "[L-CLICK] / [R-CLICK]",
                "Left-Click to turn on or toggle light. Right-Click to place it down on the floor.",
                5.5f
            )
        },
        {
            TutorialHintId.EMFReader,
            new TutorialHintData(
                "EMFReader",
                "EQUIPMENT: EMF READER",
                "[L-CLICK] / [R-CLICK]",
                "Left-Click to turn on and detect paranormal spikes. Right-Click to place it down on the floor.",
                5.5f
            )
        },
        {
            TutorialHintId.SpiritBox,
            new TutorialHintData(
                "SpiritBox",
                "EQUIPMENT: SPIRIT BOX",
                "[L-CLICK] / [R-CLICK]",
                "Left-Click inside the Ghost Room to power on and speak. Right-Click to place it down on the floor.",
                5.5f
            )
        },
        {
            TutorialHintId.Thermometer,
            new TutorialHintData(
                "Thermometer",
                "EQUIPMENT: THERMOMETER",
                "[L-CLICK] / [R-CLICK]",
                "Left-Click to measure ambient room temperatures. Right-Click to place it down on the floor.",
                5.5f
            )
        },
        {
            TutorialHintId.WritingBook,
            new TutorialHintData(
                "WritingBook",
                "EQUIPMENT: WRITING BOOK",
                "[L-CLICK] / [R-CLICK]",
                "Left-Click to inspect. Right-Click inside the Ghost Room to place it down on the floor for writing.",
                5.5f
            )
        },
        {
            TutorialHintId.VideoCamera,
            new TutorialHintData(
                "VideoCamera",
                "EQUIPMENT: VIDEO CAMERA",
                "[L-CLICK] / [R-CLICK]",
                "Left-Click to toggle handheld night vision. Right-Click to place it facing into the room for truck CCTV.",
                5.5f
            )
        },
        {
            TutorialHintId.RitualChalk,
            new TutorialHintData(
                "RitualChalk",
                "EQUIPMENT: RITUAL CHALK",
                "[L-CLICK] / [R-CLICK]",
                "Left-Click to inspect. Right-Click inside the Ghost Room to draw the exorcism diagram on the floor.",
                5.5f
            )
        },
        {
            TutorialHintId.Crucifix,
            new TutorialHintData(
                "Crucifix",
                "CRUCIFIX PROTECTION",
                "[R-CLICK]",
                "Right-Click to place down on the floor to activate. A placed crucifix blocks ghost hunts within its radius.",
                5.5f
            )
        },
        {
            TutorialHintId.SanityPills,
            new TutorialHintData(
                "SanityPills",
                "EQUIPMENT: SANITY PILLS",
                "[L-CLICK] / [R-CLICK]",
                "Left-Click to consume and restore sanity. Right-Click to place it down on the floor.",
                5.5f
            )
        },
        {
            TutorialHintId.Movement,
            new TutorialHintData(
                "Movement",
                "MOVEMENT CONTROLS",
                "[WASD] / [SHIFT]",
                "Use [W][A][S][D] to move and hold [Shift] to sprint. Press [1][2][3] to switch inventory items.",
                5.5f
            )
        },
        {
            TutorialHintId.Journal,
            new TutorialHintData(
                "Journal",
                "EVIDENCE JOURNAL",
                "[J]",
                "Press [J] to open your journal, cross-reference evidence, and determine the ghost entity type.",
                5.5f
            )
        },
        {
            TutorialHintId.GhostHunt,
            new TutorialHintData(
                "GhostHunt",
                "ENTITY HUNTING!",
                "[RUN / HIDE]",
                "The ghost has initiated a hunt! Turn off handheld electronics and hide inside closets or rooms immediately!",
                6.0f
            )
        }
    };

    private VisualElement toastContainer;
    private VisualElement toastCard;
    private Label toastKeyBadge;
    private Label toastTitle;
    private Label toastDesc;
    private VisualElement toastProgressBar;

    private readonly Queue<TutorialHintData> hintQueue = new Queue<TutorialHintData>();
    private bool isDisplaying = false;
    private Coroutine displayCoroutine;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;
    }

    public void BindUI(VisualElement root)
    {
        if (root == null) return;

        toastContainer = root.Q<VisualElement>("TutorialToastContainer");
        toastCard = root.Q<VisualElement>("TutorialToast");
        toastKeyBadge = root.Q<Label>("ToastKeyBadge");
        toastTitle = root.Q<Label>("ToastTitle");
        toastDesc = root.Q<Label>("ToastDescription");
        toastProgressBar = root.Q<VisualElement>("ToastProgressBar");

        if (toastCard != null)
        {
            toastCard.AddToClassList("tutorial-toast-hidden");
        }
    }

    /// <summary>
    /// Resets tutorial history for the current match.
    /// </summary>
    public static void ResetMatchHistory()
    {
        GameSettingsManager.Instance?.ResetMatchTutorialHints();
    }

    /// <summary>
    /// Static trigger for registered tutorial hint IDs.
    /// </summary>
    public static void TriggerHint(TutorialHintId id)
    {
        if (!Catalog.TryGetValue(id, out var hint)) return;
        TriggerHintData(hint);
    }

    /// <summary>
    /// Static trigger for arbitrary custom hints.
    /// </summary>
    public static void TriggerCustomHint(string title, string keyBadge, string description, string uniqueId, float duration = 5.5f)
    {
        var hint = new TutorialHintData(uniqueId, title, keyBadge, description, duration);
        TriggerHintData(hint);
    }

    private static void TriggerHintData(TutorialHintData hint)
    {
        // 1. Settings check
        var settings = GameSettingsManager.Instance;
        if (settings != null)
        {
            if (!settings.TutorialHintsEnabled) return;
            if (settings.HasSeenTutorial(hint.uniqueId)) return;
        }

        // 2. Self-repair instance if missing
        if (Instance == null)
        {
            var hudMgr = Object.FindAnyObjectByType<PlayerHUDManager>();
            if (hudMgr != null)
            {
                Instance = hudMgr.gameObject.GetComponent<TutorialTooltipManager>();
                if (Instance == null)
                {
                    Instance = hudMgr.gameObject.AddComponent<TutorialTooltipManager>();
                }
            }
            else
            {
                var go = new GameObject("TutorialTooltipManager");
                Instance = go.AddComponent<TutorialTooltipManager>();
            }
        }

        Instance.EnqueueHint(hint);
    }

    public void EnqueueHint(TutorialHintData hint)
    {
        // Check if already in queue
        foreach (var h in hintQueue)
        {
            if (h.uniqueId == hint.uniqueId) return;
        }

        hintQueue.Enqueue(hint);

        if (!isDisplaying)
        {
            displayCoroutine = StartCoroutine(ProcessQueueRoutine());
        }
    }

    private IEnumerator ProcessQueueRoutine()
    {
        isDisplaying = true;

        while (hintQueue.Count > 0)
        {
            var currentHint = hintQueue.Dequeue();

            // Re-verify settings before showing (in case user disabled mid-match)
            var settings = GameSettingsManager.Instance;
            if (settings != null)
            {
                if (!settings.TutorialHintsEnabled) continue;
                if (settings.HasSeenTutorial(currentHint.uniqueId)) continue;
            }

            // Ensure UI elements are bound
            if (toastCard == null)
            {
                var hudDoc = Object.FindAnyObjectByType<UIDocument>();
                if (hudDoc != null && hudDoc.rootVisualElement != null)
                {
                    BindUI(hudDoc.rootVisualElement);
                }
            }

            if (toastCard == null)
            {
                yield return new WaitForSeconds(0.5f);
                continue;
            }

            // Populate content
            if (toastKeyBadge != null)
            {
                toastKeyBadge.text = currentHint.keyBadge;
                toastKeyBadge.style.display = string.IsNullOrEmpty(currentHint.keyBadge) ? DisplayStyle.None : DisplayStyle.Flex;
            }
            if (toastTitle != null) toastTitle.text = currentHint.title;
            if (toastDesc != null) toastDesc.text = currentHint.description;
            if (toastProgressBar != null) toastProgressBar.style.width = Length.Percent(100f);

            // Mark as seen in persistence
            settings?.MarkTutorialSeen(currentHint.uniqueId);

            // Animate Slide In
            toastCard.style.display = DisplayStyle.Flex;
            yield return null; // Wait 1 frame so layout registers display: flex
            toastCard.RemoveFromClassList("tutorial-toast-hidden");

            // Deplete Progress Bar over Duration
            float duration = Mathf.Max(2.5f, currentHint.duration);
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float progress = Mathf.Clamp01(1f - (elapsed / duration));
                if (toastProgressBar != null)
                {
                    toastProgressBar.style.width = Length.Percent(progress * 100f);
                }
                yield return null;
            }

            // Animate Slide Out
            toastCard.AddToClassList("tutorial-toast-hidden");
            yield return new WaitForSeconds(0.35f); // Wait for CSS transition
            toastCard.style.display = DisplayStyle.None;

            // Small breather between consecutive toasts
            yield return new WaitForSeconds(0.2f);
        }

        isDisplaying = false;
        displayCoroutine = null;
    }
}
