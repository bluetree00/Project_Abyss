using UnityEngine;

/// <summary>
/// 전투 챌린지 오버레이 — 기존 RoomWaveController 클리어에 성과(속공/무결)를 얹어 ChallengeGrade 산출.
/// 이벤트 전투방 GO에 부착(SetupEventRoom). 클리어 직전 RoomWaveController가 EvaluateGrade()를 조회해 보상에 반영.
/// 최소 침습: 컨트롤러 웨이브 로직 무수정, 타이머(부착 시점~클리어)·피격(PlayerRunState.OnHpChanged 감소분)만 관찰.
/// </summary>
public sealed class CombatChallengeOverlay : MonoBehaviour
{
    public enum OverlayType { TimeLimit, Hitless }

    private OverlayType    _type;
    private float          _param;       // TimeLimit=제한초 / Hitless=허용 피격 수
    private float          _startTime;
    private int            _hitCount;
    private int            _lastHp = -1;
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
            _playerState.OnHpChanged += HandleHpChanged;

        _hud = UI_ChallengeHud.Create();
        _hud.SetObjective(ObjectiveText());
    }

    private void Update()
    {
        if (_tracking && _hud != null) _hud.SetStatus(StatusText());
    }

    private void OnDestroy()
    {
        if (_playerState != null) _playerState.OnHpChanged -= HandleHpChanged;
        _hud?.Close();
    }

    /// <summary>클리어 시점 RoomWaveController가 조회 — 성과 → 등급. HUD 종료.</summary>
    public ChallengeGrade EvaluateGrade()
    {
        _tracking = false;
        var grade = Compute();
        _hud?.Close();
        _hud = null;
        return grade;
    }

    private ChallengeGrade Compute() => _type switch
    {
        OverlayType.TimeLimit => GradeByTime(Elapsed),
        OverlayType.Hitless   => GradeByHits(_hitCount),
        _                     => ChallengeGrade.Silver,
    };

    private string ObjectiveText() => _type switch
    {
        OverlayType.TimeLimit => $"[속공] 제한시간 {_param:0}초 — 빠를수록 높은 등급",
        OverlayType.Hitless   => $"[무결] 피격 {Mathf.RoundToInt(_param)}회 이하 — 적을수록 높은 등급",
        _                     => "챌린지",
    };

    private string StatusText() => _type switch
    {
        OverlayType.TimeLimit => $"경과 {Elapsed:0.0}초 · {GradeName(Compute())}",
        OverlayType.Hitless   => $"피격 {_hitCount}회 · {GradeName(Compute())}",
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
        _lastHp = hp;
    }

    // ── Private Methods ───────────────────────────────────
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
}
