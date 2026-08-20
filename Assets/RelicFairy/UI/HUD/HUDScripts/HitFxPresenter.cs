//============================================================
// HitFxPresenter.cs
// - GlobalFeelCoalescer.OnBurst 구독 → FXLayer로 위임 (블로그 UI 화면 연출 3종)
//   ※ 타격 1건이 아니라 '그 프레임의 타격 전부를 합친 1건'을 받는다. 풀스크린 플래시는
//     겹칠수록 눈만 아프고 정보가 늘지 않아, 분열 20발도 플래시는 한 번이어야 한다.
// - 보완(Flash): 모든 히트(합산 1회)
// - ④ Zoom-In: 크리티컬 포함 시
// - ⑦ Damage Vignette: 플레이어가 실제 피격(PlayerController.OnDamageTaken)일 때 — HP 비례 강도
//   ※ 몬스터 공격은 ColliderInstance/RaiseHit를 타지 않고 player.TakeDamage 직접 호출이므로,
//     OnHit(info.Target=Player) 경로로는 비네트가 발동되지 않는다 → 실제 피격 이벤트에 연결.
//
// 호스트: FXLayer.prefab 루트에 부착 — 수명이 Layer와 동일
//============================================================
using RelicFairy.UI.Overlay;
using UnityEngine;

public sealed class HitFxPresenter : MonoBehaviour
{
    // ── Constants ─────────────────────────────────────────────────
    // Flash
    private const float FlashPeakNormal   = 0.3f;
    private const float FlashPeakCritical = 0.55f;
    private const float FlashDuration     = 0.06f;

    // Zoom-In (크리티컬만)
    private const float ZoomIntensity     = 0.4f;
    private const float ZoomDuration      = 0.14f;

    // Damage Vignette (플레이어 피격) — 잔여 HP 낮을수록 강하고 길게 (위험 전달)
    private const float VignetteIntensityFull = 0.45f;  // HP 가득
    private const float VignetteIntensityLow  = 0.9f;   // 빈사
    private const float VignetteDurationFull  = 0.28f;
    private const float VignetteDurationLow   = 0.45f;

    // ── Static ────────────────────────────────────────────────────
    private static readonly Color DefaultFlashColor   = new Color(1f, 1f, 1f, 1f);
    private static readonly Color CritFlashColor      = new Color(1f, 0.95f, 0.35f, 1f);
    private static readonly Color DamageVignetteColor = new Color(1f, 0.1f, 0.1f, 1f);

    // ── Private ───────────────────────────────────────────────────
    private FXLayer          _layer;
    private PlayerController  _player;

    // ── Lifecycle ─────────────────────────────────────────────────
    private void Awake()
    {
        // 같은 프리팹 루트에 FXLayer가 있음 (FXLayer.prefab)
        _layer = GetComponent<FXLayer>();
        if (_layer == null) _layer = GetComponentInParent<FXLayer>(true);
    }

    private void OnEnable()
    {
        GlobalFeelCoalescer.OnBurst += OnHitBurst;

        var pm = Managers.Player;
        if (pm != null)
        {
            pm.OnPlayerSpawned += HandlePlayerSpawned;
            HookPlayer(pm.PlayerTransform);   // 이미 스폰돼 있으면 즉시 연결
        }
    }

    private void OnDisable()
    {
        GlobalFeelCoalescer.OnBurst -= OnHitBurst;

        var pm = Managers.Player;
        if (pm != null) pm.OnPlayerSpawned -= HandlePlayerSpawned;
        UnhookPlayer();
    }

    // ── Private Methods ───────────────────────────────────────────
    private FXLayer GetLayer()
    {
        if (_layer != null) return _layer;
        _layer = Managers.UI?.GetOverlayUI<FXLayer>();
        return _layer;
    }

    private void HookPlayer(Transform playerTf)
    {
        if (playerTf == null) return;
        if (!playerTf.TryGetComponent<PlayerController>(out var pc)) return;
        if (_player == pc) return;

        UnhookPlayer();
        _player = pc;
        _player.OnDamageTaken += HandlePlayerDamaged;
    }

    private void UnhookPlayer()
    {
        if (_player == null) return;
        _player.OnDamageTaken -= HandlePlayerDamaged;
        _player = null;
    }

    // ── Event Handlers ────────────────────────────────────────────
    private void OnHitBurst(HitBurst burst)
    {
        var fx = GetLayer();
        if (fx == null) return;

        // 1) Flash — 프레임당 1회. 세기는 타격 수와 무관하다(겹침 = 시각 노이즈).
        var   flashColor = burst.AnyCritical ? CritFlashColor : DefaultFlashColor;
        float flashPeak  = burst.AnyCritical ? FlashPeakCritical : FlashPeakNormal;
        fx.Flash(flashColor, FlashDuration, flashPeak);

        // 2) Zoom-In 집중선 — 합산분에 크리티컬이 하나라도 있으면 1회.
        if (burst.AnyCritical)
            fx.ZoomIn(ZoomIntensity, ZoomDuration);

        // 3) Damage Vignette는 플레이어 실제 피격(HandlePlayerDamaged)에서 처리.
    }

    private void HandlePlayerSpawned(Transform playerTf) => HookPlayer(playerTf);

    private void HandlePlayerDamaged()
    {
        var fx = GetLayer();
        if (fx == null || _player == null) return;

        // HandlePlayerDamaged 시점엔 이미 RuntimeStats.Damage 적용 후 → 잔여 HP 비율로 위험도 산출.
        float ratio = 1f;
        var stats = _player.RuntimeStats;
        if (stats != null && stats.MaxHp > 0)
            ratio = Mathf.Clamp01((float)stats.Hp / stats.MaxHp);

        float danger    = 1f - ratio;   // HP 낮을수록 1에 근접
        float intensity = Mathf.Lerp(VignetteIntensityFull, VignetteIntensityLow, danger);
        float duration  = Mathf.Lerp(VignetteDurationFull, VignetteDurationLow, danger);

        fx.Vignette(DamageVignetteColor, intensity, duration);
    }
}
