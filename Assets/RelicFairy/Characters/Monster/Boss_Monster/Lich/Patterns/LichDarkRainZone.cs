using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// 암흑 소나기 패턴의 낙하 폭탄 하나.
///
/// 흐름: Telegraph(보라 disc가 차오름) → Active(빨간 disc + 착탄 이펙트 + SphereCollider 활성화) → 자동 소멸.
/// 충돌 판정은 SphereCollider(isTrigger)로 직접 수행한다.
/// OnTriggerStay로 플레이어에게 tickInterval마다 데미지를 준다.
/// </summary>
public class LichDarkRainZone : MonoBehaviour
{
    // ── 외부 주입 ─────────────────────────────────────────
    private float  _radius;
    private float  _telegraphDuration;
    private float  _activeDuration;
    private int    _damagePerTick;
    private float  _tickInterval;

    // ── 런타임 ────────────────────────────────────────────
    private GameObject    _disc;
    private SphereCollider _col;
    private float         _elapsed;
    private bool          _isActive;
    private float         _tickTimer;

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 팩토리
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    /// <summary>
    /// 지정 위치에 DarkRainZone을 생성한다.
    /// </summary>
    public static LichDarkRainZone Spawn(
        Vector3 pos,
        float   radius,
        float   telegraphDuration,
        float   activeDuration,
        int     damagePerTick,
        float   tickInterval)
    {
        var go   = new GameObject("DarkRainZone");
        go.transform.position = pos;
        var zone = go.AddComponent<LichDarkRainZone>();
        zone.Init(radius, telegraphDuration, activeDuration, damagePerTick, tickInterval);
        return zone;
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 초기화
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private void Init(
        float radius,
        float telegraphDuration,
        float activeDuration,
        int   damagePerTick,
        float tickInterval)
    {
        _radius            = radius;
        _telegraphDuration = telegraphDuration;
        _activeDuration    = activeDuration;
        _damagePerTick     = damagePerTick;
        _tickInterval      = tickInterval;

        // SphereCollider — Telegraph 구간에는 비활성, Active 구간에만 켜짐
        _col               = gameObject.AddComponent<SphereCollider>();
        _col.isTrigger     = true;
        _col.radius        = radius;
        _col.enabled       = false;

        // 바닥 디스크 가이드 (Telegraph 색상)
        _disc = LichPatternUtil.PrepareTelegraph(
            PatternGuideHelper.Disc(transform.position, radius, LichPatternUtil.Arcane), LichPatternUtil.Arcane);
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 라이프사이클
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private void Update()
    {
        _elapsed += Time.deltaTime;

        if (!_isActive)
        {
            PatternGuideHelper.SetProgress(_disc, _elapsed / Mathf.Max(0.01f, _telegraphDuration));
            if (_elapsed >= _telegraphDuration)
                ActivateZone();
        }
        else
        {
            _tickTimer += Time.deltaTime;
            if (_elapsed >= _telegraphDuration + _activeDuration)
                Destroy(gameObject);
        }
    }

    private void OnTriggerStay(Collider other)
    {
        if (!_isActive) return;
        if (_tickTimer < _tickInterval) return;
        _tickTimer = 0f;

        var player = other.GetComponentInParent<PlayerController>();
        if (player == null) return;
        player.TakeDamage(_damagePerTick, null, false, HitWeight.Light);   // 장판 틱 — 약(연출 과잉 방지)
    }

    private void OnDestroy()
    {
        if (_disc != null)
            Object.Destroy(_disc);
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 내부 헬퍼
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private void ActivateZone()
    {
        _isActive      = true;
        // 첫 틱은 켜지는 순간 — 0에서 시작하면 활성 시간(0.5)과 틱 간격(0.5)이 같은 순간에 닿아
        // 파괴가 먼저 걸려 피해가 한 번도 안 들어갔다(결계 · 봉인 의식 방해 공격, 09-18 감사).
        _tickTimer     = _tickInterval;
        _col.enabled   = true;
        PatternGuideHelper.SetColor(_disc, LichPatternUtil.Lethal);
        PatternGuideHelper.SetFlow(_disc, LichPatternUtil.Lethal);
        LichVfx.Play(LichVfxSlot.DarkRainImpact, transform.position + Vector3.up * 0.1f, Quaternion.identity, _radius / 1.5f);
        LichSfx.Play(LichSfxSlot.BoltImpact, transform.position, 0.6f);
    }
}
}
