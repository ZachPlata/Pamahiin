using System.Collections.Generic;
using UnityEngine;

public class GhostNode : MonoBehaviour
{
    [Tooltip("List of adjacent nodes the ghost can travel to from this node.")]
    public List<GhostNode> neighbors = new List<GhostNode>();

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.magenta;
        Gizmos.DrawSphere(transform.position, 0.05f);
        
        Gizmos.color = Color.yellow;
        if (neighbors != null)
        {
            foreach (var neighbor in neighbors)
            {
                if (neighbor != null)
                {
                    Gizmos.DrawLine(transform.position, neighbor.transform.position);
                }
            }
        }
    }
}
