using System.Collections.Generic;
using UnityEngine;
using RelicFairy.Monster;

// ═══════════════════════════════════════════════════════════════════════════
// ☘ 풀 Grass — 독안개 장판 (4단계 누적, 필드 중심)
//
// FD(GroundFieldBase 풀링/추적/겹침 레지스트리) + ST DoT를 그대로 재사용 — 신규 인프라 최소.
// 추가분: 적 공격 디버프(ApplyAttackSlow) 일반화(얼음/어둠 재사용).
//
// 데이터 키(실데이터): GrassMist / GrassMistPlus / GrassMistInsight / GrassMistDominion. threshold=점유수(6/10/14/20).
// 독 상태 id="poison". 1단계만 장판을 스폰하고, 2~4단계는 장판 동작을 조정하는 마커.
// ═══════════════════════════════════════════════════════════════════════════

public abstract class GrassRuneEffectBase : RuneElementEffectBase { }

/// <summary>
/// 1단계 살포 — 적중 자리에 독안개 장판(최대3, 10초). 장판이 내부 적에 초당 공4% 독 DoT.
/// 2~4단계 활성 여부를 읽어 장판 설정(크기/겹침/보호막/디버프/병합/보스받뎀)을 구성한다.
/// value=독비율(0.04), duration=장판수명(10).
/// </summary>
public sealed class GrassMistEffect : GrassRuneEffectBase
{
    private const float SPAWN_CD = 0.5f;
    private float _cd;

    public override void Tick(float dt, PlayerController player)
    {
        if (_cd > 0f) _cd -= dt;
    }

    public override void OnHit(in HitInfo hit, PlayerController player)
    {
        if (hit.Target == null || _cd > 0f) return;
        _cd = SPAWN_CD;
        PoisonField.SpawnAt(player, hit.Target.transform.position, BuildConfig(player));
    }

    private PoisonField.Config BuildConfig(PlayerController player)
    {
        var disp = player.RuneEffects;
        var c = new PoisonField.Config
        {
            perTick = Entry.value * GetEffectiveAttack(player),
            life    = Entry.duration > 0f ? Entry.duration : 10f,
            radiusMult = 1f,
            dotMult    = 1f,
        };

        var plusE     = disp?.GetActive("GrassMistPlus")?.Entry;
        var insightE  = disp?.GetActive("GrassMistInsight")?.Entry;
        var dominionE = disp?.GetActive("GrassMistDominion")?.Entry;
        bool plus     = plusE     != null;
        bool insight  = insightE  != null;
        bool dominion = dominionE != null;

        c.radiusMult   = 1f + (plus ? plusE.value : 0f) + (insight ? insightE.value : 0f);     // +50%씩
        c.overlapBonus = plus ? plusE.value2 : 0f;                                              // 겹침 독피해 +50%
        c.dotMult      = dominion ? dominionE.value : 1f;                                       // 지배 독×2
        c.shieldPctPerSec = dominion ? dominionE.value2 : (insight ? insightE.value2 : 0f);     // 초당 보호막 0.1%/0.2%
        c.atkDebuff    = (insight ? insightE.value3 : 0f) + (dominion ? 0.3f : 0f);             // 공-20% + 집중분산 근사
        c.bossAmp      = dominion ? dominionE.value3 : 0f;                                      // 보스 받뎀 +15%
        c.mergeAll     = dominion;
        return c;
    }
}

/// <summary>2단계 강화 — 마커. 장판 크기 +50%, 겹침 영역 독피해 +50%(장판이 Config로 읽음).</summary>
public sealed class GrassMistPlusEffect : GrassRuneEffectBase { }

/// <summary>3단계 간파 — 마커. 크기 +50%, 안개 안에서 초당 보호막 0.1%, 겹침 시 적 공-20%.</summary>
public sealed class GrassMistInsightEffect : GrassRuneEffectBase { }

/// <summary>4단계 독무 지배 — 마커. 장판 닿으면 전체 겹침판정, 독×2, 초당 보호막 0.2%, 집중분산, 보스 받뎀 +15%.</summary>
public sealed class GrassMistDominionEffect : GrassRuneEffectBase { }

// ── FD 재사용: 독안개 장판 ───────────────────────────────────────────────────

