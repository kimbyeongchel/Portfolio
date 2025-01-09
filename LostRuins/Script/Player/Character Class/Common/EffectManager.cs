using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class EffectManager : MonoBehaviour, IEffect
{
    [SerializeField] public ParticleSystem [] particle; // passive, attack, q, e, f ,hit

    public virtual ParticleSystem attackFX()
    {
        return Instantiate(particle[1], Vector3.zero, Quaternion.identity);
    }
    public virtual ParticleSystem eFX()
    {
        return Instantiate(particle[3], Vector3.zero, Quaternion.identity);
    }
    public virtual ParticleSystem fFX()
    {
        return Instantiate(particle[4], Vector3.zero, Quaternion.identity);
    }
    public virtual ParticleSystem hitFX()
    {
        return Instantiate(particle[5], Vector3.zero, Quaternion.identity);
    }
    public virtual ParticleSystem passiveFX()
    {
        return Instantiate(particle[0], Vector3.zero, Quaternion.identity);
    }
    public virtual ParticleSystem qFX()
    {
        return Instantiate(particle[2], Vector3.zero, Quaternion.identity);
    }
}
