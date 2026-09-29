using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "RelicFairy/Sound/SoundEventTable", fileName = "SoundEventTable")]
public class SoundEventTableSO : ScriptableObject
{
    [Serializable]
    public struct Entry
    {
        public string eventId;
        public string sfxKey;
        [Range(0f, 1f)] public float volume;
        [Tooltip("재생마다 피치를 1 ± 이 값 안에서 흔든다 — 자주 나는 소리(휘두름·발사)가 기계적으로 들리지 않게")]
        [Range(0f, 0.3f)] public float pitchJitter;
    }

    [SerializeField] private Entry[] _entries = Array.Empty<Entry>();

    private Dictionary<string, Entry> _map;

    private void OnEnable() => BuildMap();

    private void BuildMap()
    {
        _map = new Dictionary<string, Entry>(_entries.Length);
        foreach (var e in _entries)
        {
            if (!string.IsNullOrEmpty(e.eventId))
                _map[e.eventId] = e;
        }
    }

    public bool TryGet(string eventId, out string sfxKey, out float volume, out float pitchJitter)
    {
        if (_map == null) BuildMap();

        if (_map.TryGetValue(eventId, out var entry))
        {
            sfxKey      = entry.sfxKey;
            volume      = entry.volume > 0f ? entry.volume : 1f;
            pitchJitter = entry.pitchJitter;
            return true;
        }

        sfxKey      = null;
        volume      = 1f;
        pitchJitter = 0f;
        return false;
    }
}
