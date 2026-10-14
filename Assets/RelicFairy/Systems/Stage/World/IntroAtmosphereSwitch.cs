using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 인트로 대관홀 분위기 전환 — 초기 "밝은 성당(화창)" → 타락 시 "어둠 + 핵심 라이팅".
/// IntroMordredDirector가 타락 순간 SwitchToDarkAsync(duration)로 <b>보이는 웅장한 암전</b>을 재생한다.
/// 주 방향광 색/세기 + 환경광 + 안개 + 라이트 세트 토글을 함께 스왑. 값은 인스펙터 튜닝 전제.
/// </summary>
public sealed class IntroAtmosphereSwitch : MonoBehaviour
{
    private static readonly int SkyExposureId = Shader.PropertyToID("_Exposure");

    // ── [SerializeField] ─────────────────────────────────────────────
    [Header("라이트 세트 (오브젝트 토글)")]
    [Tooltip("밝은 상태에서 켜질 오브젝트(창 성광·볼류메트릭 등).")]
    [SerializeField] private GameObject[] brightObjects;
    [Tooltip("어둠 상태에서 켜질 오브젝트(집중 스팟·암흑 오라 등).")]
    [SerializeField] private GameObject[] darkObjects;

    [Header("주 방향광")]
    [SerializeField] private Light  sunLight;
    [SerializeField] private Color  brightSunColor     = new Color(1f, 0.95f, 0.82f);
    [SerializeField, Min(0f)] private float brightSunIntensity = 1.4f;
    [SerializeField] private Color  darkSunColor       = new Color(0.42f, 0.46f, 0.72f);
    [SerializeField, Min(0f)] private float darkSunIntensity   = 0.15f;

    [Header("환경광")]
    [SerializeField] private Color brightAmbient = new Color(0.72f, 0.68f, 0.58f);
    [SerializeField] private Color darkAmbient   = new Color(0.07f, 0.07f, 0.12f);

    [Header("안개 (선택)")]
    [SerializeField] private bool  controlFog        = true;
    [SerializeField] private Color brightFog         = new Color(0.86f, 0.82f, 0.70f);
    [SerializeField] private Color darkFog           = new Color(0.05f, 0.05f, 0.09f);
    [SerializeField, Min(0f)] private float brightFogDensity = 0.004f;
    [SerializeField, Min(0f)] private float darkFogDensity   = 0.018f;

    [Header("하늘 (스카이박스 노출)")]
    [Tooltip("어둠 상태의 스카이박스 노출 — 성당 벽 틈으로 한낮 하늘이 하얗게 뚫려 보였다(10-09 전주기 시뮬). 복제본에만 걸어 원본 재질은 그대로.")]
    [SerializeField, Min(0f)] private float darkSkyExposure = 0.08f;

    [Header("초기 상태")]
    [SerializeField] private bool startBright = true;

    // ── Private ──────────────────────────────────────────────────────
    private Material _skyInstance;

    // ── Lifecycle ────────────────────────────────────────────────────
    private void Awake() => ApplyInstant(startBright);

    private void OnDestroy()
    {
        if (_skyInstance != null) Destroy(_skyInstance);
    }

