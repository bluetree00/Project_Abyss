using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// 그래픽 품질 사용자 설정의 <b>단일 소유자</b>. 저장은 PlayerPrefs — 볼륨·해상도와 같은 저장소다.
///
/// <para><b>축은 두 개다.</b> 프리셋은 <see cref="QualitySettings.SetQualityLevel(int, bool)"/>로
/// 프로젝트에 이미 있는 URP 티어 에셋(URP-Performant / URP-Balanced / URP-HighFidelity)을 통째로
/// 갈아끼운다 — 캐스케이드 수·그림자맵 해상도·MSAA처럼 런타임에 못 바꾸는 값이 여기 실린다.
/// 개별 노브(렌더 스케일·AA·그림자 거리·포스트프로세싱·VSync/FPS)는 <b>스왑 뒤에 덧씌운다</b>.
/// 순서가 뒤집히면 SetQualityLevel이 vSyncCount까지 티어 값으로 되돌려 사용자 선택이 증발한다.</para>
///
/// <para>복원 훅이 <see cref="RuntimeInitializeLoadType.AfterSceneLoad"/>인 이유는
/// <see cref="ScreenSettings"/>와 같다 — AppBootstrapper의 부팅 기본값보다 확실히 나중이어야 한다.
/// 카메라와 Volume은 씬마다 새로 생기므로 <see cref="SceneManager.sceneLoaded"/>에서 한 번 더 얹는다.</para>
///
/// <para><b>에디터 오염 방지</b>: 렌더 스케일·그림자 거리는 URP 에셋(ScriptableObject) 필드라
/// 플레이 모드에서 건드리면 에디터에선 에셋 파일이 더티가 된다. 최초 변경 직전 값을 기억해 두고
/// 플레이 종료 시 되돌린다. 빌드에선 역직렬화된 사본이라 애초에 파일로 새지 않는다.</para>
/// </summary>
public static class GraphicsQualitySettings
{
    // ── Constants ────────────────────────────────────────────
    private const string kLevelKey       = "gfx_level";
    private const string kCustomKey      = "gfx_custom";
    private const string kRenderScaleKey = "gfx_render_scale";
    private const string kAaKey          = "gfx_aa";
    private const string kShadowKey      = "gfx_shadow";
    private const string kPostKey        = "gfx_post";
    private const string kVSyncKey       = "gfx_vsync";
    private const string kFpsKey         = "gfx_fps";

    /// <summary>프리셋 드롭다운에서 「사용자 지정」이 앉는 자리. 0~2는 QualitySettings 레벨과 1:1이다.</summary>
    public const int PresetCustom = 3;

    public const float MinRenderScale = 0.5f;
    public const float MaxRenderScale = 1.5f;

    // ── Static ───────────────────────────────────────────────
    public static readonly string[] PresetNames = { "성능 우선", "균형", "고품질", "사용자 지정" };
    public static readonly string[] AaNames     = { "끄기", "FXAA", "SMAA" };
    public static readonly string[] ShadowNames = { "끄기", "낮음", "보통", "높음" };
    public static readonly string[] PostNames   = { "끄기", "낮음", "높음" };
    public static readonly string[] FpsNames    = { "무제한", "30", "60", "120", "144", "240" };
    public static readonly int[]    FpsOptions  = { 0, 30, 60, 120, 144, 240 };

    /// <summary>그림자 품질 → 티어 에셋 기본 shadowDistance에 곱할 비율. 0이면 그림자가 사라진다.</summary>
    private static readonly float[] ShadowDistanceScale = { 0f, 0.4f, 0.7f, 1f };

    /// <summary>포스트프로세싱 강도 → 전역 Volume weight 배율. 「끄기」는 카메라 쪽에서 통째로 막는다.</summary>
    private static readonly float[] PostWeightScale = { 0f, 0.55f, 1f };

    /// <summary>URP 에셋을 처음 건드리기 직전의 값. 에디터 플레이 종료 시 이걸로 되돌린다.</summary>
    private static readonly Dictionary<UniversalRenderPipelineAsset, Vector2> _pristineAsset = new();

    /// <summary>씬에 저작된 renderPostProcessing 값. 「높음」은 이 값을 그대로 존중한다 —
    /// 포스트프로세싱을 쓰지 않도록 만들어 둔 카메라(UI 등)를 우리가 켜 버리면 안 된다.</summary>
    private static readonly Dictionary<UniversalAdditionalCameraData, bool> _authoredPost = new();

    /// <summary>씬에 저작된 Volume weight. 강도 배율은 이 값에 곱한다.</summary>
    private static readonly Dictionary<Volume, float> _authoredWeight = new();

