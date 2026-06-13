using System.Collections.Generic;
using UnityEngine;
using RelicFairy.Monster;

// ═══════════════════════════════════════════════════════════════════════════
// ✦ 빛 Light — 치명타 루프 (4단계 누적, 리소스 + 장판)
//
// 공용 시스템 첫 사용: FD 장판 실구현(성역·빛장판), RS 게이지+임계콜백, SL Crit 채널, AQ Cone.
// OnCrit 훅은 디스패처 NotifyHit(report.IsCrit)로 근/원 공통 배선됨 — 별도 작업 없음.
//
// 데이터 키(실데이터): LightRadiance / LightBurst / LightSanctuary / LightField. threshold=점유수(6/10/14/20).
// 광채 스택 = RS 게이지 "LightRadiance"(비감쇠, 광폭발이 소모). LightRadiance(1단계)가 SL Crit 합산 소유자.
// ═══════════════════════════════════════════════════════════════════════════

public abstract class LightRuneEffectBase : RuneElementEffectBase
{
    protected const string KEY_STACK    = "LightRadiance"; // 광채 스택(게이지)
    protected const string KEY_FIELD_CC = "LightFieldCC";  // 빛장판 위 치확 기여(라이브)
    protected const string KEY_FIELD_CD = "LightFieldCD";  // 빛장판 위 치피 기여
    protected const string KEY_ON_FIELD = "LightOnField";  // 플레이어가 빛장판 안인지(1/0)
}

/// <summary>
/// 1단계 광채 — 치명타 시 광채 스택+1(최대10), 스택당 치확 +1%(SL). 빛장판 위에선 +3(성역 활성 시).
/// SL Crit 채널의 합산 소유자: 스택 치확 + 빛장판(4단계) 치확/치피를 매 프레임 반영.
/// value=스택당치확(0.01), max_stack=10.
/// </summary>
public sealed class LightRadianceEffect : LightRuneEffectBase
{
    public override void OnCrit(in HitInfo hit, PlayerController player)
    {
        var res = Res(player);
        if (res == null) return;

        int gain = 1;
        var disp = player.RuneEffects;
        if (res.GetRegister(KEY_ON_FIELD) == 1 && disp != null && disp.IsActive("LightSanctuary"))
        {
            float v2 = disp.GetActive("LightSanctuary")?.Entry?.value2 ?? 3f;
            gain = v2 > 0f ? (int)v2 : 3;
        }

        int max = Entry.max_stack > 0 ? Entry.max_stack : 10;
        res.AddGauge(KEY_STACK, gain, max);   // 임계 도달 시 광폭발(2단계가 RegisterThreshold)
    }

    public override void Tick(float dt, PlayerController player)
    {
        var res   = Res(player);
        var stats = player?.RuntimeStats;
        if (res == null || stats == null) return;

        int stacks = res.GetStack(KEY_STACK);
        float cc = stacks * Entry.value + res.GetFloat(KEY_FIELD_CC);
        float cd = res.GetFloat(KEY_FIELD_CD);
        stats.SetSynergyDynamicCritChance(cc);
        stats.SetSynergyDynamicCritDamage(cd);
    }

    public override void OnDeactivate()
    {
        var stats = CachedPlayer?.RuntimeStats;
        if (stats != null)
        {
            stats.SetSynergyDynamicCritChance(0f);
            stats.SetSynergyDynamicCritDamage(0f);
        }
        CachedPlayer?.RuneEffects?.Resources?.RemoveSlot(KEY_STACK);   // 광채 게이지 잔존 방지
    }
}

/// <summary>
/// 2단계 광폭발 — 광채 10스택 도달 시(RS 임계콜백) 전방 원뿔 공격력 150% 폭발(AQ Cone + DM), 스택 소모.
/// 성역(3단계) 활성 시 폭발 자리에 빛 장판 생성(빛장판=4단계 활성 시 추적·치확/치피 부여).
/// value=폭발배율(1.5), value2=소모 임계(10).
/// </summary>
public sealed class LightBurstEffect : LightRuneEffectBase
{
    private const float CONE_RANGE      = 7f;
    private const float CONE_HALF_ANGLE = 50f;
    private const float FIELD_RADIUS    = 3f;
    private readonly List<MonsterBase> _buf = new();

    public override void OnActivate(PlayerController player)
    {
        base.OnActivate(player);
        var res = Res(player);
        if (res == null) return;
        int at = Entry.value2 > 0f ? (int)Entry.value2 : 10;
        res.RegisterThreshold(KEY_STACK, at, () => DoBurst(player));
    }

