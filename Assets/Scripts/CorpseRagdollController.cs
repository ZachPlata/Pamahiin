using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Articulated 6-part 2D corpse ragdoll (Torso kinematic anchor + Head, LeftArm, RightArm,
/// LeftLeg, RightLeg dynamic Rigidbody2Ds linked via HingeJoint2Ds with anatomical limits).
/// </summary>
public class CorpseRagdollController : MonoBehaviour
{
    public const string RagdollSheetResourcePath = "Sprites/CHARACTERS/player_corpse_ragdoll_sheet";
    public const float ActiveLinearDamping = 5.5f;
    public const float ActiveAngularDamping = 3.5f;
    public const float SettledLinearDamping = 14f;
    public const float SettledAngularDamping = 10f;

    private struct LimbSpec
    {
        public string NodeName;
        public string SpriteName;
        public Vector2 SocketOffset;
        public float MinAngle;
        public float MaxAngle;
        public float RestSlumpAngle;
        public Vector2 ColliderOffset;
        public float ColliderRadius;
    }

    // Sibling order controls draw order within identical DOTSLayer / sortingOrder = -1:
    // legs underneath, arms next, torso body, head above shoulders.
    private static readonly LimbSpec[] LimbSpecs =
    {
        new LimbSpec
        {
            NodeName = "LeftLeg",
            SpriteName = "Corpse_LeftLeg",
            SocketOffset = new Vector2(-0.07f, -0.11f),
            MinAngle = -42f,
            MaxAngle = 42f,
            RestSlumpAngle = 12f,
            ColliderOffset = new Vector2(0f, -0.14f),
            ColliderRadius = 0.055f
        },
        new LimbSpec
        {
            NodeName = "RightLeg",
            SpriteName = "Corpse_RightLeg",
            SocketOffset = new Vector2(0.07f, -0.11f),
            MinAngle = -42f,
            MaxAngle = 42f,
            RestSlumpAngle = -10f,
            ColliderOffset = new Vector2(0f, -0.14f),
            ColliderRadius = 0.055f
        },
        new LimbSpec
        {
            NodeName = "LeftArm",
            SpriteName = "Corpse_LeftArm",
            SocketOffset = new Vector2(-0.12f, 0.07f),
            MinAngle = -85f,
            MaxAngle = 95f,
            RestSlumpAngle = 26f,
            ColliderOffset = new Vector2(-0.07f, -0.09f),
            ColliderRadius = 0.05f
        },
        new LimbSpec
        {
            NodeName = "RightArm",
            SpriteName = "Corpse_RightArm",
            SocketOffset = new Vector2(0.12f, 0.07f),
            MinAngle = -85f,
            MaxAngle = 95f,
            RestSlumpAngle = -22f,
            ColliderOffset = new Vector2(0.07f, -0.09f),
            ColliderRadius = 0.05f
        },
        new LimbSpec
        {
            NodeName = "Head",
            SpriteName = "Corpse_Head",
            SocketOffset = new Vector2(0f, 0.11f),
            MinAngle = -48f,
            MaxAngle = 48f,
            RestSlumpAngle = -14f,
            ColliderOffset = new Vector2(0f, 0.09f),
            ColliderRadius = 0.08f
        }
    };

    private Transform ragdollRoot;
    private Rigidbody2D torsoBody;
    private SpriteRenderer torsoRenderer;
    private Collider2D cachedRootTrigger;
    private bool initialized;
    private bool isDragging;

    private readonly List<Rigidbody2D> limbBodies = new List<Rigidbody2D>(5);
    private readonly List<HingeJoint2D> limbJoints = new List<HingeJoint2D>(5);
    private readonly List<SpriteRenderer> allPartRenderers = new List<SpriteRenderer>(6);
    private readonly List<Collider2D> limbColliders = new List<Collider2D>(5);

    public Transform RagdollRoot => ragdollRoot;
    public Rigidbody2D TorsoBody => torsoBody;
    public IReadOnlyList<Rigidbody2D> LimbBodies => limbBodies;
    public IReadOnlyList<HingeJoint2D> LimbJoints => limbJoints;
    public IReadOnlyList<SpriteRenderer> AllPartRenderers => allPartRenderers;
    public bool IsRagdollActive => ragdollRoot != null && ragdollRoot.gameObject.activeSelf;
    public bool IsDragging => isDragging;

    public void EnsureInitialized(Collider2D rootTrigger = null)
    {
        if (rootTrigger != null)
        {
            cachedRootTrigger = rootTrigger;
        }
        else if (cachedRootTrigger == null)
        {
            cachedRootTrigger = GetComponent<Collider2D>();
        }

        if (initialized && ragdollRoot != null && torsoBody != null && limbBodies.Count == LimbSpecs.Length)
        {
            IgnoreInternalAndPlayerCollisions();
            return;
        }

        Dictionary<string, Sprite> spriteMap = LoadRagdollSprites();

        Transform existingRoot = transform.Find("RagdollRoot");
        if (existingRoot == null)
        {
            GameObject rootGo = new GameObject("RagdollRoot");
            rootGo.transform.SetParent(transform, false);
            existingRoot = rootGo.transform;
        }
        existingRoot.localPosition = Vector3.zero;
        existingRoot.localRotation = Quaternion.identity;
        existingRoot.localScale = Vector3.one;
        ragdollRoot = existingRoot;

        limbBodies.Clear();
        limbJoints.Clear();
        limbColliders.Clear();
        allPartRenderers.Clear();

        // Create Torso first so dynamic limbs can bind HingeJoint2Ds to torsoBody,
        // then arrange sibling indices cleanly so legs -> arms -> torso -> head draw back-to-front.
        Transform torsoTf = EnsurePartNode(ragdollRoot, "Torso");
        torsoTf.localPosition = Vector3.zero;
        torsoTf.localRotation = Quaternion.identity;
        torsoTf.localScale = Vector3.one;

        torsoRenderer = ConfigureSpriteRenderer(torsoTf.gameObject, spriteMap, "Corpse_Torso");
        torsoBody = GetOrAdd<Rigidbody2D>(torsoTf.gameObject);
        torsoBody.bodyType = RigidbodyType2D.Kinematic;
        torsoBody.gravityScale = 0f;
        torsoBody.interpolation = RigidbodyInterpolation2D.Interpolate;
        torsoBody.collisionDetectionMode = CollisionDetectionMode2D.Discrete;
        allPartRenderers.Add(torsoRenderer);

        for (int i = 0; i < LimbSpecs.Length; i++)
        {
            LimbSpec spec = LimbSpecs[i];
            Transform limbTf = EnsurePartNode(ragdollRoot, spec.NodeName);
            limbTf.localPosition = new Vector3(spec.SocketOffset.x, spec.SocketOffset.y, 0f);
            limbTf.localRotation = Quaternion.Euler(0f, 0f, spec.RestSlumpAngle);
            limbTf.localScale = Vector3.one;

            SpriteRenderer sr = ConfigureSpriteRenderer(limbTf.gameObject, spriteMap, spec.SpriteName);
            allPartRenderers.Add(sr);

            Rigidbody2D rb = GetOrAdd<Rigidbody2D>(limbTf.gameObject);
            rb.bodyType = RigidbodyType2D.Dynamic;
            rb.gravityScale = 0f;
            rb.mass = 0.35f;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            rb.linearDamping = SettledLinearDamping;
            rb.angularDamping = SettledAngularDamping;
            limbBodies.Add(rb);

            CircleCollider2D col = GetOrAdd<CircleCollider2D>(limbTf.gameObject);
            col.isTrigger = false;
            col.offset = spec.ColliderOffset;
            col.radius = spec.ColliderRadius;
            limbColliders.Add(col);

            HingeJoint2D joint = GetOrAdd<HingeJoint2D>(limbTf.gameObject);
            joint.connectedBody = torsoBody;
            joint.autoConfigureConnectedAnchor = false;
            joint.anchor = Vector2.zero;
            joint.connectedAnchor = spec.SocketOffset;
            joint.useLimits = true;
            joint.limits = new JointAngleLimits2D
            {
                min = spec.MinAngle,
                max = spec.MaxAngle
            };
            limbJoints.Add(joint);
        }

        // Order siblings for deterministic back-to-front draw inside DOTSLayer (-1):
        // LeftLeg(0), RightLeg(1), LeftArm(2), RightArm(3), Torso(4), Head(5)
        Transform headTf = ragdollRoot.Find("Head");
        torsoTf.SetSiblingIndex(4);
        if (headTf != null) headTf.SetSiblingIndex(5);

        initialized = true;
        IgnoreInternalAndPlayerCollisions();
        ApplyDamping();
    }

    public void ActivateRagdoll(bool active)
    {
        EnsureInitialized(cachedRootTrigger);
        if (ragdollRoot == null) return;

        ragdollRoot.gameObject.SetActive(active);
        if (active)
        {
            ResetLimbPosesToRest();
            IgnoreInternalAndPlayerCollisions();
            ApplyDamping();
        }
    }

    public void SetDragging(bool dragging)
    {
        isDragging = dragging;
        if (!initialized) EnsureInitialized(cachedRootTrigger);

        if (dragging)
        {
            IgnoreInternalAndPlayerCollisions();
        }

        ApplyDamping();
    }

    public void OnDragMotion(Vector2 worldDelta, float deltaTime)
    {
        if (!initialized || ragdollRoot == null || !ragdollRoot.gameObject.activeInHierarchy) return;

        Vector2 safeVel = deltaTime > 0.0001f ? worldDelta / deltaTime : Vector2.zero;
        if (torsoBody != null)
        {
            torsoBody.MovePosition(transform.position);
            torsoBody.MoveRotation(transform.eulerAngles.z);
        }

        if (worldDelta.sqrMagnitude < 0.000001f) return;

        // Inertial lag whip on limbs opposite to torso pull vector.
        Vector2 impulse = -worldDelta * 1.35f;
        Vector2 clampedVel = Vector2.ClampMagnitude(-safeVel * 0.45f, 3.2f);
        for (int i = 0; i < limbBodies.Count; i++)
        {
            Rigidbody2D rb = limbBodies[i];
            if (rb == null) continue;
            float sideSign = (i % 2 == 0) ? 1f : -1f;
            rb.AddForce(impulse, ForceMode2D.Impulse);
            rb.AddTorque(clampedVel.magnitude * sideSign * 0.045f, ForceMode2D.Impulse);
        }
    }

    public void ResetLimbPosesToRest()
    {
        if (!initialized || ragdollRoot == null) return;

        if (torsoBody != null)
        {
            torsoBody.transform.localPosition = Vector3.zero;
            torsoBody.transform.localRotation = Quaternion.identity;
            torsoBody.linearVelocity = Vector2.zero;
            torsoBody.angularVelocity = 0f;
        }

        for (int i = 0; i < LimbSpecs.Length && i < limbBodies.Count; i++)
        {
            LimbSpec spec = LimbSpecs[i];
            Rigidbody2D rb = limbBodies[i];
            if (rb == null) continue;
            rb.transform.localPosition = new Vector3(spec.SocketOffset.x, spec.SocketOffset.y, 0f);
            rb.transform.localRotation = Quaternion.Euler(0f, 0f, spec.RestSlumpAngle);
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
        }
    }

    public void IgnoreInternalAndPlayerCollisions()
    {
        if (!Application.isPlaying) return;

        // Ignore collisions among ragdoll limbs and against root hover/drag trigger.
        for (int i = 0; i < limbColliders.Count; i++)
        {
            Collider2D colA = limbColliders[i];
            if (colA == null) continue;

            if (cachedRootTrigger != null)
            {
                Physics2D.IgnoreCollision(colA, cachedRootTrigger, true);
            }

            for (int j = i + 1; j < limbColliders.Count; j++)
            {
                Collider2D colB = limbColliders[j];
                if (colB != null)
                {
                    Physics2D.IgnoreCollision(colA, colB, true);
                }
            }

            // Ignore live/dead player colliders so corpses never block player movement.
            for (int p = 0; p < PlayerController.AllPlayers.Count; p++)
            {
                PlayerController pc = PlayerController.AllPlayers[p];
                if (pc == null) continue;
                Collider2D[] playerCols = pc.GetComponentsInChildren<Collider2D>(true);
                for (int k = 0; k < playerCols.Length; k++)
                {
                    if (playerCols[k] != null)
                    {
                        Physics2D.IgnoreCollision(colA, playerCols[k], true);
                    }
                }
            }
        }
    }

    private void ApplyDamping()
    {
        float lin = isDragging ? ActiveLinearDamping : SettledLinearDamping;
        float ang = isDragging ? ActiveAngularDamping : SettledAngularDamping;
        for (int i = 0; i < limbBodies.Count; i++)
        {
            Rigidbody2D rb = limbBodies[i];
            if (rb == null) continue;
            rb.linearDamping = lin;
            rb.angularDamping = ang;
        }
    }

    private static Dictionary<string, Sprite> LoadRagdollSprites()
    {
        Dictionary<string, Sprite> map = new Dictionary<string, Sprite>(System.StringComparer.Ordinal);
        Sprite[] loaded = Resources.LoadAll<Sprite>(RagdollSheetResourcePath);
        if (loaded == null) return map;

        for (int i = 0; i < loaded.Length; i++)
        {
            Sprite sp = loaded[i];
            if (sp != null && !string.IsNullOrEmpty(sp.name))
            {
                map[sp.name] = sp;
            }
        }
        return map;
    }

    private static Transform EnsurePartNode(Transform parent, string nodeName)
    {
        Transform existing = parent.Find(nodeName);
        if (existing != null) return existing;

        GameObject go = new GameObject(nodeName);
        go.transform.SetParent(parent, false);
        go.layer = 0; // Default layer collides with Walls without stealing Interactable hover checks.
        return go.transform;
    }

    private static SpriteRenderer ConfigureSpriteRenderer(GameObject go, Dictionary<string, Sprite> map, string spriteName)
    {
        SpriteRenderer sr = GetOrAdd<SpriteRenderer>(go);
        sr.sortingLayerName = "DOTSLayer";
        sr.sortingOrder = -1;
        if (map != null && map.TryGetValue(spriteName, out Sprite sp))
        {
            sr.sprite = sp;
        }
        return sr;
    }

    private static T GetOrAdd<T>(GameObject go) where T : Component
    {
        T c = go.GetComponent<T>();
        return c != null ? c : go.AddComponent<T>();
    }
}
