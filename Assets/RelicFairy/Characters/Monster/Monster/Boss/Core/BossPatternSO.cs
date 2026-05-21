using UnityEngine;


namespace RelicFairy.Monster
{
/// <summary>
/// 보스 패턴 구현 추상 ScriptableObject.
///
/// ━━ 책임 ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
///  • 패턴에 필요한 데이터 필드를 담는다 (파생 클래스에서 선언).
///  • Initialize(ctx) : 보스 초기화 시 1회 호출. 런타임 상태/풀 생성.
///  • CanExecute(ctx) : 이 패턴을 지금 실행할 수 있는지 판정.
///  • GetRuntimeState() : 런타임 상태 인스턴스를 반환.
///  • OnRecycled() : 보스 오브젝트 풀 재사용 시 초기화.
///  • Dispose() : 보스 소멸 시 네이티브 리소스 해제.
///
/// ━━ 가중치 ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
///  WeightedRandom 모드에서 weight 가 높을수록 선택 빈도가 높아진다.
/// </summary>
/// <summary>
/// BossPatternSO 는 SpecialStateDataBase 를 상속한다.
/// FullLockState&lt;TData&gt; 등의 "where TData : SpecialStateDataBase" 제약을 만족시키기 위함.
/// CreateState() 는 GetRuntimeState() 에 위임한다.
/// </summary>
public abstract class BossPatternSO : SpecialStateDataBase
{
    [Tooltip("패턴 선택 가중치 (높을수록 자주 선택됨, WeightedRandom 모드에서 적용)")]
    public float weight = 1f;

    [Header("이펙트")]
    [Tooltip("패턴 실행 시 스폰할 이펙트 프리팹. null 이면 이펙트 없음.")]
    public UnityEngine.GameObject effectPrefab;

    [Header("Player Status Effect")]
    [Tooltip("Optional player status effect applied by the pattern. Leave null when the pattern should not apply one.")]
    public PlayerStatusEffectSO playerStatusEffect;

    [Header("패턴 연계")]
    [Tooltip("이 패턴 실행 후 Blackboard.LastPatternTag 에 기록되는 태그. BKLastPatternTagConditionSO 에서 참조.")]
    public string patternTag = "";

    [Tooltip("≥ 0: 이 패턴 완료 후 사용할 브레이크 쿨다운 고정값 (초). -1: Config의 기본 랜덤 사용.")]
    public float breakOverride = -1f;

    /// <summary>
    /// 보스 초기화 시 1회 호출된다.
    /// 파생 클래스에서 런타임 상태 인스턴스와 오브젝트 풀을 생성한다.
    /// </summary>
    public virtual void Initialize(BossPatternContext ctx) { }

    /// <summary>보스 오브젝트 풀 재사용 시 호출된다. 상태·풀을 초기 상태로 되돌린다.</summary>
    public virtual void OnRecycled() { }

    /// <summary>보스 소멸 시 호출된다. 네이티브 리소스(풀 등)를 해제한다.</summary>
    public virtual void Dispose() { }

    /// <summary>현재 이 패턴을 실행할 수 있으면 true.</summary>
    public abstract bool CanExecute(BossPatternContext ctx);

    /// <summary>
    /// 강제 실행 엔트리에서 이 패턴을 지금 인터럽트할 수 있으면 true.
    /// 기본 구현: CanExecute(ctx) 에 위임.
    /// 특수 패턴(예: SpinSlash)은 거리 무관 HP 임계값만 체크하도록 오버라이드한다.
    /// </summary>
    public virtual bool CanForceInterrupt(BossPatternContext ctx) => CanExecute(ctx);

    /// <summary>
    /// Initialize 이후 유효한 런타임 상태 인스턴스를 반환한다.
    /// 보스가 ChangeState(state)를 호출할 때 사용.
    /// </summary>
    public abstract SpecialStateBase GetRuntimeState();

    /// <summary>SpecialStateDataBase 구현 — GetRuntimeState() 에 위임.</summary>
    public override SpecialStateBase CreateState() => GetRuntimeState();
}
}
