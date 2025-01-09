using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "AnimationState")]
public class AnimationState : ScriptableObject
{
    [field : SerializeField] public string triggerName { get; private set; }
    [field: SerializeField] public float impactStartTime { get; private set; }
    [field: SerializeField] public float impactEndTime { get; private set; }
    [field: SerializeField] public float breakAnimTime { get; private set; }
}
