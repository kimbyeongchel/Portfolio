using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class arrowRain : MonoBehaviour
{
    public Transform pos;
    public Vector2 boxsize;

    void DestroyArrow()
    {
        Destroy(gameObject);
    }

    void FindAnd()
    {
        Collider2D[] collider2Ds = Physics2D.OverlapBoxAll(pos.position, boxsize, 0);
        foreach (Collider2D collider in collider2Ds)
        {
            if (collider.tag == "Player")
            {
                PlayerHit player = GameObject.FindGameObjectWithTag("Player").GetComponent<PlayerHit>();
                player.Hit(30, this.gameObject);
            }
        }
    }
}