    private void DoBurst(PlayerController player)
    {
        var res = Res(player);
        if (res == null) return;

        int atk = GetEffectiveAttack(player);
        float dmg = Entry.value * atk;
        Vector3 origin = player.transform.position;
        Vector3 fwd    = player.transform.forward;

        int n = CombatQuery.GetEnemiesInCone(origin, fwd, CONE_RANGE, CONE_HALF_ANGLE, 16, _buf);
        for (int i = 0; i < n; i++)
            CombatQuery.DealSynergyDamage(_buf[i], dmg, player.gameObject);

        res.Consume(KEY_STACK);

        // 성역(3단계): 폭발 자리 빛 장판
        var disp = player.RuneEffects;
        if (disp != null && disp.IsActive("LightSanctuary"))
        {
            float life = disp.GetActive("LightSanctuary")?.Entry?.duration ?? 3f;
            bool  follow = disp.IsActive("LightField");   // 4단계: 추적 + 치확/치피
            float cc = 0f, cd = 0f;
            if (follow)
            {
                var lf = disp.GetActive("LightField")?.Entry;
                cc = lf?.value  ?? 0.2f;
                cd = lf?.value2 ?? 0.3f;
            }
            LightField.SpawnSingleton(player, origin, FIELD_RADIUS, life, cc, cd, follow);
        }
    }

    public override void OnDeactivate()
    {
        // 단계 하강 시 임계 콜백 해제 — 1단계(광채)가 남아 게이지를 채워도 stale 광폭발이 발동하지 않도록.
        CachedPlayer?.RuneEffects?.Resources?.RemoveThreshold(KEY_STACK);
    }
}

/// <summary>3단계 성역 — 마커. 광폭발이 빛 장판을 생성하도록 활성화하고, 장판 위 치명 시 광채 +value2(3). 본문 무.</summary>
public sealed class LightSanctuaryEffect : LightRuneEffectBase { }

/// <summary>4단계 빛의 장판 — 마커. 광폭발 장판이 플레이어를 추적하고 치확+value(0.2)·치피+value2(0.3) 부여. 본문 무.</summary>
public sealed class LightFieldEffect : LightRuneEffectBase { }

// ── FD 본구현: 빛 장판 ──────────────────────────────────────────────────────

/// <summary>
/// 빛 장판(FD 첫 실구현). 최대 1개(SpawnSingleton 교체), 풀링(GroundFieldPool), 추적(SetFollow).
/// 체류 시 RS에 치확/치피(빛의장판) + 체류 플래그(성역 광채+3 판정)를 게시. 합산은 LightRadiance가 SL로 반영.
/// 풀(독안개)이 이 패턴(풀링·추적·겹침 레지스트리)을 그대로 재사용한다.
/// </summary>
public sealed class LightField : GroundFieldBase
{
    private static LightField s_current;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ClearSingleton() => s_current = null;

    private PlayerController _player;
    private float _bonusCC;
    private float _bonusCD;

    /// <summary>최대 1개 — 기존 장판은 즉시 종료 후 새로 생성.</summary>
    public static void SpawnSingleton(PlayerController player, Vector3 pos, float radius, float life,
                                      float bonusCC, float bonusCD, bool follow)
    {
        if (s_current != null) s_current.Despawn();

        var f = GroundFieldPool.Spawn<LightField>();
        f._player  = player;
        f._bonusCC = bonusCC;
        f._bonusCD = bonusCD;
        f.Initialize(pos, radius, life, player != null ? player.gameObject : null);
        if (follow && player != null) f.SetFollow(player.transform);
        s_current = f;
    }

    protected override void OnFieldTick(float dt)
    {
        var res = _player != null ? _player.RuneEffects?.Resources : null;
        if (res == null) return;

        bool inside = (_player.transform.position - Center).sqrMagnitude <= _radius * _radius;
        res.SetRegister(LightFieldKeys.OnField, inside ? 1 : 0);
        res.SetFloat(LightFieldKeys.Cc, inside ? _bonusCC : 0f);
        res.SetFloat(LightFieldKeys.Cd, inside ? _bonusCD : 0f);
    }

    protected override void OnExpire()
    {
        var res = _player != null ? _player.RuneEffects?.Resources : null;
        if (res != null)
        {
            res.SetRegister(LightFieldKeys.OnField, 0);
            res.SetFloat(LightFieldKeys.Cc, 0f);
            res.SetFloat(LightFieldKeys.Cd, 0f);
        }
        if (s_current == this) s_current = null;
        _player = null;
    }
}

/// <summary>LightField ↔ Light 효과가 공유하는 RS 키(LightRuneEffectBase 상수와 동일 값).</summary>
internal static class LightFieldKeys
{
    public const string OnField = "LightOnField";
    public const string Cc      = "LightFieldCC";
    public const string Cd      = "LightFieldCD";
}
