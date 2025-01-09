using System.Collections;
using System.Collections.Generic;
using Photon.Pun;
using Unity.VisualScripting;
using UnityEngine;

public class arrow : MonoBehaviour
{
    public Rigidbody rid;
    public float damage;
    public ParticleSystem hit;
    public archerSound sound;

    protected void Start()
    {
        Destroy(gameObject, 3f);
    }

    protected void FixedUpdate()
    {
        if (rid.velocity != Vector3.zero)
        {
            Vector3 velocity = rid.velocity;
            transform.rotation = Quaternion.LookRotation(velocity);
        }
    }

    protected void OnTriggerEnter(Collider other)
    {
        if (!other.gameObject.CompareTag("arrow"))
        {
            if (other.gameObject.CompareTag("BreakObject"))
            {
                other.gameObject.GetComponent<BreakObject>().Hit();
                Instantiate(hit, transform.position, Quaternion.identity).Play();
                Destroy(gameObject);
            }
            else if (other.gameObject.CompareTag("monster"))
            {
                other.gameObject.GetComponent<MonsterState>().TakeDamage((int)damage);
                Instantiate(hit, transform.position, Quaternion.identity).Play();
                Destroy(gameObject);
            }
            else
            {
                Instantiate(hit, transform.position, Quaternion.identity).Play();
                Destroy(gameObject);
            }
        }
    }
}