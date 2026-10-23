using RelicFairy.UI;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// F2 「멀린의 봉인진」 — 악몽기 3페이지(완전설계 §4-6).
///
/// O — 코어 위에 반경 arrayRadius의 보라 봉인진 — 여섯 갈래 빛줄기가 차오른다(예고, 아직 무해).
/// → A — 빛줄기가 회전한다(한 바퀴 revolutionSeconds). 빛줄기 사이 부채꼴이 안전 — 틈을 따라 함께 돈다.
///   빛줄기 사이 각은 시전마다 고르지 않다(10-22 E5 페이지2 — 6갈래 고른 틈은 바깥이 6 m로 넓어 외우면 끝이었다 → 8갈래 · ±25%:
///   회전을 따라갈 수 있는 반경 4~6 m에서 틈 1~3 m).
///   reverseEvery마다 한 줄이 흰 빛으로 깜박이고 0.5초 뒤 <b>방향이 뒤집힌다</b>(반전 예고).
/// → E · R
/// 바크: 리치가 멀린의 목소리를 흉내 — 「…작은 빛. 이리 오렴.」
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/Boss/Lich/Lich_SealArrayPattern", fileName = "Lich_SealArrayPattern")]
public class LichSealArrayPatternSO : BossPatternSO
{
    [Header("멀린의 봉인진 — 발동")]
    public float patternCooldown = 14f;

    [Header("멀린의 봉인진 — 타이밍 (초)")]
    public float openDuration      = 1.5f;
    public float activeDuration    = 6f;
    [Tooltip("한 바퀴 도는 시간")]
    public float revolutionSeconds = 8f;
    [Tooltip("방향 반전 간격(0이면 반전 없음)")]
    public float reverseEvery      = 2.2f;
    public float reverseWarn       = 0.5f;
    public float endDuration       = 0.8f;
    public float recoveryDuration  = 0.4f;

    [Header("멀린의 봉인진 — 판정")]
    public int   spokeCount        = 6;
    public float arrayRadius       = 10f;
    public float spokeWidth        = 2f;
    public float damageMultiplier  = 0.7f;
    [Tooltip("같은 빛줄기에 다시 맞기까지(초)")]
    public float tickInterval      = 0.45f;
    [Tooltip("빛줄기 사이 각을 시전마다 ±이 비율만큼 흔든다 — 틈 너비가 매번 다르다(10-22 E5 페이지2)")]
    [SerializeField] private float _spacingJitter = 0.25f;

    // ── 런타임 ───────────────────────────────────────────
    private LichSealArrayState _state;

    public float SpacingJitter => Mathf.Clamp(_spacingJitter, 0f, 0.6f);

    public override void Initialize(BossPatternContext ctx) => _state = new LichSealArrayState(this);
    public override void OnRecycled()                       => _state = new LichSealArrayState(this);

    public override bool CanExecute(BossPatternContext ctx)
    {
        var lichBB = (ctx.Boss as LichMonster)?.LichBB;
        return lichBB != null && lichBB.Page >= 3 && lichBB.SealArrayCooldown <= 0f;
    }

    public override SpecialStateBase GetRuntimeState() => _state;
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// LichSealArrayState
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

public class LichSealArrayState : UnInterruptibleState<LichSealArrayPatternSO>
{
    private enum Phase { Open, Active, End, Recovery }

    private const int   MaxSpokes    = 8;
    private const float CoreOrbScale = 0.35f;

    private readonly GameObject[] _spokes  = new GameObject[MaxSpokes];
    private readonly float[]      _offsets = new float[MaxSpokes];   // 빛줄기마다 첫 줄에서 벌어진 각(도) — 고르지 않게(10-22 E5 페이지2)

    private Phase      _phase;
    private float      _timer;
    private float      _angle;       // 첫 빛줄기 방위(도)
    private float      _spin = 1f;   // +1 시계 / −1 반시계
    private float      _reverseTimer;
    private bool       _reverseWarned;
    private float      _lastHit = -99f;
    private Vector3    _center;
    private GameObject _arrayVfx;
    private GameObject _arrayDisc;
    private int        _count;

    public LichSealArrayState(LichSealArrayPatternSO data) : base(data) { }

