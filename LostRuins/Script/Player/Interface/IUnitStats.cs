using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public interface IUnitStats
{
    public int hp { get; set; }

    public int mp { get; set; }
    public float def {  get; set; }
    public float atk { get; set; }
    public float ctr { get; set; }
}
