using UnityEngine;
using Unity.Netcode;

public class CameraFollow : MonoBehaviour
{
    public static CameraFollow Instance { get; private set; }

    private Transform target;
    private Transform targetOverride;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        Setup2DAudioListener();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// Temporarily redirects the camera (and its 2D_AudioListener child) away from the player
    /// onto a stage anchor such as the off-bounds Burial Map pit. Pass null to restore normal tracking.
    /// </summary>
    public void SetTargetOverride(Transform overrideTarget)
    {
        targetOverride = overrideTarget;
        if (targetOverride != null)
        {
            transform.position = new Vector3(targetOverride.position.x, targetOverride.position.y, -10f);
        }
        else if (target != null)
        {
            transform.position = new Vector3(target.position.x, target.position.y, -10f);
        }
    }

    private void Setup2DAudioListener()
    {
        // In 2D top-down games, the camera is positioned at Z = -10 to view sprites at Z = 0.
        // If the AudioListener is on the camera at Z = -10, all 3D AudioSources at Z = 0
        // are at least 10m away in 3D space, which mutes all 3D sound sources with maxDistance < 10m
        // (ambient noise, footsteps, equipment, etc.).
        // We move the AudioListener to a child object with localPosition (0, 0, 10) so its world Z is always 0!
        var camListener = GetComponent<AudioListener>();
        if (camListener != null)
        {
            Destroy(camListener);
        }

        var listenerChild = transform.Find("2D_AudioListener");
        if (listenerChild == null)
        {
            var go = new GameObject("2D_AudioListener");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 0f, 10f);
            go.AddComponent<AudioListener>();
        }
        else if (listenerChild.GetComponent<AudioListener>() == null)
        {
            listenerChild.gameObject.AddComponent<AudioListener>();
        }
    }

    private void LateUpdate()
    {
        if (targetOverride != null)
        {
            transform.position = new Vector3(targetOverride.position.x, targetOverride.position.y, -10f);
            return;
        }

        // If we don't have a target yet, look for the local player
        if (target == null)
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.SpawnManager != null)
            {
                var localPlayer = NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject();
                if (localPlayer != null)
                {
                    target = localPlayer.transform;
                }
            }
            return;
        }

        // Follow the target (keeping the camera's Z position at -10)
        transform.position = new Vector3(target.position.x, target.position.y, -10f);
    }
}