    // ── Private ──────────────────────────────────────────────
    private static bool  _loaded;
    private static int   _level = 2;
    private static bool  _custom;
    private static float _renderScale = 1f;
    private static int   _aa     = 2;
    private static int   _shadow = 3;
    private static int   _post   = 2;
    private static bool  _vsync  = true;
    private static int   _fps;

    // ── Properties ───────────────────────────────────────────

    /// <summary>사용자가 한 번이라도 그래픽 설정을 저장했는지. false면 프로젝트 기본값을 그대로 둔다.</summary>
    public static bool HasSaved => PlayerPrefs.HasKey(kLevelKey);

    /// <summary>드롭다운에 표시할 프리셋 인덱스. 개별 노브를 만진 뒤엔 <see cref="PresetCustom"/>이다.</summary>
    public static int PresetIndex { get { EnsureLoaded(); return _custom ? PresetCustom : _level; } }

    public static float RenderScale    { get { EnsureLoaded(); return _renderScale; } }
    public static int   Antialiasing   { get { EnsureLoaded(); return _aa; } }
    public static int   Shadows        { get { EnsureLoaded(); return _shadow; } }
    public static int   PostProcessing { get { EnsureLoaded(); return _post; } }
    public static bool  VSync          { get { EnsureLoaded(); return _vsync; } }

    /// <summary><see cref="FpsOptions"/> 안의 위치. 저장값이 목록에 없으면 「무제한」으로 떨어진다.</summary>
    public static int FrameRateIndex
    {
        get
        {
            EnsureLoaded();
            for (int i = 0; i < FpsOptions.Length; i++)
                if (FpsOptions[i] == _fps) return i;
            return 0;
        }
    }

    /// <summary>
    /// 프레임 상한 행에 실제로 <b>보여 줄</b> 글자. 인덱스만 믿으면 화면이 거짓말을 한다 —
    /// vSync가 켜져 있으면 상한 자체가 무시되고(기본 티어 High Fidelity가 vSyncCount=1이다),
    /// 저장 이력이 없을 때의 <c>_fps</c>는 부팅이 걸어 둔 실효 상한이라 <see cref="FpsNames"/>에
    /// 없는 값(모니터 주사율 등)일 수 있다. 둘 다 <see cref="FrameRateIndex"/>는 0으로 떨어져
    /// 「무제한」이 뜨는데, 실제로는 상한이 걸려 있다.
    /// </summary>
    public static string FrameCapDisplay
    {
        get
        {
            EnsureLoaded();
            if (_vsync)    return "— (수직 동기화)";
            if (_fps <= 0) return FpsNames[0];

            int index = FrameRateIndex;
            return index > 0 ? FpsNames[index] : _fps.ToString();
        }
    }

    // ── Public Methods ───────────────────────────────────────

    /// <summary>프리셋(0~2)을 고른다 — 티어 에셋을 갈아끼우고 개별 노브를 그 티어 기본값으로 되돌린다.</summary>
    public static void SetPreset(int level)
    {
        EnsureLoaded();

        if (level == PresetCustom) // 「사용자 지정」 직접 선택 — 현재 값을 그대로 둔다.
        {
            _custom = true;
            SavePrefs();
            return;
        }

        _level  = Mathf.Clamp(level, 0, Mathf.Max(0, QualitySettings.count - 1));
        _custom = false;

        QualitySettings.SetQualityLevel(_level, applyExpensiveChanges: true);

        var urp = CurrentUrpAsset;
        _renderScale = urp != null ? Pristine(urp).x : 1f;
        _aa     = 2; // SMAA — 프로젝트 전 씬 기준(project_scene_base_tone_unify)
        _shadow = 3;
        _post   = 2;
        _vsync  = QualitySettings.vSyncCount > 0; // 티어가 정한 값을 따른다

        ApplyAll();
        SavePrefs();
    }

    /// <param name="save">false면 PlayerPrefs 기록을 건너뛴다 — 슬라이더 드래그용.
    /// 손을 뗄 때 <see cref="SavePrefs"/>로 한 번 기록한다.</param>
    public static void SetRenderScale(float value, bool save = true)
    {
        EnsureLoaded();
        _renderScale = Mathf.Clamp(value, MinRenderScale, MaxRenderScale);
        MarkCustom();
        ApplyRenderScale();
        if (save) SavePrefs();
    }

    public static void SetAntialiasing(int mode)
    {
        EnsureLoaded();
        _aa = Mathf.Clamp(mode, 0, AaNames.Length - 1);
        MarkCustom();
        ApplyToCameras();
        SavePrefs();
    }

