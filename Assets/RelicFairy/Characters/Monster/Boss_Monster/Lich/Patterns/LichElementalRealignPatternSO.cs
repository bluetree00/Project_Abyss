using RelicFairy.UI;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// M8 「원소 재편」 → 「원소 일제 발동」(10-22 E2 L2 2차 — 사용자 「4가지 한 번에 · 가이드라인 · 틈이 넓으면 피하는 느낌이 안 난다」) —
/// 제단을 사분면으로 갈라 네 원소 모양을 한꺼번에 터뜨린다(빽빽이 · 좁은 틈). 끝나면 성흔 탄막으로 잇는다(연계). 리치 설계서 §3-2 · §13.
///
/// O 시전(castDuration) — 리치가 제단 중앙 상공에서 책을 들고, 제단을 가르는 빛줄기 두 줄(십자)이 그어진다
/// → 일제 발동(<see cref="LichHazards.ElementCycle"/>) — 네 원소가 무작위로 한 칸씩(옅은 원소 색 · 판정 없음) · 칸마다 원소 모양 가이드가 동시에 차오른다(1.4초).
///     얼음 원 · 번개 작은 원 · 어둠 큰 원(가장자리 사이 1.5 m만 빈다) · 불 = 중심에서 번갈아 뻗는 부채꼴 줄기(사이 좁은 틈) · 플레이어 칸은 첫 모양이 지금 자리
///     2번(간격 3초) · 한 번 발동에 한 번만 맞는다(고정 장판 · 칸 하나씩 터지기는 피해 0 · 「별로」였다)
/// → R 복귀(recoveryDuration) — 발동은 패턴이 끝나도 돈다(그동안 리치는 성흔 탄막 · 비전 구체만 · LichMonster · 끝나자마자 성흔 탄막 연계)
/// 발동 조건: HP 70% 이하, 한 전투 최대 maxUses번.
///
/// 바닥 복구(연출·UX 시나리오 §12-4): 원소 재편은 제단 전체를 쓰는 대마법 — 시전을 시작하며 그동안 부서진 바닥을
/// 가운데부터 되살린다(1페이지 지형의 「복구 패턴」).
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/Boss/Lich/Lich_ElementalRealignPattern", fileName = "Lich_ElementalRealignPattern")]
public class LichElementalRealignPatternSO : BossPatternSO
{
    [Header("M8 — 발동")]
    [Range(0f, 1f)]
    public float hpThreshold     = 0.7f;
    public int   maxUses         = 2;
    public float patternCooldown = 30f;

    [Header("M8 — 타이밍 (초)")]
    public float castDuration     = 2.0f;
    public float recoveryDuration = 0.5f;

    [Header("M8 — 연출")]
    public float hoverHeight   = 3f;
    public float crossWidth    = 0.8f;

    [Header("M8 — 원소 일제 발동 (10-22 E2 L2 2차)")]
    [Tooltip("일제 발동 횟수")]
    [SerializeField] private int   _waves         = 2;
    [Tooltip("일제 발동 사이 간격(초)")]
    [SerializeField] private float _eruptInterval = 3f;
    [Tooltip("모양이 차오르는 예고(초)")]
    [SerializeField] private float _eruptWarn     = 1.4f;
    [Tooltip("원 가장자리 사이 빈틈 기준(m) — 발동마다 × 0.8~1.6 무작위 격자(사용자 「틈이 넓으면 피하는 느낌이 안 난다 · 매번 바뀌어야 지루하지 않다」)")]
    [SerializeField] private float _safeGap       = 1.2f;
    [Tooltip("불: 줄기 폭 · 틈 기준(도) — 줄기마다 폭 × 0.8~1.3 · 틈 × 0.6~1.6 무작위")]
    [SerializeField] private float _fireWedge     = 16f;
    [SerializeField] private float _fireGap       = 7f;
    [Tooltip("얼음 · 번개 · 어둠 원 반경(m) — 세 칸이 한 격자라 하나(발동마다 × 0.9~1.15)")]
    [SerializeField] private float _discRadius    = 2.5f;
    [Tooltip("불: 칸 피해 배율")]
    [SerializeField] private float _fireDamage        = 1f;
    [Tooltip("불: 가운데 줄에 남는 불 장판(초)")]
    [SerializeField] private float _fireGroundSeconds = 2f;
    [Tooltip("불: 장판 한 틱(0.4초) 피해 배율")]
    [SerializeField] private float _fireGroundDamage  = 0.1f;
    [Tooltip("얼음: 칸 피해 배율")]
    [SerializeField] private float _iceDamage      = 0.8f;
    [Range(0f, 1f)]
    [Tooltip("얼음: 빙결 감속 — 이동 속도 배율")]
    [SerializeField] private float _iceSlowScale   = 0.5f;
    [Tooltip("얼음: 빙결 감속 시간(초)")]
    [SerializeField] private float _iceSlowSeconds = 2f;
    [Tooltip("번개: 칸 피해 배율")]
    [SerializeField] private float _boltDamage = 0.9f;
    [Tooltip("어둠: 칸 피해 배율")]
    [SerializeField] private float _darkDamage   = 1f;
    [Tooltip("어둠: 시야 축소(초)")]
    [SerializeField] private float _darkVignette = 3f;

