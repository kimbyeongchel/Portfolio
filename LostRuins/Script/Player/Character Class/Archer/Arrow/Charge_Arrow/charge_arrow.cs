using System.Collections;
using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;

public class charge_arrow : MonoBehaviour
{
    public GameObject startEffect;
    public ParticleSystem boom;
    public float damage;

    void Start()
    {
        Destroy(gameObject, 3f);
        startEffect.SetActive(true);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.gameObject.CompareTag("arrow"))
        {
            if (other.gameObject.CompareTag("BreakObject"))
            {
                other.gameObject.GetComponent<BreakObject>().Hit();
                Instantiate(boom, transform.position, Quaternion.identity).Play();
                Destroy(gameObject);
            }
            else if (other.gameObject.CompareTag("monster"))
            {
                other.gameObject.GetComponent<MonsterState>().TakeDamage((int)damage);
                Instantiate(boom, transform.position, Quaternion.identity).Play();
                Destroy(gameObject);
            }
            else
            {
                Instantiate(boom, transform.position, Quaternion.identity).Play();
                Destroy(gameObject);
            }
        }
    }
}
