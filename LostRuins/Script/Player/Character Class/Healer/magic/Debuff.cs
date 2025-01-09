using UnityEngine;
using System.Collections;

public class Debuff : MonoBehaviour // �ƹ��ų� �ε����� ���߹��� �� Ÿ�ٿ��� �����
{
    public ParticleSystem boom; // ����� ���� ���ݹ���
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
