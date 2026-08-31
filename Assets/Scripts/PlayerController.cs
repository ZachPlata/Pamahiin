using Unity.Netcode;
using UnityEngine;
using Unity.Cinemachine;

public class PlayerController : NetworkBehaviour
{
    public float walkSpeed = 5f;
    private Rigidbody2D rb;
    private Vector2 movement;

    public override void OnNetworkSpawn()
    {
        rb = GetComponent<Rigidbody2D>();

        if (IsOwner)
        {
            CinemachineCamera cam = FindAnyObjectByType<CinemachineCamera>();
            if (cam != null)
            {
                cam.Follow = transform;
            }
        }
    }

    void Update()
    {
        if (!IsOwner) return;

        movement.x = Input.GetAxisRaw("Horizontal");
        movement.y = Input.GetAxisRaw("Vertical");
    }

    void FixedUpdate()
    {
        if (!IsOwner) return;

        rb.linearVelocity = movement.normalized * walkSpeed;
    }
}
