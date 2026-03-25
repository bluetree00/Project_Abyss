using Abyss.Monster;
using UnityEngine;

/// <summary>
/// BT 리프 노드 — SpinSlash (360° 연속 회전 참격).
/// FullLockState: 실행 중 피격 차단.
///
/// CanExecute: HP 가 spinHpThresholds 중 아직 발동 안 된 구간 이하
/// 완료 시 직접 ChaseState 로 복귀.
///
/// ━━ 타이밍 (spinDuration 기준) ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
///  Phase 1 — 차지   ( 0% ~ 20%) : 가속 맥동 + 경고 장판
///  Phase 2 — 회전   (20% ~ 80%) : 연속 AoE + 진행도에 따라 가속하는 섬광
///  Phase 3 — 마무리 (80% ~100%) : 붉은 여운 소멸
/// </summary>
public class BKSpinSlashState : FullLockState<BlackKnightPatternData>
{
    private readonly BossAttackBlackboard _bb;

    private static readonly int Color07Id = Shader.PropertyToID("_Color07");
    private static readonly int Color08Id = Shader.PropertyToID("_Color08");

    // 색상 상수
    private static readonly Color ColCharge1   = new Color(0.5f, 0.02f, 0.02f, 1f);
    private static readonly Color ColCharge2   = new Color(1.0f, 0.08f, 0.08f, 1f);
    private static readonly Color ColSpinPeak  = new Color(1.0f, 0.40f, 0.05f, 1f); // 스핀 후반 주황빛
    private static readonly Color ColHitFlash  = new Color(1.0f, 1.00f, 1.00f, 1f);
    private static readonly Color ColAfter     = new Color(0.4f, 0.02f, 0.02f, 1f);

    private float      _timer;
    private float      _hitIntervalTimer;
    private bool       _spinning;
    private Renderer[] _renderers;

    private int _nextThresholdIndex;

    private BossWarningIndicator _indicator;

    public BKSpinSlashState(BlackKnightPatternData data, BossAttackBlackboard bb) : base(data)
    {
        _bb = bb;
    }

    public void ResetThresholds() => _nextThresholdIndex = 0;

    // ── BT 조건 ──────────────────────────────────────────
    public bool CanExecute(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null) return false;

        var thresholds = Data.spinHpThresholds;
        if (thresholds == null || _nextThresholdIndex >= thresholds.Length) return false;

        float hpRatio = (float)ctx.Runtime.CurrentHp / ctx.Config.stat.maxHp;
        if (hpRatio > thresholds[_nextThresholdIndex]) return false;

