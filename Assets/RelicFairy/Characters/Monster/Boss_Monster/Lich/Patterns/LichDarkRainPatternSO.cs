using RelicFairy.UI;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// 암흑 소나기 (Dark Rain) 패턴 — 광역 마법 + 해골 서포트.
///
/// 흐름: Charge(리치 고도 상승 + 해골 소환) → Rain(wavesCount 웨이브, 각 웨이브마다 zonesPerWave개
///       DarkRainZone 동시 생성) → Recovery → ChaseState.
///
/// 각 존은 telegraphDuration 동안 노란 disc를 보여주고, activeDuration 동안 충돌 판정(빨간 disc)으로 전환.
/// 존이 없는 구역이 안전지대다.
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/Boss/Lich/Lich_DarkRainPattern", fileName = "Lich_DarkRainPattern")]
public class LichDarkRainPatternSO : BossPatternSO
{
    [Header("Dark Rain — Timing")]
    [Tooltip("리치가 상승하며 해골을 소환하는 준비 시간 (초)")]
    public float chargeDuration = 2f;
    [Tooltip("웨이브 수")]
    public int waveCount = 3;
    [Tooltip("웨이브 간격 (초). telegraphDuration + activeDuration보다 크거나 같아야 자연스럽다.")]
    public float wavePeriod = 4f;
    [Tooltip("패턴 완료 후 복귀 대기 시간 (초)")]
    public float recoveryDuration = 1f;

    [Header("Dark Rain — Zone")]
    [Tooltip("웨이브당 생성할 폭탄 존 수")]
    public int zonesPerWave = 5;
    [Tooltip("존 생성 최소 반경 (m)")]
    public float minSpread = 2f;
    [Tooltip("존 생성 최대 반경 (m)")]
    public float maxSpread = 9f;
    [Tooltip("각 폭탄 존 반경 (m)")]
    public float zoneRadius = 1.5f;
    [Tooltip("존 출현 후 빨간색으로 전환되기까지 시간 (초) — 플레이어 피할 시간")]
    public float telegraphDuration = 1.5f;
    [Tooltip("빨간 존(실제 판정) 유지 시간 (초)")]
    public float activeDuration = 1.2f;
    [Tooltip("바닥 기준 Y 오프셋 (바닥보다 약간 위로)")]
    public float groundYOffset = 0.05f;

    [Header("Dark Rain — Damage")]
    [Tooltip("공격력 대비 틱 데미지 배율")]
    public float damageMultiplier = 0.4f;
    [Tooltip("틱 간격 (초). 존 안에 있는 동안 이 간격마다 데미지를 받는다.")]
    public float tickInterval = 0.5f;

    [Header("Dark Rain — Skeletons")]
    [Tooltip("Charge 단계에서 소환할 해골 프리팹. null이면 소환 없이 진행.")]
    public GameObject skeletonPrefab;
    [Tooltip("Charge 단계에서 소환할 해골 수")]
    public int skeletonCount = 3;
    [Tooltip("해골 소환 반경 (m)")]
    public float skeletonSpawnRadius = 5f;

    [Header("Dark Rain — Cooldown")]
    [Tooltip("패턴 완료 후 재사용 대기 시간 (초)")]
    public float patternCooldown = 25f;

    // ── 런타임 ───────────────────────────────────────────
    private LichDarkRainState _state;

    public override void Initialize(BossPatternContext ctx) => _state = new LichDarkRainState(this);
    public override void OnRecycled()                       => _state = new LichDarkRainState(this);

    public override bool CanExecute(BossPatternContext ctx)
    {
        if (ctx.Ctx.Runtime.PlayerTarget == null) return false;
        var lichBB = (ctx.Boss as LichMonster)?.LichBB;
        return lichBB == null || lichBB.DarkRainCooldown <= 0f;
    }

    public override SpecialStateBase GetRuntimeState() => _state;
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// LichDarkRainState — UnInterruptible (이동 잠금)
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

public class LichDarkRainState : UnInterruptibleState<LichDarkRainPatternSO>
{
    private enum Phase { Charge, Rain, Recovery }

    private Phase _phase;
    private float _phaseTimer;
    private int   _wavesSpawned;

