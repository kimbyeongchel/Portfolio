using UnityEngine;
using System.Collections;

public class Debuff : MonoBehaviour // 아무거나 부딪히면 폭발범위 내 타겟에게 디버프
{
    public ParticleSystem boom; // 디버프 입을 공격범위
    private void Start()
    {
        Invoke("playBoomAndDeleteObject", 2f);
    }
    private void OnParticleCollision(GameObject other)
    {
        if (!other.CompareTag("Player"))
        {
            playBoomAndDeleteObject();
        }
    }
    void playBoomAndDeleteObject()
    {
        ParticleSystem boomRange = Instantiate(boom, Vector3.zero, Quaternion.identity);
        boomRange.transform.position = transform.position;
        Destroy(gameObject);
    }
}
