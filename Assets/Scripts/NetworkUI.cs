using Unity.Netcode;
using UnityEngine;

public class NetworkUI : MonoBehaviour
{
    private void OnGUI()
    {
        if (NetworkManager.Singleton == null) return;

        GUILayout.BeginArea(new Rect(10, 10, 300, 300));
        
        if (!NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsServer)
        {
            if (GUILayout.Button("Host Game (Start in Truck)")) NetworkManager.Singleton.StartHost();
            if (GUILayout.Button("Join Game")) NetworkManager.Singleton.StartClient();
        }
        
        GUILayout.EndArea();
    }
}