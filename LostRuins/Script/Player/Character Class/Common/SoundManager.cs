using Photon.Pun;
using UnityEngine;
using UnityEngine.Audio;

public class SoundManager : MonoBehaviourPunCallbacks
{
    [Header("#SFX")]
    public AudioClip[] sfxClips;
    public AudioClip[] skillSfxClips;
    public float sfxVolume;
    public float maxSoundDistance = 3f;
    public float minSoundDistance = 2f;
    public int channels;
    public AudioSource[] sfxPlayers;
    protected int channelIndex;
    [SerializeField] protected AudioMixerGroup manage;
    public GameObject sfxObject;
    public bool notMonsterHit;

    public enum Sfx { walk, jump = 2, roll, hit, die }

    [PunRPC]
    public void Die()
    {
        sfxObject.SetActive(false);
    }

    void Init()
    {
        sfxObject = new GameObject("SfxPlayer");
        sfxObject.transform.parent = transform;
        sfxObject.transform.position = transform.position;
        sfxPlayers = new AudioSource[channels];

        for (int index = 0; index < sfxPlayers.Length; index++)
        {
            sfxPlayers[index] = sfxObject.AddComponent<AudioSource>();
            sfxPlayers[index].playOnAwake = false;
            sfxPlayers[index].maxDistance = maxSoundDistance;
            sfxPlayers[index].minDistance = minSoundDistance;
            sfxPlayers[index].volume = sfxVolume;
            sfxPlayers[index].outputAudioMixerGroup = manage;
        }
    }
    private void Start()
    {
        Init();
    }
    public virtual void PlaySfx(Sfx sfx)
    {
        for (int index = 0; index < sfxPlayers.Length; index++)
        {
            int loopIndex = (index + channelIndex) % sfxPlayers.Length;

            if (sfxPlayers[loopIndex].isPlaying)
                continue;

            int ranIndex = 0;
            if (sfx == Sfx.walk)
                ranIndex = Random.Range(0, 2);

            channelIndex = loopIndex;
            sfxPlayers[loopIndex].clip = sfxClips[(int)sfx + ranIndex];
            sfxPlayers[loopIndex].Play();
            break;
        }
    }
}
