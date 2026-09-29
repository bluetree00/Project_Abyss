//============================================================
// HitFxPresenter.cs
// - 플레이어 피격 화면 연출(붉은 비네트)만 맡는다.
// - 몬스터 타격 쪽 화면 연출(전체화면 플래시·크리티컬 집중선)은 09-20 사용자 결정으로 뺐다 —
//   몬스터 타격감은 이미 충분하고, 타격마다 화면 전체가 번쩍이거나 빛살이 치면 눈이 피로하다.
//   되살릴 땐 GlobalFeelCoalescer.OnBurst를 구독해 FXLayer.Flash / ZoomIn을 부르면 된다.
// - ⑦ Damage Vignette: 플레이어 피격 연출 신호(PlayerController.OnHitTaken)일 때 — 등급(약·중·강) × 남은 HP
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
    // Damage Vignette (플레이어 피격) — 등급별 기준(HP 가득 기준). 잔여 HP가 낮을수록 더 진하고 길게(위험 전달).
    // 두 번 낮췄다: 09-20 화면 실측(0.22/0.42/0.70 → 강에서 화면이 덮였다), 09-21 사용자 요청(은은하게).
    // 빈사 상태 자체는 LowHpVolumeService(후처리)가 계속 알리므로, 순간 연출은 약해도 정보가 빠지지 않는다.
    private const float VignetteLight      = 0.08f, VignetteDurLight  = 0.18f;
    private const float VignetteMedium     = 0.16f, VignetteDurMedium = 0.26f;
    private const float VignetteHeavy      = 0.28f, VignetteDurHeavy  = 0.38f;
    private const float VignetteDangerGain = 1.4f;   // 빈사 배수(세기)
    private const float VignetteDangerTime = 1.4f;   // 빈사 배수(시간)

    // ── Static ────────────────────────────────────────────────────
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
        var pm = Managers.Player;
        if (pm != null)
        {
            pm.OnPlayerSpawned += HandlePlayerSpawned;
            HookPlayer(pm.PlayerTransform);   // 이미 스폰돼 있으면 즉시 연결
        }
    }

    private void OnDisable()
    {
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
        _player.OnHitTaken += HandlePlayerDamaged;
    }

    private void UnhookPlayer()
    {
        if (_player == null) return;
        _player.OnHitTaken -= HandlePlayerDamaged;
        _player = null;
    }

    // ── Event Handlers ────────────────────────────────────────────
    private void HandlePlayerSpawned(Transform playerTf) => HookPlayer(playerTf);

    private void HandlePlayerDamaged(HitWeight weight, Vector3 hitDir, int damage)
    {
        var fx = GetLayer();
        if (fx == null || _player == null) return;

        // HandlePlayerDamaged 시점엔 이미 RuntimeStats.Damage 적용 후 → 잔여 HP 비율로 위험도 산출.
        float ratio = 1f;
        var stats = _player.RuntimeStats;
        if (stats != null && stats.MaxHp > 0)
            ratio = Mathf.Clamp01((float)stats.Hp / stats.MaxHp);

        float danger = 1f - ratio;   // HP 낮을수록 1에 근접
        float baseIntensity = weight switch { HitWeight.Heavy => VignetteHeavy, HitWeight.Medium => VignetteMedium, _ => VignetteLight };
        float baseDuration  = weight switch { HitWeight.Heavy => VignetteDurHeavy, HitWeight.Medium => VignetteDurMedium, _ => VignetteDurLight };
        float intensity = Mathf.Min(1f, baseIntensity * Mathf.Lerp(1f, VignetteDangerGain, danger));
        float duration  = baseDuration * Mathf.Lerp(1f, VignetteDangerTime, danger);

        fx.Vignette(DamageVignetteColor, intensity, duration);
    }
}
