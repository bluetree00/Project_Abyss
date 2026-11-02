using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 화면 글 언어(11-02 Q11 사용자 「글로벌 출시 — 모든 설명과 게임 안 한글 텍스트를 영어로 전환할 수 있게」 · L1 기반).
/// 열쇠 = <b>한글 원문</b> — 코드의 글을 <see cref="T"/> · <see cref="F"/>로 감싸면 표에 영어가 있을 때만 바뀌고, 없으면 한글 그대로(단계 이관 · 화면이 깨지지 않는다).
/// 표 = Addressable TextAsset <c>STRINGS</c>(CSV: ko,en — 따옴표 · 쉼표는 CSV 규칙). 설계 `세션별_남은작업_20261102/Q11_영어_전환_설계.md`.
/// </summary>
public static class Loc
{
    public enum Language { Korean = 0, English = 1 }

    private const string PrefsKey = "RelicFairy.Language";
    private const string TableKey = "STRINGS";

    private static readonly Dictionary<string, string> s_en = new();
    private static bool s_loading;

    /// <summary>언어가 바뀌었다 — 열린 창이 글을 다시 쓴다.</summary>
    public static event Action LanguageChanged;

    private static Language? s_current;

    /// <summary>지금 언어 — 씬이 열리기 전에 정한다(저장값 → 없으면 OS 언어 · 앱 부트에서 스팀 언어로 다시 맞춤).
    /// 생성자 · 필드 초기값에서 읽어도 Unity API를 부르지 않는다(11-02 — MonsterHPBar 역직렬화 중 systemLanguage 금지 예외).</summary>
    public static Language Current => s_current ?? Language.Korean;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void InitLanguage() => s_current ??= (Language)PlayerPrefs.GetInt(PrefsKey, DefaultLanguage());

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        s_en.Clear();
        s_loading = false;
        LanguageChanged = null;
        s_current = null;
    }

    /// <summary>한글 원문 → 지금 언어. 표에 없으면 원문 그대로.</summary>
    public static string T(string ko)
    {
        if (Current == Language.Korean || string.IsNullOrEmpty(ko)) return ko;
        return s_en.TryGetValue(ko, out var en) && !string.IsNullOrEmpty(en) ? en : ko;
    }

    /// <summary>자리표시가 있는 글 — 「보관함 {0}」. 영어 어순은 표 쪽에서.</summary>
    public static string F(string ko, params object[] args) => string.Format(T(ko), args);

    /// <summary>한국어에만 붙는 조각(조사 「을 · 를 · 이 · 가 · 으로」 등) — 영어면 빈 글.</summary>
    public static string K(string ko) => Current == Language.Korean ? ko : string.Empty;

    public static void SetLanguage(Language lang)
    {
        if (lang == Current) return;
        s_current = lang;
        PlayerPrefs.SetInt(PrefsKey, (int)lang);
        PlayerPrefs.Save();
        LanguageChanged?.Invoke();
    }

    /// <summary>앱 부트에서 한 번 — 표를 읽는다(없으면 조용히 한글만).</summary>
    public static async UniTask PreloadAsync()
    {
        if (s_loading || s_en.Count > 0) return;
        s_loading = true;
        try
        {
            var asset = await Managers.AddressableManager.LoadAssetAsync<TextAsset>(TableKey);
            if (asset != null) Parse(asset.text);
            // 스팀은 앱 부트에서 켜진다 — 고른 적이 없으면 스팀 게임 언어로 다시 맞춘다(11-02 「국가 전환」)
            if (!PlayerPrefs.HasKey(PrefsKey))
            {
                var d = (Language)DefaultLanguage();
                if (d != Current) { s_current = d; LanguageChanged?.Invoke(); }
            }
        }
        catch (Exception e) { Debug.Log($"[Loc] 문자열 표 없음 — 한글만({e.Message})"); }
        finally { s_loading = false; }
    }

    /// <summary>처음 실행 언어 — 스팀 게임 언어(「koreana」면 한국어) → 스팀이 없으면 OS 언어. 설정에서 고르면 저장값이 이긴다(11-02 「국가 전환」).</summary>
    private static int DefaultLanguage()
    {
        try
        {
            if (SteamManager.Initialized)
                return Steamworks.SteamApps.GetCurrentGameLanguage() == "koreana" ? 0 : 1;
        }
        catch (Exception e) { Debug.Log($"[Loc] 스팀 언어 못 읽음 — OS 언어로({e.Message})"); }
        return Application.systemLanguage == SystemLanguage.Korean ? 0 : 1;
    }

    private static void Parse(string csv)
    {
        int i = 0, n = csv.Length;
        bool header = true;
        var cells = new List<string>(4);
        while (i < n)
        {
            cells.Clear();
            while (i < n)
            {
                var sb = new System.Text.StringBuilder();
                if (csv[i] == '"')
                {
                    i++;
                    while (i < n && !(csv[i] == '"' && (i + 1 >= n || csv[i + 1] != '"'))) { if (csv[i] == '"') i++; sb.Append(csv[i]); i++; }
                    i++;
                }
                else while (i < n && csv[i] != ',' && csv[i] != '\n' && csv[i] != '\r') sb.Append(csv[i++]);
                cells.Add(sb.ToString());
                if (i < n && csv[i] == ',') { i++; continue; }
                while (i < n && (csv[i] == '\r' || csv[i] == '\n')) i++;
                break;
            }
            if (header) { header = false; continue; }
            if (cells.Count >= 2 && !string.IsNullOrEmpty(cells[0])) s_en[cells[0]] = cells[1];
        }
    }
}
