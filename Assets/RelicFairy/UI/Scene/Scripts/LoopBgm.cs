using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class LoopBgm : MonoBehaviour
{
    private AudioSource _audio;

    private void Awake() => _audio = GetComponent<AudioSource>();

    private void OnEnable()
    {
        if (_audio != null && !_audio.isPlaying)
            _audio.Play();
    }

    private void OnDisable() => _audio?.Stop();
}