    // ── Public ───────────────────────────────────────────────────────
    /// <summary>지정 시간에 걸쳐 밝은→어둠으로 <b>보이게</b> 전환(웅장한 배경 페이드). duration&lt;=0이면 즉시.</summary>
    public async UniTask SwitchToDarkAsync(float duration, CancellationToken ct)
    {
        if (duration <= 0f) { ApplyInstant(false); return; }

        SetActiveAll(darkObjects, true); // 어둠 세트는 전환 시작과 함께 등장

        Color sunFrom  = sunLight != null ? sunLight.color     : default;
        float sunIFrom = sunLight != null ? sunLight.intensity : 0f;
        Color ambFrom  = RenderSettings.ambientLight;
        Color fogFrom  = RenderSettings.fogColor;
        float fogDFrom = RenderSettings.fogDensity;
        Material sky   = DarkableSky();
        float skyFrom  = sky != null ? sky.GetFloat(SkyExposureId) : 0f;

        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        if (controlFog) RenderSettings.fog = true;

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / duration));

            if (sunLight != null)
            {
                sunLight.color     = Color.Lerp(sunFrom, darkSunColor, k);
                sunLight.intensity = Mathf.Lerp(sunIFrom, darkSunIntensity, k);
            }
            RenderSettings.ambientLight = Color.Lerp(ambFrom, darkAmbient, k);
            if (controlFog)
            {
                RenderSettings.fogColor   = Color.Lerp(fogFrom, darkFog, k);
                RenderSettings.fogDensity = Mathf.Lerp(fogDFrom, darkFogDensity, k);
            }
            if (sky != null) sky.SetFloat(SkyExposureId, Mathf.Lerp(skyFrom, darkSkyExposure, k));

            try { await UniTask.Yield(PlayerLoopTiming.Update, ct); }
            catch (System.OperationCanceledException) { return; }
        }
        ApplyInstant(false); // 최종값 확정 + 밝은 세트 off
    }

    /// <summary>
    /// 어둠 쪽으로 <b>부분</b> 감광한다(리치 강림 예고 — 촛불이 한 줄씩 꺼지는 단계).
    /// amount01=0이면 변화 없음, 1이면 완전한 어둠. 오브젝트 세트는 건드리지 않아
    /// 이후 SwitchToDarkAsync가 나머지를 이어받는다.
    /// </summary>
    public async UniTask DimStepAsync(float amount01, float duration, CancellationToken ct)
    {
        amount01 = Mathf.Clamp01(amount01);
        if (amount01 <= 0f) return;

        Color sunFrom  = sunLight != null ? sunLight.color     : default;
        float sunIFrom = sunLight != null ? sunLight.intensity : 0f;
        Color ambFrom  = RenderSettings.ambientLight;
        Color fogFrom  = RenderSettings.fogColor;
        float fogDFrom = RenderSettings.fogDensity;

        // 목표 = 현재값과 최종 어둠값 사이의 amount01 지점
        Color sunTo  = Color.Lerp(sunFrom,  darkSunColor,     amount01);
        float sunITo = Mathf.Lerp(sunIFrom, darkSunIntensity, amount01);
        Color ambTo  = Color.Lerp(ambFrom,  darkAmbient,      amount01);
        Color fogTo  = Color.Lerp(fogFrom,  darkFog,          amount01);
        float fogDTo = Mathf.Lerp(fogDFrom, darkFogDensity,   amount01);

        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        if (controlFog) RenderSettings.fog = true;

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / duration));

            if (sunLight != null)
            {
                sunLight.color     = Color.Lerp(sunFrom, sunTo, k);
                sunLight.intensity = Mathf.Lerp(sunIFrom, sunITo, k);
            }
            RenderSettings.ambientLight = Color.Lerp(ambFrom, ambTo, k);
            if (controlFog)
            {
                RenderSettings.fogColor   = Color.Lerp(fogFrom, fogTo, k);
                RenderSettings.fogDensity = Mathf.Lerp(fogDFrom, fogDTo, k);
            }

            try { await UniTask.Yield(PlayerLoopTiming.Update, ct); }
            catch (System.OperationCanceledException) { return; }
        }
    }

    public void SwitchToDark()   => ApplyInstant(false);
    public void SwitchToBright() => ApplyInstant(true);

    // ── Private ──────────────────────────────────────────────────────
    private void ApplyInstant(bool bright)
    {
        SetActiveAll(brightObjects, bright);
        SetActiveAll(darkObjects, !bright);

        if (sunLight != null)
        {
            sunLight.color     = bright ? brightSunColor : darkSunColor;
            sunLight.intensity = bright ? brightSunIntensity : darkSunIntensity;
        }

        RenderSettings.ambientMode  = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = bright ? brightAmbient : darkAmbient;

        if (controlFog)
        {
            RenderSettings.fog        = true;
            RenderSettings.fogColor   = bright ? brightFog : darkFog;
            RenderSettings.fogDensity = bright ? brightFogDensity : darkFogDensity;
        }

        // 밝은 쪽은 씬 하늘 그대로(복제본을 만들지 않는다) — 어둠으로 간 뒤에만 노출을 내린다.
        if (!bright) DarkableSky()?.SetFloat(SkyExposureId, darkSkyExposure);
    }

    /// <summary>노출을 바꿀 수 있는 하늘의 복제본 — 처음 한 번 만들어 RenderSettings에 건다(원본 재질 에셋을 플레이 중에 고치지 않게).</summary>
    private Material DarkableSky()
    {
        if (_skyInstance != null) return _skyInstance;
        var sky = RenderSettings.skybox;
        if (sky == null || !sky.HasProperty(SkyExposureId)) return null;
        _skyInstance = new Material(sky);
        RenderSettings.skybox = _skyInstance;
        return _skyInstance;
    }

    private static void SetActiveAll(GameObject[] objs, bool on)
    {
        if (objs == null) return;
        foreach (var o in objs) if (o != null) o.SetActive(on);
    }
}
