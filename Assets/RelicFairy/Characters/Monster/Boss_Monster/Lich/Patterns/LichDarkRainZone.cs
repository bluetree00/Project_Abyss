using System.Collections.Generic;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// 암흑 소나기 패턴의 낙하 폭탄 하나.
///
/// 흐름: Telegraph(보라 disc가 차오름) → Active(빨간 disc + 착탄 이펙트) → 자동 소멸.
/// 판정은 수평 거리 — 그린 원 반경 그대로(리치 공용 판정과 같다). Active 동안 원 안의 플레이어에게 tickInterval마다 데미지를 준다.
/// (10-08 점검: 3D 구 트리거는 그린 원보다 플레이어 몸 반경만큼 컸고, 해골 같은 다른 몸이 들어와도 틱을 써 버렸다)
/// 리치 사망 · 페이지 전환 · 전투 종료 때 <see cref="ClearAll"/>로 거둔다(<see cref="LichHazards.Clear"/>가 부른다).
/// </summary>
public class LichDarkRainZone : MonoBehaviour
{
    private const float SignalSeconds = 0.15f;   // 켜지기 전 빨강 신호(리치 예고 규약)

    private static readonly List<LichDarkRainZone> s_live = new();

    // ── 외부 주입 ─────────────────────────────────────────
    private PlayerController _player;     // 판정 대상
    private GameObject       _attacker;   // 피해의 주인 = 리치(저스트 회피 · 결과창 「~에게」 · 반사가 읽는다, 10-08 점검)
    private float  _radius;
    private float  _telegraphDuration;
    private float  _activeDuration;
    private int    _damagePerTick;
    private float  _tickInterval;

    // ── 런타임 ────────────────────────────────────────────
    private GameObject    _disc;
    private float         _elapsed;
    private bool          _isActive;
    private bool          _signaled;
    private float         _tickTimer;

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 팩토리
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    /// <summary>
    /// 지정 위치에 DarkRainZone을 생성한다.
    /// </summary>
    public static LichDarkRainZone Spawn(
        MonsterContext ctx,
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
        zone.Init(ctx, radius, telegraphDuration, activeDuration, damagePerTick, tickInterval);
        s_live.Add(zone);
        return zone;
    }

    /// <summary>모두 거둔다 — 리치 사망 · 페이지 전환 · 전투 종료(10-08 점검: 남아 있다가 터졌다).</summary>
    public static void ClearAll()
    {
        for (int i = s_live.Count - 1; i >= 0; i--)
            if (s_live[i] != null) Destroy(s_live[i].gameObject);
        s_live.Clear();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => s_live.Clear();

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 초기화
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private void Init(
        MonsterContext ctx,
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
        _player            = ctx.Runtime.CachedPlayer;
        if (_player == null && ctx.Runtime.PlayerTarget != null) ctx.Runtime.PlayerTarget.TryGetComponent(out _player);
        _attacker          = ctx.Monster != null ? ctx.Monster.gameObject : null;

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
            LichPatternUtil.TickTelegraph(_disc, _elapsed, _telegraphDuration, SignalSeconds, ref _signaled);   // 끝 0.15초 빨강 — 피해 프레임에야 빨강이었다(10-13 검증)
            if (_elapsed >= _telegraphDuration)
                ActivateZone();
        }
        else
        {
            _tickTimer += Time.deltaTime;
            if (_elapsed >= _telegraphDuration + _activeDuration)
            {
                Destroy(gameObject);
                return;
            }
            TryTick();
        }
    }

    private void OnDestroy()
    {
        s_live.Remove(this);
        if (_disc != null)
            Object.Destroy(_disc);
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 내부 헬퍼
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    /// <summary>틱 간격이 찼고 플레이어가 원 안(수평 거리)이면 피해 — 틱은 플레이어를 판정했을 때만 쓴다(10-08 점검).</summary>
    private void TryTick()
    {
        if (_tickTimer < _tickInterval || _player == null) return;
        if (LichPatternUtil.FlatDistance(transform.position, _player.transform.position) > _radius) return;
        _tickTimer = 0f;
        _player.TakeDamage(_damagePerTick, _attacker, false, HitWeight.Light);   // 장판 틱 — 약(연출 과잉 방지)
    }

    private void ActivateZone()
    {
        _isActive      = true;
        // 첫 틱은 켜지는 순간 — 0에서 시작하면 활성 시간(0.5)과 틱 간격(0.5)이 같은 순간에 닿아
        // 파괴가 먼저 걸려 피해가 한 번도 안 들어갔다(결계 · 봉인 의식 방해 공격, 09-18 감사).
        _tickTimer     = _tickInterval;
        PatternGuideHelper.SetColor(_disc, LichPatternUtil.Lethal);
        PatternGuideHelper.SetFlow(_disc, LichPatternUtil.Lethal);
        LichVfx.Play(LichVfxSlot.DarkRainImpact, transform.position + Vector3.up * 0.1f, Quaternion.identity, _radius / 1.5f);
        LichSfx.Play(LichSfxSlot.BoltImpact, transform.position, 0.6f);
    }
}
}