    public static void SetShadows(int quality)
    {
        EnsureLoaded();
        _shadow = Mathf.Clamp(quality, 0, ShadowNames.Length - 1);
        MarkCustom();
        ApplyShadows();
        SavePrefs();
    }

    public static void SetPostProcessing(int quality)
    {
        EnsureLoaded();
        _post = Mathf.Clamp(quality, 0, PostNames.Length - 1);
        MarkCustom();
        ApplyToCameras();
        ApplyVolumeWeights();
        SavePrefs();
    }

    public static void SetVSync(bool on)
    {
        EnsureLoaded();
        _vsync = on;
        MarkCustom();
        ApplyFrameRate();
        SavePrefs();
    }

    /// <param name="index"><see cref="FpsOptions"/> 안의 위치.</param>
    public static void SetFrameRateIndex(int index)
    {
        EnsureLoaded();
        _fps = FpsOptions[Mathf.Clamp(index, 0, FpsOptions.Length - 1)];
        MarkCustom();
        ApplyFrameRate();
        SavePrefs();
    }

    public static void SavePrefs()
    {
        PlayerPrefs.SetInt  (kLevelKey,       _level);
        PlayerPrefs.SetInt  (kCustomKey,      _custom ? 1 : 0);
        PlayerPrefs.SetFloat(kRenderScaleKey, _renderScale);
        PlayerPrefs.SetInt  (kAaKey,          _aa);
        PlayerPrefs.SetInt  (kShadowKey,      _shadow);
        PlayerPrefs.SetInt  (kPostKey,        _post);
        PlayerPrefs.SetInt  (kVSyncKey,       _vsync ? 1 : 0);
        PlayerPrefs.SetInt  (kFpsKey,         _fps);
        PlayerPrefs.Save();
    }

    /// <summary>저장된 값 전부를 현재 렌더 상태에 얹는다. 설정 화면을 열 때도 한 번 부른다(멱등).</summary>
    public static void ApplyAll()
    {
        EnsureLoaded();

        // 티어 스왑이 먼저다 — 아래 개별 노브가 그 위에 덧씌워진다.
        int level = Mathf.Clamp(_level, 0, Mathf.Max(0, QualitySettings.count - 1));
        if (QualitySettings.GetQualityLevel() != level)
            QualitySettings.SetQualityLevel(level, applyExpensiveChanges: true);

        ApplyRenderScale();
        ApplyShadows();
        ApplyFrameRate();
        ApplyToCameras();
        ApplyVolumeWeights();
    }

    // ── Private Methods ──────────────────────────────────────

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void RestoreOnBoot()
    {
        // 카메라·Volume은 씬 소유물이라 씬이 바뀌면 저작값으로 돌아간다 — 매 씬 다시 얹는다.
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;

#if UNITY_EDITOR
        Application.quitting -= RestorePristineAssets;
        Application.quitting += RestorePristineAssets;
#endif

        if (!HasSaved)
            return; // 저장된 선택 없음 — 프로젝트 기본 품질을 존중한다.

        ApplyAll();
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!HasSaved) return;

