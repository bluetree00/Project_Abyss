using UnityEngine;


namespace Abyss.Monster
{
/// <summary>
/// 몬스터 인스턴스별 가변 런타임 상태.
/// SO에는 담을 수 없는 "지금 이 몬스터의 현재 상태"를 보관.
/// </summary>
public class MonsterRuntimeData
{
    // ── 체력 ──────────────────────────────────────────────
    public int  CurrentHp;
    public bool IsDead;

    // ── 원소 누적치 (인덱스: ElementType) ─────────────────
    /// <summary>원소별 현재 누적치. 인덱스는 ElementType.ToIndex() 사용.</summary>
    public float[] ElementAccumulation = new float[ElementTypeUtil.Count];

    // ── 타깃 ──────────────────────────────────────────────
    /// <summary>현재 추적 중인 플레이어 Transform.</summary>
    public Transform PlayerTarget;

    /// <summary>PlayerTarget 의 PlayerController 캐시. GetComponent 반복 방지.</summary>
    public PlayerController CachedPlayer;

    /// <summary>이번 Update에서 계산된 몬스터 → 플레이어 거리. 각 상태에서 재계산 없이 사용.</summary>
    public float DistToPlayer = float.MaxValue;

    // ── 위치 ──────────────────────────────────────────────
    /// <summary>스폰(배치) 위치 — 귀환 기준점.</summary>
    public Vector3 SpawnPosition;

    // ── 공통 상태 타이머 ───────────────────────────────────
    /// <summary>현재 상태에서 범용으로 사용하는 카운트다운 타이머.</summary>
    public float StateTimer;

    // ── 배회 ──────────────────────────────────────────────
    /// <summary>배회 방향 (1 = 오른쪽/앞, -1 = 왼쪽/뒤).</summary>
    public int   PatrolDirection     = 1;
    /// <summary>웨이포인트 대기 중 여부.</summary>
    public bool  IsWaitingAtWaypoint;
    /// <summary>웨이포인트 대기 카운트다운.</summary>
    public float PatrolWaitTimer;

    // ── 공격 ──────────────────────────────────────────────
    /// <summary>이번 Attack 상태에서 데미지를 이미 줬는지.</summary>
    public bool AttackHitDealt;
    /// <summary>이번 조우에서 첫 번째 공격인지. true면 AttackReady 딜레이 없이 즉시 공격.</summary>
    public bool IsFirstAttack = true;

    // ── 특수 상태 배율 ─────────────────────────────────────
    /// <summary>이동 속도 배율. 광폭화 등 영구 버프에 사용. 기본값 1.</summary>
    public float SpeedMultiplier  = 1f;
    /// <summary>공격력 배율. 광폭화 등 영구 버프에 사용. 기본값 1.</summary>
    public float AttackMultiplier = 1f;
    /// <summary>받는 데미지 배율. 방어 상태 등에서 임시 감소에 사용. 기본값 1.</summary>
    public float DamageMultiplier = 1f;
}
}
