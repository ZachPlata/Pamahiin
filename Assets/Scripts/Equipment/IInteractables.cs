using UnityEngine;

public interface IInteractable
{
    void Interact();
    string GetInteractText();
    
    // Dragging mechanics
    bool CanDrag();
    void OnDragBegin(ulong clientId);
    void OnDragUpdate(Vector2 targetPos);
    void OnDragEnd(ulong clientId);
}