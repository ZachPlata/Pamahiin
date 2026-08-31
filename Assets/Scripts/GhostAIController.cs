using UnityEngine;

public class GhostAIController : MonoBehaviour
{
    public float moveSpeed = 2f;
    private Vector2 targetPosition;
    private float timer;
    public float changeDirectionInterval = 3f;

    void Start()
    {
        SetNewRandomDestination();
    }

    void Update()
    {
        transform.position = Vector2.MoveTowards(transform.position, targetPosition, moveSpeed * Time.deltaTime);

        timer += Time.deltaTime;
        if (timer >= changeDirectionInterval)
        {
            SetNewRandomDestination();
            timer = 0f;
        }
    }

    void SetNewRandomDestination()
    {
        float randomX = Random.Range(-5f, 5f);
        float randomY = Random.Range(-5f, 5f);
        targetPosition = new Vector2(transform.position.x + randomX, transform.position.y + randomY);
    }
}