    public override void Enter(MonsterContext ctx)
    {
        _phase         = Phase.Open;
        _timer         = 0f;
        _spin          = Random.value < 0.5f ? 1f : -1f;
        _reverseTimer  = 0f;
        _reverseWarned = false;
        _count         = Mathf.Clamp(Data.spokeCount, 2, MaxSpokes);

        var mc = LichPatternUtil.Mover(ctx);
        mc?.SetLocked(true);
        mc?.RequestMovementState(LichMovementState.AltitudeRise);

        var grid = ArenaTileGrid.Active;
        if (grid == null || !grid.TryGetWorldCenter(out _center))
            _center = mc != null ? mc.ArenaCenter : LichPatternUtil.OnFloor(ctx, ctx.Transform.position);

        // 빛줄기 사이 각을 시전마다 고르지 않게 — 틈 너비가 매번 다르다(10-22 E5 페이지2)
        float sum = 0f;
        for (int i = 0; i < _count; i++)
        {
            _offsets[i] = sum;
            sum += 1f + Random.Range(-Data.SpacingJitter, Data.SpacingJitter);
        }
        for (int i = 0; i < _count; i++) _offsets[i] *= 360f / sum;
        float lastGap = 360f - _offsets[_count - 1];   // 마지막 줄 → 첫 줄 사이

        // 빛줄기 사이(안전)가 플레이어 쪽에서 시작하게 — 공정한 첫 자리(마지막 틈 한가운데).
        Vector3 toPlayer = LichPatternUtil.PlayerFloorPos(ctx) - _center;
        toPlayer.y = 0f;
        float playerYaw = toPlayer.sqrMagnitude > 0.01f ? Quaternion.LookRotation(toPlayer).eulerAngles.y : 0f;
        _angle = playerYaw + lastGap * 0.5f;

        for (int i = 0; i < _count; i++)
        {
            _spokes[i] = LichPatternUtil.PrepareTelegraph(
                PatternGuideHelper.Beam(_center, Vector3.forward, Data.arrayRadius, Data.spokeWidth, LichPatternUtil.Arcane),
                LichPatternUtil.Arcane);
        }
        PlaceSpokes();

        // 봉인진 = 바닥의 보라 문양 원(반경 arrayRadius). 가운데 시계형 구체는 작게(크면 리치를 가린다 — 09-19 실측).
        // (10-23 가이드 색 통일) 원 자체는 판정이 없다(빛줄기만 친다) — 공격 색이 아닌 배치 색으로.
        _arrayDisc = PatternGuideHelper.Disc(_center + Vector3.up * 0.02f, Data.arrayRadius, PatternGuideHelper.Layout);
        PatternGuideHelper.SetIntensity(_arrayDisc, 0.6f);
        _arrayVfx = LichVfx.PlayLoop(LichVfxSlot.SealArray, _center + Vector3.up * 0.05f, BossStoryScenes.SigilFlat, CoreOrbScale);   // 칸 60 = 보스 봉인진 문양(10-11 이펙트 톤 2차)
        LichPatternUtil.CastBeat(ctx, LichCast.ArcaneOrb, Data.openDuration, 2f, 0.12f);
        LichPatternUtil.Lich(ctx)?.PulseBook(Data.openDuration + Data.activeDuration);
        LichSfx.Play(LichSfxSlot.ZoneHum, _center);
        // 멀린 말투 흉내 — 대사 CSV Lich_MerlinVoice 먼저(리치 이름표로 뜨고, 멀린의 「그건 내가 아니야!」가 잇는다)
        if (!UI_BossBark.ShowDialogue("Lich_MerlinVoice", BossBarkType.PatternAnnounce))
            UI_BossBark.Show("…작은 빛. 이쪽이야, 이리 와.", BossBarkType.PatternAnnounce);
    }

    public override void Update(MonsterContext ctx)
    {
        float dt = Time.deltaTime;
        _timer += dt;

        switch (_phase)
        {
            case Phase.Open:
                for (int i = 0; i < _count; i++) PatternGuideHelper.SetProgress(_spokes[i], _timer / Data.openDuration);
                if (_timer >= Data.openDuration)
                {
                    for (int i = 0; i < _count; i++)
                    {
                        PatternGuideHelper.SetColor(_spokes[i], LichPatternUtil.Lethal);
                        PatternGuideHelper.SetFlow(_spokes[i], LichPatternUtil.Lethal);
                    }
                    LichPatternProbe.Signal();
                    LichPatternUtil.Impact(LichImpact.Heavy);
                    Next(Phase.Active);
                }
                break;

            case Phase.Active:
                Rotate(dt);
                TickReverse(dt);
                TickHit(ctx);
                if (_timer >= Data.activeDuration)
                {
                    Clear();
                    LichPatternUtil.Lich(ctx)?.NotifyVulnerableWindow(Data.endDuration);
                    Next(Phase.End);
                }
                break;

            case Phase.End:
                if (_timer >= Data.endDuration) Next(Phase.Recovery);
                break;

            case Phase.Recovery:
                if (_timer >= LichPatternUtil.Recovery(ctx, Data.recoveryDuration))
                    ctx.Monster.ChangeState<ChaseState>();
                break;
        }
    }

