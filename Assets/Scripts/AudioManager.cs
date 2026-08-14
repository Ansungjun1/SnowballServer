using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AudioManager : MonoBehaviour
{
    public AudioSource audioSource;
    public AudioClip[] audioClip;
    public AudioSource BGM;
    public AudioClip[] BGMClip;
    int currentBGM;

    private void Update()
    {
        if (!BGM.isPlaying)
        {
            BGM.clip = BGMClip[currentBGM == 1 ? 0 : 1];

            BGM.Play();
        }
    }
    public void AttackAudio()
    {

    }
    public void HitAudio()
    {

    }
    public void ClickAudio()
    {
        
    }
}
