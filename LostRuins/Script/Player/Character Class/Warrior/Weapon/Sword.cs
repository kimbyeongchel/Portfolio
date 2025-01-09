using Photon.Pun;
using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

public class Sword : HitBox
{
    public WarriorEffect effectControl;
    public WarriorSound sound;
    protected ParticleSystem hitEffect;

    private void Start()
    {
        effectControl = GetComponentInParent<WarriorEffect>();
        sound = GetComponentInParent<WarriorSound>();
    }

    public override void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("BreakObject"))
        {
            other.GetComponent<BreakObject>().Hit();
            sound.notMonsterHit = true;
        }
        else if (other.CompareTag("monster"))
        {
            other.TryGetComponent(out PhotonView pv);
            if (pv != null && PhotonNetwork.IsConnected)
            {
                pv.RPC("TakeDamage", RpcTarget.MasterClient, damage);
            }
            sound.notMonsterHit = false;
            hitEffect = effectControl.attackFX();
            hitEffect.transform.position = other.transform.position + new Vector3(0, Random.Range(1f, 1.5f), 0);
        }
        
        sound.PlaySkillSfx(WarriorSound.skillSfx.hitSound);
    }
}
