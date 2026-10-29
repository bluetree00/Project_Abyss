using System.Threading;
using Cysharp.Threading.Tasks;
using RelicFairy.Monster;
using TMPro;
using UnityEngine;

/// <summary>
/// 욕심의 상자 — 멈출 때 정하기(최대 30초, 10-01 설계 §3-3). 「열수록 커진다 — 언제 멈출지는 네가 정한다」.
/// 상자 앞 [F] = 열기 → 0.8초 두근거림 → 성공이면 단계 +1(다음 확률 85 / 70 / 55 / 40%), 실패면 상자가 문다(4% 피해 · 실패).
/// 「챙기기」 빛 원에서 [F] = 지금 단계로 끝(0 브론즈 · 1 실버 · 2 골드 · 3 플래티넘 · 4 플래티넘). 시간이 다 되면 자동으로 챙긴다.
/// 10-29 보상 = 금화(단계마다 는다 · 4단계 ×1.5) + 4단계면 정수도(EventRewards). 카운트다운 없이 바로 시작 · 최대 45 → 30초(「빨리 끝나는 이벤트」).
/// 운은 방 시드로 정해져 있다(다시 들어와도 같다 — 기존 도박 상자 규칙).
/// 상자 모델 = 의태 상자 몬스터 프리팹을 <b>소품으로</b>(스크립트 · 에이전트 · 충돌체를 떼고 애니메이터만 쓴다).
/// </summary>
public sealed class GreedChestGame : EventMinigame
{
    // ── Constants ──────────────────────────────────────────────
    private const float PlayTime     = 30f;
    private const float OpenRange    = 2.8f;
    private const float BankRange    = 1.6f;
    private const float BankDistance = 2.5f;
    private const float Heartbeat    = 0.8f;
    private const float BiteRange    = 3.5f;
    private const float RiseSeconds  = 0.45f;
    private const float RiseDepth    = 1.2f;
    private const int   MaxStage     = 4;
    private const float TopGoldScale = 1.5f;   // 4단계까지 열었다 — 금화 ×1.5 + 정수

    private const string IdleState  = "IdleChest";
    private const string ShakeState = "SenseSomethingST";
    private const string BiteState  = "Attack01";
    private const string TauntState = "Taunting";

    private static readonly float[] Chances = { 0.85f, 0.70f, 0.55f, 0.40f };
    private static readonly Color   GoldLight = new Color(1f, 0.82f, 0.3f);

    private enum State { Idle, Opening, Done }

    // ── Private ────────────────────────────────────────────────
    private readonly bool[] _outcomes = new bool[MaxStage];
    private State      _state;
    private int        _stage;
    private bool       _bitten, _banked;
    private float      _openTimer, _nextBeat;
    private Vector3    _chestPos, _bankPos;
    private GameObject _chest, _glow, _bankDisc;
    private Animator   _anim;
    private TextMeshPro _chance, _openPrompt, _bankLabel, _bankPrompt;
    private Transform  _camT;
    private float      _demoTimer;
    private bool       _demoForce;

    // ── Properties ─────────────────────────────────────────────
    public override string Id => "greed";
    protected override string Title => "욕심의 상자";
    protected override string Rule => "열수록 커진다 — 언제 멈출지는 네가 정한다";
    protected override string GradeHint => "멈추면 그 단계만큼 금화 · 4단까지 열면 정수도 · 실패하면 물린다";
    protected override float Duration => PlayTime;
    protected override float WantRadius => 6f;
    protected override bool FinishedEarly => _state == State.Done;
    protected override Vector3 RewardSpot => _chestPos;
    protected override bool UseCountdown => false;   // 10-29 상자 앞에 서면 바로 — 카운트다운이 기다림만 늘렸다
    public override ChallengeGrade CurrentGrade => _bitten ? ChallengeGrade.Fail : GradeOf(_stage);

    protected override string StatusLine
    {
        get
        {
            if (_bitten) return "물렸다 — 욕심이 과했다";
            if (_stage >= MaxStage) return "최고 단계 — 금화 ×1.5 · 정수";
            return $"지금 챙기면 {MinigameHud.GradeName(GradeOf(_stage))} · 다음 열기 성공 {Mathf.RoundToInt(Chances[_stage] * 100f)}%";
        }
    }

    protected override string ResultLine => _bitten ? $"{_stage}단에서 물렸다" : $"{_stage}단에서 챙겼다";

    // ── Public Methods ─────────────────────────────────────────

