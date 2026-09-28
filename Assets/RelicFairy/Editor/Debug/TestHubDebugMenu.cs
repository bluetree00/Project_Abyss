#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 테스트 허브(Scenes/Test/BaseCamp_Test) 바로가기.
/// 메뉴: RelicFairy/Test Run/…
///   · Open Test Hub        — 허브 씬을 연다(편집 모드)
///   · Launch Current Selection (Play) — 허브 화면의 「시작」과 같다(현재 선택 그대로)
///   · Select/… (Play)         — 챕터·시작점 선택(허브 화면의 툴바·토글과 같다)
///   · Enter Open Exit (Play)  — 현재 방의 열린 출구로 순간이동(다음 방으로 넘어간다)
///   · Boss Room/1~3 (Play)    — 입구 트리거 안 → 트리거 너머 → 보스 12m 앞 (걸어 들어가는 흐름 재현)
///   · Boss Room/4~8 (Play)    — 보스 HP 절반 / 처치(페이지 보스는 다음 페이지) / 리치 상태 로그 / 봉인석 타격 / 리치 이동 표본
///   · Boss Room/9 (Play)      — 리치 패턴 강제 실행(P1 M1~M8 · 2페이지 C1~C5/R1·R2·R4·R5) · 자동 선택 끄기
///   · Player Invincible 1h (Play) — 보스를 오래 지켜볼 때
///   · Story/Override …        — 봉인기·악몽기로 보기(에디터 설정, 이야기 기록 저장 안 함) · 상태 로그
///   · Advance Dialogue (Play) — 열린 대사창을 한 번 넘긴다
///   · Log Player State (Play) — 위치·입력 잠금·접지·발밑 콜라이더를 콘솔에 찍는다
/// </summary>
public static class TestHubDebugMenu
{
    private const string Root    = "RelicFairy/Test Run/";
    private const string HubPath = "Assets/RelicFairy/Scenes/Test/BaseCamp_Test.unity";

    [MenuItem(Root + "Open Test Hub")]
    public static void OpenHub()
    {
        if (Application.isPlaying) { Debug.LogWarning("[TestHub] 플레이 중에는 씬을 열지 않는다."); return; }
        // 저장 확인 모달은 띄우지 않는다(자동화 도구가 멈춘다) — 수정된 씬이 있으면 먼저 저장하라고만 알린다.
        for (int i = 0; i < EditorSceneManager.sceneCount; i++)
        {
            if (!EditorSceneManager.GetSceneAt(i).isDirty) continue;
            Debug.LogWarning($"[TestHub] 저장 안 된 씬이 있다: {EditorSceneManager.GetSceneAt(i).path} — 저장 후 다시 연다.");
            return;
        }
        EditorSceneManager.OpenScene(HubPath);
    }

    [MenuItem(Root + "Launch Current Selection (Play)")]
    public static void Launch()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[TestHub] 플레이 모드에서만 동작한다."); return; }

