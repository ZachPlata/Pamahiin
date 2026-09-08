using UnityEngine;

/// <summary>
/// Attach this script to a GameObject in your scene to designate it as the player spawn point.
/// </summary>
public class PlayerSpawnPoint : MonoBehaviour
{
    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(0f, 1f, 0f, 0.5f);
        Gizmos.DrawCube(transform.position, Vector3.one);
        Gizmos.color = Color.green;
        Gizmos.DrawWireCube(transform.position, Vector3.one);
    }
}
