using System.Collections;
using System.Collections.Generic;
using Photon.Pun;
using Unity.VisualScripting;
using UnityEngine;

public class HitBox: MonoBehaviour
{
    public int damage;

    public virtual void OnTriggerEnter(Collider other)
    {
        if(other.CompareTag("monster"))
        {   
            other.TryGetComponent(out PhotonView pv);
            if (pv != null && PhotonNetwork.IsConnected)
            {
                pv.RPC("TakeDamage",RpcTarget.MasterClient,damage);
            }
        }
        else if(other.CompareTag("BreakObject"))
        {
            Debug.Log("물건 명중!!");
        }
    }
}
