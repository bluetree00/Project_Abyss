using RelicFairy.UI;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// M8 「원소 재편」 — 제단을 사분면으로 갈라 사분면마다 원소 성질을 입힌다(20초). 리치 설계서 §3-2 · §13.
///
/// O 시전(castDuration) — 리치가 제단 중앙 상공에서 책을 들고, 제단을 가르는 빛줄기 두 줄(십자)이 그어진다
/// → 사분면 발동 — 네 원소가 무작위로 한 칸씩, 바닥 색으로 상시 표시(<see cref="LichHazards.ElementQuadrants"/>)
///     불 = 초당 피해 · 얼음 = 이동 −30% · 번개 = 맞으면 낙뢰가 한 번 더 · 어둠 = 시야 축소
/// → R 복귀(recoveryDuration) — 사분면은 패턴이 끝나도 zoneSeconds 동안 남는다
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
    public float zoneSeconds      = 20f;

    [Header("M8 — 사분면 성질")]
    [Tooltip("불: 초당 피해 (공격력 배율)")]
    public float fireDps          = 0.25f;
    [Range(0f, 1f)]
    [Tooltip("얼음: 이동 속도 배율")]
    public float iceSlowScale     = 0.7f;
    [Tooltip("번개: 맞은 뒤 따라오는 낙뢰 — 예고 시간 · 반경 · 피해 · 재발동 대기")]
    public float chainWarn        = 0.45f;
    public float chainRadius      = 1.6f;
    public float chainDamage      = 0.6f;
    public float chainCooldown    = 2f;
    [Tooltip("어둠: 시야 축소 비네트를 한 번에 유지하는 시간 (안에 있는 동안 계속 연장)")]
    public float darkVignette     = 0.4f;

    [Header("M8 — 연출")]
    public float hoverHeight   = 3f;
    public float crossWidth    = 0.8f;

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

            LichHazards.ElementQuadrants(ctx, new LichHazards.QuadrantSettings
            {
                Center        = _center,
                Radius        = _radius,
                BaseAngle     = _baseAngle,
                Elements      = (LichHazards.QuadrantElement[])_elements.Clone(),
                Seconds       = Data.zoneSeconds,
                FireDps       = Data.fireDps,
                IceSlowScale  = Data.iceSlowScale,
                ChainWarn     = Data.chainWarn,
                ChainRadius   = Data.chainRadius,
                ChainDamage   = Data.chainDamage,
                ChainCooldown = Data.chainCooldown,
                DarkVignette  = Data.darkVignette,
            });
            LichVfx.Play(LichVfxSlot.PhaseBurst, _center + Vector3.up, Quaternion.identity, 0.6f);
            LichSfx.Play(LichSfxSlot.ZoneHum, _center);
            LichCinematics.Chroma(0.3f, 0.3f);
            return;
        }

        if (_timer >= Data.recoveryDuration)
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
