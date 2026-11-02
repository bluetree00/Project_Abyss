using System.Collections.Generic;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using RelicFairy.Monster;
using UnityEngine;

/// <summary>
/// 룬 기억 — 외우기(최대 30초, 10-01 설계 §3-2). 「빛난 순서대로 밟아라」.
/// 가운데 둘레 반경 5 m에 룬 발판 4개(자리가 막히면 반경을 줄이고, 끝내 안 되면 3개). 발판마다 빛깔과 음이 다르다(0.8 · 1.0 · 1.2 · 1.5).
/// 2라운드 — 길이 3·4(Ch3·4는 4·5). 보여 주기(칸당 0.55초, 뒤 챕터 0.45초) → 밟기(발판 반경 1.4 m).
/// 10-29 3 → 2라운드 · 60 → 30초(「빨리 끝나는 이벤트」, 약 25초). 보상 = 근접 증강 다시 뽑기 +1(플래티넘 +2 · 이번 런) — 근접 무기가 없거나 실패면 원석.
/// 틀리면 낮은 불협음 + 발판이 붉게 떨리고, 같은 줄을 다시 보여 준다. 등급 = 틀린 횟수 0 / 1 / 2 / 3 / 4 이상.
/// 시간이 다 되면 브론즈(끝낸 라운드가 0이면 실패).
/// </summary>
public sealed class RuneMemoryGame : EventMinigame
{
    // ── Constants ──────────────────────────────────────────────
    private const float PlayTime    = 30f;
    private const float PlateRing   = 5f;
    private const float PlateRadius = 1.4f;
    private const float ShowGap     = 0.12f;   // 칸 사이 어두운 틈
    private const float BeforeShow  = 0.7f;
    private const float AfterMiss   = 0.9f;
    private const float AfterRound  = 1.0f;
    private const float DemoStepGap = 0.45f;
    // 10-30 카드 보상 줄 · 메달 아래 · 보상이 같은 값을 읽는다
    private const int   GoldMaxMistakes   = 1;   // 등급 문턱 = 틀린 횟수 상한(플래티넘 0 · 골드 1 · 실버 2 · 브론즈 3)
    private const int   SilverMaxMistakes = 2;
    private const int   BronzeMaxMistakes = 3;
    private const int   RerollTop         = 2;   // 근접 증강 다시 뽑기 — 플래티넘 +2 · 브론즈~골드 +1
    private const int   RerollBase        = 1;

    private static readonly Color[] PlateColors =
    {
        new Color(0.40f, 0.75f, 1.00f),   // 청
        new Color(0.50f, 1.00f, 0.55f),   // 녹
        new Color(1.00f, 0.82f, 0.35f),   // 금
        new Color(1.00f, 0.50f, 0.62f),   // 분홍
    };
    private static readonly float[] PlatePitches = { 0.8f, 1.0f, 1.2f, 1.5f };
    private const float BuildNoteVolume = 0.6f;   // 발판이 서며 내는 음은 작게(예전 0.5 / 0.8)
    private const float DimFactor = 0.35f;

    private enum Phase { Wait, Show, Input, Pause }

    // ── Private ────────────────────────────────────────────────
    private readonly List<Vector3>    _plates = new();
    private readonly List<GameObject> _discs  = new();
    private readonly List<int>        _seq    = new();
    private int[]  _lengths;
    private float  _stepTime;
    private Phase  _phase;
    private float  _timer;
    private int    _round, _showIndex, _inputIndex, _mistakes, _streak;
    private int    _underfoot = -1;
    private float  _plateR = PlateRadius;
    private bool   _lit;
    private float  _demoTimer;
    private bool   _demoMissed;
    private int    _statusKey = -1;   // 10-30 상태 줄은 단계 · 밟은 수 · 틀린 횟수가 바뀔 때만 다시 만든다(매 프레임 문자열 할당 금지)
    private string _statusText;