    /// <summary>챙긴 단계 → 등급(0 브론즈 · 1 실버 · 2 골드 · 3~4 플래티넘).</summary>
    public static ChallengeGrade GradeOf(int stage) => stage switch
    {
        <= 0 => ChallengeGrade.Bronze,
        1    => ChallengeGrade.Silver,
        2    => ChallengeGrade.Gold,
        _    => ChallengeGrade.Platinum,
    };

    // ── Lifecycle ──────────────────────────────────────────────
    private void LateUpdate()
    {
        if (_camT == null) return;
        Face(_chance); Face(_openPrompt); Face(_bankLabel); Face(_bankPrompt);
    }

    // ── Stage ──────────────────────────────────────────────────

    protected override async UniTask BuildStageAsync(CancellationToken ct)
    {
        // 운은 방 시드로 미리 정한다(다시 들어와도 같다)
        for (int i = 0; i < MaxStage; i++) _outcomes[i] = Rng.NextDouble() < Chances[i];

        _camT = Camera.main != null ? Camera.main.transform : null;
        _chestPos = Center;
        Vector3 toPlayer = PlayerT.position - Center; toPlayer.y = 0f;
        Vector3 dir = toPlayer.sqrMagnitude > 0.01f ? toPlayer.normalized : Vector3.back;
        _bankPos = MinigameArena.Snap(Center + dir * BankDistance, 1f, out var b) ? b : Center + dir * BankDistance;

        _chest = Own(await SpawnChestPropAsync(_chestPos, Quaternion.LookRotation(dir), RiseDepth, ct));
        _anim  = _chest != null ? _chest.GetComponentInChildren<Animator>() : null;
        Play(IdleState);

        _glow = Own(PatternGuideHelper.Disc(_chestPos, 1.0f, GoldLight));
        _bankDisc = Own(PatternGuideHelper.Prepare(PatternGuideHelper.Disc(_bankPos, BankRange, Theme), Theme));
        PatternGuideHelper.SetProgress(_bankDisc, 1f);

        _chance     = Label(_chestPos + Vector3.up * 2.6f, "", 6f, GoldLight);
        _openPrompt = Label(_chestPos + Vector3.up * 1.7f, $"<color={UIPalette.GoldHex}>[F]</color> 열기", 4f, Color.white);
        _bankLabel  = Label(_bankPos + Vector3.up * 1.2f, "챙기기", 4.5f, Theme);
        _bankPrompt = Label(_bankPos + Vector3.up * 1.8f, $"<color={UIPalette.GoldHex}>[F]</color> 챙기기", 4f, Color.white);
        _openPrompt.gameObject.SetActive(false);
        _bankPrompt.gameObject.SetActive(false);
        RefreshChance();

        // 상자가 바닥에서 솟는다
        RunFx.Play(RunFxSlot.Ring, _chestPos, 0.6f, GoldLight);
        if (_chest != null)
        {
            float t = 0f;
            while (t < 1f)
            {
                t = Mathf.Min(1f, t + Time.deltaTime / RiseSeconds);
                float e = 1f - (1f - t) * (1f - t);
                _chest.transform.position = _chestPos + Vector3.down * (RiseDepth * (1f - e));
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
        }
    }

    protected override void TickPlay(float dt)
    {
        switch (_state)
        {
            case State.Idle:    TickIdle();       break;
            case State.Opening: TickOpening(dt);  break;
        }
    }

    protected override void OnPlayEnd()
    {
        if (_state != State.Done) Bank();   // 시간이 다 됐다 — 지금 단계로 챙긴다
        _openPrompt?.gameObject.SetActive(false);
        _bankPrompt?.gameObject.SetActive(false);
    }

    /// <summary>10-29 특별 보상 — 챙긴 단계의 금화(물리면 실패 몫) · 4단계면 ×1.5와 정수.</summary>
    protected override void GrantReward(ChallengeGrade grade, Vector3 at)
    {
        int ch   = EventRewards.Chapter(Run);
        bool top = _stage >= MaxStage && !_bitten;
        int gold = EventRewards.Gold(grade, ch);
        if (top) gold = Mathf.RoundToInt(gold * TopGoldScale);
        int essence = top ? EventRewards.Essence(ChallengeGrade.Platinum, ch) : 0;
        EventRewards.DropGold(at, gold);
        EventRewards.DropEssence(at, essence);
        EventRewards.Notice(essence > 0 ? $"욕심의 상자 — 금화 <color=#F2D26A>+{gold}</color> · 정수 <color=#73D9C7>+{essence}</color>"
                                        : $"욕심의 상자 — 금화 <color=#F2D26A>+{gold}</color>");
    }

    protected override void DemoTick(MinigameDemo mode, float dt)
    {
        if (mode == MinigameDemo.Idle || _state != State.Idle) return;
        _demoTimer -= dt;
        if (_demoTimer > 0f) return;
        _demoTimer = 0.6f;

        // 완벽 — 운을 성공으로 고정하고 끝까지 연다 · 보통 — 한 번 열고 챙긴다(운은 그대로)
        if (mode == MinigameDemo.Perfect) { _demoForce = true; BeginOpen(); return; }
        if (_stage == 0) BeginOpen();
        else Bank();
    }

    // ── Private Methods ────────────────────────────────────────

    private void TickIdle()
    {
        if (PlayerT == null) return;
        Vector3 p = PlayerT.position;
        bool nearChest = Flat(p, _chestPos) <= OpenRange && _stage < MaxStage;
        bool nearBank  = Flat(p, _bankPos) <= BankRange;
        _openPrompt.gameObject.SetActive(nearChest && !nearBank);
        _bankPrompt.gameObject.SetActive(nearBank);

        if (UIInputGate.Blocked || !Input.GetKeyDown(KeyCode.F)) return;
        if (nearBank) Bank();
        else if (nearChest) BeginOpen();
    }

    private void BeginOpen()
    {
        if (_state != State.Idle || _stage >= MaxStage) return;
        _state     = State.Opening;
        _openTimer = 0f;
        _nextBeat  = 0f;
        _openPrompt.gameObject.SetActive(false);
        _bankPrompt.gameObject.SetActive(false);
        Play(ShakeState);
    }

    /// <summary>두근거림 — 0.8초 동안 틱이 빨라지고 상자가 떤다 → 결과.</summary>
    private void TickOpening(float dt)
    {
        _openTimer += dt;
        if (_openTimer >= _nextBeat)
        {
            float k = _openTimer / Heartbeat;
            MinigameFx.Tick(1f + 0.5f * k);
            _nextBeat = _openTimer + Mathf.Lerp(0.22f, 0.08f, k);
        }
        if (_chest != null)
        {
            float wob = Mathf.Sin(_openTimer * 60f) * 0.04f * (_openTimer / Heartbeat);
            _chest.transform.position = _chestPos + new Vector3(wob, 0f, wob * 0.5f);
        }
        if (_openTimer < Heartbeat) return;

        if (_chest != null) _chest.transform.position = _chestPos;
        bool ok = _demoForce || _outcomes[_stage];
        if (ok) Succeed(); else Bite();
    }

    private void Succeed()
    {
        _stage++;
        MinigameFx.Success(_stage);
        // 4단계는 아래 빛기둥 하나로 — 같은 자리 · 같은 순간 단계 고리 · 빛기둥 · 챙기기 고리 셋이 겹쳤다(10-11 이펙트 톤 점검)
        if (_stage < MaxStage) RunFx.Play(RunFxSlot.Ring, _chestPos, 0.5f + 0.15f * _stage, GoldLight);
        // 금빛 더미가 한 단 넓어진다
        if (_glow != null) Disown(_glow);
        _glow = Own(PatternGuideHelper.Disc(_chestPos, 1f + 0.35f * _stage, GoldLight));
        Play(IdleState);
        RefreshChance();

        if (_stage >= MaxStage)
        {
            // 4단계 — 상자가 열린 채 빛기둥, 그대로 챙긴다
            RunFx.Play(RunFxSlot.Pillar, _chestPos, 0.09f, GoldLight);
            Hud?.Float("최고 단계!", GoldLight);
            Bank();
            return;
        }
        _state = State.Idle;
    }

    private void Bite()
    {
        _bitten = true;
        _state  = State.Done;
        Play(BiteState);
        MinigameFx.Fail();
        MinigameFx.Flash(MinigameFx.HurtColor, 0.3f);
        if (PlayerT != null && Flat(PlayerT.position, _chestPos) <= BiteRange) HurtPlayer();
        // 금빛이 흩어지며 사라진다(「놓쳤다」)
        RunFx.Play(RunFxSlot.Ring, _chestPos, 0.9f, new Color(0.55f, 0.5f, 0.45f));
        if (_glow != null) Disown(_glow);
        _glow = null;
        if (_chance != null) { _chance.text = "놓쳤다"; _chance.color = MinigameFx.HurtColor; }
        TauntLaterAsync().Forget();
    }

    private void Bank()
    {
        if (_state == State.Done) return;
        _banked = true;
        _state  = State.Done;
        MinigameFx.Sound(SoundKey.Sfx.GoldPickup, 0.9f, 1f + 0.08f * _stage);
        if (_stage < MaxStage) RunFx.Play(RunFxSlot.Ring, _bankPos, 0.6f, GoldLight);   // 4단계 자동 챙기기는 상자 빛기둥이 맡는다
        if (_chance != null) { _chance.text = "챙겼다"; _chance.color = Theme; }
    }

    private void RefreshChance()
    {
        if (_chance == null) return;
        _chance.text = _stage >= MaxStage ? "최고!" : $"{Mathf.RoundToInt(Chances[_stage] * 100f)}%";
    }

    private async UniTaskVoid TauntLaterAsync()
    {
        try
        {
            await UniTask.Delay(900, cancellationToken: this.GetCancellationTokenOnDestroy());
            Play(TauntState);
        }
        catch (System.OperationCanceledException) { }
    }

    private void Play(string state)
    {
        if (_anim == null || !_anim.isActiveAndEnabled) return;
        int hash = Animator.StringToHash(state);
        if (_anim.HasState(0, hash)) _anim.CrossFade(hash, 0.1f);
    }

    /// <summary>
    /// 의태 상자 몬스터의 <b>모델만</b> 소품으로 세운다 — 프리팹 안 애니메이터가 달린 모델 부분만 복제한다(몬스터 스크립트 · 에이전트 · 충돌체는 뿌리에 있어 따라오지 않는다).
    /// 모델의 애니 이벤트 수신기는 떼고 이벤트도 끈다(물기 모션의 타격 이벤트가 받을 곳 없이 울리지 않게). 못 읽으면 금빛 기둥으로 대신한다.
    /// 10-29 보물고도 같은 상자를 쓴다(B2) — <paramref name="sinkDepth"/>만큼 바닥 아래에 세운다(솟는 연출은 부른 쪽 몫 · 0이면 제자리). 닫힌 자세로 둔다.
    /// </summary>
    public static async UniTask<GameObject> SpawnChestPropAsync(Vector3 pos, Quaternion rot, float sinkDepth, CancellationToken ct)
    {
        GameObject prefab = null;
        try { prefab = await Managers.AddressableManager.TryLoadAssetAsync<GameObject>(ChestMonster.PrefabAddress); }
        catch (System.Exception e) when (e is not System.OperationCanceledException) { Debug.LogWarning($"[Minigame] 상자 모델 읽기 실패 — {e.Message}"); }
        ct.ThrowIfCancellationRequested();

        var model = prefab != null ? prefab.GetComponentInChildren<Animator>(true) : null;
        if (model == null)
            return PatternGuideHelper.Pillar(pos, 0.5f, 0.9f, GoldLight);

        var holder = new GameObject("GreedChestHolder");
        holder.SetActive(false);   // 꺼 둔 채 만들어 수신기의 Awake가 돌지 않게
        var go = Instantiate(model.gameObject, pos + Vector3.down * sinkDepth, rot, holder.transform);
        go.name = "GreedChestProp";
        foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true)) DestroyImmediate(mb);
        go.transform.SetParent(null, true);
        go.transform.localScale = model.transform.lossyScale;
        Destroy(holder);
        go.SetActive(true);
        if (go.TryGetComponent<Animator>(out var anim))
        {
            anim.fireEvents = false;
            int idle = Animator.StringToHash(IdleState);
            if (anim.isActiveAndEnabled && anim.HasState(0, idle)) anim.Play(idle, 0, 0f);   // 닫힌 상자 자세
        }
        return go;
    }

    private TextMeshPro Label(Vector3 at, string text, float size, Color color)
    {
        var go = Own(new GameObject("GreedLabel"));
        go.transform.position = at;
        var t = go.AddComponent<TextMeshPro>();
        t.text             = text;
        t.fontSize         = size;
        t.alignment        = TextAlignmentOptions.Center;
        t.color            = color;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.sortingOrder     = UISortingOrder.WorldPrompt;
        TMPOutlineHelper.ApplySoftShadow(t);
        return t;
    }

    private void Face(TextMeshPro t)
    {
        if (t != null && t.gameObject.activeSelf) t.transform.rotation = _camT.rotation;
    }

    private static float Flat(Vector3 a, Vector3 b)
    {
        a.y = b.y = 0f;
        return Vector3.Distance(a, b);
    }
}
