using UnityEngine;

/// <summary>
/// 전투 챌린지 오버레이 — 기존 RoomWaveController 클리어에 성과를 얹어 ChallengeGrade 산출.
/// 이벤트 전투방 GO에 부착(SetupEventRoom). 클리어 직전 RoomWaveController가 EvaluateGrade()를 조회해 보상에 반영.
/// 최소 침습: 컨트롤러 웨이브 로직 무수정, 타이머·HP(PlayerRunState.OnHpChanged)만 관찰.
///
/// 성과 지표 5종:
///  - TimeLimit(속공): 제한초 대비 경과 — 빠를수록 ↑
///  - Hitless(무결):   피격 수 — 적을수록 ↑
///  - Survival(생존):  클리어 시 남은 HP% — 높을수록 ↑ (방어 숙련)
///  - NoHeal(고행):    회복 봉인(HealLocked) + 남은 HP% — 회복 없이 버틸수록 ↑
///  - Berserk(배수진): 전투 내내 평균 HP% — 낮게 유지할수록 ↑ (고위험·고보상)
/// </summary>
public sealed class CombatChallengeOverlay : MonoBehaviour
{
    public enum OverlayType { TimeLimit, Hitless, Survival, NoHeal, Berserk }

    private OverlayType    _type;
    private float          _param;       // TimeLimit=제한초 / Hitless=허용 피격 수 / 그 외 미사용
    private float          _startTime;
    private int            _hitCount;
    private int            _lastHp = -1;
    private int            _lastMaxHp = -1;
    private float          _hpIntegral;   // Berserk: Σ(HP% · dt)
    private float          _timeIntegral; // Berserk: Σ dt
    private bool           _tracking;
    private PlayerRunState _playerState;
    private UI_ChallengeHud _hud;

    public OverlayType Type      => _type;
    public float       Param     => _param;
    public float       Elapsed   => Mathf.Max(0f, Time.time - _startTime);
    public int         HitCount  => _hitCount;

    /// <summary>SetupEventRoom이 방 빌드 직후 호출. run=현재 세션, type=오버레이 종류, param=제한초/허용피격.</summary>
    public void Initialize(GameRunSession run, OverlayType type, float param)
    {
        _type      = type;
        _param     = param;
        _startTime = Time.time;
        _tracking  = true;

        _playerState = run?.PlayerState;
        if (_playerState != null)
        {
            _lastHp    = _playerState.Hp;
            _lastMaxHp = _playerState.MaxHp;
            _playerState.OnHpChanged += HandleHpChanged;
            if (_type == OverlayType.NoHeal) _playerState.HealLocked = true;   // 회복 봉인
        }

        _hud = UI_ChallengeHud.Create();
        _hud.SetObjective(ObjectiveText());
    }

    private void Update()
    {
        if (!_tracking) return;

        // Berserk 평균 HP% 적산 — 저체력 유지를 보상.
        if (_type == OverlayType.Berserk && _lastMaxHp > 0)
        {
            float dt = Time.deltaTime;
            _hpIntegral   += HpPct() * dt;
            _timeIntegral += dt;
        }

        if (_hud != null) _hud.SetStatus(StatusText());
    }

    private void OnDestroy()
    {
        if (_playerState != null)
        {
            _playerState.OnHpChanged -= HandleHpChanged;
            if (_type == OverlayType.NoHeal) _playerState.HealLocked = false;  // 봉인 해제 보장
        }
        _hud?.Close();
    }

    /// <summary>클리어 시점 RoomWaveController가 조회 — 성과 → 등급. HUD 종료 + 회복 봉인 해제.</summary>
    public ChallengeGrade EvaluateGrade()
    {
        _tracking = false;
        if (_playerState != null && _type == OverlayType.NoHeal) _playerState.HealLocked = false;
        var grade = Compute();
        _hud?.Close();
        _hud = null;
        return grade;
    }

    private ChallengeGrade Compute() => _type switch
    {
        OverlayType.TimeLimit => GradeByTime(Elapsed),
        OverlayType.Hitless   => GradeByHits(_hitCount),
        OverlayType.Survival  => GradeBySurvival(HpPct()),
        OverlayType.NoHeal    => GradeByNoHeal(HpPct()),
        OverlayType.Berserk   => GradeByBerserk(AvgPct()),
        _                     => ChallengeGrade.Silver,
    };

