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

    private float _dps;
    private float _burnDuration;
    private float _reapplyTimer;

    /// <summary>불씨 지대 생성. dps = 화상 초당 피해, burnDuration = 부여 화상의 지속(초). 최대 개수 초과 시 가장 오래된 것 Despawn.</summary>
    public static void SpawnAt(GameObject instigator, Vector3 pos, float radius, float life, float dps, float burnDuration)
    {
        if (dps <= 0f) return;
        var f = GroundFieldPool.Spawn<FireField>();
        f._dps          = dps;
        f._burnDuration = Mathf.Max(BURN_TICK, burnDuration);
        f._reapplyTimer = 0f;
        f.Initialize(pos, Mathf.Max(0.1f, radius), life, instigator);

        // 생성 순간 발화 버스트 — 지대 반경과 크기 일치(판정 = 시각).
        ElementVfxPlayer.PlayBurst(RuneElement.Fire, pos, Mathf.Max(0.1f, radius));

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
