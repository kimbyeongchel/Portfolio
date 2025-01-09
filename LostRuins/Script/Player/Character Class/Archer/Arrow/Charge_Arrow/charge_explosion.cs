using System.Collections;
using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;

public class charge_explosion : MonoBehaviour
{
    public float range;
    [SerializeField] ParticleSystem burnFire;
    ParticleSystem Fire;
    protected Collider[] targetColliders;

    void Start()
    {
        targetColliders = Physics.OverlapSphere(transform.position, range, LayerMask.GetMask("monster"));
        for(int i = 0; i<targetColliders.Length; i++)
        {
            targetColliders[i].GetComponent<MonsterState>().TakeDamage(40);
            Fire = Instantiate(burnFire, targetColliders[i].transform);
            Fire.transform.position = targetColliders[i].transform.position;
            Fire.Play();
        }
    }
}
