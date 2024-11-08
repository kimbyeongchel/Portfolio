using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;

public class Living : MonoBehaviour
{
    public float startingHealth = 100f;
    public float health { get; protected set; }
    public bool dead { get; protected set; }
    private Animator animator;

    private void Awake()
    {
        animator = GetComponent<Animator>();
    }

    public virtual void OnEnable()
    {
        dead = false;
        health = startingHealth;
    }

    public virtual void TakeDamage(float damage)
    {
        health -= damage;
        Invoke("TriggerHurt", 0.8f);

        if (health <= 0 && !dead)
        {
            Invoke("Die", 1f);
        }
    }

    private void TriggerHurt()
    {
        animator.SetTrigger("isHurt");
    }

    public virtual void Die()
    {
        dead = true;
        animator.SetTrigger("Die");
    }

    public void Attack()
    {
        animator.SetTrigger("Attack");
    }
}