    public LichDarkRainState(LichDarkRainPatternSO data) : base(data) { }

    public override void Enter(MonsterContext ctx)
    {
        _phase       = Phase.Charge;
        _phaseTimer  = 0f;
        _wavesSpawned = 0;

        ctx.Animator?.CrossFade("ArcaneOrb", 0.1f); // Phase2 전용 — ElementalBarrage와 구분

        var mc = (ctx.Monster as LichMonster)?.MovementController;
        mc?.RequestMovementState(LichMovementState.AltitudeRise);
        mc?.SetLocked(true);

        UI_BossBark.Show("암흑이여, 쏟아져라!", BossBarkType.PatternAnnounce);
        Debug.Log($"[DarkRain] 시작 — {Data.waveCount}웨이브 × {Data.zonesPerWave}존, telegraph {Data.telegraphDuration}s → active {Data.activeDuration}s");

        SpawnSkeletons(ctx);
    }

    public override void Update(MonsterContext ctx)
    {
        _phaseTimer += Time.deltaTime;

        switch (_phase)
        {
            case Phase.Charge:
                if (_phaseTimer >= Data.chargeDuration)
                {
                    _phase      = Phase.Rain;
                    _phaseTimer = 0f;
                    Debug.Log($"[DarkRain] 웨이브 1 시작");
                    SpawnWave(ctx);
                    _wavesSpawned = 1;
                }
                break;

            case Phase.Rain:
                if (_wavesSpawned < Data.waveCount && _phaseTimer >= _wavesSpawned * Data.wavePeriod)
                {
                    Debug.Log($"[DarkRain] 웨이브 {_wavesSpawned + 1} 시작");
                    SpawnWave(ctx);
                    _wavesSpawned++;
                }
                if (_phaseTimer >= Data.waveCount * Data.wavePeriod)
                {
                    _phase      = Phase.Recovery;
                    _phaseTimer = 0f;
                    Debug.Log("[DarkRain] 전체 웨이브 완료 → Recovery");
                }
                break;

            case Phase.Recovery:
                if (_phaseTimer >= Data.recoveryDuration)
                    ctx.Monster.ChangeState<ChaseState>();
                break;
        }
    }

    public override void Exit(MonsterContext ctx)
    {
        var mc = (ctx.Monster as LichMonster)?.MovementController;
        mc?.RequestMovementState(LichMovementState.AltitudeDescend);
        mc?.SetLocked(false);

        var lich = ctx.Monster as LichMonster;
        if (lich?.LichBB != null)
            lich.LichBB.DarkRainCooldown = Data.patternCooldown;
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 헬퍼
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private void SpawnWave(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null) return;

        Vector3 playerPos = ctx.Runtime.PlayerTarget.position;
        int     dmg       = Mathf.Max(1, (int)(ctx.Config.stat.attackPower * Data.damageMultiplier));
        float   angleStep = 360f / Data.zonesPerWave;

        for (int i = 0; i < Data.zonesPerWave; i++)
        {
            float   angle  = i * angleStep + Random.Range(-angleStep * 0.4f, angleStep * 0.4f);
            float   dist   = Random.Range(Data.minSpread, Data.maxSpread);
            Vector3 offset = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * dist;
            Vector3 pos    = playerPos + offset;
            pos.y          = playerPos.y + Data.groundYOffset;

            LichDarkRainZone.Spawn(pos, Data.zoneRadius, Data.telegraphDuration, Data.activeDuration,
                dmg, Data.tickInterval);
        }
    }

    private void SpawnSkeletons(MonsterContext ctx)
    {
        if (Data.skeletonPrefab == null || Data.skeletonCount <= 0) return;

        Vector3 origin    = ctx.Transform.position;
        float   angleStep = 360f / Data.skeletonCount;

        for (int i = 0; i < Data.skeletonCount; i++)
        {
            float   angle  = i * angleStep;
            Vector3 offset = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * Data.skeletonSpawnRadius;
            Vector3 pos    = origin + offset;
            pos.y          = origin.y;
            Object.Instantiate(Data.skeletonPrefab, pos, Quaternion.identity);
        }
    }
}
}
