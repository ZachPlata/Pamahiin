using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class SafeZone : MonoBehaviour
{
    private void OnTriggerEnter2D(Collider2D collision)
    {
        var player = collision.GetComponent<PlayerController>();
        if (player != null)
        {
            player.SetInSafeZone(true);
        }
    }

    private void OnTriggerExit2D(Collider2D collision)
    {
        var player = collision.GetComponent<PlayerController>();
        if (player != null)
        {
            player.SetInSafeZone(false);
        }
    }

    private void OnDisable()
    {
        // When the LightZone is disabled, any player currently standing inside it will lose safe zone status
        var collider = GetComponent<Collider2D>();
        if (collider != null)
        {
            Collider2D[] overlapping = new Collider2D[10];
            ContactFilter2D filter = new ContactFilter2D();
            filter.NoFilter();
            int count = Physics2D.OverlapCollider(collider, filter, overlapping);
            for (int i = 0; i < count; i++)
            {
                if (overlapping[i] != null)
                {
                    var player = overlapping[i].GetComponent<PlayerController>();
                    if (player != null)
                    {
                        player.SetInSafeZone(false);
                    }
                }
            }
        }
    }
}
