using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 보스 VFX 프리팹 오브젝트 풀.
/// - 사운드 겹침 방지: 짧은 시간 내 여러 개 꺼낼 경우 후속 오브젝트 오디오를 자동 음소거.
/// - 파티클 시스템 자동 재시작 (Get) / 강제 정지 (Return).
/// </summary>
public class BKEffectPool
{
    private readonly GameObject          _prefab;
    private readonly Transform           _container;
    private readonly Queue<GameObject>   _idle   = new();
    private readonly HashSet<GameObject> _active = new();

    private float        _lastSoundTime = -999f;
    private const float  SoundThrottle  = 0.3f;   // 같은 풀에서 이 시간 내 재생 시 음소거

    public BKEffectPool(GameObject prefab, int initialSize, Transform container = null)
    {
        _prefab    = prefab;
        _container = container;
        for (int i = 0; i < initialSize; i++)
            Enqueue(CreateNew());
    }

    // ── 풀에서 꺼내기 ─────────────────────────────────────
    public GameObject Get(Vector3 pos, Quaternion rot)
    {
        var go = _idle.Count > 0 ? _idle.Dequeue() : CreateNew();
        go.transform.SetPositionAndRotation(pos, rot);

        // 사운드 겹침 방지
        bool mute = Time.time - _lastSoundTime < SoundThrottle;
        if (!mute) _lastSoundTime = Time.time;
        foreach (var src in go.GetComponentsInChildren<AudioSource>(true))
            src.mute = mute;

        // 파티클 재시작
        foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
        {
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.Play(true);
        }

        go.SetActive(true);
        _active.Add(go);
        return go;
    }

    // ── 풀에 반납 ─────────────────────────────────────────
    public void Return(GameObject go)
    {
        if (go == null || !_active.Remove(go)) return;
        foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        Enqueue(go);
    }

    // ── 전체 강제 반납 ────────────────────────────────────
    public void RecycleAll()
    {
        foreach (var go in _active)
            if (go != null) Enqueue(go);
        _active.Clear();
    }

    // ── 풀 해제 ───────────────────────────────────────────
    public void Dispose()
    {
        _active.Clear();
        while (_idle.Count > 0)
        {
            var go = _idle.Dequeue();
            if (go != null) Object.Destroy(go);
        }
    }

    // ─────────────────────────────────────────────────────
    private GameObject CreateNew()
    {
        var go = Object.Instantiate(_prefab);
        go.name = _prefab.name;
        if (_container != null) go.transform.SetParent(_container, false);
        go.SetActive(false);
        foreach (var src in go.GetComponentsInChildren<AudioSource>(true))
            src.volume *= 0.5f;
        return go;
    }

    private void Enqueue(GameObject go)
    {
        if (go == null) return;
        go.SetActive(false);
        _idle.Enqueue(go);
    }
}
