using System.Collections.Generic;
using RelicFairy.Monster;
using UnityEngine;

/// <summary>
/// 불씨 지대 — GroundFieldBase(풀링/수명/추적) 재사용. 반경 내 살아있는 적에게 주기적으로 화상을 부여한다.
/// 독안개 장판(PoisonField)의 화염판. 가웨인 파츠(정오의 개화·잔염의 검흔)가 스폰한다.
/// </summary>
public sealed class FireField : GroundFieldBase
{
    private const int   MAX_FIELDS = 5;
    private const float REAPPLY    = 0.5f;   // 화상 재부여 간격
    private const float BURN_TICK  = 0.5f;

    private static readonly List<FireField> s_fields = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ClearList() => s_fields.Clear();

    protected override RuneElement? FieldElement => RuneElement.Fire;

    private string _pool;   // 상한을 같이 세는 묶음(null = 전역) — 조각이 만든 불씨는 따로 센다
    private float _dps;
    private float _burnDuration;
    private float _reapplyTimer;

    /// <summary>
    /// 불씨 지대 생성. dps = 화상 초당 피해, burnDuration = 부여 화상의 지속(초).
    /// pool이 같은 것끼리 cap개를 넘으면 가장 오래된 것 Despawn(null = 전역 5개). 조각이 「최대 N」을 약속하면 자기 묶음으로 부른다(10-12 B10).
    /// </summary>
    public static FireField SpawnAt(GameObject instigator, Vector3 pos, float radius, float life, float dps, float burnDuration,
                                    string pool = null, int cap = MAX_FIELDS)
    {
        if (dps <= 0f) return null;
        var f = GroundFieldPool.Spawn<FireField>();
        f._pool         = pool;
        f._dps          = dps;
        f._burnDuration = Mathf.Max(BURN_TICK, burnDuration);
        f._reapplyTimer = 0f;
        f.Initialize(pos, Mathf.Max(0.1f, radius), life, instigator);

        // 생성 순간 발화 버스트 — 지대 반경과 크기 일치(판정 = 시각).
        ElementVfxPlayer.PlayBurst(RuneElement.Fire, pos, Mathf.Max(0.1f, radius));

        if (!s_fields.Contains(f)) s_fields.Add(f);
        EvictOldest(pool, Mathf.Max(1, cap));
        return f;
    }

    /// <summary>같은 묶음을 새것부터 세어 cap을 넘는 오래된 것을 끈다(끄면 OnDisable이 목록에서 뺀다 — 뒤에서부터 돈다).</summary>
    private static void EvictOldest(string pool, int cap)
    {
        int n = 0;
        for (int i = s_fields.Count - 1; i >= 0; i--)
        {
            if (i >= s_fields.Count || s_fields[i]._pool != pool) continue;
            if (++n > cap) s_fields[i].Despawn();
        }
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        s_fields.Remove(this);
    }

    protected override void OnFieldTick(float dt)
    {
        _reapplyTimer -= dt;
        if (_reapplyTimer > 0f) return;
        _reapplyTimer = REAPPLY;

        QueryEnemies(16);
        for (int i = 0; i < _enemyBuffer.Count; i++)
        {
            var mb = _enemyBuffer[i];
            if (mb == null) continue;
            MonsterBurnHandler.Apply(mb.gameObject, _dps, _burnDuration, BURN_TICK, _instigator);
        }
    }

    protected override void OnExpire()
    {
        s_fields.Remove(this);
    }
}
