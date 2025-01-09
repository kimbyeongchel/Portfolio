using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 워리어 사운드 작동 스크립트.
/// attack => 휘두르는 사운드 2개
/// hit -> 2개(monster), 2개(wall), 1개(q), 1개(e), 1개(f)
/// q 발생 사운드 1개, e 발생 사운드 1개, f 발생 사운드 2개 ( 주위 원, 공간 가르기 )
/// </summary>
public class WarriorSound : SoundManager
{
    public int skillNum;

    public enum skillSfx { attack, hitSound = 3, q = 9, e, f=12 }
    public void PlaySkillSfx(skillSfx sfx)
    {
        for (int index = 0; index < sfxPlayers.Length; index++)
        {
            int loopIndex = (index + channelIndex) % sfxPlayers.Length;

            if (sfxPlayers[loopIndex].isPlaying)
                continue;

            int ranIndex = 0;
            if (sfx == skillSfx.attack)
                ranIndex = skillNum;
            else if (sfx == skillSfx.hitSound)
            {
                if (notMonsterHit)
                    ranIndex = Random.Range(3, 6);
                else
                    ranIndex = Random.Range(0, 3);
            }
            else
            {
                ranIndex = 0;
            }

            channelIndex = loopIndex;
            sfxPlayers[loopIndex].clip = skillSfxClips[(int)sfx + ranIndex];
            sfxPlayers[loopIndex].Play();
            break;
        }
    }

    /// <summary>
    /// 스킬 설정에 따른 hit 사운드 설정
    /// </summary>
    /// <param name="skillNum"></param>
    /// <returns></returns>
    public int takeHitSoundIndex(int skillNum)
    {
        int hitIndex;

        switch (skillNum)
        {
            case 0:
            case 1:
            case 2:
                hitIndex = skillNum;
                break;
            default:
                hitIndex = 0;
                break;
        }
        return hitIndex;
    }

    public int skillSoundIndex(int skillNum)
    {
        int hitIndex;

        switch (skillNum)
        {
            case 0:
            case 1:
            case 2:
                hitIndex = skillNum;
                break;
            default:
                hitIndex = 0;
                break;
        }
        return hitIndex;
    }
}