        var launcher = Object.FindFirstObjectByType<TestHubLauncher>();
        if (launcher == null) { Debug.LogWarning("[TestHub] 테스트 허브 씬이 아니다."); return; }
        if (!launcher.TryLaunch()) Debug.LogWarning("[TestHub] 아직 초기화 중이거나 이미 이동 중이다.");
    }

    [MenuItem(Root + "Go To BaseCamp (Play)")]
    public static void GoToBaseCamp()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[TestHub] 플레이 모드에서만 동작한다."); return; }

        var launcher = Object.FindFirstObjectByType<TestHubLauncher>();
        if (launcher == null) { Debug.LogWarning("[TestHub] 테스트 허브 씬이 아니다."); return; }
        if (launcher.TryGoToBaseCamp()) Debug.Log("[TestHub] 베이스캠프로 이동");
        else Debug.LogWarning("[TestHub] 아직 초기화 중이거나 이미 이동 중이다.");
    }

    [MenuItem(Root + "Select/Chapter 1 (Play)")] public static void SelectCh1() => SelectChapter(1);
    [MenuItem(Root + "Select/Chapter 2 (Play)")] public static void SelectCh2() => SelectChapter(2);
    [MenuItem(Root + "Select/Chapter 3 (Play)")] public static void SelectCh3() => SelectChapter(3);
    [MenuItem(Root + "Select/Chapter 4 (Play)")] public static void SelectCh4() => SelectChapter(4);

    [MenuItem(Root + "Select/Toggle Boss Approach (Play)")]
    public static void ToggleBossApproach()
    {
        var launcher = FindLauncher();
        if (launcher == null) return;
        Debug.Log($"[TestHub] 시작점 → {(launcher.ToggleBossApproach() ? "보스 대기방 직행" : "대기방(정상 흐름)")}");
    }

    private static void SelectChapter(int chapter)
    {
        var launcher = FindLauncher();
        if (launcher == null) return;
        launcher.SelectChapter(chapter);
        Debug.Log($"[TestHub] 챕터 → Ch{chapter}");
    }

    private static TestHubLauncher FindLauncher()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[TestHub] 플레이 모드에서만 동작한다."); return null; }
        var launcher = Object.FindFirstObjectByType<TestHubLauncher>();
        if (launcher == null) Debug.LogWarning("[TestHub] 테스트 허브 씬이 아니다.");
        return launcher;
    }

    /// <summary>현재 방의 열린 출구 트리거 안으로 플레이어를 옮긴다 — 걸어가지 않고 다음 방(보스방 등)으로 넘어가 볼 때.</summary>
    [MenuItem(Root + "Enter Open Exit (Play)")]
    public static void EnterOpenExit()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[TestHub] 플레이 모드에서만 동작한다."); return; }

        var player = GameRunBootstrapper.Instance?.Run?.Player;
        if (player == null) { Debug.LogWarning("[TestHub] 런 플레이어가 없다."); return; }

        foreach (var gate in Object.FindObjectsByType<ProcRoomGate>(FindObjectsSortMode.None))
        {
            if (!gate.IsArmed || !gate.TryGetComponent<Collider>(out var col)) continue;

            var b   = col.bounds;
            var pos = new Vector3(b.center.x, b.min.y + 0.3f, b.center.z);
            player.transform.position = pos;
            if (player.TryGetComponent<Rigidbody>(out var rb))
            {
                rb.position       = pos;
                rb.linearVelocity = Vector3.zero;
            }
            Debug.Log($"[TestHub] 열린 출구로 이동 → {gate.Kind} ({pos})");
            return;
        }
        Debug.LogWarning("[TestHub] 열린 출구가 없다(방을 아직 클리어하지 않았거나 출구가 없는 방).");
    }

    // ── 보스방 걸음 재현 — 입구 트리거·보스 스폰 기준이라 어느 보스 아레나에서나 쓴다 ──
    // 트리거는 Enter 뒤 Exit가 있어야 발동하므로 Into → Past 순서로 쓴다(사이에 물리 한 스텝 필요).

    [MenuItem(Root + "Boss Room/1 Step Into Entrance (Play)")]
    public static void StepIntoBossEntrance() => StepBossRoom(0);

    [MenuItem(Root + "Boss Room/2 Step Past Entrance (Play)")]
    public static void StepPastBossEntrance() => StepBossRoom(1);

    [MenuItem(Root + "Boss Room/3 Step Near Boss 12m (Play)")]
    public static void StepNearBoss() => StepBossRoom(2);

    private static void StepBossRoom(int step)
    {
        if (!Application.isPlaying) { Debug.LogWarning("[TestHub] 플레이 모드에서만 동작한다."); return; }

        var player = GameRunBootstrapper.Instance?.Run?.Player;
        var room   = Object.FindFirstObjectByType<BossRoomController>();
        var boss   = Object.FindFirstObjectByType<BossSpawner>();
        if (player == null || room == null || boss == null || !room.TryGetComponent<Collider>(out var trigger))
        {
            Debug.LogWarning("[TestHub] 보스방(플레이어·BossRoomController·BossSpawner)이 아니다.");
            return;
        }

        var b   = trigger.bounds;
        var dir = boss.transform.position - b.center;
        dir.y = 0f;
        dir.Normalize();
        float depth = Mathf.Abs(Vector3.Dot(b.extents, new Vector3(Mathf.Abs(dir.x), 0f, Mathf.Abs(dir.z))));

        Vector3 target = step switch
        {
            0 => b.center,
            1 => b.center + dir * (depth + 1.5f),
            _ => boss.transform.position - dir * 12f,
        };

        // 바닥에 세운다 — 트리거 윗면에서 Ground 레이어로 내려 쏜다.
        target.y = Physics.Raycast(new Vector3(target.x, b.max.y + 1f, target.z), Vector3.down, out var hit,
                                   b.size.y + 20f, LayerMask.GetMask("Ground"), QueryTriggerInteraction.Ignore)
            ? hit.point.y + 0.25f
            : b.min.y + 0.25f;

        player.transform.position = target;
        if (player.TryGetComponent<Rigidbody>(out var rb))
        {
            rb.position       = target;
            rb.linearVelocity = Vector3.zero;
        }
        float dist = Vector3.Distance(target, boss.transform.position);
        Debug.Log($"[TestHub] 보스방 이동({step}) → {target} · 보스 스폰까지 {dist:F1}m");
    }

    /// <summary>보스 HP를 절반 깎는다 — 정상 피해 경로(<c>TakeDamage</c>)라 페이지 전환 조건이 그대로 돈다.</summary>
    [MenuItem(Root + "Boss Room/4 Damage Boss 50% (Play)")]
    public static void DamageBossHalf() => DamageBoss(0.5f);

    /// <summary>
    /// 보스를 쓰러뜨린다 — 정상 피해 경로라 보스의 치명타 분기(봉인 퇴각·사망)를 그대로 탄다.
    /// 페이지 보스(리치)는 전환 임계에서 HP가 멈춘다 → 한 번 = 다음 페이지로, 마지막 페이지에서 처치.
    /// </summary>
    [MenuItem(Root + "Boss Room/5 Kill Boss or Next Page (Play)")]
    public static void KillBoss() => DamageBoss(10f);

    /// <summary>
    /// 보스를 가드 없이 처치 — 시너지 피해 경로(보스별 TakeDamage 가드 · 화룡 소환 차단 · 기사 50% 고정을 우회, HP 바닥은 지킴).
    /// 사망 흐름(사망 모션 · 처치 뒤 공격)을 확인할 때.
    /// </summary>
    // ── 플레이어 피격 등급 기록(09-20 사용자 지시: 강한 공격과 기본 공격의 받는 연출이 달라야) ──
    private static PlayerController s_hitLogPlayer;

    /// <summary>플레이어가 맞을 때마다 등급(약 · 중 · 강)과 피해를 로그로 — 보스 쪽 등급 연결 확인용(켜기/끄기).</summary>
    [MenuItem(Root + "Player Hit Weight Log (Toggle, Play)")]
    public static void TogglePlayerHitLog()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[TestHub] 플레이 모드에서만 동작한다."); return; }
        if (s_hitLogPlayer != null)
        {
            s_hitLogPlayer.OnHitTaken -= LogPlayerHit;
            s_hitLogPlayer = null;
            Debug.Log("[TestHub] 피격 등급 기록 끔");
            return;
        }
        s_hitLogPlayer = GameRunBootstrapper.Instance?.Run?.Player;
        if (s_hitLogPlayer == null) { Debug.LogWarning("[TestHub] 런 플레이어가 없다."); return; }
        s_hitLogPlayer.OnHitTaken += LogPlayerHit;
        Debug.Log("[TestHub] 피격 등급 기록 켬");
    }

    private static void LogPlayerHit(HitWeight weight, Vector3 dir, int damage)
        => Debug.Log($"[HitWeight] {weight} · 피해 {damage}");

    [MenuItem(Root + "Boss Room/5b Force Kill Boss - Bypass Gates (Play)")]
    public static void ForceKillBoss()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[TestHub] 플레이 모드에서만 동작한다."); return; }
        var spawner = Object.FindFirstObjectByType<BossSpawner>();
        var boss    = spawner != null ? spawner.SpawnedBoss : null;
        if (boss == null) { Debug.LogWarning("[TestHub] 소환된 보스가 없다."); return; }
        var player  = GameRunBootstrapper.Instance?.Run?.Player;
        int before  = boss.CurrentHp;
        boss.TakeSynergyDamage(before * 10f + 10000f, player != null ? player.gameObject : null, 1f);
        Debug.Log($"[TestHub] 보스 강제 처치(가드 우회) — HP {before} → {boss.CurrentHp} · 사망={boss.IsDead}");
    }

    /// <summary>봉인 의식의 봉인석을 한 번씩 친다 — 세 번 실행하면 전부 점화(한 휘두름 = 1타 간격 때문에 한 번에 한 타).</summary>
    [MenuItem(Root + "Boss Room/7 Hit Seal Stones (Play)")]
    public static void HitSealStones()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[TestHub] 플레이 모드에서만 동작한다."); return; }
        var player = GameRunBootstrapper.Instance?.Run?.Player;
        int hit = 0, lit = 0;
        foreach (var stone in Object.FindObjectsByType<RelicFairy.Monster.LichSealStone>(FindObjectsSortMode.None))
        {
            if (stone.IsIgnited) { lit++; continue; }
            stone.TakeDamage(1f, player != null ? player.gameObject : null);
            hit++;
            if (stone.IsIgnited) lit++;
        }
        Debug.Log(hit + lit == 0 ? "[TestHub] 봉인석이 없다(봉인 의식 중이 아님)." : $"[TestHub] 봉인석 타격 {hit}개 · 점화 {lit}개");
    }

    [MenuItem(Root + "Boss Room/6 Log Lich State (Play)")]
    public static void LogLichState()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[TestHub] 플레이 모드에서만 동작한다."); return; }
        var lich = Object.FindFirstObjectByType<RelicFairy.Monster.LichMonster>();
        if (lich == null || lich.LichBB == null) { Debug.LogWarning("[TestHub] 리치가 없다."); return; }

        var bb = lich.LichBB;
        Debug.Log($"[TestHub] 리치 — {lich.BossName} · {bb.Page}페이지 · HP {lich.CurrentHp}/{lich.BossMaxHp} ({lich.HpRatio:P0}) · " +
                  $"임계 [{string.Join(", ", lich.PageThresholds)}] · 패턴 중={lich.IsInSpecialState} · 휴식 {bb.BreakDurationMinOverride:0.0}~{bb.BreakDurationMaxOverride:0.0}");
        LogLichMovementSample(lich, 0f);
    }

    // ── 리치 이동 표본 — 0.5초마다 10초간 이동 상태·거리·제단 반경을 찍는다 ──
    private const float MovementSampleSeconds  = 10f;
    private const float MovementSampleInterval = 0.5f;
    private static double s_sampleStart;
    private static double s_nextSample;

    [MenuItem(Root + "Boss Room/8 Sample Lich Movement 10s (Play)")]
    public static void SampleLichMovement()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[TestHub] 플레이 모드에서만 동작한다."); return; }
        EditorApplication.update -= TickMovementSample;
        s_sampleStart = s_nextSample = EditorApplication.timeSinceStartup;
        EditorApplication.update += TickMovementSample;
        Debug.Log("[TestHub] 리치 이동 표본 시작(10초)");
    }

    private static void TickMovementSample()
    {
        double now = EditorApplication.timeSinceStartup;
        if (!Application.isPlaying || now - s_sampleStart > MovementSampleSeconds)
        {
            EditorApplication.update -= TickMovementSample;
            return;
        }
        if (now < s_nextSample) return;
        s_nextSample = now + MovementSampleInterval;

        var lich = Object.FindFirstObjectByType<RelicFairy.Monster.LichMonster>();
        if (lich != null) LogLichMovementSample(lich, (float)(now - s_sampleStart));
    }

    private static void LogLichMovementSample(RelicFairy.Monster.LichMonster lich, float t)
    {
        var mc     = lich.MovementController;
        var player = GameRunBootstrapper.Instance?.Run?.Player;
        if (mc == null) return;

        Vector3 pos    = lich.transform.position;
        Vector3 flatC  = mc.ArenaCenter; flatC.y = pos.y;
        float   toCtr  = Vector3.Distance(pos, flatC);
        float   toPly  = player != null ? Vector3.Distance(new Vector3(pos.x, 0f, pos.z),
                                         new Vector3(player.transform.position.x, 0f, player.transform.position.z)) : -1f;
        Vector2 band   = mc.CurrentRangeBand();
        Debug.Log($"[TestHub] 리치 이동 t={t:0.0} · {mc.CurrentState} · 패턴={lich.IsInSpecialState} · 플레이어까지 {toPly:0.0}m (띠 {band.x:0}~{band.y:0}) · " +
                  $"중심까지 {toCtr:0.0}/{mc.ArenaRadius:0}m · 고도 {pos.y - mc.FloorY:0.0}m · 기울기 {lich.transform.eulerAngles.z:0}°");
    }

    // ── 리치 패턴 강제 실행 — 쿨다운·조건 무시(설정에 든 패턴만) ──
    [MenuItem(Root + "Boss Room/9 Force Lich Pattern/0 Toggle Auto Patterns (Play)")]
    public static void ToggleLichAutoPatterns()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[TestHub] 플레이 모드에서만 동작한다."); return; }
        var lich = Object.FindFirstObjectByType<RelicFairy.Monster.LichMonster>();
        if (lich == null) { Debug.LogWarning("[TestHub] 리치가 없다."); return; }
        Debug.Log($"[TestHub] 리치 패턴 자동 선택 → {(lich.Editor_ToggleAutoPatterns() ? "켬" : "끔(강제 실행만)")}");
    }

    [MenuItem(Root + "Boss Room/9 Force Lich Pattern/M1 Magic Bolt (Play)")]
    public static void ForceLichMagicBolt() => ForceLichPattern("LichMagicBoltPatternSO");

    [MenuItem(Root + "Boss Room/9 Force Lich Pattern/M2 Elemental Barrage (Play)")]
    public static void ForceLichElementalBarrage() => ForceLichPattern("LichElementalBarragePatternSO");

    [MenuItem(Root + "Boss Room/9 Force Lich Pattern/M3 Arcane Orb (Play)")]
    public static void ForceLichArcaneOrb() => ForceLichPattern("LichArcaneOrbPatternSO");

    [MenuItem(Root + "Boss Room/9 Force Lich Pattern/M4 Twin Cast (Play)")]
    public static void ForceLichTwinCast() => ForceLichPattern("LichTwinCastPatternSO");

    [MenuItem(Root + "Boss Room/9 Force Lich Pattern/M5 Teleport Strike (Play)")]
    public static void ForceLichTeleportStrike() => ForceLichPattern("LichTeleportStrikePatternSO");

    [MenuItem(Root + "Boss Room/9 Force Lich Pattern/M6 Skeleton Legion (Play)")]
    public static void ForceLichSkeletonLegion() => ForceLichPattern("LichSkeletonSummonPatternSO");

    [MenuItem(Root + "Boss Room/9 Force Lich Pattern/M7 Ward (Play)")]
    public static void ForceLichWard() => ForceLichPattern("LichSealBreakerPatternSO");

    [MenuItem(Root + "Boss Room/9 Force Lich Pattern/M8 Elemental Realign (Play)")]
    public static void ForceLichElementalRealign() => ForceLichPattern("LichElementalRealignPatternSO");

    // 낫 계열은 2페이지에서만(봉인판 C · 해방판 R — 지금 모드의 에셋이 골라진다). 먼저 Boss Room/5로 페이지를 넘긴다.
    [MenuItem(Root + "Boss Room/9 Force Lich Pattern/C1 R1 Scythe Sweep (Play)")]
    public static void ForceLichScytheSweep() => ForceLichPattern("LichScytheSweepPatternSO");

    [MenuItem(Root + "Boss Room/9 Force Lich Pattern/C2 R2 Scythe Throw (Play)")]
    public static void ForceLichScytheThrow() => ForceLichPattern("LichScytheThrowPatternSO");

    [MenuItem(Root + "Boss Room/9 Force Lich Pattern/C3 Chain Break (Play)")]
    public static void ForceLichChainBreak() => ForceLichPattern("LichChainBreakPatternSO");

    [MenuItem(Root + "Boss Room/9 Force Lich Pattern/C4 R4 Blink Strike (Play)")]
    public static void ForceLichBlinkStrike() => ForceLichPattern("LichBlinkStrikePatternSO");

    [MenuItem(Root + "Boss Room/9 Force Lich Pattern/C5 R5 Dark Rain (Play)")]
    public static void ForceLichDarkRain() => ForceLichPattern("LichDarkRainPatternSO");

    [MenuItem(Root + "Boss Room/9 Force Lich Pattern/C6 Reaper Flurry (Play)")]
    public static void ForceLichReaperFlurry() => ForceLichPattern("LichReaperFlurryPatternSO");

    [MenuItem(Root + "Boss Room/9 Force Lich Pattern/C7 Chain Capture (Play)")]
    public static void ForceLichChainCapture() => ForceLichPattern("LichChainCapturePatternSO");

    [MenuItem(Root + "Boss Room/9 Force Lich Pattern/C8 Abyssal Rend (Play)")]
    public static void ForceLichAbyssalRend() => ForceLichPattern("LichAbyssalRendPatternSO");

    [MenuItem(Root + "Boss Room/9 Force Lich Pattern/C9 Falling Stars (Play)")]
    public static void ForceLichFallingStars() => ForceLichPattern("LichFallingStarsPatternSO");

    // 악몽기 2페이지 전용 — 봉인기 전투에서도 강제로 볼 수 있다(모드 조건은 무시하고 설정에서 찾는다).
    [MenuItem(Root + "Boss Room/9 Force Lich Pattern/N Reaper Cadence (Play)")]
    public static void ForceLichReaperCadence() => ForceLichPattern("LichReaperCadencePatternSO");

    [MenuItem(Root + "Boss Room/9 Force Lich Pattern/N Arcane Torrent (Play)")]
    public static void ForceLichArcaneTorrent() => ForceLichPattern("LichArcaneTorrentPatternSO");

    [MenuItem(Root + "Boss Room/9 Force Lich Pattern/N R8 Scythe Magic (Play)")]
    public static void ForceLichScytheMagic() => ForceLichPattern("LichScytheMagicPatternSO");

    [MenuItem(Root + "Boss Room/9 Force Lich Pattern/N N2 Seal Shards (Play)")]
    public static void ForceLichSealShards() => ForceLichPattern("LichSealShardsPatternSO");

    [MenuItem(Root + "Boss Room/9 Force Lich Pattern/N N1 Inverted Seal (Play)")]
    public static void ForceLichInvertedSeal() => ForceLichPattern("LichInvertedSealPatternSO");

    [MenuItem(Root + "Boss Room/9 Force Lich Pattern/P F1 Soul Copy (Play)")]
    public static void ForceLichSoulCopy() => ForceLichPattern("LichSoulCopyPatternSO");

    [MenuItem(Root + "Boss Room/9 Force Lich Pattern/P F2 Seal Array (Play)")]
    public static void ForceLichSealArray() => ForceLichPattern("LichSealArrayPatternSO");

    [MenuItem(Root + "Boss Room/9 Force Lich Pattern/P F3 Sky Debris (Play)")]
    public static void ForceLichSkyDebris() => ForceLichPattern("LichSkyDebrisPatternSO");

    [MenuItem(Root + "Boss Room/9 Force Lich Pattern/P F4 Final Magic (Play)")]
    public static void ForceLichFinalMagic() => ForceLichPattern("LichFinalMagicPatternSO");

    // ── 패턴 검증 기록(09-19 사용자 지시: 패턴 하나하나 검증 · 가이드라인이 사전에 제시되는지) ──
    private static readonly string[] ProbeSequence =
    {
        // 2페이지 풀(봉인판 C · 해방판 R — 지금 모드 설정에 없는 것은 건너뛴다)
        "LichScytheSweepPatternSO", "LichScytheThrowPatternSO", "LichChainBreakPatternSO", "LichBlinkStrikePatternSO",
        "LichDarkRainPatternSO", "LichReaperFlurryPatternSO", "LichChainCapturePatternSO", "LichReaperCadencePatternSO",
        "LichArcaneTorrentPatternSO", "LichAbyssalRendPatternSO", "LichFallingStarsPatternSO", "LichScytheMagicPatternSO",
        "LichSealShardsPatternSO", "LichTeleportStrikePatternSO",
        // 3페이지(악몽기)
        "LichSoulCopyPatternSO", "LichSealArrayPatternSO", "LichSkyDebrisPatternSO",
        // 1페이지 악몽 특수
        "LichInvertedSealPatternSO",
        "LichMagicBoltPatternSO",
        "LichArcaneOrbPatternSO",
    };
    private static int   s_probeIndex = -1;
    private static float s_probeNext;
    private static bool  s_probeWasBusy;

    // ── 근접 확인 도구 — 베기 이펙트가 쓸고 가는 방향처럼 0.3초 안에 끝나는 것을 캡처할 때 ──
    private static readonly object s_slowMoOwner = new();

    [MenuItem(Root + "Boss Room/16 Toggle Slow Motion 0.2x (Play)")]
    public static void ToggleSlowMotion()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[TestHub] 플레이 모드에서만 동작한다."); return; }
        if (TimeScaleArbiter.IsHeldBy(s_slowMoOwner))
        {
            TimeScaleArbiter.Release(s_slowMoOwner);
            Debug.Log("[TestHub] 슬로모 끔");
        }
        else
        {
            TimeScaleArbiter.Acquire(s_slowMoOwner, 0.2f, TimeScaleArbiter.Priority.SlowMotion);
            Debug.Log("[TestHub] 슬로모 0.2배 켬(다시 누르면 끔)");
        }
    }

    /// <summary>플레이어를 제단 코어 밖(중심에서 18 m, 지금 방향)으로 — 코어 안은 영구 붕괴가 없어 C8·C9 붕괴를 볼 때.</summary>
    [MenuItem(Root + "Boss Room/17 Move Player Off Core 18m (Play)")]
    public static void MovePlayerOffCore()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[TestHub] 플레이 모드에서만 동작한다."); return; }
        var player = GameRunBootstrapper.Instance?.Run?.Player;
        var grid   = ArenaTileGrid.Active;
        if (player == null || grid == null || !grid.TryGetWorldCenter(out var center))
        {
            Debug.LogWarning("[TestHub] 플레이어나 제단 격자가 없다.");
            return;
        }
        Vector3 dir = player.transform.position - center;
        dir.y = 0f;
        dir = dir.sqrMagnitude > 0.01f ? dir.normalized : Vector3.back;
        Vector3 dest = center + dir * 18f + Vector3.up * 0.3f;
        player.transform.position = dest;
        if (player.TryGetComponent<Rigidbody>(out var rb))
        {
            rb.position       = dest;
            rb.linearVelocity = Vector3.zero;
        }
        Debug.Log($"[TestHub] 플레이어 → 코어 밖 {dest}");
    }

    /// <summary>
    /// 베기 이펙트 방향 확인 — 제단 중심 왼쪽(−6 m)에 왼→오 베기, 오른쪽(+6 m)에 오→왼 베기를 둘 다 +Z(월드 앞)로.
    /// 각 자리에 앞쪽 반원(흰 부채꼴)을 깔아 「앞」을 표시한다. 슬로모(16)를 켜고 위에서 캡처한다.
    /// 기대: 위에서 볼 때 왼→오는 시계 방향(왼쪽 → 앞 → 오른쪽), 오→왼은 반시계 방향으로 쓸고, 둘 다 앞쪽 반원 안에 그려진다.
    /// </summary>
    [MenuItem(Root + "Boss Room/18 Slash Direction Test (Play)")]
    public static void SlashDirectionTest()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[TestHub] 플레이 모드에서만 동작한다."); return; }
        var grid = ArenaTileGrid.Active;
        if (grid == null || !grid.TryGetWorldCenter(out var center)) { Debug.LogWarning("[TestHub] 제단 격자가 없다."); return; }

        Vector3 left  = center + Vector3.left  * 6f + Vector3.up * 0.02f;
        Vector3 right = center + Vector3.right * 6f + Vector3.up * 0.02f;
        RelicFairy.Monster.PatternGuideHelper.Sector(left,  4f, 180f, 0f, RelicFairy.Monster.LichPatternUtil.SafeWhite, 6f);
        RelicFairy.Monster.PatternGuideHelper.Sector(right, 4f, 180f, 0f, RelicFairy.Monster.LichPatternUtil.SafeWhite, 6f);
        RelicFairy.Monster.LichPatternUtil.SlashVfx(left,  Vector3.forward, 4f, RelicFairy.Monster.LichSwing.LeftToRight);
        RelicFairy.Monster.LichPatternUtil.SlashVfx(right, Vector3.forward, 4f, RelicFairy.Monster.LichSwing.RightToLeft);
        Debug.Log($"[TestHub] 베기 방향 확인 — 왼쪽 {left}: 왼→오(시계 기대) · 오른쪽 {right}: 오→왼(반시계 기대) · 앞 = +Z");
    }

    [MenuItem(Root + "Boss Room/14 Lich Probe Report (Play)")]
    public static void LichProbeReport()
    {
        string report = RelicFairy.Monster.LichPatternProbe.Report();
        string path   = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Logs", "lich_probe.txt");
        System.IO.File.WriteAllText(path, report);
        Debug.Log(report + $"\n(파일: {path})");
    }

    /// <summary>
    /// 2페이지 패턴을 차례로 한 번씩 강제 실행하며 예고·타격 간격을 기록한다(자동 패턴은 끈다). 끝나면 보고서를 찍는다.
    /// 먼저 2페이지로 넘기고(Boss Room/5), 플레이어 무적을 켠다.
    /// </summary>
    [MenuItem(Root + "Boss Room/15 Probe Page-2 Patterns (Play)")]
    public static void ProbePage2Patterns()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[TestHub] 플레이 모드에서만 동작한다."); return; }
        var lich = Object.FindFirstObjectByType<RelicFairy.Monster.LichMonster>();
        if (lich == null) { Debug.LogWarning("[TestHub] 리치가 없다."); return; }
        if (lich.Editor_AutoPatternsEnabled) lich.Editor_ToggleAutoPatterns();
        RelicFairy.Monster.LichPatternProbe.Clear();
        s_probeIndex    = 0;
        s_probeNext     = Time.realtimeSinceStartup + 1f;
        s_probeWasBusy  = false;
        EditorApplication.update -= TickProbe;
        EditorApplication.update += TickProbe;
        Debug.Log($"[TestHub] 패턴 검증 시작 — {ProbeSequence.Length}종 차례로(자동 패턴 끔)");
    }

    private static void TickProbe()
    {
        var lich = Application.isPlaying ? Object.FindFirstObjectByType<RelicFairy.Monster.LichMonster>() : null;
        if (lich == null || s_probeIndex < 0)
        {
            EditorApplication.update -= TickProbe;
            s_probeIndex = -1;
            return;
        }
        if (lich.Editor_IsBusy)
        {
            s_probeWasBusy = true;
            return;
        }
        if (s_probeWasBusy)
        {
            // 방금 패턴이 끝났다 — 잔여 장판·추격탄이 정리될 시간을 둔다.
            s_probeWasBusy = false;
            s_probeNext    = Time.realtimeSinceStartup + 2.5f;
            return;
        }
        if (Time.realtimeSinceStartup < s_probeNext) return;

        while (s_probeIndex < ProbeSequence.Length)
        {
            string type = ProbeSequence[s_probeIndex++];
            if (lich.Editor_ForcePattern(type))
            {
                Debug.Log($"[TestHub] 패턴 검증 {s_probeIndex}/{ProbeSequence.Length} — {type}");
                return;
            }
            Debug.Log($"[TestHub] 패턴 검증 — {type} 건너뜀(지금 모드 설정에 없음)");
        }

        EditorApplication.update -= TickProbe;
        s_probeIndex = -1;
        LichProbeReport();
    }

    private static void ForceLichPattern(string typeName)
    {
        if (!Application.isPlaying) { Debug.LogWarning("[TestHub] 플레이 모드에서만 동작한다."); return; }
        var lich = Object.FindFirstObjectByType<RelicFairy.Monster.LichMonster>();
        if (lich == null) { Debug.LogWarning("[TestHub] 리치가 없다."); return; }
        bool ok = lich.Editor_ForcePattern(typeName);
        Debug.Log(ok ? $"[TestHub] 리치 패턴 강제 실행 — {typeName}"
                     : $"[TestHub] 리치 패턴 강제 실행 실패 — {typeName} (패턴 진행 중·등장 연출 중·설정에 없음)");
    }

    /// <summary>플레이어를 1시간 무적으로 — 보스 움직임·패턴을 오래 지켜볼 때.</summary>
    [MenuItem(Root + "Player Invincible 1h (Play)")]
    public static void PlayerInvincible()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[TestHub] 플레이 모드에서만 동작한다."); return; }
        var player = GameRunBootstrapper.Instance?.Run?.Player;
        if (player == null) { Debug.LogWarning("[TestHub] 런 플레이어가 없다."); return; }
        player.SetInvincible(3600f);
        Debug.Log("[TestHub] 플레이어 1시간 무적");
    }

    private static void DamageBoss(float ratioOfCurrent)
    {
        if (!Application.isPlaying) { Debug.LogWarning("[TestHub] 플레이 모드에서만 동작한다."); return; }

        var spawner = Object.FindFirstObjectByType<BossSpawner>();
        var boss    = spawner != null ? spawner.SpawnedBoss : null;
        var player  = GameRunBootstrapper.Instance?.Run?.Player;
        if (boss == null) { Debug.LogWarning("[TestHub] 소환된 보스가 없다."); return; }

        var instigator = player != null ? player.gameObject : null;

        // 리치 결계(무적) 중이면 결계 해골부터 쓰러뜨린다 — 결계는 해골이 다 죽어야 풀린다.
        int wards = 0;
        foreach (var marker in Object.FindObjectsByType<RelicFairy.Monster.SealSkeletonMarker>(FindObjectsSortMode.None))
        {
            var skeleton = marker.GetComponentInParent<RelicFairy.Monster.MonsterBase>();
            if (skeleton == null || skeleton.CurrentHp <= 0) continue;
            skeleton.TakeDamage(skeleton.CurrentHp * 10f + 10000f, instigator);
            wards++;
        }
        if (wards > 0)
        {
            Debug.Log($"[TestHub] 결계 해골 {wards}마리 처치 — 결계가 풀린 뒤 다시 실행");
            return;
        }

        int before = boss.CurrentHp;
        // 처치는 방어력·배율을 넘기도록 넉넉히 준다. 무적 상태(등장·전환·순간이동 중)면 보스 쪽에서 무시한다.
        float amount = ratioOfCurrent >= 1f ? before * 10f + 10000f : before * ratioOfCurrent;
        boss.TakeDamage(amount, instigator);
        Debug.Log($"[TestHub] 보스 피해 — HP {before} → {boss.CurrentHp} / {boss.BossMaxHp}" +
                  (boss.CurrentHp == before ? " (무적 상태라 무시됨 — 잠시 뒤 다시)" : ""));
    }

    // ── 이야기 상태 (저장을 건드리지 않는 오버라이드) ──
    private const string StoryRoot = Root + "Story/";

    [MenuItem(StoryRoot + "Override - Use Saved State")]
    public static void StoryUseSaved() => SetStoryOverride(-1);

    [MenuItem(StoryRoot + "Override - Seal Era")]
    public static void StorySealEra() => SetStoryOverride(0);

    [MenuItem(StoryRoot + "Override - Nightmare")]
    public static void StoryNightmare() => SetStoryOverride(1);

    [MenuItem(StoryRoot + "Override - Use Saved State", true)]
    private static bool StoryUseSavedCheck()  { Menu.SetChecked(StoryRoot + "Override - Use Saved State", StoryProgress.DebugNightmareOverride < 0);  return true; }
    [MenuItem(StoryRoot + "Override - Seal Era", true)]
    private static bool StorySealEraCheck()   { Menu.SetChecked(StoryRoot + "Override - Seal Era",        StoryProgress.DebugNightmareOverride == 0); return true; }
    [MenuItem(StoryRoot + "Override - Nightmare", true)]
    private static bool StoryNightmareCheck() { Menu.SetChecked(StoryRoot + "Override - Nightmare",       StoryProgress.DebugNightmareOverride == 1); return true; }

    private static void SetStoryOverride(int value)
    {
        EditorPrefs.SetInt(StoryProgress.DebugOverridePrefsKey, value);
        StoryProgress.RefreshDebugOverride();
        Debug.Log(value < 0
            ? "[TestHub] 이야기 상태 — 저장값 사용"
            : $"[TestHub] 이야기 상태 오버라이드 — {(value == 1 ? "악몽기" : "봉인기")} (이야기 기록은 저장하지 않음)");
    }

    [MenuItem(StoryRoot + "Log Story State")]
    public static void LogStoryState()
    {
        Debug.Log($"[TestHub] 이야기 — 악몽기={StoryProgress.IsNightmare} 엔딩={StoryProgress.HasEnded} " +
                  $"리치조우={StoryProgress.HasMetLich} 멀린이름={StoryProgress.IsMerlinNamed} " +
                  $"봉인 {StoryProgress.SealedCount}/4 처치 {StoryProgress.KilledCount}/4 · 오버라이드={StoryProgress.DebugNightmareOverride}");
    }

    [MenuItem(StoryRoot + "Log Nightmare Rules (Play)")]
    public static void LogNightmareRules()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[TestHub] 플레이 모드에서만 동작한다."); return; }
        var run = GameRunBootstrapper.Instance?.Run;
        if (run == null) { Debug.LogWarning("[TestHub] 런 중이 아니다."); return; }

        var rules = new System.Collections.Generic.List<NightmareRules.RuleInfo>();
        NightmareRules.CollectActive(rules);
        var names = new System.Text.StringBuilder();
        foreach (var r in rules) names.Append(names.Length > 0 ? " · " : "").Append(r.Name);

        var stats = run.Player != null ? run.Player.RuntimeStats : null;
        Debug.Log($"[TestHub] 악몽 규칙 — {run.CurrentChapter} 활성={NightmareRules.IsActive} [{names}] " +
                  $"포션 {run.PlayerState?.PotionCount}/{run.PlayerState?.PotionCapacity} · 회피 스태미나×{NightmareRules.DodgeStaminaMultiplier} · " +
                  $"무기 스킬 대기×{NightmareRules.WeaponSkillCooldownMultiplier} · 서약 차단={NightmareRules.BlocksCovenantAltar} · " +
                  $"정수×{NightmareRules.EssenceMultiplier} · 보호막 상한={(stats != null ? stats.SynergyMechanics.ShieldCapRatio.ToString("0.00") : "-")}");

        // HUD가 실제로 받은 버프창 목록(마지막 반영분) — 오버레이 UI는 씬 검색·카메라 캡처로 안 보인다.
        var hud = Object.FindFirstObjectByType<HudPresenter>(FindObjectsInactive.Include);
        var field = typeof(HudPresenter).GetField("_lastBuffItems", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (hud == null || field?.GetValue(hud) is not System.Collections.Generic.List<BuffViewItem> shown) return;
        var labels = new System.Text.StringBuilder();
        foreach (var item in shown)
            labels.Append(labels.Length > 0 ? " | " : "").Append(item.IconKey).Append(':').Append(item.Label.Replace('\n', ' '));
        Debug.Log($"[TestHub] 버프창 {shown.Count}칸 — {labels}");
    }

    /// <summary>열린 대사창을 한 번 넘긴다(클릭 1회와 같다 — 타이핑 중이면 줄 완성, 아니면 다음 줄).</summary>
    [MenuItem(Root + "Advance Dialogue (Play)")]
    public static void AdvanceDialogue()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[TestHub] 플레이 모드에서만 동작한다."); return; }

        var popup = Object.FindFirstObjectByType<UI_DialoguePopup>();
        if (popup == null) { Debug.LogWarning("[TestHub] 열린 대사창이 없다."); return; }

        var button = new SerializedObject(popup).FindProperty("advanceButton").objectReferenceValue as UnityEngine.UI.Button;
        if (button == null) { Debug.LogWarning("[TestHub] 대사창에 넘기기 버튼이 없다."); return; }
        button.onClick.Invoke();
    }

    // ── 리치 이펙트 진열 — 목록의 칸을 제단 위 격자에 실제 게임 조명·카메라로 띄운다(20초, 2.5초마다 다시 재생) ──
    private const int   GalleryPageSize   = 20;
    private const int   GalleryColumns    = 5;
    private const float GallerySpacing    = 9f;
    private const float GallerySeconds    = 20f;
    private const float GalleryReplay     = 2.5f;
    private static readonly System.Collections.Generic.List<GameObject> s_galleryObjects = new();
    private static readonly System.Collections.Generic.List<RelicFairy.Monster.LichVfxSlot> s_gallerySlots = new();
    private static readonly System.Collections.Generic.List<Vector3> s_galleryPositions = new();
    private static double s_galleryEnd;
    private static double s_galleryNext;

    [MenuItem(Root + "Boss Room/10 Lich VFX Gallery Page 1 (Play)")]
    public static void LichVfxGallery1() => StartLichVfxGallery(0);

    [MenuItem(Root + "Boss Room/10 Lich VFX Gallery Page 2 (Play)")]
    public static void LichVfxGallery2() => StartLichVfxGallery(1);

    [MenuItem(Root + "Boss Room/10 Lich VFX Gallery Page 3 (Play)")]
    public static void LichVfxGallery3() => StartLichVfxGallery(2);

    // ── 판정 크기 대조 — 착탄 이펙트를 패턴이 쓰는 배율로 띄우고, 그 아래 실제 판정 반경의 원을 깐다(위에서 캡처) ──
    private struct HitSizeRow
    {
        public RelicFairy.Monster.LichVfxSlot Slot;
        public float  Scale;    // 패턴이 넘기는 호출 배율
        public float  Radius;   // 판정 반경(m)
        public string Label;
        public HitSizeRow(RelicFairy.Monster.LichVfxSlot slot, float scale, float radius, string label)
        { Slot = slot; Scale = scale; Radius = radius; Label = label; }
    }

    // 값은 패턴 에셋 기준(09-18). 배율 식은 각 패턴 코드와 같다.
    private static readonly HitSizeRow[] HitSizeRows =
    {
        new(RelicFairy.Monster.LichVfxSlot.BoltImpact,     1.0f,  2.0f, "M1 착탄 r2"),
        new(RelicFairy.Monster.LichVfxSlot.IceImpact,      1.0f,  1.5f, "M2 얼음 r1.5"),
        new(RelicFairy.Monster.LichVfxSlot.LightningStrike,1.0f,  1.8f, "M2 번개 r1.8"),
        new(RelicFairy.Monster.LichVfxSlot.DarkOrbImpact,  0.35f, 1.2f, "M2 구체 r1.2"),
        new(RelicFairy.Monster.LichVfxSlot.ArcaneOrbBurst, 1.0f,  3.0f, "M3 폭발 r3"),
        new(RelicFairy.Monster.LichVfxSlot.TwinBlast,      1.0f,  5.0f, "M4 폭발 r5"),
        new(RelicFairy.Monster.LichVfxSlot.SlamImpact,     1.0f,  4.0f, "M5 착지 r4"),
        new(RelicFairy.Monster.LichVfxSlot.ScytheSlash,    5f / 5.5f,   5.0f, "C1 베기 r5(반원)"),
        new(RelicFairy.Monster.LichVfxSlot.ScytheSlash,    3.2f / 5.5f, 3.2f, "C4 베기 r3.2"),
        new(RelicFairy.Monster.LichVfxSlot.DarkRainImpact, 1.0f,  1.5f, "C5 낙하 r1.5"),
        new(RelicFairy.Monster.LichVfxSlot.LingerPool,     2.0f,  2.0f, "여운 장판 r2"),
        new(RelicFairy.Monster.LichVfxSlot.BindRing,       1.0f,  1.0f, "결박 고리"),
        new(RelicFairy.Monster.LichVfxSlot.ParryClash,     1.0f,  1.0f, "패링 불꽃"),
        new(RelicFairy.Monster.LichVfxSlot.ParryGlint,     1.0f,  0.5f, "패링 섬광"),
        new(RelicFairy.Monster.LichVfxSlot.TileRestore,    1.0f,  2.5f, "바닥 복구(칸 5 m)"),
        new(RelicFairy.Monster.LichVfxSlot.SkeletonDeath,  1.0f,  1.0f, "해골 죽음"),
    };

    // ── 구멍 낙하 시험 — 가장 가까운 부서진 칸 위로 플레이어를 옮겨 떨어뜨린다(낙하 깊이 · 복구 끼임 확인) ──
    [MenuItem(Root + "Boss Room/13 Drop Player Into Hole (Play)")]
    public static void DropPlayerIntoHole()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[TestHub] 플레이 모드에서만 동작한다."); return; }
        var player = GameRunBootstrapper.Instance?.Run?.Player;
        var grid   = ArenaTileGrid.Active;
        if (player == null || grid == null) { Debug.LogWarning("[TestHub] 플레이어·붕괴형 아레나가 없다."); return; }
        if (!grid.TryGetBrokenCellCenter(player.transform.position, out var hole))
        {
            Debug.LogWarning("[TestHub] 지금 구멍인 칸이 없다 — M5·M4로 바닥을 먼저 부술 것.");
            return;
        }

        Vector3 target = hole + Vector3.up * 1.2f;
        player.transform.position = target;
        if (player.TryGetComponent<Rigidbody>(out var rb))
        {
            rb.position       = target;
            rb.linearVelocity = Vector3.zero;
        }
        Debug.Log($"[TestHub] 플레이어를 구멍 위로 옮겼다 → {target} (부서진 칸 {grid.BrokenCount})");
    }

    // ── 패링 시험 — 켜 두면 낫이 빛나는 순간 플레이어가 리치를 한 번 친 것으로 한다(6 m 안에서만 통한다) ──
    private static bool s_autoParry;

    [MenuItem(Root + "Boss Room/12 Toggle Auto Parry (Play)")]
    public static void ToggleAutoParry()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[TestHub] 플레이 모드에서만 동작한다."); return; }
        s_autoParry = !s_autoParry;
        EditorApplication.update -= TickAutoParry;
        if (s_autoParry) EditorApplication.update += TickAutoParry;
        Debug.Log($"[TestHub] 자동 패링 → {(s_autoParry ? "켬" : "끔")}");
    }

    private static void TickAutoParry()
    {
        if (!Application.isPlaying) { s_autoParry = false; EditorApplication.update -= TickAutoParry; return; }
        var lich   = Object.FindFirstObjectByType<RelicFairy.Monster.LichMonster>();
        var player = GameRunBootstrapper.Instance?.Run?.Player;
        if (lich == null || player == null || !lich.IsParryWindowOpen) return;
        lich.TakeDamage(1f, player.gameObject);
        Debug.Log("[TestHub] 자동 패링 — 창 안에서 리치를 쳤다");
    }

    private static bool s_hitSizeMode;
    private const float HitSizeSpacing = 13f;
    private const int   HitSizeColumns = 4;

    [MenuItem(Root + "Boss Room/11 Lich Hit-Size Check (Play)")]
    public static void LichHitSizeCheck()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[TestHub] 플레이 모드에서만 동작한다."); return; }
        var lich = Object.FindFirstObjectByType<RelicFairy.Monster.LichMonster>();
        var mc   = lich != null ? lich.MovementController : null;
        if (mc == null || !RelicFairy.Monster.LichVfx.IsReady) { Debug.LogWarning("[TestHub] 리치·이펙트 목록이 아직 없다."); return; }

        ClearLichVfxGallery();
        s_gallerySlots.Clear();
        s_galleryPositions.Clear();
        s_hitSizeMode = true;

        int rows = Mathf.CeilToInt(HitSizeRows.Length / (float)HitSizeColumns);
        Vector3 origin = mc.ArenaCenter;
        origin.y = mc.FloorY;
        var sb = new System.Text.StringBuilder($"[TestHub] 판정 크기 대조 — 중심 {origin} · 열 {HitSizeColumns} · 간격 {HitSizeSpacing}m (흰 원 = 판정 반경)\n");
        for (int i = 0; i < HitSizeRows.Length; i++)
        {
            int col = i % HitSizeColumns;
            int row = i / HitSizeColumns;
            Vector3 p = origin + new Vector3((col - (HitSizeColumns - 1) * 0.5f) * HitSizeSpacing, 0f,
                                             ((rows - 1) * 0.5f - row) * HitSizeSpacing);
            s_galleryPositions.Add(p);
            sb.Append($"  [{row},{col}] {HitSizeRows[i].Label} · {HitSizeRows[i].Slot} ×{HitSizeRows[i].Scale:0.##} @ ({p.x:0},{p.z:0})\n");
        }
        Debug.Log(sb.ToString());

        SpawnLichVfxGallery();
        s_galleryEnd  = EditorApplication.timeSinceStartup + GallerySeconds;
        s_galleryNext = EditorApplication.timeSinceStartup + GalleryReplay;
        EditorApplication.update -= TickLichVfxGallery;
        EditorApplication.update += TickLichVfxGallery;
    }

    private static void SpawnHitSizeRows()
    {
        for (int i = 0; i < HitSizeRows.Length && i < s_galleryPositions.Count; i++)
        {
            var row = HitSizeRows[i];
            var p   = s_galleryPositions[i];
            var go  = RelicFairy.Monster.LichVfx.Play(row.Slot, p + Vector3.up * 0.05f, Quaternion.identity, row.Scale);
            if (go != null) s_galleryObjects.Add(go);

            var disc = RelicFairy.Monster.PatternGuideHelper.Disc(p, row.Radius, Color.white);
            RelicFairy.Monster.PatternGuideHelper.SetProgress(disc, 0f);   // 테두리만 — 이펙트가 원을 넘는지·모자란지 본다
            disc.name = "GalleryLabel_Disc";
            s_galleryObjects.Add(disc);

            var label = new GameObject("GalleryLabel_" + row.Slot);
            label.transform.position = p + new Vector3(0f, 0.2f, -Mathf.Max(3.2f, row.Radius + 1f));
            label.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            var text = label.AddComponent<TextMesh>();
            text.text          = row.Label;
            text.font          = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.characterSize = 0.18f;
            text.fontSize      = 48;
            text.anchor        = TextAnchor.MiddleCenter;
            text.color         = Color.white;
            label.GetComponent<MeshRenderer>().sharedMaterial = text.font.material;
            s_galleryObjects.Add(label);
        }
    }

    private static void StartLichVfxGallery(int page)
    {
        if (!Application.isPlaying) { Debug.LogWarning("[TestHub] 플레이 모드에서만 동작한다."); return; }
        var lich = Object.FindFirstObjectByType<RelicFairy.Monster.LichMonster>();
        var mc   = lich != null ? lich.MovementController : null;
        if (mc == null || !RelicFairy.Monster.LichVfx.IsReady) { Debug.LogWarning("[TestHub] 리치·이펙트 목록이 아직 없다."); return; }

        ClearLichVfxGallery();
        s_gallerySlots.Clear();
        s_galleryPositions.Clear();
        s_hitSizeMode = false;

        var all = new System.Collections.Generic.List<RelicFairy.Monster.LichVfxSlot>();
        foreach (RelicFairy.Monster.LichVfxSlot slot in System.Enum.GetValues(typeof(RelicFairy.Monster.LichVfxSlot)))
        {
            // 화면 전체 오버레이(Screen*)는 카메라에 붙는 이펙트라 진열장에선 뜻이 없다.
            if (slot == RelicFairy.Monster.LichVfxSlot.None || (int)slot >= 70) continue;
            if (RelicFairy.Monster.LichVfx.Has(slot)) all.Add(slot);
        }

        int from = page * GalleryPageSize;
        int count = Mathf.Clamp(all.Count - from, 0, GalleryPageSize);
        int rows  = Mathf.CeilToInt(count / (float)GalleryColumns);
        Vector3 origin = mc.ArenaCenter;
        origin.y = mc.FloorY;

        var sb = new System.Text.StringBuilder($"[TestHub] 리치 이펙트 진열 {page + 1}쪽 — 중심 {origin} · 열 {GalleryColumns} · 간격 {GallerySpacing}m (+X 오른쪽, +Z 위쪽 줄부터)\n");
        for (int i = 0; i < count; i++)
        {
            int col = i % GalleryColumns;
            int row = i / GalleryColumns;
            Vector3 p = origin + new Vector3((col - (GalleryColumns - 1) * 0.5f) * GallerySpacing, 0f,
                                             ((rows - 1) * 0.5f - row) * GallerySpacing);
            s_gallerySlots.Add(all[from + i]);
            s_galleryPositions.Add(p);
            sb.Append($"  [{row},{col}] {all[from + i]} @ ({p.x:0},{p.z:0})\n");
        }
        Debug.Log(sb.ToString());

        SpawnLichVfxGallery();
        s_galleryEnd  = EditorApplication.timeSinceStartup + GallerySeconds;
        s_galleryNext = EditorApplication.timeSinceStartup + GalleryReplay;
        EditorApplication.update -= TickLichVfxGallery;
        EditorApplication.update += TickLichVfxGallery;
    }

    private static void SpawnLichVfxGallery()
    {
        if (s_hitSizeMode)
        {
            SpawnHitSizeRows();
            return;
        }
        for (int i = 0; i < s_gallerySlots.Count; i++)
        {
            var slot = s_gallerySlots[i];
            var p    = s_galleryPositions[i];
            GameObject go = slot == RelicFairy.Monster.LichVfxSlot.FireBeam
                ? RelicFairy.Monster.LichVfx.PlayBeam(slot, p + Vector3.up, p + Vector3.up + Vector3.forward * 8f, 1f, GalleryReplay)
                : RelicFairy.Monster.LichVfx.PlayLoop(slot, p + Vector3.up * 0.05f, Quaternion.identity);
            if (go != null) s_galleryObjects.Add(go);

            var label = new GameObject("GalleryLabel_" + slot);
            label.transform.position = p + new Vector3(0f, 0.2f, -3.2f);
            label.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            var text = label.AddComponent<TextMesh>();
            text.text          = slot.ToString();
            text.font          = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.characterSize = 0.18f;
            text.fontSize      = 48;
            text.anchor        = TextAnchor.MiddleCenter;
            text.color         = Color.white;
            label.GetComponent<MeshRenderer>().sharedMaterial = text.font.material;
            s_galleryObjects.Add(label);
        }
    }

    private static void TickLichVfxGallery()
    {
        double now = EditorApplication.timeSinceStartup;
        if (!Application.isPlaying || now >= s_galleryEnd)
        {
            EditorApplication.update -= TickLichVfxGallery;
            if (Application.isPlaying) ClearLichVfxGallery();
            return;
        }
        if (now < s_galleryNext) return;
        s_galleryNext = now + GalleryReplay;
        ClearLichVfxGallery();
        SpawnLichVfxGallery();
    }

    private static void ClearLichVfxGallery()
    {
        for (int i = 0; i < s_galleryObjects.Count; i++)
        {
            var go = s_galleryObjects[i];
            if (go == null) continue;
            if (go.name.StartsWith("GalleryLabel_")) Object.Destroy(go);
            else RelicFairy.Monster.LichVfx.Stop(ref go);
        }
        s_galleryObjects.Clear();
    }

    /// <summary>플레이어 상태 한 줄 — 위치 · 입력 잠금 · 접지 · 발밑 콜라이더(레이어). 「못 움직인다」 진단용.</summary>
    [MenuItem(Root + "Log Player State (Play)")]
    public static void LogPlayerState()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[TestHub] 플레이 모드에서만 동작한다."); return; }

        var player = GameRunBootstrapper.Instance?.Run?.Player;
        if (player == null) { Debug.LogWarning("[TestHub] 런 플레이어가 없다."); return; }

        var lockField = typeof(PlayerController).GetField("_inputDisabledExternally",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        string locked = lockField != null ? lockField.GetValue(player).ToString() : "?";

        var pos = player.transform.position;
        string under = "없음";
        if (Physics.Raycast(pos + Vector3.up, Vector3.down, out var hit, 5f, ~0, QueryTriggerInteraction.Ignore))
            under = $"{hit.collider.name} (layer {LayerMask.LayerToName(hit.collider.gameObject.layer)}, y={hit.point.y:F2})";

        var stats = player.RuntimeStats;
        Debug.Log($"[TestHub] 플레이어 {pos} · HP {stats?.Hp}/{stats?.MaxHp} · 입력잠금={locked} · 접지={player.IsGrounded()} · 발밑={under}");
    }
}
#endif