/// <summary>
/// 독안개 장판 — GroundFieldBase(풀링/수명/겹침 레지스트리) + ST DoT 재사용.
/// 최대 3개(s_fields 개수관리, 초과 시 가장 오래된 것 Despawn). 매 1초 내부 적에 poison DoT 재적용.
/// 겹침 판정: 일반=한 점을 2개 이상 장판이 덮음 / 지배=이 장판이 다른 장판과 닿으면 전체 겹침.
/// </summary>
public sealed class PoisonField : GroundFieldBase
{
    public struct Config
    {
        public float perTick;     // 틱당 독 피해(공격력 반영 후)
        public float life;        // 장판 수명
        public float radiusMult;  // 크기 배율
        public float overlapBonus;// 겹침 영역 독피해 가산(+0.5)
        public float dotMult;     // 독피해 배율(지배 ×2)
        // 예전엔 healPct — <b>독피해가 들어갈 때마다</b> 최대HP 비율만큼 회복했다.
        // 흡혈·회복 계열은 이 프로젝트가 두지 않는 것이고("적에게서 가져오는가"가 판정 기준),
        // 게다가 적 1마리마다 콜백이 돌아 <b>적이 많을수록 회복이 배로 늘어나는</b> 의도 밖 배율이 있었다.
        // 지금은 "내 안개 안에 서 있다"는 자기 조건으로만 서는 보호막이다 — 적은 관여하지 않는다.
        public float shieldPctPerSec;  // 장판 안에 있는 동안 초당 보호막(최대HP 비율)
        public float atkDebuff;   // 겹침 시 적 공격 디버프
        public float bossAmp;     // 보스 받는피해 증폭
        public bool  mergeAll;    // 닿은 장판 전체 겹침 판정
    }

    private const int   MAX_FIELDS   = 3;
    private const float BASE_RADIUS  = 2.5f;
    private const float REAPPLY      = 1f;
    private const float DOT_INTERVAL = 1f;
    private const int   DOT_TICKS    = 2;     // 체류 이탈 후에도 ~2초 잔류, 매초 top-up 재적용
    private const float DEBUFF_DUR   = 3f;

    private static readonly List<PoisonField> s_fields = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ClearList() => s_fields.Clear();

    protected override RuneElement? FieldElement => RuneElement.Grass;

    private PlayerController _player;
    private Config _cfg;
    private float  _reapplyTimer;

    /// <summary>최대 3개 — 초과 시 가장 오래된 장판을 Despawn.</summary>
    public static void SpawnAt(PlayerController player, Vector3 pos, Config cfg)
    {
        var f = GroundFieldPool.Spawn<PoisonField>();
        f._player     = player;
        f._cfg        = cfg;
        f._reapplyTimer = 0f;
        f.Initialize(pos, BASE_RADIUS * Mathf.Max(0.1f, cfg.radiusMult), cfg.life,
                     player != null ? player.gameObject : null);

        if (!s_fields.Contains(f)) s_fields.Add(f);
        while (s_fields.Count > MAX_FIELDS) s_fields[0].Despawn();
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        s_fields.Remove(this);
    }

    protected override void OnFieldTick(float dt)
    {
        if (_player == null) return;

        _reapplyTimer -= dt;
        if (_reapplyTimer > 0f) return;
        _reapplyTimer = REAPPLY;

        GrantShieldIfInside();

        QueryEnemies(16);
        for (int i = 0; i < _enemyBuffer.Count; i++)
        {
            var mb = _enemyBuffer[i];
            if (mb == null) continue;

            bool overlap = _cfg.mergeAll
                ? FieldTouchesAnother()
                : PoisonFieldsContaining(mb.transform.position) >= 2;

            float perTick = _cfg.perTick * _cfg.dotMult * (overlap ? 1f + _cfg.overlapBonus : 1f);
            mb.Status.ApplyDot("poison", perTick, DOT_INTERVAL, DOT_TICKS, _player.gameObject, 1f, null, null);

            if (_cfg.atkDebuff > 0f && overlap)
                mb.Status.ApplyAttackSlow("poison_atk", _cfg.atkDebuff, DEBUFF_DUR);

            if (_cfg.bossAmp > 0f && mb is IBoss)
                mb.ApplyDamageTakenAmp(_cfg.bossAmp, DEBUFF_DUR, "vulnerable");
        }
    }

    protected override void OnExpire()
    {
        s_fields.Remove(this);
        _player = null;
    }

    /// <summary>내 안개 안에 서 있는 동안 초당 보호막. 적 상태·피해와 무관한 자기 조건이다.</summary>
    private void GrantShieldIfInside()
    {
        if (_player == null || _cfg.shieldPctPerSec <= 0f) return;
        float r = Radius;
        if ((Center - _player.transform.position).sqrMagnitude > r * r) return;
        _player.RuntimeStats.AddShield(_player.RuntimeStats.MaxHp * _cfg.shieldPctPerSec);
    }

    private bool FieldTouchesAnother()
    {
        for (int i = 0; i < s_fields.Count; i++)
            if (Overlaps(s_fields[i])) return true;
        return false;
    }

    private int PoisonFieldsContaining(Vector3 pos)
    {
        int count = 0;
        for (int i = 0; i < s_fields.Count; i++)
        {
            var f = s_fields[i];
            if (f == null) continue;
            float r = f.Radius;
            if ((f.Center - pos).sqrMagnitude <= r * r) count++;
        }
        return count;
    }
}
