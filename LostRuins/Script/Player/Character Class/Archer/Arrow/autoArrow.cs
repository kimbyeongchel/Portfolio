using System.Collections;
using Photon.Pun;
using UnityEngine;

public class autoArrow : MonoBehaviour
{
    public ParticleSystem hit;
    [SerializeField] protected LayerMask target;
    [SerializeField] protected Transform SetTarget;
    public Vector3 rotateVector;
    public Rigidbody rid;
    protected bool auto = false;
    protected float currentSpeed = 10f;
    protected int reset = 0;
    public float damage;
    public Vector3 rangeRadius;
    public Transform rangeCenter;

    protected void Start()
    {
        rid.useGravity = false;
        StartCoroutine(shootDelay());
    }
    protected void Update()
    {
        if (auto)
        {
            if (SetTarget.GetComponent<MonsterState>().isDead)
            {
                if (reset >= 1)
                    Destroy(gameObject);
                reset++;
                SearchEnemy();
            }

            rid.useGravity = false;
            Vector3 t_dir = ((SetTarget.position  + new Vector3(0f, 1f, 0f)) - transform.position).normalized;
            transform.forward = Vector3.Slerp(transform.forward, t_dir, 0.1f);
            rid.velocity = transform.forward * currentSpeed;
        }
        transform.rotation = Quaternion.LookRotation(rid.velocity);
    }
    protected void SearchEnemy()
    {
        Collider[] t_collider = Physics.OverlapBox(rangeCenter.position, rangeRadius, transform.rotation, target);

        if (t_collider.Length > 0)
        {
            SetTarget = t_collider[Random.Range(0, t_collider.Length)].transform;
        }
    }
    protected IEnumerator shootDelay()
    {
        yield return new WaitForSeconds(0.5f);
        SearchEnemy();

        if (SetTarget != null)
        {
            rid.AddForce((transform.forward + rotateVector)*5f, ForceMode.Impulse);
            yield return new WaitForSeconds(0.5f);
            auto = true;
        }
        else
        {
            rid.AddForce(transform.forward * 40f, ForceMode.Impulse);
            rid.useGravity = true;
        }
        
        yield return new WaitForSeconds(5f);
        Destroy(gameObject);
    }
    protected void OnTriggerEnter(Collider other)
    {
        if (!other.gameObject.CompareTag("arrow"))
        {
            if (other.gameObject.CompareTag("BreakObject"))
            {
                other.gameObject.GetComponent<BreakObject>().Hit();
                Instantiate(hit, transform.position, Quaternion.identity).Play();
                Destroy(gameObject);
            }
            else if (other.gameObject.CompareTag("monster"))
            {
                other.gameObject.GetComponent<MonsterState>().TakeDamage((int)damage);
                Instantiate(hit, transform.position, Quaternion.identity).Play();
                Destroy(gameObject);
            }
            else
            {
                Instantiate(hit, transform.position, Quaternion.identity).Play();
                Destroy(gameObject);
            }
        }
    }
}