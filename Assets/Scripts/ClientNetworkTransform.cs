using Unity.Netcode.Components;
using UnityEngine;

[DisallowMultipleComponent]
public class ClientNetworkTransform : NetworkTransform
{
    // This tells Unity Netcode: "Trust the client's position and rotation for this object"
    protected override bool OnIsServerAuthoritative()
    {
        return false;
    }
}