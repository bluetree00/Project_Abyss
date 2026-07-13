using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using RelicFairy.Monster;
using UnityEngine;

/// <summary>
/// 히트 판정 컴포넌트.
/// attackId로 공격 행위를 구분 — 같은 적이라도 다른 attackId면 다시 맞음.
/// </summary>
public class ColliderInstance : MonoBehaviour
{
    public string payloadKey;
    public float damage;
    public float knockbackMultiplier;
    public float hitInterval;       // 0 = 단발, >0 = 주기적 피해
    public WeaponActionType actionType;
    public GameObject owner;
    public float duration = 1f;
    public int attackId;            // 공격 행위 ID (콤보 스텝 등으로 구분)

    [Header("Hit Effect")]
    public string hitEffectKey;
    public float hitEffectScale = 1f;

    private float _elapsedTime;
    private bool _standalone;
    private bool _active;
    private Vector3 _baseScale = Vector3.one;

    // 근접 원샷 오버랩 질의 버퍼(재사용 — alloc 방지). 밀집 지역 대비 넉넉히.
    // 근접 원샷 오버랩 질의 버퍼.
    // ⚠️ 작으면 환경 콜라이더(바닥·벽·소품·VFX 트리거 등)가 버퍼를 채워버려 몬스터가 안 잡힌다
    //    — 물리엔진은 버퍼가 차면 거기서 끊고, 그 순서는 거리순이 아니다("가끔 씹힘"의 원인).
    //    레이어 `~0`으로 훑을 수밖에 없는 구조(몬스터 콜라이더가 Default 레이어)라 넉넉히 잡는다.
    private static readonly Collider[] s_overlapBuf = new Collider[256];

    // 타격 대상 레이어 마스크 — 몬스터(MonsterHit) + 플레이어.
    // `~0`(전 레이어)로 훑으면 바닥·벽·소품·VFX 트리거가 버퍼를 채워 정작 대상이 안 잡힌다.
    // Player를 포함하는 건 몬스터가 이 컴포넌트를 쓰게 될 경우를 위한 대비(현재는 플레이어 무기 전용).
    private static int HitMask => MonsterBase.HitLayerMask | (1 << LayerMask.NameToLayer("Player"));

    // key: 대상, value: 마지막으로 맞은 attackId
    private readonly Dictionary<GameObject, int> _hitRecord = new();
    // 주기적 피해용
    private readonly Dictionary<GameObject, float> _hitTimestamps = new();

    private void Awake()
    {
        _standalone = GetComponent<EffectBehaviour>() == null;
        _baseScale = transform.localScale;
    }

    private void OnEnable()
    {
        _elapsedTime = 0f;
        _active = false;
        _hitRecord.Clear();
        _hitTimestamps.Clear();
    }

    public void Activate()
    {
        _active = true;

        // 아이템 근접 사거리 변형(확장된 칼끝/기다림의 미학) — 근접 액션이면 콜라이더 스케일.
        // 변형이 없으면 기준 스케일 그대로(회귀 0). 풀 재사용 정합을 위해 매 Activate 절대 설정.
        float rangeMult = CombatDamage.IsMeleeAction(actionType) ? ItemCombatMods.Current.meleeRangeMult : 0f;
        transform.localScale = rangeMult > 0f ? _baseScale * (1f + rangeMult) : _baseScale;

        // 근접 원샷(hitInterval=0): 스윙 순간 콜라이더 영역에 겹친 대상만 즉시 1회 판정.
        // 트리거 지속에 의존하지 않아 "타이밍 씹힘"과 "잔류 콜라이더 뒤늦은 타격"을 동시에 제거.
        if (hitInterval <= 0f && CombatDamage.IsMeleeAction(actionType))
        {
            OneShotMeleeOverlap();
            _active = false;
            return;
        }

        // 지속/주기 판정: 콜라이더를 껐다 켜서 물리 엔진이 재감지하도록 강제(OnTriggerEnter/Stay)
        var col = GetComponent<Collider>();
        if (col != null)
        {
            col.enabled = false;
            col.enabled = true;
        }
    }

