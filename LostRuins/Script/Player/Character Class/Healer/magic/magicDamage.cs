using System.Collections;
using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;

public class magicDamage : MonoBehaviour
{
    public int damage;
    public LayerMask enemyLayer;
    [Range(1f, 5f)] public float splashRadius;

    private List<ParticleCollisionEvent> collisionEvents = new List<ParticleCollisionEvent>();
    private ParticleSystem pSystem;

    private void Start()
    {
        pSystem = GetComponent<ParticleSystem>();
    }

    protected void SplashDamage(Vector3 damagedObject)
    {
        Collider[] colliders = Physics.OverlapSphere(damagedObject, splashRadius, enemyLayer);
        foreach (Collider collider in colliders)
        {
            collider.GetComponent<MonsterState>().TakeDamage(damage / 3);
        }
    }

    private void OnParticleCollision(GameObject other)
    {
        collisionEvents.Clear();

        int eventCount = pSystem.GetCollisionEvents(other, collisionEvents);

        Vector3 collisionPoint = collisionEvents[0].intersection;
        SplashDamage(collisionPoint);

        if (other.CompareTag("monster"))
        {
            other.GetComponent<Rigidbody>().AddForce((transform.forward + transform.up) * 1f, ForceMode.Impulse);
            other.GetComponent<MonsterState>().TakeDamage(damage);
        }
        else if (other.gameObject.CompareTag("BreakObject"))
        {
            other.gameObject.GetComponent<BreakObject>().Hit();
        }
    }
}
