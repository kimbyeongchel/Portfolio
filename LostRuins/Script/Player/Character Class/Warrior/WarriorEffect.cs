using Cinemachine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class WarriorEffect : EffectManager
{
    public Transform swordEdgePoint;
    public Transform[] lightningPosition;
    public GameObject sword;

    public GameObject[] makeLightningSword()
    {
        GameObject[] lightningSwords = new GameObject[4];

        for (int i = 0; i < lightningPosition.Length; i++)
        {
            lightningSwords[i] = Instantiate(sword, lightningPosition[i].position, Quaternion.LookRotation(Camera.main.transform.forward));
        }
        return lightningSwords;
    }

    public override ParticleSystem eFX()
    {
        return Instantiate(particle[2], Vector3.zero, Quaternion.identity);
    }

    public void makePlayerFX(ParticleSystem particle, Transform spawnPosition)
    {
        var effect = particle;
        effect.transform.position = spawnPosition.position;
    }

    public override ParticleSystem fFX()
    {
        ParticleSystem fFx = Instantiate(particle[3], transform.position + new Vector3(0, 1, 0), Quaternion.identity);
        fFx.transform.parent = transform;
        fFx.transform.rotation = Quaternion.LookRotation(transform.forward);
        return fFx;
    }

    public void AfterUseF()
    {
        Instantiate(particle[5], transform.position, Quaternion.identity);
    }

    public ParticleSystem fChargeVFX()
    {
        ParticleSystem draw = Instantiate(particle[4], transform);
        return draw;
    }

    public ParticleSystem fSlashHitVFX()
    {
        return Instantiate(particle[1], Vector3.zero, Quaternion.identity);
    }

    public ParticleSystem ELightHitVFX()
    {
        return Instantiate(particle[6], Vector3.zero, Quaternion.identity);
    }

    public override ParticleSystem attackFX()
    {
        return Instantiate(particle[0], Vector3.zero, Quaternion.identity);
    }
}
