using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 보스 전용 AudioSource 오브젝트 풀.
/// AudioSource.PlayClipAtPoint 대체 — 재생 완료 시 자동 회수.
/// Tick() 을 매 프레임 호출해야 회수가 정상 동작한다.
/// </summary>
public class BKAudioPool
{
    private readonly Transform            _container;
    private readonly Queue<AudioSource>   _idle     = new();
    private readonly HashSet<AudioSource> _active   = new();
    private readonly List<AudioSource>    _toReturn = new();

    public BKAudioPool(int initialSize, Transform container)
    {
        _container = container;
        for (int i = 0; i < initialSize; i++)
            Enqueue(CreateNew());
    }

    // ── 재생 ─────────────────────────────────────────────
    public void Play(Vector3 worldPos, AudioClip clip, float volume = 1f)
    {
        if (clip == null) return;
        var src = _idle.Count > 0 ? _idle.Dequeue() : CreateNew();
        src.transform.position = worldPos;
        src.clip               = clip;
        src.volume             = volume;
        src.gameObject.SetActive(true);
        src.Play();
        _active.Add(src);
    }

    // ── 매 프레임 회수 체크 (BlackKnightBoss.Update 에서 호출) ──
    public void Tick()
    {
        foreach (var src in _active)
            if (src == null || !src.isPlaying) _toReturn.Add(src);

        foreach (var src in _toReturn)
        {
            _active.Remove(src);
            if (src != null) Enqueue(src);
        }
        _toReturn.Clear();
    }

    // ── 전체 즉시 회수 ────────────────────────────────────
    public void RecycleAll()
    {
        foreach (var src in _active)
            if (src != null) Enqueue(src);
        _active.Clear();
    }

    // ── 풀 해제 ───────────────────────────────────────────
    public void Dispose()
    {
        _active.Clear();
        while (_idle.Count > 0)
        {
            var src = _idle.Dequeue();
            if (src != null) Object.Destroy(src.gameObject);
        }
    }

    // ─────────────────────────────────────────────────────
    private AudioSource CreateNew()
    {
        var go = new GameObject("[Audio]");
        go.transform.SetParent(_container, false);
        var src = go.AddComponent<AudioSource>();
        src.playOnAwake  = false;
        src.spatialBlend = 1f;                        // 3D
        src.rolloffMode  = AudioRolloffMode.Logarithmic;
        src.minDistance  = 1f;
        src.maxDistance  = 40f;
        go.SetActive(false);
        return src;
    }

    private void Enqueue(AudioSource src)
    {
        if (src == null) return;
        src.Stop();
        src.clip = null;
        src.gameObject.SetActive(false);
        _idle.Enqueue(src);
    }
}
