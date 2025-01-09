using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class LancerEffect : EffectManager
{
    public override ParticleSystem qFX()
    {
        return Instantiate(particle[0], transform.position + new Vector3(0, 1f, 0), Quaternion.identity);
    }

    public ParticleSystem shieldEffect()
    {
        return Instantiate(particle[1], Vector3.zero, Quaternion.identity);
    }

    public override ParticleSystem fFX()
    {
        return Instantiate(particle[2] ,transform);
    }

    public ParticleSystem fDashFX()
    {
        return Instantiate(particle[4], transform);
    }

    public override ParticleSystem attackFX()
    {
        return Instantiate(particle[3], Vector3.zero, Quaternion.identity);
    }
}