    // ── 런타임 ───────────────────────────────────────────
    private LichElementalRealignState _state;

    public override void Initialize(BossPatternContext ctx) => _state = new LichElementalRealignState(this);
    public override void OnRecycled()                       => _state = new LichElementalRealignState(this);

    public override bool CanExecute(BossPatternContext ctx)
    {
        if (ctx.Ctx.Runtime.PlayerTarget == null) return false;
        var lich = ctx.Boss as LichMonster;
        if (lich == null || lich.LichBB == null) return false;
        if (lich.HpRatio > hpThreshold) return false;
        return lich.LichBB.ElementalRealignUses < maxUses && lich.LichBB.ElementalRealignCooldown <= 0f;
    }

    public override SpecialStateBase GetRuntimeState() => _state;

    /// <summary>원소 순환 폭발 설정 — 배치(중심 · 반경 · 경계 각 · 원소 순서)는 시전이 정한다.</summary>
    public LichHazards.CycleSettings BuildCycle(Vector3 center, float radius, float baseAngle,
                                                LichHazards.QuadrantElement[] elements)
        => new LichHazards.CycleSettings
        {
            Center              = center,
            Radius              = radius,
            BaseAngle           = baseAngle,
            Elements            = elements,
            Interval            = Mathf.Max(0.5f, _eruptInterval),
            Warn                = Mathf.Max(LichPatternUtil.MinLead, _eruptWarn),
            FireDamage          = _fireDamage,
            FireGroundSeconds   = _fireGroundSeconds,
            FireGroundDamage    = _fireGroundDamage,
            IceDamage           = _iceDamage,
            IceSlowScale        = _iceSlowScale,
            IceSlowSeconds      = _iceSlowSeconds,
            BoltDamage          = _boltDamage,
            DarkDamage          = _darkDamage,
            DarkVignetteSeconds = _darkVignette,
            Waves               = Mathf.Max(1, _waves),
            SafeGap             = Mathf.Max(0.8f, _safeGap),
            DiscRadius          = Mathf.Max(1f, _discRadius),
            FireWedge           = Mathf.Max(5f, _fireWedge),
            FireGap             = Mathf.Max(3f, _fireGap),
        };
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// LichElementalRealignState — 중단 불가(피해는 받음)
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

public class LichElementalRealignState : UnInterruptibleState<LichElementalRealignPatternSO>
{
    private const string BarkKey = "Lich_Realign";

    private readonly LichHazards.QuadrantElement[] _elements =
    {
        LichHazards.QuadrantElement.Fire, LichHazards.QuadrantElement.Ice,
        LichHazards.QuadrantElement.Lightning, LichHazards.QuadrantElement.Dark,
    };

    private bool       _released;
    private float      _timer;
    private float      _baseAngle;
    private Vector3    _center;
    private float      _radius;
    private GameObject _crossA;
    private GameObject _crossB;

