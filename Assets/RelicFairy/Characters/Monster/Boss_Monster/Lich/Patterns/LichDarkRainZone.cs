using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// 암흑 소나기 패턴의 낙하 폭탄 하나.
///
/// 흐름: Telegraph(노란 disc) → Active(빨간 disc + SphereCollider 활성화) → 자동 소멸.
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
        _disc = PatternGuideHelper.Disc(transform.position, radius, PatternGuideHelper.Telegraph);
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 라이프사이클
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private void Update()
    {
        _elapsed += Time.deltaTime;

        if (!_isActive)
        {
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
        player.TakeDamage(_damagePerTick);
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
        _tickTimer     = 0f;
        _col.enabled   = true;
        PatternGuideHelper.SetColor(_disc, PatternGuideHelper.Active);
    }
}
}