    // ── Properties ─────────────────────────────────────────────
    public override string Id => "memory";
    // 10-30 「룬 기억」은 룬을 주는 방 · 「유물 기억」으로 읽혔다(R-03) · 규칙은 명령조 대신 할 일과 판 수(M-12)
    protected override string Title => "발판 순서 외우기";
    protected override string Rule => Loc.F("발판이 빛난 순서를 외워 그대로 밟는다 · {0}판", _lengths?.Length ?? 2);
    /// <summary>10-30 M-13 — 등급 이름 대신 받는 것. 근접 무기가 없으면 원석만 나오니 그 줄로.</summary>
    protected override string GradeHint
    {
        get
        {
            if (EventRewards.HasMeleeWeapon(Run))
                return Loc.F("안 틀리면 <color=#8fd3ff>근접 증강 카드 다시 뽑기</color> +{0} · {1}번까지 틀리면 +{2} · 더 틀리면 원석", RerollTop, BronzeMaxMistakes, RerollBase);
            int ch = EventRewards.Chapter(Run);
            return Loc.F("적게 틀릴수록 <color=#73B3F2>원석</color>이 많다 — 0번 {0}", EventRewards.Ore(ChallengeGrade.Platinum, ch))
                 + Loc.F(" · {0}번 {1}", GoldMaxMistakes, EventRewards.Ore(ChallengeGrade.Gold, ch))
                 + Loc.F(" · {0}번 {1}", SilverMaxMistakes, EventRewards.Ore(ChallengeGrade.Silver, ch))
                 + Loc.F(" · {0}번 {1}", BronzeMaxMistakes, EventRewards.Ore(ChallengeGrade.Bronze, ch));
        }
    }
    protected override float Duration => PlayTime;
    protected override float WantRadius => 7f;
    public override ChallengeGrade CurrentGrade => GradeOf(_mistakes, _round, _lengths?.Length ?? 2, Remaining <= 0f);
    protected override bool FinishedEarly => _lengths != null && _round >= _lengths.Length;

    /// <summary>10-30 M-14 — 지금이 보는 때인지 밟는 때인지(예전 「잘 보고 들어라」 명령조).</summary>
    protected override string StatusLine
    {
        get
        {
            int key = (int)_phase | (_inputIndex << 3) | (_seq.Count << 10) | (_mistakes << 17);
            if (key == _statusKey) return _statusText;
            _statusKey = key;
            return _statusText = _phase switch
            {
                Phase.Show  => Loc.F("순서 보여 주는 중 · 틀린 횟수 {0}", _mistakes),
                Phase.Input => Loc.F("{0} / {1} 밟음 · 틀린 횟수 {2}", _inputIndex, _seq.Count, _mistakes),
                _           => Loc.F("틀린 횟수 {0}", _mistakes),
            };
        }
    }

    protected override string DotsLine
    {
        get
        {
            if (_lengths == null) return null;
            var sb = new StringBuilder(_lengths.Length);
            for (int i = 0; i < _lengths.Length; i++) sb.Append(i < _round ? '●' : '○');
            return sb.ToString();
        }
    }

    protected override string ResultLine => FinishedEarly
        ? (_mistakes == 0 ? "한 번도 안 틀렸다" : Loc.F("틀린 횟수 {0}", _mistakes))
        : Loc.F("{0} / {1}판 · 틀린 횟수 {2}", _round, _lengths?.Length ?? 2, _mistakes);   // 10-30 규칙 카드와 같은 「판」

    /// <summary>10-30 M-18 메달 아래 — 지금 등급이면 받는 것(GrantReward와 같은 갈래).</summary>
    protected override string GradeReward(ChallengeGrade grade)
    {
        if (grade >= ChallengeGrade.Bronze && EventRewards.HasMeleeWeapon(Run))
            return Loc.F("다시 뽑기 +{0}", (grade == ChallengeGrade.Platinum ? RerollTop : RerollBase));
        return Loc.F("원석 {0}", EventRewards.Ore(grade, EventRewards.Chapter(Run)));
    }

    // ── Public Methods ─────────────────────────────────────────

    /// <summary>
    /// 틀린 횟수 → 등급(0 / 1 / 2 / 3 / 4 이상). 시간이 다 됐으면 브론즈가 천장이고, 끝낸 라운드가 0이면 실패.
    /// </summary>
    public static ChallengeGrade GradeOf(int mistakes, int roundsDone, int roundsTotal, bool timedOut)
    {
        var g = GradeByMax(mistakes, 0, GoldMaxMistakes, SilverMaxMistakes, BronzeMaxMistakes);
        if (!timedOut || roundsDone >= roundsTotal) return g;
        if (roundsDone <= 0) return ChallengeGrade.Fail;
        return g < ChallengeGrade.Bronze ? g : ChallengeGrade.Bronze;
    }

    // ── Stage ──────────────────────────────────────────────────

