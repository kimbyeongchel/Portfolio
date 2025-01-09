using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class chargeReload_arrow : MonoBehaviour
{
    public GameObject [] particle;
    public Dealer archer;

    private void OnEnable()
    {
        particle[0].SetActive(false);
        particle[1].SetActive(false);
    }

    void Update()
    {
        if (archer.s_damage[4] >= 90)
            particle[1].SetActive(true);
        else if (archer.s_damage[4] > 60 && archer.s_damage[4] < 90)
            particle[0].SetActive(true);
    }
}