    public LichElementalRealignState(LichElementalRealignPatternSO data) : base(data) { }

    public override void Enter(MonsterContext ctx)
    {
        _released = false;
        _timer    = 0f;

        var lich = LichPatternUtil.Lich(ctx);
        var mc   = lich?.MovementController;
        _center = LichPatternUtil.OnFloor(ctx, mc != null ? mc.ArenaCenter : ctx.Transform.position);
        _radius = mc != null ? mc.ArenaRadius : 12f;
        if (mc != null)
        {
            mc.SetLocked(true);
            mc.SetAltitudeOffset(Data.hoverHeight);
            mc.ScriptMove(_center, Data.castDuration * 0.6f, 0f, facePlayer: true);
        }
        if (lich?.LichBB != null) lich.LichBB.ElementalRealignUses++;

        ctx.Animator?.CrossFade("ElementalBarrage", 0.1f);
        UI_BossBark.ShowDialogue(BarkKey, BossBarkType.PatternAnnounce);
        LichPatternUtil.RestoreFloor(_center);   // 제단을 되살린 뒤 원소를 입힌다
        lich?.PulseBook(Data.castDuration);
        LichSfx.Play(LichSfxSlot.CastCharge, ctx.Transform.position);

        // 십자 빛줄기 — 사분면 경계. 플레이어가 한 사분면 한가운데 오도록 경계를 45° 틀어 둔다.
        Vector3 toPlayer = LichPatternUtil.PlayerFloorPos(ctx) - _center;
        _baseAngle = (toPlayer.sqrMagnitude > 0.01f ? Mathf.Atan2(toPlayer.x, toPlayer.z) * Mathf.Rad2Deg : 0f) - 45f;
        _crossA = CrossLine(_baseAngle);
        _crossB = CrossLine(_baseAngle + 90f);

        for (int i = _elements.Length - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (_elements[i], _elements[j]) = (_elements[j], _elements[i]);
        }
    }

    public override void Update(MonsterContext ctx)
    {
        _timer += Time.deltaTime;

        if (!_released)
        {
            float k = _timer / Mathf.Max(0.01f, Data.castDuration);
            PatternGuideHelper.SetProgress(_crossA, k);
            PatternGuideHelper.SetProgress(_crossB, k);
            if (_timer < Data.castDuration) return;
            _released = true;
            _timer    = 0f;
            PatternGuideHelper.SafeDestroy(ref _crossA);
            PatternGuideHelper.SafeDestroy(ref _crossB);

            LichHazards.ElementCycle(ctx, Data.BuildCycle(_center, _radius, _baseAngle,
                                                          (LichHazards.QuadrantElement[])_elements.Clone()));
            LichVfx.Play(LichVfxSlot.PhaseBurst, _center + Vector3.up, Quaternion.identity, 0.6f);
            LichSfx.Play(LichSfxSlot.ZoneHum, _center);
            LichCinematics.Chroma(0.3f, 0.3f);
            return;
        }

        if (_timer >= LichPatternUtil.Recovery(ctx, Data.recoveryDuration))
            ctx.Monster.ChangeState<ChaseState>();
    }

    public override void Exit(MonsterContext ctx)
    {
        PatternGuideHelper.SafeDestroy(ref _crossA);
        PatternGuideHelper.SafeDestroy(ref _crossB);

        var lich = LichPatternUtil.Lich(ctx);
        var mc   = lich?.MovementController;
        mc?.SetAltitudeOffset(0f);
        mc?.SetLocked(false);
        if (lich?.LichBB != null)
            lich.LichBB.ElementalRealignCooldown = Data.patternCooldown;
    }

    private GameObject CrossLine(float angle)
    {
        Vector3 dir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
        return LichPatternUtil.PrepareTelegraph(
            PatternGuideHelper.Beam(_center - dir * _radius, dir, _radius * 2f, Data.crossWidth, LichPatternUtil.Arcane),
            LichPatternUtil.Arcane);
    }
}
}
