using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class LancerSound : SoundManager
{
    public enum skillSfx { attack, hitSound = 3, q=6, f, rightAttack };

    public void PlaySkillSfx(skillSfx sfx)
    {
        for (int index = 0; index < sfxPlayers.Length; index++)
        {
            int loopIndex = (index + channelIndex) % sfxPlayers.Length;

            if (sfxPlayers[loopIndex].isPlaying)
                continue;

            int ranIndex = 0;
            if (sfx == skillSfx.attack)
                ranIndex = Random.Range(0, 3);
            else if (sfx == skillSfx.hitSound)
            {
                if (notMonsterHit)
                    ranIndex = Random.Range(0, 2);
                else
                    ranIndex = 2;
            }

            channelIndex = loopIndex;
            sfxPlayers[loopIndex].clip = skillSfxClips[(int)sfx + ranIndex];
            sfxPlayers[loopIndex].Play();
            break;
        }
    }
}
