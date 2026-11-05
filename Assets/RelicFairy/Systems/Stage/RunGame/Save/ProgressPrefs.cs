using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// 진행 기록 키-값(인트로 완료 · 사망 · 클리어 수 · 대사 방문 · 거점 첫 연출 · 몬스터 특성 본 것 …) — 11-06 사용자 「클라우드를 스팀에 맞게」.
/// PlayerPrefs는 Windows 레지스트리라 Steam 클라우드(파일)가 옮기지 않아, 다른 PC에서 이어하면 인트로를 다시 보거나 슬롯 기록이 지워졌다.
/// 쓰임은 PlayerPrefs와 같다(Get/Set/HasKey/DeleteKey/Save) — 값은 <see cref="SaveRoot"/> 폴더의 <c>progress_prefs.json</c>(원자적 쓰기 · .bak · .sig).
/// <para>파일에 없는 키는 PlayerPrefs에서 한 번 읽어 옮긴다(업데이트 전 기록 · 무손실). 지울 땐 PlayerPrefs도 지운다(옛 값이 되살아나지 않게).
/// 기기 설정(해상도 · 소리 · 언어 · 그래픽 · 카메라 거리)은 PlayerPrefs 그대로 — PC마다 다르다.</para>
/// Set은 메모리에만 · <see cref="Save"/>(또는 앱 종료)에 파일로 — PlayerPrefs.Save와 같은 자리에서 부르면 된다.
/// </summary>
public static class ProgressPrefs
{
    // ── Constants ─────────────────────────────────────────────
    public const string FileStem = "progress_prefs";
    private const string Tag = "ProgressPrefs";

    [Serializable]
    private sealed class Store
    {
        public List<string> keys = new();
        public List<string> values = new();
    }

    // ── Static ────────────────────────────────────────────────
    private static Dictionary<string, string> s_map;
    private static string s_path;
    private static bool s_dirty;

    // ── Properties ────────────────────────────────────────────
    /// <summary>값 파일 경로(SaveRoot 폴더).</summary>
    public static string FilePath => s_path ??= SaveFileIO.PathFor(FileStem + ".json");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        s_map = null; s_path = null; s_dirty = false;
        Application.quitting -= Save;
        Application.quitting += Save;   // Save를 안 부른 Set도 끌 때 남긴다
    }

    // ── Public Methods ────────────────────────────────────────
    public static int GetInt(string key, int defaultValue = 0)
    {
        var map = Map();
        if (map.TryGetValue(key, out var s))
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : defaultValue;
        if (!PlayerPrefs.HasKey(key)) return defaultValue;
        int legacy = PlayerPrefs.GetInt(key, defaultValue);   // 옛 레지스트리 기록을 한 번 옮긴다
        map[key] = legacy.ToString(CultureInfo.InvariantCulture);
        s_dirty = true;
        return legacy;
    }

    public static string GetString(string key, string defaultValue = "")
    {
        var map = Map();
        if (map.TryGetValue(key, out var s)) return s;
        if (!PlayerPrefs.HasKey(key)) return defaultValue;
        string legacy = PlayerPrefs.GetString(key, defaultValue);
        map[key] = legacy ?? string.Empty;
        s_dirty = true;
        return legacy;
    }

    public static void SetInt(string key, int value) => Set(key, value.ToString(CultureInfo.InvariantCulture));

    public static void SetString(string key, string value) => Set(key, value ?? string.Empty);

    public static bool HasKey(string key) => Map().ContainsKey(key) || PlayerPrefs.HasKey(key);

    /// <summary>지운다 — 파일과 옛 PlayerPrefs 둘 다(옛 값이 다시 옮겨 오지 않게).</summary>
    public static void DeleteKey(string key)
    {
        if (Map().Remove(key)) s_dirty = true;
        if (PlayerPrefs.HasKey(key)) PlayerPrefs.DeleteKey(key);
    }

    /// <summary>바뀐 것이 있으면 파일로 쓴다(원자적 · 실패해도 게임은 계속).</summary>
    public static void Save()
    {
        if (!s_dirty || s_map == null) return;
        var store = new Store();
        foreach (var kv in s_map) { store.keys.Add(kv.Key); store.values.Add(kv.Value); }
        if (SaveFileIO.WriteAtomic(FilePath, JsonUtility.ToJson(store), Tag)) s_dirty = false;
    }

    /// <summary>메모리 값을 버리고 파일에서 다시 읽는다(실측 · 슬롯 도구용 — 저장 안 한 값은 사라진다).</summary>
    public static void Reload()
    {
        s_map = null;
        s_dirty = false;
    }

    // ── Private Methods ───────────────────────────────────────
    private static void Set(string key, string value)
    {
        var map = Map();
        if (map.TryGetValue(key, out var old) && old == value) return;
        map[key] = value;
        s_dirty = true;
    }

    private static Dictionary<string, string> Map()
    {
        if (s_map != null) return s_map;
        s_map = new Dictionary<string, string>();
        string json = SaveFileIO.ReadWithBackup(FilePath, Tag);
        if (string.IsNullOrEmpty(json)) return s_map;
        try
        {
            var store = JsonUtility.FromJson<Store>(json);
            if (store?.keys != null && store.values != null)
                for (int i = 0; i < store.keys.Count && i < store.values.Count; i++)
                    if (!string.IsNullOrEmpty(store.keys[i])) s_map[store.keys[i]] = store.values[i];
        }
        catch (Exception e) { Debug.LogError($"[{Tag}] 읽기 실패 — 빈 값으로 시작(옛 PlayerPrefs 기록은 다시 옮겨 온다): {e.Message}"); }
        return s_map;
    }
}
