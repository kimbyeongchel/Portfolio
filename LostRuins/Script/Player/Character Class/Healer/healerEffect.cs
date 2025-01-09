using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class healerEffect : EffectManager
{
    public Healer healer;

    public override ParticleSystem attackFX()
    {
        return Instantiate(particle[1], healer.magicPosition[0].position, Quaternion.identity);
    }

    public override ParticleSystem eFX()
    {
        return Instantiate(particle[3], healer.magicPosition[1].position, Quaternion.identity);
    }

    public ParticleSystem eBuffFX() // DebuffEffect
    {
        return Instantiate(particle[4], Vector3.zero, Quaternion.identity);
    }

    public ParticleSystem fBuffFX() // StateUpBuff
    {
        return Instantiate(particle[5], Vector3.zero, Quaternion.identity);
    }
}