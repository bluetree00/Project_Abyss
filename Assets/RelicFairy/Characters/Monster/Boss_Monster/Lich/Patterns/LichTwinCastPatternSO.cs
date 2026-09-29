using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// M4 「쌍둥이 주문」 — 플레이어 좌우의 마법진 둘 중 진짜 하나만 터진다. 리치 설계서 §3-2 · §13.
///
/// O 두 원(castDuration) — 색은 같고 <b>움직임으로</b> 가른다: 진짜는 테두리가 안쪽으로 조여들고, 가짜는 바깥으로 흩어진다.
///   리치는 플레이어를 축으로 호를 그리며 옆으로 돈다
/// → S 신호(signalDuration) — 진짜가 빨개지고 가짜는 사라진다
/// → A 폭발(activeDuration) — 진짜 원 안(겹친 곳 포함)이 맞는다
/// → E 반격창(endDuration) → R 복귀
/// 두 원은 플레이어를 가운데 두고 겹친다 — 가짜 쪽으로 반걸음 이상 옮기면 산다.
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/Boss/Lich/Lich_TwinCastPattern", fileName = "Lich_TwinCastPattern")]
public class LichTwinCastPatternSO : BossPatternSO
{
    [Header("M4 — 발동")]
    public float maxRange        = 25f;
    public float patternCooldown = 7f;

    [Header("M4 — 타이밍 (초)")]
    public float castDuration     = 1.0f;
    public float signalDuration   = 0.2f;
    public float activeDuration   = 0.4f;
    public float endDuration      = 0.5f;
    public float recoveryDuration = 0.3f;

    [Header("M4 — 원")]
    public float circleRadius = 5f;
    [Tooltip("플레이어에서 두 원 중심까지 옆 거리 (m) — 반경보다 작아야 두 원이 플레이어 위에서 겹친다")]
    public float sideOffset   = 3f;
    [Tooltip("진짜 원이 이 배율에서 1로 조여든다")]
    public float realStartScale = 1.3f;
    [Tooltip("가짜 원이 1에서 이 배율로 흩어진다")]
    public float fakeEndScale   = 1.35f;

    [Header("M4 — 판정")]
    public float damageMultiplier    = 1.2f;
    public float knockbackMultiplier = 0.8f;

    [Header("M4 — 움직임")]
    [Tooltip("시전하는 동안 플레이어를 축으로 도는 각도 (도)")]
    public float orbitAngle = 35f;
    [Tooltip("호의 옆 부풂 (m)")]
    public float orbitArc   = 2f;

    [Header("M4 — 바닥 파괴 (연출·UX 시나리오 §12-4)")]
    [Tooltip("진짜 원이 터진 자리의 칸을 잠깐 부순다 — 칸 중심이 이 반경(m) 안. 0이면 없음")]
    public float breakRadius  = 0f;
    [Tooltip("붉게 흔들리는 시간(초)")]
    public float breakWarn    = 0.6f;
    [Tooltip("부서진 뒤 스스로 복구까지(초). 음수 = 복구 패턴(M8 원소 재편 · T1 전환)까지 구멍으로 남아 누적")]
    public float breakSeconds = 4f;

    [Header("M4⁺ — 악몽판(완전설계 §4-2): 원 셋 중 진짜 하나")]
    [Tooltip("가짜 원 수 — 1이면 좌우 둘, 2면 플레이어 둘레 120°마다 셋(악몽판)")]
    [Range(1, 2)]
    public int   fakeCount    = 1;

    // ── 런타임 ───────────────────────────────────────────
    private LichTwinCastState _state;

    public override void Initialize(BossPatternContext ctx) => _state = new LichTwinCastState(this);
    public override void OnRecycled()                       => _state = new LichTwinCastState(this);

    public override bool CanExecute(BossPatternContext ctx)
    {
        if (ctx.Ctx.Runtime.PlayerTarget == null) return false;
        var lichBB = (ctx.Boss as LichMonster)?.LichBB;
        if (lichBB != null && lichBB.TwinCastCooldown > 0f) return false;
        return Vector3.Distance(ctx.Ctx.Transform.position, ctx.Ctx.Runtime.PlayerTarget.position) <= maxRange;
    }

    public override SpecialStateBase GetRuntimeState() => _state;
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// LichTwinCastState — 중단 불가(피해는 받음)
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

public class LichTwinCastState : UnInterruptibleState<LichTwinCastPatternSO>
{
    private enum Phase { Cast, Signal, Active, End, Recovery }

