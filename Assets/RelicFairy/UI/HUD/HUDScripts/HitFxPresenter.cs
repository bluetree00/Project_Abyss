//============================================================
// HitFxPresenter.cs
// - HitFeedbackService.OnHit 구독 → FXLayer로 위임 (블로그 UI 화면 연출 3종)
// - 보완(Flash): 모든 히트
// - ④ Zoom-In: 크리티컬만
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
        HitFeedbackService.OnHit += OnHit;

        var pm = Managers.Player;
        if (pm != null)
        {
            pm.OnPlayerSpawned += HandlePlayerSpawned;
            HookPlayer(pm.PlayerTransform);   // 이미 스폰돼 있으면 즉시 연결
        }
    }

    private void OnDisable()
    {
        HitFeedbackService.OnHit -= OnHit;

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
    private void OnHit(HitInfo info)
    {
        var fx = GetLayer();
        if (fx == null) return;

        // 1) Flash — 모든 히트 (공격자 측 타격감)
        var   flashColor = info.IsCritical ? CritFlashColor : DefaultFlashColor;
        float flashPeak  = info.IsCritical ? FlashPeakCritical : FlashPeakNormal;
        fx.Flash(flashColor, FlashDuration, flashPeak);

        // 2) Zoom-In 집중선 — 크리티컬만
        if (info.IsCritical)
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
