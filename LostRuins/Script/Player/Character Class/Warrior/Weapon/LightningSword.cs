using UnityEngine;
using DG.Tweening;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.PlayerLoop;
using Photon.Pun;

public class LightningSword : HitBox
{
    public ParticleSystem hitParticle;
    public Vector3 rangeRadius;
    public Transform rangeCenter;
    public LayerMask target;
    public Transform setTarget;
    public Vector3 moveDir;
    public bool acessMove = false;
    protected AudioSource audioControl;
    public float moveSpeed;
    Rigidbody rigid;

    private void Start()
    {
        rigid = GetComponent<Rigidbody>();
        audioControl = GetComponent<AudioSource>();

        SearchEnemy();
        acessMove = false;
        if (setTarget == null)
            Destroy(gameObject, 1f);
        else
            StartCoroutine(moveToTarget());
    }

    void Update()
    {
        if (acessMove)
        {
            rigid.velocity = transform.forward * moveSpeed * Time.deltaTime;
        }
    }

    IEnumerator moveToTarget()
    {
        float timer = 0f;

        while (timer < 1f)
        {
            moveDir = (setTarget.position - transform.position + new Vector3(0, 0.9f, 0)).normalized;
            transform.rotation = Quaternion.LookRotation(moveDir);
            timer += Time.deltaTime;
            yield return null;
        }

        yield return null;

        acessMove = true;
    }

    protected void SearchEnemy()
    {
        Collider[] t_collider = Physics.OverlapBox(rangeCenter.position, rangeRadius, transform.rotation, target);

        if (t_collider.Length > 0)
        {
            setTarget = t_collider[Random.Range(0, t_collider.Length)].transform;
        }
    }

    public override void OnTriggerEnter(Collider other)
    {
        if (other.gameObject.CompareTag("BreakObject"))
        {
            other.gameObject.GetComponent<BreakObject>().Hit();
        }
        else if (other.gameObject.CompareTag("monster"))
        {
            other.TryGetComponent(out PhotonView pv);
            if (pv != null && PhotonNetwork.IsConnected)
            {
                pv.RPC("TakeDamage", RpcTarget.MasterClient, damage);
            }
        }

        var particle = Instantiate(hitParticle);
        particle.transform.position = other.ClosestPoint(transform.position);
        audioControl.Play();
        Destroy(gameObject, audioControl.clip.length);
    }
}
