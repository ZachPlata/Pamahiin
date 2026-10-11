using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class SafeZone : MonoBehaviour
{
    private Collider2D col;

    private void Awake()
    {
        col = GetComponent<Collider2D>();
        if (col != null)
        {
            col.isTrigger = true;
        }
    }

    private void OnEnable()
    {
        // When this SafeZone becomes active (e.g. room light switched ON),
        // immediately register any player currently standing inside it.
        CheckOverlappingPlayers();
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        var player = collision.GetComponent<PlayerController>();
        if (player != null)
        {
            player.AddSafeZone(this);
        }
    }

    private void OnTriggerStay2D(Collider2D collision)
    {
        var player = collision.GetComponent<PlayerController>();
        if (player != null)
        {
            player.AddSafeZone(this);
        }
    }

    private void OnTriggerExit2D(Collider2D collision)
    {
        var player = collision.GetComponent<PlayerController>();
        if (player != null)
        {
            player.RemoveSafeZone(this);
        }
    }

    private void OnDisable()
    {
        // When this SafeZone is disabled (e.g. light switch turned OFF or fuse tripped),
        // remove this safe zone from any player standing in it.
        if (col == null) col = GetComponent<Collider2D>();
        if (col != null)
        {
            Collider2D[] overlapping = new Collider2D[10];
            ContactFilter2D filter = ContactFilter2D.noFilter;
            int count = Physics2D.OverlapCollider(col, filter, overlapping);
            for (int i = 0; i < count; i++)
            {
                if (overlapping[i] != null)
                {
                    var player = overlapping[i].GetComponent<PlayerController>();
                    if (player != null)
                    {
                        player.RemoveSafeZone(this);
                    }
                }
            }
        }
    }

    private void CheckOverlappingPlayers()
    {
        if (col == null) col = GetComponent<Collider2D>();
        if (col != null)
        {
            Collider2D[] overlapping = new Collider2D[10];
            ContactFilter2D filter = ContactFilter2D.noFilter;
            int count = Physics2D.OverlapCollider(col, filter, overlapping);
            for (int i = 0; i < count; i++)
            {
                if (overlapping[i] != null)
                {
                    var player = overlapping[i].GetComponent<PlayerController>();
                    if (player != null)
                    {
                        player.AddSafeZone(this);
                    }
                }
            }
        }
    }
}
