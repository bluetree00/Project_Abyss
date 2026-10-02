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

    [Tooltip("연계 — 이 패턴이 끝나면 쉬지 않고(0.5초) 이어 낼 패턴. 2페이지 구성 §9 「컨셉 연계기」. 비우면 없음")]
    public BossPatternSO followUp;

    [Tooltip("연계를 2페이지 후반(간판 뒤)에만 쓴다")]
    public bool followUpLateOnly = true;

    [Tooltip("연계 쿨다운(초) — 이 안에 같은 연계가 다시 오면 이어 내지 않는다(09-28: 쿨다운이 없어 후반이 두 패턴 되풀이로 굳었다)")]
    public float followUpCooldown = 14f;

    [Header("시기")]
    [Tooltip("해방기부터만 쓴다 — 봉인기엔 옛 봉인 사슬이 이 기술을 막고 있다(10-03 봉인 해방 설계 S2)")]
    public bool liberatedOnly = false;

    /// <summary>시기 때문에 지금 못 쓰는가 — 봉인기의 해방기 전용 기술.</summary>
    public bool EraLocked => liberatedOnly && !StoryProgress.IsLiberated;

    /// <summary>실행기가 고를 수 있는가 = 시기 잠금이 아니고 <see cref="CanExecute"/>.</summary>
    public bool Available(BossPatternContext ctx) => !EraLocked && CanExecute(ctx);

    /// <summary>지금 <see cref="followUp"/>을 이어 낼 때인가 — 후반 전용이면 2페이지 간판 뒤에만.</summary>
    public bool FollowUpActive(MonsterBase monster)
        => followUp != null && (!followUpLateOnly || (monster as IPagedBoss)?.Pages?.IsLate == true);

    /// <summary>연계로 이어질 때 낼 수 있는가 — 기본은 <see cref="CanExecute"/>. 연계는 앞 패턴의 일부라 자기 쿨다운은 안 보게 재정의할 수 있다.</summary>
    public virtual bool CanFollowUp(BossPatternContext ctx) => CanExecute(ctx);

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
