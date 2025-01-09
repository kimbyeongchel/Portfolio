using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class healerSound : SoundManager
{
    public enum skillSfx { attack, q, e, f, passive }

    public void PlaySkillSfx(skillSfx sfx)
    {
        for (int index = 0; index < sfxPlayers.Length; index++)
        {
            int loopIndex = (index + channelIndex) % sfxPlayers.Length;

            if (sfxPlayers[loopIndex].isPlaying)
                continue;

            channelIndex = loopIndex;
            sfxPlayers[loopIndex].clip = skillSfxClips[(int)sfx];
            sfxPlayers[loopIndex].Play();
            break;
        }
    }
}
