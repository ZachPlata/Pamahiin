using UnityEngine;

public class JournalController : MonoBehaviour
{
    public GameObject journalUI;

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.J))
        {
            if (journalUI != null)
            {
                bool isCurrentlyOpen = journalUI.activeSelf;
                journalUI.SetActive(!isCurrentlyOpen);
            }
        }
    }
}