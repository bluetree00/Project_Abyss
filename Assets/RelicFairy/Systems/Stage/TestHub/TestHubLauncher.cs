using UnityEngine;

/// <summary>
/// 테스트 허브 런처 — Scenes/Test/BaseCamp_Test 전용.
///
/// 빌드용 베이스캠프(Scenes/Build/BaseCamp)를 건드리지 않고, 원하는 구조로 곧장 들어가 보기 위한 화면이다.
/// 챕터·시작 지점·로드아웃을 고르면 로드아웃과 <see cref="TestRunRequest"/>를 세우고 해당 챕터 씬을 연다.
/// 챕터 씬의 GameRunBootstrapper는 베이스캠프 게이트를 지난 새 런과 똑같이 받아들인다(MarkNewRunPending).
///
/// 에디터에서 이 씬을 열고 Play. 런이 끝나면 이 씬으로 돌아온다(<see cref="TestRunRequest.TryReturnToHub"/>).
/// 「베이스캠프로」는 빌드용 베이스캠프 씬을 정상 흐름 그대로 연다(테스트 요청·로드아웃 없음 — 거기서 갖춘다).
/// </summary>
public class TestHubLauncher : MonoBehaviour
{
    // ── Constants ─────────────────────────────────────────────────
    private const float PanelWidth  = 560f;
    private const float PanelHeight = 790f;

    // ── Static ────────────────────────────────────────────────────
    private static readonly string[] ChapterLabels = { "Ch1", "Ch2", "Ch3", "Ch4" };
    private static readonly string[] StartLabels   = { "대기방 (정상 흐름)", "보스 대기방 직행" };
#if UNITY_EDITOR
    // 이야기 시기(에디터 테스트 오버라이드 — 저장을 건드리지 않는다). 값 = StoryProgress 오버라이드
    private static readonly string[] EraLabels = { "저장값", "봉인기", "해방기", "엔딩 뒤", "악몽 모드" };
    private static readonly int[]    EraValues = { -1, 0, 1, 3, 2 };
#endif

    // ── Serialized ────────────────────────────────────────────────
    [Header("로드아웃")]
    [Tooltip("플레이어 몸 Addressable 키 — BaseCampBootstrapper.playerBodyKey와 같다.")]
    [SerializeField] private string playerBodyKey = "PlayerCharacter";

    [SerializeField] private RelicClassSO[] relics;

    [Tooltip("주무기(슬롯 0) 후보.")]
    [SerializeField] private MainWeaponSO[] mainWeapons;

    [Tooltip("원거리(슬롯 1) 후보. 고르면 초행 기록(메타)이 남는다.")]
    [SerializeField] private MainWeaponSO[] rangedWeapons;

    // ── Private ───────────────────────────────────────────────────
    private int  _chapter      = 4;
    private bool _bossApproach = true;
    private int  _relicIndex;
    private int  _mainIndex;
    private int  _rangedIndex  = -1;   // -1 = 없음
    private bool _launching;

    private string[] _relicLabels;
    private string[] _mainLabels;
    private string[] _rangedLabels;

    private GUIStyle _title;
    private GUIStyle _header;

    // ── Lifecycle ─────────────────────────────────────────────────
    private void Awake()
    {
        _relicLabels  = BuildLabels(relics, allowNone: false);
        _mainLabels   = BuildLabels(mainWeapons, allowNone: false);
        _rangedLabels = BuildLabels(rangedWeapons, allowNone: true);
    }

    private void OnEnable()
    {
        // 허브로 돌아왔다 = 이전 테스트 런은 끝났다.
        TestRunRequest.Clear();
    }

    private void OnGUI()
    {
        EnsureStyles();

        float scale = Mathf.Max(1f, Screen.height / 900f);
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

        float w = Screen.width / scale;
        float h = Screen.height / scale;
        GUILayout.BeginArea(new Rect((w - PanelWidth) * 0.5f, Mathf.Max(10f, (h - PanelHeight) * 0.5f), PanelWidth, PanelHeight), GUI.skin.box);

        GUILayout.Label("테스트 허브", _title);
        GUILayout.Label("빌드용 베이스캠프를 거치지 않고 원하는 구조로 바로 들어간다.");
        GUILayout.Space(8f);

        GUILayout.Label("챕터", _header);
        _chapter = GUILayout.Toolbar(_chapter - 1, ChapterLabels) + 1;

        GUILayout.Label("시작 지점", _header);
        _bossApproach = GUILayout.Toolbar(_bossApproach ? 1 : 0, StartLabels) == 1;

#if UNITY_EDITOR
        DrawEra();
#endif

        GUILayout.Label("유물", _header);
        _relicIndex = DrawChoice(_relicLabels, _relicIndex, allowNone: false);

        GUILayout.Label("주무기", _header);
        _mainIndex = DrawChoice(_mainLabels, _mainIndex, allowNone: false);

        GUILayout.Label("원거리 (선택)", _header);
        _rangedIndex = DrawChoice(_rangedLabels, _rangedIndex, allowNone: true);

        GUILayout.FlexibleSpace();

        var app = AppBootstrapper.Instance;
        bool ready = app != null && app.IsReady;
        GUI.enabled = ready && !_launching;
        if (GUILayout.Button(!ready ? "초기화 중…" : _launching ? "이동 중…" : "시작", GUILayout.Height(44f)))
            TryLaunch();
        if (GUILayout.Button("베이스캠프로", GUILayout.Height(32f)))
            TryGoToBaseCamp();
        GUI.enabled = true;

        GUILayout.EndArea();
    }

