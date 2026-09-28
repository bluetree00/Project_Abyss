using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 조립 서약 발동 연출 — 티어 3단(실버/골드/루비)으로 세기를 나눈다.
///
/// 티어는 순수 파워 축이라 <b>숫자로만</b> 커진다. 숫자는 화면에 거의 안 보이므로,
/// 루비를 뽑았다는 사실이 손끝에 전달되지 않는다. 발동 순간의 체감을 티어에 묶어
/// "좋은 걸 뽑았다"가 즉시 느껴지게 한다.
///
/// 새 연출 시스템을 만들지 않는다 — 타격감(<see cref="HitFeelService"/>)과
/// 화면 펄스(<see cref="VolumePulseService"/>)를 그대로 재사용한다.
/// 효과 VFX(초신성 폭발·결계 등)도 여기서 낸다 — 프리팹은 <see cref="CovenantVfxSet"/>, 재생은 <see cref="ElementVfxPlayer"/>의 풀.
/// <b>Time.timeScale을 직접 건드리지 않는다</b>(TimeScaleArbiter 소유). 정지는 HitFeelService.HitStop만.
/// </summary>
public static class CovenantFxService
{
    // ── Constants ────────────────────────────────────────
    private const float Throttle = 0.4f;   // 효과별 최소 발동 간격(초, unscaled)

    /// <summary>효과 VFX 세트 Addressable 키.</summary>
    public const string SetKey  = "CovenantVfxSet";
    /// <summary>루비 발동 VFX 키(세트 안).</summary>
    public const string RubyKey = "ruby";

    // 실버 — 존재감만. 연타 원인(연격 등)에 얹혀도 거슬리지 않을 만큼 약하게.
    private const float SilverShakeAmp = HitFeelService.DealtShakeBasic, SilverShakeDur = 0.06f;   // 09-25: 0.04 → 기본 공격 상한

    // 골드 — 짧고 얕은 정지 + 중간 셰이크 + 화면 펄스.
    private const float GoldStopScale  = 0.10f, GoldStopDur    = 0.05f;
    private const float GoldShakeAmp   = HitFeelService.DealtShakeSkill, GoldShakeDur   = 0.12f;   // 09-25: 0.10 → 스킬 상한
    private const float GoldPulsePeak  = 0.35f, GoldPulseDur   = 0.12f;

    // 루비 — 더 깊고 긴 정지(단 0.10s 상한) + 방향성 셰이크 + 큰 펄스 + VFX.
    private const float RubyStopScale  = 0.04f, RubyStopDur    = 0.10f;
    private const float RubyShakeAmp   = HitFeelService.DealtShakeFinisher, RubyShakeDur   = 0.18f;   // 09-25: 0.18(최대치) → 막타 상한
    private const float RubyPulsePeak  = 0.70f, RubyPulseDur   = 0.20f;

    // ── Static ───────────────────────────────────────────
    private static readonly Dictionary<string, float> _lastPlay = new();
    private static int _lastFrame = -1;

    private static CovenantVfxSet _set;
    private static bool _setLoading;

    // 도메인 리로드 OFF 2회차 대비 — unscaledTime은 0부터 다시 흐르는데 _lastPlay에는
    // 지난 세션의 큰 시각이 남아 (now - last)가 음수가 되고, 연출이 영구히 스로틀에 걸린다.
    // (HitFeelService/VolumePulseService의 ResetStatics와 같은 패턴)
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _lastPlay.Clear();
        _lastFrame = -1;
        _set        = null;
        _setLoading = false;
    }

    // ── Public Methods ───────────────────────────────────
    /// <summary>
    /// 티어 연출 1회. 효과별 0.4초 스로틀 + 프레임당 1회로 제한한다
    /// (광역 발동이 여러 적을 죽여 같은 프레임에 연쇄로 불려도 화면이 경련하지 않게).
    /// </summary>
    /// <param name="effectId">스로틀 키. 서로 다른 효과는 각자 자기 간격을 갖는다.</param>
    /// <param name="dir">플레이어 → 발동 지점 방향(루비 방향성 셰이크용). 0이면 무방향.</param>
    /// <returns>실제로 연출이 나갔는지. 루비 VFX처럼 호출측이 이어 붙일 게 있을 때 쓴다.</returns>
    public static bool Play(CovenantTier tier, string effectId, Vector3 dir)
    {
        float now = Time.unscaledTime;
        int frame = Time.frameCount;
        if (frame == _lastFrame) return false;

        string key = effectId ?? string.Empty;
        if (_lastPlay.TryGetValue(key, out float last) && now - last < Throttle) return false;

        _lastPlay[key] = now;
        _lastFrame     = frame;

        switch (tier)
        {
            case CovenantTier.Ruby:
                HitFeelService.HitStop(RubyStopScale, RubyStopDur);
                HitFeelService.CameraShakeDirectional(dir, RubyShakeAmp, RubyShakeDur);
                VolumePulseService.Pulse(RubyPulsePeak, RubyPulseDur);
                break;

            case CovenantTier.Gold:
                HitFeelService.HitStop(GoldStopScale, GoldStopDur);
                HitFeelService.CameraShake(GoldShakeAmp, GoldShakeDur);
                VolumePulseService.Pulse(GoldPulsePeak, GoldPulseDur);
                break;

            default:
                HitFeelService.CameraShake(SilverShakeAmp, SilverShakeDur);
                break;
        }
        return true;
    }

    /// <summary>효과 VFX 세트를 미리 읽어 둔다 — 첫 발동에서 이펙트가 빠지지 않게. 서약 핸들러 초기화가 부른다.</summary>
    public static void Preload() => LoadSetAsync().Forget();

    /// <summary>
    /// 효과 VFX 1회. radius>0이면 판정 반경에 크기를 맞춘다(세트가 반경 맞춤으로 표시한 항목만).
    /// 세트가 아직 없거나 키가 없으면 조용히 넘어간다 — 연출이 빠질 뿐 효과는 이미 들어갔다.
    /// </summary>
    public static void Burst(string key, Vector3 pos, float radius = 0f)
    {
        if (_set == null) { Preload(); return; }
        if (_set.TryGet(key, radius, out var prefab, out float scale, out float life))
            ElementVfxPlayer.PlayPrefab(prefab, pos, scale, life);
    }

    /// <summary>효과 VFX를 대상에 붙인다(대상+프리팹별 1개, 다시 붙이면 수명 갱신). duration≤0이면 세트의 기본 지속.</summary>
    public static void Attach(string key, Transform target, float duration = 0f)
    {
        if (target == null) return;
        if (_set == null) { Preload(); return; }
        if (_set.TryGet(key, 0f, out var prefab, out float scale, out float life))
            ElementVfxPlayer.AttachPrefab(prefab, target, duration > 0f ? duration : life, scale);
    }

    // ── Private Methods ──────────────────────────────────
    private static async UniTaskVoid LoadSetAsync()
    {
        if (_set != null || _setLoading) return;
        _setLoading = true;
        try
        {
            _set = await Managers.AddressableManager.TryLoadAssetAsync<CovenantVfxSet>(SetKey);
            if (_set == null) Debug.LogWarning($"[CovenantFx] '{SetKey}' Addressable 로드 실패 — 서약 효과 VFX 미표시");
        }
        finally { _setLoading = false; }
    }
}
