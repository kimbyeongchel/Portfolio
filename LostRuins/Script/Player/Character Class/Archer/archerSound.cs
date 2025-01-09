using UnityEngine;

public class archerSound : SoundManager
{
    public enum skillSfx { loading, shoot, arrowHit, q, e, f, passive }

    public void PlaySkillSfx(skillSfx sfx)
    {
        for (int index = 0; index < sfxPlayers.Length; index++)
        {
            int loopIndex = (index + channelIndex) % sfxPlayers.Length;

            if (sfxPlayers[loopIndex].isPlaying && sfx != skillSfx.shoot)
                continue;

            channelIndex = loopIndex;
            sfxPlayers[loopIndex].clip = skillSfxClips[(int)sfx];
            sfxPlayers[loopIndex].Play();
            break;
        }
    }
}
