using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Burn : MonoBehaviour
{
    public bool isBurning = false;
    [SerializeField] private int damage;

    private void Start()
    {
        StartBurning();
    }
    public void StartBurning()
    {
        isBurning = true;
        StartCoroutine(burnTarget());
    }
    public IEnumerator burnTarget()
    {
        int count = 0;
        while(count < 5)
        {
            Damage();
            yield return new WaitForSeconds(1f);
            count++;
        }
        Off();
    }
    private void Damage()
    {
        transform.parent.GetComponent<MonsterState>().TakeDamage(damage);
    }
    private void Off()
    {
        isBurning = false;
        Destroy(gameObject);
    }
}
