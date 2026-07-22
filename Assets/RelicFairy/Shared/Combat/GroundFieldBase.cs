using System.Collections.Generic;
using UnityEngine;
using RelicFairy.Monster;

/// <summary>
/// 룬 장판/필드(FD) 공용 기반 클래스 — 풀(독안개)·빛(성역/빛장판)이 상속.
///
/// 본구현 제공(6속성 공용):
///  • 수명/나이/반경/시전자/추적(follow) 관리 + 매 프레임 추적→OnFieldTick→만료 처리
///  • 풀링: 만료/Despawn 시 GroundFieldPool로 반환(파생은 GroundFieldPool.Spawn<T>()로 생성)
///  • 활성 장판 레지스트리(OnEnable/OnDisable 자동 관리) + 겹침 질의(Overlaps/GetOverlappingFields)
///    → 풀 "장판 겹침/연결 병합"이 이 레지스트리를 그대로 재사용
///  • 반경 내 살아있는 적 질의(QueryEnemies) = CombatQuery 재사용
///
/// 파생이 구현: OnFieldTick(체류/DoT/버프). 선택 override: OnExpire, Initialize.
/// (TODO: 시각 VFX 부착 — 현재 기능만. 풀/빛 본구현 시 Addressable VFX 자식 스폰.)
/// </summary>
public abstract class GroundFieldBase : MonoBehaviour
{
    // ── 활성 장판 레지스트리 (겹침/연결 질의용) ──
    private static readonly List<GroundFieldBase> s_active = new();
    public static IReadOnlyList<GroundFieldBase> ActiveFields => s_active;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ClearRegistry() => s_active.Clear();

    protected GameObject _instigator;
    protected float      _radius;
    protected float      _lifetime;
    protected float      _age;
    protected Transform  _followTarget;   // null이면 고정 장판
    protected bool       _active;

    private int _visualHandle;            // [가이드라인 비주얼] 장판 디스크 해제 핸들
    private int _auraHandle;              // [실제 VFX] 장판 속성 오라 해제 핸들

    protected readonly List<MonsterBase> _enemyBuffer = new();

    public bool    IsActive => _active;
    public float   Radius   => _radius;
    public Vector3 Center   => transform.position;

    /// <summary>이 장판의 속성(실제 VFX 오라용). null이면 오라 미부착. 파생이 override(독=Grass, 빛=Light).</summary>
    protected virtual RuneElement? FieldElement => null;

    protected virtual void OnEnable()
    {
        if (!s_active.Contains(this)) s_active.Add(this);
    }

    protected virtual void OnDisable()
    {
        s_active.Remove(this);
        _active = false;

        // [가이드라인 비주얼] 장판 디스크 해제(Despawn/풀 반환/씬 언로드 공통 경로)
        GuidelineVisual.ReleaseGroundField(_visualHandle);
        _visualHandle = 0;

        // [실제 VFX] 장판 속성 오라 해제
        ElementVfxPlayer.ReleaseAura(_auraHandle);
        _auraHandle = 0;
    }

    /// <summary>장판 생성 초기화. 고정 위치(추적 없음).</summary>
    public virtual void Initialize(Vector3 position, float radius, float lifetime, GameObject instigator)
    {
        transform.position = position;
        _radius       = Mathf.Max(0.1f, radius);
        _lifetime     = Mathf.Max(0f, lifetime);
        _instigator   = instigator;
        _followTarget = null;
        _age          = 0f;
        _active       = true;

        // [가이드라인 비주얼] 장판 디스크 부착(통지만 — 토글 OFF면 핸들 0)
        GuidelineVisual.ReleaseGroundField(_visualHandle);
        _visualHandle = GuidelineVisual.GroundField(transform, _radius, GetType().Name);

        // [실제 VFX] 장판 속성 오라 — 반경에 맞춰 스케일. 수명 만료/추적대상 비활성 시 자동 해제.
        ElementVfxPlayer.ReleaseAura(_auraHandle);
        _auraHandle = 0;
        if (FieldElement.HasValue && _lifetime > 0f)
            _auraHandle = ElementVfxPlayer.AttachAura(FieldElement.Value, transform, _lifetime, _radius);
    }

    /// <summary>플레이어 등 대상을 추적하는 장판으로 전환(빛 4단계 빛장판).</summary>
    public void SetFollow(Transform target) => _followTarget = target;

    protected virtual void Update()
    {
        if (!_active) return;

        _age += Time.deltaTime;

        if (_followTarget != null)
            transform.position = _followTarget.position;

        OnFieldTick(Time.deltaTime);

        if (_lifetime > 0f && _age >= _lifetime)
            Despawn();
    }

    /// <summary>체류/DoT/버프 — 파생 구현. (풀=독 DoT, 빛=치확 부여)</summary>
    protected abstract void OnFieldTick(float dt);

    /// <summary>수명 만료/강제 종료 시 1회. (연쇄/정리 훅)</summary>
    protected virtual void OnExpire() { }

    /// <summary>즉시 종료 + 풀 반환(최대 1개 제약·교체 시).</summary>
    public void Despawn()
    {
        if (!_active) return;
        _active = false;
        OnExpire();
        GroundFieldPool.Return(this);
    }

    // ── 겹침 질의 (풀 연결 병합용) ──
    public bool Overlaps(GroundFieldBase other)
    {
        if (other == null || other == this) return false;
        float r = _radius + other._radius;
        return (transform.position - other.transform.position).sqrMagnitude <= r * r;
    }

    /// <summary>현재 이 장판과 겹치는 활성 장판들을 buffer에 채운다. 반환 = 개수.</summary>
    public int GetOverlappingFields(List<GroundFieldBase> buffer)
    {
        if (buffer == null) return 0;
        buffer.Clear();
        for (int i = 0; i < s_active.Count; i++)
            if (Overlaps(s_active[i])) buffer.Add(s_active[i]);
        return buffer.Count;
    }

    /// <summary>반경 내 살아있는 적 질의(파생 공용).</summary>
    protected int QueryEnemies(int max = 16)
        => CombatQuery.GetNearbyEnemies(Center, _radius, null, max, _enemyBuffer);
}