    private Phase      _phase;
    private float      _timer;
    private const int MaxFakes = 2;

    private readonly Vector3[]    _fake          = new Vector3[MaxFakes];
    private readonly GameObject[] _fakeDisc      = new GameObject[MaxFakes];
    private readonly GameObject[] _fakeVfx       = new GameObject[MaxFakes];
    private readonly Vector3[]    _fakeDiscScale = new Vector3[MaxFakes];
    private readonly Vector3[]    _fakeVfxScale  = new Vector3[MaxFakes];

    private Vector3    _real;
    private int        _fakes;
    private GameObject _realDisc;
    private GameObject _realVfx;
    private GameObject _castFlare;
    private Vector3    _realDiscScale;
    private Vector3    _realVfxScale;

    public LichTwinCastState(LichTwinCastPatternSO data) : base(data) { }

    public override void Enter(MonsterContext ctx)
    {
        _phase = Phase.Cast;
        _timer = 0f;

        Vector3 player = LichPatternUtil.PlayerFloorPos(ctx);
        LichPatternUtil.FaceInstant(ctx, player);
        ctx.Animator?.CrossFade("MagicBolt", 0.1f);

        // 둘이면 리치 → 플레이어 방향의 옆축(좌우), 셋이면 플레이어 둘레 120°마다. 진짜는 무작위.
        Vector3 fwd = player - ctx.Transform.position;
        fwd.y = 0f;
        fwd = fwd.sqrMagnitude > 0.001f ? fwd.normalized : ctx.Transform.forward;
        _fakes = Mathf.Clamp(Data.fakeCount, 1, MaxFakes);
        int   total = _fakes + 1;
        float step  = 360f / total;
        float first = total == 2 ? 90f : Random.Range(0f, 360f);
        int   real  = Random.Range(0, total);
        for (int i = 0, f = 0; i < total; i++)
        {
            Vector3 at = player + Quaternion.Euler(0f, first + step * i, 0f) * fwd * Data.sideOffset;
            if (i == real) _real = at;
            else           _fake[f++] = at;
        }

        // 진짜는 조여들며 안이 차오르고, 가짜는 번지며 옅어진다(색은 같게 — 움직임으로 읽는다).
        _realDisc = LichPatternUtil.PrepareTelegraph(
            PatternGuideHelper.Disc(_real, Data.circleRadius * Data.realStartScale, LichPatternUtil.Arcane), LichPatternUtil.Arcane);
        _realDiscScale = _realDisc != null ? _realDisc.transform.localScale : Vector3.one;

        float vfxScale = Data.circleRadius / 5f;
        _realVfx = LichVfx.PlayLoop(LichVfxSlot.TwinCircle, _real, Quaternion.identity, vfxScale * Data.realStartScale);
        _realVfxScale = _realVfx != null ? _realVfx.transform.localScale : Vector3.one;
        for (int f = 0; f < _fakes; f++)
        {
            _fakeDisc[f]      = PatternGuideHelper.Disc(_fake[f], Data.circleRadius, LichPatternUtil.Arcane);
            _fakeDiscScale[f] = _fakeDisc[f] != null ? _fakeDisc[f].transform.localScale : Vector3.one;
            _fakeVfx[f]       = LichVfx.PlayLoop(LichVfxSlot.TwinCircle, _fake[f], Quaternion.identity, vfxScale);
            _fakeVfxScale[f]  = _fakeVfx[f] != null ? _fakeVfx[f].transform.localScale : Vector3.one;
        }

        var lich = LichPatternUtil.Lich(ctx);
        Transform hand = lich != null ? lich.CastPoint : ctx.Transform;
        _castFlare = LichVfx.PlayLoop(LichVfxSlot.CastFlare, hand.position, hand.rotation, 1f, hand);
        lich?.PulseBook(Data.castDuration);
        LichSfx.Play(LichSfxSlot.CircleSpawn, player);

        // 플레이어를 축으로 호를 그리며 돈다 — 두 원 중 어느 쪽에서도 치우치지 않게 방향은 무작위.
        var mc = lich?.MovementController;
        mc?.SetLocked(true);
        if (mc != null && Data.orbitAngle > 0f)
        {
            float   sign = Random.value < 0.5f ? -1f : 1f;
            Vector3 from = ctx.Transform.position - player;
            from.y = 0f;
            Vector3 dest = player + Quaternion.Euler(0f, Data.orbitAngle * sign, 0f) * from;
            mc.ScriptMove(dest, Data.castDuration + Data.signalDuration + Data.activeDuration, Data.orbitArc * sign, facePlayer: true);
        }
    }

