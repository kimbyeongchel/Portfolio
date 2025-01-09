using System.Collections;
using UnityEngine;

public class Shield : MonoBehaviour
{
    public GameObject targetPlayer;
    public float currentSpeed;
    public ParticleSystem effect;
    public Lancer lancer;

    private void Start()
    {
        Destroy(gameObject, 2f);
    }

    void Update()
    {
        transform.position = Vector3.MoveTowards(transform.position, targetPlayer.transform.position + new Vector3(0, 1, 0), 10f*Time.deltaTime);
    }
    private void OnParticleCollision(GameObject other)
    {
        if (other.CompareTag("Player") && (other.gameObject == targetPlayer))
        {
            Instantiate(effect, other.transform).transform.position = other.transform.position;
            lancer.callShieldCoroutine(other);
            Destroy(gameObject);
        }
    }
}