    protected override async UniTask BuildStageAsync(CancellationToken ct)
    {
        _lengths  = Tier >= 2 ? new[] { 4, 5 } : new[] { 3, 4 };   // 10-29 3 → 2라운드
        _stepTime = Tier >= 2 ? 0.45f : 0.55f;

        // 좁은 판에서도 발판끼리 겹치지 않게 — 둘레가 좁으면 발판을 줄인다(설계 반경 5 m · 발판 1.4 m)
        float ring = Mathf.Clamp(Radius - PlateRadius, 2.4f, PlateRing);
        _plateR = Mathf.Min(PlateRadius, ring * 0.6f);
        var spots = MinigameArena.RingSpots(Center, ring, 4, 0f);
        if (spots.Count < 3) spots = MinigameArena.RingSpots(Center, ring * 0.7f, 3, 0f);
        if (spots.Count < 3)
        {
            spots.Clear();   // 끝내 못 구하면 가운데 둘레 3 m에 세 개(가운데는 반드시 선다)
            for (int i = 0; i < 3; i++) spots.Add(Center + Quaternion.Euler(0f, 120f * i, 0f) * Vector3.forward * 3f);
        }
        _plates.AddRange(spots);

        // 발판이 하나씩 차오르며 선다(자기 음)
        for (int i = 0; i < _plates.Count; i++)
        {
            var disc = PatternGuideHelper.Prepare(PatternGuideHelper.Disc(_plates[i], _plateR, Dim(i)), PlateColors[i]);
            Own(disc);
            _discs.Add(disc);
            RunFx.Play(RunFxSlot.Ring, _plates[i], 0.35f, PlateColors[i]);
            MinigameFx.Cue(SoundEvent.MgNote, PlatePitches[i], BuildNoteVolume);
            float t = 0f;
            while (t < 1f)
            {
                t = Mathf.Min(1f, t + Time.deltaTime / 0.15f);
                PatternGuideHelper.SetProgress(disc, t);
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
        }
        Debug.Log($"[Minigame] memory 발판 {_plates.Count}개 · 반경 {ring:0.0}m");
    }

    protected override void OnPlayStart()
    {
        NewSequence();
        BeginShow();
    }

    protected override void TickPlay(float dt)
    {
        _timer -= dt;
        switch (_phase)
        {
            case Phase.Show:  TickShow();  break;
            case Phase.Input: TickInput(); break;
            case Phase.Pause:
                if (_timer <= 0f) BeginShow();
                break;
        }
    }

    protected override void OnPlayEnd()
    {
        for (int i = 0; i < _discs.Count; i++) SetPlate(i, false);
        if (FinishedEarly)
        {
            // 네 발판에서 빛줄기가 가운데로 — 보상이 나올 자리
            for (int i = 0; i < _plates.Count; i++) RunFx.Play(RunFxSlot.Ring, _plates[i], 0.5f, PlateColors[i]);
            RunFx.Play(RunFxSlot.Pillar, Center, 0.08f, Theme);
        }
    }

    /// <summary>10-29 특별 보상 — 근접 증강 다시 뽑기(이번 런 · 브론즈~골드 +1 · 플래티넘 +2). 근접 무기가 없거나 실패면 원석.</summary>
    protected override void GrantReward(ChallengeGrade grade, Vector3 at)
    {
        if (grade >= ChallengeGrade.Bronze && EventRewards.HasMeleeWeapon(Run))
        {
            int n = grade == ChallengeGrade.Platinum ? RerollTop : RerollBase;
            MeleeAugmentDraft.RerollsLeft += n;   // 런 세이브는 「쓴 수」(RerollsPerRun − 남은 수)로 적어 음수로도 그대로 이어진다
            EventRewards.Notice(Loc.F("근접 증강 카드 다시 뽑기 <color=#8fd3ff>+{0}회</color>(이번 런)", n));   // 10-30 M-15 무엇을 다시 뽑는지
            return;
        }
        int ore = EventRewards.Ore(grade, EventRewards.Chapter(Run));
        EventRewards.AddOre(Run, ore);
        EventRewards.Notice(Loc.F("{0} — 원석 <color=#73B3F2>+{1}</color>", Title, ore));
    }

    protected override void DemoTick(MinigameDemo mode, float dt)
    {
        if (mode == MinigameDemo.Idle || _phase != Phase.Input) return;
        _demoTimer -= dt;
        if (_demoTimer > 0f) return;
        _demoTimer = DemoStepGap;

        int expected = _seq[_inputIndex];
        // 보통 — 둘째 라운드에서 한 번 틀린다
        if (mode == MinigameDemo.Normal && _round == 1 && !_demoMissed)
        {
            _demoMissed = true;
            OnStep((expected + 1) % _plates.Count);
            return;
        }
        OnStep(expected);
    }

    // ── Private Methods ────────────────────────────────────────

    private void NewSequence()
    {
        _seq.Clear();
        int len = _lengths[Mathf.Min(_round, _lengths.Length - 1)];
        int prev = -1;
        for (int i = 0; i < len; i++)
        {
            int k;
            do { k = Rng.Next(_plates.Count); } while (k == prev);
            _seq.Add(k);
            prev = k;
        }
    }

    private void BeginShow()
    {
        _phase     = Phase.Show;
        _showIndex = -1;
        _lit       = false;
        _timer     = BeforeShow;
    }

    private void TickShow()
    {
        if (_timer > 0f) return;

        if (_lit)
        {
            // 켜진 칸을 끄고 짧은 어둠
            SetPlate(_seq[_showIndex], false);
            _lit   = false;
            _timer = ShowGap;
            return;
        }

        _showIndex++;
        if (_showIndex >= _seq.Count)
        {
            _phase      = Phase.Input;
            _inputIndex = 0;
            _underfoot  = PlateUnder();   // 이미 서 있는 발판은 한 번 비켜 서야 센다
            _demoTimer  = DemoStepGap;
            MinigameFx.Tick(1.3f);
            return;
        }

        int k = _seq[_showIndex];
        SetPlate(k, true);
        RunFx.Play(RunFxSlot.Pillar, _plates[k], 0.04f, PlateColors[k]);
        MinigameFx.Cue(SoundEvent.MgNote, PlatePitches[k]);
        _lit   = true;
        _timer = _stepTime - ShowGap;
    }

    private void TickInput()
    {
        int now = PlateUnder();
        if (now != _underfoot && now >= 0) OnStep(now);
        _underfoot = now;
    }

    private void OnStep(int plate)
    {
        if (_phase != Phase.Input) return;

        if (plate == _seq[_inputIndex])
        {
            _streak++;
            FlashPlateAsync(plate, PlateColors[plate]).Forget();
            MinigameFx.Cue(SoundEvent.MgNote, PlatePitches[plate]);
            // 고리는 뺐다 — 같은 순간 발판 반짝임 · 음이 「맞았다」를 말한다(10-11 이펙트 톤 2차 V-14)
            _inputIndex++;
            if (_inputIndex >= _seq.Count) CompleteRound();
            return;
        }

        // 틀림 — 불협음 + 발판이 붉게 떨리고 같은 줄을 다시 보여 준다
        _mistakes++;
        _streak = 0;
        MinigameFx.Fail();
        Hud?.Float("틀렸다", MinigameFx.HurtColor);
        for (int i = 0; i < _discs.Count; i++) FlashPlateAsync(i, MinigameFx.HurtColor).Forget();
        _phase = Phase.Pause;
        _timer = AfterMiss;
    }

    private void CompleteRound()
    {
        _round++;
        PlayArpeggioAsync().Forget();
        if (_round >= _lengths.Length) return;   // FinishedEarly → 결말(같은 프레임 가운데 빛기둥이 맡는다 — 고리를 겹치지 않는다, 10-11 톤 점검)
        RunFx.Play(RunFxSlot.Ring, Center, 0.9f, Theme);
        NewSequence();
        _phase = Phase.Pause;
        _timer = AfterRound;
    }

    /// <summary>플레이어가 서 있는 발판(반경 1.4 m 안에서 가장 가까운). 없으면 −1.</summary>
    private int PlateUnder()
    {
        if (PlayerT == null) return -1;
        Vector3 p = PlayerT.position;
        int best = -1;
        float bestD = _plateR * _plateR;
        for (int i = 0; i < _plates.Count; i++)
        {
            Vector3 d = p - _plates[i]; d.y = 0f;
            float sq = d.sqrMagnitude;
            if (sq <= bestD) { bestD = sq; best = i; }
        }
        return best;
    }

    private void SetPlate(int i, bool lit)
    {
        if (i < 0 || i >= _discs.Count || _discs[i] == null) return;
        PatternGuideHelper.SetColor(_discs[i], lit ? PlateColors[i] : Dim(i));
        PatternGuideHelper.SetIntensity(_discs[i], lit ? 2.2f : 1f);
    }

    private Color Dim(int i) => Color.Lerp(new Color(0.08f, 0.08f, 0.1f), PlateColors[i], DimFactor);

    private async UniTaskVoid FlashPlateAsync(int i, Color color)
    {
        if (i < 0 || i >= _discs.Count) return;
        var disc = _discs[i];
        var ct = this.GetCancellationTokenOnDestroy();
        try
        {
            PatternGuideHelper.SetColor(disc, color);
            PatternGuideHelper.SetIntensity(disc, 2.2f);
            await UniTask.Delay(220, cancellationToken: ct);
            if (disc != null && !(_phase == Phase.Show && _lit && _seq.Count > _showIndex && _showIndex >= 0 && _seq[_showIndex] == i))
                SetPlate(i, false);
        }
        catch (System.OperationCanceledException) { }
    }

    private async UniTaskVoid PlayArpeggioAsync()
    {
        var ct = this.GetCancellationTokenOnDestroy();
        try
        {
            for (int i = 0; i < _plates.Count; i++)
            {
                MinigameFx.Cue(SoundEvent.MgNote, PlatePitches[i]);
                SetPlate(i, true);
                await UniTask.Delay(110, cancellationToken: ct);
            }
            await UniTask.Delay(200, cancellationToken: ct);
            for (int i = 0; i < _plates.Count; i++) SetPlate(i, false);
        }
        catch (System.OperationCanceledException) { }
    }
}
