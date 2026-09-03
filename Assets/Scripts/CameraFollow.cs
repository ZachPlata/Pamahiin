using UnityEngine;
using Unity.Netcode;

public class CameraFollow : MonoBehaviour
{
    private Transform target;

    private void Update()
    {
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