    private string ObjectiveText() => _type switch
    {
        OverlayType.TimeLimit => $"[속공] 제한시간 {_param:0}초 — 빠를수록 높은 등급",
        OverlayType.Hitless   => $"[무결] 피격 {Mathf.RoundToInt(_param)}회 이하 — 적을수록 높은 등급",
        OverlayType.Survival  => "[생존] 높은 체력으로 클리어 — 남은 HP가 많을수록 높은 등급",
        OverlayType.NoHeal    => "[고행] 회복 봉인 — 회복 없이 체력을 지킬수록 높은 등급",
        OverlayType.Berserk   => "[배수진] 저체력 유지 — 위험할수록 높은 등급",
        _                     => "챌린지",
    };

    private string StatusText() => _type switch
    {
        OverlayType.TimeLimit => $"경과 {Elapsed:0.0}초 · {GradeName(Compute())}",
        OverlayType.Hitless   => $"피격 {_hitCount}회 · {GradeName(Compute())}",
        OverlayType.Survival  => $"HP {Mathf.RoundToInt(HpPct() * 100f)}% · {GradeName(Compute())}",
        OverlayType.NoHeal    => $"HP {Mathf.RoundToInt(HpPct() * 100f)}% (회복봉인) · {GradeName(Compute())}",
        OverlayType.Berserk   => $"평균 HP {Mathf.RoundToInt(AvgPct() * 100f)}% · {GradeName(Compute())}",
        _                     => "",
    };

    private static string GradeName(ChallengeGrade g) => g switch
    {
        ChallengeGrade.Platinum => "플래티넘",
        ChallengeGrade.Gold     => "골드",
        ChallengeGrade.Silver   => "실버",
        ChallengeGrade.Bronze   => "브론즈",
        _                       => "실패",
    };

    // ── Event Handlers ────────────────────────────────────
    private void HandleHpChanged(int hp, int maxHp)
    {
        if (!_tracking) return;
        if (_lastHp >= 0 && hp < _lastHp) _hitCount++;   // HP 감소만 = 피격(힐/장판 회복 제외)
        _lastHp    = hp;
        _lastMaxHp = maxHp;
    }

    // ── Private Methods ───────────────────────────────────
    private float HpPct()  => _lastMaxHp > 0 ? Mathf.Clamp01((float)_lastHp / _lastMaxHp) : 1f;
    private float AvgPct() => _timeIntegral > 0f ? _hpIntegral / _timeIntegral : HpPct();

    private ChallengeGrade GradeByTime(float elapsed)
    {
        if (_param <= 0f) return ChallengeGrade.Silver;
        float r = elapsed / _param;
        if (r <= 0.5f)  return ChallengeGrade.Platinum;
        if (r <= 0.75f) return ChallengeGrade.Gold;
        if (r <= 1.0f)  return ChallengeGrade.Silver;
        if (r <= 1.3f)  return ChallengeGrade.Bronze;
        return ChallengeGrade.Fail;
    }

    private ChallengeGrade GradeByHits(int hits)
    {
        int allow = Mathf.Max(1, Mathf.RoundToInt(_param));
        if (hits <= 0)         return ChallengeGrade.Platinum;
        if (hits <= 1)         return ChallengeGrade.Gold;
        if (hits <= allow)     return ChallengeGrade.Silver;
        if (hits <= allow + 2) return ChallengeGrade.Bronze;
        return ChallengeGrade.Fail;
    }

    // 클리어=생존 성공 → 최저 Bronze(margin 등급). 남은 HP% 높을수록 ↑
    private ChallengeGrade GradeBySurvival(float pct)
    {
        if (pct >= 0.85f) return ChallengeGrade.Platinum;
        if (pct >= 0.65f) return ChallengeGrade.Gold;
        if (pct >= 0.40f) return ChallengeGrade.Silver;
        return ChallengeGrade.Bronze;
    }

    // 회복 봉인이라 더 후한 곡선.
    private ChallengeGrade GradeByNoHeal(float pct)
    {
        if (pct >= 0.70f) return ChallengeGrade.Platinum;
        if (pct >= 0.50f) return ChallengeGrade.Gold;
        if (pct >= 0.28f) return ChallengeGrade.Silver;
        return ChallengeGrade.Bronze;
    }

    // 평균 HP%가 낮을수록 ↑ (위험 보상). 사망 시엔 방이 클리어되지 않아 여기 도달 안 함.
    private ChallengeGrade GradeByBerserk(float avg)
    {
        if (avg <= 0.25f) return ChallengeGrade.Platinum;
        if (avg <= 0.40f) return ChallengeGrade.Gold;
        if (avg <= 0.60f) return ChallengeGrade.Silver;
        return ChallengeGrade.Bronze;
    }
}
