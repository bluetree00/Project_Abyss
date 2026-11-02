using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 11-02 Q11 사용자 「영문에 한글이 나오지 않게 — 텍스트가 있는 곳 다 점검 · 검증」 — 영어 화면의 안전망 겸 점검기.
/// ① 모든 TMP 글에 전처리기(<see cref="ITextPreprocessor"/>)를 붙여 <b>그릴 때만</b> 지금 언어로 바꾼다(원문 .text는 그대로 — 한국어로 돌아가면 원문).
///    글 전체가 표 열쇠면 통째로, 아니면 리치 태그 · 줄 · 「 · 」로 나눈 조각마다 표 열쇠면 그 조각만.
///    프리팹 · 씬에 박힌 글 · 코드가 Loc로 감싸지 않은 고정 글을 잡는다. 값이 끼는 조립 글은 원천에서 Loc.F여야 한다.
/// ② 바꾼 뒤에도 한글이 남은 글은 (편집기 · 개발 빌드) 오브젝트 경로 · 글을 모아 loc_hangul_seen.txt에 남긴다 — 영어 순회 실측의 점검표.
/// 붙이는 때: 켜질 때 · 씬이 열릴 때 · 언어가 바뀔 때 전부(꺼진 것 포함) · 팝업이 만들어질 때(UIManager) · 그 밖에 처음 그려지는 글(TMP 글 바뀜 알림 → 다음 프레임 LateUpdate에 붙이고 다시 그림).
/// </summary>
public sealed class LocTextGuard : MonoBehaviour, ITextPreprocessor
{
    // ── Constants ────────────────────────────────────────────
    private const string SeenFile  = "loc_hangul_seen.txt";
    private const int    CacheMax  = 4096;

    // ── Static ───────────────────────────────────────────────
    private static LocTextGuard s_instance;
    private static readonly Regex PieceRx = new(@"(<[^>]*>|\n| · )", RegexOptions.Compiled);

    // ── Private ──────────────────────────────────────────────
    private readonly List<TMP_Text>           _pending    = new(64);
    private readonly HashSet<TMP_Text>        _pendingSet = new();
    private readonly Dictionary<string, string> _cache    = new(512);   // 원문 → 그릴 글(영어일 때 · 언어가 바뀌면 비운다)
    private readonly HashSet<string>          _seen       = new();
    private readonly List<TMP_Text>           _fitPending = new(32);
    private readonly HashSet<TMP_Text>        _fitSet     = new();   // 이번 프레임 맞춤 대기(겹침 막기)
    private readonly Dictionary<TMP_Text, float> _fitted  = new();   // 줄이기로 돌린 글 → 원래 글자 크기
    private TextMeshProUGUI _measurer;                                  // 한국어 원문 폭을 재는 숨은 글(캔버스 밖 — 그려지지 않는다)
    private bool   _record;
    private string _seenPath;

    // ── Properties ───────────────────────────────────────────
    /// <summary>영어 화면에서 바꾸지 못한 한글 글(「경로 | 글」) — 실측이 읽는다.</summary>
    public static IReadOnlyCollection<string> Seen => s_instance != null ? s_instance._seen : (IReadOnlyCollection<string>)System.Array.Empty<string>();

