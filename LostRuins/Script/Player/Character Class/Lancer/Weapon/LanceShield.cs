using Photon.Pun;
using UnityEngine;

public class LanceShield : HitBox 
{
    public LancerSound sound;
    protected ParticleSystem hitEffect;
    public LancerEffect effectControl;

    private void Start()
    {
        sound = GetComponentInParent<LancerSound>();
        effectControl = GetComponentInParent<LancerEffect>();
    }
    public override void OnTriggerEnter(Collider other) // f
    {
        if (other.CompareTag("BreakObject"))
        {
            other.GetComponent<BreakObject>().Hit();
            sound.notMonsterHit = false;
        }
        else if (other.CompareTag("monster"))
        {
            other.TryGetComponent(out PhotonView pv);
            if (pv != null && PhotonNetwork.IsConnected)
            {
                pv.RPC("TakeDamage",RpcTarget.MasterClient,damage);
            }
            sound.notMonsterHit = true;
            hitEffect = effectControl.attackFX();
            hitEffect.transform.position = other.transform.position + new Vector3(0, 1, 0);
        }
        sound.PlaySkillSfx(LancerSound.skillSfx.hitSound);
    }

}
