using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 빈사 경고 — 체력이 낮을 때 화면 가장자리에 붉은 기운을 <b>아주 옅게, 계속</b> 깔아 둔다.
/// 피격 순간 연출(<see cref="PlayerHitPresentation"/> · HUD 비네트)과 달리 번쩍이지 않는다.
/// 숨 쉬듯 아주 천천히 밝아졌다 어두워지기만 해서, 전투 중 읽기를 방해하지 않는다.
///
/// UI 비네트가 아니라 <b>URP Volume</b>이다 — 후처리라 HUD와 팝업 <b>아래</b>에 깔린다(가독성 보존).
/// VolumePulseService와 같은 방식으로 호스트·Volume·Profile을 런타임에 만든다(에셋 파일 0개).
/// Time.timeScale과 무관하게 숨쉬도록 unscaledTime을 쓴다.
/// </summary>
public static class LowHpVolumeService
{
    // ── Constants ─────────────────────────────────────────────────
    private const float StartRatio    = 0.35f;  // 이 비율 아래부터 보이기 시작
    // 09-21 실측으로 올림(0.26 → 0.30 → 0.45). URP 비네트는 intensity가 '어두워지는 범위'라, 0.3 이하면
    // 꼭짓점만 아주 살짝 물들어(모서리 R +6/255) 눈에 안 띈다. 번짐도 0.65는 너무 퍼져서 0.45로 좁혔다.
    private const float MaxIntensity  = 0.45f;  // 체력 0에 가까울 때의 최대 — 은은하되 보이는 선
    private const float Smoothness    = 0.45f;  // 가장자리 번짐
    private const float BreathHz      = 0.45f;  // 숨 주기(초당)
    private const float BreathDepth   = 0.18f;  // 숨 깊이(최대치 대비)
    private const float FollowPerSec  = 6f;     // 목표치로 따라가는 속도(급변 방지)
    private const float VolumePriority = 9f;    // 히트 펄스(10) 바로 아래, ambient 위

    private static readonly Color WarnColor = new(0.72f, 0.03f, 0.03f, 1f);

    // ── Static ────────────────────────────────────────────────────
    private class Host : MonoBehaviour
    {
        private void LateUpdate() => Tick();
    }

    private static Host          _host;
    private static Volume        _volume;
    private static VolumeProfile _profile;
    private static Vignette      _vignette;
    private static bool          _initialized;

    private static float _targetLevel;   // 0~1 — 체력에서 뽑은 목표 세기
    private static float _currentLevel;  // 0~1 — 실제로 따라가는 값

    // 도메인 리로드 비활성(에디터) 시 정적 상태가 다음 플레이세션으로 새지 않게 한다(VolumePulseService와 같은 이유).
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _initialized  = false;
        _host         = null;
        _volume       = null;
        _profile      = null;
        _vignette     = null;
        _targetLevel      = 0f;
        _currentLevel     = 0f;
        CurrentIntensity  = 0f;
    }

    // ── Public Methods ────────────────────────────────────────────
    /// <summary>지금 화면에 들어간 세기(0~1). 실측·검증용.</summary>
    public static float CurrentIntensity { get; private set; }

    /// <summary>남은 체력 비율(0~1)을 알려 준다. StartRatio 위면 꺼진다.</summary>
    public static void SetHpRatio(float ratio01)
    {
        if (!_initialized) Init();
        // StartRatio에서 0까지 곧게 올린다. 제곱으로 깔았더니 체력 18%에서도 0.06이라 화면에서 안 보였다(09-21 실측).
        _targetLevel = Mathf.InverseLerp(StartRatio, 0f, Mathf.Clamp01(ratio01));
    }

    /// <summary>즉시 끈다(런 종료·플레이어 해제). 사망 연출처럼 서서히 빠지길 원하면 SetHpRatio(1)을 쓴다.</summary>
    public static void Clear()
    {
        _targetLevel  = 0f;
        _currentLevel = 0f;
        Apply(0f);
    }

    // ── Private Methods ───────────────────────────────────────────
    private static void Init()
    {
        if (_initialized) return;
        _initialized = true;

        var go = new GameObject("[LowHpVolumeHost]");
        Object.DontDestroyOnLoad(go);
        _host = go.AddComponent<Host>();

        _profile = ScriptableObject.CreateInstance<VolumeProfile>();
        _profile.name = "LowHpVolumeProfile (Runtime)";

        _vignette = _profile.Add<Vignette>(overrides: true);
        _vignette.active = true;
        _vignette.color.overrideState      = true;
        _vignette.color.value              = WarnColor;
        _vignette.smoothness.overrideState = true;
        _vignette.smoothness.value         = Smoothness;
        _vignette.intensity.overrideState  = true;
        _vignette.intensity.value          = 0f;

        var volumeGo = new GameObject("[LowHpVolume]");
        volumeGo.transform.SetParent(_host.transform, worldPositionStays: false);

        _volume               = volumeGo.AddComponent<Volume>();
        _volume.isGlobal      = true;
        _volume.priority      = VolumePriority;
        _volume.weight        = 1f;
        _volume.sharedProfile = _profile;
    }

    private static void Tick()
    {
        if (_vignette == null) return;

        _currentLevel = Mathf.MoveTowards(_currentLevel, _targetLevel, FollowPerSec * Time.unscaledDeltaTime);
        if (_currentLevel <= 0f)
        {
            Apply(0f);
            return;
        }

        float breath = 1f - BreathDepth * (0.5f - 0.5f * Mathf.Cos(Time.unscaledTime * BreathHz * 2f * Mathf.PI));
        Apply(_currentLevel * MaxIntensity * breath);
    }

    private static void Apply(float intensity)
    {
        if (_vignette == null) return;
        CurrentIntensity = intensity;
        _vignette.intensity.value = intensity;
        if (_volume != null) _volume.enabled = intensity > 0.0005f;
    }
}
