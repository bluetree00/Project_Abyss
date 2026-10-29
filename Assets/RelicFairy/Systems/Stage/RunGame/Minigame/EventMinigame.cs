using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using RelicFairy.Monster;
using UnityEngine;

/// <summary>자동 시연(에디터 확인용) — 놀이가 「완벽 / 보통 / 방치」를 흉내 낸다.</summary>
public enum MinigameDemo { None, Perfect, Normal, Idle }

/// <summary>
/// 이벤트방 놀이 공용 뼈대(10-01 설계 §2 · §6). 박자 ①~⑦만 알고 놀이의 규칙은 모른다:
///   ① 입장(소품이 솟음) → ② 규칙 카드 → ③ 카운트다운 → ④ 진행(제한 시간) → ⑤ 결말 슬로모 → ⑥ 등급 도장 → ⑦ 보상(놀이가 직접 — <see cref="GrantReward"/>).
/// 시간 제한이 곧 탈출구다 — 어떤 놀이도 제한 시간이 지나면 그때까지의 성적으로 끝난다. 뼈대에서 예외가 나도 브론즈로 끝낸다(소프트락 금지).
///
/// 출구: 보상을 준 뒤 <see cref="OnResolved"/> — 10-29 룬 고르기 창(RoomClearGate)을 걷어 끝나는 순간 출구가 열린다(설계 「룬이 아닌 특별한 보상」).
/// 규칙 카드는 처음 보는 놀이만 길게(4초), 다음부터는 1초 — 「빨리 끝나는 이벤트」.
/// 시작 시점: RunFlowController가 <see cref="OnResolved"/>를 구독하는 순간 = 방 입장 연출이 끝나고 조작이 풀린 뒤.
/// </summary>
public abstract class EventMinigame : MonoBehaviour, IInteractionChallenge
{
    // ── Constants ──────────────────────────────────────────────
    private const float CardSeconds      = 1f;     // 10-29 2.5 → 1 — 두 번째부터는 제목만 스친다
    private const float CardFirstSeconds = 4f;
    private const float LowTimeSeconds   = 5f;

    // ── Static ─────────────────────────────────────────────────
    /// <summary>지금 도는 놀이(확인 메뉴 · 자동 시연용). 없으면 null.</summary>
    public static EventMinigame Active { get; private set; }