    // ── Lifecycle ────────────────────────────────────────────
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => s_instance = null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (s_instance != null) return;
        var go = new GameObject("@LocTextGuard");
        DontDestroyOnLoad(go);
        s_instance = go.AddComponent<LocTextGuard>();
    }

    private void Awake()
    {
        _record   = Application.isEditor || Debug.isDebugBuild;
        _seenPath = Path.Combine(Application.isEditor ? "Temp" : Application.persistentDataPath, SeenFile);
        var m = new GameObject("@LocMeasure", typeof(RectTransform));
        m.transform.SetParent(transform, false);
        _measurer = m.AddComponent<TextMeshProUGUI>();
    }

    private void OnEnable()
    {
        TMPro_EventManager.TEXT_CHANGED_EVENT.Add(OnTextChanged);
        Loc.LanguageChanged    += OnLanguageChanged;
        SceneManager.sceneLoaded += OnSceneLoaded;
        AttachAll(false);
    }

    private void LateUpdate()
    {
        if (_fitPending.Count > 0)
        {
            for (int i = 0; i < _fitPending.Count; i++) Fit(_fitPending[i]);
            _fitPending.Clear();
            _fitSet.Clear();
        }
        if (_pending.Count == 0) return;
        for (int i = 0; i < _pending.Count; i++)
        {
            var t = _pending[i];
            if (t == null || t.textPreprocessor != null) continue;
            t.textPreprocessor = this;
            t.SetAllDirty();
        }
        _pending.Clear();
        _pendingSet.Clear();
    }

    private void OnDisable()
    {
        TMPro_EventManager.TEXT_CHANGED_EVENT.Remove(OnTextChanged);
        Loc.LanguageChanged    -= OnLanguageChanged;
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    // ── Public Methods ───────────────────────────────────────
    /// <summary>새로 만든 UI 묶음(팝업 등)의 글에 바로 붙인다 — 첫 프레임부터 지금 언어로.</summary>
    public static void AttachTree(GameObject root)
    {
        if (s_instance == null || root == null) return;
        foreach (var t in root.GetComponentsInChildren<TMP_Text>(true))
            if (t.textPreprocessor == null) t.textPreprocessor = s_instance;
    }

    /// <summary>점검표를 비운다(실측 시작 때).</summary>
    public static void ClearSeen()
    {
        if (s_instance == null) return;
        s_instance._seen.Clear();
        try { if (File.Exists(s_instance._seenPath)) File.Delete(s_instance._seenPath); }
        catch (IOException) { }
    }

    /// <summary>그릴 글 — 영어면 통째로 · 조각마다 표에서 바꾼다. 한국어면 원문 그대로.</summary>
    public string PreprocessText(string text)
    {
        if (Loc.Current == Loc.Language.Korean || string.IsNullOrEmpty(text) || !HasHangul(text)) return text;
        if (_cache.TryGetValue(text, out var hit)) return hit;

        string whole  = Loc.T(text);
        string result = whole != text ? whole : TranslatePieces(text);
        if (_cache.Count >= CacheMax) _cache.Clear();
        _cache[text] = result;
        return result;
    }

    public static bool HasHangul(string s)
    {
        if (s == null) return false;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if ((c >= '가' && c <= '힣') || (c >= 'ㄱ' && c <= 'ㆎ')) return true;
        }
        return false;
    }

    // ── Private Methods ──────────────────────────────────────
    private static string TranslatePieces(string text)
    {
        var parts   = PieceRx.Split(text);
        bool changed = false;
        for (int i = 0; i < parts.Length; i++)
        {
            string p = parts[i];
            if (p.Length == 0 || p[0] == '<' || !HasHangul(p)) continue;
            string core = p.Trim();
            string en   = Loc.T(core);
            if (en == core)
            {
                // 앞뒤의 기호 · 숫자를 떼고 가운데 한글 낱말만(「▲ 불」 · 「전체 22」 · 「… — 랜슬롯」)
                int a = FirstHangul(core), b = LastHangul(core);
                string mid = core.Substring(a, b - a + 1);
                string midEn = Loc.T(mid);
                if (midEn == mid) continue;
                en = core.Substring(0, a) + midEn + core.Substring(b + 1);
            }
            int lead = p.IndexOf(core, System.StringComparison.Ordinal);
            parts[i] = p.Substring(0, lead) + en + p.Substring(lead + core.Length);
            changed = true;
        }
        return changed ? string.Concat(parts) : text;
    }

    private static int FirstHangul(string s)
    {
        for (int i = 0; i < s.Length; i++) if (HasHangul(s[i].ToString())) return i;
        return 0;
    }

    private static int LastHangul(string s)
    {
        for (int i = s.Length - 1; i >= 0; i--) if (HasHangul(s[i].ToString())) return i;
        return s.Length - 1;
    }

    private void AttachAll(bool redraw)
    {
        foreach (var t in FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (t.textPreprocessor == null) t.textPreprocessor = this;
            else if (!ReferenceEquals(t.textPreprocessor, this)) continue;   // 다른 전처리기는 건드리지 않는다
            if (redraw) t.SetAllDirty();
        }
    }

    private void Record(TMP_Text t)
    {
        string shown = PreprocessText(t.text);
        if (HasHangul(shown)) Write(PathOf(t.transform) + " | " + shown.Replace("\n", "\\n"));
    }

    /// <summary>
    /// 영어로 바꾼 글이 칸을 넘고 한국어 원문은 들어가면 그 글만 크기 자동 맞춤(최소 60%)으로 — 원래부터 넘치던 글(「—」 등)은 건드리지 않는다.
    /// 줄바꿈 없는 글은 가로 폭, 줄바꿈 글은 높이로 본다. 원문 폭은 숨은 측정용 글로 잰다(실제 글의 내부 상태를 바꾸지 않게).
    /// </summary>
    private void Fit(TMP_Text t)
    {
        if (t == null || !t.isActiveAndEnabled || t.enableAutoSizing || _fitted.ContainsKey(t)) return;
        var rt = t.rectTransform;
        float w = rt.rect.width, h = rt.rect.height;
        if (w < 2f || h < 2f) return;
        bool wrap = t.textWrappingMode != TextWrappingModes.NoWrap;
        if (!(wrap ? t.preferredHeight > h + 2f : t.preferredWidth > w + 2f)) return;

        _measurer.font               = t.font;
        _measurer.fontSize           = t.fontSize;
        _measurer.fontStyle          = t.fontStyle;
        _measurer.characterSpacing   = t.characterSpacing;
        _measurer.wordSpacing        = t.wordSpacing;
        _measurer.lineSpacing        = t.lineSpacing;
        _measurer.margin             = t.margin;
        _measurer.richText           = t.richText;
        _measurer.textWrappingMode   = t.textWrappingMode;
        Vector2 ko = wrap ? _measurer.GetPreferredValues(t.text, w, 0f) : _measurer.GetPreferredValues(t.text);
        if (wrap ? ko.y > h + 2f : ko.x > w + 2f) return;   // 한국어도 넘친다 — 원래 모양

        _fitted[t] = t.fontSize;
        t.fontSizeMax = t.fontSize;
        t.fontSizeMin = Mathf.Max(8f, t.fontSize * 0.6f);
        t.enableAutoSizing = true;
        t.SetAllDirty();
        if (_record) Write("맞춤 " + PathOf(t.transform) + " | " + PreprocessText(t.text).Replace("\n", "\\n"));
    }

    private void Unfit()
    {
        foreach (var kv in _fitted)
        {
            if (kv.Key == null) continue;
            kv.Key.enableAutoSizing = false;
            kv.Key.fontSize = kv.Value;
        }
        _fitted.Clear();
        _fitSet.Clear();
    }

    private void Write(string line)
    {
        if (!_seen.Add(line)) return;
        try { File.AppendAllText(_seenPath, line + "\n"); }
        catch (IOException) { }
    }

    private static string PathOf(Transform tr)
    {
        string path = tr.name;
        for (var p = tr.parent; p != null; p = p.parent) path = p.name + "/" + path;
        return tr.gameObject.scene.name + ":" + path;
    }

    // ── Event Handlers ───────────────────────────────────────
    private void OnTextChanged(Object obj)
    {
        if (obj is not TMP_Text t || Loc.Current == Loc.Language.Korean) return;
        if (t.textPreprocessor == null)
        {
            if (_pendingSet.Add(t)) _pending.Add(t);   // 다음 프레임 LateUpdate에 붙이고 다시 그린다
            return;
        }
        if (!ReferenceEquals(t.textPreprocessor, this) || ReferenceEquals(t, _measurer)) return;
        if (_record) Record(t);
        if (t is TextMeshProUGUI && !t.enableAutoSizing && PreprocessText(t.text) != t.text && _fitSet.Add(t))
            _fitPending.Add(t);   // 영어로 바뀐 글만 — 다음 LateUpdate에 칸 맞춤을 본다
    }

    private void OnLanguageChanged()
    {
        _cache.Clear();
        Unfit();   // 한국어든 영어든 원래 크기에서 다시 본다
        AttachAll(true);
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => AttachAll(false);
}