        float dist = Vector3.Distance(ctx.Transform.position, ctx.Runtime.PlayerTarget.position);
        return dist <= Data.spinRadius;
    }

    /// <summary>
    /// HP 임계값 충족 여부만 확인 (거리 무관).
    /// 패턴 인터럽트 판정에 사용 — 현재 패턴이 끝나는 즉시 SpinSlash 로 강제 진입할지 결정.
    /// </summary>
    public bool IsHpThresholdMet(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null) return false;
        var thresholds = Data.spinHpThresholds;
        if (thresholds == null || _nextThresholdIndex >= thresholds.Length) return false;
        float hpRatio = (float)ctx.Runtime.CurrentHp / ctx.Config.stat.maxHp;
        return hpRatio <= thresholds[_nextThresholdIndex];
    }

    // ── FSM Enter/Update/Exit ────────────────────────────
    public override void Enter(MonsterContext ctx)
    {
        _timer            = Data.spinDuration;
        _hitIntervalTimer = 0f;
        _spinning         = false;
        _renderers        = ctx.Transform.GetComponentsInChildren<Renderer>(true);
        _indicator        = ctx.Transform.GetComponent<BossWarningIndicator>();

        _bb.AudioPool?.Play(ctx.Transform.position, Data.spinSfx, 0.5f);

        ctx.Agent.ResetPath();

        // 경고 장판: 차지 구간 동안만 표시
        _indicator?.ShowCircle(ctx.Transform, Data.spinRadius, Data.spinDuration * Data.spinChargeRatio);

        string ctrlName = ctx.Animator != null
            ? (ctx.Animator.runtimeAnimatorController?.name ?? "컨트롤러 없음")
            : "Animator null";
        Debug.Log($"[SpinSlash] ▶ Enter | anim={Data.spinAnimState} | ctrl={ctrlName} | HP={ctx.Runtime.CurrentHp}/{ctx.Config.stat.maxHp}");

        ctx.Animator?.CrossFade(Data.spinAnimState, 0.1f);
    }

    public override void Update(MonsterContext ctx)
    {
        float dt      = Time.deltaTime;
        _timer       -= dt;
        float elapsed = Data.spinDuration - _timer;

        float spinStart = Data.spinDuration * Data.spinChargeRatio;
        float spinEnd   = Data.spinDuration * (1f - Data.spinWindDownRatio);

        // ── 회전 구간 진입 ──────────────────────────────
        if (!_spinning && elapsed >= spinStart)
        {
            _spinning         = true;
            _hitIntervalTimer = 0f; // 즉시 첫 히트
            // 장판 유지 — 스핀 구간 동안 처음부터 최대 밝기로 재활성
            float spinPhaseDuration = spinEnd - spinStart;
            _indicator?.ShowCircle(ctx.Transform, Data.spinRadius, spinPhaseDuration,
                                   colorFloor: 1f);
        }

        // ── 연속 히트 + 장판 반지름 성장 (회전 구간만) ──
        if (_spinning && elapsed < spinEnd)
        {
            _hitIntervalTimer -= dt;
            if (_hitIntervalTimer <= 0f)
            {
                _hitIntervalTimer = Data.spinHitInterval;
                DealAoeDamage(ctx);
            }

            _indicator?.UpdateCircleRadius(CalcSpinRadius(elapsed, spinStart, spinEnd));
        }

        // ── 시각 연출 ────────────────────────────────────
        ApplyTint(_renderers, CalcTint(elapsed, spinStart, spinEnd));

        if (_timer <= 0f)
            ctx.Monster.ChangeState<ChaseState>();
    }

    public override void Exit(MonsterContext ctx)
    {
        _indicator?.HideCircle();
        ClearTint(_renderers);
        _nextThresholdIndex++;
        // 스핀 슬래시 완료마다 운석 페이즈 상승 (최대 2)
        if (_bb.RainPhase < 2) _bb.RainPhase++;
    }

    // ── 장판 반지름 3단계 성장 ───────────────────────────
    /// <summary>
    /// 스핀 구간 진행도에 따라 반지름을 3단계로 키운다.
    ///  Stage 0 (0%~ 33%): spinRadius      → spinRadius * 1.5
    ///  Stage 1 (33%~66%): spinRadius * 1.5 → spinRadius * 1.9
    ///  Stage 2 (66%~100%): spinRadius * 1.9 → spinRadius * 2.4
    /// 각 구간은 SmoothStep 으로 부드럽게 보간한다.
    /// </summary>
    private float CalcSpinRadius(float elapsed, float spinStart, float spinEnd)
    {
        float progress = (elapsed - spinStart) / (spinEnd - spinStart); // 0→1
        float stage    = progress * 3f;
        int   stageIdx = Mathf.Min((int)stage, 2);
        float stageT   = Mathf.SmoothStep(0f, 1f, stage - stageIdx);

        float[] radii =
        {
            Data.spinRadius,
            Data.spinRadius * Data.spinRadiusMul1,
            Data.spinRadius * Data.spinRadiusMul2,
            Data.spinRadius * Data.spinRadiusMul3,
        };
        return Mathf.Lerp(radii[stageIdx], radii[stageIdx + 1], stageT);
    }

    // ── 단계별 틴트 계산 ─────────────────────────────────
    /// <summary>
    /// Phase 1: 가속 맥동 (어두운 붉→밝은 붉).
    /// Phase 2: 히트 섬광(흰→붉) + 진행에 따라 주황빛으로 달아오름.
    ///          맥동 주기가 점점 짧아져 회전감 표현.
    /// Phase 3: 붉은 여운 → 소멸.
    /// </summary>
    private Color CalcTint(float elapsed, float spinStart, float spinEnd)
    {
        float flashDuration = Data.spinFlashDuration;

        if (elapsed < spinStart)
        {
            // Phase 1 — 가속 맥동: 초반엔 느리고, 임박할수록 빠르게
            float t     = elapsed / spinStart;                                            // 0→1
            float freq  = Mathf.Lerp(Data.spinChargeFreqMin, Data.spinChargeFreqMax, t); // Hz 증가
            float pulse = (Mathf.Sin(elapsed * freq) + 1f) * 0.5f;
            return Color.Lerp(ColCharge1, ColCharge2, t * 0.5f + pulse * 0.5f);
        }
        else if (elapsed < spinEnd)
        {
            // Phase 2 — 회전 중
            float progress = (elapsed - spinStart) / (spinEnd - spinStart); // 0→1
            float cycleT   = elapsed % Data.spinHitInterval;

            if (cycleT < flashDuration)
            {
                // 히트 순간: 흰색 → 붉은 섬광
                float ft = cycleT / flashDuration;
                return Color.Lerp(ColHitFlash, Color.Lerp(ColCharge2, ColSpinPeak, progress), ft);
            }

            // 히트 사이: 진행도에 따라 밝아지며 주황빛으로 달아오름
            // 맥동 주기도 점점 빨라져 회전하는 느낌을 강조
            float pulseFreq  = Mathf.Lerp(Data.spinSpinFreqMin, Data.spinSpinFreqMax, progress);
            float pulse      = (Mathf.Sin(elapsed * pulseFreq) + 1f) * 0.5f;
            Color baseColor  = Color.Lerp(ColCharge2, ColSpinPeak, progress);
            Color peakColor  = Color.Lerp(ColCharge2, ColHitFlash, progress * 0.3f);
            return Color.Lerp(baseColor, peakColor, pulse * 0.4f);
        }
        else
        {
            // Phase 3 — 마무리: 주황빛 여운에서 소멸
            float fadeDuration = Data.spinDuration * Data.spinWindDownRatio;
            float t = Mathf.Clamp01((elapsed - spinEnd) / fadeDuration);
            return Color.Lerp(ColAfter, Color.clear, t);
        }
    }

    // ── AoE 데미지 ───────────────────────────────────────
    private void DealAoeDamage(MonsterContext ctx)
    {
        var colliders = Physics.OverlapSphere(ctx.Transform.position, Data.spinRadius);
        foreach (var col in colliders)
        {
            if (col.transform.IsChildOf(ctx.Transform) || col.transform == ctx.Transform) continue;

            var player = col.GetComponent<PlayerController>()
                      ?? col.GetComponentInParent<PlayerController>();
            if (player == null) continue;

            int dmg = Mathf.RoundToInt(ctx.Config.stat.attackPower
                                       * Data.spinDamageMul
                                       * ctx.Runtime.AttackMultiplier);
            player.TakeDamage(dmg);

            var rb = col.GetComponent<Rigidbody>() ?? col.GetComponentInParent<Rigidbody>();
            if (rb != null)
            {
                Vector3 dir = (col.transform.position - ctx.Transform.position).normalized;
                dir.y = Data.spinKnockbackY;
                rb.AddForce(dir.normalized * ctx.Config.stat.knockbackForce * Data.spinKnockbackMul, ForceMode.Impulse);
            }
        }
    }

    // ── 틴트 적용 ────────────────────────────────────────
    private static void ApplyTint(Renderer[] renderers, Color color)
    {
        if (renderers == null) return;
        var mpb = new MaterialPropertyBlock();
        foreach (var r in renderers)
        {
            r.GetPropertyBlock(mpb);
            if (r.sharedMaterial != null)
            {
                if (r.sharedMaterial.HasProperty(Color07Id)) mpb.SetColor(Color07Id, color);
                if (r.sharedMaterial.HasProperty(Color08Id)) mpb.SetColor(Color08Id, color);
            }
            r.SetPropertyBlock(mpb);
        }
    }

    private static void ClearTint(Renderer[] renderers)
    {
        if (renderers == null) return;
        var mpb = new MaterialPropertyBlock();
        foreach (var r in renderers)
        {
            r.GetPropertyBlock(mpb);
            mpb.Clear();
            r.SetPropertyBlock(mpb);
        }
    }
}
