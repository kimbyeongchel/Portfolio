using UnityEngine;

public interface IEffect
{
    ParticleSystem attackFX();
    ParticleSystem passiveFX();
    ParticleSystem qFX();
    ParticleSystem eFX();
    ParticleSystem fFX();
    ParticleSystem hitFX();
}