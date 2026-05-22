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

    public bool TryGet(string eventId, out string sfxKey, out float volume)
    {
        if (_map == null) BuildMap();

        if (_map.TryGetValue(eventId, out var entry))
        {
            sfxKey = entry.sfxKey;
            volume = entry.volume > 0f ? entry.volume : 1f;
            return true;
        }

        sfxKey = null;
        volume = 1f;
        return false;
    }
}
