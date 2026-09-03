public interface IInteractable
{
    // What happens when the player presses E
    void Interact();
    
    // Optional: Text to show on the screen (e.g., "Press E to Open Door")
    string GetInteractText(); 
}