    /// <summary>에디터 자동 시연 모드 — 놀이마다 <see cref="DemoTick"/>에서 흉내 낸다.</summary>
    public static MinigameDemo DebugDemo = MinigameDemo.None;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Active    = null;
        DebugDemo = MinigameDemo.None;
    }

    // ── Private / Protected ────────────────────────────────────
    protected GameRunSession   Run;
    protected System.Random    Rng;
    protected int              Tier;
    protected Color            Theme;
    protected Vector3          Center;
    protected float            Radius;
    protected PlayerController Player;
    protected Transform        PlayerT;
    protected MinigameHud      Hud;
    protected float            Elapsed;
    protected bool             Playing;

    private readonly List<GameObject> _owned = new();
    private CancellationTokenSource _cts;
    private bool  _resolved;
    private bool  _startNow;
    private int   _lastTickSecond = -1;

    // ── Properties ─────────────────────────────────────────────
    public event Action OnResolved;
    public bool   IsResolved => _resolved;
    public bool   IsPlaying  => Playing;
    public float  Remaining  => Mathf.Max(0f, PlayDuration - Elapsed);

    /// <summary>실제 제한 시간 — 놀이의 기준 시간 × 시기 배율(악몽 모드 −15%, EraBalance).</summary>
    protected float PlayDuration => Duration * EraBalance.Current.EventTimeScale;

    // ── 놀이가 정하는 것 ───────────────────────────────────────
    /// <summary>기록 · 로그 · 방 열쇠말(barrage · memory · greed …).</summary>
    public abstract string Id { get; }
    protected abstract string Title { get; }
    protected abstract string Rule { get; }
    protected abstract string GradeHint { get; }
    protected abstract float  Duration { get; }
    /// <summary>놀이판 반경(원하는 값 — 방이 좁으면 줄어든다).</summary>
    protected abstract float  WantRadius { get; }
    /// <summary>지금 성적의 등급 — 진행 중 메달 · 끝의 도장.</summary>
    public abstract ChallengeGrade CurrentGrade { get; }
    /// <summary>진행 띠 둘째 줄 — 「맞은 횟수 1 · 한 번 더 맞으면 실버」 같은 다음 등급 안내.</summary>
    protected abstract string StatusLine { get; }
    /// <summary>도장 아래 성적 한 줄.</summary>
    protected abstract string ResultLine { get; }
    /// <summary>라운드 점(●●○). 없으면 null.</summary>
    protected virtual string DotsLine => null;
    /// <summary>제한 시간 전에 끝났는가(모든 라운드 · 챙기기 · 물림).</summary>
    protected virtual bool FinishedEarly => false;
    /// <summary>보상이 설 자리(바닥) — 플레이어를 못 찾을 때만(보상은 발밑에 떨어진다).</summary>
    protected virtual Vector3 RewardSpot => Center;
    /// <summary>③ 카운트다운을 쓰는가(욕심의 상자는 바로 시작 — 10-29).</summary>
    protected virtual bool UseCountdown => true;

    /// <summary>① 무대 소품을 세운다(발판 · 상자 — 아래에서 솟는 연출 포함, 약 0.6초).</summary>
    protected abstract UniTask BuildStageAsync(CancellationToken ct);
    protected virtual void OnPlayStart() { }
    /// <summary>④ 한 프레임 — <paramref name="dt"/>는 게임 시간(팝업 · 슬로모를 따른다).</summary>
    protected abstract void TickPlay(float dt);
    /// <summary>⑤ 진행이 끝난 순간 — 남은 위험을 거둔다.</summary>
    protected virtual void OnPlayEnd() { }
    /// <summary>에디터 자동 시연 한 프레임.</summary>
    protected virtual void DemoTick(MinigameDemo mode, float dt) { }
    /// <summary>⑦ 특별 보상(10-29) — 금화 · 정수 · 증강 다시 뽑기 등 놀이마다 하나. <paramref name="at"/> = 플레이어 발밑(금화 · 정수가 자석 거리 안).</summary>
    protected abstract void GrantReward(ChallengeGrade grade, Vector3 at);

    // ── Lifecycle ──────────────────────────────────────────────
    private void Start()
    {
        _cts = new CancellationTokenSource();
        RunAsync(_cts.Token).Forget();
    }

    private void OnDestroy()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        Teardown();
    }

    // ── Public Methods ─────────────────────────────────────────

    /// <summary>SetupEventRoom이 호출 — 세션 · 방 시드(다시 들어와도 같은 판).
    /// 행운표 · 보상 이펙트는 10-29부터 쓰지 않는다(보상을 놀이가 직접 준다) — 확인 메뉴 호출과 맞추려고 자리만 둔다.</summary>
    public void Initialize(GameRunSession run, LuckRollTableSO luck, GameObject endEffect, GameObject endEffect2, int seed)
    {
        Run         = run;
        Rng         = new System.Random(seed);
        Tier        = MinigameTheme.Tier(run);
        Theme       = MinigameTheme.Of(run);
    }

    /// <summary>확인 메뉴 전용 — 방 입장 흐름 없이 바로 시작한다.</summary>
    public void StartNow() => _startNow = true;

    /// <summary>
    /// 방 열쇠말로 놀이를 붙인다. 붙였으면 true. 열쇠말은 서약 제단(sanctum) · 보물고(vault/treasure)와 겹치지 않게 고른다.
    /// </summary>
    public static bool TryAttach(GameObject roomGO, string keyLower, GameRunSession run, LuckRollTableSO luck,
                                 GameObject endEffect, GameObject endEffect2, int seed, out EventMinigame game)
    {
        game = null;
        if (roomGO == null || string.IsNullOrEmpty(keyLower)) return false;

        if      (keyLower.Contains("barrage")) game = roomGO.AddComponent<BarrageCorridorGame>();
        else if (keyLower.Contains("memory"))  game = roomGO.AddComponent<RuneMemoryGame>();
        else if (keyLower.Contains("greed"))   game = roomGO.AddComponent<GreedChestGame>();
        else return false;

        game.Initialize(run, luck, endEffect, endEffect2, seed);
        return true;
    }

    // ── Protected Methods ──────────────────────────────────────

    /// <summary>놀이가 만든 오브젝트를 맡긴다 — 끝나거나 방이 사라지면 함께 거둔다.</summary>
    protected GameObject Own(GameObject go)
    {
        if (go != null) _owned.Add(go);
        return go;
    }

    protected void Disown(GameObject go)
    {
        if (go == null) return;
        _owned.Remove(go);
        Destroy(go);
    }

    /// <summary>「맞음」 한 번 — 비치명 피해 + 떠오르는 「−1」. 실제로 들어갔으면 true(회피 무적이면 false).</summary>
    protected bool HurtPlayer()
    {
        if (!MinigameFx.Hurt(Player)) return false;
        Hud?.Float("−1", MinigameFx.HurtColor);
        return true;
    }

    /// <summary>작은 수(적을수록 좋다) → 등급. 문턱 = 그 등급이 허락하는 최대값.</summary>
    public static ChallengeGrade GradeByMax(int value, int platinum, int gold, int silver, int bronze)
    {
        if (value <= platinum) return ChallengeGrade.Platinum;
        if (value <= gold)     return ChallengeGrade.Gold;
        if (value <= silver)   return ChallengeGrade.Silver;
        if (value <= bronze)   return ChallengeGrade.Bronze;
        return ChallengeGrade.Fail;
    }

    /// <summary>「n번 더 틀리면 한 등급 내려간다」 — 적을수록 좋은 수에서 지금 등급을 지킬 여유.</summary>
    protected static int SlackBeforeDrop(int value, int platinum, int gold, int silver, int bronze)
    {
        if (value <= platinum) return platinum - value + 1;
        if (value <= gold)     return gold - value + 1;
        if (value <= silver)   return silver - value + 1;
        if (value <= bronze)   return bronze - value + 1;
        return 0;
    }

    // ── Private Methods ────────────────────────────────────────

    private async UniTaskVoid RunAsync(CancellationToken ct)
    {
        try
        {
            // 방 입장 연출이 끝나고 조작이 풀릴 때까지(RunFlowController가 해결 알림을 구독하는 순간)
            while (OnResolved == null && !_startNow)
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            while ((PlayerT = Managers.Player?.PlayerTransform) == null)
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            Player = Run?.Player != null ? Run.Player : PlayerT.GetComponent<PlayerController>();

            Active = this;
            await RunFx.LoadAsync();
            ServiceRoomDecorPlacer.SyncPhysics();
            MinigameArena.Resolve(transform.position, PlayerT.position, WantRadius, out Center, out Radius);
            Debug.Log($"[Minigame] {Id} 시작 — 판 가운데 {Center} · 반경 {Radius:0.0}m · 단계 {Tier + 1}");

            MinigameFx.BeginGuides();
            Hud = MinigameHud.Create(Theme);

            // ① 입장 — 놀이 빛깔로 화면 한 번 · 낮은 울림 · 소품이 솟는다
            MinigameFx.Flash(Theme, 0.4f, 0.1f);
            MinigameFx.Cue(SoundEvent.MgStart);   // 예전 키 sfx_room_clear는 주소가 없어 무음이었다(10-12 E4)
            await BuildStageAsync(ct);

            // ② 규칙 카드(처음 보는 놀이면 길게)
            string seenKey = "mg_" + Id;
            bool first = !StoryProgress.IsSeen(seenKey);
            await Hud.ShowCardAsync(Title, Rule, GradeHint, first ? CardFirstSeconds : CardSeconds, ct);
            if (first) StoryProgress.MarkSeen(seenKey);

            // ③ 카운트다운
            if (UseCountdown) await Hud.CountdownAsync(ct);

            // ④ 진행
            Hud.ShowBand(Title);
            Playing = true;
            OnPlayStart();
            while (Elapsed < PlayDuration && !FinishedEarly)
            {
                float dt = Time.deltaTime;
                Elapsed += dt;
                TickPlay(dt);
                if (DebugDemo != MinigameDemo.None) DemoTick(DebugDemo, dt);
                RefreshHud();
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            Playing = false;

            // ⑤ 결말
            var grade = CurrentGrade;
            OnPlayEnd();
            MinigameFx.Cue(SoundEvent.RoomClear);   // 놀이 끝 = 방을 해결했다는 같은 뜻(10-12 E5)
            await MinigameFx.FinaleSlowAsync(ct);

            // ⑥ 등급 도장
            await Hud.StampAsync(grade, ResultLine, ct);

            // ⑦ 보상
            Finish(grade);
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            Debug.LogError($"[Minigame] {Id} 진행 중 예외 — 브론즈로 끝낸다(방에 가두지 않는다): {e}");
            Finish(ChallengeGrade.Bronze);
        }
    }

    private void RefreshHud()
    {
        if (Hud == null) return;
        float left = Remaining;
        Hud.SetTime(PlayDuration > 0f ? left / PlayDuration : 0f, left);
        Hud.SetGrade(CurrentGrade);
        Hud.SetStatus(StatusLine);
        Hud.SetDots(DotsLine);

        // 마지막 5초 — 초마다 틱
        int sec = Mathf.CeilToInt(left);
        if (left <= LowTimeSeconds && sec != _lastTickSecond && sec > 0)
        {
            _lastTickSecond = sec;
            MinigameFx.Tick(1.2f);
        }
    }

    private void Finish(ChallengeGrade grade)
    {
        if (_resolved) return;
        _resolved = true;
        Playing   = false;
        string result = SafeResult();
        Vector3 at = PlayerT != null ? PlayerT.position : RewardSpot;
        Teardown();

        // ⑦ 놀이가 정한 특별 보상을 그 자리에서(10-29) — 보상이 깨져도 출구는 연다(소프트락 금지)
        try { GrantReward(grade, at); }
        catch (Exception e) { Debug.LogError($"[Minigame] {Id} 보상 예외 — 출구는 연다: {e}"); }
        Debug.Log($"[Minigame] {Id} 끝 — {grade} · {result}");
        OnResolved?.Invoke();
    }

    private string SafeResult()
    {
        try { return ResultLine; }
        catch { return string.Empty; }
    }

    private void Teardown()
    {
        for (int i = 0; i < _owned.Count; i++)
            if (_owned[i] != null) Destroy(_owned[i]);
        _owned.Clear();

        if (Hud != null) { Hud.Close(); Hud = null; }
        if (Active == this)
        {
            Active = null;
            MinigameFx.EndGuides();
        }
    }
}
