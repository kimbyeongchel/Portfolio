using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ShieldEffect : MonoBehaviour
{
    PlayerState target;
    ParticleSystem thisParticle;

    private void Start()
    {
        target = transform.parent.GetComponent<PlayerState>();
        thisParticle = gameObject.GetComponent<ParticleSystem>();
    }

    void Update()
    {
        if (target.Shield.value == 0)
            thisParticle.Stop();
    }
}