    // 근접 원샷 판정: 콜라이더 영역에 겹친 대상에 즉시 1회 데미지(트리거 지속 없음).
    private void OneShotMeleeOverlap()
    {
        var col = GetComponent<Collider>();
        if (col == null) return;

        int n;
        // 박스는 콜라이더 회전(플레이어 방향)을 반영한 OBB로 판정한다.
        // AABB(col.bounds)로 판정하면 대각선을 바라볼 때 박스가 부풀어 슬래시 밖까지 오판정된다.
        // 구/기타 형상은 AABB가 회전 불변이라 기존 판정 그대로 사용(대검 등).
        if (col is BoxCollider box)
        {
            Vector3 s = box.transform.lossyScale;
            Vector3 half = new Vector3(
                box.size.x * 0.5f * Mathf.Abs(s.x),
                box.size.y * 0.5f * Mathf.Abs(s.y),
                box.size.z * 0.5f * Mathf.Abs(s.z));
            n = Physics.OverlapBoxNonAlloc(
                box.transform.TransformPoint(box.center), half, s_overlapBuf,
                box.transform.rotation, HitMask, QueryTriggerInteraction.Collide);
        }
        else
        {
            Bounds b = col.bounds;
            n = Physics.OverlapBoxNonAlloc(
                b.center, b.extents, s_overlapBuf, Quaternion.identity, HitMask, QueryTriggerInteraction.Collide);
        }
        for (int i = 0; i < n; i++)
        {
            var other = s_overlapBuf[i];
            if (other == null || other == col) continue;
            if (!CanHit(other.gameObject)) continue;
            ApplyDamage(other);
            RecordHit(other.gameObject);
        }
    }

    private void Update()
    {
        if (!_standalone) return;
        _elapsedTime += Time.deltaTime;
        if (_elapsedTime >= duration)
            ReturnToPool();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!_active) return;
        if (!CanHit(other.gameObject)) return;
        ApplyDamage(other);
        RecordHit(other.gameObject);
    }

    private void OnTriggerStay(Collider other)
    {
        if (!_active) return;
        if (hitInterval <= 0f) return;
        if (!CanHit(other.gameObject)) return;
        ApplyDamage(other);
        _hitTimestamps[other.gameObject] = Time.time;
    }

    private bool CanHit(GameObject target)
    {
        if (target == owner) return false;
        if (!target.TryGetComponent<IDamageable>(out _)) return false;
        if (target.TryGetComponent<MonsterBase>(out var monster) && monster.IsMeleeImmuneNow) return false;

        // 같은 attackId로 이미 맞았으면 스킵
        if (_hitRecord.TryGetValue(target, out int lastId) && lastId == attackId)
        {
            // 주기적 피해인 경우 시간 체크
            if (hitInterval > 0f)
            {
                if (_hitTimestamps.TryGetValue(target, out float lastTime))
                    return Time.time - lastTime >= hitInterval;
                return true;
            }
            return false;
        }

        return true;
    }

    private void RecordHit(GameObject target)
    {
        _hitRecord[target] = attackId;
        _hitTimestamps[target] = Time.time;
    }

    // 실제 피해 처리는 CombatDamage(주 피해 단일 파이프라인)에 위임한다.
    // 콜라이더는 '판정 형상'만 담당하고, 크리티컬·아이템·서약·패시브·타격감은 전부 그쪽에서 일괄 처리된다.
    private void ApplyDamage(Collider other)
    {
        if (other == null) return;

        CombatDamage.Deal(new CombatDamage.Request
        {
            Target              = other.gameObject,
            BaseDamage          = damage,
            Owner               = owner,
            ActionType          = actionType,
            KnockbackMultiplier = knockbackMultiplier,
            HitPoint            = other.ClosestPoint(transform.position),
            SourcePosition      = owner != null ? owner.transform.position : transform.position,
            HitEffectKey        = hitEffectKey,
            HitEffectScale      = hitEffectScale,
            ComboStep           = attackId,
        });
    }

    private void ReturnToPool()
    {
        if (!string.IsNullOrEmpty(payloadKey))
            Managers.ObjectPooler.Despawn(gameObject);
        else
            Destroy(gameObject);
    }

}
