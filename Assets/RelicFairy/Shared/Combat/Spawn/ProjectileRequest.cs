using UnityEngine;

/// <summary>
/// 투사체 1회 발사 요청. <see cref="CombatSpawner"/>가 이 구조체를 수정자 계층에 통과시킨 뒤
/// 실제 투사체를 만든다. <b>struct + ref 전달</b>이라 공격마다 힙 할당이 생기지 않는다.
///
/// 필드는 두 종류다:
///  · <b>원본</b> — 호출자(어빌리티·스킬)가 채운다. 무엇을 어디로 쏘는가.
///  · <b>변형</b> — 수정자 계층(파츠·아이템·버프)이 누적해 올린다. 몇 발을·얼마나 세게·어떻게.
/// </summary>
public struct ProjectileRequest
{
    // ── 원본 ────────────────────────────────────────────────
    /// <summary>추가 투사체를 뽑을 풀 키. 주 투사체가 이미 스폰돼 있어도 갈래를 늘리려면 필요하다.</summary>
    public string     prefabKey;
    public Vector3    origin;
    public Vector3    direction;
    public float      damage;
    public GameObject owner;
    /// <summary>추가 투사체 스케일(어빌리티 EffectStep.scaleMultiplier). sizeMult가 여기에 곱해진다.</summary>
    public float      baseScale;

    // ── 변형(수정자가 채운다) ───────────────────────────────
    /// <summary>총 발사 수(주 투사체 포함). <b>분열 파츠</b>·아이템 추가 투사체가 올린다.</summary>
    public int   count;
    /// <summary>부채꼴 전체 확산각(도). 갈래 사이 간격은 count로 나눠 균등 배치한다.</summary>
    public float spreadDeg;
    /// <summary>관통 수. 0이면 관통 없음(첫 적중에 소멸) — <b>관통 파츠</b>·아이템이 올린다.</summary>
    public int   pierce;
    /// <summary>피해 배율. <b>크기·위력 파츠</b>가 sizeMult와 함께 올린다.</summary>
    public float damageMult;
    /// <summary>투사체 크기 배율(히트박스 포함). <b>크기·위력 파츠</b>.</summary>
    public float sizeMult;
    /// <summary>속도 배율.</summary>
    public float speedMult;
    /// <summary>착탄 폭발 반경. 0이면 폭발 없음 — <b>폭발 파츠</b>.</summary>
    public float explodeRadius;
    /// <summary>폭발 피해 = damage × 이 값.</summary>
    public float explodeDamageRatio;
    /// <summary>유도 선회 강도(0=직선). <b>유도 파츠</b> — 에임 보정이 아니라 궤적 조작이다.</summary>
    public float homingStrength;
    /// <summary>관통 후 선회해 되돌아올지. 유도 × 관통 결합 시너지.</summary>
    public bool  returnOnPierce;

    // ── 메타 ────────────────────────────────────────────────
    /// <summary>이 공격을 쏜 무기 슬롯. <b>파츠 스코프 게이트</b> — 근접으로 전환하면 원거리 파츠가 안 걸린다.</summary>
    public int sourceSlot;
    /// <summary>재귀 스폰 깊이. 상한을 넘으면 더 이상 파생시키지 않는다.</summary>
    public int depth;

    /// <summary>원본 값만 채운 기본 요청. 변형 필드는 "효과 없음" 상태로 시작한다.</summary>
    public static ProjectileRequest Create(string prefabKey, Vector3 origin, Vector3 direction,
                                           float damage, GameObject owner, int sourceSlot, float baseScale = 1f)
        => new ProjectileRequest
        {
            prefabKey  = prefabKey,
            origin     = origin,
            direction  = direction,
            damage     = damage,
            owner      = owner,
            baseScale  = baseScale <= 0f ? 1f : baseScale,
            sourceSlot = sourceSlot,

            count      = 1,
            spreadDeg  = 0f,
            pierce     = 0,     // 기본 화살은 관통하지 않는다 — 관통 파츠의 체감을 남긴다
            damageMult = 1f,
            sizeMult   = 1f,
            speedMult  = 1f,
            depth      = 0,
        };
}