    public override void Update(MonsterContext ctx)
    {
        _timer += Time.deltaTime;

        switch (_phase)
        {
            case Phase.Cast:
            {
                float t = Mathf.Clamp01(_timer / Data.castDuration);
                // 진짜: 크게 떴다가 판정 반경으로 조여든다. 가짜: 판정 반경에서 바깥으로 번진다.
                float real = Mathf.Lerp(1f, 1f / Data.realStartScale, t * t);
                float fake = Mathf.Lerp(1f, Data.fakeEndScale, t);
                if (_realDisc != null) _realDisc.transform.localScale = LichPatternUtil.ScaleFlat(_realDiscScale, real);
                if (_realVfx  != null) _realVfx.transform.localScale  = _realVfxScale * real;
                PatternGuideHelper.SetProgress(_realDisc, t);
                for (int f = 0; f < _fakes; f++)
                {
                    if (_fakeDisc[f] != null) _fakeDisc[f].transform.localScale = LichPatternUtil.ScaleFlat(_fakeDiscScale[f], fake);
                    if (_fakeVfx[f]  != null) _fakeVfx[f].transform.localScale  = _fakeVfxScale[f] * fake;
                    PatternGuideHelper.SetIntensity(_fakeDisc[f], Mathf.Lerp(1f, 0.35f, t));
                }

                if (t >= 1f)
                {
                    PatternGuideHelper.SetColor(_realDisc, LichPatternUtil.Lethal);
                    PatternGuideHelper.SetFlow(_realDisc, LichPatternUtil.Lethal);
                    PatternGuideHelper.SetIntensity(_realDisc, 2f);
                    ClearFakes(0.3f);
                    Next(Phase.Signal);
                }
                break;
            }

            case Phase.Signal:
                if (_timer >= Data.signalDuration)
                {
                    Blast(ctx);
                    Next(Phase.Active);
                }
                break;

            case Phase.Active:
                if (_timer >= Data.activeDuration) Next(Phase.End);
                break;

            case Phase.End:
                if (_timer >= Data.endDuration) Next(Phase.Recovery);
                break;

            case Phase.Recovery:
                if (_timer >= Data.recoveryDuration)
                    ctx.Monster.ChangeState<ChaseState>();
                break;
        }
    }

    public override void Exit(MonsterContext ctx)
    {
        PatternGuideHelper.SafeDestroy(ref _realDisc);
        LichVfx.Stop(ref _realVfx);
        ClearFakes(0f);
        LichVfx.Stop(ref _castFlare);

        var lich = LichPatternUtil.Lich(ctx);
        lich?.MovementController?.SetLocked(false);
        if (lich?.LichBB != null)
            lich.LichBB.TwinCastCooldown = Data.patternCooldown;
    }

    private void Next(Phase phase)
    {
        _phase = phase;
        _timer = 0f;
    }

    private void ClearFakes(float fade)
    {
        for (int f = 0; f < MaxFakes; f++)
        {
            PatternGuideHelper.SafeDestroy(ref _fakeDisc[f]);
            LichVfx.Stop(ref _fakeVfx[f], fade);
        }
    }

    private void Blast(MonsterContext ctx)
    {
        PatternGuideHelper.SafeDestroy(ref _realDisc);
        LichVfx.Stop(ref _realVfx, 0.2f);
        LichVfx.Stop(ref _castFlare, 0.2f);

        LichVfx.Play(LichVfxSlot.TwinBlast, _real, Quaternion.identity, Data.circleRadius / 5f);
        LichSfx.Play(LichSfxSlot.SlamImpact, _real, 0.8f);
        LichCrack.Spawn(_real, Data.circleRadius * 1.6f, 10f);

        bool hit = LichPatternUtil.HitCircle(ctx, _real, Data.circleRadius, Data.damageMultiplier, Data.knockbackMultiplier);
        LichPatternUtil.Impact(LichImpact.Medium, hit);
        LichPatternUtil.BreakFloor(_real, Data.breakRadius, Data.breakWarn, Data.breakSeconds);
    }
}
}