        ApplyToCameras();
        ApplyVolumeWeights();
    }

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;

        var urp = CurrentUrpAsset;

        _level       = PlayerPrefs.GetInt  (kLevelKey,       QualitySettings.GetQualityLevel());
        _custom      = PlayerPrefs.GetInt  (kCustomKey,      0) != 0;
        _renderScale = PlayerPrefs.GetFloat(kRenderScaleKey, urp != null ? urp.renderScale : 1f);
        _aa          = PlayerPrefs.GetInt  (kAaKey,          2);
        _shadow      = PlayerPrefs.GetInt  (kShadowKey,      3);
        _post        = PlayerPrefs.GetInt  (kPostKey,        2);
        _vsync       = PlayerPrefs.GetInt  (kVSyncKey,       QualitySettings.vSyncCount > 0 ? 1 : 0) != 0;

        // 저장 이력이 없으면 0(무제한)이 아니라 부팅이 실제로 걸어 둔 상한을 물려받는다.
        // AppBootstrapper가 주사율에 맞춰 targetFrameRate를 잡아 두는데, 0으로 시작하면
        // ① 화면엔 「무제한」이 뜨고 ② 다른 노브를 하나만 만져도 ApplyFrameRate가 그 상한을 조용히 푼다.
        // -1(미설정)은 Max가 0으로 눌러 준다.
        _fps         = PlayerPrefs.GetInt  (kFpsKey,         Mathf.Max(0, Application.targetFrameRate));
    }

    private static void MarkCustom() => _custom = true;

    private static UniversalRenderPipelineAsset CurrentUrpAsset
        => GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;

    /// <summary>이 에셋을 우리가 건드리기 <b>직전</b>의 (renderScale, shadowDistance). 최초 1회만 기록된다.</summary>
    private static Vector2 Pristine(UniversalRenderPipelineAsset asset)
    {
        if (!_pristineAsset.TryGetValue(asset, out var v))
        {
            v = new Vector2(asset.renderScale, asset.shadowDistance);
            _pristineAsset[asset] = v;
        }
        return v;
    }

    private static void ApplyRenderScale()
    {
        var urp = CurrentUrpAsset;
        if (urp == null) return;

        Pristine(urp); // 변경 전 값을 먼저 확보한다
        urp.renderScale = Mathf.Clamp(_renderScale, MinRenderScale, MaxRenderScale);
    }

    private static void ApplyShadows()
    {
        var urp = CurrentUrpAsset;
        if (urp == null) return;

        // 캐스케이드 수·그림자맵 해상도는 티어 에셋이 들고 있다(런타임 변경 불가) — 여기선 거리만 조절한다.
        float baseDistance = Pristine(urp).y;
        urp.shadowDistance = baseDistance * ShadowDistanceScale[Mathf.Clamp(_shadow, 0, ShadowDistanceScale.Length - 1)];
    }

    private static void ApplyFrameRate()
    {
        QualitySettings.vSyncCount = _vsync ? 1 : 0;

        // vSync가 켜져 있으면 targetFrameRate는 무시된다 — UI에서도 상호배타로 잠근다.
        Application.targetFrameRate = (_vsync || _fps <= 0) ? -1 : _fps;
    }

    private static void ApplyToCameras()
    {
        var mode = _aa switch
        {
            1 => AntialiasingMode.FastApproximateAntialiasing,
            2 => AntialiasingMode.SubpixelMorphologicalAntiAliasing,
            _ => AntialiasingMode.None,
        };
        bool postOn = _post > 0;

        Prune(_authoredPost);

        var cameras = Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var cam in cameras)
        {
            if (!cam.TryGetComponent<UniversalAdditionalCameraData>(out var data))
                continue;

            // 오버레이 카메라의 AA·포스트프로세싱은 베이스 카메라 스택이 결정한다 — 여기서 또 켜면 이중 적용이다.
            if (data.renderType != CameraRenderType.Base)
                continue;

            if (!_authoredPost.TryGetValue(data, out bool authored))
            {
                authored = data.renderPostProcessing;
                _authoredPost[data] = authored;
            }

            data.antialiasing        = mode;
            data.antialiasingQuality = AntialiasingQuality.High;
            data.renderPostProcessing = authored && postOn;
        }
    }

    private static void ApplyVolumeWeights()
    {
        if (_post <= 0)
            return; // 카메라에서 통째로 막았다 — 저작된 weight는 건드리지 않고 남겨 둔다.

        float scale = PostWeightScale[Mathf.Clamp(_post, 0, PostWeightScale.Length - 1)];

        Prune(_authoredWeight);

        var volumes = Object.FindObjectsByType<Volume>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var volume in volumes)
        {
            // 로컬 볼륨은 연출용(트리거 범위 안에서만 도는 값) — 전역 톤에만 강도를 적용한다.
            if (!volume.isGlobal)
                continue;

            if (!_authoredWeight.TryGetValue(volume, out float authored))
            {
                authored = volume.weight;
                _authoredWeight[volume] = authored;
            }

            volume.weight = authored * scale;
        }
    }

    /// <summary>씬이 바뀌면서 파괴된 키를 걷어낸다 — 안 하면 캐시가 씬 수만큼 자란다.
    /// 걷어낼 게 없으면 할당도 없다(설정 화면 경로라 프레임 예산과는 무관하지만 습관).</summary>
    private static void Prune<TKey, TValue>(Dictionary<TKey, TValue> map) where TKey : Object
    {
        if (map.Count == 0) return;

        List<TKey> dead = null;
        foreach (var key in map.Keys)
        {
            if (key == null)
                (dead ??= new List<TKey>()).Add(key);
        }

        if (dead == null) return;
        foreach (var key in dead)
            map.Remove(key);
    }

#if UNITY_EDITOR
    /// <summary>플레이 모드에서 바꾼 URP 에셋 값을 되돌린다 — 안 하면 .asset 파일이 더티인 채로 남는다.</summary>
    private static void RestorePristineAssets()
    {
        foreach (var pair in _pristineAsset)
        {
            if (pair.Key == null) continue;
            pair.Key.renderScale   = pair.Value.x;
            pair.Key.shadowDistance = pair.Value.y;
        }
        _pristineAsset.Clear();
    }
#endif
}