    // ── Public Methods ────────────────────────────────────────────
    /// <summary>챕터를 고른다(1~4). 에디터 메뉴용 — 화면의 챕터 툴바와 같다.</summary>
    public void SelectChapter(int chapter) => _chapter = Mathf.Clamp(chapter, 1, ChapterLabels.Length);

    /// <summary>시작점을 뒤집는다(대기방 ↔ 보스 대기방 직행). 에디터 메뉴용 — 바뀐 값을 돌려준다(true = 보스 직행).</summary>
    public bool ToggleBossApproach() => _bossApproach = !_bossApproach;

    /// <summary>현재 선택으로 시작한다. 화면 버튼과 에디터 메뉴(RelicFairy/Test Run)가 쓴다. 준비 전이면 false.</summary>
    public bool TryLaunch()
    {
        var app = AppBootstrapper.Instance;
        if (_launching || app == null || !app.IsReady) return false;
        Launch(app);
        return true;
    }

    /// <summary>
    /// 베이스캠프(빌드용 허브 씬)로 간다 — 베이스캠프 지형·스테이션 확인용. 테스트 요청은 세우지 않아 이후 흐름은 정상 게임과 같다
    /// (게이트로 나간 런이 끝나면 베이스캠프로 돌아온다). 화면 버튼과 에디터 메뉴가 쓴다. 준비 전이면 false.
    /// </summary>
    public bool TryGoToBaseCamp()
    {
        var app = AppBootstrapper.Instance;
        if (_launching || app == null || !app.IsReady) return false;
        _launching = true;
        app.Loadout.Clear();   // 베이스캠프에서 무형검 각성부터 갖춘다
        app.RequestLoad(Define.Scene.BaseCamp);
        return true;
    }

    // ── Private Methods ───────────────────────────────────────────
    private void Launch(AppBootstrapper app)
    {
        _launching = true;
        var chapter = (ChapterId)_chapter;

        var loadout = app.Loadout;
        loadout.Clear();
        loadout.SetCharacter(null, playerBodyKey);
        loadout.SetRelic(Pick(relics, _relicIndex));
        loadout.SetWeaponSlot0(Pick(mainWeapons, _mainIndex));
        var ranged = Pick(rangedWeapons, _rangedIndex);
        if (ranged != null) loadout.SetWeaponSlot1(ranged);

        // 보스 아레나는 룸풀 그대로 — Ch4 Arena_Boss_Ch4가 천공의 대제단이다(09-17 A안 적용).
        TestRunRequest.Set(chapter, _bossApproach, null, gameObject.scene.path);
#if UNITY_EDITOR
        TestRunOverlay.Ensure();   // 런 중 오른쪽 위 작은 패널 — 보스 페이지 점프 · 시기 확인
        Debug.Log($"[TestHub] 시작 — {chapter} · 시기 {TestRunOverlay.EraLabel()} · {EraBalance.Describe(StoryProgress.Era)}");
#endif

        app.MarkNewRunPending();
        app.RequestLoad(AppBootstrapper.GetSceneForChapter(chapter));
    }

#if UNITY_EDITOR
    /// <summary>이야기 시기 줄 — 고르면 에디터 오버라이드를 바꾼다(저장값은 그대로). 아래 한 줄은 그 시기 보스 · 리치가 싸우는 방식.</summary>
    private void DrawEra()
    {
        GUILayout.Label("이야기 시기 (테스트 — 저장 안 함)", _header);
        int cur = StoryProgress.DebugNightmareOverride;
        int idx = Mathf.Max(0, System.Array.IndexOf(EraValues, cur));
        int sel = GUILayout.Toolbar(idx, EraLabels);
        if (sel != idx)
        {
            UnityEditor.EditorPrefs.SetInt(StoryProgress.DebugOverridePrefsKey, EraValues[sel]);
            StoryProgress.RefreshDebugOverride();
            Debug.Log($"[TestHub] 이야기 시기 — {TestRunOverlay.EraLabel()} · {EraBalance.Describe(StoryProgress.Era)}");
        }
        GUILayout.Label($"{TestRunOverlay.EraLabel()} · {TestRunOverlay.EraRule()}");
    }
#endif

    /// <summary>선택 그리드. 인덱스는 후보 배열 기준(allowNone이면 -1 = 없음).</summary>
    private static int DrawChoice(string[] labels, int index, bool allowNone)
    {
        int offset = allowNone ? 1 : 0;
        if (labels.Length <= offset)
        {
            GUILayout.Label("  후보가 없다 — 인스펙터에서 채운다.");
            return index;
        }

        int selected = Mathf.Clamp(index + offset, 0, labels.Length - 1);
        return GUILayout.SelectionGrid(selected, labels, 3) - offset;
    }

    private static string[] BuildLabels<T>(T[] options, bool allowNone) where T : Object
    {
        int count  = options != null ? options.Length : 0;
        int offset = allowNone ? 1 : 0;
        var labels = new string[count + offset];
        if (allowNone) labels[0] = "없음";
        for (int i = 0; i < count; i++)
            labels[i + offset] = options[i] != null ? options[i].name : "(비어 있음)";
        return labels;
    }

    private static T Pick<T>(T[] options, int index) where T : Object
        => options != null && index >= 0 && index < options.Length ? options[index] : null;

    private void EnsureStyles()
    {
        if (_title != null) return;
        _title  = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold };
        _header = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold };
        _header.margin = new RectOffset(4, 4, 10, 2);
    }
}
