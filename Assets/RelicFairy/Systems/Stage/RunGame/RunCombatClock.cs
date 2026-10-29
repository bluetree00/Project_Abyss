using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using RelicFairy.Monster;
using UnityEngine;

/// <summary>
/// 챕터 전투 시계(10-24 U5 · 기록만) — 런 중 <b>싸우는 동안만</b> <see cref="RunStats"/>에 시간을 더한다.
/// 싸우는 동안 = 세는 방(<see cref="RunTimeGrade.Counts"/>)에 살아 있는 적이 있고 · 컷신(보스 등장 · 페이지 전환) 화면이 아니고 · 플레이어가 살아 있을 때.
/// <c>Time.deltaTime</c>을 더하므로 고르기 창 · 대사 · 일시정지(시간 정지)는 저절로 빠진다. 방마다 처음 싸움이 붙는 순간 그 방 기준 시간을 더한다.
/// 보스방 클리어(<see cref="GameRunSession.OnBossRoomCleared"/>) 뒤 화면이 조용해지면 띠 알림 한 줄 「Ch1 숲 — 전투 3:02 · A」.
/// 방 · 보스 코드를 건드리지 않게 따로 돈다(ZoneTitleBanner처럼 앱 시작 때 한 번 세운다).
/// </summary>
public sealed class RunCombatClock : MonoBehaviour
{
    // ── Constants ──
    private const float BannerDelay   = 1.5f;   // 보스가 쓰러지는 순간은 비워 둔다
    private const float BannerMaxWait = 20f;    // 그 안에 화면이 조용해지지 않으면(결과창 · 장면 전환) 띄우지 않는다

    // ── Static ──
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        if (FindFirstObjectByType<RunCombatClock>() != null) return;
        var go = new GameObject("RunCombatClock");
        DontDestroyOnLoad(go);
        go.AddComponent<RunCombatClock>();
    }

    // ── Private ──
    private GameRunSession _run;
    private int _baseRoomSerial = -1;   // 기준 시간을 이미 더한 방(방 들어선 순번)

    // ── Lifecycle ──
    private void Update()
    {
        var run = GameRunBootstrapper.Instance != null ? GameRunBootstrapper.Instance.Run : null;
        if (run != _run) Rebind(run);
        if (run == null || !run.IsRunning || run.RunStats == null) return;
        RoomSpeedClear.Enter(run);   // F1 속도 보상(10-29 §6-2 · 2a) — 방이 바뀌면 싸운 시간 · 정해진 시간을 새로

        // 10-29 랭킹 — 해방기 · 악몽 클리어 시간: 그 시기 동안의 게임 시간(봉인기 몫 · 컷신은 뺀다 · 고르기 창 · 대사 · 일시정지는 시간 0이라 저절로 빠진다)
        if (StoryProgress.Era != StoryEra.Sealed && !InCutscene(run)) run.RunStats.AddClearTime(Time.deltaTime);

        var kind = run.CurrentRoomKind;
        if (!RunTimeGrade.Counts(kind) || !Fighting(run)) return;

        if (_baseRoomSerial != run.RoomEnterSerial)
        {
            _baseRoomSerial = run.RoomEnterSerial;
            run.RunStats.AddRoomBase(RunTimeGrade.RoomBase(kind, (int)run.CurrentChapter, run.StartEra, run.AbyssDepth));
        }
        run.RunStats.AddCombatTime(Time.deltaTime);
        RoomSpeedClear.Tick(Time.deltaTime);
    }

    private void OnDestroy() => Rebind(null);

    // ── Private Methods ──
    private void Rebind(GameRunSession run)
    {
        if (_run != null) _run.OnBossRoomCleared -= HandleBossRoomCleared;
        _run = run;
        _baseRoomSerial = -1;
        if (_run != null) _run.OnBossRoomCleared += HandleBossRoomCleared;
    }

    private static bool InCutscene(GameRunSession run)
        => run.TryGetHudMode(out var mode) && (mode == HUDIds.Mode.Cutscene || mode == HUDIds.Mode.BossCutscene);

    private static bool Fighting(GameRunSession run)
    {
        if (InCutscene(run)) return false;
        var stats = run.Player != null ? run.Player.RuntimeStats : null;
        if (stats != null && stats.Hp <= 0) return false;
        MonsterBase.FindCompassTargets(Vector3.zero, out var nearest, out _, out var boss);   // 장부를 훑는다 — 할당 없음
        return nearest != null || boss != null;
    }

    private static bool Quiet(GameRunSession run)
        => Time.timeScale > 0f && !(run.TryGetHudMode(out var mode) && (mode == HUDIds.Mode.Cutscene || mode == HUDIds.Mode.BossCutscene));

    private async UniTaskVoid ShowGradeAsync(GameRunSession run, string title, string desc, CancellationToken ct)
    {
        try
        {
            await UniTask.Delay(TimeSpan.FromSeconds(BannerDelay), DelayType.Realtime, cancellationToken: ct);
            float waited = 0f;
            while (!Quiet(run))
            {
                waited += Time.unscaledDeltaTime;
                if (waited > BannerMaxWait || run != _run || !run.IsRunning) return;
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            if (run != _run || !run.IsRunning) return;
            RoomEventNotice.Raise(title, desc);
        }
        catch (OperationCanceledException) { }
    }

    // ── Event Handlers ──
    private void HandleBossRoomCleared(Vector3 _)
    {
        var run = _run;
        var stats = run?.RunStats;
        if (stats == null) return;
        float combat = stats.CombatSeconds, baseSec = stats.BaseSeconds;   // 지금 값을 떠 둔다(곧 챕터가 넘어가며 비운다)
        string letter = RunTimeGrade.LetterInk(combat, baseSec);
        if (letter.Length == 0) return;
        string name = run.ChapterName(run.CurrentChapter);
        int ch = (int)run.CurrentChapter;
        string label = string.IsNullOrEmpty(name) ? $"Ch{ch}" : $"Ch{ch} {name}";
        string title = $"{label} — 전투 {RunResultPresenter.FormatTime(Mathf.RoundToInt(combat))} · {letter}";
        string desc  = $"기준 {RunResultPresenter.FormatTime(Mathf.RoundToInt(baseSec))}";
        ShowGradeAsync(run, title, desc, this.GetCancellationTokenOnDestroy()).Forget();
    }
}