    public override void Exit(MonsterContext ctx)
    {
        Clear();
        var lich = LichPatternUtil.Lich(ctx);
        var mc   = lich?.MovementController;
        mc?.RequestMovementState(LichMovementState.AltitudeDescend);
        mc?.SetLocked(false);
        if (lich?.LichBB != null)
            lich.LichBB.SealArrayCooldown = Data.patternCooldown;
    }

    private void Next(Phase phase)
    {
        _phase = phase;
        _timer = 0f;
    }

    private void Rotate(float dt)
    {
        _angle += _spin * 360f / Mathf.Max(0.5f, Data.revolutionSeconds) * dt;
        PlaceSpokes();
    }

    /// <summary>방향 반전 — 한 줄이 흰 빛으로 깜박이고 reverseWarn 뒤 뒤집힌다.</summary>
    private void TickReverse(float dt)
    {
        if (Data.reverseEvery <= 0f) return;
        _reverseTimer += dt;
        if (!_reverseWarned && _reverseTimer >= Data.reverseEvery - Data.reverseWarn)
        {
            _reverseWarned = true;
            PatternGuideHelper.SetColor(_spokes[0], LichPatternUtil.Lethal);   // 반전 경고는 빨강 — 흰색(안전)인데 피해가 들어갔다(10-13 검증)
            PatternGuideHelper.SetIntensity(_spokes[0], 3f);
            LichSfx.Play(LichSfxSlot.CastShort, _center, 0.8f);
        }
        if (_reverseTimer < Data.reverseEvery) return;
        _reverseTimer  = 0f;
        _reverseWarned = false;
        _spin         *= -1f;
        PatternGuideHelper.SetColor(_spokes[0], LichPatternUtil.Lethal);
        PatternGuideHelper.SetIntensity(_spokes[0], 1f);
    }

    /// <summary>플레이어가 빛줄기 위에 있으면 피해(간격마다).</summary>
    private void TickHit(MonsterContext ctx)
    {
        if (Time.time - _lastHit < Data.tickInterval) return;
        var target = ctx.Runtime.PlayerTarget;
        if (target == null) return;
        Vector3 to = target.position - _center;
        to.y = 0f;
        float d = to.magnitude;
        if (d > Data.arrayRadius || d < 0.5f) return;

        float yaw = Quaternion.LookRotation(to).eulerAngles.y;
        float halfWidthDeg = Mathf.Atan2(Data.spokeWidth * 0.5f, d) * Mathf.Rad2Deg;
        for (int i = 0; i < _count; i++)
        {
            float spoke = _angle + _offsets[i];
            if (Mathf.Abs(Mathf.DeltaAngle(yaw, spoke)) > halfWidthDeg) continue;
            _lastHit = Time.time;
            // 넉백은 빛줄기가 이미 지나간 옆으로 — 중심에서 바깥으로 밀면 빛줄기를 따라 미끄러져 그 위에 남았다(10-08 점검).
            Vector3 trailing = Vector3.Cross(Vector3.up, Quaternion.Euler(0f, spoke, 0f) * Vector3.forward) * -_spin;
            bool hit = LichPatternUtil.HitPlayer(ctx, target.position - trailing, Data.damageMultiplier, 0.6f);
            LichPatternUtil.Impact(LichImpact.Light, hit);
            return;
        }
    }

    private void PlaceSpokes()
    {
        for (int i = 0; i < _count; i++)
        {
            if (_spokes[i] == null) continue;
            Vector3 dir = Quaternion.Euler(0f, _angle + _offsets[i], 0f) * Vector3.forward;
            var t = _spokes[i].transform;
            t.position = _center + dir * (Data.arrayRadius * 0.5f) + Vector3.up * 0.04f;
            t.rotation = Quaternion.LookRotation(Vector3.down, dir);
        }
    }

    private void Clear()
    {
        for (int i = 0; i < MaxSpokes; i++) PatternGuideHelper.SafeDestroy(ref _spokes[i]);
        PatternGuideHelper.SafeDestroy(ref _arrayDisc);
        LichVfx.Stop(ref _arrayVfx, 0.4f);
    }
}
}
