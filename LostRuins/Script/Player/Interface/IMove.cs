using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public interface IMove
{
    public float moveAmount { get; set; }
    void Move();
    void GroundCheck();
    void Jump();
    void Dodge();
}
