using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "State/MakeState")]
public class State : ScriptableObject
{
    [Header("state")]
    public float atk;
    public float def;
    public float ctr;

    [Header("skillCoolTime // passive, attack, q, e, f")]
    public float[] skillCoolTime;

    [Header("damage // passive, attack, q, e, f order")]
    public float[] s_damage;
}
