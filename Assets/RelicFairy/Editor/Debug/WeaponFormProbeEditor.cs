#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Inputs;
using RelicFairy.Monster;
using UnityEditor;
using UnityEngine;
using TMPro;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

/// <summary>
/// [실측 · 플레이 중] 장비 개선 「형」 — 데이터 · 진화 · 단련 · 칼바람 · 무게 · 변주 · 예산.
/// 테스트 허브에서 런을 열고 첫 방에 들어서면 자동 플레이를 멈추고 이 메뉴의 일을 돈다. 결과 Temp/weapon_form_*.txt
/// 기획: 바탕화면 기획/RelicFairy_장비개선_진화형능력·형단련_20261002.md · 구현계획 구현설계/RelicFairy_장비개선_형능력·형단련_구현계획_20261006.md
/// </summary>
public static class WeaponFormProbeEditor
{
    private const string Root     = "RelicFairy/Debug/장비 형/";
    private const string AutoRoot = "RelicFairy/Debug/런 구조 자동 실측/";
    private const string Nameless = "Assets/RelicFairy/Weapon/Nameless/Data/T0_Nameless.asset";
    private const string Katana   = "Assets/RelicFairy/Weapon/Katana/Data/T1_Katana.asset";
    private const string Great    = "Assets/RelicFairy/Weapon/Greatsword/Data/T1_Greatsword.asset";
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static string s_job;
    private static bool   s_armed;
    private static readonly List<string> s_formLog = new();

    [MenuItem(Root + "1 데이터 · 진화 · 단련 (테스트 허브, 플레이 중)")]
    private static void BeginData() => Begin("data");

    [MenuItem(Root + "2 칼바람 (테스트 허브, 플레이 중)")]
    private static void BeginWind() => Begin("wind");

    [MenuItem(Root + "3 무게 (테스트 허브, 플레이 중)")]
    private static void BeginWeight() => Begin("weight");

    [MenuItem(Root + "4 변주 8종 (테스트 허브, 플레이 중)")]
    private static void BeginVariants() => Begin("variants");

    [MenuItem(Root + "7 재련소 화면 — 활 든 채 진화 · 형 단련 (테스트 허브, 플레이 중)")]
    private static void BeginCrucible() => Begin("crucible");

    [MenuItem(Root + "8 평타 이펙트 방향 — 칼 궤적 대조 (테스트 허브, 플레이 중)")]
    private static void BeginSlash() => Begin("slash");

    [MenuItem(Root + "8c 평타 이펙트 방향 — 대검만 (테스트 허브, 플레이 중)")]
    private static void BeginSlashGreat() => Begin("slash_great");

    [MenuItem(Root + "15c 대시 중 약 피해 — 틱 · 잡기 회피 · 큰 한 방 (테스트 허브, 플레이 중)")]
    private static void BeginSkillTier3() => Begin("skilltier3");

    [MenuItem(Root + "15b 무형일섬 3단계 · 대시 중 장판 틱 · 잡기 회피 (테스트 허브, 플레이 중)")]
    private static void BeginSkillTier2() => Begin("skilltier2");

    [MenuItem(Root + "15 강화 점검 — 스킬 단계 수정 (테스트 허브, 플레이 중)")]
    private static void BeginSkillTier() => Begin("skilltier");

    [MenuItem(Root + "14 스킬별 손질 C (테스트 허브, 플레이 중)")]
    private static void BeginSkillC() => Begin("skillc");

    [MenuItem(Root + "13 스킬 트레일 · HUD 신호 · 범위 고리 (테스트 허브, 플레이 중)")]
    private static void BeginSkillVis() => Begin("skillvis");

    [MenuItem(Root + "12 스킬 조작감 A1~A5 (테스트 허브, 플레이 중)")]
    private static void BeginSkillFeel() => Begin("skillfeel");

    [MenuItem(Root + "11 스킬 버그 B1~B7 (테스트 허브, 플레이 중)")]
    private static void BeginSkillBugs() => Begin("skillbugs");

    [MenuItem(Root + "10 스킬 피격 이펙트 — 이중 확인 (테스트 허브, 플레이 중)")]
    private static void BeginSkillFx() => Begin("skillfx");

    [MenuItem(Root + "8b 슬래시 기준 촬영만 — 자라는 방향 (테스트 허브, 플레이 중)")]
    private static void BeginSlashRef() => Begin("slash_ref");

    [MenuItem(Root + "8d 평타 이펙트 방향 — 카타나만 (테스트 허브, 플레이 중)")]
    private static void BeginSlashKatana() => Begin("slash_katana");

    [MenuItem(Root + "22 애니메이션 흐름 · 자연스러움 (테스트 허브, 플레이 중)")]
    private static void BeginAnimFlow() => Begin("animflow");

    [MenuItem(Root + "23 런 공용 이펙트 빈 칸 후보 (테스트 허브, 플레이 중)")]
    private static void BeginRunFxCand() => Begin("runfxcand");

    [MenuItem(Root + "24 출시 점검 수정 확인 (테스트 허브, 플레이 중)")]
    private static void BeginReleaseFix() => Begin("releasefix");

    // 플레이어 낙하 루프(JumpBlend의 JumpValue=1 칸)가 loopTime 0이라 0.8초 넘게 떨어지면 공중 자세가 마지막 프레임에 굳었다(10-09 출시 점검)
    [MenuItem(Root + "25 낙하 루프 클립 반복 켜기 (에디터)")]
    private static void FixFallLoopClip()
    {
        const string path = "Assets/RelicFairy/Animations/Player/Test_01/Jump/GhostSamurai_APose_Jump_Loop_Inplace.FBX";
        if (AssetImporter.GetAtPath(path) is not ModelImporter mi) { Debug.LogError("[WeaponFormProbe] 낙하 루프 임포터 없음"); return; }
        var clips = mi.clipAnimations;
        if (clips == null || clips.Length == 0) { Debug.LogError("[WeaponFormProbe] 낙하 루프 클립 설정 없음"); return; }
        var sb = new StringBuilder();
        foreach (var c in clips) { sb.Append($"{c.name} loop {c.loopTime} → True; "); c.loopTime = true; }
        mi.clipAnimations = clips;
        mi.SaveAndReimport();
        Debug.Log("[WeaponFormProbe] 낙하 루프 반복 켬 — " + sb);
    }

    [MenuItem(Root + "26 겹침 줄이기 — 동작 후보 미리보기 (테스트 허브, 플레이 중)")]
    private static void BeginAnimCandidates() => Begin("anim_cand");

    [MenuItem(Root + "27 동작 연결 점수 (테스트 허브, 플레이 중)")]
    private static void BeginAnimConn() => Begin("anim_conn");

    [MenuItem(Root + "28 동작 연속기 확인 (테스트 허브, 플레이 중)")]
    private static void BeginChains() => Begin("chains");

    [MenuItem(Root + "29 무형검 연타 진단 (테스트 허브, 플레이 중)")]
    private static void BeginNamelessDiag() => Begin("nldiag");

    [MenuItem(Root + "30 석궁 동작 — 쏘는 순간 찾기 (테스트 허브, 플레이 중)")]
    private static void BeginCrossbowFrames() => Begin("xbow");

    [MenuItem(Root + "31 기본 이동 자세 촬영 (테스트 허브, 플레이 중)")]
    private static void BeginLocoPose() => Begin("loco");

    [MenuItem(Root + "31b 이동 자세 촬영 — 대검 (테스트 허브, 플레이 중)")]
    private static void BeginLocoPoseGreat() => Begin("loco_great");

    [MenuItem(Root + "33 평타 이어짐 필름 — 무형검 (테스트 허브, 플레이 중)")]
    private static void BeginComboFilm() => Begin("combofilm");

    [MenuItem(Root + "36 형 발광 — 무기 전환 뒤 (10-12 B11, 테스트 허브, 플레이 중)")]
    private static void BeginFormGlow() => Begin("glow");

    [MenuItem(Root + "37 이펙트 = 판정 — 평타 · 스킬 · 형 · 유물 · 룬 · 전설 (10-13, 테스트 허브, 플레이 중)")]
    private static void BeginEffectJudgment() => Begin("ejplayer");

    [MenuItem(Root + "34 이펙트 톤 촬영 — 79 담당 10종 (테스트 허브, 플레이 중)")]
    private static void BeginToneFx() => Begin("tonefx");

    [MenuItem(Root + "35 평타 전수 — 무기 5종 · 연타 0.1 · 0.3초 (테스트 허브, 플레이 중)")]
    private static void BeginNormalAttack() => Begin("nattack");

    [MenuItem(Root + "34b 맞힘 이펙트 계층 배율 전 · 후 (테스트 허브, 플레이 중)")]
    private static void BeginToneFxHier() => Begin("tonefx_hier");

    [MenuItem(Root + "32 랜슬롯 Q 연타 중 회전 (테스트 허브, 플레이 중)")]
    private static void BeginQTurn() => Begin("qturn");

    [MenuItem(Root + "21 저스트 회피 슬로모 중 스킬 피해 시각 (테스트 허브, 플레이 중)")]
    private static void BeginWitchTime() => Begin("witchtime");

    [MenuItem(Root + "20 이어하기 유물 주소 (테스트 허브, 플레이 중)")]
    private static void BeginRelicKeys() => Begin("relickeys");

    [MenuItem(Root + "19 플레이어 의상 (테스트 허브, 플레이 중)")]
    private static void BeginCostume() => Begin("costume");

    [MenuItem(Root + "18 환영베기 전용 연타 (테스트 허브, 플레이 중)")]
    private static void BeginPhantomFlurry() => Begin("phantom_flurry");

    [MenuItem(Root + "17 석궁 추가탄 = 기본 공격만 (테스트 허브, 플레이 중)")]
    private static void BeginExtraShot() => Begin("extrashot");

    [MenuItem(Root + "16 환영베기 연타 모션 후보 미리보기 (테스트 허브, 플레이 중)")]
    private static void BeginPhantomCandidates() => Begin("phantom_cand");

    [MenuItem(Root + "9 대검 콤보 후보 미리보기 (테스트 허브, 플레이 중)")]
    private static void BeginGreatCandidates() => Begin("great_cand");

    [MenuItem(Root + "5 예산 6칸 (테스트 허브, 플레이 중, 약 5분)")]
    private static void BeginBudget() => Begin("budget");

    [MenuItem(Root + "5b 예산 — 대검 3칸만 (테스트 허브, 플레이 중, 약 3분)")]
    private static void BeginBudgetGreat() => Begin("budget_great");

    [MenuItem(Root + "38 평타 초당 피해 R2 — 무형검 · 카타나 · 대검 (형 끔 · 강화 0 · 테스트 허브, 플레이 중, 약 3분)")]
    private static void BeginR2() => Begin("r2");

    [MenuItem(Root + "39 빌드 시너지 계획 1 — 정오 공속 · 스탯 룬 각인 · 발동 계수 · 포이즈 · CC 간격 · 같은 적 · 내려찍기 (테스트 허브, 플레이 중)")]
    private static void BeginBs1() => Begin("bs1");

    [MenuItem(Root + "47 빌드 시너지 §5 — 빌드 4종 초당 피해 · 단일 + 무리 (테스트 허브, 플레이 중, 약 4분)")]
    private static void BeginBs5x() => Begin("bs5x");

    [MenuItem(Root + "48 화살 멈칫 — 활 · 석궁 6초 연사 중 느려진 시간 비율 (테스트 허브, 플레이 중)")]
    private static void BeginArrowFeel() => Begin("arrowfeel");

    [MenuItem(Root + "49 처치 이펙트 맞춤 — 몬스터 7종 몸 대비 위치 · 크기 · 사진 (테스트 허브, 플레이 중)")]
    private static void BeginDeathFx() => Begin("deathfx");

    [MenuItem(Root + "51 추적 화살 × 새끼 용 — 보스 잡몹을 노리나 (테스트 허브, 플레이 중)")]
    private static void BeginMiniHoming() => Begin("minihome");

    [MenuItem(Root + "52 분열 갈래 반감 — 활 다발 1발 vs 3발 · 룬 몫 (테스트 허브, 플레이 중)")]
    private static void BeginSplitCut() => Begin("splitcut");

    [MenuItem(Root + "53 원거리 무기 바꿔 끼운 뒤 발사 — 활 → 석궁 · 카타나 → 활 (테스트 허브, 플레이 중)")]
    private static void BeginRangedSwap() => Begin("rswap");

    [MenuItem(Root + "54 화살 경직 · 넉백 없음 — 카타나(대조) vs 활 (테스트 허브, 플레이 중)")]
    private static void BeginNoFlinch() => Begin("noflinch");

    [MenuItem(Root + "55 범위 표시 구분 — 적 예고 vs 플레이어 표시 사진 (테스트 허브, 플레이 중)")]
    private static void BeginGuides() => Begin("guides");

    [MenuItem(Root + "50 돌진 벽 — 무형검 R · 회피 대시 · 카타나 E가 벽을 넘나 (테스트 허브, 플레이 중)")]
    private static void BeginDashWall() => Begin("dashwall");

    [MenuItem(Root + "45 빌드 시너지 계획 6 — 유물 진화(가웨인) 7칸 (테스트 허브, 플레이 중)")]
    private static void BeginBs6() => Begin("bs6");

    [MenuItem(Root + "46 빌드 시너지 계획 3b — 사격 조각(가웨인) 4칸 (테스트 허브, 플레이 중)")]
    private static void BeginBs3bG() => Begin("bs3bg");

    [MenuItem(Root + "46b 빌드 시너지 계획 3b — 사격 조각(랜슬롯) 3칸 (테스트 허브, 플레이 중)")]
    private static void BeginBs3bL() => Begin("bs3bl");

    [MenuItem(Root + "44 빌드 시너지 계획 5 — 룬 재설계 새 효과 7칸 (테스트 허브, 플레이 중)")]
    private static void BeginBs5() => Begin("bs5");

    [MenuItem(Root + "43 활 · 석궁 R 스킬 교환 확인 — 석궁 궁수의 집중 · 활 더 빨리쏘기 (테스트 허브, 플레이 중)")]
    private static void BeginK4() => Begin("k4");

    [MenuItem(Root + "42 빌드 시너지 계획 4 — 전설 룬 새 발동 7칸 (테스트 허브, 플레이 중)")]
    private static void BeginBs4() => Begin("bs4");

    [MenuItem(Root + "41 빌드 시너지 계획 3 — 조각 · 서약 × 각인 · 탄두 × 존 · 보유 우대 · 겨눔 · 시작 파츠 (테스트 허브, 플레이 중)")]
    private static void BeginBs3() => Begin("bs3");

    [MenuItem(Root + "40 빌드 시너지 계획 2 — 독 중첩 · 화상 · 작열 · 정전기 · 광채 · 넘친 공속 · 표식 간격 (테스트 허브, 플레이 중)")]
    private static void BeginBs2() => Begin("bs2");

    [MenuItem(Root + "39b 빌드 시너지 계획 1 — 정예 포이즈만 (테스트 허브, 플레이 중)")]
    private static void BeginBs1Poise() => Begin("bs1p");

    [MenuItem(Root + "6 연출 후보 비교 (테스트 허브, 플레이 중)")]
    private static void BeginFx() => Begin("fx");

    private static void Begin(string job)
    {
        if (!Application.isPlaying) { Debug.LogWarning("[WeaponFormProbe] 플레이 모드에서만 동작한다."); return; }
        var launcher = UnityEngine.Object.FindFirstObjectByType<TestHubLauncher>();
        if (launcher == null) { Debug.LogWarning("[WeaponFormProbe] 테스트 허브에서 시작한다."); return; }
        // 예산은 유물 없이 — 가웨인 「정오」 주기 · 낙인 누적이 칸 순서대로 피해를 키워 형 몫을 가렸다(실측 5 1회차 대검 318 → 405 → 552).
        // 인덱스 −1은 허브 화면이 0으로 되돌린다(2회차도 가웨인) → 목록을 「없음」 하나로 바꾼다(Pick → null → 유물 없음).
        if (job.StartsWith("budget") || job == "r2" || (job.StartsWith("bs") && job != "bs6" && !job.StartsWith("bs3b"))) typeof(TestHubLauncher).GetField("relics", Inst)?.SetValue(launcher, new RelicClassSO[] { null });   // R2도 유물 없이(10-13)
        if (job == "chains" || job == "qturn" || job == "bs3bl")   // 랜슬롯 Q — 허브 유물을 랜슬롯으로(LancelotQSwordProbeEditor와 같은 길)
        {
            var relics = typeof(TestHubLauncher).GetField("relics", Inst)?.GetValue(launcher) as RelicClassSO[];
            int idx = relics == null ? -1 : Array.FindIndex(relics, r => r != null && r.Id.ToString() == "Lancelot");
            if (idx >= 0) typeof(TestHubLauncher).GetField("_relicIndex", Inst)?.SetValue(launcher, idx);
        }
        s_job = job;
        s_armed = true;
        Application.logMessageReceived -= OnLog;
        Application.logMessageReceived += OnLog;
        EditorApplication.ExecuteMenuItem(AutoRoot + "시작 (테스트 허브에서, 플레이 중)");
    }

    private static void OnLog(string msg, string stack, LogType type)
    {
        if (msg.StartsWith("[WeaponForm]")) s_formLog.Add(msg);
        if (!s_armed || !msg.StartsWith("[RunAuto]") || !msg.Contains("방 진입")) return;
        s_armed = false;
        EditorApplication.delayCall += () =>
        {
            EditorApplication.ExecuteMenuItem(AutoRoot + "중지·기록");
            var p = GameRunBootstrapper.Instance?.Run?.Player;
            if (p == null) { Debug.LogWarning("[WeaponFormProbe] 런 플레이어가 없다"); return; }
            RunAsync(p, p.GetCancellationTokenOnDestroy()).Forget();
        };
    }

    private static async UniTaskVoid RunAsync(PlayerController p, CancellationToken ct)
    {
        var sb = new StringBuilder($"장비 형 실측 — {s_job}\n");
        try
        {
            TakeMouse();
            typeof(PlayerController).GetField("debugInvincible", Inst)?.SetValue(p, true);
            GameRunBootstrapper.Instance?.Run?.CovenantHandler?.Cleanup();
            foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
                if (m != null && !m.IsDead) m.gameObject.SetActive(false);
            await Wait(2f, ct);
            switch (s_job)
            {
                case "data": await DataAsync(p, sb, ct); break;
                case "ejplayer": await EffectJudgmentPlayerAsync(p, sb, ct); break;
                case "glow": await FormGlowAsync(p, sb, ct); break;
                case "wind": await WindAsync(p, sb, ct); break;
                case "weight": await WeightAsync(p, sb, ct); break;
                case "variants": await VariantsAsync(p, sb, ct); break;
                case "crucible": await CrucibleAsync(p, sb, ct); break;
                case "budget_great": await BudgetAsync(p, sb, ct, Great); break;
                case "slash": await SlashDirAsync(p, sb, ct); break;
                case "slash_ref": await SlashDirAsync(p, sb, ct, refOnly: true); break;
                case "slash_great": await SlashDirAsync(p, sb, ct, onlyPath: Great); break;
                case "slash_katana": await SlashDirAsync(p, sb, ct, onlyPath: Katana); break;
                case "skillfx": await SkillFxAsync(p, sb, ct); break;
                case "skillbugs": await SkillBugsAsync(p, sb, ct); break;
                case "skillfeel": await SkillFeelAsync(p, sb, ct); break;
                case "skillvis": await SkillVisAsync(p, sb, ct); break;
                case "skillc": await SkillCAsync(p, sb, ct); break;
                case "skilltier": await SkillTierAsync(p, sb, ct); break;
                case "skilltier2": await SkillTierAsync(p, sb, ct, onlyNew: true); break;
                case "skilltier3": await SkillTierAsync(p, sb, ct, onlyNew: true, dodgeOnly: true); break;
                case "great_cand": await GreatCandidatesAsync(p, sb, ct); break;
                case "phantom_cand": await PhantomCandidatesAsync(p, sb, ct); break;
                case "extrashot": await ExtraShotAsync(p, sb, ct); break;
                case "phantom_flurry": await PhantomFlurryAsync(p, sb, ct); break;
                case "costume": await CostumeAsync(p, sb, ct); break;
                case "relickeys": await RelicKeysAsync(p, sb, ct); break;
                case "witchtime": await WitchTimeAsync(p, sb, ct); break;
                case "xbow": await CrossbowFramesAsync(p, sb, ct); break;
                case "nldiag": await NamelessComboDiagAsync(p, sb, ct); break;
                case "loco": await LocoPoseAsync(p, sb, ct, Nameless); break;
                case "loco_great": await LocoPoseAsync(p, sb, ct, Great, "great_"); break;
                case "qturn": await QTurnAsync(p, sb, ct); break;
                case "combofilm": await ComboFilmAsync(p, sb, ct); break;
                case "tonefx": await ToneFxAsync(p, sb, ct); break;
                case "tonefx_hier": await ToneFxAsync(p, sb, ct, ToneFxHier); break;
                case "nattack": await NormalAttackAuditAsync(p, sb, ct); break;
                case "chains": await ChainsAsync(p, sb, ct); break;
                case "anim_conn": await AnimConnAsync(p, sb, ct); break;
                case "anim_cand": await AnimCandidatesAsync(p, sb, ct); break;
                case "releasefix": await ReleaseFixAsync(p, sb, ct); break;
                case "runfxcand": await RunFxCandAsync(p, sb, ct); break;
                case "animflow": await AnimFlowAsync(p, sb, ct); break;
                case "budget": await BudgetAsync(p, sb, ct); break;
                case "r2": await R2Async(p, sb, ct); break;
                case "bs1": await BuildSynergy1Async(p, sb, ct); break;
                case "bs1p": await BuildSynergy1Async(p, sb, ct, poiseOnly: true); break;
                case "bs2": await BuildSynergy2Async(p, sb, ct); break;
                case "bs3": await BuildSynergy3Async(p, sb, ct); break;
                case "bs4": await BuildSynergy4Async(p, sb, ct); break;
                case "k4": await SkillSwapK4Async(p, sb, ct); break;
                case "bs5": await BuildSynergy5Async(p, sb, ct); break;
                case "bs6": await BuildSynergy6Async(p, sb, ct); break;
                case "dashwall": await DashWallAsync(p, sb, ct); break;
                case "minihome": await MiniHomingAsync(p, sb, ct); break;
                case "splitcut": await SplitCutAsync(p, sb, ct); break;
                case "rswap": await RangedSwapAsync(p, sb, ct); break;
                case "noflinch": await NoFlinchAsync(p, sb, ct); break;
                case "guides": await GuidesAsync(p, sb, ct); break;
                case "deathfx": await DeathFxAsync(p, sb, ct); break;
                case "arrowfeel": await ArrowFeelAsync(p, sb, ct); break;
                case "bs5x": await FourBuildsAsync(p, sb, ct); break;
                case "bs3bg": await BuildSynergy3bGawainAsync(p, sb, ct); break;
                case "bs3bl": await BuildSynergy3bLancelotAsync(p, sb, ct); break;
                case "fx": await FxAsync(p, sb, ct); break;
            }
        }
        catch (OperationCanceledException) { sb.AppendLine("취소됨"); }
        catch (Exception e) { sb.AppendLine("예외: " + e); Debug.LogException(e); }
        finally
        {
            ReleaseMouse();
            File.WriteAllText($"Temp/weapon_form_{s_job}.txt", sb.ToString());
            Debug.Log($"[WeaponFormProbe] 끝 → Temp/weapon_form_{s_job}.txt");
            Application.logMessageReceived -= OnLog;
        }
    }

    // 실측 동안 진짜 마우스를 끄고 가상 마우스로 겨눈다. 커서가 게임 창 위에 있으면 진짜 마우스 위치가 계속 들어와
    // 넣은 조준을 덮었다(10-07 진단: 마우스 방향 149° 고정 · 원함 ≠ 읽음 → 콤보 2 · 3타가 옆 표적으로 돌고 헛침).
    private const string ProbeMouseName = "WeaponFormProbeMouse";
    private static Mouse s_probeMouse, s_realMouse;

    private static void TakeMouse()
    {
        if (s_probeMouse != null) return;
        s_realMouse = Mouse.current;
        s_probeMouse = InputSystem.AddDevice<Mouse>(ProbeMouseName);
        if (s_realMouse != null) InputSystem.DisableDevice(s_realMouse);
        s_probeMouse.MakeCurrent();
        EditorApplication.playModeStateChanged += ReleaseOnExit;
    }

    private static void ReleaseMouse()
    {
        EditorApplication.playModeStateChanged -= ReleaseOnExit;
        if (s_probeMouse != null) { InputSystem.RemoveDevice(s_probeMouse); s_probeMouse = null; }
        if (s_realMouse != null) { InputSystem.EnableDevice(s_realMouse); s_realMouse.MakeCurrent(); s_realMouse = null; }
    }

    private static void ReleaseOnExit(PlayModeStateChange s)
    {
        if (s == PlayModeStateChange.ExitingPlayMode) ReleaseMouse();
    }

    /// <summary>실측 중 리컴파일 · 리로드로 되돌리지 못한 마우스를 되살린다(정적 참조는 리로드에 사라진다).</summary>
    [InitializeOnLoadMethod]
    private static void RestoreMiceAfterReload()
    {
        foreach (var d in InputSystem.devices.ToArray())
        {
            if (d is not Mouse) continue;
            if (d.name == ProbeMouseName) InputSystem.RemoveDevice(d);
            else if (!d.enabled) InputSystem.EnableDevice(d);
        }
    }

    // ── 15 강화 점검 — 스킬 단계 수정(10-08) ──────────────
    private static async UniTask SkillTierAsync(PlayerController p, StringBuilder sb, CancellationToken ct, bool onlyNew = false, bool dodgeOnly = false)
    {
        var actField = typeof(PlayerController).GetField("actStateDebug", Inst);
        bool InSkill() => (actField?.GetValue(p)?.ToString() ?? "").Contains("Skill");
        var aimObj = typeof(PlayerController).GetField("_aim", Inst)?.GetValue(p) as PlayerAim;
        await Wait(3f, ct);

        async UniTask AimSettle(Vector3 world)
        {
            for (int i = 0; i < 30; i++)
            {
                AimAt(world);
                await UniTask.DelayFrame(2, cancellationToken: ct);
                if (aimObj != null && aimObj.TryComputeLookDir(p.transform, out var look) && Vector3.Angle(look, Flat(world - p.transform.position)) < 3f) return;
            }
        }

        // 스킬을 쓰고 플레이어가 준 적중(표적 · 피해 · 마무리 · 치명)을 모은다
        async UniTask<List<(GameObject who, float dmg, bool fin, bool crit)>> Hits(Func<UniTask> cast, float sec)
        {
            var list = new List<(GameObject, float, bool, bool)>();
            Action<HitInfo> onHit = h => { if (h.Attacker == p.gameObject && h.Target != null) list.Add((h.Target, h.Damage, h.IsFinisher, h.IsCritical)); };
            HitFeedbackService.OnHit += onHit;
            try { await cast(); await Wait(sec, ct); }
            finally { HitFeedbackService.OnHit -= onHit; }
            return list;
        }

        async UniTask<(Vector3 f, Vector3 at)> Setup(string path, int enhance, int slot = PlayerWeaponManager.Slot0)
        {
            await EquipAsync(p, path, ct, slot);
            var wd = slot == PlayerWeaponManager.Slot0 ? p.WeaponManager.Weapon0Data : p.WeaponManager.CurrentWeaponData;
            if (wd != null) { wd.enhanceLevel = enhance; wd.RecomputeEnhancedStats(); }
            await Wait(1f, ct);
            return (Flat(p.transform.forward), p.transform.position);
        }

        async UniTask CastR() { p.CooldownTracker.ResetCooldown(SkillType.R); p.InputBuffer.Push(Command.RSkill); await UniTask.Yield(); }

        // 1 무형일섬 3단계(+6) — 길 가르기(마무리) = 연참 한 타의 3배. 예전엔 기본값만 3배(15 + 공격력)
        if (!dodgeOnly)
        {
            string note = "적중 없음"; bool ok = false;
            for (int k = 0; k < 3 && !ok; k++)   // 치명이 섞이면 비율이 흐려진다 — 자리를 새로 잡아 다시 쓴다
            {
                DetachAll();
                var (f, at) = await Setup(Nameless, 6);
                s_home = at;
                var t = await TrackAsync(at + f * 4f, ct);
                HideStrays();
                await AimSettle(at + f * 6f);
                var hits = await Hits(CastR, 1.6f);
                await WaitEnd(InSkill, ct);
                hits = hits.FindAll(h => t != null && h.who == t.M.gameObject);
                var slash = hits.FindAll(h => !h.fin && !h.crit);   // 치명 타는 비교에서 뺀다(실측 15 1회차 — 세 번 다 치명이 섞였다)
                var cut = hits.Find(h => h.fin);
                if (cut.who != null && cut.crit) { note = "길 가르기가 치명 — 다시"; continue; }
                if (slash.Count == 0 || cut.who == null) { note = $"연참 {slash.Count} · 길 가르기 {(cut.who != null)}"; continue; }
                float ratio = cut.dmg / slash[0].dmg;
                note = $"연참 한 타 {slash[0].dmg:0.#} · 길 가르기 {cut.dmg:0.#} · 비율 {ratio:0.00}(목표 3.00)";
                ok = Mathf.Abs(ratio - 3f) < 0.1f;
            }
            Check(sb, "무형일섬 3단계 — " + note, ok);
            DetachAll();
        }

        // 2 궁수의 집중 1·2·3단계(파츠 레벨 합 0 · 2 · 10) — 단일 표적 피해 합이 단계마다 오른다(예전엔 2단계에서 줄었다)
        if (!onlyNew)
        {
            var prevParts = RangedPartsState.Current;
            var totals = new float[3];
            var notes = new string[3];
            try
            {
                var (f, at) = await Setup(Bow, 0, PlayerWeaponManager.Slot1);
                s_home = at;
                var t = await TrackAsync(at + f * 5f, ct);
                HideStrays();
                var kb = Keyboard.current;
                async UniTask HoldR()
                {
                    p.CooldownTracker.ResetCooldown(SkillType.R);
                    InputSystem.QueueStateEvent(kb, new KeyboardState(Key.R));
                    await Wait(1.3f, ct);   // 끝까지 모은다(1초)
                    InputSystem.QueueStateEvent(kb, new KeyboardState());
                }
                int[] partLv = { 0, 2, 10 };
                for (int tier = 0; tier < 3; tier++)
                {
                    var st = new RangedPartsState();
                    if (partLv[tier] > 0) st.Equip("part_homing", partLv[tier]);   // 유도 — 피해를 바꾸지 않는 파츠로 레벨 합만 맞춘다
                    RangedPartsState.Current = st;
                    notes[tier] = "적중 없음";
                    for (int k = 0; k < 3; k++)
                    {
                        if (t != null) t.Refill();
                        await AimSettle(t != null ? t.M.transform.position : at + f * 5f);
                        var hits = await Hits(HoldR, 1.4f);
                        await WaitEnd(InSkill, ct);
                        hits = hits.FindAll(h => t != null && h.who == t.M.gameObject);
                        if (hits.Exists(h => h.crit)) { notes[tier] = "치명 섞임 — 다시"; continue; }
                        if (hits.Count == 0) continue;
                        float sum = 0f; var parts = new StringBuilder();
                        foreach (var h in hits) { sum += h.dmg; parts.Append(parts.Length > 0 ? "+" : "").Append(h.dmg.ToString("0.#")); }
                        totals[tier] = sum;
                        notes[tier] = $"{sum:0.#}({parts})";
                        break;
                    }
                }
            }
            finally { RangedPartsState.Current = prevParts; }
            Check(sb, $"궁수의 집중 피해 합 — 1단계 {notes[0]} · 2단계 {notes[1]} · 3단계 {notes[2]}",
                  totals[0] > 0f && totals[0] < totals[1] && totals[1] < totals[2]);
            DetachAll();
        }

        // 3 최후의 일격 — 판정 = 범위 고리(플레이어 중심 3 m 반원). 앞 2.5 m는 맞고, 몸 가장자리가 3.5 m인 표적은 안 맞는다
        //   (예전엔 구의 중심이 1.5 m 앞이라 4.5 m까지 닿았다)
        if (!onlyNew)
        {
            var (f, at) = await Setup(Great, 0);
            s_home = at;
            var near = await TrackAsync(at + f * 2.5f, ct);
            float r = near != null && near.M.TryGetComponent<Collider>(out var col) ? Mathf.Max(col.bounds.extents.x, col.bounds.extents.z) : 0.5f;
            var far = await TrackAsync(at + f * (3.5f + r), ct);
            HideStrays();
            await AimSettle(at + f * 6f);
            var hits = await Hits(CastR, 1.6f);
            await WaitEnd(InSkill, ct);
            bool hitNear = near != null && hits.Exists(h => h.who == near.M.gameObject);
            bool hitFar = far != null && hits.Exists(h => h.who == far.M.gameObject);
            Check(sb, $"최후의 일격 판정 — 2.5 m 적중 {hitNear} · {3.5f + r:0.0} m(몸 반경 {r:0.00}) 적중 {hitFar}", hitNear && !hitFar);
            DetachAll();
        }

        // 4 대시 중 장판 틱(약)은 흘리기만 — 저스트 회피(슬로모 · 무적 · 게이지 환급)를 일으키지 않는다
        // 5 잡기용 TryEvadeHit — 대시 밖 false · 대시 중 true(저스트 회피 1회)
        {
            var (f, at) = await Setup(Nameless, 0);
            s_home = at;
            var t = await TrackAsync(at + f * 6f, ct);
            HideStrays();
            GameObject src = t != null ? t.M.gameObject : null;
            int dodges = 0;
            Action<float> onPd = _ => dodges++;
            var outcomes = new List<(PlayerController.DamageOutcome o, int dmg)>();
            Action<GameObject, int, int, PlayerController.DamageOutcome> onRes = (a, raw, fin, o) => outcomes.Add((o, fin));
            p.OnPerfectDodge += onPd; p.OnDamageResolved += onRes;
            try
            {
                await Wait(1f, ct);
                p.InputBuffer.Push(Command.Dodge);
                float tw = Time.time;
                while (p.LocoSM.CurrentId != LocoState.Dodge && Time.time - tw < 0.3f) await UniTask.Yield(PlayerLoopTiming.Update, ct);
                bool inDodge = p.LocoSM.CurrentId == LocoState.Dodge;
                p.TakeDamage(5, src, false, HitWeight.Light);
                p.TakeDamage(5, src, false, HitWeight.Light);
                var desc = new StringBuilder();
                foreach (var x in outcomes) desc.Append(desc.Length > 0 ? ", " : "").Append(x.o).Append(' ').Append(x.dmg);
                Check(sb, $"대시 중 장판 틱 2번 — 대시 중 {inDodge} · 결과 [{desc}] · 저스트 회피 {dodges}회",
                      inDodge && dodges == 0 && outcomes.Count == 2 && outcomes.TrueForAll(x => x.dmg == 0));

                await Wait(1.5f, ct);
                bool outside = p.TryEvadeHit(src);
                p.InputBuffer.Push(Command.Dodge);
                tw = Time.time;
                while (p.LocoSM.CurrentId != LocoState.Dodge && Time.time - tw < 0.3f) await UniTask.Yield(PlayerLoopTiming.Update, ct);
                bool inside = p.TryEvadeHit(src);
                Check(sb, $"잡기 회피 TryEvadeHit — 대시 밖 {outside} · 대시 중 {inside} · 저스트 회피 {dodges}회", !outside && inside && dodges == 1);
                await Wait(2.5f, ct);   // 슬로모 · 반격 창이 끝나게

                // 6 약이라도 최대 HP 15% 이상 한 방(보스 환영 · 결계 폭격)은 대시로 피하면 저스트 회피(40 제안)
                p.InputBuffer.Push(Command.Dodge);
                tw = Time.time;
                while (p.LocoSM.CurrentId != LocoState.Dodge && Time.time - tw < 0.3f) await UniTask.Yield(PlayerLoopTiming.Update, ct);
                int before = dodges;
                int big = Mathf.CeilToInt(p.RuntimeStats.MaxHp * 0.2f);
                outcomes.Clear();
                p.TakeDamage(big, src, false, HitWeight.Light);
                Check(sb, $"대시 중 약 큰 한 방 {big}(최대 HP {p.RuntimeStats.MaxHp}의 20%) — 결과 {(outcomes.Count > 0 ? outcomes[0].o + " " + outcomes[0].dmg : "없음")} · 저스트 회피 {dodges - before}회",
                      dodges - before == 1 && outcomes.Count == 1 && outcomes[0].dmg == 0);
                await Wait(2.5f, ct);
            }
            finally { p.OnPerfectDodge -= onPd; p.OnDamageResolved -= onRes; }
            DetachAll();
        }
    }

    // ── 14 스킬별 손질 C(10-08 · 기획 §5-C, 결정 S4 · S5) ──────────────
    private static async UniTask SkillCAsync(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        var actField = typeof(PlayerController).GetField("actStateDebug", Inst);
        bool InSkill() => (actField?.GetValue(p)?.ToString() ?? "").Contains("Skill");
        var invEnd = typeof(PlayerController).GetField("_invincibleEnd", Inst);
        var aimObj = typeof(PlayerController).GetField("_aim", Inst)?.GetValue(p) as PlayerAim;
        await Wait(3f, ct);

        async UniTask AimSettle(Vector3 world)
        {
            for (int i = 0; i < 30; i++)
            {
                AimAt(world);
                await UniTask.DelayFrame(2, cancellationToken: ct);
                if (aimObj != null && aimObj.TryComputeLookDir(p.transform, out var look) && Vector3.Angle(look, Flat(world - p.transform.position)) < 3f) return;
            }
        }

        // 표적 하나에 스킬을 쓰고 적중(시각 · 피해 · 마무리)을 모은다
        async UniTask<List<(float t, float dmg, bool fin)>> Hits(HpTrack t, Func<UniTask> cast, float sec)
        {
            var list = new List<(float, float, bool)>();
            float t0 = Time.time;
            Action<HitInfo> onHit = h => { if (h.Attacker == p.gameObject && t != null && h.Target == t.M.gameObject) list.Add((Time.time - t0, h.Damage, h.IsFinisher)); };
            HitFeedbackService.OnHit += onHit;
            try { await cast(); await Wait(sec, ct); }
            finally { HitFeedbackService.OnHit -= onHit; }
            return list;
        }

        async UniTask<(Vector3 f, Vector3 r, Vector3 at)> Setup(string path, int enhance, int slot = PlayerWeaponManager.Slot0)
        {
            await EquipAsync(p, path, ct, slot);
            var wd = slot == PlayerWeaponManager.Slot0 ? p.WeaponManager.Weapon0Data : p.WeaponManager.CurrentWeaponData;
            if (wd != null) { wd.enhanceLevel = enhance; wd.RecomputeEnhancedStats(); }
            await Wait(1f, ct);
            Vector3 f = Flat(p.transform.forward);
            return (f, Vector3.Cross(Vector3.up, f), p.transform.position);
        }

        // C1-1 무형일섬 돌진 무적
        {
            var (f, r, at) = await Setup(Nameless, 0);
            s_home = at; HideStrays();
            await AimSettle(at + f * 6f);
            p.CooldownTracker.ResetCooldown(SkillType.R);
            p.InputBuffer.Push(Command.RSkill);
            await UniTask.DelayFrame(3, cancellationToken: ct);
            float left = invEnd != null ? (float)invEnd.GetValue(p) - Time.time : -1f;
            Check(sb, $"C1 무형일섬 돌진 무적 — 시전 직후 남은 무적 {left:0.00}초", left > 0.05f);
            await WaitEnd(InSkill, ct);
        }

        // C1-2 2단계(+3) 연참 7개가 모션 안에(마지막 ≤ 0.97초)
        {
            var (f, r, at) = await Setup(Nameless, 3);
            s_home = at;
            var t = await TrackAsync(at + f * 4f, ct);
            HideStrays();
            await AimSettle(at + f * 6f);
            var hits = await Hits(t, async () => { p.CooldownTracker.ResetCooldown(SkillType.R); p.InputBuffer.Push(Command.RSkill); await UniTask.Yield(); }, 1.6f);
            float last = hits.Count > 0 ? hits[hits.Count - 1].t : -1f;
            Check(sb, $"C1 무형일섬 2단계 — 적중 {hits.Count} · 마지막 {last:0.00}초(모션 끝 ≈ 0.95) · 시각 [{string.Join(" ", hits.ConvertAll(h => h.t.ToString("0.00")))}]", hits.Count >= 7 && last <= 0.97f);
            await WaitEnd(InSkill, ct); DetachAll();
        }

        // C1-3 3단계(+6) 마지막 일섬 = 지나온 길 전체 — 길 옆 2.6 m 표적(감지 2 m 밖 · 길 폭 3 m 안)
        {
            var (f, r, at) = await Setup(Nameless, 6);
            s_home = at;
            var t = await TrackAsync(at + f * 4f + r * 2.6f, ct);
            HideStrays();
            await AimSettle(at + f * 6f);
            var hits = await Hits(t, async () => { p.CooldownTracker.ResetCooldown(SkillType.R); p.InputBuffer.Push(Command.RSkill); await UniTask.Yield(); }, 1.6f);
            bool fin = hits.Exists(h => h.fin);
            Check(sb, $"C1 무형일섬 3단계 길 가르기 — 길 옆 표적 적중 {hits.Count} · 마무리 {fin}", hits.Count == 1 && fin);
            await WaitEnd(InSkill, ct); DetachAll();
        }

        // C3 주변경계 3단계(+6) 확산 검기 — 시전 3 + 확산 1 · 마지막이 마무리
        {
            var (f, r, at) = await Setup(Great, 6);
            s_home = at;
            var t = await TrackAsync(at + f * 2f, ct);
            HideStrays();
            var hits = await Hits(t, async () => { p.CooldownTracker.ResetCooldown(SkillType.E); p.InputBuffer.Push(Command.ESkill); await UniTask.Yield(); }, 4f);
            int fins = hits.FindAll(h => h.fin).Count;
            sb.AppendLine($"   C3 시각 [{string.Join(" ", hits.ConvertAll(h => h.t.ToString("0.00") + (h.fin ? "!" : "")))}]");
            Check(sb, $"C3 주변경계 3단계 — 적중 {hits.Count}(시전 3 + 확산 1) · 마무리 {fins}개(마지막 {(hits.Count > 0 && hits[hits.Count - 1].fin)})", hits.Count == 4 && fins == 1 && hits[hits.Count - 1].fin);
            await WaitEnd(InSkill, ct); DetachAll();
        }

        // A5 보강 주변경계 1단계 — 한 번 베기가 마무리
        {
            var (f, r, at) = await Setup(Great, 0);
            s_home = at;
            var t = await TrackAsync(at + f * 2f, ct);
            HideStrays();
            var hits = await Hits(t, async () => { p.CooldownTracker.ResetCooldown(SkillType.E); p.InputBuffer.Push(Command.ESkill); await UniTask.Yield(); }, 1.2f);
            Check(sb, $"A5 주변경계 1단계 마무리 — 적중 {hits.Count} · 마무리 {(hits.Count > 0 && hits[0].fin)}", hits.Count == 1 && hits[0].fin);
            await WaitEnd(InSkill, ct); DetachAll();
        }

        // C4 궁수의 집중 — R을 누르고 있다가 0.45초에 떼기 vs 끝까지(1.2초) 누르기
        {
            var (f, r, at) = await Setup(Bow, 0, PlayerWeaponManager.Slot1);
            s_home = at;
            var t = await TrackAsync(at + f * 5f, ct);
            HideStrays();
            await AimSettle(t != null ? t.M.transform.position : at + f * 5f);
            var kb = Keyboard.current;
            async UniTask HoldR(float sec)
            {
                p.CooldownTracker.ResetCooldown(SkillType.R);
                InputSystem.QueueStateEvent(kb, new KeyboardState(Key.R));
                await Wait(sec, ct);
                InputSystem.QueueStateEvent(kb, new KeyboardState());
            }
            var early = await Hits(t, () => HoldR(0.45f), 1.8f);
            await WaitEnd(InSkill, ct);
            if (t != null) t.Refill();
            var full = await Hits(t, () => HoldR(1.3f), 1.2f);
            float eT = early.Count > 0 ? early[0].t : -1f, fT = full.Count > 0 ? full[0].t : -1f;
            float eD = early.Count > 0 ? early[0].dmg : 0f, fD = full.Count > 0 ? full[0].dmg : 0f;
            Check(sb, $"C4 궁수의 집중 — 0.45초에 떼면 {eT:0.00}초에 피해 {eD:0} · 끝까지 누르면 {fT:0.00}초에 {fD:0}", eT > 0f && fT > 0f && eT < fT - 0.3f && eD < fD);
            await WaitEnd(InSkill, ct); DetachAll();
        }

        // C5 석궁 빨리쏘기 — 바로 끝나 움직일 수 있다
        {
            await Setup(Crossbow, 0, PlayerWeaponManager.Slot1);
            HideStrays();
            p.CooldownTracker.ResetCooldown(SkillType.E);
            float t0 = Time.time;
            p.InputBuffer.Push(Command.ESkill);
            await UniTask.DelayFrame(6, cancellationToken: ct);
            Check(sb, $"C5 석궁 빨리쏘기 — {Time.time - t0:0.00}초 뒤 스킬 중 {InSkill()} · 공속 {p.RuntimeStats.AttackSpeedMultiplier:0.00}", !InSkill() && p.RuntimeStats.AttackSpeedMultiplier > 1.2f);
        }
    }

    // ── 13 스킬 A7 트레일 · A9 HUD 신호 · A10 범위 고리(10-08) ──────────────
    private static async UniTask SkillVisAsync(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        Directory.CreateDirectory("Temp/ui_shots");
        var actField = typeof(PlayerController).GetField("actStateDebug", Inst);
        bool InSkill() => (actField?.GetValue(p)?.ToString() ?? "").Contains("Skill");
        bool HasGuide(string name)
        {
            foreach (var mr in UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
                if (mr.gameObject.activeInHierarchy && mr.name.StartsWith(name)) return true;
            return false;
        }
        await Wait(3f, ct);

        // A10 주변경계 둘레 원 — 누른 직후
        if (await EquipAsync(p, Great, ct))
        {
            await Wait(1f, ct);
            HideStrays();
            p.CooldownTracker.ResetCooldown(SkillType.E);
            p.InputBuffer.Push(Command.ESkill);
            await UniTask.DelayFrame(3, cancellationToken: ct);
            bool disc = HasGuide(SkillRangeRing.DiscName);
            if (Camera.main != null) CaptureMain(Camera.main, "Temp/ui_shots/WF_SkillVis_ring_guard.png");
            Check(sb, $"A10 주변경계 둘레 원 — 바닥 원 {disc} · 데칼 재질 {RelicFairy.Monster.PatternGuideHelper.CircleMaterial?.name ?? "없음"}", disc);
            await WaitEnd(InSkill, ct);
        }

        // A10 최후의 일격 반원 — 내려찍는 순간(0.6초)
        if (await EquipAsync(p, Great, ct))
        {
            await Wait(1f, ct);
            HideStrays();
            p.CooldownTracker.ResetCooldown(SkillType.R);
            p.InputBuffer.Push(Command.RSkill);
            float t0 = Time.time; bool sector = false;
            while (Time.time - t0 < 1f && !sector) { sector = HasGuide(SkillRangeRing.SectorName); await UniTask.Yield(PlayerLoopTiming.Update, ct); }
            if (Camera.main != null) CaptureMain(Camera.main, "Temp/ui_shots/WF_SkillVis_ring_final.png");
            Check(sb, $"A10 최후의 일격 반원 — {Time.time - t0:0.00}초에 바닥 반원 {sector}", sector);
            await WaitEnd(InSkill, ct);
        }

        // A7 환영베기(카타나 R) 동안 칼날 트레일 — 0.3초 화면
        if (await EquipAsync(p, Katana, ct))
        {
            await Wait(1f, ct);
            HideStrays();
            p.CooldownTracker.ResetCooldown(SkillType.R);
            p.InputBuffer.Push(Command.RSkill);
            await Wait(0.3f, ct);
            var cam = new GameObject("~SkillVisCam").AddComponent<Camera>();
            if (Camera.main != null) cam.CopyFrom(Camera.main);
            cam.enabled = false; cam.fieldOfView = 50f;
            PlaceBehind(cam, p, Flat(p.AimForward));
            CaptureMain(cam, "Temp/ui_shots/WF_SkillVis_trail_katanaR.png");
            UnityEngine.Object.Destroy(cam.gameObject);
            sb.AppendLine("   A7 환영베기 트레일 화면 → WF_SkillVis_trail_katanaR.png (눈으로 확인)");
            await WaitEnd(InSkill, ct);
        }

        // A9 HUD 신호 — 재사용 대기 중 누르면 거절 · 석궁 강화 남은 시간
        if (await EquipAsync(p, Crossbow, ct, PlayerWeaponManager.Slot1))
        {
            await Wait(1f, ct);
            int rejected = 0; float buffDur = -1f;
            Action<SkillType> onRej = _ => rejected++;
            Action<SkillType, float> onBuff = (_, d) => buffDur = d;
            p.SkillRejected += onRej; p.SkillBuffStarted += onBuff;
            try
            {
                p.CooldownTracker.ResetCooldown(SkillType.E);
                p.InputBuffer.Push(Command.ESkill);
                await Wait(0.8f, ct);
                p.InputBuffer.Push(Command.ESkill);   // 재사용 대기 중
                await Wait(0.3f, ct);
            }
            finally { p.SkillRejected -= onRej; p.SkillBuffStarted -= onBuff; }
            Check(sb, $"A9 신호 — 강화 지속 {buffDur:0.0}초 · 대기 중 거절 {rejected}회", buffDur > 0f && rejected == 1);
        }
    }

    // ── 12 스킬 조작감 A1~A5(10-08 · 기획 §5-A, 결정 S2 피해 뒤만 · S3 경직 면역만) ──────────
    // 7칸: 조준 보조 · 피해 전 회피(버퍼에 남고 끊지 않음) · 후딜 회피 캔슬 · 헛스윙 2 · 스탠스 · 마무리.
    private static async UniTask SkillFeelAsync(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        var actField = typeof(PlayerController).GetField("actStateDebug", Inst);
        string Act() => actField?.GetValue(p)?.ToString() ?? "";
        bool InSkill() => Act().Contains("Skill");
        await Wait(3f, ct);   // 입장 카메라가 자리 잡을 때까지
        var aimObj = typeof(PlayerController).GetField("_aim", Inst)?.GetValue(p) as PlayerAim;

        // 마우스가 실제로 world 쪽을 가리킬 때까지 다시 넣는다(1회차 A1 · A5: 넣은 조준이 아직 안 먹어 119° 빗나감)
        async UniTask<string> AimSettle(Vector3 world)
        {
            float err = 999f;
            for (int i = 0; i < 30; i++)
            {
                AimAt(world);
                await UniTask.DelayFrame(2, cancellationToken: ct);
                if (aimObj != null && aimObj.TryComputeLookDir(p.transform, out var look))
                {
                    err = Vector3.Angle(look, Flat(world - p.transform.position));
                    if (err < 3f) return $"마우스 맞춤 {err:0}°";
                }
            }
            return $"⚠️마우스 못 맞춤 {err:0}°";
        }

        async UniTask<float> CastAndTime(Command cmd, SkillType type)
        {
            p.CooldownTracker.ResetCooldown(type);
            float t0 = Time.time;
            p.InputBuffer.Push(cmd);
            while (!InSkill() && Time.time - t0 < 0.5f) await UniTask.Yield(PlayerLoopTiming.Update, ct);
            while (InSkill() && Time.time - t0 < 5f) await UniTask.Yield(PlayerLoopTiming.Update, ct);
            return Time.time - t0;
        }

        // A1 조준 보조 — 마우스는 정면, 적은 12° 옆 4 m → 성광베기가 적 쪽으로(오차 4° 안) · 입력 → 발동 프레임
        if (await EquipAsync(p, Katana, ct))
        {
            await Wait(1f, ct);
            Vector3 f = Flat(p.transform.forward), at = p.transform.position;
            s_home = at;
            Vector3 toT = Quaternion.AngleAxis(12f, Vector3.up) * f;
            var t = await TrackAsync(at + toT * 4f, ct);
            HideStrays();
            RequestFace(p, at + f * 3f);
            await Wait(0.3f, ct);
            string aimNote = await AimSettle(at + f * 6f);
            p.CooldownTracker.ResetCooldown(SkillType.E);
            int f0 = Time.frameCount;
            p.InputBuffer.Push(Command.ESkill);
            while (!InSkill() && Time.frameCount - f0 < 30) await UniTask.Yield(PlayerLoopTiming.Update, ct);
            int frames = Time.frameCount - f0;
            await Wait(0.1f, ct);
            float err = Vector3.Angle(Flat(p.AimForward), toT);
            Check(sb, $"A1 조준 보조 — 마우스 정면 · 적 12° 옆 → 스킬 정면과 적 사이 {err:0}° · 입력 → 발동 {frames}프레임 · {aimNote}", err < 4f);
            await WaitEnd(InSkill, ct);
            DetachAll();
        }

        // A2-1 피해 전(모으는 중) 회피 — 끊지 않는다(버퍼에 0.4초 남았다 사라짐)
        if (await EquipAsync(p, Great, ct))
        {
            await Wait(1f, ct);
            HideStrays();
            p.CooldownTracker.ResetCooldown(SkillType.R);
            p.InputBuffer.Push(Command.RSkill);
            await Wait(0.2f, ct);
            p.InputBuffer.Push(Command.Dodge);
            await Wait(0.2f, ct);
            bool still = InSkill();
            Check(sb, $"A2 피해 전 회피는 끊지 않는다 — 0.4초에 스킬 중 {still} · 이동 {p.LocoSM.CurrentId}", still && p.LocoSM.CurrentId != LocoState.Dodge);
            await WaitEnd(InSkill, ct);
        }

        // A2-2 후딜 회피 캔슬 — 내려찍은(0.6초) 뒤 회피 → 바로 회피 · 스킬 끝
        if (await EquipAsync(p, Great, ct))
        {
            await Wait(1f, ct);
            HideStrays();
            p.CooldownTracker.ResetCooldown(SkillType.R);
            float t0 = Time.time;
            p.InputBuffer.Push(Command.RSkill);
            await Wait(0.68f, ct);
            p.InputBuffer.Push(Command.Dodge);
            float tw = Time.time;
            while (p.LocoSM.CurrentId != LocoState.Dodge && Time.time - tw < 0.3f) await UniTask.Yield(PlayerLoopTiming.Update, ct);
            float react = Time.time - tw;
            Check(sb, $"A2 후딜 회피 캔슬 — 누른 뒤 {react:0.00}초에 회피 · 스킬 중 {InSkill()} · 시전부터 {Time.time - t0:0.00}초", p.LocoSM.CurrentId == LocoState.Dodge && !InSkill() && react < 0.1f);
            await Wait(0.8f, ct);
        }

        // A3 헛스윙 — 무형일섬(아무도 없음): 연참을 접고 바로 끝(예전 ≈ 1.2초)
        if (await EquipAsync(p, Nameless, ct))
        {
            await Wait(1f, ct);
            HideStrays();
            float len = await CastAndTime(Command.RSkill, SkillType.R);
            Check(sb, $"A3 무형일섬 헛스윙 길이 {len:0.00}초 (돌진 0.15 + 끝 0.3 — 예전 ≈ 1.2)", len < 0.7f);
            await Wait(0.5f, ct);
        }

        // A3 헛스윙 — 최후의 일격 2단계(+3, 아무도 없음): 추가타 대기 없이 끝(예전 ≈ 1.7초)
        if (await EquipAsync(p, Great, ct))
        {
            p.WeaponManager.Weapon0Data.enhanceLevel = 3;
            p.WeaponManager.Weapon0Data.RecomputeEnhancedStats();
            await Wait(1f, ct);
            HideStrays();
            float len = await CastAndTime(Command.RSkill, SkillType.R);
            Check(sb, $"A3 최후의 일격 2단계 헛스윙 길이 {len:0.00}초 (모음 0.6 + 끝 0.3 — 예전 ≈ 1.7)", len < 1.05f);
            p.WeaponManager.Weapon0Data.enhanceLevel = 0;
            p.WeaponManager.Weapon0Data.RecomputeEnhancedStats();
            await Wait(0.5f, ct);
        }

        // A4 스탠스 — 모으는 중 강타(확정 날아감)를 맞아도 끊기지 않는다(피해는 들어간다)
        if (await EquipAsync(p, Great, ct))
        {
            await Wait(1f, ct);
            HideStrays();
            var inv = typeof(PlayerController).GetField("debugInvincible", Inst);
            p.CooldownTracker.ResetCooldown(SkillType.R);
            p.InputBuffer.Push(Command.RSkill);
            await Wait(0.2f, ct);
            int hp0 = p.RuntimeStats.Hp;
            inv?.SetValue(p, false);
            p.TakeDamage(5, null, ignorePoise: true, HitWeight.Heavy);
            inv?.SetValue(p, true);
            await Wait(0.2f, ct);
            Check(sb, $"A4 스탠스 — 모으는 중 강타 뒤 스킬 중 {InSkill()} · 이동 {p.LocoSM.CurrentId} · 체력 {hp0} → {p.RuntimeStats.Hp}", InSkill() && p.LocoSM.CurrentId != LocoState.Launched && p.RuntimeStats.Hp < hp0);
            await WaitEnd(InSkill, ct);
        }

        // A5 마무리 — 성광베기 1단계 돌진 타가 마무리(isFinisher)
        if (await EquipAsync(p, Katana, ct))
        {
            await Wait(1f, ct);
            Vector3 f = Flat(p.transform.forward), at = p.transform.position;
            s_home = at;
            var t = await TrackAsync(at + f * 3f, ct);
            HideStrays();
            bool fin = false; int hits = 0;
            Action<HitInfo> onHit = h => { if (h.Attacker == p.gameObject && t != null && h.Target == t.M.gameObject) { hits++; fin |= h.IsFinisher; } };
            HitFeedbackService.OnHit += onHit;
            try
            {
                RequestFace(p, at + f * 3f);
                await Wait(0.3f, ct);
                sb.AppendLine("   A5 " + await AimSettle(at + f * 6f));
                await CastAndTime(Command.ESkill, SkillType.E);
            }
            finally { HitFeedbackService.OnHit -= onHit; }
            Check(sb, $"A5 성광베기 1단계 마무리 — 적중 {hits} · 마무리 {fin}", hits > 0 && fin);
            DetachAll();
        }
    }

    private static async UniTask WaitEnd(Func<bool> inSkill, CancellationToken ct)
    {
        float t0 = Time.realtimeSinceStartup;
        while (inSkill() && Time.realtimeSinceStartup - t0 < 5f) await UniTask.Yield(PlayerLoopTiming.Update, ct);
        await Wait(0.4f, ct);
    }

    // ── 11 스킬 버그 B1~B7(10-08 · 기획 장비스킬_조작감개선 §5-B) ──────────────
    // 7칸: 방향 3(성광베기 돌진 · 최후의 일격 놓을 때 · 궁수의 집중 화살) · 석궁 공속 누수 · 최후의 일격 3단계 추가타 · 끝 대기 · 주변경계 첫 판정.
    private const string Bow      = "Assets/RelicFairy/Weapon/Bow/Data/T1_Bow.asset";
    private const string Crossbow = "Assets/RelicFairy/Weapon/Crossbow/Data/T1_Crossbow.asset";

    private static async UniTask SkillBugsAsync(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        var actField = typeof(PlayerController).GetField("actStateDebug", Inst);
        bool InSkill() => (actField?.GetValue(p)?.ToString() ?? "").Contains("Skill");   // 열거형 — as string이면 늘 null이었다

        // 진단: 지금부터 sec초 동안 행동 상태가 바뀔 때마다 · 기준 방향 대비 AimForward · 마우스 방향(°)
        var aimObj = typeof(PlayerController).GetField("_aim", Inst)?.GetValue(p) as PlayerAim;
        async UniTask Trace(string label, Vector3 refDir, float sec)
        {
            var tr = new StringBuilder($"   진단 {label}:");
            string last = null;
            float t0 = Time.time;
            while (Time.time - t0 < sec)
            {
                string a = actField?.GetValue(p)?.ToString() ?? "?";
                if (a != last)
                {
                    float md = aimObj != null && aimObj.TryComputeLookDir(p.transform, out var look) ? Vector3.SignedAngle(refDir, look, Vector3.up) : float.NaN;
                    tr.Append($" <{Time.time - t0:0.00} {a} 정면 {Vector3.SignedAngle(refDir, p.AimForward, Vector3.up):0}° 마우스 {md:0}°>");
                    last = a;
                }
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            sb.AppendLine(tr.ToString());
        }

        // 정면 f로 세우고 마우스는 aimAt — 스킬은 누른 순간의 마우스 쪽으로 돌아야 한다
        async UniTask Face(Vector3 f, Vector3 aimAt)
        {
            RequestFace(p, p.transform.position + f * 3f);
            await Wait(0.3f, ct);
            AimAt(aimAt);
            await UniTask.DelayFrame(3, cancellationToken: ct);
        }

        await Wait(3f, ct);   // 첫 전투방 입장 카메라가 자리 잡을 때까지 — 1회차 B1은 카메라가 움직이는 중이라 마우스가 79° 빗나갔다

        // B1 성광베기 돌진 = 겨눈 쪽
        if (await EquipAsync(p, Katana, ct))
        {
            await Wait(1f, ct);
            HideStrays();
            Vector3 f = Flat(p.transform.forward), r = Vector3.Cross(Vector3.up, f), at = p.transform.position;
            await Face(f, at + r * 4f);
            p.CooldownTracker.ResetCooldown(SkillType.E);
            p.InputBuffer.Push(Command.ESkill);
            await Trace("B1(기준 = 겨눈 오른쪽)", r, 0.6f);
            Vector3 d = Flat(p.transform.position - at);
            float ang = Vector3.Angle(d, r);
            Check(sb, $"B1 성광베기 돌진이 겨눈 쪽(오른쪽 90°)으로 — 어긋남 {ang:0}° · 이동 {(p.transform.position - at).magnitude:0.0} m", ang < 15f && (p.transform.position - at).magnitude > 1f);
            await WaitSkillEnd(InSkill, ct);
        }

        // B2 최후의 일격 — 모으는 동안 마우스를 옮기면 놓을 때 그쪽을 벤다(뒤쪽 비스듬한 표적: 옛 정면이면 반원 밖)
        if (await EquipAsync(p, Great, ct))
        {
            await Wait(1f, ct);
            Vector3 f = Flat(p.transform.forward), r = Vector3.Cross(Vector3.up, f), at = p.transform.position;
            s_home = at;
            var t = await TrackAsync(at + (r - f).normalized * 2f, ct);
            HideStrays();
            await Face(f, at + f * 4f);
            p.CooldownTracker.ResetCooldown(SkillType.R);
            p.InputBuffer.Push(Command.RSkill);
            await Wait(0.3f, ct);
            AimAt(at + (r - f).normalized * 4f);   // 모으는 중 겨눔을 오른쪽 뒤로
            await Trace("B2(기준 = 오른쪽 뒤)", (r - f).normalized, 1.2f);
            Check(sb, $"B2 최후의 일격 놓을 때 다시 겨눔 — 오른쪽 뒤 표적 피해 {(t != null ? t.Dealt : 0):0}", t != null && t.Dealt > 0f);
            await WaitSkillEnd(InSkill, ct);
            DetachAll();
        }

        // B6 최후의 일격 1단계 끝 대기 한 번 — 모음 0.6 + 끝 0.3 ≈ 0.9초(예전 1.2초)
        if (await EquipAsync(p, Great, ct))
        {
            await Wait(1f, ct);
            HideStrays();
            p.CooldownTracker.ResetCooldown(SkillType.R);
            float t0 = Time.time;
            p.InputBuffer.Push(Command.RSkill);
            while (!InSkill() && Time.time - t0 < 0.5f) await UniTask.Yield(PlayerLoopTiming.Update, ct);
            while (InSkill() && Time.time - t0 < 5f) await UniTask.Yield(PlayerLoopTiming.Update, ct);
            float len = Time.time - t0;
            Check(sb, $"B6 최후의 일격 1단계 길이 {len:0.00}초 (모음 0.6 + 끝 0.3 — 예전 1.2)", len < 1.05f);
        }

        // B5 최후의 일격 3단계 추가타 = 공격력 기준(예전 플레이어 최대 체력 10%)
        if (await EquipAsync(p, Great, ct))
        {
            p.WeaponManager.Weapon0Data.enhanceLevel = 6;   // 3단계(SkillTierResolver 근접 +6)
            p.WeaponManager.Weapon0Data.RecomputeEnhancedStats();
            await Wait(1f, ct);
            Vector3 f = Flat(p.transform.forward), at = p.transform.position;
            s_home = at;
            var t = await TrackAsync(at + f * 2f, ct);
            HideStrays();
            var dmgs = new List<float>();
            Action<HitInfo> onHit = h => { if (h.Attacker == p.gameObject && t != null && h.Target == t.M.gameObject) dmgs.Add(h.Damage); };
            HitFeedbackService.OnHit += onHit;
            try
            {
                await Face(f, at + f * 4f);
                p.CooldownTracker.ResetCooldown(SkillType.R);
                p.InputBuffer.Push(Command.RSkill);
                await Wait(2.2f, ct);
            }
            finally { HitFeedbackService.OnHit -= onHit; }
            string list = string.Join(" · ", dmgs.ConvertAll(x => x.ToString("0")));
            Check(sb, $"B5 최후의 일격 3단계 — 본타 → 추가타 피해 [{list}] · 최대 체력 {p.RuntimeStats.MaxHp} (추가타 ≥ 45 = 공격력 기준)", dmgs.Count >= 2 && dmgs[dmgs.Count - 1] >= 45f);
            p.WeaponManager.Weapon0Data.enhanceLevel = 0;
            p.WeaponManager.Weapon0Data.RecomputeEnhancedStats();
            await WaitSkillEnd(InSkill, ct);
            DetachAll();
        }

        // B7 주변경계 첫 판정 = 누른 뒤 휘두름이 닿을 때(약 0.15초) — 예전 누른 프레임
        if (await EquipAsync(p, Great, ct))
        {
            await Wait(1f, ct);
            Vector3 f = Flat(p.transform.forward), at = p.transform.position;
            s_home = at;
            var t = await TrackAsync(at + f * 1.5f, ct);
            HideStrays();
            float first = -1f, t0 = 0f;
            Action<HitInfo> onHit = h => { if (first < 0f && h.Attacker == p.gameObject && t != null && h.Target == t.M.gameObject) first = Time.time - t0; };
            HitFeedbackService.OnHit += onHit;
            try
            {
                p.CooldownTracker.ResetCooldown(SkillType.E);
                t0 = Time.time;
                p.InputBuffer.Push(Command.ESkill);
                await Wait(1.2f, ct);
            }
            finally { HitFeedbackService.OnHit -= onHit; }
            Check(sb, $"B7 주변경계 첫 판정 {first:0.00}초 뒤 (0.1~0.3 — 예전 누른 프레임)", first >= 0.1f && first <= 0.3f);
            await WaitSkillEnd(InSkill, ct);
            DetachAll();
        }

        // B3 궁수의 집중 — 모으는 동안 마우스를 옮기면 그쪽으로 쏜다(원거리 칸 1)
        if (await EquipAsync(p, Bow, ct, PlayerWeaponManager.Slot1))
        {
            await Wait(1f, ct);
            Vector3 f = Flat(p.transform.forward), r = Vector3.Cross(Vector3.up, f), at = p.transform.position;
            s_home = at;
            var t = await TrackAsync(at + r * 5f, ct);
            HideStrays();
            await Face(f, at + f * 6f);
            p.CooldownTracker.ResetCooldown(SkillType.R);
            p.InputBuffer.Push(Command.RSkill);
            await Wait(0.4f, ct);
            AimAt(t != null ? t.M.transform.position : at + r * 5f);   // 모으는 중 겨눔을 오른쪽 표적으로
            await Trace("B3(기준 = 오른쪽)", r, 2.2f);
            Check(sb, $"B3 궁수의 집중 화살이 마지막 겨눔으로 — 오른쪽 표적 피해 {(t != null ? t.Dealt : 0):0}", t != null && t.Dealt > 0f);
            await WaitSkillEnd(InSkill, ct);
            DetachAll();
        }

        // B4 석궁 빨리쏘기 — 두 번 걸어도 끝나면 원래 공격 속도로(예전엔 시전마다 영구히 쌓였다)
        if (await EquipAsync(p, Crossbow, ct, PlayerWeaponManager.Slot1))
        {
            await Wait(1f, ct);
            HideStrays();
            float base0 = p.RuntimeStats.AttackSpeedMultiplier;
            for (int i = 0; i < 2; i++)
            {
                p.CooldownTracker.ResetCooldown(SkillType.E);
                p.InputBuffer.Push(Command.ESkill);
                await Wait(0.8f, ct);
            }
            float during = p.RuntimeStats.AttackSpeedMultiplier;
            await Wait(5.5f, ct);
            float after = p.RuntimeStats.AttackSpeedMultiplier;
            Check(sb, $"B4 석궁 빨리쏘기 ×2 — 공속 {base0:0.00} → 버프 중 {during:0.00}(+0.6 기대) → 끝 {after:0.00}", Mathf.Abs(during - (base0 + 0.6f)) < 0.02f && Mathf.Abs(after - base0) < 0.01f);
        }
    }

    private static async UniTask WaitSkillEnd(Func<bool> inSkill, CancellationToken ct)
    {
        float t0 = Time.realtimeSinceStartup;
        while (inSkill() && Time.realtimeSinceStartup - t0 < 5f) await UniTask.Yield(PlayerLoopTiming.Update, ct);
        await Wait(0.3f, ct);
    }

    // ── 10 스킬 피격 이펙트(10-08) ──────────────────────────────
    // 근접 스킬 적중마다 스킬이 띄우는 SwordHitImpact에 공용 히트 VFX(HitEffect_02)가 한 번 더 떠 한 타에 둘이었다 → skipHitVfx.
    // 스킬마다 표적 1마리에 쓰고 3.5초 동안 새로 켜진 이펙트 인스턴스(풀 이름 = 주소)를 센다. 환영베기 검기 투사체는 공용 이펙트가 정상.
    private static async UniTask SkillFxAsync(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        var cases = new (string path, Command cmd, SkillType type, string label)[]
        {
            (Katana,   Command.ESkill, SkillType.E, "카타나 E 섬광베기"),
            (Katana,   Command.RSkill, SkillType.R, "카타나 R 환영베기"),
            (Great,    Command.ESkill, SkillType.E, "대검 E 주변경계"),
            (Great,    Command.RSkill, SkillType.R, "대검 R 최후의 일격"),
            (Nameless, Command.RSkill, SkillType.R, "무형검 R 무형일섬"),
        };
        foreach (var c in cases)
        {
            if (!await EquipAsync(p, c.path, ct)) { sb.AppendLine($"장착 실패 {c.label}"); continue; }
            await Wait(1f, ct);
            s_home = p.transform.position;
            Vector3 f = Flat(p.transform.forward);
            var t = await TrackAsync(p.transform.position + f * 2f, ct);
            if (t == null) { sb.AppendLine($"{c.label}: 표적 실패"); continue; }
            HideStrays();
            p.CooldownTracker.ResetCooldown(c.type);
            int hits = 0, fallback = 0, own = 0;
            Action<HitInfo> onHit = h => { if (h.Attacker == p.gameObject) hits++; };
            HitFeedbackService.OnHit += onHit;
            var prev = new HashSet<int>();
            var cur  = new HashSet<int>();
            try
            {
                Steer(p, t.M.transform.position);
                p.InputBuffer.Push(c.cmd);
                float t0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - t0 < 3.5f)
                {
                    RefillAll();
                    cur.Clear();
                    foreach (var tr in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
                    {
                        bool fb = tr.name == "HitEffect_02", ow = tr.name == "SwordHitImpact";
                        if (!fb && !ow) continue;
                        int id = tr.GetInstanceID();
                        cur.Add(id);
                        if (prev.Contains(id)) continue;   // 이미 켜져 있던 것
                        if (fb) fallback++; else own++;
                    }
                    (prev, cur) = (cur, prev);
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                }
            }
            finally { HitFeedbackService.OnHit -= onHit; }
            sb.AppendLine($"{c.label}: 적중 {hits} · 공용 HitEffect_02 {fallback} · 스킬 SwordHitImpact {own} · 표적 피해 {t.Dealt:0}");
            DetachAll();
            await Wait(0.5f, ct);
        }
    }

    // ── 1 데이터 · 진화 · 단련 ──────────────────────────────────
    private static async UniTask DataAsync(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        var wm  = p.WeaponManager;
        var run = GameRunBootstrapper.Instance.Run;
        if (!await EquipAsync(p, Nameless, ct)) { sb.AppendLine("무형검 장착 실패"); return; }
        var w0 = wm.Weapon0Data;
        Check(sb, "무형검 형 없음", !w0.HasForm && w0.formTier == 0);

        w0.enhanceLevel = 5;
        WeaponEvolutionSO.Branch katana = null;
        foreach (var b in w0.evolution.Branches) if (b.branchId == "katana") katana = b;
        Check(sb, "+5에서 카타나 갈래 열림(D6)", katana != null && WeaponEvolutionSO.IsUnlocked(katana, 5));
        bool evolved = await wm.EvolveCurrentWeaponAsync(katana, ct);
        var w1 = wm.Weapon0Data;
        Check(sb, "진화 성공", evolved);
        Check(sb, "진화하면 형이 깨어난다(단계 1 · 칼바람)", w1.HasForm && w1.formTier == 1 && w1.form.DisplayName == "칼바람");

        run.FuelBank.Add(FuelKind.EnhanceMaterial, 40);
        int fuel0 = run.FuelBank.EnhanceMaterial;
        Check(sb, "다음 단련 = Ⅱ · 12", WeaponFormService.CanTemper(w1, out int t2, out int c2) && t2 == 2 && c2 == 12);
        Check(sb, "Ⅲ 변주로 Ⅱ 단련 거절", !WeaponFormService.TryTemper(w1, run.FuelBank, WindbladeForm.StillWind));
        Check(sb, "Ⅱ 쌍풍 단련", WeaponFormService.TryTemper(w1, run.FuelBank, WindbladeForm.TwinWind) && w1.formTier == 2 && w1.HasFormVariant(WindbladeForm.TwinWind));
        Check(sb, "강화재료 −12", run.FuelBank.EnhanceMaterial == fuel0 - 12);
        Check(sb, "Ⅲ 폭풍의 눈 단련", WeaponFormService.TryTemper(w1, run.FuelBank, WindbladeForm.StormEye) && w1.formTier == 3);
        Check(sb, "Ⅲ 뒤 단련 없음", !WeaponFormService.CanTemper(w1, out _, out _));

        var d = new RunSaveData();
        typeof(RunProgressManager).GetMethod("CaptureWeaponEnhance", BindingFlags.Static | BindingFlags.NonPublic)
            ?.Invoke(null, new object[] { d, run });
        Check(sb, "세이브 저장(단계 3 · twin_wind,storm_eye)", d.weapon0FormTier == 3 && d.weapon0FormVariants == "twin_wind,storm_eye");
        d.weapon0FormTier = 9;
        SaveSanitizer.Sanitize(d);
        Check(sb, "Sanitize 9 → 3", d.weapon0FormTier == 3);

        // Review Focus #2 — 원거리를 든 채 진화해도 근접 칸이 진화한다(UI_CruciblePanel.EvolveAsync와 같은 순서)
        if (!await EquipAsync(p, Nameless, ct)) { sb.AppendLine("무형검 재장착 실패"); return; }
        wm.Weapon0Data.enhanceLevel = 5;
        if (wm.Weapon1Data == null)   // 테스트 허브는 2번 칸이 비어 있다 — 비면 「원거리 그대로」가 null == null로 거짓 통과한다(실측 1 2회차)
        {
            var bow = AssetDatabase.LoadAssetAtPath<WeaponSO>("Assets/RelicFairy/Weapon/Bow/Data/T1_Bow.asset");
            if (bow != null) await wm.AcquireWeaponToSlotAsync(WeaponData.FromSO(bow), PlayerWeaponManager.Slot1, setActive: false, playAppear: false);
        }
        var rangedBefore = wm.Weapon1Data;
        await wm.SwitchToSlotAsync(PlayerWeaponManager.Slot1);
        Check(sb, "원거리를 든 상태", wm.CurrentSlotIndex == PlayerWeaponManager.Slot1);
        if (wm.CurrentSlotIndex != PlayerWeaponManager.Slot0) await wm.SwitchToSlotAsync(PlayerWeaponManager.Slot0);
        WeaponEvolutionSO.Branch great = null;
        foreach (var b in wm.Weapon0Data.evolution.Branches) if (b.branchId == "greatsword") great = b;
        bool ok2 = await wm.EvolveCurrentWeaponAsync(great, ct);
        Check(sb, "근접 칸이 대검으로 진화 · 원거리 그대로",
              ok2 && wm.Weapon0Data.form?.DisplayName == "무게" && rangedBefore != null && ReferenceEquals(wm.Weapon1Data, rangedBefore));
    }

    // ── 36 형 발광 — 무기 전환 뒤(10-12 B11) ─────────────────────
    // 슬롯 전환은 디졸브(PlayAppear)가 재질을 먼저 바꾼 뒤 OnWeaponChanged가 온다. 형 가득(카타나)으로 두고
    // 활 ↔ 카타나 전환 3번 — 디졸브가 끝난 뒤 칼날 재질 칸마다 형 색이 실제로 칠해져 있는가(속성 블록 색 · URP Lit은 _EMISSION 켜짐).
    private static readonly int GlowToonId = Shader.PropertyToID("_Emissive_Color");
    private static readonly int GlowLitId  = Shader.PropertyToID("_EmissionColor");

    private static (int lit, int all) FormGlowCount(PlayerController p)
    {
        int lit = 0, all = 0;
        var inst = p.WeaponManager.CurrentWeaponInstance;
        if (inst == null) return (0, 0);
        var block = new MaterialPropertyBlock();
        foreach (var rd in inst.GetComponentsInChildren<Renderer>(true))
        {
            if (rd is ParticleSystemRenderer || rd is TrailRenderer || rd is LineRenderer) continue;
            var mats = rd.sharedMaterials;
            for (int m = 0; m < mats.Length; m++)
            {
                var mat = mats[m];
                if (mat == null) continue;
                int prop = mat.HasProperty(GlowToonId) ? GlowToonId : mat.HasProperty(GlowLitId) ? GlowLitId : 0;
                if (prop == 0) continue;
                all++;
                rd.GetPropertyBlock(block, m);
                // 형이 더한 몫만 — 재질 기본 발광(룬 등)을 빼고 본다(10-12 「전」 실측: 쌓임 0인데도 기본 발광 때문에 켜짐으로 셌다)
                Color add = block.GetColor(prop) - mat.GetColor(prop);
                if (Mathf.Max(add.r, add.g, add.b) > 0.05f && (prop == GlowToonId || mat.IsKeywordEnabled("_EMISSION"))) lit++;
            }
        }
        return (lit, all);
    }

    private static async UniTask FormGlowAsync(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        var wm = p.WeaponManager;
        if (!await EquipAsync(p, Katana, ct)) { sb.AppendLine("카타나 장착 실패"); return; }
        if (wm.Weapon1Data == null)
        {
            var bow = AssetDatabase.LoadAssetAtPath<WeaponSO>(Bow);
            if (bow != null) await wm.AcquireWeaponToSlotAsync(WeaponData.FromSO(bow), PlayerWeaponManager.Slot1, setActive: false, playAppear: false);
        }
        var form = WeaponFormRuntime.Of(p)?.Current;
        if (form == null) { sb.AppendLine("형 없음"); return; }
        typeof(WeaponFormBase).GetMethod("SetStacks", Inst)?.Invoke(form, new object[] { form.MaxStacks, WeaponFormBase.StackReason.Gain });
        // 쌓임을 가득으로 붙든다 — 전환 사이 기다림에 쉬는 시간 감소가 와 0이 되면 발광을 못 본다(다시 칠하지 않게 SetStacks는 한 번만)
        var lastHit = form.GetType().GetField("_lastHitAt", Inst) ?? form.GetType().GetField("_lastSwingAt", Inst);
        lastHit?.SetValue(form, Time.time + 9999f);
        await Wait(0.3f, ct);
        var g0 = FormGlowCount(p);
        Check(sb, $"장착 직후(디졸브 없음) 형 발광 칸 {g0.lit}/{g0.all}", g0.all > 0 && g0.lit == g0.all);
        // 디졸브 ②(10-12) — 빠지는 무기는 0.3초 디졸브 뒤 꺼지고, 다시 들면 원본 재질이어야 한다(마젠타 0). 원본은 디졸브가 없을 때 잡는다
        var katana = wm.slots[PlayerWeaponManager.Slot0].instance;
        var katanaOrig = SnapMaterials(katana);
        Dictionary<Renderer, Material[]> bowOrig = null;
        for (int i = 1; i <= 4; i++)
        {
            bool fast = i == 4;   // 4번째 = 빠른 재전환(빠지는 디졸브가 도는 중에 다시 든다 — 등장이 퇴장을 되돌린다)
            await wm.SwitchToSlotAsync(PlayerWeaponManager.Slot1);
            await Wait(fast ? 0.1f : 0.6f, ct);
            bowOrig ??= SnapMaterials(wm.slots[PlayerWeaponManager.Slot1].instance);
            await wm.SwitchToSlotAsync(PlayerWeaponManager.Slot0);
            await Wait(1.2f, ct);
            var g = FormGlowCount(p);
            var bow = wm.slots[PlayerWeaponManager.Slot1].instance;
            string tag = fast ? "빠른 재전환(0.1초)" : $"활 → 카타나 전환 {i}회";
            Check(sb, $"{tag} 뒤(디졸브 끝) 형 발광 칸 {g.lit}/{g.all} (전: 0/n 기대 · 후: n/n · 쌓임 {form.Stacks}/{form.MaxStacks})", g.all > 0 && g.lit == g.all);
            Check(sb, $"{tag} 뒤 재질 — 카타나 원본 {SameMaterials(katana, katanaOrig)} · 활 꺼짐 {(bow != null && !bow.activeSelf)} · 활 원본 {SameMaterials(bow, bowOrig)} · 마젠타 {MagentaCount(katana) + MagentaCount(bow)}",
                  SameMaterials(katana, katanaOrig) && bow != null && !bow.activeSelf && SameMaterials(bow, bowOrig) && MagentaCount(katana) + MagentaCount(bow) == 0);
        }
        typeof(WeaponFormBase).GetMethod("SetStacks", Inst)?.Invoke(form, new object[] { 0, WeaponFormBase.StackReason.Gain });
    }

    private static Dictionary<Renderer, Material[]> SnapMaterials(GameObject go)
    {
        var d = new Dictionary<Renderer, Material[]>();
        if (go == null) return d;
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            if (!(r is ParticleSystemRenderer) && !(r is TrailRenderer) && !(r is LineRenderer)) d[r] = r.sharedMaterials;
        return d;
    }

    private static bool SameMaterials(GameObject go, Dictionary<Renderer, Material[]> orig)
    {
        if (go == null || orig == null) return false;
        foreach (var kv in orig)
        {
            if (kv.Key == null) continue;
            var now = kv.Key.sharedMaterials;
            if (now.Length != kv.Value.Length) return false;
            for (int i = 0; i < now.Length; i++) if (now[i] != kv.Value[i]) return false;
        }
        return true;
    }

    /// <summary>빈 재질 · 오류 셰이더 칸 수(마젠타로 그려지는 칸).</summary>
    private static int MagentaCount(GameObject go)
    {
        int n = 0;
        if (go == null) return 0;
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
            foreach (var m in r.sharedMaterials)
                if (m == null || m.shader == null || m.shader.name == "Hidden/InternalErrorShader" || !m.shader.isSupported) n++;
        }
        return n;
    }

    // ── 37 이펙트 = 판정 — 플레이어(10-13) ─────────────────────────
    // 판정 크기는 코드 값(10-13 전수표 · 4b 대조). 0이면 그 축은 비교하지 않는다.
    private static async UniTask EffectJudgmentPlayerAsync(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        sb.AppendLine("셈: EffectReachProbe(보이는 양 90% 도달 · 굴절층 뺌) — 판정 대비 0.8 ~ 1.25배면 맞음 · 앞 = 정면 투영 · 옆 = 정면에 수직 · 반경 = 수평 거리");
        int ok = 0, bad = 0;
        Vector3 F() { var f = p.transform.forward; f.y = 0f; return f.normalized; }
        void Row(string label, EffectReachProbe.Result r, float hitF, float hitL, float hitR, string hitText)
        {
            var parts = new List<string>();
            bool fine = r.Any;
            if (hitF > 0f) { parts.Add($"앞 {r.Forward:0.0}/{hitF:0.#} m({EffectReachProbe.Ratio(r.Forward, hitF)})"); fine &= EffectReachProbe.Ok(r.Forward, hitF); }
            if (hitL > 0f) { parts.Add($"옆 ±{r.Lateral:0.0}/{hitL:0.#}({EffectReachProbe.Ratio(r.Lateral, hitL)})"); fine &= EffectReachProbe.Ok(r.Lateral, hitL); }
            if (hitR > 0f) { parts.Add($"반경 {r.Radius:0.0}/{hitR:0.#} m({EffectReachProbe.Ratio(r.Radius, hitR)})"); fine &= EffectReachProbe.Ok(r.Radius, hitR); }
            if (hitF <= 0f && hitL <= 0f && hitR <= 0f) parts.Add($"앞 {r.Forward:0.0} · 옆 ±{r.Lateral:0.0} · 반경 {r.Radius:0.0}");
            sb.AppendLine($"  {(fine ? "맞음" : r.Any ? "어긋남" : "이펙트 없음")} — {label} · 판정 {hitText} · {string.Join(" · ", parts)} · 입자계 {r.Systems}({r.Names})");
            if (fine) ok++; else bad++;
        }
        async UniTask<EffectReachProbe.Result> Watch(Action play, Vector3 origin, Vector3 fwd, float sec)
        {
            var before = EffectReachProbe.Snapshot();
            play();
            return await EffectReachProbe.ObserveAsync(before, origin, fwd, sec, ct);
        }

        // ① 평타 — 슬래시 이펙트와 판정 상자(이펙트 루트의 ColliderInstance)를 같이 띄운다
        sb.AppendLine("\n■ 평타(EffectHandler.PlayEffect) — 판정 상자 앞 · 뒤 · 옆 vs 슬래시 이펙트");
        foreach (var (tag, path) in new[] { ("무형검", Nameless), ("카타나", Katana), ("대검", Great) })
        {
            if (!await EquipAsync(p, path, ct)) { sb.AppendLine($"  {tag} 장착 실패"); continue; }
            await Wait(1f, ct);
            int combos = Mathf.Max(1, p.WeaponManager.CurrentWeaponData.groundEndCount);
            for (int c = 0; c < combos; c++)
                for (int step = 0; step < 4; step++)
                {
                    var colBefore = new HashSet<int>();
                    foreach (var ci in UnityEngine.Object.FindObjectsByType<ColliderInstance>(FindObjectsSortMode.None)) colBefore.Add(ci.GetInstanceID());
                    Vector3 o = p.transform.position, f = F();
                    var r = await Watch(() => p.EffectHandler.PlayEffect(WeaponActionType.GroundLight, c, step, new AbilityExecution()).Forget(), o, f, 0.6f);
                    if (!r.Any) continue;
                    // 판정 상자 — 게임과 같은 셈(상자 = 회전 반영 OBB · 그 밖 = AABB)
                    float hf = 0f, hb = 0f, hl = 0f; string kind = "없음";
                    foreach (var ci in UnityEngine.Object.FindObjectsByType<ColliderInstance>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    {
                        if (colBefore.Contains(ci.GetInstanceID()) || !ci.TryGetComponent(out Collider col)) continue;
                        var corners = new List<Vector3>(8);
                        if (col is BoxCollider box)
                        {
                            kind = "상자(OBB)";
                            for (int i = 0; i < 8; i++)
                                corners.Add(box.transform.TransformPoint(box.center + Vector3.Scale(box.size * 0.5f, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
                        }
                        else
                        {
                            kind = col is SphereCollider ? "구 → AABB 정육면체" : "AABB";
                            var b = col.bounds;
                            for (int i = 0; i < 8; i++)
                                corners.Add(b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                        }
                        Vector3 right = Vector3.Cross(Vector3.up, f);
                        foreach (var k in corners)
                        {
                            Vector3 d = k - o; d.y = 0f;
                            hf = Mathf.Max(hf, Vector3.Dot(d, f));
                            hb = Mathf.Min(hb, Vector3.Dot(d, f));
                            hl = Mathf.Max(hl, Mathf.Abs(Vector3.Dot(d, right)));
                        }
                    }
                    Row($"{tag} 콤보 {c + 1} · 이벤트 {step}", r, hf, hl, 0f, $"{kind} 앞 {hf:0.0} · 뒤 {-hb:0.0} · 옆 ±{hl:0.0}");
                    await Wait(0.4f, ct);
                }
        }

        // ② 스킬 — 3단계(+6)에서 실제 시전. 표적 3 m 앞(붙듦 · 체력 큼)
        sb.AppendLine("\n■ 스킬(3단계 · 실제 시전) — 표적 3 m 앞");
        var target = await SpawnAsync(p.transform.position + F() * 3f, ct);
        async UniTask Skill(string tag, string path, SkillType slot, Command cmd, bool atTarget, float hitF, float hitL, float hitR, string hitText, int enhance = 6)
        {
            if (!await EquipAsync(p, path, ct)) { sb.AppendLine($"  {tag} 장착 실패"); return; }
            var wd = p.WeaponManager.Weapon0Data;
            if (wd != null) { wd.enhanceLevel = enhance; wd.RecomputeEnhancedStats(); }
            await Wait(0.8f, ct);
            if (target != null) { target.transform.position = p.transform.position + F() * 3f; SetHp(target, TankHp); }
            Vector3 o = atTarget && target != null ? target.transform.position : p.transform.position, f = F();
            var r = await Watch(() => { p.CooldownTracker.ResetCooldown(slot); p.InputBuffer.Push(cmd); }, o, f, 2.2f);
            Row($"{tag}", r, hitF, hitL, hitR, hitText);
            await Wait(1.2f, ct);
        }
        await Skill("무형검 E 무형참 3단계 착탄 폭발(표적 둘레)", Nameless, SkillType.E, Command.ESkill, true, 0f, 0f, 2f, "원 2 m");
        await Skill("카타나 E 성광베기 3단계 폭발(표적 둘레)", Katana, SkillType.E, Command.ESkill, true, 0f, 0f, 3f, "적마다 원 3 m");
        await Skill("카타나 R 환영베기 3단계(플레이어 둘레)", Katana, SkillType.R, Command.RSkill, false, 0f, 0f, 3f, "원 3 m(마무리)");
        await Skill("대검 E 주변경계 1단계(플레이어 둘레)", Great, SkillType.E, Command.ESkill, false, 0f, 0f, 3.5f, "원 3.5 m", 0);
        await Skill("대검 E 주변경계 3단계 파동(플레이어 둘레)", Great, SkillType.E, Command.ESkill, false, 0f, 0f, 6.5f, "파동 원 6.5 m(마지막)");
        await Skill("대검 R 최후의 일격 1단계(앞 반원)", Great, SkillType.R, Command.RSkill, false, 3f, 3f, 0f, "반원 3 m(±90°)", 0);

        // ③ 형 · 유물 · 룬 · 전설 — 게임과 같은 재생 함수 · 같은 인자
        sb.AppendLine("\n■ 형 · 유물 · 룬 · 전설(같은 재생 함수 · 같은 인자)");
        {
            Vector3 o = p.transform.position, f = F();
            Row("형 풍절(칼바람)", await Watch(() => FormStrike.PlayVfx("vfx_form_windcut", o + Vector3.up, f, 1.3f), o, f, 1.2f), 8f, 1.3f, 0f, "직선 8 m · 반폭 0.9 + 0.4");
            Vector3 slam = o + f * 1.5f;
            Row("형 내려찍기(무게)", await Watch(() => FormStrike.PlayVfx("NovaOrangeHit", slam, f, 3f), slam, f, 1.2f), 0f, 0f, 3.5f, "원 3.5 m");
            Row("형 여진", await Watch(() => FormStrike.PlayVfx("NovaOrangeHit", slam, f, 4.2f), slam, f, 1.2f), 0f, 0f, 4.9f, "원 4.9 m");
            Row("랜슬롯 심판 막타 베기", await Watch(() => RelicStateVfx.PlayOneShot("vfx_lancelot_judgment", o + f * 2f + Vector3.up * 0.9f, 0.189f, f, 1.2f), o, f, 1f), 6f, 4.2f, 0f, "부채꼴 6 m · ±45°(끝 옆 4.2)");
            Row("랜슬롯 심판 연타 베기", await Watch(() => RelicStateVfx.PlayOneShot("vfx_lancelot_judgment", o + f * 2f + Vector3.up * 0.55f, 0.42f, f), o, f, 1f), 6f, 4.2f, 0f, "부채꼴 6 m · ±45°");
            Row("랜슬롯 작은 심판(콘)", await Watch(() => RelicStateVfx.PlayOneShot("vfx_lancelot_judgment", o + f * 1.6f + Vector3.up * 0.7f, 0.12f, f, 1.2f), o, f, 1f), 4f, 2.6f, 0f, "부채꼴 4 m · ±40°");
            Vector3 impact = o + f * 3f;
            Row("가웨인 낙일 착탄 폭발", await Watch(() => RelicStateVfx.PlayOneShot("vfx_gawain_solar_impact", impact, 1.25f), impact, f, 2f), 0f, 0f, 6.5f, "원 6.5 m(고리 일치)");
            Row("빛 룬 광폭발", await Watch(() => ElementVfxPlayer.PlayBurst(RuneElement.Light, o + f * 2.8f, 3.5f), o, f, 1.2f), 7f, 5.4f, 0f, "부채꼴 7 m · ±50°(끝 옆 5.4)");
            // 스킬 폭발 · 마무리 — 스킬 SpawnEffectScaled와 같은 길(⑥ · 돌진 궤적과 섞이지 않게 따로)
            async UniTaskVoid Pooled(string key, Vector3 at, float scale)
            {
                var obj = await Managers.ObjectPooler.SpawnAsync(key, ObjectPoolerManager.PoolType.Effect, at, p.transform.rotation);
                if (obj == null) return;
                obj.transform.localScale = Vector3.one * scale;
                foreach (var ps in obj.GetComponentsInChildren<ParticleSystem>(true)) { ps.Clear(true); ps.Play(true); }
            }
            foreach (var (tag, key, sc, skR) in new[] {
                ("성광베기 1 · 2단계 폭발", "FrontAttackHit", 4.5f, 3f), ("성광베기 3단계 폭발", "MissileExplosion", 0.65f, 3f),
                ("환영베기 마무리 폭발", "DoubleSlash", 0.9f, 3f), ("환영베기 마무리 옆 폭발", "DoubleSlash", 0.63f, 3f),
                ("무형 연참 2단계 마무리", "NamelessCut", 0.28f, 2f), ("무형 연참 3단계 일섬(길 가운데 · 길 폭 3 m)", "NamelessCut", 0.42f, 3f),
                ("무형 광선 착탄 폭발", "NamelessBeamHit", 0.19f, 2f), ("무형 광선 3단계 추가 착탄", "NamelessBeamHit", 0.22f, 2f),
                // 맞힘(한 대상 · 광역 없음 — 반경 1 m 안팎이면 맞음 · ⑦)
                ("성광베기 2단계 추가 맞힘", "KatanaSlashHit", 0.1f, 1f), ("성광베기 맞힘", "SwordHitImpact", 0.23f, 1f), ("환영베기 맞힘", "SwordHitImpact", 0.23f, 1f) })
                Row($"스킬 {tag} {key} ×{sc:0.#}", await Watch(() => Pooled(key, o + Vector3.up * 0.5f, sc).Forget(), o, f, 2f), 0f, 0f, skR, $"원 {skR:0} m");
            Row("불 룬 작열 폭발", await Watch(() => ElementVfxPlayer.PlayBurst(RuneElement.Fire, o, 4f), o, f, 1.2f), 0f, 0f, 4f, "원 4 m");
            foreach (var (tag, key, itemR, isc) in new[] { ("스킬 화염 폭발", "VFX_FireExplosion", 4f, 4f / 1.25f), ("스킬 번개 강타", "VFX_LightningStrike", 3f, 3f / 4.3f), ("구르기 착지 충격", "VFX_JumpLandingDamage", 3f, 3f / 4.17f) })
                Row($"아이템 {tag}(×{isc:0.##})", await Watch(() => ItemEffectVfxHelper.SpawnOneShotAt(key, o, isc), o, f, 2f), 0f, 0f, itemR, $"원 {itemR:0} m");
            foreach (var (tag, el) in new[] { ("얼음", RuneElement.Ice), ("번개", RuneElement.Electric), ("풀", RuneElement.Grass), ("어둠", RuneElement.Dark) })
                Row($"{tag} 광역 폭발(공칭)", await Watch(() => ElementVfxPlayer.PlayBurst(el, o, 3f), o, f, 1.2f), 0f, 0f, 3f, "원 3 m");
            var cat = LegendaryRuntime.Catalog;
            if (cat != null)
            {
                // 게임과 같은 배율(10-13 ⑤: 반경 3 m × 속성별 m당 배율 · 어둠은 아직 옛 0.4 — 이번에 잼)
                foreach (var (tag, fx, sc) in new[] { ("불", cat.fireAoeVfx, 0.4f), ("얼음", cat.iceAoeVfx, 3f * 0.0488f), ("풀", cat.grassAoeVfx, 3f * 0.0556f), ("빛", cat.lightAoeVfx, 3f * 0.0377f), ("어둠", cat.darkAoeVfx, 3f * 0.286f) })
                    if (fx != null) Row($"전설 {tag} 광역(0단계)", await Watch(() => LegendaryRuntime.SpawnVfx(fx, o, Quaternion.identity, 3f, sc, 0.18f, 0.1f), o, f, 2f), 0f, 0f, 3f, "원 3 m(플레이어 둘레)");
                if (cat.elecHitVfx != null)
                    Row("전설 번개 단일 폭발(0단계)", await Watch(() => LegendaryRuntime.SpawnVfx(cat.elecHitVfx, impact, Quaternion.identity, 1.5f, 0.3f), impact, f, 1.5f), 0f, 0f, 3f, "원 3 m");
            }
            else sb.AppendLine("  전설 목록 없음(LegendaryRuntime.Catalog null)");
        }
        sb.Insert(0, $"맞음 {ok} · 어긋남 · 없음 {bad}\n");
    }

    // ── 2 칼바람 ────────────────────────────────────────────────
    private static async UniTask WindAsync(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        if (!await EquipAsync(p, Katana, ct)) { sb.AppendLine("카타나 장착 실패"); return; }
        await Wait(1f, ct);
        var rt = WeaponFormRuntime.Of(p);
        var form = rt?.Current;
        s_home = p.transform.position;
        Check(sb, "구동기 · 칼바람 생성", form is WindbladeForm);
        if (form == null) return;

        Vector3 f = Flat(p.transform.forward), r = Vector3.Cross(Vector3.up, f);
        var near = await TrackAsync(p.transform.position + f * 1.5f, ct);   // 평타가 닿는 적
        var far  = await TrackAsync(p.transform.position + f * 7f, ct);     // 풍절만 닿는 거리
        var side = await TrackAsync(p.transform.position + r * 6f, ct);     // 풍절 줄 밖
        if (near == null || far == null || side == null) { sb.AppendLine("슬라임 부르기 실패"); return; }
        int maxSeen = 0, triggers = 0, damaged = 0;
        form.Triggered += _ => triggers++;
        s_formLog.Clear();
        // 진단(10-06 6회차 풍절 0회): 적중마다 「콤보 단계:칼바람」, 행동 상태가 바뀔 때 <상태>, 피격
        var trace = new StringBuilder();
        string lastAct = null;
        var actField = typeof(PlayerController).GetField("actStateDebug", Inst);
        Action onDmg = () => { damaged++; trace.Append(" [피격]"); };
        Action<HitInfo> onHit = h =>
        {
            if (h.Attacker != p.gameObject || h.ActionType != WeaponActionType.GroundLight) return;
            trace.Append($" {p.Combo.CurrentComboStep}:{form.Stacks}");
        };
        p.OnDamageTaken += onDmg;
        HitFeedbackService.OnHit += onHit;
        bool fullShot = false;
        form.Triggered += FirstTriggerShots("WF_Wind");   // 계획 Task 6 Step 6 화면 — 발동 이펙트 · 첫 발동 3D 글자 · 계정 첫 자막
        float t0 = Time.realtimeSinceStartup;
        try
        {
            while (Time.realtimeSinceStartup - t0 < 14f)
            {
                RefillAll();
                Steer(p, near.M.transform.position);
                p.InputBuffer.Push(Command.Light);
                maxSeen = Mathf.Max(maxSeen, form.Stacks);
                if (!fullShot && form.IsFull) { fullShot = true; await ShotUi("WF_Wind_1_Full", 150); }   // 가득 트레일 · 칼날 빛
                float tNext = Time.realtimeSinceStartup + 0.25f;
                while (Time.realtimeSinceStartup < tNext)   // 프레임마다 <행동 상태 + 콤보 단계>가 바뀌면 남긴다
                {
                    string act = $"{actField?.GetValue(p)}{p.Combo.CurrentComboStep}";
                    if (act != lastAct)
                    {
                        Vector3 toNear = near.M.transform.position - p.transform.position; toNear.y = 0f;
                        trace.Append($" <{act} {Vector3.SignedAngle(p.AimForward, toNear, Vector3.up):0}° {toNear.magnitude:0.0}m{AimDiag(p, toNear, near, far, side)}>");
                        lastAct = act;
                    }
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                }
            }
        }
        finally { p.OnDamageTaken -= onDmg; HitFeedbackService.OnHit -= onHit; }
        sb.AppendLine($"   진단 — 적중(단계:칼바람)·<행동>:{trace} · 피격 {damaged}");
        Check(sb, $"칼바람 최대 {maxSeen}/5 도달", maxSeen >= 5);
        Check(sb, $"풍절 발동 {triggers}회", triggers >= 1);
        Check(sb, $"7 m 표적 피해 {far.Dealt:0} (풍절만 닿는다)", far.Dealt > 0);
        Check(sb, $"줄 밖 표적 피해 {side.Dealt:0} = 0", side.Dealt == 0);

        // 3초 못 맞히면 1초에 1씩 준다 — 피격 시험보다 먼저(밀려나거나 표적이 쓰러지면 다시 쌓지 못한다, 실측 2 3~5회차)
        near = await EnsureAliveAsync(near, ct);
        float guard = Time.realtimeSinceStartup;
        while (form.Stacks < 3 && Time.realtimeSinceStartup - guard < 8f) { RefillAll(); Steer(p, near.M.transform.position); p.InputBuffer.Push(Command.Light); await Wait(0.25f, ct); }
        // 남은 입력으로 휘두름이 이어지면 쉬는 시간이 아니다(실측 2 7회차 3 → 3) — 입력을 비우고 행동이 멈춘 뒤 잰다
        p.InputBuffer.Clear();
        var actF = typeof(PlayerController).GetField("actStateDebug", Inst);
        float settle = Time.realtimeSinceStartup;
        while (actF?.GetValue(p)?.ToString() != "None" && Time.realtimeSinceStartup - settle < 3f) await UniTask.Yield(PlayerLoopTiming.Update, ct);
        int before = form.Stacks, idleHits = 0;
        float gameT0 = Time.time;
        Action<HitInfo> onIdleHit = h => { if (h.Attacker == p.gameObject) idleHits++; };
        HitFeedbackService.OnHit += onIdleHit;
        try { await Wait(5.2f, ct); }
        finally { HitFeedbackService.OnHit -= onIdleHit; }
        sb.AppendLine($"   진단 — 쉬는 동안: 게임 시간 {Time.time - gameT0:0.0}초 · 배속 {Time.timeScale:0.00} · 적중 {idleHits} · 행동 {typeof(PlayerController).GetField("actStateDebug", Inst)?.GetValue(p)}");
        Check(sb, $"쉬면 줄어든다 {before} → {form.Stacks}", before >= 3 && idleHits == 0 && form.Stacks <= before - 2);

        // 피격하면 0 — 무적을 잠깐 끄고 피해
        near = await EnsureAliveAsync(near, ct);
        guard = Time.realtimeSinceStartup;
        while (form.Stacks < 2 && Time.realtimeSinceStartup - guard < 8f) { RefillAll(); Steer(p, near.M.transform.position); p.InputBuffer.Push(Command.Light); await Wait(0.25f, ct); }
        int beforeHit = form.Stacks;
        typeof(PlayerController).GetField("debugInvincible", Inst)?.SetValue(p, false);
        p.TakeDamage(30);   // 방어로 0이 되면 OnDamageTaken이 안 온다 — 넉넉히
        typeof(PlayerController).GetField("debugInvincible", Inst)?.SetValue(p, true);
        await Wait(0.1f, ct);
        Check(sb, $"피격 → 칼바람 {beforeHit} → 0", beforeHit >= 2 && form.Stacks == 0);
        DetachAll();
    }

    // ── 3 무게 ──────────────────────────────────────────────────
    private static async UniTask WeightAsync(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        if (!await EquipAsync(p, Great, ct)) { sb.AppendLine("대검 장착 실패"); return; }
        await Wait(1f, ct);
        var form = WeaponFormRuntime.Of(p)?.Current;
        s_home = p.transform.position;
        Check(sb, "구동기 · 무게 생성", form is WeightForm);
        if (form == null) return;

        // 확정 치명은 「가득일 때 마지막 타 슬래시가 나오는 순간」에 건다(10-07 · 기획 §4-3). 마지막 단계에 들어설 때 걸면
        // 새 대검 3타(03_5)는 베기까지 약 1.6초라 그 사이 다른 피해가 확정 치명을 먼저 썼다(실측 3 12회차: 슬래시 순간 걸림 False · 치명 1/2).
        // 슬래시 이벤트(OnSwing)와 콤보 단계를 직접 움직이는 단위 시험(플레이어는 서 있다).
        {
            int last = Mathf.Max(1, p.WeaponManager.Weapon0Data.groundEndCount) - 1;
            var fake = new HitInfo(p.gameObject, p.gameObject, p.transform.position, p.transform.forward, 1f, false, WeaponActionType.GroundLight);
            var onSwing = typeof(WeaponFormBase).GetMethod("OnSwing");
            p.InputBuffer.Clear();
            p.Combo.Reset();
            p.RuntimeStats.ConsumeForceCrit(out _);
            for (int i = 0; i < form.MaxStacks - 1; i++) form.OnMeleeHit(fake, true, false);   // 무게 3
            for (int i = 0; i < last; i++) p.Combo.IncrementStep();
            await UniTask.DelayFrame(2, cancellationToken: ct);
            onSwing?.Invoke(form, new object[] { last });
            bool armedNotFull = p.RuntimeStats.ForceCritArmed;                                  // 가득 전 마지막 타 휘두름
            form.OnMeleeHit(fake, true, true);                                                   // 마지막 타가 가득을 채운다
            await UniTask.DelayFrame(2, cancellationToken: ct);
            bool armedOnFill = p.RuntimeStats.ForceCritArmed;
            bool fullAfterFill = form.IsFull;
            p.Combo.ResetStep();
            await UniTask.DelayFrame(2, cancellationToken: ct);
            for (int i = 0; i < last; i++) p.Combo.IncrementStep();                              // 가득으로 마지막 단계에 들어서기만
            await UniTask.DelayFrame(2, cancellationToken: ct);
            bool armedOnEnter = p.RuntimeStats.ForceCritArmed;
            onSwing?.Invoke(form, new object[] { last });                                        // 마지막 타 슬래시가 나온다
            bool armedOnSwing = p.RuntimeStats.ForceCritArmed;
            p.Combo.ResetStep();                                                                  // 콤보가 넘어가도 판정은 남는다
            await UniTask.DelayFrame(2, cancellationToken: ct);
            bool armedJustAfter = p.RuntimeStats.ForceCritArmed;
            await UniTask.Delay(TimeSpan.FromSeconds(0.6f), DelayType.DeltaTime, cancellationToken: ct);
            bool armedAfterGrace = p.RuntimeStats.ForceCritArmed;
            for (int i = 0; i < last; i++) p.Combo.IncrementStep();                              // 다시 건 뒤 다음 휘두름이 오면
            await UniTask.DelayFrame(2, cancellationToken: ct);
            onSwing?.Invoke(form, new object[] { last });
            p.Combo.ResetStep();
            await UniTask.DelayFrame(2, cancellationToken: ct);
            onSwing?.Invoke(form, new object[] { 0 });
            bool armedAfterNextSwing = p.RuntimeStats.ForceCritArmed;
            Check(sb, $"가득 전 마지막 타 휘두름 → 안 건다 (걸림 {armedNotFull})", !armedNotFull);
            Check(sb, $"마지막 타가 가득을 채우면 → 안 건다 (걸림 {armedOnFill} · 가득 {fullAfterFill})", !armedOnFill && fullAfterFill);
            Check(sb, $"가득으로 마지막 단계에 들어서기만 → 아직 안 건다 (걸림 {armedOnEnter})", !armedOnEnter);
            Check(sb, $"가득 · 마지막 타 슬래시가 나오면 → 건다 (걸림 {armedOnSwing})", armedOnSwing);
            Check(sb, $"콤보가 넘어간 직후엔 아직 건 채(판정이 남아 있다) (걸림 {armedJustAfter})", armedJustAfter);
            Check(sb, $"0.6초 동안 안 쓰이면 → 푼다 (걸림 {armedAfterGrace})", !armedAfterGrace);
            Check(sb, $"다음 휘두름이 오면 → 바로 푼다 (걸림 {armedAfterNextSwing})", !armedAfterNextSwing);
            p.Combo.Reset();
            p.RuntimeStats.ConsumeForceCrit(out _);
            typeof(WeaponFormBase).GetMethod("SetStacks", Inst)?.Invoke(form, new object[] { 0, WeaponFormBase.StackReason.Gain });
        }

        Vector3 f = Flat(p.transform.forward), r = Vector3.Cross(Vector3.up, f);
        var near  = await TrackAsync(p.transform.position + f * 1.5f, ct);
        var ring  = await TrackAsync(p.transform.position + f * 1.5f + r * 3f, ct);   // 충격파 반경(3.5 m) 안 · 평타 호 밖
        var outer = await TrackAsync(p.transform.position + f * 1.5f + r * 6f, ct);   // 반경 밖
        if (near == null || ring == null || outer == null) { sb.AppendLine("슬라임 부르기 실패"); return; }
        int maxSeen = 0, slams = 0, finisherCrits = 0, finisherHits = 0;
        bool stunSeen = false;
        form.Triggered += _ => slams++;
        int slamsSeen = 0;
        var critTrace = new StringBuilder();
        Action<HitInfo> onHit = h =>
        {
            // 내려찍기를 낸 타 — 구동기가 먼저 구독해 이 타에서 이미 내려찍고 무게를 비웠다(실측 3 1회차: 「가득일 때」로 걸러 0/0)
            if (h.Attacker != p.gameObject || h.ActionType != WeaponActionType.GroundLight || slams == slamsSeen) return;
            slamsSeen = slams;
            finisherHits++;
            if (h.IsCritical) finisherCrits++;
            critTrace.Append($" [내려찍기 타 {h.Target.name} 치명 {h.IsCritical} 피해 {h.Damage:0}]");
        };
        // 진단(실측 3 7 · 9회차 치명 1/2): 마지막 타 슬래시가 나오는 순간 확정 치명이 걸려 있었나 · 무게
        var recvW = p.GetComponentInChildren<PlayerAnimationEventReceiver>();
        Action<int> onSlash = n =>
        {
            int stepNow = p.Combo.CurrentComboStep;
            critTrace.Append($" <슬래시 {n} 단계 {stepNow} 무게 {form.Stacks} 걸림 {p.RuntimeStats.ForceCritArmed}>");
        };
        if (recvW != null) recvW.OnEffectStep += onSlash;
        HitFeedbackService.OnHit += onHit;
        bool fullShot = false;
        form.Triggered += FirstTriggerShots("WF_Weight");
        try
        {
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 16f)
            {
                RefillAll();
                Steer(p, near.M.transform.position);
                p.InputBuffer.Push(Command.Light);
                maxSeen  = Mathf.Max(maxSeen, form.Stacks);
                if (!fullShot && form.IsFull) { fullShot = true; await ShotUi("WF_Weight_1_Full", 150); }
                stunSeen |= ring.M != null && ring.M.Status.HasCc("stun");
                await Wait(0.25f, ct);
            }
        }
        finally { HitFeedbackService.OnHit -= onHit; if (recvW != null) recvW.OnEffectStep -= onSlash; }
        sb.AppendLine("   진단 — " + critTrace);
        Check(sb, $"무게 최대 {maxSeen}/4 도달", maxSeen >= 4);
        Check(sb, $"내려찍기 {slams}회", slams >= 1);
        int critFallbacks = s_formLog.FindAll(l => l.Contains("내려찍기 치명 보정")).Count;   // 확정 치명이 안 실린 타는 무게가 치명 몫을 더 준다
        Check(sb, $"가득일 때 마지막 타 치명 — 직접 {finisherCrits} + 보정 {critFallbacks} / {finisherHits}", finisherHits > 0 && finisherCrits + critFallbacks == finisherHits);
        Check(sb, $"반경 안 표적 피해 {ring.Dealt:0} · 경직 {stunSeen}", ring.Dealt > 0 && stunSeen);
        Check(sb, $"반경 밖 표적 피해 {outer.Dealt:0} = 0", outer.Dealt == 0);

        // 마지막 타를 헛치면 확정 치명이 풀린다 — 가득으로 마지막 타 단계에 들어서는 순간 표적을 치운다
        near = await EnsureAliveAsync(near, ct);
        int lastStep = Mathf.Max(1, p.WeaponManager.Weapon0Data.groundEndCount) - 1;
        float guard = Time.realtimeSinceStartup;
        bool reached = false;
        while (!reached && Time.realtimeSinceStartup - guard < 12f)
        {
            RefillAll(); Steer(p, near.M.transform.position); p.InputBuffer.Push(Command.Light);
            float tNext = Time.realtimeSinceStartup + 0.25f;
            while (Time.realtimeSinceStartup < tNext && !(reached = form.IsFull && p.Combo.CurrentComboStep >= lastStep))
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
        }
        p.InputBuffer.Clear();
        foreach (var t in s_tracks) if (t.M != null) t.M.gameObject.SetActive(false);
        // 확정 치명은 마지막 타 슬래시가 나올 때 걸린다 — 그 이벤트를 기다렸다가 본다(표적은 이미 치웠으니 헛친다)
        bool finisherSlash = false;
        var recvS = p.GetComponentInChildren<PlayerAnimationEventReceiver>();
        Action<int> onFin = _ => { if (p.Combo.CurrentComboStep >= lastStep) finisherSlash = true; };
        if (recvS != null) recvS.OnEffectStep += onFin;
        float waitFin = Time.realtimeSinceStartup;
        while (!finisherSlash && Time.realtimeSinceStartup - waitFin < 4f) await UniTask.Yield(PlayerLoopTiming.Update, ct);
        if (recvS != null) recvS.OnEffectStep -= onFin;
        await UniTask.DelayFrame(2, cancellationToken: ct);
        bool armedAtStop = p.RuntimeStats.ForceCritArmed;
        await Wait(0.7f, ct);   // 판정 수명(0.5초)이 지나도록
        var actF = typeof(PlayerController).GetField("actStateDebug", Inst);
        float settle = Time.realtimeSinceStartup;
        while (actF?.GetValue(p)?.ToString() != "None" && Time.realtimeSinceStartup - settle < 3f) await UniTask.Yield(PlayerLoopTiming.Update, ct);
        await Wait(0.3f, ct);
        bool leaked = p.RuntimeStats.ForceCritArmed;
        Check(sb, $"마지막 타를 헛치면 확정 치명이 풀린다 — 도달 {reached} · 걸림 {armedAtStop} → 남음 {leaked} (무게 {form.Stacks})", reached && armedAtStop && !leaked);
        DetachAll();
    }

    // ── 4 변주 ──────────────────────────────────────────────────
    private static async UniTask VariantsAsync(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        await VariantCaseAsync(p, sb, Katana, "twin_wind,storm_eye", new[] { "쌍풍", "폭풍의 눈" }, dodge: false, skill: false, ct);
        await VariantCaseAsync(p, sb, Katana, "return_wind,still_wind", new[] { "되감는 바람", "무풍" }, dodge: true, skill: false, ct);
        await VariantCaseAsync(p, sb, Great, "aftershock,heavy_hand", new[] { "여진", "무거운 손" }, dodge: false, skill: true, ct);
        await VariantCaseAsync(p, sb, Great, "fissure,crush", new[] { "균열", "짓누름" }, dodge: false, skill: false, ct);
    }

    private static async UniTask VariantCaseAsync(PlayerController p, StringBuilder sb, string weaponPath, string variants,
                                                  string[] expectLogs, bool dodge, bool skill, CancellationToken ct)
    {
        if (!await EquipAsync(p, weaponPath, ct)) { sb.AppendLine($"장착 실패 {weaponPath}"); return; }
        var w = p.WeaponManager.Weapon0Data;
        w.formTier = 3;
        w.formVariants = variants;
        var rt = WeaponFormRuntime.Of(p);
        rt.Rebuild();
        DetachAll();
        foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
            if (m != null && !m.IsDead) m.gameObject.SetActive(false);
        await Wait(0.5f, ct);
        s_home = p.transform.position;
        Vector3 f = Flat(p.transform.forward), r = Vector3.Cross(Vector3.up, f);
        var near = await TrackAsync(p.transform.position + f * 1.5f, ct);
        await TrackAsync(p.transform.position + Quaternion.AngleAxis(12f, Vector3.up) * f * 6f, ct);    // 쌍풍 오른쪽 갈래
        await TrackAsync(p.transform.position + Quaternion.AngleAxis(-12f, Vector3.up) * f * 6f, ct);   // 쌍풍 왼쪽 갈래
        await TrackAsync(p.transform.position + f * 1.5f + r * 4.5f, ct);                               // 여진만 닿는 거리(3.5 < 4.5 < 4.9)
        if (near == null) { sb.AppendLine("슬라임 부르기 실패"); return; }
        s_formLog.Clear();
        // 쌍풍은 「넓게」(기획 §4-2) — 두 갈래 어디에 들었든 적마다 한 번. 발동 순간 두 갈래 안의 서로 다른 적 수와 맞힘 수를 맞댄다
        int twinChecked = 0, twinOff = 0;
        string twinLast = null;
        var trigForm = rt.Current;
        Action<string> onTrig = _ =>
        {
            string line = s_formLog.FindLast(l => l.Contains("쌍풍 — 맞힘"));
            if (line == null || !int.TryParse(line.Substring(line.LastIndexOf(' ') + 1), out int n)) return;
            int distinct = CountInTwin(p, trigForm.Form.TriggerRange);
            twinChecked++;
            if (n != distinct) { twinOff++; twinLast = $"{n} ≠ {distinct}"; }
        };
        bool twin = variants.Contains("twin_wind") && trigForm != null;
        if (twin) trigForm.Triggered += onTrig;
        // 진단(실측 4 3~4회차 「맞힘 0」): 발동 순간 [집에서 거리 · 앞 표적 거리(꺼짐) · 겨눈 쪽과 앞 표적 각 · 9.4 m 안 적 수]
        var trigTrace = new StringBuilder();
        Action<string> onTrigDiag = name =>
        {
            var nm = near?.M;
            Vector3 to = nm != null ? nm.transform.position - p.transform.position : Vector3.zero; to.y = 0f;
            int around = CombatQuery.GetNearbyEnemies(p.transform.position, 9.4f, p.gameObject, 32, s_twinBuf);
            trigTrace.Append($" [{name} 집 {Vector3.Distance(p.transform.position, s_home):0.0} · 앞 {to.magnitude:0.0}{(nm != null && nm.gameObject.activeInHierarchy ? "" : " 꺼짐")} · {Vector3.SignedAngle(p.AimForward, to, Vector3.up):0}° · 적 {around}]");
        };
        if (trigForm != null) trigForm.Triggered += onTrigDiag;
        bool usedThisFull = false;   // 가득 한 번에 회피 · E 한 번 — 나머지는 평타로 마지막 타(발동)까지 간다
        float t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < 18f)
        {
            RefillAll();
            HideStrays();   // 전투방 파도 몬스터 — 조준 보정이 끌려가고, 발동에 죽어 셈이 어긋난다(실측 4 3회차 「맞힘 0」 · 6 ≠ 3)
            near = await EnsureAliveAsync(near, ct);
            Steer(p, near.M.transform.position);
            var form = rt.Current;
            bool full = form != null && form.IsFull;
            if (!full) usedThisFull = false;
            if (full && !usedThisFull && dodge)      { p.InputBuffer.Push(Command.Dodge);  usedThisFull = true; }
            else if (full && !usedThisFull && skill) { p.InputBuffer.Push(Command.ESkill); usedThisFull = true; }
            else p.InputBuffer.Push(Command.Light);
            await Wait(0.25f, ct);
        }
        foreach (var name in expectLogs)
        {
            int n = s_formLog.FindAll(l => l.Contains(name)).Count;
            Check(sb, $"{variants} — 「{name}」 로그 {n}회", n > 0);
        }
        if (trigForm != null) trigForm.Triggered -= onTrigDiag;
        sb.AppendLine($"   진단 — 발동:{trigTrace}");
        if (twin)
        {
            trigForm.Triggered -= onTrig;
            Check(sb, $"{variants} — 쌍풍은 적마다 한 번 (발동 {twinChecked}회 중 어긋남 {twinOff}{(twinLast != null ? " · 마지막 " + twinLast : "")})", twinChecked > 0 && twinOff == 0);
        }
        DetachAll();
    }

    private static readonly List<MonsterBase> s_twinBuf = new(32);

    /// <summary>표적 슬라임이 아닌 살아 있는 몬스터를 제자리에 묶는다(끄지 않는다 — 방 클리어 · 보상 연출을 부르지 않게).</summary>
    private static void HoldStrays()
    {
        foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
            if (m != null && !m.IsDead && !s_tracks.Exists(t => t.M == m)) m.Status.ApplyCc("probe_hold", 9999f);
    }

    /// <summary>표적 슬라임이 아닌 살아 있는 몬스터를 끈다.</summary>
    private static void HideStrays()
    {
        foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
            if (m != null && !m.IsDead && !s_tracks.Exists(t => t.M == m)) m.gameObject.SetActive(false);
    }

    /// <summary>지금 플레이어 자리 · 겨눈 쪽에서 ±12° 두 갈래(폭 0.9 + 0.4 m — FormStrike.Line과 같은 판정) 안의 서로 다른 적 수.</summary>
    private static int CountInTwin(PlayerController p, float length)
    {
        Vector3 from = p.transform.position, dir = Flat(p.AimForward);
        Vector3 toL = from + Quaternion.AngleAxis(-12f, Vector3.up) * dir * length;
        Vector3 toR = from + Quaternion.AngleAxis( 12f, Vector3.up) * dir * length;
        int n = CombatQuery.GetNearbyEnemies(from, length + 1.8f, p.gameObject, 32, s_twinBuf), c = 0;
        for (int i = 0; i < n; i++)
        {
            var e = s_twinBuf[i];
            if (e == null || e.IsDead) continue;
            Vector3 at = e.transform.position;
            if (FormStrike.DistToSegmentXZ(at, from, toL) <= 1.3f || FormStrike.DistToSegmentXZ(at, from, toR) <= 1.3f) c++;
        }
        return c;
    }

    // ── 5 예산 ──────────────────────────────────────────────────
    private static async UniTask BudgetAsync(PlayerController p, StringBuilder sb, CancellationToken ct, string onlyPath = null)
    {
        var cases = new (string path, int tier, string variants, string label)[]
        {
            (Katana, 0, "",                    "카타나 +5 형 끔"),
            (Katana, 1, "",                    "카타나 +5 형 Ⅰ"),
            (Katana, 3, "twin_wind,storm_eye", "카타나 +5 형 Ⅲ(쌍풍 · 폭풍의 눈)"),
            (Great,  0, "",                    "대검 +5 형 끔"),
            (Great,  1, "",                    "대검 +5 형 Ⅰ"),
            (Great,  3, "aftershock,crush",    "대검 +5 형 Ⅲ(여진 · 짓누름)"),
        };
        // 기획 §6: 단일 표적 DPS(범위 · 경직 · 원거리 같은 기능 값은 DPS에 넣지 않는다). 1회차는 표적 3마리 합계라 풍절 · 내려찍기 범위가 부풀었다.
        var loadoutRelic = AppBootstrapper.Instance != null ? AppBootstrapper.Instance.Loadout?.Relic : null;   // p.RelicClass는 프리팹 기본값(3회차 「가웨인」 오표기)
        sb.AppendLine($"유물 {(loadoutRelic != null ? loadoutRelic.name : "없음")} · 단일 표적 · 칸당 정착 3초 + 측정 45초");
        float baseDps = 0f, baseSwings = 0f;
        // 예열 — 런 들어 첫 장착 칸은 팝업을 닫고 다시 재도 평타 0회였다(대검 예산 8회차, 형 끔 칸만). 같은 무기로 한 번 장착 · 2초 휘둘러 둔다
        if (await EquipAsync(p, onlyPath ?? cases[0].path, ct))
        {
            float w0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - w0 < 2f) { p.InputBuffer.Push(Command.Light); await Wait(0.25f, ct); }
            await Wait(1f, ct);
        }
        foreach (var c in cases)
        {
            if (onlyPath != null && c.path != onlyPath) continue;   // 바뀐 칸만 다시 잰다
            // 런 시작 직후 첫 칸은 팝업(유물 기억 고르기 등)에 입력이 막혀 평타 0회로 재였다(대검 예산 6 · 7회차) — 팝업을 닫고 시간을 되돌린다
            Managers.UI?.CloseAllPopupUI();
            Time.timeScale = 1f;
            if (!await EquipAsync(p, c.path, ct)) { sb.AppendLine($"장착 실패 {c.label}"); continue; }
            var w = p.WeaponManager.Weapon0Data;
            w.enhanceLevel = 5;
            w.RecomputeEnhancedStats();
            w.formTier = c.tier;
            w.formVariants = c.variants;
            WeaponFormRuntime.Of(p).Rebuild();
            p.WeaponManager.RaiseEquippedWeaponRefreshed();   // 공격력 다시 적용
            DetachAll();
            foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
                if (m != null && !m.IsDead) m.gameObject.SetActive(false);
            await Wait(0.5f, ct);
            s_home = p.transform.position;
            Vector3 f = Flat(p.transform.forward);
            var t0Track = await TrackAsync(p.transform.position + f * 1.6f, ct);
            if (t0Track == null) { sb.AppendLine($"슬라임 부르기 실패 {c.label}"); continue; }
            float settle = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - settle < 3f) { RefillAll(); Steer(p, t0Track.M.transform.position); p.InputBuffer.Push(Command.Light); await Wait(0.25f, ct); }
            foreach (var t in s_tracks) t.Dealt = 0f;   // 정착 동안 쌓인 것은 버린다
            // 진단(2회차 카타나 Ⅰ −33%): 평타 적중 수 · 평타 피해 · 치명 · 형 발동 수 — DPS 차이가 어디서 오는지 가른다
            int swings = 0, crits = 0, triggers = 0;
            float basic = 0f;
            float lastHitAt = -1f;
            Action<HitInfo> onHit = h =>
            {
                if (h.Attacker != p.gameObject || h.ActionType != WeaponActionType.GroundLight || t0Track.M == null || h.Target != t0Track.M.gameObject) return;
                basic += h.Damage;
                if (h.IsCritical) crits++;
                if (Time.time - lastHitAt > 0.12f) swings++;
                lastHitAt = Time.time;
            };
            Action<string> onTrig = _ => triggers++;
            var formNow = WeaponFormRuntime.Of(p).Current;
            HitFeedbackService.OnHit += onHit;
            if (formNow != null) formNow.Triggered += onTrig;
            float t0 = Time.realtimeSinceStartup;
            try
            {
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    t0 = Time.realtimeSinceStartup;
                    while (Time.realtimeSinceStartup - t0 < 45f)
                    {
                        RefillAll();
                        Steer(p, t0Track.M.transform.position);
                        p.InputBuffer.Push(Command.Light);
                        await Wait(0.25f, ct);
                    }
                    if (swings > 0) break;
                    sb.AppendLine($"   {c.label}: 평타 0회 — 팝업을 닫고 다시 잰다");
                    Managers.UI?.CloseAllPopupUI();
                    Time.timeScale = 1f;
                    foreach (var t in s_tracks) t.Dealt = 0f;
                    basic = 0f; crits = 0; triggers = 0;
                }
            }
            finally
            {
                HitFeedbackService.OnHit -= onHit;
                if (formNow != null) formNow.Triggered -= onTrig;
            }
            float dealt = 0f;
            foreach (var t in s_tracks) dealt += t.Dealt;
            float dps = dealt / 45f;
            if (c.tier == 0) { baseDps = dps; baseSwings = swings; }
            string delta = c.tier == 0 || baseDps <= 0f ? "" : $" ({(dps / baseDps - 1f) * 100f:+0;-0}%";
            // 평타 횟수 보정(실측 5b 3회차: 형 끔 55회 · 형 Ⅰ 62회 — 휘두른 횟수 차이가 형 몫보다 컸다)
            if (delta.Length > 0) delta += swings > 0 && baseSwings > 0 ? $" · 평타 수 보정 {(dps / swings) / (baseDps / baseSwings) * 100f - 100f:+0.0;-0.0}%)" : ")";
            sb.AppendLine($"{c.label}: DPS {dps:0.0}{delta} · 평타 {swings}회 {basic / 45f:0.0}/초 · 치명 {crits} · 형 발동 {triggers} · 그 밖 {(dealt - basic) / 45f:0.0}/초");
        }
        DetachAll();
    }

    // ── 39 빌드 시너지 계획 1(10-14) — 바뀐 칸 7칸 ─────────────────────────
    // 기획 바탕화면 기획/RelicFairy_빌드시너지_축세우기·유물진화_설계_20261013.md · 계획 구현설계/RelicFairy_빌드시너지_계획1_결함·발동계수·강타_20261014.md

    /// <summary>적중 보고 엿보기 — 효과 목록에 끼워 발동 계수 · 강타를 그대로 받아 적는다.</summary>
    private sealed class ReportSpy : ItemEffectBase
    {
        public readonly List<DamageReport> Seen = new();
        public ReportSpy() : base(new ItemEffectSlot { effectType = "ProbeSpy", trigger = "Always" }) { }
        public override void OnPostDealDamage(ItemEffectContext ctx, DamageReport report) => Seen.Add(report);
    }

    private static void InjectEffect(IItemEffect e)
    {
        var mgr  = GameRunBootstrapper.Instance?.Run?.EffectManager;
        if (mgr == null) return;
        var list = typeof(ItemEffectManager).GetField("_activeEffects", Inst)?.GetValue(mgr) as List<IItemEffect>;
        var ctx  = typeof(ItemEffectManager).GetField("_ctx", Inst)?.GetValue(mgr) as ItemEffectContext;
        if (list == null || list.Contains(e)) return;
        list.Add(e);
        e.OnActivate(ctx);
    }

    private static void RemoveEffect(IItemEffect e)
    {
        var mgr  = GameRunBootstrapper.Instance?.Run?.EffectManager;
        var list = mgr != null ? typeof(ItemEffectManager).GetField("_activeEffects", Inst)?.GetValue(mgr) as List<IItemEffect> : null;
        if (list != null && list.Remove(e)) e.OnDeactivate();
    }

    /// <summary>플레이어 이름으로 주 피해 한 번(무기 계수는 지금 든 무기 · 계수 칸 비움).</summary>
    private static void DealAs(PlayerController p, MonsterBase m, WeaponActionType kind, bool ranged = false)
    {
        if (m == null || m.IsDead) return;
        CombatDamage.Deal(new CombatDamage.Request
        {
            Target = m.gameObject, BaseDamage = 3f, Owner = p.gameObject, ActionType = kind, IsRanged = ranged,
            KnockbackMultiplier = 0f, HitPoint = m.transform.position + Vector3.up, SourcePosition = p.transform.position, SkipHitVfx = true,
        });
    }

    /// <summary>플레이어 이름으로 주 피해 한 번 — 기본 피해를 정한다(화상 세기 비교용).</summary>
    private static void DealAsBase(PlayerController p, MonsterBase m, float baseDamage)
    {
        if (m == null || m.IsDead) return;
        CombatDamage.Deal(new CombatDamage.Request
        {
            Target = m.gameObject, BaseDamage = baseDamage, Owner = p.gameObject, ActionType = WeaponActionType.GroundLight,
            KnockbackMultiplier = 0f, HitPoint = m.transform.position + Vector3.up, SourcePosition = p.transform.position, SkipHitVfx = true,
        });
    }

    /// <summary>각인 수를 실측 동안만 바꾼다(다음 Recount가 되돌린다).</summary>
    private static void ForceImprint(BuildFamily f, int count)
    {
        if (typeof(BuildImprint).GetField("s_counts", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) is int[] c) c[(int)f] = count;
    }

    private static async UniTask BuildSynergy2Async(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        sb.AppendLine("빌드 시너지 계획 2 — 바뀐 칸 7칸(유물 없음)");
        var rs  = p.RuntimeStats;
        var run = GameRunBootstrapper.Instance.Run;
        var disp = p.RuneEffects;
        Vector3 fw = Flat(p.transform.forward);
        s_home = p.transform.position;
        foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
            if (m != null && !m.IsDead) m.gameObject.SetActive(false);
        var t = await TrackAsync(p.transform.position + fw * 2f, ct);
        if (t == null) { sb.AppendLine("슬라임 부르기 실패"); return; }
        await EquipAsync(p, Great, ct);   // 대검 = 발동 계수 1.6 → 확률 100% · 모든 평타가 강타
        await Wait(0.3f, ct);
        var st = t.M.Status;

        // ① 독 — 같은 적 10타 → 8중첩에서 멈춤 · 틱 피해 = 중첩당 × 8
        {
            var poison = new PoisonOnHitEffect(new ItemEffectSlot { effectType = "PoisonOnHit", trigger = "Always", value = 1f, value2 = 0.05f, value3 = 1f, duration = 3f });
            poison.SetElement("GRASS");
            InjectEffect(poison);
            float per1 = -1f;
            for (int i = 0; i < 10; i++) { DealAs(p, t.M, WeaponActionType.GroundLight); RefillAll(); if (i == 0) per1 = st.GetDotDamagePerTick("item_poison"); }
            int stacks = st.GetDotStacks("item_poison");
            float per = st.GetDotDamagePerTick("item_poison");
            RemoveEffect(poison);
            Check(sb, $"① 독 8중첩 — 10타 뒤 중첩 {stacks}(= 8) · 틱 피해 {per:0.0} = 1중첩 {per1:0.0} × {(per1 > 0f ? per / per1 : 0f):0.0}", stacks == 8 && per1 > 0f && Mathf.Abs(per / per1 - 8f) < 0.05f);
        }

        // ② 화상 — 센 한 방(기본 300) 뒤 약한 타(기본 3) → 틱 피해는 센 쪽 유지 · 더 센 한 방(600)이면 갈아 끼움
        {
            var burn = new PoisonOnHitEffect(new ItemEffectSlot { effectType = "PoisonOnHit", trigger = "Always", value = 1f, value2 = 0.25f, value3 = 1f, duration = 3f });
            burn.SetElement("FIRE");
            InjectEffect(burn);
            DealAsBase(p, t.M, 300f); RefillAll(); float big = st.GetDotDamagePerTick("item_burn");
            DealAsBase(p, t.M, 3f);   RefillAll(); float afterWeak = st.GetDotDamagePerTick("item_burn");
            DealAsBase(p, t.M, 600f); RefillAll(); float bigger = st.GetDotDamagePerTick("item_burn");
            RemoveEffect(burn);
            Check(sb, $"② 화상 가장 센 1개 — 센 한 방 {big:0} · 약한 타 뒤 {afterWeak:0}(그대로) · 더 센 한 방 {bigger:0}(갈아 끼움) · 중첩 {st.GetDotStacks("item_burn")}",
                  big > 0f && Mathf.Abs(afterWeak - big) < 0.5f && bigger > big + 0.5f);
        }

        // ③ 작열 — 점화가 같은 적에 3번 쌓이면 폭발(만료 아님): 잔불 1회마다 점화 · 2타 뒤 셈 2 · 3타째 폭발하며 셈 비움
        {
            disp.Activate(new RuneSynergyEntry { zone_id = "FIRE", threshold = 10, effect_type = "FireIgnite", value = 0.16f, value3 = 1f, max_stack = 3, duration = 3f });
            disp.Activate(new RuneSynergyEntry { zone_id = "FIRE", threshold = 19, effect_type = "FireScorch", value = 1.5f });
            var ignite = disp.GetActive("FireIgnite");
            var fCount = ignite?.GetType().GetField("_igniteCount", Inst);
            int CountOf() => fCount?.GetValue(ignite) is Dictionary<int, int> d && d.TryGetValue(t.M.GetInstanceID(), out int n) ? n : 0;
            DealAs(p, t.M, WeaponActionType.GroundLight); RefillAll();
            DealAs(p, t.M, WeaponActionType.GroundLight); RefillAll();
            int two = CountOf();
            DealAs(p, t.M, WeaponActionType.GroundLight); RefillAll();
            int three = CountOf();
            disp.Deactivate("FireScorch"); disp.Deactivate("FireIgnite");
            Check(sb, $"③ 작열 = 점화 3회 — 2타 뒤 점화 셈 {two}(= 2) · 3타 뒤 {three}(= 0 · 터지며 비움)", ignite != null && two == 2 && three == 0);
        }

        // ④ 정전기 상한 — 연격 각인 2단계(4)면 10 + 5 × 2 = 20
        {
            disp.Activate(new RuneSynergyEntry { zone_id = "ELECTRIC", threshold = 6, effect_type = "ElecStatic", value = 0.1f, value2 = 0.01f, max_stack = 10, duration = 10f });
            ForceImprint(BuildFamily.Combo, 4);
            for (int i = 0; i < 25; i++) { DealAs(p, t.M, WeaponActionType.GroundLight); RefillAll(); }
            int stat2 = disp.Resources?.GetStack("ElecStatic") ?? -1;
            disp.Deactivate("ElecStatic");
            run.EffectManager.Rebuild();   // 각인 다시 셈
            Check(sb, $"④ 정전기 상한 — 연격 2단계에서 25타 뒤 {stat2}(= 20)", stat2 == 20);
        }

        // ⑤ 광채 — 대검 평타(강타)로 쌓인다: 5타 → 5
        {
            disp.Activate(new RuneSynergyEntry { zone_id = "LIGHT", threshold = 6, effect_type = "LightRadiance", value = 0.01f, max_stack = 10 });
            for (int i = 0; i < 5; i++) { DealAs(p, t.M, WeaponActionType.GroundLight); RefillAll(); }
            int rad = disp.Resources?.GetStack("LightRadiance") ?? -1;
            disp.Deactivate("LightRadiance");
            Check(sb, $"⑤ 빛 광채 = 치명 또는 강타 — 대검 평타 5타 뒤 {rad}(= 5)", rad == 5);
        }

        // ⑥ 넘친 공속 — 공속 합 2.4 → 배율 2.0 · 모든 피해 +20%
        {
            var fBonus = typeof(PlayerRuntimeStats).GetField("_bonusAttackSpeed", Inst);
            float prev = fBonus != null ? (float)fBonus.GetValue(rs) : 0f;
            rs.SetZenithAttackSpeed(0f);
            int atk0 = rs.MeleeAttack;
            float raw0 = rs.AttackSpeedMultiplier;
            fBonus?.SetValue(rs, prev + (2.4f - raw0));
            rs.SetZenithAttackSpeed(0f);   // 다시 셈
            float asMul = rs.AttackSpeedMultiplier, over = rs.AttackSpeedOverflowDamage;
            int atk1 = rs.MeleeAttack;
            fBonus?.SetValue(rs, prev);
            rs.SetZenithAttackSpeed(0f);
            Check(sb, $"⑥ 넘친 공속 — 합 2.4 → 배율 {asMul:0.00}(= 2.0) · 넘친 피해 +{over * 100f:0}%(= 20) · 근접 공격력 {atk0} → {atk1}",
                  Mathf.Abs(asMul - 2f) < 0.01f && Mathf.Abs(over - 0.2f) < 0.01f && atk1 > atk0);
        }

        // ⑦ 표식 간격 — 연격 각인 3단계(6)면 0.3초 · 아니면 0.5초
        {
            run.EffectManager.Rebuild();
            float g0 = RelicMarkStatus.ApplyGap;
            ForceImprint(BuildFamily.Combo, 6);
            float g3 = RelicMarkStatus.ApplyGap;
            run.EffectManager.Rebuild();
            Check(sb, $"⑦ 유물 표식 대상 간격 — 연격 3단계 아님 {g0:0.0}초 · 3단계 {g3:0.0}초", Mathf.Abs(g0 - 0.5f) < 0.01f && Mathf.Abs(g3 - 0.3f) < 0.01f);
        }
        DetachAll();
    }

    private static async UniTask BuildSynergy3Async(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        sb.AppendLine("빌드 시너지 계획 3 — 바뀐 칸 7칸(유물 없음)");
        var run = GameRunBootstrapper.Instance.Run;
        Vector3 fw = Flat(p.transform.forward);
        s_home = p.transform.position;
        const BindingFlags SP = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

        // ① 유물 조각 × 계열 각인 — 그 계열 각인 2단계(4)에서 ImprintMul 1.4
        {
            RelicPartEntry e = null;
            foreach (var x in Managers.RelicParts.GetByRelic("gawain")) if (x != null && x.build_family == "Combo") { e = x; break; }
            string row = "연격 조각 없음"; bool ok = false;
            if (e != null)
            {
                var fx = RelicPartEffectFactory.Create(e.effect_key);
                var prop = fx?.GetType().GetProperty("ImprintMul", Inst);
                run.EffectManager.Rebuild();
                ForceImprint(BuildFamily.Combo, 0);
                float m0 = prop != null ? (float)prop.GetValue(fx) : -1f;
                ForceImprint(BuildFamily.Combo, 4);
                float m2 = prop != null ? (float)prop.GetValue(fx) : -1f;
                run.EffectManager.Rebuild();
                ok = Mathf.Abs(m0 - 1f) < 0.01f && Mathf.Abs(m2 - 1.4f) < 0.01f;
                row = $"{e.part_name}({e.effect_key}) 각인 0 {m0:0.00} · 2단계 {m2:0.00}";
            }
            Check(sb, "① 유물 조각 × 각인 — " + row, ok);
        }

        // ② 서약 × 원인 계열 각인 — 「연격」 원인 · 상한 없는 효과에서 연격 2단계면 × 1.4
        {
            string row = "효과 없음"; bool ok = false;
            if (CovenantPalette.TryGetCause("streak", out var cause))
                foreach (var id in CovenantPalette.EffectIds)
                {
                    if (!CovenantPalette.TryGetEffect(id, out var eff) || eff.cap > 0f || eff.mode == CovenantScaleMode.Count) continue;
                    run.EffectManager.Rebuild();
                    ForceImprint(BuildFamily.Combo, 0);
                    float v0 = CovenantMath.Effective(eff, CovenantTier.Silver, cause, CovenantTier.Silver);
                    ForceImprint(BuildFamily.Combo, 4);
                    float v2 = CovenantMath.Effective(eff, CovenantTier.Silver, cause, CovenantTier.Silver);
                    run.EffectManager.Rebuild();
                    ok = v0 > 0f && Mathf.Abs(v2 / v0 - 1.4f) < 0.01f;
                    row = $"연격 × {eff.name} — 각인 0 {v0:0.###} · 2단계 {v2:0.###}(× {(v0 > 0f ? v2 / v0 : 0f):0.00})";
                    break;
                }
            Check(sb, "② 서약 × 각인 — " + row, ok);
        }

        // ③ 격발 탄두 × 존 단계 — 불 존 2단계 · 4단계 피해 비 1.75 / 1.2
        {
            var burst = new TriggerBurstEffect(new ItemEffectSlot { effectType = "TriggerBurst", trigger = "OnCrit", value = 1f });
            burst.SetElement("FIRE");
            InjectEffect(burst);
            var ctx = typeof(ItemEffectManager).GetField("_ctx", Inst)?.GetValue(run.EffectManager) as ItemEffectContext;
            var wd = typeof(TriggerBurstEffect).GetMethod("WarheadDamage", Inst);
            var bridge = MerlinRuneBridge.Instance;
            var map = bridge != null ? typeof(MerlinRuneBridge).GetField("_appliedThresholds", Inst)?.GetValue(bridge) as Dictionary<string, HashSet<int>> : null;
            HashSet<int> saved = null;
            bool had = map != null && map.TryGetValue("FIRE", out saved);
            float D(int tier)
            {
                if (map == null) return -1f;
                map["FIRE"] = new HashSet<int>();
                for (int i = 0; i < tier; i++) map["FIRE"].Add(100 + i);
                return wd != null ? (float)wd.Invoke(burst, new object[] { ctx }) : -1f;
            }
            float d2 = D(2), d4 = D(4);
            if (map != null) { if (had) map["FIRE"] = saved; else map.Remove("FIRE"); }
            RemoveEffect(burst);
            Check(sb, $"③ 탄두 × 존 단계 — 불 2단계 {d2:0.0} · 4단계 {d4:0.0} · 비 {(d2 > 0f ? d4 / d2 : 0f):0.000}(= {1.75f / 1.2f:0.000})",
                  d2 > 0f && Mathf.Abs(d4 / d2 - 1.75f / 1.2f) < 0.01f);
        }

        // ④ 룬 보상 둘째 장 — 판에 불 룬 3 · 얼음 1을 두고 60번 굴려 둘째 장(일반 · 희귀) 불 비율 > 0.42(기대 ≈ 0.58)
        {
            var placed = typeof(RunItemInventory).GetField("_placedItems", Inst)?.GetValue(run.ItemInventory) as List<RuntimeItemData>;
            var added = new List<RuntimeItemData>();
            string[] ids = { "item_t1_keen_eye", "item_t1_destruction_sigil", "item_t1_enhanced_tendon", "item_t1_swift_charm" };
            string[] els = { "FIRE", "FIRE", "FIRE", "ICE" };
            for (int i = 0; i < ids.Length; i++)
            {
                var d = RuntimeItemData.FromServer(Managers.ItemData.GetItem(ids[i]));
                if (d == null || placed == null) continue;
                d.element = els[i]; placed.Add(d); added.Add(d);
            }
            var top = typeof(RoomClearGate).GetMethod("TopOwnedElement", SP)?.Invoke(null, null) as string;
            int n = 0, fire = 0, tries = 0;
            try
            {
                for (; n < 200; n++)
                {
                    tries++;
                    var probe = new RuntimeItemData { rarity = ItemRarity.Rare, element = "ICE" };
                    RoomClearGate.BiasOwnedElement(probe);
                    if (probe.element == "FIRE") fire++;
                }
            }
            finally
            {
                foreach (var x in added) placed?.Remove(x);
                run.EffectManager.Rebuild();
            }
            float ratio = n > 0 ? (float)fire / n : 0f;
            Check(sb, $"④ 룬 보상 둘째 장 — 보유 1위 {top} · 희귀 200장 중 불로 기운 {fire} = {ratio:0.00}(≈ 0.5 · 0.4 ~ 0.6)", top == "FIRE" && ratio > 0.4f && ratio < 0.6f);
        }

        // ⑤ 서약 제시 — 연격 1위(각인 4)에서 200번 굴려 원인 3장 중 연격 원인이 늘 1장 이상
        {
            run.EffectManager.Rebuild();
            ForceImprint(BuildFamily.Combo, 4);
            var rng = new System.Random(1234);
            int boards = 0, hit = 0;
            for (int i = 0; i < 200; i++)
            {
                CovenantAssembleService.DraftBoard(3, rng, false, new List<CovenantBase>(), out var causes, out _);
                if (causes == null) continue;
                boards++;
                foreach (var c in causes)
                    if (CovenantPalette.TryGetCause(c.id, out var d) && BuildFamilyRules.FromCause(d.cls) == BuildFamily.Combo) { hit++; break; }
            }
            run.EffectManager.Rebuild();
            Check(sb, $"⑤ 서약 제시 보유 우대 — {boards}판 중 연격 원인 포함 {hit}판", boards > 0 && hit == boards);
        }

        // ⑥ 겨눔 — 원거리로 같은 적 3번 → 1회 · 근접 3번 → 0회
        {
            int fired = 0; string row = "원인 없음"; bool ok = false;
            if (CovenantPalette.TryGetCause("aim", out var aim))
            {
                var tr = new CovenantCauseTracker(aim, null, _ => fired++);
                var dummy = p.gameObject;
                CovenantCauseTracker.CurrentHitRanged = false;
                for (int i = 0; i < 3; i++) tr.OnAttackHit(dummy);
                int melee = fired;
                CovenantCauseTracker.CurrentHitRanged = true;
                for (int i = 0; i < 3; i++) tr.OnAttackHit(dummy);
                CovenantCauseTracker.CurrentHitRanged = false;
                int ranged = fired - melee;
                ok = melee == 0 && ranged == 1 && BuildFamilyRules.FromCause(aim.cls) == BuildFamily.Ranged;
                row = $"근접 3번 {melee}회 · 원거리 3번 {ranged}회 · 계열 {BuildFamilyRules.Label(BuildFamilyRules.FromCause(aim.cls))}";
            }
            Check(sb, "⑥ 서약 「겨눔」 — " + row, ok);
        }

        // ⑦ 시작 파츠 성향 — 이번 런 원거리 무기가 활이면 분열, 석궁이면 거력이 시작 파츠보다 1 높다
        {
            var bowSo  = AssetDatabase.LoadAssetAtPath<WeaponSO>(Bow);
            var xbowSo = AssetDatabase.LoadAssetAtPath<WeaponSO>(Crossbow);
            bool famBow  = bowSo  != null && BuildFamilyRules.OfWeapon(WeaponData.FromSO(bowSo))  == BuildFamily.Ranged;
            bool famXbow = xbowSo != null && BuildFamilyRules.OfWeapon(WeaponData.FromSO(xbowSo)) == BuildFamily.Ranged;
            var a = new RangedPartsState(); a.ApplyWeaponBias(WeaponType.Bow);
            var b = new RangedPartsState(); b.ApplyWeaponBias(WeaponType.Crossbow);
            int split = a.LevelOf("part_split"), power = b.LevelOf("part_power");
            Check(sb, $"⑦ 활 · 석궁 = 사격 · 시작 파츠 성향 — 계열 활 {famBow} · 석궁 {famXbow} · 새 런 활 분열 Lv {split}(= 1) · 석궁 거력 Lv {power}(= 1)",
                  famBow && famXbow && split == 1 && power == 1);
        }
    }

    /// <summary>K4(10-14 D1) — 활 · 석궁 R 스킬 교환 뒤 각 R이 제 무기로 쏘고 맞히는지 · 장면.</summary>
    private static async UniTask SkillSwapK4Async(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        sb.AppendLine("활 · 석궁 R 스킬 교환 확인(K4 · D1)");
        Vector3 fw = Flat(p.transform.forward);
        s_home = p.transform.position;
        foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
            if (m != null && !m.IsDead) m.gameObject.SetActive(false);
        var t = await TrackAsync(p.transform.position + fw * 5f, ct);
        if (t == null) { sb.AppendLine("슬라임 부르기 실패"); return; }
        var spy = new ReportSpy();
        HubGui("HideTestHubGui");
        try
        {
            foreach (var (path, label, wantSkill) in new[] { (Crossbow, "석궁", "궁수의 집중"), (Bow, "활", "더 빨리쏘기") })
            {
                if (!await EquipAsync(p, path, ct)) { Check(sb, label + " 장착", false); continue; }
                await Wait(0.5f, ct);
                var w = p.WeaponManager.CurrentWeaponData;
                string rName = w?.skillQ != null ? w.skillQ.skillName : "(없음)";
                InjectEffect(spy); spy.Seen.Clear();
                RefillAll(); t.Dealt = 0f;
                Steer(p, t.M.transform.position);
                p.CooldownTracker.ResetCooldown(SkillType.R);
                p.InputBuffer.Push(Command.RSkill);
                string tag = path == Crossbow ? "xbow" : "bow";
                await ShotUi($"K4_{tag}_1", 200);
                await ShotUi($"K4_{tag}_2", 500);
                await ShotUi($"K4_{tag}_3", 600);
                // 활(더 빨리쏘기 = 5초 버프)은 이어 평타 몇 번 — 추가 화살이 나가는지
                if (path == Bow)
                    for (int i = 0; i < 6; i++) { Steer(p, t.M.transform.position); p.InputBuffer.Push(Command.Light); await Wait(0.35f, ct); }
                await Wait(1.2f, ct);
                int rHits = spy.Seen.FindAll(r => r.ActionType == WeaponActionType.RSkill && r.DamageDealt > 0f).Count;
                int lHits = spy.Seen.FindAll(r => r.ActionType == WeaponActionType.GroundLight && r.DamageDealt > 0f).Count;
                bool heavy = spy.Seen.Exists(r => r.ActionType == WeaponActionType.RSkill && r.IsHeavy);
                RemoveEffect(spy);
                bool ok = rName == wantSkill && (path == Crossbow ? rHits > 0 && heavy : lHits > 0) && t.Dealt > 0f;
                Check(sb, $"{label} R = {rName}(원함 {wantSkill}) · R 맞힘 {rHits}(강타 {heavy}) · 평타 맞힘 {lHits} · 피해 {t.Dealt:0} · 사진 Temp/ui_shots/K4_{tag}_1~3", ok);
                await Wait(1f, ct);
            }
        }
        finally { HubGui("RestoreTestHubGui"); DetachAll(); }
    }

    private static async UniTask BuildSynergy5Async(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        sb.AppendLine("빌드 시너지 계획 5 — 룬 재설계 새 효과 7칸(유물 없음)");
        var rs  = p.RuntimeStats;
        var run = GameRunBootstrapper.Instance.Run;
        var ctx = typeof(ItemEffectManager).GetField("_ctx", Inst)?.GetValue(run.EffectManager) as ItemEffectContext;
        Vector3 fw = Flat(p.transform.forward), right = Vector3.Cross(Vector3.up, fw);
        s_home = p.transform.position;
        foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
            if (m != null && !m.IsDead) m.gameObject.SetActive(false);
        await EquipAsync(p, Nameless, ct);
        await Wait(0.3f, ct);
        var t = await TrackAsync(p.transform.position + fw * 2f, ct);
        if (t == null) { sb.AppendLine("슬라임 부르기 실패"); return; }
        IItemEffect Rune(string type, string trigger, float v, float v2 = 0f, float v3 = 0f, float dur = 0f)
        {
            var e = ItemEffectRegistry.Create(new ItemEffectSlot { effectType = type, trigger = trigger, value = v, value2 = v2, value3 = v3, duration = dur });
            if (e != null) InjectEffect(e);
            return e;
        }

        // ① 맞서는 분노 — 정예에게 +15% · 잡몹은 그대로
        {
            MonsterBase elite = null;
            try { elite = await Managers.ObjectPooler.SpawnAsync<MonsterBase>("Werewolf/Werewolf", ObjectPoolerManager.PoolType.Monster, p.transform.position + fw * 8f, Quaternion.identity); }
            catch (Exception) { }
            float g0 = Time.realtimeSinceStartup;   // 등급(설정)은 소환 직후엔 비어 Common — 들 때까지
            while (elite != null && elite.Grade != MonsterGrade.Elite && Time.realtimeSinceStartup - g0 < 4f) await UniTask.Yield(ct);
            var fx = Rune("TargetGradeBonus", "DuringBoss", 0.15f);
            var a = new DamagePacket(100f, p.gameObject, t.M.gameObject);
            var b = new DamagePacket(100f, p.gameObject, elite != null ? elite.gameObject : null);
            fx?.OnPreDealDamage(ctx, ref a);
            if (elite != null) fx?.OnPreDealDamage(ctx, ref b);
            RemoveEffect(fx);
            if (elite != null) elite.gameObject.SetActive(false);
            Check(sb, $"① 맞서는 분노 — 잡몹 {a.FinalDamage:0} · 정예 {b.FinalDamage:0}(= 115)", elite != null && Mathf.Abs(a.FinalDamage - 100f) < 0.5f && Mathf.Abs(b.FinalDamage - 115f) < 0.5f);
        }

        // ② 질주하는 분노 — 6 m 움직이면 다음 일격 +60% 한 번
        {
            var fx = Rune("MoveChargeNextHit", "WhileMoving", 0.6f, 6f);
            Vector3 home = p.transform.position;
            fx.OnTick(ctx, 0f);
            p.transform.position = home + right * 3.5f; fx.OnTick(ctx, 0.1f);
            p.transform.position = home;                fx.OnTick(ctx, 0.1f);
            var a = new DamagePacket(100f, p.gameObject, t.M.gameObject); fx.OnPreDealDamage(ctx, ref a);
            var b = new DamagePacket(100f, p.gameObject, t.M.gameObject); fx.OnPreDealDamage(ctx, ref b);
            RemoveEffect(fx);
            Steer(p, t.M.transform.position);
            Check(sb, $"② 질주하는 분노 — 7 m 뒤 첫 일격 {a.FinalDamage:0}(= 160) · 다음 {b.FinalDamage:0}(= 100)", Mathf.Abs(a.FinalDamage - 160f) < 0.5f && Mathf.Abs(b.FinalDamage - 100f) < 0.5f);
        }

        // ③ 꿰찌르는 질풍 — 치명 3번 → 치명 피해 +75%(최대 3중첩)
        {
            var fx = Rune("CritDamageStack", "AfterCrit", 0.25f, 3f, 0f, 3f);
            var crit = new DamageReport { DamageDealt = 10f, IsCrit = true, Target = t.M.gameObject, Attacker = p.gameObject };
            for (int i = 0; i < 4; i++) fx.OnPostDealDamage(ctx, crit);
            var dyn = new ItemDynamicStats();
            fx.ContributeDynamicStats(ctx, ref dyn);
            RemoveEffect(fx);
            Check(sb, $"③ 꿰찌르는 질풍 — 치명 4번 뒤 치명 피해 +{dyn.critDamage * 100f:0}%(= 75)", Mathf.Abs(dyn.critDamage - 0.75f) < 0.01f);
        }

        // ④ 차오르는 수호(위기) — 맞은 뒤 3초 받는 피해 −15%
        {
            float dr0 = rs.DamageReduction;
            var fx = Rune("CondDamageReduction", "AfterHit", 0.15f, 0f, 0f, 3f);
            fx.OnPostTakeDamage(ctx, new DamageReport { DamageDealt = 1f, Target = p.gameObject });
            await Wait(0.3f, ct);   // 효과 관리자가 동적 칸을 밀어 넣는다
            float dr1 = rs.DamageReduction;
            RemoveEffect(fx);
            await Wait(0.3f, ct);
            Check(sb, $"④ 차오르는 수호 — 받는 피해 감소 {dr0 * 100f:0}% → 맞은 뒤 {dr1 * 100f:0}%(+15)", Mathf.Abs(dr1 - dr0 - 0.15f) < 0.01f);
        }

        // ⑤ 덮치는 그림자 — 구르기 뒤 첫 타(무형검 계수 0.7)가 강타
        {
            var fx = Rune("FirstAttackAfterRoll", "Always", 0.8f, 0f, 1f);
            var spy = new ReportSpy(); InjectEffect(spy);
            fx.OnRollEnd(ctx);
            DealAs(p, t.M, WeaponActionType.GroundLight); RefillAll();
            bool first = spy.Seen.Count > 0 && spy.Seen[0].IsHeavy;
            DealAs(p, t.M, WeaponActionType.GroundLight); RefillAll();
            bool second = spy.Seen.Count > 1 && spy.Seen[1].IsHeavy;
            RemoveEffect(spy); RemoveEffect(fx);
            Check(sb, $"⑤ 덮치는 그림자 — 구르기 뒤 첫 타 강타 {first} · 다음 타 {second}(거짓)", first && !second);
        }

        // ⑥ 감싸는 성광 — 치명으로 쓰러뜨리면 폭발(치명 아닌 처치는 아님)
        {
            var burst = Rune("TriggerBurst", "OnCritKill", 1f, 0f, 2.5f) as TriggerBurstEffect;
            burst?.SetElement("LIGHT");
            var fIcd = typeof(TriggerBurstEffect).GetField("_icdUntil", Inst);
            var k1 = await TrackAsync(p.transform.position + fw * 2f + right * 1.5f, ct);
            SetHp(k1.M, 1); DealAs(p, k1.M, WeaponActionType.GroundLight);   // 치명 아닐 수 있다 — 무치명이면 안 터져야
            bool crit1 = false;
            float icd1 = fIcd != null ? (float)fIcd.GetValue(burst) : -1f;
            var k2 = await TrackAsync(p.transform.position + fw * 2f - right * 1.5f, ct);
            rs.ArmForceCrit(0f);
            SetHp(k2.M, 1); DealAs(p, k2.M, WeaponActionType.GroundLight); crit1 = true;
            float icd2 = fIcd != null ? (float)fIcd.GetValue(burst) : -1f;
            RemoveEffect(burst);
            Check(sb, $"⑥ 감싸는 성광(치명 처치) — 확정 치명 처치 뒤 터짐 {icd2 > icd1}", crit1 && icd2 > icd1);
        }

        // ⑦ 가벼운 그림자 — 구르기 길 위 적에게 0.5초 뒤 잔상 피해
        {
            var fx = Rune("DashAfterimage", "OnDash", 0.8f);
            Vector3 home = p.transform.position;
            Vector3 a = t.M.transform.position - right * 2f, b = t.M.transform.position + right * 2f;
            p.transform.position = a; fx.OnTick(ctx, 0.05f);
            await Wait(0.15f, ct);
            p.transform.position = b; fx.OnTick(ctx, 0.05f);
            RefillAll(); t.Dealt = 0f;
            fx.OnRollEnd(ctx);
            float tAt = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - tAt < 1f) { fx.OnTick(ctx, Time.deltaTime); await UniTask.Yield(ct); }
            float got = t.Dealt;
            RemoveEffect(fx);
            p.transform.position = home;
            Check(sb, $"⑦ 가벼운 그림자 — 길 위 슬라임 잔상 피해 {got:0}(> 0)", got > 0f);
        }
        DetachAll();
    }

    private static async UniTask BuildSynergy6Async(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        sb.AppendLine("빌드 시너지 계획 6 — 유물 진화 7칸");
        var run = GameRunBootstrapper.Instance.Run;
        var lo  = AppBootstrapper.Instance.Loadout;
        string relicId = lo.Relic != null ? lo.Relic.Id.ToString().ToLower() : null;
        if (relicId != "gawain") { sb.AppendLine($"유물이 가웨인이 아니다({relicId}) — 실측 안 함"); return; }
        string savedIds = lo.SerializeRelicParts(), savedExtra = lo.SerializeRelicExtra();
        var clear = typeof(PlayerLoadout).GetMethod("ClearRelicMemory", Inst);
        Vector3 fw = Flat(p.transform.forward);
        s_home = p.transform.position;
        try
        {
            // 조각 4개(가웨인 · 반응 제외)
            int added = 0;
            foreach (var e in Managers.RelicParts.GetByRelic("gawain"))
            {
                if (added >= 4) break;
                if (e == null || e.IsReaction || lo.HasRelicPart(e.part_id)) continue;
                lo.AddRelicPart(e.part_id, RelicMemoryGrade.Faint); added++;
            }
            run.EffectManager.Rebuild();
            ForceImprint(BuildFamily.Combo, 4);

            // ① 레시피
            var ready = RelicEvolution.Ready(lo, "gawain");
            Check(sb, $"① 레시피 — 조각 {lo.RelicPartIds.Count} · 연격 각인 4 → 열린 진화 {ready?.Name}(= 타오르는 정오)", ready?.Id == "evo_gawain_combo");

            // ② 4번째 카드
            var all = Managers.RelicParts.GetByRelic("gawain");
            var draft = RelicDraftComposer.Compose("gawain", new List<RelicPartEntry>(all), lo, 0, 0, _ => false, new System.Random(7));
            var last = draft.Cards.Count > 0 ? draft.Cards[draft.Cards.Count - 1] : null;
            Check(sb, $"② 기억 카드 {draft.Cards.Count}장 · 마지막 {last?.Kind} {last?.EvolutionId}", draft.Cards.Count == 4 && last?.Kind == RelicDraftCardKind.Evolution);

            // ⑦ 카드 창 — 4장으로 그려지는지(사진)
            HubGui("HideTestHubGui");
            var popup = await Managers.UI.ShowPopupUIAndGetAsync<UI_RelicPartDraftPopup>();
            bool popupOk = false;
            if (popup != null)
            {
                popup.Setup(draft, false, "가웨인");
                await UniTask.Delay(900, ignoreTimeScale: true, cancellationToken: ct);
                await ShotUi("BS6_DraftEvolution", 300);
                foreach (var tx in popup.GetComponentsInChildren<TMPro.TMP_Text>(true)) if (tx.text.Contains("타오르는 정오")) { popupOk = true; break; }
                Managers.UI.CloseAllPopupUI();
                await UniTask.Delay(400, ignoreTimeScale: true, cancellationToken: ct);
            }
            HubGui("RestoreTestHubGui");
            Time.timeScale = 1f;

            // ③ 적용 — 진화 칸 · 연격 각인 +2
            run.EffectManager.Rebuild();
            int combo0 = BuildImprint.Count(BuildFamily.Combo);
            typeof(GameRunBootstrapper).GetMethod("ApplyRelicDraftCard", Inst)?.Invoke(GameRunBootstrapper.Instance, new object[] { last, lo });
            typeof(GameRunBootstrapper).GetMethod("ActivateRelicParts", Inst)?.Invoke(GameRunBootstrapper.Instance, new object[] { p });
            int combo1 = BuildImprint.Count(BuildFamily.Combo);
            Check(sb, $"③ 적용 — 진화 {lo.RelicEvolutionId} · 연격 각인 {combo0} → {combo1}(+2)", lo.RelicEvolutionId == "evo_gawain_combo" && combo1 == combo0 + 2);

            // ④ 저장 · 복원 — 덤 칸 evo=1
            string extra = lo.SerializeRelicExtra();
            string ids = lo.SerializeRelicParts();
            clear?.Invoke(lo, null);
            lo.RestoreRelicParts(ids, extra);
            Check(sb, $"④ 저장 덤 칸 「{extra}」 → 복원 진화 {lo.RelicEvolutionId}", extra.Contains("evo=1") && lo.RelicEvolutionId == "evo_gawain_combo");

            // ⑤ 효과 활성
            bool active = p.RuneEffects.Parts.IsActive("evo_gawain_combo");
            Check(sb, $"⑤ 진화 효과 활성 {active}", active);

            // ⑥ 정오 동안 적중마다 태양흔(같은 적 간격 없음) — 0.1초 안 3타 → 태양흔 3
            var t = await TrackAsync(p.transform.position + fw * 2f, ct);
            var relic = p.RelicBehavior as GawainZenithRelic;
            var gauge = relic != null ? typeof(GawainZenithRelic).GetField("_gauge", Inst)?.GetValue(relic) : null;
            gauge?.GetType().GetMethod("OpenNoonNow")?.Invoke(gauge, null);
            await Wait(0.3f, ct);
            var hub = GawainMemoryHub.Of(p);
            bool noon = hub != null && hub.IsNoon;
            for (int i = 0; i < 3; i++) { DealAs(p, t.M, WeaponActionType.GroundLight); RefillAll(); }
            var st = RelicMarkStatus.Of(t.M.gameObject, false);
            int marks = st != null ? st.Sunmark : -1;
            Check(sb, $"⑥ 타오르는 정오 — 정오 {noon} · 0.1초 안 3타 뒤 태양흔 {marks}(≥ 3 · 간격 0.5초면 1)", noon && marks >= 3);
            Check(sb, $"⑦ 기억 카드 창 4장 · 진화 카드 글 보임 {popupOk} · 사진 Temp/ui_shots/BS6_DraftEvolution", popupOk);
        }
        finally
        {
            clear?.Invoke(lo, null);
            lo.RestoreRelicParts(savedIds, savedExtra);
            run.EffectManager.Rebuild();
            DetachAll();
        }
    }

    /// <summary>계획 3b — 사격 조각 행(build_family Ranged)을 프로젝트 JSON에서 읽어 이번 플레이 동안만 등록한다(CDN 업로드 전).</summary>
    private static int RegisterRangedFragmentRows()
    {
        var ta  = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/RelicFairy/Systems/RelicParts/RELIC_PARTS_DATA.json");
        var col = ta != null ? JsonUtility.FromJson<RelicPartEntryCollection>(ta.text) : null;
        var reg = typeof(RelicPartsDataManager).GetMethod("Register", Inst);
        if (col?.entries == null || reg == null) return 0;
        int n = 0;
        foreach (var e in col.entries)
            if (e != null && e.build_family == "Ranged") { reg.Invoke(Managers.RelicParts, new object[] { e }); n++; }
        return n;
    }

    /// <summary>계획 3b 앞뒤 — 기억을 비우고 사격 조각만 찬란으로 들인다.</summary>
    private static void GrantRangedFragments(PlayerController p, params string[] ids)
    {
        var lo = AppBootstrapper.Instance.Loadout;
        typeof(PlayerLoadout).GetMethod("ClearRelicMemory", Inst)?.Invoke(lo, null);
        foreach (var id in ids) lo.AddRelicPart(id, RelicMemoryGrade.Radiant);
        GameRunBootstrapper.Instance.Run.EffectManager.Rebuild();
        typeof(GameRunBootstrapper).GetMethod("ActivateRelicParts", Inst)?.Invoke(GameRunBootstrapper.Instance, new object[] { p });
    }

    private static int Sunmark(MonsterBase m) { var st = m != null ? RelicMarkStatus.Of(m.gameObject, false) : null; return st != null ? st.Sunmark : 0; }

    private static async UniTask BuildSynergy3bGawainAsync(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        sb.AppendLine("빌드 시너지 계획 3b — 사격 조각(가웨인) 4칸 · 찬란");
        var lo = AppBootstrapper.Instance.Loadout;
        string relicId = lo.Relic != null ? lo.Relic.Id.ToString().ToLower() : null;
        if (relicId != "gawain") { sb.AppendLine($"유물이 가웨인이 아니다({relicId}) — 실측 안 함"); return; }
        string savedIds = lo.SerializeRelicParts(), savedExtra = lo.SerializeRelicExtra();
        Vector3 fw = Flat(p.transform.forward), right = Vector3.Cross(Vector3.up, fw);
        s_home = p.transform.position;
        foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
            if (m != null && !m.IsDead) m.gameObject.SetActive(false);
        await EquipAsync(p, Bow, ct);   // 무기 없이 시작하면 공격력 ≈ 0 — 조각 피해가 0으로 잰다
        try
        {
            // ⑦ 데이터 — 사격 행 4개 · 자리 · 계열 · 효과 활성
            int reg = RegisterRangedFragmentRows();
            GrantRangedFragments(p, "g_sun_arrow", "g_dusk_hunter");
            var parts = p.RuneEffects.Parts;
            var eS = Managers.RelicParts.GetById("g_sun_arrow"); var eD = Managers.RelicParts.GetById("g_dusk_hunter");
            var eQ = Managers.RelicParts.GetById("l_quarry_brand"); var eJ = Managers.RelicParts.GetById("l_distant_judgment");
            bool rows = reg == 4 && eS?.anchor == "noon" && eD?.anchor == "dusk" && eQ?.anchor == "r10" && eJ?.anchor == "judgment";
            bool fam = BuildFamilyRules.OfRelicPart("g_sun_arrow") == BuildFamily.Ranged && BuildFamilyRules.OfRelicPart("l_distant_judgment") == BuildFamily.Ranged;
            bool act = parts.IsActive("g_sun_arrow") && parts.IsActive("g_dusk_hunter");
            Check(sb, $"⑦ 데이터 — 사격 행 {reg}(= 4) · 자리 {eS?.anchor}/{eD?.anchor}/{eQ?.anchor}/{eJ?.anchor} · 계열 사격 {fam} · 가웨인 조각 활성 {act}", rows && fam && act);

            var hub   = GawainMemoryHub.Of(p);
            var relic = p.RelicBehavior as GawainZenithRelic;
            var gauge = relic != null ? typeof(GawainZenithRelic).GetField("_gauge", Inst)?.GetValue(relic) : null;
            // 표적을 먼저 부른다 — 소환(무적 대기)이 정오를 다 써 버렸다(1회차: 첫 두 화살이 정오 밖)
            var t = await TrackAsync(p.transform.position + fw * 3f, ct);
            await Wait(0.3f, ct);
            gauge?.GetType().GetMethod("OpenNoonNow")?.Invoke(gauge, null);
            await Wait(0.2f, ct);

            // ① 햇살 화살 — 정오: 근접 1타는 태양흔 0(정오엔 기본 쌓기 없음) · 원거리 0.1초 안 2타 → 2(같은 적 간격 무시)
            bool noon = hub != null && hub.IsNoon;
            DealAs(p, t.M, WeaponActionType.GroundLight); RefillAll();
            int melee = Sunmark(t.M);
            t.Dealt = 0f;
            for (int i = 0; i < 2; i++) { DealAs(p, t.M, WeaponActionType.GroundLight, ranged: true); RefillAll(); }
            int ranged2 = Sunmark(t.M);
            float plain = t.Dealt / 2f;   // 원거리 1타 피해(개화 · 조각 없이)
            Check(sb, $"① 햇살 화살 — 정오 {noon} · 근접 1타 태양흔 {melee}(= 0) · 원거리 2타 뒤 {ranged2}(= 2 · 간격 0.5초면 1)", noon && melee == 0 && ranged2 == 2);

            // ② 햇살 화살 ③ — 셋째 원거리로 상한 3 → 그 자리 개화(태양흔 0 · 피해) · 2초 안 다시 상한이면 개화 없음(3 남음)
            t.Dealt = 0f;
            DealAs(p, t.M, WeaponActionType.GroundLight, ranged: true); RefillAll();
            int afterBloom = Sunmark(t.M);
            float bloomDmg = t.Dealt;
            for (int i = 0; i < 3; i++) { DealAs(p, t.M, WeaponActionType.GroundLight, ranged: true); RefillAll(); }
            int held = Sunmark(t.M);
            Check(sb, $"② 햇살 화살 ③ — 상한에서 개화 뒤 태양흔 {afterBloom}(= 0) · 피해 {bloomDmg:0}(> 원거리 1타 {plain:0} × 2) · 2초 안 다시 상한 {held}(= 3 · 개화 없음)", afterBloom == 0 && bloomDmg > plain * 2f && held == 3);

            // ③ 노을 사냥꾼 — 황혼: A(태양흔 2)를 원거리로 처치 → 그 자리 불씨 · 1.5 m 곁 B에 태양흔 2가 옮음 · 불씨 안 B 원거리 적중 → +1
            gauge?.GetType().GetField("_timer", Inst)?.SetValue(gauge, 999f);   // 정오 끝 → 다음 틱에 황혼
            await Wait(0.3f, ct);
            bool dusk = hub != null && hub.IsDusk;
            t.M.gameObject.SetActive(false);   // 옮겨 갈 곁 적은 B 하나만(4 m 안 1마리 — 겹침 순서는 정해져 있지 않다)
            var a = await TrackAsync(p.transform.position + fw * 4f + right * 2f, ct);
            var b = await TrackAsync(p.transform.position + fw * 4f + right * 3.5f, ct);
            hub?.AddSunmark(a.M, 2, ignoreGap: true);
            SetHp(a.M, 1);
            DealAs(p, a.M, WeaponActionType.GroundLight, ranged: true);
            await Wait(0.1f, ct);
            bool killed = a.M.IsDead;
            int carried = Sunmark(b.M);
            int fields = 0;
            foreach (var ff in UnityEngine.Object.FindObjectsByType<FireField>(FindObjectsSortMode.None))
            {
                if (ff == null || !ff.IsActive) continue;
                Vector3 dd = ff.Center - a.Pin; dd.y = 0f;
                if (dd.magnitude < 0.6f) fields++;
            }
            await Wait(0.6f, ct);   // 태양흔 같은 적 간격(0.5초) 뒤
            DealAs(p, b.M, WeaponActionType.GroundLight, ranged: true); RefillAll();
            int inEmber = Sunmark(b.M);
            Check(sb, $"③ 노을 사냥꾼 — 황혼 {dusk} · 원거리 처치 {killed} · 그 자리 불씨 {fields}(≥ 1) · 곁 적 태양흔 {carried}(= 2) · 불씨 안 원거리 적중 뒤 {inEmber}(= 3)",
                  dusk && killed && fields >= 1 && carried == 2 && inEmber == 3);
        }
        finally
        {
            var clear = typeof(PlayerLoadout).GetMethod("ClearRelicMemory", Inst);
            clear?.Invoke(lo, null);
            lo.RestoreRelicParts(savedIds, savedExtra);
            GameRunBootstrapper.Instance.Run.EffectManager.Rebuild();
            DetachAll();
        }
    }

    private static async UniTask BuildSynergy3bLancelotAsync(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        sb.AppendLine("빌드 시너지 계획 3b — 사격 조각(랜슬롯) 3칸 · 찬란");
        var lo = AppBootstrapper.Instance.Loadout;
        string relicId = lo.Relic != null ? lo.Relic.Id.ToString().ToLower() : null;
        if (relicId != "lancelot") { sb.AppendLine($"유물이 랜슬롯이 아니다({relicId}) — 실측 안 함"); return; }
        string savedIds = lo.SerializeRelicParts(), savedExtra = lo.SerializeRelicExtra();
        Vector3 fw = Flat(p.transform.forward);
        s_home = p.transform.position;
        foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
            if (m != null && !m.IsDead) m.gameObject.SetActive(false);
        await EquipAsync(p, Bow, ct);   // 무기 없이 시작하면 공격력 ≈ 0
        try
        {
            RegisterRangedFragmentRows();
            GrantRangedFragments(p, "l_quarry_brand", "l_distant_judgment");
            var hub = LancelotMemoryHub.Of(p);
            if (hub == null || hub.Madness == null) { sb.AppendLine("랜슬롯 허브 · 광기 없음"); return; }

            // ④ 표적의 낙인 — 광기 10: 근접 3타 낙인 0 · 같은 적 원거리 3타 → 1 · 6타 → 2
            var t = await TrackAsync(p.transform.position + fw * 3f, ct);
            hub.Madness.SetStacks(10);
            for (int i = 0; i < 3; i++) { DealAs(p, t.M, WeaponActionType.GroundLight); RefillAll(); }
            int melee = LancelotMemoryHub.BrandOf(t.M);
            hub.Madness.SetStacks(10);
            for (int i = 0; i < 3; i++) { DealAs(p, t.M, WeaponActionType.GroundLight, ranged: true); RefillAll(); }
            int three = LancelotMemoryHub.BrandOf(t.M);
            for (int i = 0; i < 3; i++) { DealAs(p, t.M, WeaponActionType.GroundLight, ranged: true); RefillAll(); }
            int six = LancelotMemoryHub.BrandOf(t.M);
            t.Dealt = 0f;
            DealAs(p, t.M, WeaponActionType.GroundLight); RefillAll();
            float plain = t.Dealt;   // 근접 1타 — 연속 셈을 건드리지 않는 대조 피해
            Check(sb, $"④ 표적의 낙인 — 광기 {hub.Stacks}(10 ~ 19 · 계단 2 아래) · 근접 3타 낙인 {melee}(= 0) · 원거리 3타 {three}(= 1) · 6타 {six}(= 2)", melee == 0 && three == 1 && six == 2);

            // ⑤ 표적의 낙인 ③ — 상한까지 채운 적을 원거리로 → 낙인 1개 찢김(상한 − 1) · X자 피해
            hub.Madness.SetStacks(10);
            hub.FillBrand(t.M);
            int cap = LancelotMemoryHub.BrandOf(t.M);
            t.Dealt = 0f;
            DealAs(p, t.M, WeaponActionType.GroundLight, ranged: true); RefillAll();   // 7번째 — 연속 셈은 3의 배수가 아니라 새기지 않는다
            int afterTear = LancelotMemoryHub.BrandOf(t.M);
            Check(sb, $"⑤ 표적의 낙인 ③ — 상한 {cap} → 원거리 1타 뒤 {afterTear}(= 상한 − 1) · 피해 {t.Dealt:0}(> 1타 {plain:0} — X자 몫)", cap == hub.BrandCap && afterTear == cap - 1 && t.Dealt > plain + 1f);

            // ⑥ 멀리서 내리는 심판 — 막타 사건을 직접 불러: 앞 6 m · 18° 9 m 적은 맞고 낙인 +1 · 뒤 4 m 적은 안 맞음
            t.M.gameObject.SetActive(false);
            var front = await TrackAsync(p.transform.position + fw * 6f, ct);
            var side  = await TrackAsync(p.transform.position + Quaternion.Euler(0f, LancelotMemoryHub.WaveSpreadDeg, 0f) * fw * 9f, ct);
            var back  = await TrackAsync(p.transform.position - fw * 4f, ct);
            await Wait(0.3f, ct);   // 막 소환한 적은 물리 위치가 아직 옛 자리 — 겹침 조회가 못 찾는다
            front.Dealt = side.Dealt = back.Dealt = 0f;
            IRelicPartEffect fx = null;
            foreach (var e in p.RuneEffects.Parts.Active) if (e.EffectKey == "l_distant_judgment") { fx = e; break; }
            fx?.GetType().GetMethod("OnFinisher", Inst)?.Invoke(fx, new object[] { p.transform.position, fw });
            await Wait(0.1f, ct);
            int bf = LancelotMemoryHub.BrandOf(front.M), bs = LancelotMemoryHub.BrandOf(side.M), bb = LancelotMemoryHub.BrandOf(back.M);
            Check(sb, $"⑥ 멀리서 내리는 심판 — 효과 {fx != null} · 앞 6 m 피해 {front.Dealt:0} 낙인 {bf} · 옆 줄 9 m 피해 {side.Dealt:0} 낙인 {bs} · 뒤 피해 {back.Dealt:0} 낙인 {bb}(= 0)",
                  fx != null && front.Dealt > 0f && bf == 1 && side.Dealt > 0f && bs == 1 && back.Dealt == 0f && bb == 0);
        }
        finally
        {
            typeof(PlayerLoadout).GetMethod("ClearRelicMemory", Inst)?.Invoke(lo, null);
            lo.RestoreRelicParts(savedIds, savedExtra);
            GameRunBootstrapper.Instance.Run.EffectManager.Rebuild();
            DetachAll();
        }
    }

    // ── §5 빌드 4종 초당 피해(10-14 빌드 시너지 검증) ───────────────────
    // 같은 투자: 전설 1 · 영웅 2 · 희귀 2(속성 없는 일반 · 희귀 룬은 빌드 속성으로) · 계열 각인 4(강제) · 원거리는 성향 파츠 Lv 2 ·
    // 형 끔 · 강화 0 · 유물 없음. 룬은 판 칸 없이 놓아 존 단계는 넷 다 0(공평).
    // 목표(설계 §5): 단일 표적 초당 피해가 넷 평균의 ±20% 안 · 무리 배율(무리 ÷ 단일)은 활 · 카타나가 대검 · 석궁보다 크다.
    private static readonly (string label, string weapon, BuildFamily fam, string part, float range, string el, string[] ids)[] FourBuilds =
    {
        ("카타나 연타", Katana,   BuildFamily.Combo,  null,         1.6f, "ELECTRIC", new[] { "item_t4_fire_single",  "item_t3_elec_static",    "item_t3_fire_spark",    "item_t2_sharp_instinct", "item_t2_trig_same" }),
        ("대검 한 방",  Great,    BuildFamily.Crit,   null,         1.8f, "LIGHT",    new[] { "item_t4_light_single", "item_t3_light_radiance", "item_t3_light_sanctum", "item_t2_heavy_weight",   "item_t2_fierce_blade" }),
        ("활 다발",     Bow,      BuildFamily.Ranged, "part_split", 6f,   "GRASS",    new[] { "item_t4_grass_proj",   "item_t3_elec_arc",       "item_t3_grass_venom",   "item_t2_trig_ranged",    "item_t2_sharp_instinct" }),
        ("석궁 저격",   Crossbow, BuildFamily.Ranged, "part_power", 6f,   "ICE",      new[] { "item_t4_ice_single",   "item_t3_light_radiance", "item_t3_fire_ember",    "item_t2_heavy_weight",   "item_t2_aim_fury" }),
    };
    private const float FourBuildSeconds = 20f;

    /// <summary>프로젝트 ITEM_DATA.json(재설계 룬 · CDN 업로드 전 정본)에서 룬 하나를 만든다. 없으면 null.</summary>
    private static RuntimeItemData ProjectRune(string id)
    {
        var ta  = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/RelicFairy/Resources/ITEM_DATA.json");
        var col = ta != null ? JsonUtility.FromJson<ItemEntryCollection>(ta.text) : null;
        if (col?.items == null) return null;
        var rows = new List<ItemEntry>();
        foreach (var e in col.items)
        {
            if (e == null) continue;
            if (string.IsNullOrEmpty(e.item_id) && !string.IsNullOrEmpty(e.passive_id)) e.item_id = e.passive_id;
            if (e.ResolvedId == id) rows.Add(e);
        }
        return rows.Count > 0 ? RuntimeItemData.FromServer(rows) : null;
    }

    private static async UniTask FourBuildsAsync(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        sb.AppendLine($"빌드 시너지 §5 — 빌드 4종 초당 피해(같은 투자 · 단일 {FourBuildSeconds:0}초 + 무리 5마리 {FourBuildSeconds:0}초 · 유물 없음 · 형 끔 · 강화 0 · 룬 = 프로젝트 ITEM_DATA.json · 무기 = 차트 · 입력 0.1초)");
        var run    = GameRunBootstrapper.Instance.Run;
        var placed = typeof(RunItemInventory).GetField("_placedItems", Inst)?.GetValue(run.ItemInventory) as List<RuntimeItemData>;
        if (placed == null) { sb.AppendLine("룬 판 목록을 못 읽음"); return; }
        var savedPlaced = new List<RuntimeItemData>(placed);
        var parts = RangedPartsState.Current;
        var savedParts = new List<RangedPartsState.Equipped>(parts.Equipped_);
        var rows = new List<(string label, float single, float group)>();

        // 첫 장착 무기는 평타 0회로 잰다(R2 예열과 같은 함정 — 1회차 카타나 단일 0) → 무형검으로 2초 휘둘러 둔다
        if (await EquipAsync(p, Nameless, ct))
        {
            float w0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - w0 < 2f) { p.InputBuffer.Push(Command.Light); await Wait(0.25f, ct); }
            await Wait(1f, ct);
        }
        int hitCount = 0;
        float basicDmg = 0f;
        Action<HitInfo> onHit = h =>
        {
            if (h.Attacker != p.gameObject) return;
            hitCount++;
            if (h.ActionType == WeaponActionType.GroundLight) basicDmg += h.Damage;
        };

        string lastSplit = "";
        // 표적들을 겨누며 평타만 — 정착 2초 뒤 게임 시간으로 잰다(느린 에디터에서 실제 시간과 어긋나지 않게)
        async UniTask<float> MeasureAsync(List<HpTrack> ts, Vector3 aim)
        {
            float s0 = Time.time;
            while (Time.time - s0 < 2f) { RefillAll(); Steer(p, aim); p.InputBuffer.Push(Command.Light); await Wait(0.1f, ct); }
            foreach (var t in ts) t.Dealt = 0f;
            hitCount = 0; basicDmg = 0f;
            var stats = run.RunStats;
            long sb0 = stats.DamageOf(DamageSource.Basic), sr0 = stats.DamageOf(DamageSource.Rune), sd0 = stats.DamageOf(DamageSource.Dot), ss0 = stats.DamageOf(DamageSource.Skill);
            float t0 = Time.time;
            while (Time.time - t0 < FourBuildSeconds) { RefillAll(); Steer(p, aim); p.InputBuffer.Push(Command.Light); await Wait(0.1f, ct); }   // 연타하는 손(0.25초는 원거리를 깎았다)
            float secs = Mathf.Max(0.01f, Time.time - t0);
            lastSplit = $"평타 {(stats.DamageOf(DamageSource.Basic) - sb0) / secs:0} · 룬 {(stats.DamageOf(DamageSource.Rune) - sr0) / secs:0} · 지속 {(stats.DamageOf(DamageSource.Dot) - sd0) / secs:0} · 스킬 {(stats.DamageOf(DamageSource.Skill) - ss0) / secs:0}";
            float sum = 0f;
            foreach (var t in ts) sum += t.Dealt;
            return sum / Mathf.Max(0.01f, Time.time - t0);
        }

        HitFeedbackService.OnHit += onHit;
        try
        {
            foreach (var b in FourBuilds)
            {
                Managers.UI?.CloseAllPopupUI();
                Time.timeScale = 1f;
                DetachAll();
                foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
                    if (m != null && !m.IsDead) m.gameObject.SetActive(false);
                int slot = b.part != null ? PlayerWeaponManager.Slot1 : PlayerWeaponManager.Slot0;   // 원거리 파츠는 슬롯 1 화살에만(10-14 — 그 전 원거리 줄은 파츠 없이 쟀다)
                if (!await EquipAsync(p, b.weapon, ct, slot)) { sb.AppendLine($"장착 실패 {b.label}"); continue; }
                var w = slot == PlayerWeaponManager.Slot1 ? p.WeaponManager.Weapon1Data : p.WeaponManager.Weapon0Data;
                typeof(PlayerWeaponManager).GetMethod("ApplyServerOverrideIfAvailable", BindingFlags.Static | BindingFlags.NonPublic)
                    ?.Invoke(null, new object[] { w, Path.GetFileNameWithoutExtension(b.weapon) });   // 차트(EQUIPMENT_DATA)가 정본 — SO 값은 게임과 다르다
                w.enhanceLevel = 0;
                w.RecomputeEnhancedStats();
                w.formTier = 0;
                w.formVariants = "";
                WeaponFormRuntime.Of(p).Rebuild();
                p.WeaponManager.RaiseEquippedWeaponRefreshed();

                placed.Clear();
                var names = new List<string>();
                foreach (var id in b.ids)
                {
                    var d = ProjectRune(id);   // 런타임 데이터는 CDN 캐시(옛 룬)일 수 있다
                    if (d == null) { names.Add(id + "(없음)"); continue; }
                    if (string.IsNullOrEmpty(d.element)) d.element = b.el;
                    placed.Add(d); names.Add(d.displayName);
                }
                parts.Restore(b.part != null ? new[] { new RangedPartsState.Equipped { partId = b.part, level = 2 } } : Array.Empty<RangedPartsState.Equipped>());
                run.EffectManager.Rebuild();
                ForceImprint(b.fam, 4);
                await Wait(0.5f, ct);

                s_home = p.transform.position;
                sb.AppendLine($"   {b.label} 시작 상태 — {ActDump(p)}");
                Vector3 fw = Flat(p.transform.forward);
                Vector3 c = p.transform.position + fw * b.range;
                var one = await TrackAsync(c, ct);
                if (one == null) { sb.AppendLine($"슬라임 부르기 실패 {b.label}"); continue; }
                float single = await MeasureAsync(new List<HpTrack> { one }, one.M.transform.position);
                int singleHits = hitCount;
                string singleSplit = lastSplit;
                float singleBasic = basicDmg / Mathf.Max(0.01f, FourBuildSeconds);

                one.M.gameObject.SetActive(false);
                DetachAll();
                var grp = new List<HpTrack>();
                var center = await TrackAsync(c, ct);
                if (center != null) grp.Add(center);
                for (int k = 0; k < 4; k++)
                {
                    float a = k * Mathf.PI * 0.5f + Mathf.PI * 0.25f;
                    var t = await TrackAsync(c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 1.8f, ct);   // 1.2 m는 슬라임끼리 겹쳐 매 틱 되돌려져 화살이 빠졌다
                    if (t != null) grp.Add(t);
                }
                float group = await MeasureAsync(grp, c);
                int groupHits = hitCount;
                string groupSplit = lastSplit;
                var per = new StringBuilder();
                foreach (var t in grp) per.Append($"{t.Dealt:0} ");

                var rs = p.RuntimeStats;
                rows.Add((b.label, single, group));
                sb.AppendLine($"{b.label}: 단일 {single:0.0}/초(맞힘 {singleHits} · 평타 몫 {(single > 0f ? singleBasic / single : 0f):0%}) · 무리 {group:0.0}/초(표적 {grp.Count} · 맞힘 {groupHits} · 표적별 {per}) · 무리 배율 {(single > 0f ? group / single : 0f):0.00} · 공격력 {rs.AttackPower:0} · 공속 {rs.AttackSpeedMultiplier:0.00} · {BuildFamilyRules.Label(b.fam)} 각인 {BuildImprint.Count(b.fam)} · 무기 공격 {w.baseAttack} · 파츠 {b.part ?? "-"} · 룬 {string.Join(" / ", names)}");
                sb.AppendLine($"   출처(초당) — 단일: {singleSplit} | 무리: {groupSplit}");
                foreach (var t in grp) if (t.M != null) t.M.gameObject.SetActive(false);
            }
        }
        finally
        {
            HitFeedbackService.OnHit -= onHit;
            placed.Clear();
            placed.AddRange(savedPlaced);
            parts.Restore(savedParts);
            run.EffectManager.Rebuild();
            DetachAll();
        }

        if (rows.Count < 4) { Check(sb, $"빌드 4종 모두 잼 — {rows.Count}/4", false); return; }
        float mean = 0f;
        foreach (var r in rows) mean += r.single;
        mean /= rows.Count;
        var spread = new StringBuilder();
        bool within = true;
        foreach (var r in rows)
        {
            float k = mean > 0f ? r.single / mean : 0f;
            spread.Append($"{r.label} {k:0.00} · ");
            if (k < 0.8f || k > 1.2f) within = false;
        }
        Check(sb, $"① 단일 표적 — 평균 {mean:0.0}/초 대비 {spread}(0.80 ~ 1.20)", within);
        float Ratio(int i) => rows[i].single > 0f ? rows[i].group / rows[i].single : 0f;
        float multi = Mathf.Min(Ratio(0), Ratio(2)), heavy = Mathf.Max(Ratio(1), Ratio(3));
        Check(sb, $"② 무리 배율 — 카타나 {Ratio(0):0.00} · 활 {Ratio(2):0.00} 중 작은 값 {multi:0.00} > 대검 {Ratio(1):0.00} · 석궁 {Ratio(3):0.00} 중 큰 값 {heavy:0.00}", multi > heavy);
    }

    // ── 48 화살 멈칫(10-14) ─────────────────────────────────────
    // 타격마다 히트스톱(timeScale 0.10 ~ 0.13 · 0.02 ~ 0.09초)이 걸리고, 화살은 착탄이 프레임마다 흩어져 프레임 합산으로도 안 묶인다.
    // 연사 6초 동안 세계가 느려져 있던 시간 비율을 잰다(분열 · 거력 Lv2 · 6 m 슬라임).
    private static async UniTask ArrowFeelAsync(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        sb.AppendLine("화살 멈칫 — 6초 연사 중 느려진 시간(timeScale < 0.99) 비율 · 맞힘 · 프레임");
        var parts = RangedPartsState.Current;
        var saved = new List<RangedPartsState.Equipped>(parts.Equipped_);
        int hits = 0, lastHitFrame = -999;
        Action<HitInfo> onHit = h => { if (h.Attacker == p.gameObject) { hits++; lastHitFrame = Time.frameCount; } };
        HitFeedbackService.OnHit += onHit;
        var ratios = new List<float>();
        try
        {
            foreach (var (path, label, part) in new[] { (Bow, "활(분열 Lv2)", "part_split"), (Crossbow, "석궁(거력 Lv2)", "part_power") })
            {
                DetachAll();
                foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
                    if (m != null && !m.IsDead) m.gameObject.SetActive(false);
                if (!await EquipAsync(p, path, ct, PlayerWeaponManager.Slot1)) { sb.AppendLine("장착 실패 " + label); continue; }   // 원거리 파츠는 슬롯 1 화살에만
                parts.Restore(new[] { new RangedPartsState.Equipped { partId = part, level = 2 } });
                await Wait(0.5f, ct);
                s_home = p.transform.position;
                Vector3 fw = Flat(p.transform.forward);
                var t = await TrackAsync(p.transform.position + fw * 6f, ct);
                if (t == null) { sb.AppendLine("슬라임 부르기 실패 " + label); continue; }
                float w0 = Time.realtimeSinceStartup;   // 예열 — 첫 장착 무기는 첫 몇 발이 비었다
                while (Time.realtimeSinceStartup - w0 < 1.5f) { RefillAll(); Steer(p, t.M.transform.position); p.InputBuffer.Push(Command.Light); await Wait(0.1f, ct); }
                hits = 0;
                float slow = 0f, total = 0f, maxFrame = 0f, nextPush = 0f;
                int frames = 0, slowFrames = 0, hitches = 0, hitchNearHit = 0;
                float t0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - t0 < 6f)
                {
                    float dt = Time.unscaledDeltaTime;
                    total += dt; frames++;
                    if (Time.timeScale < 0.99f) { slow += dt; slowFrames++; }
                    if (dt > maxFrame) maxFrame = dt;
                    if (dt > 0.05f) { hitches++; if (Time.frameCount - lastHitFrame <= 2) hitchNearHit++; }
                    if (Time.realtimeSinceStartup >= nextPush)
                    {
                        RefillAll(); Steer(p, t.M.transform.position); p.InputBuffer.Push(Command.Light);
                        nextPush = Time.realtimeSinceStartup + 0.1f;
                    }
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                }
                float ratio = total > 0f ? slow / total : 0f;
                ratios.Add(ratio);
                sb.AppendLine($"{label}: 맞힘 {hits} · 느려진 시간 {ratio:0%}({slowFrames}/{frames}프레임) · 평균 프레임 {(frames > 0 ? total / frames * 1000f : 0f):0}ms · 최장 {maxFrame * 1000f:0}ms · 50ms 넘는 프레임 {hitches}(타격 ±2프레임 {hitchNearHit})");
            }
        }
        finally
        {
            HitFeedbackService.OnHit -= onHit;
            parts.Restore(saved);
            DetachAll();
        }
        bool ok = ratios.Count == 2 && ratios[0] < 0.1f && ratios[1] < 0.1f;
        Check(sb, $"활 · 석궁 연사 중 느려진 시간 10% 아래 — {string.Join(" · ", ratios.ConvertAll(r => r.ToString("0%")))}", ok);
    }

    // ── 49 처치 이펙트 맞춤(10-14) ───────────────────────────────
    // 일반 · 희귀 · 정예 · 날짐승 · 사망 설정 없는 둘. 몸 = 메시 렌더러 바운즈(입자 · 트레일 제외 — MonsterBase.MeasureModelHeight와 같은 기준).
    // 섬광 = 막타 뒤 새로 켜진 입자 렌더러 바운즈(4 m 안). 0.05초 · 0.15초(실시간) 입자 수로 「막타 멈칫 동안 보이나」도 본다.
    private static readonly string[] DeathFxMonsters =
        { "Slime/Slime", "Golem/Golem", "Werewolf/Werewolf", "FairyBat/FairyBat", "FlyingDemon/FlyingDemon", "Cactus/Cactus", "CrabMonster/CrabMonster" };

    private static Bounds BodyBounds(MonsterBase m)
    {
        // 몸 = 스킨 메시(바닥 그림자 · 정예 고리 · 예고 원은 MeshRenderer라 빠진다). 스킨 메시가 없으면 이름에 Shadow · Ring이 없는 메시.
        Bounds b = default; bool any = false;
        foreach (var r in m.GetComponentsInChildren<SkinnedMeshRenderer>(false))
        {
            if (!r.enabled) continue;
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        if (any) return b;
        foreach (var r in m.GetComponentsInChildren<MeshRenderer>(false))
        {
            if (!r.enabled || r.name.Contains("Shadow") || r.name.Contains("Ring")) continue;
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        return b;
    }

    private static (Bounds b, bool any, int count) NewParticles(HashSet<ParticleSystem> before, Vector3 near)
    {
        Bounds b = default; bool any = false; int count = 0;
        foreach (var ps in UnityEngine.Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
        {
            if (ps == null || before.Contains(ps) || !ps.gameObject.activeInHierarchy) continue;
            if ((ps.transform.position - near).sqrMagnitude > 16f) continue;
            count += ps.particleCount;
            if (ps.particleCount > 0 && ps.TryGetComponent<ParticleSystemRenderer>(out var r) && r.enabled)
            {
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
        }
        return (b, any, count);
    }

    private static async UniTask DeathFxAsync(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        sb.AppendLine("처치 이펙트 맞춤 — 몸 높이 · 몸 가운데(발 기준) · 섬광 중심 · 섬광 크기 · 입자 수(0.05초 / 0.15초 실시간) · 설정");
        Vector3 fw = Flat(p.transform.forward);
        s_home = p.transform.position;
        DetachAll();
        foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
            if (m != null && !m.IsDead) m.gameObject.SetActive(false);
        await EquipAsync(p, Nameless, ct);
        HubGui("HideTestHubGui");
        var cfgField = typeof(MonsterBase).GetField("_config", Inst);
        try
        {
            foreach (var key in DeathFxMonsters)
            {
                string name = key.Substring(key.IndexOf('/') + 1);
                MonsterBase m = null;
                try { m = await Managers.ObjectPooler.SpawnAsync<MonsterBase>(key, ObjectPoolerManager.PoolType.Monster, p.transform.position + fw * 4f, Quaternion.identity); }
                catch (Exception) { }
                if (m == null) { sb.AppendLine($"{name}: 소환 실패"); continue; }
                float t0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - t0 < 6f && m.IsDamageImmuneNow) await UniTask.Yield(ct);
                m.Status.ApplyCc("probe_hold", 9999f);
                await Wait(1.2f, ct);   // 크기 맞춤(InitAsync) · 등장 연출이 끝나게
                foreach (var o in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
                    if (o != null && o != m && !o.IsDead) o.gameObject.SetActive(false);   // 방 파도 몬스터가 사진에 끼지 않게
                var body = BodyBounds(m);
                float feet = m.transform.position.y, bh = Mathf.Max(0.01f, body.size.y), bodyMid = body.center.y - feet;
                var cfg = cfgField?.GetValue(m) as MonsterConfigSO;
                var d = cfg != null ? cfg.death : null;
                var before = new HashSet<ParticleSystem>();
                foreach (var ps in UnityEngine.Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
                    if (ps != null && ps.gameObject.activeInHierarchy) before.Add(ps);
                SetHp(m, 1);
                DealAsBase(p, m, 300f);
                await Wait(0.05f, ct);
                var early = NewParticles(before, body.center);
                await Wait(0.10f, ct);
                var late = NewParticles(before, body.center);
                // 가까운 카메라 — 몸 앞 3.5 m · 위 1.2 m에서 몸 가운데를 본다(게임 카메라와 같은 컬링)
                var main = Camera.main;
                var camGo = new GameObject("ProbeDeathFxCam");
                var cam = camGo.AddComponent<Camera>();
                if (main != null) { cam.cullingMask = main.cullingMask; cam.clearFlags = main.clearFlags; cam.backgroundColor = main.backgroundColor; }
                cam.fieldOfView = 45f;
                Vector3 look = body.center;
                camGo.transform.position = look - fw * 3.5f + Vector3.up * 1.2f;
                camGo.transform.LookAt(look);
                CaptureMain(cam, $"Temp/ui_shots/DF_{name}.png");
                UnityEngine.Object.Destroy(camGo);
                float vy = late.any ? late.b.center.y - feet : -1f;
                Vector3 rootXZ = m.transform.position; rootXZ.y = 0f;
                Vector3 bodyXZ = body.center; bodyXZ.y = 0f;
                Vector3 fxXZ   = late.any ? late.b.center : rootXZ; fxXZ.y = 0f;
                sb.AppendLine($"   {name} 수평 — 루트 ↔ 몸 {(bodyXZ - rootXZ).magnitude:0.00} m · 섬광 ↔ 몸 {(fxXZ - bodyXZ).magnitude:0.00} m · 섬광 ↔ 루트 {(fxXZ - rootXZ).magnitude:0.00} m · 몸이 루트에서 카메라 쪽으로 {Vector3.Dot(bodyXZ - rootXZ, -fw):+0.00;-0.00} m");
                float vs = late.any ? Mathf.Max(late.b.size.x, late.b.size.y) : 0f;
                sb.AppendLine($"{name}({m.Grade}): 몸 {bh:0.00} m · 몸 가운데 {bodyMid:0.00} · 섬광 중심 {vy:0.00}(차 {(vy - bodyMid) / bh:+0.00;-0.00}배 몸) · 섬광 크기 {vs:0.00} m(몸의 {vs / bh:0.00}배) · 입자 {early.count} / {late.count} · "
                            + $"설정 {(d != null ? $"{d.deathVfxKey} ×{d.deathVfxScale} 높이 {d.deathVfxHeight} 실시간 {d.deathVfxUnscaled}" : "없음")} · 사진 DF_{name}");
                await Wait(1.5f, ct);
                if (m != null && m.gameObject.activeSelf) m.gameObject.SetActive(false);
            }
        }
        finally
        {
            HubGui("RestoreTestHubGui");
            DetachAll();
        }
    }

    // ── 51 추적 화살 × 새끼 용(10-14 사용자 「3챕터 보스가 소환하는 잡몹 드래곤에게 추적 화살이 안 가고 보스만 인식한다」) ──
    // 새끼 용(DragonMiniDragon)은 MonsterBase가 아니다(IDamageable · IKillable). 소환 패턴과 같은 프리팹 · 크기 0.35.
    private const string MiniDragonGuid = "714f07f145d35924fb9671342321d622";

    private static async UniTask MiniHomingAsync(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        sb.AppendLine("추적 화살 × 새끼 용 — 정면 16 m 슬라임(보스 역) · 오른쪽 20° 10 m 새끼 용 · 유도 Lv4 · 슬라임을 겨눠 8발");
        var parts = RangedPartsState.Current;
        var saved = new List<RangedPartsState.Equipped>(parts.Equipped_);
        GameObject mini = null, anchor = null;
        try
        {
            DetachAll();
            foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
                if (m != null && !m.IsDead) m.gameObject.SetActive(false);
            await Wait(5f, ct);   // 방 입장 입력 잠금(50 돌진 벽과 같은 함정 — 1회차 화살 0발)
            if (!await EquipAsync(p, Bow, ct, PlayerWeaponManager.Slot1)) { sb.AppendLine("활 장착 실패"); return; }   // 원거리 파츠는 슬롯 1 화살에만
            parts.Restore(new[] { new RangedPartsState.Equipped { partId = "part_homing", level = 4 } });
            await Wait(0.5f, ct);
            s_home = p.transform.position;
            Vector3 fw = Flat(p.transform.forward);
            var boss = await TrackAsync(p.transform.position + fw * 16f, ct);
            if (boss == null) { sb.AppendLine("슬라임 부르기 실패"); return; }
            float w0 = Time.realtimeSinceStartup;   // 예열 — 첫 장착 무기는 첫 몇 발이 비었다(새끼 용을 세우기 전이라 슬라임만 맞는다)
            while (Time.realtimeSinceStartup - w0 < 1.5f) { RefillAll(); Steer(p, boss.M.transform.position); p.InputBuffer.Push(Command.Light); await Wait(0.1f, ct); }
            await Wait(1f, ct);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(MiniDragonGuid));
            if (prefab == null) { sb.AppendLine("새끼 용 프리팹 없음"); return; }
            Vector3 at = p.transform.position + Quaternion.Euler(0f, 20f, 0f) * fw * 10f;
            anchor = new GameObject("probe_mini_anchor");
            anchor.transform.position = at;
            mini = UnityEngine.Object.Instantiate(prefab, at, Quaternion.LookRotation(-fw));
            mini.transform.localScale = Vector3.one * 0.35f;   // 소환 패턴 _miniScale
            if (!mini.TryGetComponent<DragonMiniDragon>(out var md)) md = mini.AddComponent<DragonMiniDragon>();
            md.Init(hp: TankHp, speed: 0f, breathRange: 0f, breathCooldown: 999f, breathDamage: 0, breathSpeed: 0f,
                    breathPrefab: null, tintColor: Color.white, playerTarget: anchor.transform);
            md.enabled = false;   // 걷지도 쏘지도 않게 — 맞기만 한다
            await Wait(0.8f, ct);
            var hpF = typeof(DragonMiniDragon).GetField("_currentHp", Inst);
            int Hp() => hpF != null && md != null ? (int)hpF.GetValue(md) : -1;
            int hp0 = Hp();
            boss.Dealt = 0f;
            var tgtF = typeof(BasicArrow).GetField("_homingTarget", Inst);
            var homF = typeof(BasicArrow).GetField("_homing", Inst);
            for (int i = 0; i < 8; i++)
            {
                RefillAll(); Steer(p, boss.M.transform.position); p.InputBuffer.Push(Command.Light);
                await Wait(0.2f, ct);
                if (i < 3)
                    foreach (var a in UnityEngine.Object.FindObjectsByType<BasicArrow>(FindObjectsSortMode.None))
                        if (a.isActiveAndEnabled)
                        {
                            var tg = tgtF?.GetValue(a) as Component;
                            Vector3 rel = a.transform.position - s_home;
                            sb.AppendLine($"     화살 {i}: 앞 {Vector3.Dot(rel, fw):0.0} m · 옆 {Vector3.Dot(rel, Vector3.Cross(Vector3.up, fw)):0.0} m · 높이 {rel.y:0.0} m · 유도 {homF?.GetValue(a)} · 표적 {(tg != null ? tg.name : "없음")}");
                        }
                await Wait(0.2f, ct);
            }
            await Wait(1f, ct);
            int miniDealt = hp0 - Hp();
            var cols = mini.GetComponentsInChildren<Collider>();
            Bounds cb = cols.Length > 0 ? cols[0].bounds : new Bounds(at, Vector3.zero);
            foreach (var c in cols) cb.Encapsulate(c.bounds);
            sb.AppendLine($"   새끼 용 받은 피해 {miniDealt} · 슬라임(보스 역) {boss.Dealt:0} · 새끼 용 체력 {Hp()}/{hp0} · 피격 콜라이더 {cols.Length}개 높이 {cb.min.y - at.y:0.00} ~ {cb.max.y - at.y:0.00} m");
            Check(sb, "추적 화살이 가까운 새끼 용을 노린다(새끼 용이 맞음)", miniDealt > 0);
        }
        finally
        {
            parts.Restore(saved);
            if (mini != null) UnityEngine.Object.Destroy(mini);
            if (anchor != null) UnityEngine.Object.Destroy(anchor);
            DetachAll();
        }
    }

    // ── 52 분열 갈래 반감(10-14 사용자 「분열은 룬 타격 · 시너지 효과가 화살마다 붙어 효과가 붙은 뒤 너무 강하다 — 마지막 계산식에 반감」) ──
    // 활 다발 빌드(룬 5 · 사격 각인 4) · 유도 Lv4로 갈래가 모두 한 표적에 모인다(실전에서 가장 센 경우). 1발 vs 분열 Lv4(3발).
    private static async UniTask SplitCutAsync(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        var b = FourBuilds[2];   // 활 다발
        sb.AppendLine($"분열 갈래 반감 — {b.label} 룬 5 · {BuildFamilyRules.Label(b.fam)} 각인 4 · 유도 Lv4 · 단일 {b.range:0} m 10초 · 입력 0.1초");
        var run = GameRunBootstrapper.Instance.Run;
        var placed = typeof(RunItemInventory).GetField("_placedItems", Inst)?.GetValue(run.ItemInventory) as List<RuntimeItemData>;
        if (placed == null) { sb.AppendLine("룬 판 목록을 못 읽음"); return; }
        var savedPlaced = new List<RuntimeItemData>(placed);
        var parts = RangedPartsState.Current;
        var savedParts = new List<RangedPartsState.Equipped>(parts.Equipped_);
        int hits = 0;
        Action<HitInfo> onHit = h => { if (h.Attacker == p.gameObject) hits++; };
        var rows = new List<(string label, float total, float effect, float perVolley)>();
        HitFeedbackService.OnHit += onHit;
        try
        {
            DetachAll();
            foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
                if (m != null && !m.IsDead) m.gameObject.SetActive(false);
            await Wait(5f, ct);   // 방 입장 입력 잠금
            if (!await EquipAsync(p, b.weapon, ct, PlayerWeaponManager.Slot1)) { sb.AppendLine("장착 실패 " + b.label); return; }   // 원거리 파츠는 슬롯 1 화살에만
            s_home = p.transform.position;
            Vector3 probeAim = p.transform.position + Flat(p.transform.forward) * 6f;
            var seen = new HashSet<int>();
            async UniTask<int> ShotsAsync()
            {
                int shots = 0; seen.Clear();
                float q0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - q0 < 1.5f)
                {
                    RefillAll(); Steer(p, probeAim); p.InputBuffer.Push(Command.Light);
                    var now = new HashSet<int>();
                    foreach (var a in UnityEngine.Object.FindObjectsByType<BasicArrow>(FindObjectsSortMode.None))
                        if (a.isActiveAndEnabled) { int id = a.GetInstanceID(); now.Add(id); if (!seen.Contains(id)) shots++; }
                    seen = now;
                    await Wait(0.05f, ct);
                }
                return shots;
            }
            var bis = new StringBuilder("     단계별 새 화살(1.5초): ① 장착 ");
            bis.Append(await ShotsAsync());
            var w = p.WeaponManager.Weapon1Data;
            typeof(PlayerWeaponManager).GetMethod("ApplyServerOverrideIfAvailable", BindingFlags.Static | BindingFlags.NonPublic)
                ?.Invoke(null, new object[] { w, Path.GetFileNameWithoutExtension(b.weapon) });   // 차트(EQUIPMENT_DATA)가 정본
            w.enhanceLevel = 0;
            w.RecomputeEnhancedStats();
            w.formTier = 0;
            w.formVariants = "";
            WeaponFormRuntime.Of(p).Rebuild();
            p.WeaponManager.RaiseEquippedWeaponRefreshed();
            bis.Append(" · ② 차트 ").Append(await ShotsAsync());
            placed.Clear();
            foreach (var id in b.ids)
            {
                var d = ProjectRune(id);
                if (d == null) continue;
                if (string.IsNullOrEmpty(d.element)) d.element = b.el;
                placed.Add(d);
            }
            run.EffectManager.Rebuild();
            bis.Append(" · ③ 룬 ").Append(await ShotsAsync());
            ForceImprint(b.fam, 4);
            bis.Append(" · ④ 각인 ").Append(await ShotsAsync());
            sb.AppendLine(bis.ToString());

            foreach (var (label, splitLv, homing) in new[] { ("1발(분열 없음)", 0, true), ("3발(분열 Lv4)", 4, true), ("5발(분열 Lv10)", 10, true) })
            {
                var eq = new List<RangedPartsState.Equipped>();
                if (homing) eq.Add(new RangedPartsState.Equipped { partId = "part_homing", level = 4 });
                if (splitLv > 0) eq.Add(new RangedPartsState.Equipped { partId = "part_split", level = splitLv });
                parts.Restore(eq.ToArray());
                await Wait(0.5f, ct);
                s_home = p.transform.position;
                var t = await TrackAsync(p.transform.position + Flat(p.transform.forward) * b.range, ct);
                if (t == null) { sb.AppendLine("슬라임 부르기 실패 " + label); continue; }
                Vector3 aim = t.M.transform.position;
                float s0 = Time.time;
                bool diag = false;
                while (Time.time - s0 < 2f)
                {
                    RefillAll(); Steer(p, aim); p.InputBuffer.Push(Command.Light); await Wait(0.1f, ct);
                    if (!diag && Time.time - s0 > 1f)
                    {
                        diag = true;
                        int arrows = 0; string first = "";
                        foreach (var a in UnityEngine.Object.FindObjectsByType<BasicArrow>(FindObjectsSortMode.None))
                            if (a.isActiveAndEnabled) { arrows++; if (first.Length == 0) { var r = a.transform.position - p.transform.position; first = $" · 첫 화살 앞 {Vector3.Dot(r, Flat(p.transform.forward)):0.0} m 높이 {r.y:0.0} m"; } }
                        var wmD = p.WeaponManager;
                        sb.AppendLine($"     진단: 슬롯 {wmD.CurrentSlotIndex} · 무기 {(wmD.CurrentWeaponInstance != null ? wmD.CurrentWeaponInstance.name : "없음")} · 시간 {Time.timeScale:0.00} · 날아가는 화살 {arrows}{first} · 표적 {Vector3.Distance(p.transform.position, aim):0.0} m");
                    }
                }
                t.Dealt = 0f; hits = 0;
                var st = run.RunStats;
                long b0 = st.DamageOf(DamageSource.Basic), r0 = st.DamageOf(DamageSource.Rune), d0 = st.DamageOf(DamageSource.Dot);
                float t0 = Time.time, r0t = Time.realtimeSinceStartup, nextPush = 0f;
                int frames = 0, centers = 0, extras = 0;
                var wF = typeof(BasicArrow).GetField("_splitWeight", Inst);
                var live = new HashSet<int>();
                while (Time.time - t0 < 10f)
                {
                    if (Time.realtimeSinceStartup >= nextPush) { RefillAll(); Steer(p, aim); p.InputBuffer.Push(Command.Light); nextPush = Time.realtimeSinceStartup + 0.1f; }
                    var now = new HashSet<int>();
                    foreach (var a in UnityEngine.Object.FindObjectsByType<BasicArrow>(FindObjectsSortMode.None))
                    {
                        if (!a.isActiveAndEnabled) continue;
                        int id = a.GetInstanceID(); now.Add(id);
                        if (live.Contains(id)) continue;
                        float wt = wF != null ? (float)wF.GetValue(a) : 1f;
                        if (wt >= 0.999f) centers++; else extras++;
                    }
                    live = now; frames++;
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                }
                float secs = Mathf.Max(0.01f, Time.time - t0);
                float basic = (st.DamageOf(DamageSource.Basic) - b0) / secs, rune = (st.DamageOf(DamageSource.Rune) - r0) / secs, dot = (st.DamageOf(DamageSource.Dot) - d0) / secs;
                rows.Add((label, t.Dealt / secs, rune + dot, centers > 0 ? t.Dealt / centers : 0f));
                sb.AppendLine($"   {label}: 초당 {t.Dealt / secs:0.0} · 일제 사격 1회당 {(centers > 0 ? t.Dealt / centers : 0f):0.0} · 맞힘 {hits} · 출처(초당) 평타 {basic:0} · 룬 {rune:0} · 지속 {dot:0} · 쏜 화살 가운데 {centers} · 갈래 {extras} · 실제 {Time.realtimeSinceStartup - r0t:0.0}초 · 평균 프레임 {(Time.realtimeSinceStartup - r0t) / Mathf.Max(1, frames) * 1000f:0}ms");
                t.M.gameObject.SetActive(false);
                DetachAll();
            }
        }
        finally
        {
            HitFeedbackService.OnHit -= onHit;
            placed.Clear();
            placed.AddRange(savedPlaced);
            parts.Restore(savedParts);
            run.EffectManager.Rebuild();
            DetachAll();
        }
        if (rows.Count < 3 || rows[0].perVolley <= 0f) { Check(sb, $"세 줄 모두 잼 — {rows.Count}/3", false); return; }
        float k = rows[1].perVolley / rows[0].perVolley, k5 = rows[2].perVolley / rows[0].perVolley;
        sb.AppendLine($"   일제 사격 1회당 — 3발 ÷ 1발 {k:0.00}배 · 5발 ÷ 1발 {k5:0.00}배(참고 — 부채꼴이 넓어 단일엔 바깥 갈래가 덜 맞는다) · 초당은 박자가 흔들려 참고만({rows[1].total / Mathf.Max(0.01f, rows[0].total):0.00}배)");
        sb.AppendLine("   (갈래 3발 45% · 5발 38% — 다 맞으면 1.9 · 2.5배 · 갈래 명중 약 60%면 3발 약 1.55배 · 고치기 전 무게 1이면 약 2.2배)");
        Check(sb, $"분열 3발 1회당이 1발보다 조금 더 — 1.2 ~ 1.8배({k:0.00}배)", k >= 1.2f && k <= 1.8f);
    }

    // ── 55 범위 표시 구분(10-15 사용자 「플레이어 룬 · 서약 가이드라인과 보스 · 몬스터 가이드라인 구분 — 누가 쓴 건지 구분하기 어려워」) ──
    private static async UniTask GuidesAsync(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        sb.AppendLine("범위 표시 구분 — 왼쪽 줄 적 예고(앞 = 노랑 차오름 60% · 뒤 = 빨강 발동) · 오른쪽 줄 플레이어(앞 = 고리 · 뒤 = 부채꼴) · 가운데 먼 쪽 = 무기 형 고리 · 띠");
        foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
            if (m != null && !m.IsDead) m.gameObject.SetActive(false);
        await Wait(5f, ct);   // 방 입장 입력 잠금 · 등장 연출
        Vector3 fw = Flat(p.transform.forward), rt = Vector3.Cross(Vector3.up, fw);
        Vector3 c = p.transform.position + fw * 5f;
        SkillRangeRing.Circle(c + fw * 40f, 0.1f, 0.05f);   // 데칼 재질 불러오기(일반 방엔 주입이 없다 — 런 공용 예고 재질)
        await Wait(1.5f, ct);
        var t1 = PatternGuideHelper.Disc(c - rt * 2.6f, 1.5f, PatternGuideHelper.Telegraph, 6f);
        PatternGuideHelper.Prepare(t1, PatternGuideHelper.Telegraph);
        PatternGuideHelper.SetProgress(t1, 0.6f);
        var t2 = PatternGuideHelper.Disc(c - rt * 2.6f + fw * 3.6f, 1.5f, PatternGuideHelper.Telegraph, 6f);
        PatternGuideHelper.Arm(t2);
        SkillRangeRing.Circle(c + rt * 2.6f, 1.5f, 6f);
        SkillRangeRing.Fan(c + rt * 2.6f + fw * 2.4f, fw, 3f, 90f, 6f);
        FormStrike.GroundRing(c + fw * 7.5f, 1.5f, 6f);
        FormStrike.GroundStrip(c + fw * 4.5f - rt * 0.8f, fw, 1.5f, 0.4f, 6f);
        await ShotUi("GU_1", 400);
        string path = Path.Combine(Directory.GetCurrentDirectory(), "Temp", "ui_shots", "GU_1.png");
        sb.AppendLine($"   적 예고 데칼 {(t1 != null && t2 != null ? "있음" : "없음")} · 사진 {path}");
        Check(sb, "사진 찍힘", File.Exists(path));
    }

    // ── 54 화살 경직 · 넉백 없음(10-15 사용자 「활 같은 공격은 경직이나 넉백이 포함되면 안 될 것 같아」) ──
    private static async UniTask NoFlinchAsync(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        sb.AppendLine("화살 경직 · 넉백 — 슬라임(붙잡지 않음) 3초 · 경직 들어간 횟수 · 플레이어 반대쪽으로 밀린 최대 속도");
        foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
            if (m != null && !m.IsDead) m.gameObject.SetActive(false);
        DetachAll();
        await Wait(5f, ct);   // 방 입장 입력 잠금
        s_home = p.transform.position;
        int hits = 0;
        Action<HitInfo> onHit = h => { if (h.Attacker == p.gameObject) hits++; };
        HitFeedbackService.OnHit += onHit;
        var rows = new List<(string label, int stuns, float push, int hits)>();
        try
        {
            foreach (var (label, path, slot, range) in new[] { ("카타나(근접 · 대조)", Katana, PlayerWeaponManager.Slot0, 1.6f), ("활(화살)", Bow, PlayerWeaponManager.Slot1, 6f) })
            {
                if (!await EquipAsync(p, path, ct, slot)) { sb.AppendLine("   장착 실패 " + label); continue; }
                await Wait(0.5f, ct);
                Vector3 at = s_home + Flat(p.transform.forward) * range;
                var m = await Managers.ObjectPooler.SpawnAsync<MonsterBase>("Slime/Slime", ObjectPoolerManager.PoolType.Monster, at, Quaternion.LookRotation(-Flat(p.transform.forward)));
                if (m == null) { sb.AppendLine("   슬라임 부르기 실패"); continue; }
                float w0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - w0 < 6f && m.IsDamageImmuneNow) await UniTask.Yield(PlayerLoopTiming.Update, ct);
                SetHp(m, TankHp);
                m.TryGetComponent<Rigidbody>(out var mrb);
                int stuns = 0; bool wasStun = false; float push = 0f; hits = 0;
                float t0 = Time.realtimeSinceStartup, nextPush = 0f;
                while (Time.realtimeSinceStartup - t0 < 3f)
                {
                    if (Time.realtimeSinceStartup >= nextPush)
                    {
                        // 파도 몬스터만 끈다 — HideStrays는 추적 목록에 없는 이 슬라임까지 꺼서 1회차 맞힘 0이었다
                        foreach (var o in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
                            if (o != null && o != m && !o.IsDead) o.gameObject.SetActive(false);
                        Steer(p, m.transform.position); p.InputBuffer.Push(Command.Light); nextPush = Time.realtimeSinceStartup + 0.1f;
                    }
                    bool stun = m.IsInState<GetHitState>();
                    if (stun && !wasStun) stuns++;
                    wasStun = stun;
                    if (mrb != null && !mrb.isKinematic)
                    {
                        Vector3 away = Flat(m.transform.position - p.transform.position);
                        float v = Vector3.Dot(new Vector3(mrb.linearVelocity.x, 0f, mrb.linearVelocity.z), away);
                        if (v > push) push = v;
                    }
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                }
                rows.Add((label, stuns, push, hits));
                sb.AppendLine($"   {label}: 맞힘 {hits} · 경직 {stuns}번 · 밀린 최대 속도 {push:0.0} m/s");
                m.gameObject.SetActive(false);
            }
        }
        finally { HitFeedbackService.OnHit -= onHit; DetachAll(); }
        if (rows.Count < 2) { Check(sb, $"두 줄 모두 잼 — {rows.Count}/2", false); return; }
        Check(sb, $"대조 — 카타나는 경직 · 넉백이 있다(경직 {rows[0].stuns} · {rows[0].push:0.0} m/s)", rows[0].stuns > 0);
        Check(sb, $"화살 — 맞히되 경직 0 · 넉백 0(맞힘 {rows[1].hits} · 경직 {rows[1].stuns} · {rows[1].push:0.0} m/s)", rows[1].hits > 0 && rows[1].stuns == 0 && rows[1].push < 0.5f);
    }

    // ── 53 원거리 무기 바꿔 끼운 뒤 발사(10-15 — 빌드 4종 석궁 0발 · 52 무형검 예열 뒤 활 0발) ──
    private static string ActDump(PlayerController p)
    {
        object Cur(string field) { var sm = typeof(PlayerController).GetField(field, Inst)?.GetValue(p); return sm?.GetType().GetProperty("CurrentId")?.GetValue(sm); }
        var wm = p.WeaponManager;
        return $"행동 {Cur("_actSM")} · 이동 {Cur("_locoSM")} · 공격 중 {p.Combo.IsAttacking} · 땅 {p.IsGrounded()} · 제자리까지 {Vector3.Distance(p.transform.position, s_home):0.0} m · 슬롯 {wm.CurrentSlotIndex} · 무기 {(wm.CurrentWeaponInstance != null ? wm.CurrentWeaponInstance.name : "없음")} · 무기 있음 {wm.HasWeapon}";
    }

    private static async UniTask RangedSwapAsync(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        sb.AppendLine("원거리 무기 바꿔 끼운 뒤 발사 — 칸마다 1.5초 · 새 화살 수");
        foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
            if (m != null && !m.IsDead) m.gameObject.SetActive(false);
        DetachAll();
        await Wait(5f, ct);   // 방 입장 입력 잠금
        s_home = p.transform.position;   // Steer의 제자리 — 안 잡으면 지난 플레이 값으로 순간이동해 내내 「공중」이었다(1회차)
        Vector3 aim = p.transform.position + Flat(p.transform.forward) * 6f;
        async UniTask<int> ShootAsync(string label, float secs)
        {
            int shots = 0; var live = new HashSet<int>();
            var dumps = new List<string>();
            float q0 = Time.realtimeSinceStartup, nextDump = 0.3f;
            while (Time.realtimeSinceStartup - q0 < secs)
            {
                RefillAll(); Steer(p, aim); p.InputBuffer.Push(Command.Light);
                var now = new HashSet<int>();
                foreach (var a in UnityEngine.Object.FindObjectsByType<BasicArrow>(FindObjectsSortMode.None))
                    if (a.isActiveAndEnabled) { int id = a.GetInstanceID(); now.Add(id); if (!live.Contains(id)) shots++; }
                live = now;
                if (Time.realtimeSinceStartup - q0 >= nextDump && dumps.Count < 3) { dumps.Add(ActDump(p)); nextDump += 0.4f; }
                await Wait(0.05f, ct);
            }
            sb.AppendLine($"   {label}: 새 화살 {shots}");
            if (shots == 0) foreach (var d in dumps) sb.AppendLine("      " + d);
            return shots;
        }
        int a1 = 0, a2 = 0, a3 = 0;
        if (await EquipAsync(p, Bow, ct, PlayerWeaponManager.Slot1)) a1 = await ShootAsync("① 활(슬롯 1)", 1.5f);
        else sb.AppendLine("   활 장착 실패");
        if (await EquipAsync(p, Crossbow, ct, PlayerWeaponManager.Slot1)) a2 = await ShootAsync("② 석궁(슬롯 1 바꿔 끼움)", 1.5f);
        else sb.AppendLine("   석궁 장착 실패");
        if (await EquipAsync(p, Katana, ct))
        {
            float k0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - k0 < 1.5f) { p.InputBuffer.Push(Command.Light); await Wait(0.1f, ct); }
            sb.AppendLine($"   카타나 휘두른 뒤 상태 — {ActDump(p)}");
        }
        if (await EquipAsync(p, Bow, ct, PlayerWeaponManager.Slot1)) a3 = await ShootAsync("③ 카타나 휘두른 뒤 활(슬롯 1)", 1.5f);
        sb.AppendLine($"   다시 1.5초 — {ActDump(p)}");
        if (a2 == 0 || a3 == 0) await ShootAsync("   (재시도) 같은 무기 1.5초 더", 1.5f);
        Check(sb, "바꿔 끼운 원거리 무기도 쏜다", a1 > 0 && a2 > 0 && a3 > 0);

        // 석궁 단계 끊기(47 석궁 저격 0맞힘)
        var b = FourBuilds[3];
        var run = GameRunBootstrapper.Instance.Run;
        var placed = typeof(RunItemInventory).GetField("_placedItems", Inst)?.GetValue(run.ItemInventory) as List<RuntimeItemData>;
        var savedPlaced = placed != null ? new List<RuntimeItemData>(placed) : null;
        var parts = RangedPartsState.Current;
        var savedParts = new List<RangedPartsState.Equipped>(parts.Equipped_);
        int hits = 0, crits = 0;
        float dealt = 0f;
        Action<HitInfo> onHit = h => { if (h.Attacker == p.gameObject) { hits++; dealt += h.Damage; if (h.IsCritical) crits++; } };
        HitFeedbackService.OnHit += onHit;
        try
        {
            if (!await EquipAsync(p, b.weapon, ct, PlayerWeaponManager.Slot1)) { sb.AppendLine("   석궁 장착 실패"); return; }
            var t = await TrackAsync(p.transform.position + Flat(p.transform.forward) * b.range, ct);
            if (t == null) { sb.AppendLine("   슬라임 부르기 실패"); return; }
            aim = t.M.transform.position;
            var line = new StringBuilder("   석궁 단계(새 화살/맞힘): ");
            async UniTask StageAsync(string label)
            {
                hits = 0; crits = 0; dealt = 0f;
                int s = await ShootAsync(label, 3f);
                line.Append($"{label} {s}/{hits} 한 발 {(hits > 0 ? dealt / hits : 0f):0.0}(치명 {crits}) · ");
            }
            await StageAsync("① 석궁만");
            parts.Restore(new[] { new RangedPartsState.Equipped { partId = b.part, level = 2 } });
            await StageAsync("② 거력 Lv2");
            var w = p.WeaponManager.Weapon1Data;
            typeof(PlayerWeaponManager).GetMethod("ApplyServerOverrideIfAvailable", BindingFlags.Static | BindingFlags.NonPublic)
                ?.Invoke(null, new object[] { w, Path.GetFileNameWithoutExtension(b.weapon) });
            w.enhanceLevel = 0; w.RecomputeEnhancedStats(); w.formTier = 0; w.formVariants = "";
            WeaponFormRuntime.Of(p).Rebuild();
            p.WeaponManager.RaiseEquippedWeaponRefreshed();
            await StageAsync("③ 차트");
            if (placed != null)
            {
                placed.Clear();
                foreach (var id in b.ids) { var d = ProjectRune(id); if (d == null) continue; if (string.IsNullOrEmpty(d.element)) d.element = b.el; placed.Add(d); }
                run.EffectManager.Rebuild();
            }
            await StageAsync("④ 룬 5");
            ForceImprint(b.fam, 4);
            await StageAsync("⑤ 각인 4");
            sb.AppendLine(line.ToString());
            sb.AppendLine($"   끝 상태 — {ActDump(p)}");
        }
        finally
        {
            HitFeedbackService.OnHit -= onHit;
            if (placed != null && savedPlaced != null) { placed.Clear(); placed.AddRange(savedPlaced); run.EffectManager.Rebuild(); }
            parts.Restore(savedParts);
            DetachAll();
        }
    }

    // ── 50 돌진 벽(10-14 사용자 「무형검의 R 스킬과 대시 스킬이 벽을 뚫고 간다」) ──
    private static readonly RaycastHit[] s_wallHits = new RaycastHit[16];

    /// <summary>바닥 · 트리거 · 적 · 자기 자신을 뺀 첫 벽까지 거리(없으면 -1) — 돌진 끝점 판정과 같은 기준.</summary>
    private static float WallAhead(PlayerController p, Vector3 from, Vector3 dir, float max)
    {
        int ground = p.CharacterData != null ? p.CharacterData.groundLayer.value : 0;
        int n = Physics.RaycastNonAlloc(from + Vector3.up * 0.6f, dir, s_wallHits, max, ~ground, QueryTriggerInteraction.Ignore);
        float best = -1f;
        for (int i = 0; i < n; i++)
        {
            var h = s_wallHits[i];
            if (h.collider == null || h.collider.transform.IsChildOf(p.transform)) continue;
            if (h.collider.GetComponentInParent<IDamageable>() != null) continue;
            if (best < 0f || h.distance < best) best = h.distance;
        }
        return best;
    }

    private static async UniTask DashWallAsync(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        sb.AppendLine("돌진 벽 — 벽 3 m 앞에서 벽 쪽으로. 몸 가운데가 벽면(0.6 m 높이 광선)을 넘으면 실패");
        await Wait(5f, ct);   // 방 입장 입력 잠금 · 등장 연출(2회차: 무형검 R · 회피가 0 m)
        foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
            if (m != null && !m.IsDead) m.gameObject.SetActive(false);
        DetachAll();
        await EquipAsync(p, Nameless, ct);
        await Wait(0.5f, ct);
        // 가장 가까운 벽 방향(12방향 · 2 ~ 15 m)
        Vector3 pos = p.transform.position, wallDir = Vector3.zero;
        float wallDist = -1f;
        for (int k = 0; k < 12; k++)
        {
            var d = Quaternion.Euler(0f, k * 30f, 0f) * Vector3.forward;
            float w = WallAhead(p, pos, d, 40f);
            if (w >= 2f && (wallDist < 0f || w < wallDist)) { wallDist = w; wallDir = d; }
        }
        if (wallDist < 0f)
        {
            sb.AppendLine("벽을 못 찾음(40 m 안) — 방향별 원시 적중(모든 레이어 · 트리거 포함):");
            for (int k = 0; k < 12; k += 3)
            {
                var d = Quaternion.Euler(0f, k * 30f, 0f) * Vector3.forward;
                int n = Physics.RaycastNonAlloc(pos + Vector3.up * 0.6f, d, s_wallHits, 40f, ~0, QueryTriggerInteraction.Collide);
                var line = new StringBuilder($"   {k * 30}°:");
                for (int i = 0; i < n && i < 6; i++)
                {
                    var h = s_wallHits[i];
                    line.Append($" [{h.collider.name} · {LayerMask.LayerToName(h.collider.gameObject.layer)} · 트리거 {h.collider.isTrigger} · {h.distance:0.0} m · 적 {h.collider.GetComponentInParent<IDamageable>() != null}]");
                }
                sb.AppendLine(line.ToString());
            }
            return;
        }
        Vector3 home = pos + wallDir * Mathf.Max(0f, wallDist - 3f);
        float wall = WallAhead(p, home, wallDir, 10f);
        sb.AppendLine($"   벽 방향 {Quaternion.LookRotation(wallDir).eulerAngles.y:0}° · 벽까지 {wall:0.00} m");
        var rb = p.Rigid;

        async UniTask<float> RunAsync(string path, Command cmd, SkillType? slot)
        {
            if (path != null && !await EquipAsync(p, path, ct)) return float.NaN;
            if (rb != null) { rb.linearVelocity = Vector3.zero; rb.position = home; }
            p.transform.position = home;
            s_home = home;
            await Wait(0.4f, ct);
            AimAt(home + wallDir * 3f);
            p.RequestFacing(Quaternion.LookRotation(wallDir));
            await Wait(0.3f, ct);
            if (slot.HasValue) p.CooldownTracker.ResetCooldown(slot.Value);
            p.InputBuffer.Push(cmd);
            await Wait(1.0f, ct);
            return Vector3.Dot(p.transform.position - home, wallDir);
        }

        var cases = new (string label, string path, Command cmd, SkillType? slot)[]
        {
            ("무형검 R 무형일섬(8 m)", null,   Command.RSkill, SkillType.R),
            ("회피 대시",            null,   Command.Dodge,  null),
            ("카타나 E 성광베기(5 m)", Katana, Command.ESkill, SkillType.E),
        };
        bool allOk = true;
        foreach (var c in cases)
        {
            float along = await RunAsync(c.path, c.cmd, c.slot);
            bool moved = !float.IsNaN(along) && along > 0.5f;
            bool ok = moved && along < wall;
            allOk &= ok;
            sb.AppendLine($"   {c.label}: 벽 쪽으로 {along:0.00} m 이동(벽 {wall:0.00} m) — {(float.IsNaN(along) ? "장착 실패" : !moved ? "안 움직임" : ok ? "벽 앞에서 멈춤" : "벽을 넘음")}");
        }
        Check(sb, "돌진 3종이 벽을 넘지 않는다", allOk);
        if (rb != null) { rb.linearVelocity = Vector3.zero; rb.position = pos; }
        p.transform.position = pos;
    }

    private static IItemEffect Legend(string type, string trigger, float v, float v2 = 0f, float v3 = 0f)
    {
        var e = ItemEffectRegistry.Create(new ItemEffectSlot { effectType = type, trigger = trigger, value = v, value2 = v2, value3 = v3, maxStack = 1 });
        if (e != null) InjectEffect(e);
        return e;
    }

    private static async UniTask BuildSynergy4Async(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        sb.AppendLine("빌드 시너지 계획 4 — 전설 룬 새 발동 7칸(유물 없음)");
        var rs = p.RuntimeStats;
        Vector3 fw = Flat(p.transform.forward), right = Vector3.Cross(Vector3.up, fw);
        s_home = p.transform.position;
        foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
            if (m != null && !m.IsDead) m.gameObject.SetActive(false);
        await EquipAsync(p, Great, ct);
        await Wait(0.3f, ct);
        var spy = new ReportSpy();

        // ① 마그마 분출 — A를 쓰러뜨리면 1.5 m 곁 B가 폭발 피해
        {
            var a = await TrackAsync(p.transform.position + fw * 2f, ct);
            var b = await TrackAsync(p.transform.position + fw * 2f + right * 1.5f, ct);
            var fx = Legend("FireLegendAoe", "OnKill", 1f, 0f, 3f);
            await Wait(0.3f, ct);   // 막 소환한 적 — 초기화 · 물리 위치 반영
            float im0 = Time.realtimeSinceStartup;   // 런 첫 소환은 방 입장 연출 동안 무적이 6초를 넘길 수 있다
            while (a?.M != null && a.M.IsDamageImmuneNow && Time.realtimeSinceStartup - im0 < 10f) await UniTask.Yield(ct);
            bool immune = a?.M != null && a.M.IsDamageImmuneNow;
            float b0 = b != null ? b.Dealt : 0f;
            if (a != null) { SetHp(a.M, 1); DealAsBase(p, a.M, 300f); }   // 기본 3은 슬라임 방어에 먹혀 0이 될 수 있다(시기 배율)
            await Wait(0.3f, ct);
            sb.AppendLine($"   ① 진단 — A 무적 {immune} · 활성 {a?.M != null && a.M.gameObject.activeInHierarchy} · 죽음 {a?.M != null && a.M.IsDead}");
            float got = b != null ? b.Dealt - b0 : 0f;
            RemoveEffect(fx);
            if (b?.M != null) b.M.gameObject.SetActive(false);
            Check(sb, $"① 마그마 분출(처치) — A 처치 {a?.M != null && a.M.IsDead} · 곁 B 폭발 피해 {got:0}(> 0 · 기대 ≈ 공격력 {rs.MeleeAttack} × 존 배율)", a?.M != null && a.M.IsDead && got > 0f);
        }

        var t = await TrackAsync(p.transform.position + fw * 2f, ct);
        if (t == null) { sb.AppendLine("슬라임 부르기 실패"); return; }

        // ② 불사조 강타 — 12번째 적중 뒤 6연타 합 ≈ 공격력 × 3.5 × 공속 배율 × 존 배율
        {
            var fx = Legend("FireLegendSingle", "OnHitCount", 3.5f, 12f, 0.5f);
            for (int i = 0; i < 11; i++) { DealAs(p, t.M, WeaponActionType.GroundLight); RefillAll(); }
            await Wait(0.2f, ct);
            t.Dealt = 0f;
            DealAs(p, t.M, WeaponActionType.GroundLight);
            float hit12 = t.Dealt;
            float tAt = Time.time;   // 불사조 6연타는 게임 시간 0.4초 간격 — 느린 에디터에선 실제 시간보다 늦게 흐른다
            while (Time.time - tAt < 3f) { RefillAll(); await UniTask.Yield(ct); }
            float strikes = t.Dealt - hit12;
            RemoveEffect(fx);
            float want = rs.MeleeAttack * 3.5f * rs.AttackSpeedMultiplier * LegendaryTuning.TierMul("FIRE");
            Check(sb, $"② 불사조 강타(연격 12) — 6연타 합 {strikes:0} · 기대 {want:0}(공격력 {rs.MeleeAttack} × 3.5 × 공속 {rs.AttackSpeedMultiplier:0.00} × 존) · 비 {(want > 0f ? strikes / want : 0f):0.00}",
                  want > 0f && strikes / want > 0.7f && strikes / want < 1.3f);
        }

        // ③ 얼음 창 + 과전류 포박(필살) — 대검 강타 한 번: 대상 속박 · 창 + 폭발 합 ≈ 그 타격 × 3(1.5 + 1.5)
        {
            var ice  = Legend("IceLegendSingle", "OnCrit", 1.5f, 5f);
            var elec = Legend("ElecLegendSingle", "OnCrit", 1.5f, 6f);
            InjectEffect(spy); spy.Seen.Clear();
            RefillAll(); t.Dealt = 0f;
            DealAsBase(p, t.M, 300f);
            float x = spy.Seen.Count > 0 ? spy.Seen[spy.Seen.Count - 1].DamageDealt : 0f;
            bool heavy = spy.Seen.Count > 0 && spy.Seen[spy.Seen.Count - 1].IsHeavy;
            bool bound = t.M.Status.HasCc("bind");
            float first = t.Dealt;
            float tAt = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - tAt < 2.3f) { RefillAll(); await UniTask.Yield(ct); }
            float extra = t.Dealt - first;
            RemoveEffect(spy); RemoveEffect(ice); RemoveEffect(elec);
            float ratio = x > 0f ? extra / (x * LegendaryTuning.TierMul("ICE")) : 0f;
            Check(sb, $"③ 얼음 창 · 과전류 포박(필살) — 강타 {heavy} · 타격 {x:0} · 속박 {bound} · 창 + 폭발 {extra:0} = 타격 × {ratio:0.00}(≈ 3)", heavy && bound && ratio > 2.2f && ratio < 3.4f);
        }

        // ④ 포자 폭발(연격) — 8타 뒤 독 8중첩 · 포자 1회
        {
            var fx = Legend("GrassLegendAoe", "OnHit", 0.4f, 0.04f, 3f);
            t.M.Status.ClearCc("bind");
            for (int i = 0; i < 8; i++) { DealAs(p, t.M, WeaponActionType.GroundLight); RefillAll(); }
            int stacks = t.M.Status.GetDotStacks("item_poison");
            bool burst = t.M.Status.IsOnCooldown("spore_burst");
            RemoveEffect(fx);
            Check(sb, $"④ 포자 폭발(연격) — 8타 뒤 독 {stacks}중첩 · 포자 폭발 {burst}", stacks == 8 && burst);
        }

        // ⑤ 성스러운 심판(필살) — 강타 10번째 뒤 광선 합 ≈ 그 타격 × 3
        {
            var fx = Legend("LightLegendSingle", "OnHeavyHit", 3f, 10f);
            InjectEffect(spy);
            for (int i = 0; i < 9; i++) { DealAs(p, t.M, WeaponActionType.GroundLight); RefillAll(); }
            await Wait(0.2f, ct);
            spy.Seen.Clear(); t.Dealt = 0f;
            DealAsBase(p, t.M, 300f);   // 큰 타격 — 기본 3으로는 광선 한 줄이 정수 반올림에 묻혔다
            float x = spy.Seen.Count > 0 ? spy.Seen[0].DamageDealt : 0f;
            float first = t.Dealt;
            float tAt = Time.time;
            while (Time.time - tAt < 1.2f) { RefillAll(); await UniTask.Yield(ct); }
            float beams = t.Dealt - first;
            RemoveEffect(spy); RemoveEffect(fx);
            float ratio = x > 0f ? beams / (x * LegendaryTuning.TierMul("LIGHT")) : 0f;
            Check(sb, $"⑤ 성스러운 심판(필살 10) — 마지막 타격 {x:0} · 광선 합 {beams:0} = × {ratio:0.00}(≈ 3)", ratio > 2.3f && ratio < 3.4f);
        }

        // ⑥ 관통 투사체 같은 대상 간격 — 멈춘 성검이 대상에 겹쳐 2초 동안 맞힌 수 ≤ 2 / 0.4 + 1
        {
            int hits = 0; int last = -1;
            System.Action<int, int> onHp = (cur, max) => { if (last >= 0 && cur < last) hits++; last = cur; };
            // 앞 칸의 독 · 포자 지속 피해가 없는 새 슬라임(같은 슬라임이면 지속 피해 틱까지 셌다)
            var fresh = await TrackAsync(p.transform.position + fw * 4f + right * 3f, ct);
            if (fresh != null) t = fresh;
            var go = new GameObject("ProbeSword");
            go.transform.position = t.M.transform.position + Vector3.up * 0.5f;
            var sword = go.AddComponent<LightSwordAgent>();
            var ctx = typeof(ItemEffectManager).GetField("_ctx", Inst)?.GetValue(GameRunBootstrapper.Instance.Run.EffectManager) as ItemEffectContext;
            sword.Init(spy, ctx, 1f, 0f, 9999f, RuneElement.Light, Vector3.forward, maxBounces: 99999, pierce: true, hitRadius: 1.5f);
            sword.PerTargetGap = 0.4f;
            SetHp(t.M, TankHp); last = TankHp;
            t.M.OnHPChanged += onHp;
            float tAt = Time.time;
            while (Time.time - tAt < 2f) await UniTask.Yield(ct);
            t.M.OnHPChanged -= onHp;
            UnityEngine.Object.Destroy(go);
            Check(sb, $"⑥ 관통 같은 대상 간격 0.4초 — 2초 동안 {hits}타(≤ 6 · 간격 없으면 약 25)", hits >= 3 && hits <= 6);
        }

        // ⑦ 어둠의 낫(처치) — 방 시작 2(+단계) → 처치 한 번에 +1
        {
            var fx = Legend("DarkLegendProjectile", "OnKill", 0.5f) as DarkLegendProjectileEffect;
            await UniTask.Yield(ct); await UniTask.Yield(ct);   // OnTick 한 번
            var fN = typeof(DarkLegendProjectileEffect).GetField("_scythes", Inst);
            int n0 = fx != null && fN != null ? (int)fN.GetValue(fx) : -1;
            var k = await TrackAsync(p.transform.position + fw * 3f + right * 2f, ct);
            if (k != null) { SetHp(k.M, 1); DealAs(p, k.M, WeaponActionType.GroundLight); }
            int n1 = fx != null && fN != null ? (int)fN.GetValue(fx) : -1;
            RemoveEffect(fx);
            int start = 2 + LegendaryTuning.Tier("DARK");
            Check(sb, $"⑦ 어둠의 낫(처치) — 방 시작 {n0}(= {start}) · 처치 뒤 {n1}(= {start + 1})", n0 == start && n1 == start + 1);
        }
        DetachAll();
    }

    private static async UniTask PoiseCellAsync(PlayerController p, StringBuilder sb, Vector3 fw, Vector3 right, CancellationToken ct)
    {
        MonsterBase elite = null;
        Vector3 farPos = p.transform.position + fw * 8f + right * 2.5f;
        if (UnityEngine.AI.NavMesh.SamplePosition(farPos, out var nh, 4f, UnityEngine.AI.NavMesh.AllAreas)) farPos = nh.position;
        try { elite = await Managers.ObjectPooler.SpawnAsync<MonsterBase>("Werewolf/Werewolf", ObjectPoolerManager.PoolType.Monster, farPos, Quaternion.identity); }
        catch (Exception e) { sb.AppendLine("  (정예 소환 실패 " + e.Message + ")"); }
        string row = "정예 없음"; bool ok = false;
        var diag = new StringBuilder();
        if (elite != null)
        {
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 6f && elite.IsDamageImmuneNow) await UniTask.Yield(ct);
            await Wait(2f, ct);
            var poise = elite.Poise;
            diag.Append($"등급 {elite.Grade} · 포이즈 {(poise != null ? "있음" : "없음")} · 면역 {elite.IsDamageImmuneNow}");
            // 포이즈 실측(MonsterPoiseProbeEditor ③)과 같은 길 — 멀리 두고 추격 상태에서 몰아 때린다(공격 확약 중이면 깨지지 않으므로)
            async UniTask<(bool before, bool at)> Count(string path, int n)
            {
                elite.Status.ApplyCc("probe_hold", 9999f);
                await EquipAsync(p, path, ct);
                await Wait(3.3f, ct);   // 포이즈 회복(3초)
                elite.Status.ClearCc("probe_hold");
                if (elite.TryGetComponent<UnityEngine.AI.NavMeshAgent>(out var ag) && ag.isActiveAndEnabled && ag.isOnNavMesh) ag.Warp(farPos);
                else elite.transform.position = farPos;
                elite.ChangeState<ChaseState>();
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
                SetHp(elite, TankHp);
                DealAs(p, elite, WeaponActionType.GroundLight); SetHp(elite, TankHp);
                diag.Append($" | {Path.GetFileNameWithoutExtension(path)} 1타 뒤 줄 {(poise != null ? poise.Fill01 : -1f):0.00} · 사망 {elite.IsDead} · 특수 {elite.IsInSpecialState} · 확약 {elite.IsInState<AttackState>()} · 상태 {(elite.IsInState<ChaseState>() ? "추격" : elite.IsInState<GetHitState>() ? "경직" : "기타")}");
                for (int i = 1; i < n - 1; i++) { DealAs(p, elite, WeaponActionType.GroundLight); SetHp(elite, TankHp); }
                bool before = poise != null && poise.IsBroken;
                DealAs(p, elite, WeaponActionType.GroundLight); SetHp(elite, TankHp);
                bool at = poise != null && poise.IsBroken;
                diag.Append($" · 끝 줄 {(poise != null ? poise.Fill01 : -1f):0.00}");
                await Wait(1.8f, ct);   // 휘청 1.5초가 끝나게
                return (before, at);
            }
            var g = await Count(Great, 4);
            var k = await Count(Nameless, 9);
            ok = poise != null && !g.before && g.at && !k.before && k.at;
            row = $"대검 3타 깨짐 {g.before} · 4타 {g.at} / 무형검 8타 깨짐 {k.before} · 9타 {k.at}";
            elite.gameObject.SetActive(false);
        }
        Check(sb, "④ A2 정예 포이즈 = 발동 계수 — " + row, ok);
        sb.AppendLine("   ④ 진단: " + diag);
    }

    private static async UniTask BuildSynergy1Async(PlayerController p, StringBuilder sb, CancellationToken ct, bool poiseOnly = false)
    {
        sb.AppendLine("빌드 시너지 계획 1 — 바뀐 칸 7칸(유물 없음)");
        var rs  = p.RuntimeStats;
        var run = GameRunBootstrapper.Instance.Run;
        var spy = new ReportSpy();
        Vector3 fw = Flat(p.transform.forward), right = Vector3.Cross(Vector3.up, fw);
        s_home = p.transform.position;
        if (poiseOnly)
        {
            foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
                if (m != null && !m.IsDead) m.gameObject.SetActive(false);
            await PoiseCellAsync(p, sb, fw, right, ct);
            return;
        }

        // ① K1 — 정오 공속은 자기 칸: 패시브 +0.2 → 정오 +0.3 켬 · 끔 → 패시브 몫이 남는다
        {
            var fBonus = typeof(PlayerRuntimeStats).GetField("_bonusAttackSpeed", Inst);
            float prevBonus = fBonus != null ? (float)fBonus.GetValue(rs) : 0f;
            rs.SetZenithAttackSpeed(0f);
            float baseAs = rs.AttackSpeedMultiplier;
            fBonus?.SetValue(rs, prevBonus + 0.2f);
            rs.SetZenithAttackSpeed(0.3f);
            float on = rs.AttackSpeedMultiplier;
            rs.SetZenithAttackSpeed(0f);
            float off = rs.AttackSpeedMultiplier;
            fBonus?.SetValue(rs, prevBonus);
            rs.SetZenithAttackSpeed(0f);
            Check(sb, $"① K1 정오 공속 자기 칸 — 기준 {baseAs:0.00} · 패시브 +0.2 · 정오 켬 {on:0.00}(≈ +0.5) · 끔 {off:0.00}(≈ +0.2 남음)",
                  Mathf.Abs(on - (baseAs + 0.5f)) < 0.011f && Mathf.Abs(off - (baseAs + 0.2f)) < 0.011f);
        }

        // ② K3 · R0 — 스탯 룬 계열 · 절반 셈: 치명 스탯 룬 1개 = 필살 +0 · 2개 = +1 · 공속 스탯 룬 2개 = 연격 +1
        {
            var placed = typeof(RunItemInventory).GetField("_placedItems", Inst)?.GetValue(run.ItemInventory) as List<RuntimeItemData>;
            var added = new List<RuntimeItemData>();
            RuntimeItemData Make(string id) { var d = RuntimeItemData.FromServer(Managers.ItemData.GetItem(id)); if (d != null) { d.element = "LIGHT"; } return d; }
            int crit0 = BuildImprint.Count(BuildFamily.Crit), combo0 = BuildImprint.Count(BuildFamily.Combo);
            int crit1 = -1, crit2 = -1, combo2 = -1;
            try
            {
                var a = Make("item_t1_keen_eye"); if (a != null && placed != null) { placed.Add(a); added.Add(a); }
                run.EffectManager.Rebuild(); crit1 = BuildImprint.Count(BuildFamily.Crit);
                var b = Make("item_t1_destruction_sigil"); if (b != null && placed != null) { placed.Add(b); added.Add(b); }
                run.EffectManager.Rebuild(); crit2 = BuildImprint.Count(BuildFamily.Crit);
                var c = Make("item_t1_enhanced_tendon"); if (c != null && placed != null) { placed.Add(c); added.Add(c); }
                var d = Make("item_t2_sharp_instinct"); if (d != null && placed != null) { placed.Add(d); added.Add(d); }
                run.EffectManager.Rebuild(); combo2 = BuildImprint.Count(BuildFamily.Combo);
            }
            finally
            {
                foreach (var x in added) placed?.Remove(x);
                run.EffectManager.Rebuild();
            }
            Check(sb, $"② K3 스탯 룬 절반 셈 — 필살 {crit0} → 치명 룬 1개 {crit1}(+0) · 2개 {crit2}(+1) · 연격 {combo0} → 공속 룬 2개 {combo2}(+1) · 룬 {added.Count}/4",
                  added.Count == 4 && crit1 == crit0 && crit2 == crit0 + 1 && combo2 == combo0 + 1);
        }

        var slime = await TrackAsync(p.transform.position + fw * 2f, ct);
        if (slime == null) { sb.AppendLine("슬라임 부르기 실패"); return; }
        foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
            if (m != null && !m.IsDead && m != slime.M) m.gameObject.SetActive(false);

        // ③ A1 — 발동 계수 · 강타: 무형검 평타 0.7(강타 아님) · 대검 평타 1.6(강타) · 대검 R 2.5 · 활 0.6 · 석궁 1.5(강타)
        {
            var rows = new List<string>(); bool ok = true;
            foreach (var (path, label, kind, ranged, want, heavy) in new[]
            {
                (Nameless, "무형검 평타", WeaponActionType.GroundLight, false, 0.7f, false),
                (Great,    "대검 평타",   WeaponActionType.GroundLight, false, 1.6f, true),
                (Great,    "대검 R",      WeaponActionType.RSkill,      false, 2.5f, true),
                (Bow,      "활 한 발",    WeaponActionType.GroundLight, true,  0.6f, false),
                (Crossbow, "석궁 한 발",  WeaponActionType.GroundLight, true,  1.5f, true),
            })
            {
                if (!await EquipAsync(p, path, ct)) { rows.Add(label + " 장착 실패"); ok = false; continue; }
                await Wait(0.3f, ct);
                InjectEffect(spy);   // 장착이 효과를 다시 짓는다 — 매번 다시 끼운다
                spy.Seen.Clear();
                RefillAll();
                DealAs(p, slime.M, kind, ranged);
                var r = spy.Seen.Count > 0 ? spy.Seen[spy.Seen.Count - 1] : default;
                bool rowOk = spy.Seen.Count > 0 && Mathf.Abs(r.ProcCoef - want) < 0.01f && r.IsHeavy == heavy;
                ok &= rowOk;
                rows.Add($"{label} {r.ProcCoef:0.0}{(r.IsHeavy ? " 강타" : "")}{(rowOk ? "" : $" ✗(원함 {want:0.0}{(heavy ? " 강타" : "")})")}");
                RemoveEffect(spy);
            }
            Check(sb, "③ A1 발동 계수 · 강타 — " + string.Join(" · ", rows), ok);
        }

        // ④ A2 — 정예 포이즈 = 계수만큼: 대검 평타 3타 안 깨짐 · 4타 휘청 / 무형검 8타 안 깨짐 · 9타 휘청
        await PoiseCellAsync(p, sb, fw, right, ct);

        // ⑤ K7 — 룬 빙결 같은 대상 2초 간격: 대검(계수 1.6 → 확률 100% · 지속 1.6초) 0초 걸림 · 2.0초 안 걸림 · 3.8초 걸림
        {
            await EquipAsync(p, Great, ct);
            await Wait(0.3f, ct);
            var freeze = new FreezeEffect(new ItemEffectSlot { effectType = "Freeze", trigger = "Always", value = 1f, duration = 1f });
            InjectEffect(freeze);
            float t0 = Time.time;
            DealAs(p, slime.M, WeaponActionType.GroundLight); bool f0 = slime.M.Status.HasCc("freeze");
            while (Time.time - t0 < 2.0f) await UniTask.Yield(ct);
            DealAs(p, slime.M, WeaponActionType.GroundLight); bool f2 = slime.M.Status.HasCc("freeze");
            while (Time.time - t0 < 3.8f) await UniTask.Yield(ct);
            DealAs(p, slime.M, WeaponActionType.GroundLight); bool f38 = slime.M.Status.HasCc("freeze");
            RemoveEffect(freeze);
            slime.M.Status.ClearCc("freeze");
            RefillAll();
            Check(sb, $"⑤ K7 빙결 재적용 간격 — 0초 {f0} · 2.0초 {f2}(거짓이어야) · 3.8초 {f38}", f0 && !f2 && f38);
        }

        // ⑥ R1 — 같은 적 3회 격발: 세 마리를 번갈아(광역 휘두름처럼) A B C A B C A — 주 대상 A 셋째에 터진다
        {
            var b = await TrackAsync(p.transform.position + fw * 2f + right * 1.2f, ct);
            var c = await TrackAsync(p.transform.position + fw * 2f - right * 1.2f, ct);
            var burst = new TriggerBurstEffect(new ItemEffectSlot { effectType = "TriggerBurst", trigger = "SameTarget", value = 0.01f, value2 = 3f, value3 = 0f });
            burst.SetElement("LIGHT");
            InjectEffect(burst);
            var fIcd = typeof(TriggerBurstEffect).GetField("_icdUntil", Inst);
            var fStreak = typeof(TriggerBurstEffect).GetField("_streak", Inst);
            int CountNow() { var st = fStreak?.GetValue(burst); return st != null ? (int)st.GetType().GetProperty("Count").GetValue(st) : -1; }
            var order = new[] { slime.M, b?.M, c?.M, slime.M, b?.M, c?.M };
            foreach (var m in order) { DealAs(p, m, WeaponActionType.GroundLight); RefillAll(); await UniTask.Yield(ct); }
            int countBefore = CountNow();
            bool firedBefore = fIcd != null && (float)fIcd.GetValue(burst) > 0f;
            DealAs(p, slime.M, WeaponActionType.GroundLight); RefillAll();
            bool fired = fIcd != null && (float)fIcd.GetValue(burst) > 0f;
            RemoveEffect(burst);
            if (b?.M != null) b.M.gameObject.SetActive(false);
            if (c?.M != null) c.M.gameObject.SetActive(false);
            Check(sb, $"⑥ R1 같은 적 3회 — A B C A B C 뒤 주 대상 셈 {countBefore}(= 2) · 터짐 {firedBefore}(거짓) → A 셋째 터짐 {fired}",
                  countBefore == 2 && !firedBefore && fired);
        }

        // ⑦ A2 — 무게 내려찍기가 주 피해(강타)로: 적중 사건 GroundHeavy · 계수 2.5 강타 · 발동 1회(재귀 없음)
        {
            await EquipAsync(p, Great, ct);
            await Wait(1f, ct);
            var form = WeaponFormRuntime.Of(p)?.Current;
            string row = "무게 형 없음"; bool ok = false;
            if (form is WeightForm)
            {
                Vector3 at = p.transform.position + Flat(p.AimForward) * 1.5f;
                var t = await TrackAsync(at, ct);
                InjectEffect(spy); spy.Seen.Clear();
                int heavyEvents = 0, triggered = 0;
                Action<HitInfo> onHit = h => { if (h.ActionType == WeaponActionType.GroundHeavy) heavyEvents++; };
                Action<string> onTrig = _ => triggered++;
                HitFeedbackService.OnHit += onHit;
                form.Triggered += onTrig;
                try
                {
                    var fake = new HitInfo(p.gameObject, p.gameObject, p.transform.position, p.transform.forward, 1f, false, WeaponActionType.GroundLight);
                    for (int i = 0; i < form.MaxStacks; i++) form.OnMeleeHit(fake, true, false);
                    var real = new HitInfo(p.gameObject, t.M.gameObject, t.M.transform.position, p.transform.forward, 10f, false, WeaponActionType.GroundLight);
                    form.OnMeleeHit(real, true, true);
                    await Wait(0.3f, ct);
                }
                finally { HitFeedbackService.OnHit -= onHit; form.Triggered -= onTrig; RemoveEffect(spy); }
                var heavyReports = spy.Seen.FindAll(r => r.ActionType == WeaponActionType.GroundHeavy);
                bool coefOk = heavyReports.Count > 0 && heavyReports.TrueForAll(r => Mathf.Abs(r.ProcCoef - 2.5f) < 0.01f && r.IsHeavy);
                ok = heavyEvents >= 1 && coefOk && triggered == 1;
                row = $"적중 사건 GroundHeavy {heavyEvents} · 보고 {heavyReports.Count}(계수 2.5 강타 {coefOk}) · 발동 {triggered}회";
            }
            Check(sb, "⑦ A2 무게 내려찍기 주 피해 — " + row, ok);
        }
        DetachAll();
    }

    // ── 38 평타 초당 피해 R2(10-13) — 예산 실측과 같은 셈(단일 표적 · 정착 3초 + 45초 · 평타만) ──
    private static async UniTask R2Async(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        var cases = new (string path, string label)[] { (Nameless, "무형검"), (Katana, "카타나"), (Great, "대검") };
        sb.AppendLine("평타 초당 피해 R2 — 형 끔 · 강화 0 · 단일 표적 · 정착 3초 + 측정 45초 · 무형검 = 1(기획 R2: 카타나 · 대검 1.0 ~ 1.1)");
        float baseDps = 0f, basePerAtk = 0f;
        if (await EquipAsync(p, cases[0].path, ct))   // 예열 — 첫 장착 칸이 평타 0회로 재였다(예산 실측)
        {
            float w0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - w0 < 2f) { p.InputBuffer.Push(Command.Light); await Wait(0.25f, ct); }
            await Wait(1f, ct);
        }
        foreach (var c in cases)
        {
            Managers.UI?.CloseAllPopupUI();
            Time.timeScale = 1f;
            if (!await EquipAsync(p, c.path, ct)) { sb.AppendLine($"장착 실패 {c.label}"); continue; }
            var w = p.WeaponManager.Weapon0Data;
            w.enhanceLevel = 0;
            w.RecomputeEnhancedStats();
            w.formTier = 0;
            w.formVariants = "";
            WeaponFormRuntime.Of(p).Rebuild();
            p.WeaponManager.RaiseEquippedWeaponRefreshed();
            DetachAll();
            foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
                if (m != null && !m.IsDead) m.gameObject.SetActive(false);
            await Wait(0.5f, ct);
            s_home = p.transform.position;
            Vector3 f = Flat(p.transform.forward);
            var tr = await TrackAsync(p.transform.position + f * 1.6f, ct);
            if (tr == null) { sb.AppendLine($"슬라임 부르기 실패 {c.label}"); continue; }
            float settle = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - settle < 3f) { RefillAll(); Steer(p, tr.M.transform.position); p.InputBuffer.Push(Command.Light); await Wait(0.25f, ct); }
            foreach (var t in s_tracks) t.Dealt = 0f;
            int swings = 0, hits = 0, crits = 0; float basic = 0f, basicNoCrit = 0f, lastHitAt = -1f;
            Action<HitInfo> onHit = h =>
            {
                if (h.Attacker != p.gameObject || h.ActionType != WeaponActionType.GroundLight || tr.M == null || h.Target != tr.M.gameObject) return;
                basic += h.Damage; hits++;
                if (h.IsCritical) crits++; else basicNoCrit += h.Damage;
                if (Time.time - lastHitAt > 0.12f) swings++;
                lastHitAt = Time.time;
            };
            HitFeedbackService.OnHit += onHit;
            try
            {
                float t0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - t0 < 45f) { RefillAll(); Steer(p, tr.M.transform.position); p.InputBuffer.Push(Command.Light); await Wait(0.25f, ct); }
            }
            finally { HitFeedbackService.OnHit -= onHit; }
            int atk = p.RuntimeStats != null ? p.RuntimeStats.MeleeAttack : 0;
            float dps = basic / 45f;
            float nonCritHit = hits - crits > 0 ? basicNoCrit / (hits - crits) : 0f;
            float dpsNoCrit = nonCritHit * hits / 45f;   // 치명을 빼고 본 초당 피해(타당 평균 × 타 수) — 치명 운을 걷어낸 비교
            float perAtk = atk > 0 ? dpsNoCrit / atk : 0f;
            if (c.path == Nameless) { baseDps = dpsNoCrit; basePerAtk = perAtk; }
            string rel = baseDps > 0f && c.path != Nameless ? $" · 무형검 대비 {dpsNoCrit / baseDps:0.00}배 · 공격력 1당 {(basePerAtk > 0f ? perAtk / basePerAtk : 0f):0.00}배" : "";
            sb.AppendLine($"{c.label}: 공격력 {atk} · 초당 피해 {dps:0.0}(치명 뺌 {dpsNoCrit:0.0}) · 맞힘 {hits}(휘두름 {swings} · {swings / 45f:0.00}/초) · 치명 {crits} · 타당(치명 뺌) {nonCritHit:0.0}{rel}");
        }
        DetachAll();
    }

    // ── 6 연출 후보 비교 ────────────────────────────────────────
    // (경로, 크기, 내려찍기 여부)
    // 1회차: 입장 부감 카메라 중 촬영. 2회차: 풍절 = Slash wave(정면 초승달 검기)가 맞다 · 특수기술 팩 내려찍기 3종은 금빛 폭발+화면 어둡힘 / 붉은 가시 / 불 폭발이라
    // 「무게」의 묵직한 충격파와 다름 → 3회차: 바닥 충격파 계열(Hovl 노바 · 운석 착탄 · 대검 피격) + Slash wave 크게
    // 4회차: 실측 3 화면에서 내려찍기 0.25초에 화면 반을 덮는 베이지 막 — 노바 ×3 단독으로 크기별 확인
    private static readonly (string path, float scale, bool slam)[] FxCandidates =
    {
        ("Assets/RelicFairy/_Imported/EffectSource/Hovl Studio/AAA Projectiles Vol 1/Prefabs/Hit 18 nova orange.prefab", 3f, true),
        ("Assets/RelicFairy/_Imported/EffectSource/Hovl Studio/AAA Projectiles Vol 1/Prefabs/Hit 18 nova orange.prefab", 2f, true),
        ("Assets/RelicFairy/_Imported/EffectSource/Hovl Studio/AAA Projectiles Vol 1/Prefabs/Hit 18 nova orange.prefab", 1.5f, true),
    };

    private static async UniTask FxAsync(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        await Wait(4f, ct);   // 첫 전투방 입장 부감 카메라가 끝날 때까지(1회차는 부감 중에 찍혔다)
        var cam = Camera.main;
        Vector3 fwd = Flat(p.transform.forward);
        for (int i = 0; i < FxCandidates.Length; i++)
        {
            var (path, scale, slam) = FxCandidates[i];
            HideStrays();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) { sb.AppendLine($"#{i} 로드 실패 {path}"); continue; }
            // 게임과 같은 자리 — 풍절: 플레이어 가슴 높이에서 정면으로 · 내려찍기: 정면 1.5 m 바닥
            Vector3 at = slam ? p.transform.position + fwd * 1.5f : p.transform.position + Vector3.up;
            var go = UnityEngine.Object.Instantiate(prefab, at, Quaternion.LookRotation(fwd));
            go.transform.localScale *= scale;
            float[] times = { 0.06f, 0.15f, 0.3f };
            float prev = 0f;
            for (int k = 0; k < times.Length; k++)
            {
                await Wait(times[k] - prev, ct);
                prev = times[k];
                CaptureMain(cam, $"Temp/weapon_form_fx_{i}_{(char)('a' + k)}.png");
            }
            UnityEngine.Object.Destroy(go);
            sb.AppendLine($"#{i} {Path.GetFileNameWithoutExtension(path)} ×{scale}");
            await Wait(1f, ct);
        }
    }

    /// <summary>게임 카메라 화면 그대로 한 장(UI 오버레이 제외) — LancelotQSwordProbeEditor.CaptureMain과 같다.</summary>
    private static void CaptureMain(Camera cam, string path)
    {
        if (cam == null) return;
        int w = 960, h = 540;
        var rt = new RenderTexture(w, h, 24);
        var prevTarget = cam.targetTexture;
        cam.targetTexture = rt;
        cam.Render();
        cam.targetTexture = prevTarget;
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        File.WriteAllBytes(path, tex.EncodeToPNG());
        UnityEngine.Object.Destroy(rt);
        UnityEngine.Object.Destroy(tex);
    }

    // ── 8 평타 이펙트 방향 ──────────────────────────────────────
    // 사용자(10-07): 「검의 방향과 이펙트의 방향을 맞춰야 해」 — 평타 휘두름 이펙트. 슬래시는 몸 방향 × 단계별 고정 회전(WeaponAbilitySO
    // rotationEuler)으로 나오고 칼 뼈대를 보지 않는다. 이펙트가 나오는 순간(SpawnSlashEffectN) 칼끝 이동 방향을 등 뒤 시점 각도로 잰다
    // (오른쪽 = 0°, 위 = 90°, 왼쪽 = 180°, 아래 = −90°). 등 뒤 카메라로 +1프레임 · +0.08초를 찍는다. 기준: 슬래시 프리팹 회전 0° · 90°.
    private static readonly string[] SlashWeapons = { Nameless, Katana, Great };

    private static async UniTask SlashDirAsync(PlayerController p, StringBuilder sb, CancellationToken ct, bool refOnly = false, string onlyPath = null)
    {
        Directory.CreateDirectory("Temp/ui_shots");
        var camGo = new GameObject("~SlashProbeCam");
        var cam = camGo.AddComponent<Camera>();
        if (Camera.main != null) cam.CopyFrom(Camera.main);
        cam.enabled = false;
        cam.fieldOfView = 50f;
        var recv = p.GetComponentInChildren<PlayerAnimationEventReceiver>();
        try
        {
            // 기준 촬영 — 슬래시 프리팹을 몸 앞에 회전 0° · 90°(Z)로 띄워 어느 쪽에서 어느 쪽으로 쓰는지 본다
            HideStrays();
            Vector3 fwd0 = Flat(p.AimForward);
            var slashPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/RelicFairy/Prefabs/UseEffect/PlayerEffect/SlashAttack.prefab");
            foreach (float roll in new[] { 0f, 90f })
            {
                if (slashPrefab == null || onlyPath != null) break;
                var rot = Quaternion.LookRotation(fwd0) * Quaternion.Euler(0f, 0f, roll);
                var fx = UnityEngine.Object.Instantiate(slashPrefab, p.transform.position + Vector3.up + fwd0 * 0.5f, rot);
                float[] at = refOnly ? new[] { 0.005f, 0.02f, 0.04f, 0.07f } : new[] { 0.03f, 0.1f, 0.2f };   // 8b: 초승달이 어느 끝에서 자라는지
                float prev = 0f;
                for (int k = 0; k < at.Length; k++)
                {
                    await Wait(at[k] - prev, ct); prev = at[k];
                    PlaceBehind(cam, p, fwd0);
                    CaptureMain(cam, $"Temp/ui_shots/WF_Slash_ref_roll{roll:0}_{(char)('a' + k)}.png");
                }
                UnityEngine.Object.Destroy(fx);
                await Wait(0.5f, ct);
            }

            foreach (var path in SlashWeapons)
            {
                if (refOnly) break;
                if (onlyPath != null && path != onlyPath) continue;
                if (!await EquipAsync(p, path, ct)) { sb.AppendLine($"장착 실패 {path}"); continue; }
                sb.AppendLine("   " + s_equipNote);
                string wname = Path.GetFileNameWithoutExtension(path);
                var wi = p.WeaponManager.CurrentWeaponInstance != null ? p.WeaponManager.CurrentWeaponInstance.GetComponentInChildren<WeaponInstance>() : null;
                var tip = wi != null ? wi.tipPoint : null;
                if (tip == null) { sb.AppendLine($"{wname}: 칼끝(tipPoint) 없음"); continue; }
                HideStrays();
                await Wait(0.6f, ct);
                s_home = p.transform.position;
                Vector3 fwd = Flat(p.AimForward);
                Vector3 aim = p.transform.position + fwd * 4f;

                var samples = new List<(float t, Vector3 local)>(512);
                var normT   = new List<float>(512);   // 같은 순간의 공격 클립 진행(0~1) — 칼끝이 가장 빠른 순간을 클립 진행으로 옮겨 이벤트 자리를 정한다
                var events  = new List<(float t, int evt, int combo, float nt)>(8);
                Action<int> onEvt = n =>
                {
                    events.Add((Time.time, n, p.Combo.CurrentComboStep, ClipNt(p)));
                    int combo = p.Combo.CurrentComboStep;
                    UniTask.Void(async () =>
                    {
                        await UniTask.Yield(PlayerLoopTiming.PostLateUpdate);
                        PlaceBehind(cam, p, fwd);
                        CaptureMain(cam, $"Temp/ui_shots/WF_Slash_{wname}_c{combo}_e{n}_a.png");
                        await UniTask.Delay(80, DelayType.DeltaTime);
                        PlaceBehind(cam, p, fwd);
                        CaptureMain(cam, $"Temp/ui_shots/WF_Slash_{wname}_c{combo}_e{n}_b.png");
                    });
                };
                if (recv != null) recv.OnEffectStep += onEvt;
                // 0.3배속 — 카타나(1.8배속)는 한 프레임에 칼끝이 크게 움직여 방향이 회차마다 흔들렸다(10-07 2 · 4회차). 판정 · 전진은 게임 시간이라 그대로다
                const float Slow = 0.3f;
                Time.timeScale = Slow;
                try
                {
                    float t0 = Time.realtimeSinceStartup, nextPush = 0f;
                    while (Time.realtimeSinceStartup - t0 < 3.2f / Slow)
                    {
                        // 마지막 타는 입력을 멈춰 회수까지 보게 한다 — 연타하면 0.35(연결 지점)에서 다음 콤보로 끊겨 늦게 오는 휘두름이 안 보였다(대검 02_5)
                        bool finisherNow = p.Combo.CurrentComboStep >= Mathf.Max(1, p.WeaponManager.Weapon0Data.groundEndCount) - 1;
                        if (Time.realtimeSinceStartup - t0 < 2.2f / Slow && Time.realtimeSinceStartup >= nextPush && !finisherNow)
                        {
                            nextPush = Time.realtimeSinceStartup + 0.15f;
                            AimAt(aim); RequestFace(p, aim);
                            HideStrays();   // 파도 몬스터가 조준 보정을 끌어가면 몸이 돌아 칼끝 기록이 흔들린다
                            p.InputBuffer.Push(Command.Light);
                        }
                        // 몸 기준 좌표 — 슬래시가 몸 transform 기준으로 나온다. 1회차는 명령 정면 기준이라 몸이 도는 것 · 전진이 칼 휘두름에 섞였다
                        samples.Add((Time.time, p.transform.InverseTransformPoint(tip.position)));
                        normT.Add(ClipNt(p));
                        await UniTask.Yield(PlayerLoopTiming.Update, ct);
                    }
                }
                finally { Time.timeScale = 1f; if (recv != null) recv.OnEffectStep -= onEvt; }

                var abil = p.WeaponManager.Weapon0Data;
                foreach (var e in events)
                {
                    Vector3 a = TipAt(samples, e.t - 0.03f), b = TipAt(samples, e.t + 0.03f), at = TipAt(samples, e.t);
                    Vector3 v = b - a;
                    float ang = Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg;
                    // 슬래시가 그려지는 동안(나온 뒤 0.06초 — 8b: 0.04초에 거의 다 그려진다)의 칼끝 방향. 카타나는 1.8배속이라 ±0.03초 창이 휘두름 시작 전후를 섞었다(10-07 2회차)
                    Vector3 va = TipAt(samples, e.t + 0.06f) - at;
                    float angAfter = Mathf.Atan2(va.y, va.x) * Mathf.Rad2Deg;
                    // 실제 휘두름 — 이벤트가 든 클립 구간 전체(클립 진행이 되감기는 자리까지)에서 칼끝이 가장 크게 움직인 0.06초 구간.
                    // 10-07: ±0.12초 안만 보던 것은 대검 사무라이 1타처럼 이벤트가 휘두름에서 멀리 있으면 찾지 못했다(0.07 m)
                    int ei = 0; while (ei < samples.Count - 1 && samples[ei].t < e.t) ei++;
                    int lo = ei, hi = ei;
                    while (lo > 0 && normT[lo - 1] <= normT[lo] + 1e-4f) lo--;
                    while (hi < samples.Count - 1 && normT[hi + 1] + 1e-4f >= normT[hi]) hi++;
                    float segA = samples[lo].t - e.t, segB = samples[hi].t - e.t - 0.06f;
                    float bestOff = 0f, bestLen = -1f; Vector3 bestV = Vector3.zero;
                    for (float o = segA; o <= segB + 1e-4f; o += 0.01f)
                    {
                        Vector3 dv = TipAt(samples, e.t + o + 0.06f) - TipAt(samples, e.t + o);
                        if (dv.magnitude > bestLen) { bestLen = dv.magnitude; bestV = dv; bestOff = o; }
                    }
                    float angSwing = Mathf.Atan2(bestV.y, bestV.x) * Mathf.Rad2Deg;
                    float rollFit = Mathf.DeltaAngle(0f, angSwing - 180f);
                    float swingNt0 = NtAt(samples, normT, e.t + bestOff), swingNt1 = NtAt(samples, normT, e.t + bestOff + 0.06f);
                    // 큰 움직임 상위 3구간(겹치지 않게) — 들어 올리기(예비 동작)와 베기를 가른다(10-07: 대검 1타 34° ↔ −110°가 회차마다 바뀜)
                    var wins = new List<(float off, float len, Vector3 v)>();
                    for (float o = segA; o <= segB + 1e-4f; o += 0.01f)
                    {
                        Vector3 dv = TipAt(samples, e.t + o + 0.06f) - TipAt(samples, e.t + o);
                        wins.Add((o, dv.magnitude, dv));
                    }
                    wins.Sort((x, y) => y.len.CompareTo(x.len));
                    var top = new List<(float off, float len, Vector3 v)>();
                    foreach (var w in wins)
                    {
                        if (top.Count >= 3) break;
                        if (top.Exists(q => Mathf.Abs(q.off - w.off) < 0.06f)) continue;
                        top.Add(w);
                    }
                    top.Sort((x, y) => x.off.CompareTo(y.off));
                    var topTxt = new StringBuilder();
                    foreach (var w in top)
                        topTxt.Append($" [클립 {NtAt(samples, normT, e.t + w.off):0.00} {Mathf.Atan2(w.v.y, w.v.x) * Mathf.Rad2Deg:0}° {w.len:0.0} m (가로 {w.v.x:+0.0;-0.0} 세로 {w.v.y:+0.0;-0.0} 앞뒤 {w.v.z:+0.0;-0.0})]");
                    sb.AppendLine($"   {wname} 콤보 {e.combo} 큰 움직임:{topTxt}");
                    // 칼끝이 가장 빠른 순간 — 이벤트가 든 같은 클립 안에서(클립 진행이 줄면 다른 클립) 앞뒤 한 칸 중심 차분
                    int peak = -1; float peakSpeed = 0f;
                    for (int i = 1; i < samples.Count - 1; i++)
                    {
                        if (Mathf.Abs(samples[i].t - e.t) > 0.45f) continue;
                        if (normT[i + 1] < normT[i] || normT[i] < normT[i - 1]) continue;   // 클립이 바뀌는 자리
                        float dt = samples[i + 1].t - samples[i - 1].t;
                        if (dt <= 1e-4f) continue;
                        float sp = (samples[i + 1].local - samples[i - 1].local).magnitude / dt;
                        if (sp > peakSpeed) { peakSpeed = sp; peak = i; }
                    }
                    string peakTxt = "없음";
                    if (peak > 0)
                    {
                        Vector3 pv = samples[peak + 1].local - samples[peak - 1].local;
                        peakTxt = $"{samples[peak].t - e.t:+0.00;-0.00}초 · 클립 {normT[peak]:0.00} · {peakSpeed:0.0} m/s · 그때 방향 {Mathf.Atan2(pv.y, pv.x) * Mathf.Rad2Deg:0}°";
                    }
                    sb.AppendLine($"{wname} 콤보 {e.combo} · 이벤트 {e.evt}(클립 {e.nt:0.00}): 휘두름 {angSwing:0}° ({bestLen:0.00} m · 이벤트 {bestOff:+0.00;-0.00}초 · 클립 {swingNt0:0.00}~{swingNt1:0.00}) → 맞는 회전 {rollFit:0} · 직후 0.06초 {angAfter:0}° ({va.magnitude:0.00} m) · 앞뒤 0.03초 {ang:0}° (가로 {v.x:+0.00;-0.00} · 세로 {v.y:+0.00;-0.00} · 앞뒤 {v.z:+0.00;-0.00} m/0.06초) · 칼끝 자리 (오른쪽 {at.x:0.00} · 높이 {at.y:0.00} · 앞 {at.z:0.00}) · 가장 빠른 순간 {peakTxt}");
                }
                if (events.Count == 0) sb.AppendLine($"{wname}: 슬래시 이벤트 0건");
                await Wait(1f, ct);
            }
        }
        finally { UnityEngine.Object.Destroy(camGo); }
    }

    private static void PlaceBehind(Camera cam, PlayerController p, Vector3 fwd)
    {
        Vector3 pos = p.transform.position;
        cam.transform.position = pos - fwd * 3.4f + Vector3.up * 2.1f;
        cam.transform.LookAt(pos + fwd * 1.2f + Vector3.up * 1.0f);
    }

    private static Vector3 TipAt(List<(float t, Vector3 local)> s, float t)
    {
        if (s.Count == 0) return Vector3.zero;
        if (t <= s[0].t) return s[0].local;
        for (int i = 1; i < s.Count; i++)
        {
            if (s[i].t < t) continue;
            float span = s[i].t - s[i - 1].t;
            return Vector3.Lerp(s[i - 1].local, s[i].local, span > 1e-5f ? (t - s[i - 1].t) / span : 1f);
        }
        return s[s.Count - 1].local;
    }

    // ── 9 대검 콤보 후보 ────────────────────────────────────────
    // 사용자(10-07): Grruzam 7연속 콤보는 뼈가 깨진다(UE 골격 리타깃) → 다른 애니메이션. 대검을 쥔 채 지상 1~3타 상태에 후보 클립을 덮어 씌워
    // 각 클립의 15 · 30 · 45 · 60% 순간을 옆 앞쪽에서 찍는다. 결과 Temp/ui_shots/WF_GreatCand_<후보>_<타>_<순간>.png
    private const string GS = "Assets/_ThirdParty/GhostSamurai_Animset/Animation/katana/APose/Attack/Inplace/GhostSamurai_APose_";
    private const string GI = "Assets/RelicFairy/_Imported/GhostSamurai_Animset/Animation/katana/APose/Attack/Inplace/GhostSamurai_APose_";
    // 10-07 오후 결정: 대검 = 사무라이 02_3 · 02_4 · 02_5(02_2는 무형검 · 카타나 E 스킬과 같은 동작). 앞선 후보 A~E 비교는 guide/shots WF_GreatCand_sheet_ABD
    // 10-07 오후: 대검 3타 02_5는 찌르기 — 사용자 「휘두르는 동작이어야」 → 사무라이 기본 공격 중 겹치지 않는 것(Attack01 = 무형검 · 카타나, 02_2 = E 스킬)
    private static readonly (string name, string[] clips)[] GreatCandidates =
    {
        ("G_3hit_a", new[] { GS + "Attack02_6_Inplace.FBX", GS + "Attack03_2_Inplace.FBX", GS + "Attack03_3_Inplace.FBX" }),
        ("G_3hit_b", new[] { GS + "Attack03_4_Inplace.FBX", GS + "Attack03_5_Inplace.FBX", GI + "Attack02_5_Inplace.FBX" }),
    };

    private static async UniTask GreatCandidatesAsync(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        if (!await EquipAsync(p, Great, ct)) { sb.AppendLine("대검 장착 실패"); return; }
        var svc = typeof(PlayerController).GetField("_animSvc", Inst)?.GetValue(p);
        var ovr = svc?.GetType().GetMethod("Override", new[] { typeof(string), typeof(AnimationClip) });
        if (ovr == null || p.Anim == null) { sb.AppendLine("덮어쓰기 서비스 없음"); return; }
        Directory.CreateDirectory("Temp/ui_shots");
        var camGo = new GameObject("~GreatCandCam");
        var cam = camGo.AddComponent<Camera>();
        if (Camera.main != null) cam.CopyFrom(Camera.main);
        cam.enabled = false;
        cam.fieldOfView = 45f;
        float[] at = { 0.05f, 0.1f, 0.15f, 0.2f, 0.25f, 0.3f, 0.35f, 0.42f, 0.5f, 0.6f };
        try
        {
            foreach (var (name, clips) in GreatCandidates)
            {
                for (int k = 0; k < clips.Length; k++)
                {
                    AnimationClip clip = null;
                    foreach (var o in AssetDatabase.LoadAllAssetsAtPath(clips[k]))
                        if (o is AnimationClip c && !c.name.StartsWith("__preview__")) { clip = c; break; }
                    if (clip == null) { sb.AppendLine($"{name} {k + 1}타: 클립 없음 {clips[k]}"); continue; }
                    string state = $"GroundLightAttack_0{k + 1}";
                    bool ok = (bool)ovr.Invoke(svc, new object[] { state, clip });
                    HoldStrays();   // 끄면 방이 클리어돼 보상 연출(시간 정지)로 멈췄다(1회차) — 묶어만 둔다
                    await Wait(0.3f, ct);
                    Vector3 fwd = Flat(p.transform.forward), right = Vector3.Cross(Vector3.up, fwd);
                    // 미리보기 동안만 상태 기계를 끈다 — 켜 두면 공격 상태가 아니라서 곧바로 대기 자세로 되돌렸다(1회차: 첫 장만 공격 자세)
                    p.enabled = false;
                    try
                    {
                        p.Anim.speed = 1f;
                        p.Anim.Play(state, 0, 0f);
                        for (int i = 0; i < at.Length; i++)
                        {
                            float guard = Time.realtimeSinceStartup;
                            while (Time.realtimeSinceStartup - guard < 4f)
                            {
                                var info = p.Anim.GetCurrentAnimatorStateInfo(0);
                                if (info.IsName(state) && info.normalizedTime >= at[i]) break;
                                await UniTask.Yield(PlayerLoopTiming.Update, ct);
                            }
                            Vector3 pos = p.transform.position;
                            cam.transform.position = pos + right * 2.6f + fwd * 1.8f + Vector3.up * 1.5f;
                            cam.transform.LookAt(pos + Vector3.up * 1.0f + fwd * 0.5f);
                            CaptureMain(cam, $"Temp/ui_shots/WF_GreatCand_{name}_{k + 1}_{i}.png");
                        }
                    }
                    finally { p.enabled = true; }
                    sb.AppendLine($"{name} {k + 1}타: {clip.name} · {clip.length:0.00}초 · 덮어쓰기 {ok}");
                    await Wait(0.6f, ct);
                }
            }
        }
        finally
        {
            UnityEngine.Object.Destroy(camGo);
            await EquipAsync(p, Great, ct);   // 덮어쓴 클립을 대검 원래 클립으로 되돌린다
        }
    }

    // ── 16 환영베기 연타 모션 후보(10-08 · 기획 C2) ─────────────────────
    // 환영베기는 평타 콤보를 0.12초마다 갈아 끼워 스킬로 안 보인다 → 전용 연타 모션. 무기끼리 겹치면 안 된다(사용자 10-07):
    // 무형검 · 카타나 평타 = 사무라이 Attack01 계열 · 대검 = 02_3 · 02_4 · 03_5 · 기본 E 칸(ESkill_01) = 02_2 · 기본 QSkill_01 = SPAttack02 ·
    // 유물 Q = SPAttack01 → 남은 16개. 카타나를 쥔 채 지상 1타 상태에 후보를 덮어 한 번씩 틀며 오른손(몸 기준) 속도로 휘두름 수 · 순간을 재고,
    // 휘두름이 많은 7개만 다시 틀어 6장씩 찍는다. 결과 Temp/ui_shots/WF_PhantomCand_<후보>_<순간>.png
    private static readonly string[] PhantomCandidates =
    {
        "Attack02_1_ALL", "Attack02_5", "Attack02_6", "Attack02_6_ALL", "Attack03_1_ALL", "Attack03_2", "Attack03_3", "Attack03_4", "Attack03_4_ALL",
        "Attack04", "Attack05", "Attack06", "Attack07", "SPAttack04", "SPAttack05", "SPAttack06",
    };

    private static AnimationClip LoadSamuraiClip(string n)
    {
        foreach (var root in new[] { GI, GS })
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(root + n + "_Inplace.FBX"))
                if (o is AnimationClip c && !c.name.StartsWith("__preview__")) return c;
        return null;
    }

    /// <summary>오른손 속도 봉우리 = 휘두름. 3칸 평균 · 최고의 45%(최소 2.5 m/s) 이상 · 0.12초 안 겹친 봉우리는 하나로.</summary>
    private static (int count, string times) CountSwings(List<(float nt, float v)> s, float length)
    {
        int n = s.Count;
        if (n < 3) return (0, "");
        var sm = new float[n];
        for (int i = 0; i < n; i++) sm[i] = (s[Mathf.Max(0, i - 1)].v + s[i].v + s[Mathf.Min(n - 1, i + 1)].v) / 3f;
        float max = 0f;
        foreach (var v in sm) max = Mathf.Max(max, v);
        float th = Mathf.Max(2.5f, max * 0.45f);
        int count = 0; float last = -1f;
        var times = new StringBuilder();
        for (int i = 1; i < n - 1; i++)
        {
            if (sm[i] < th || sm[i] < sm[i - 1] || sm[i] < sm[i + 1]) continue;
            float t = s[i].nt * length;
            if (last >= 0f && t - last < 0.12f) continue;
            count++; last = t;
            times.Append(times.Length > 0 ? " " : "").Append(t.ToString("0.00"));
        }
        return (count, times.ToString());
    }

    private static async UniTask PhantomCandidatesAsync(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        if (!await EquipAsync(p, Katana, ct)) { sb.AppendLine("카타나 장착 실패"); return; }
        var svc = typeof(PlayerController).GetField("_animSvc", Inst)?.GetValue(p);
        var ovr = svc?.GetType().GetMethod("Override", new[] { typeof(string), typeof(AnimationClip) });
        var hand = p.Anim != null ? p.Anim.GetBoneTransform(HumanBodyBones.RightHand) : null;
        if (ovr == null || hand == null) { sb.AppendLine("덮어쓰기 서비스 · 오른손 뼈 없음"); return; }
        Directory.CreateDirectory("Temp/ui_shots");
        const string State = "GroundLightAttack_01";
        var camGo = new GameObject("~PhantomCandCam");
        var cam = camGo.AddComponent<Camera>();
        if (Camera.main != null) cam.CopyFrom(Camera.main);
        cam.enabled = false;
        cam.fieldOfView = 45f;
        float[] shotsAt = { 0.1f, 0.25f, 0.4f, 0.55f, 0.7f, 0.85f };

        // 한 번 튼다 — shotName이 있으면 shotsAt 순간마다 찍고(이때 속도는 안 쓴다), 없으면 프레임마다 오른손 속도를 모은다
        async UniTask<List<(float nt, float v)>> PlayOnce(AnimationClip clip, string shotName)
        {
            ovr.Invoke(svc, new object[] { State, clip });
            HoldStrays();
            await Wait(0.3f, ct);
            var samples = new List<(float, float)>();
            p.enabled = false;   // 미리보기 동안만 상태 기계를 끈다(메뉴 9와 같은 이유 — 켜 두면 곧바로 대기 자세로 되돌린다)
            try
            {
                p.Anim.speed = 1f;
                p.Anim.Play(State, 0, 0f);
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
                Vector3 prev = p.transform.InverseTransformPoint(hand.position);
                int shot = 0;
                float guard = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - guard < clip.length + 2f)
                {
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                    var info = p.Anim.GetCurrentAnimatorStateInfo(0);
                    if (!info.IsName(State) || info.normalizedTime >= 1f) break;
                    Vector3 cur = p.transform.InverseTransformPoint(hand.position);
                    if (shotName == null && Time.deltaTime > 0f) samples.Add((info.normalizedTime, (cur - prev).magnitude / Time.deltaTime));
                    prev = cur;
                    if (shotName != null && shot < shotsAt.Length && info.normalizedTime >= shotsAt[shot])
                    {
                        Vector3 pos = p.transform.position, fwd = Flat(p.transform.forward), right = Vector3.Cross(Vector3.up, fwd);
                        cam.transform.position = pos + right * 2.6f + fwd * 1.8f + Vector3.up * 1.5f;
                        cam.transform.LookAt(pos + Vector3.up * 1.0f + fwd * 0.5f);
                        CaptureMain(cam, $"Temp/ui_shots/WF_PhantomCand_{shotName}_{shot}.png");
                        shot++;
                    }
                }
            }
            finally { p.enabled = true; }
            await Wait(0.4f, ct);
            return samples;
        }

        var measured = new List<(string n, AnimationClip clip, int swings, string times)>();
        try
        {
            foreach (var n in PhantomCandidates)
            {
                var clip = LoadSamuraiClip(n);
                if (clip == null) { sb.AppendLine($"{n}: 클립 없음"); continue; }
                var (count, times) = CountSwings(await PlayOnce(clip, null), clip.length);
                measured.Add((n, clip, count, times));
                sb.AppendLine($"{n}: {clip.length:0.00}초 · 휘두름 {count}회 @ {times}초");
            }
            measured.Sort((a, b) => b.swings != a.swings ? b.swings.CompareTo(a.swings) : a.clip.length.CompareTo(b.clip.length));
            sb.AppendLine("— 촬영(휘두름 많은 순 7개): " + string.Join(", ", measured.GetRange(0, Mathf.Min(7, measured.Count)).ConvertAll(m => m.n)));
            for (int i = 0; i < Mathf.Min(7, measured.Count); i++)
                await PlayOnce(measured[i].clip, measured[i].n);
        }
        finally
        {
            UnityEngine.Object.Destroy(camGo);
            await EquipAsync(p, Katana, ct);   // 덮어쓴 클립을 카타나 원래 클립으로 되돌린다
        }
    }

    // ── 17 석궁 추가탄 범위(10-08 강화 점검) ─────────────────────────
    // 더 빨리쏘기 「기본 공격에 추가 공격」 — 발사 퍼널(CombatSpawner.ApplyModifiers)에 같은 요청을 행동 종류만 바꿔 넣어 발 수를 비교한다.
    private static async UniTask ExtraShotAsync(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        await Wait(1f, ct);
        var apply = typeof(CombatSpawner).GetMethod("ApplyModifiers", BindingFlags.Static | BindingFlags.NonPublic);
        if (apply == null) { sb.AppendLine("ApplyModifiers 없음"); return; }
        int Count(WeaponActionType t)
        {
            var req = ProjectileRequest.Create("probe", p.transform.position, p.transform.forward, 10f, p.gameObject, PlayerWeaponManager.Slot0);
            req.actionType = t;
            var args = new object[] { req };
            apply.Invoke(null, args);
            return ((ProjectileRequest)args[0]).count;
        }
        int prev = p.ExtraShotCount;
        try
        {
            p.ExtraShotCount = 0;
            int basic0 = Count(WeaponActionType.GroundLight);
            p.ExtraShotCount = 2;
            int basic = Count(WeaponActionType.GroundLight), e = Count(WeaponActionType.ESkill), r = Count(WeaponActionType.RSkill);
            Check(sb, $"추가탄 2 — 기본 공격 {basic0} → {basic}발", basic == basic0 + 2);
            Check(sb, $"추가탄 2 — 스킬 E {e}발 · R {r}발(기본 {basic0}과 같아야)", e == basic0 && r == basic0);
        }
        finally { p.ExtraShotCount = prev; }
    }

    // ── 18 환영베기 전용 연타(10-08 · 기획 C2) ─────────────────────────
    // 카타나 +0(1단계)로 앞 표적에 R — 난무 동안 상태가 QSkill_03 하나로 이어지는지 · 타격 5번 · 마지막만 마무리인지,
    // 타격 순간의 클립 시각이 휘두름 순간(실측 16)과 맞는지 잰다(±0.1초).
    private static async UniTask PhantomFlurryAsync(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        var actField = typeof(PlayerController).GetField("actStateDebug", Inst);
        bool InSkill() => (actField?.GetValue(p)?.ToString() ?? "").Contains("Skill");
        await Wait(3f, ct);
        if (!await EquipAsync(p, Katana, ct)) { sb.AppendLine("카타나 장착 실패"); return; }
        var wd = p.WeaponManager.Weapon0Data;
        if (wd != null) { wd.enhanceLevel = 0; wd.RecomputeEnhancedStats(); }
        await Wait(1f, ct);
        Vector3 f = Flat(p.transform.forward), at = p.transform.position;
        s_home = at;
        var t = await TrackAsync(at + f * 2f, ct);
        HideStrays();
        AimAt(at + f * 6f);
        await UniTask.DelayFrame(4, cancellationToken: ct);
        var hits = new List<(float t, bool fin)>();
        var hitClip = new List<string>();   // 타격 순간의 클립 시각 · 실제 경과 · 시간 배율
        var hitClipT = new List<float>();
        int others = 0;
        var states = new HashSet<string>();
        float t0 = Time.time, u0 = Time.unscaledTime;
        float ClipNow()
        {
            var info = p.Anim.IsInTransition(0) ? p.Anim.GetNextAnimatorStateInfo(0) : p.Anim.GetCurrentAnimatorStateInfo(0);
            return info.IsName("QSkill_03") ? info.normalizedTime * info.length * info.speed : -1f;
        }
        Action<HitInfo> onHit = h =>
        {
            if (h.Attacker != p.gameObject || t == null || h.Target != t.M.gameObject) return;
            if (h.ActionType != WeaponActionType.RSkill) { others++; return; }   // 유물 · 서약 등 다른 피해는 따로 센다(3회차 — 섞여 들어온 의심)
            hits.Add((Time.time - t0, h.IsFinisher));
            hitClipT.Add(ClipNow());
            hitClip.Add($"클립 {ClipNow():0.00}초 · 실제 {Time.unscaledTime - u0:0.00} · 배율 {Time.timeScale:0.00} · 프레임 {Time.frameCount}");
        };
        HitFeedbackService.OnHit += onHit;
        try
        {
            p.CooldownTracker.ResetCooldown(SkillType.R);
            p.InputBuffer.Push(Command.RSkill);
            float g = Time.time;
            while (!InSkill() && Time.time - g < 0.5f) await UniTask.Yield(PlayerLoopTiming.Update, ct);
            t0 = Time.time; u0 = Time.unscaledTime;
            while (Time.time - t0 < 1.3f)
            {
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
                var info = p.Anim.IsInTransition(0) ? p.Anim.GetNextAnimatorStateInfo(0) : p.Anim.GetCurrentAnimatorStateInfo(0);
                if (hits.Count < 5) states.Add(info.IsName("QSkill_03") ? "QSkill_03" : info.shortNameHash.ToString());
            }
        }
        finally { HitFeedbackService.OnHit -= onHit; }
        await WaitEnd(InSkill, ct);
        DetachAll();

        var hitDesc = new StringBuilder();
        foreach (var h in hits) hitDesc.Append(hitDesc.Length > 0 ? " " : "").Append(h.t.ToString("0.00")).Append(h.fin ? "(막타)" : "");
        bool finOk = hits.Count == 5 && hits[4].fin && !hits.GetRange(0, 4).Exists(h => h.fin);
        Check(sb, $"환영베기 전용 연타 — 상태 [{string.Join(",", states)}] · 타격 {hits.Count}번 · 마지막만 막타 {finOk}",
              states.Count == 1 && states.Contains("QSkill_03") && finOk);
        for (int i = 0; i < hitClip.Count; i++) sb.AppendLine($"   {i + 1}타 — {hitClip[i]}");
        sb.AppendLine($"   R 스킬이 아닌 피해 {others}번");
        // 손 속도 봉우리는 타격 경직(시간 배율 0.04) 프레임에서 튀어 못 잡았다(2회차) — 클립 시각을 미리보기(실측 16)로 잰 휘두름 순간과 바로 댄다.
        // 연타 3번 1.04 · 1.28 · 1.59초 사이에 앞 4타를 고르게(1.04 · 1.22 · 1.41 · 1.59), 5타 = 큰 마무리 2.71초
        float[] want = { 1.04f, 1.223f, 1.407f, 1.59f, 2.71f };
        bool aligned = hitClipT.Count == 5;
        for (int i = 0; aligned && i < 5; i++) aligned = Mathf.Abs(hitClipT[i] - want[i]) <= 0.1f;
        Check(sb, $"타격 순간 = 클립 휘두름 — 클립 {string.Join(" ", hitClipT.ConvertAll(x => x.ToString("0.00")))} · 목표 1.04 1.22 1.41 1.59 2.71(±0.1) · 실제 타격 {hitDesc}", aligned);
    }

    // ── 19 플레이어 의상(10-08 · 기획 플레이어의상_유물별덧입기) ─────────────
    // 테스트 허브 기본 유물로 — ① 유물 적용 = 1단계(망토 · 유물 색 견습 옷) ② 선명 조각 = 2단계(흉갑 · 어깨) ③ 찬란 = 3단계(치마 · 망토 테두리 빛)
    // ④ 회피 잔상 · 피격 반짝임이 모은 렌더러에 망토 · 흉갑이 있다 ⑤ 서 있을 때 · 회피 순간 뒤 옆 화면(망토 뚫림 눈으로). 화면 Temp/ui_shots/WF_Costume_*.png
    private static async UniTask CostumeAsync(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        await Wait(2f, ct);
        var costume = p.GetComponent<PlayerCostume>();
        var lo = AppBootstrapper.Instance != null ? AppBootstrapper.Instance.Loadout : null;
        if (costume == null || lo == null) { sb.AppendLine("PlayerCostume · Loadout 없음"); return; }
        const string Top = "SwordAndShiled_Cloth_Top", Pauldrons = "SwordAndShiled_Clotth_Pauldrons", Skirt = "SwordAndShiled_Cloth_Skirt";
        Transform Part(string n) => Util.FindDeepChild(p.transform, n);
        bool On(string n) { var t = Part(n); return t != null && t.gameObject.activeInHierarchy; }
        string Mat(string n) { var t = Part(n); var r = t != null ? t.GetComponent<Renderer>() : null; return r != null && r.sharedMaterial != null ? r.sharedMaterial.name : "-"; }
        string relic = p.RelicClass != null ? p.RelicClass.Id.ToString() : "없음";

        Check(sb, $"① 1단계({relic}) — 단계 {costume.Stage} · 견습 옷 {Mat("Squire_Cloth")} · 흉갑 {On(Top)}",
              costume.Stage == 1 && Mat("Squire_Cloth").StartsWith("Squire_") && Mat("Squire_Cloth") != "Squire_Cloth" && !On(Top));

        var camGo = new GameObject("~CostumeShotCam");
        var cam = camGo.AddComponent<Camera>();
        if (Camera.main != null) cam.CopyFrom(Camera.main);
        cam.enabled = false;
        cam.fieldOfView = 40f;
        void Shot(string name, Vector3 offset)
        {
            Vector3 pos = p.transform.position, fwd = Flat(p.transform.forward), right = Vector3.Cross(Vector3.up, fwd);
            cam.transform.position = pos + right * offset.x + Vector3.up * offset.y + fwd * offset.z;
            cam.transform.LookAt(pos + Vector3.up * 1.0f);
            CaptureMain(cam, $"Temp/ui_shots/WF_Costume_{name}.png");
        }
        Renderer Rend(string n) { var t = Part(n); return t != null ? t.GetComponent<Renderer>() : null; }
        // 단계 오름 뒤 0.4초 안에 그 부위에 디졸브가 걸리는지 · 디졸브 도중을 찍고 · 끝난 뒤(1.3초) 켜짐 + 원래(유물) 재질인지
        async UniTask<(bool dissolved, bool done)> Rise(Action raise, string[] parts, string shotName, Vector3 shotAt)
        {
            raise();
            bool seen = false;
            float t0 = Time.time;
            bool shot = false;
            while (Time.time - t0 < 0.45f)
            {
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
                foreach (var n in parts) if (DissolveEffect.IsDissolving(Rend(n))) seen = true;
                if (seen && !shot && Time.time - t0 > 0.3f) { Shot(shotName, shotAt); shot = true; }
            }
            if (!shot) Shot(shotName, shotAt);
            await Wait(1.3f, ct);
            bool done = true;
            foreach (var n in parts) if (!On(n) || DissolveEffect.IsDissolving(Rend(n)) || Mat(n).Contains("Dissolve")) done = false;
            return (seen, done);
        }
        Directory.CreateDirectory("Temp/ui_shots");

        const string ProbePart = "probe_costume_part";
        var (d2, ok2) = await Rise(() => lo.AddRelicPart(ProbePart, RelicMemoryGrade.Clear), new[] { Top, Pauldrons }, "rise2_mid", new Vector3(1.2f, 1.4f, 2.6f));
        Check(sb, $"② 선명 조각 — 단계 {costume.Stage} · 흉갑 · 어깨 디졸브 {d2} · 끝난 뒤 켜짐 · 재질 {Mat(Top)} {ok2} · 치마 {On(Skirt)}",
              costume.Stage == 2 && d2 && ok2 && Mat(Top).StartsWith("Shield_Sword_Cloth") && !On(Skirt));

        var (d3, ok3) = await Rise(() => lo.RaiseRelicPartGrade(ProbePart), new[] { Skirt }, "rise3_mid", new Vector3(0.6f, 1.5f, -3.2f));
        Check(sb, $"③ 찬란 — 단계 {costume.Stage} · 치마 디졸브 {d3} · 끝난 뒤 {ok3}",
              costume.Stage == 3 && d3 && ok3);

        // ④ 회피 잔상이 모은 렌더러(처음부터 꺼진 것까지)에 망토 · 흉갑 — 피격 반짝임도 같은 방식(includeInactive)이라 코드로 확인
        var dodgeArr = typeof(DodgePresentation).GetField("_renderers", Inst)?.GetValue(p.GetComponent<DodgePresentation>()) as SkinnedMeshRenderer[];
        bool topIn = false;
        if (dodgeArr != null) foreach (var r in dodgeArr) { if (r == null) continue; if (r.name == Top) topIn = true; }
        Check(sb, $"④ 회피 잔상 목록에 흉갑 {topIn}", topIn);

        try
        {
            Directory.CreateDirectory("Temp/ui_shots");
            Shot("idle_front", new Vector3(1.2f, 1.4f, 2.6f));   // 등 뒤는 시작 자리에서 벽에 막혔다(1회차)
            Shot("idle_side", new Vector3(3.0f, 1.3f, -0.6f));
            p.InputBuffer.Push(Command.Dodge);
            float tw = Time.time;
            while (p.LocoSM.CurrentId != LocoState.Dodge && Time.time - tw < 0.4f) await UniTask.Yield(PlayerLoopTiming.Update, ct);
            await Wait(0.08f, ct);
            Shot("dodge_side", new Vector3(3.0f, 1.3f, -0.6f));
            await Wait(0.12f, ct);
            Shot("dodge_back", new Vector3(0.6f, 1.5f, -3.2f));
        }
        finally { UnityEngine.Object.Destroy(camGo); }
        sb.AppendLine("⑤ 화면 Temp/ui_shots/WF_Costume_rise2_mid · rise3_mid(디졸브 도중) · idle_front · idle_side · dodge_side · dodge_back");
    }

    // ── 20 이어하기 유물 주소(10-08) ─────────────────────────────
    // 런 저장 relicKey = 유물 SO 이름 → 이어하기가 그 이름을 Addressables 주소로 불러온다(AppBootstrapper.RestoreRun). 두 유물이 실제로 불러와지는지.
    private static async UniTask RelicKeysAsync(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        await Wait(1f, ct);
        foreach (var key in new[] { "RelicClass_Gawain", "RelicClass_Lancelot" })
        {
            var so = await Managers.AddressableManager.TryLoadAssetAsync<RelicClassSO>(key);
            Check(sb, $"이어하기 유물 주소 {key} → {(so != null ? so.Id.ToString() + " · 의상 " + (so.Costume != null ? so.Costume.name : "없음") : "로드 실패")}",
                  so != null && key.EndsWith(so.Id.ToString()));
        }
    }

    // ── 21 저스트 회피 슬로모 중 스킬 피해 시각(10-09) ─────────────
    // 슬로모(게임 시간 0.35배) 동안 플레이어 애니메이터는 실제 시간(UnscaledTime)으로 돈다. 스킬 타이머가 게임 시간이면 동작보다 피해가 늦다.
    // 대검 R(최후의 일격 — 모으기 0.6초 뒤 내려찍기)을 평소 · 슬로모 중에 써서 시작 → 내려찍는 순간(범위 고리 PlayerRange_Sector가 뜸)까지 실제 시간을 댄다
    // (슬로모 쪽이 평소의 1.25배 안이면 통과). 피해로 재면 회피로 표적에서 멀어져 슬로모 쪽이 0이었다(1회차).
    private static async UniTask WitchTimeAsync(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        var actField = typeof(PlayerController).GetField("actStateDebug", Inst);
        bool InSkill() => (actField?.GetValue(p)?.ToString() ?? "").Contains("Skill");
        await Wait(2f, ct);
        if (!await EquipAsync(p, Great, ct)) { sb.AppendLine("대검 장착 실패"); return; }
        var wd = p.WeaponManager.Weapon0Data;
        if (wd != null) { wd.enhanceLevel = 0; wd.RecomputeEnhancedStats(); }
        await Wait(1f, ct);
        Vector3 f = Flat(p.transform.forward), at = p.transform.position;
        s_home = at;
        var t = await TrackAsync(at + f * 2.2f, ct);
        HideStrays();
        AimAt(at + f * 6f);
        await UniTask.DelayFrame(4, cancellationToken: ct);

        async UniTask<(float real, float ts, string mode)> CastAndTime()
        {
            p.CooldownTracker.ResetCooldown(SkillType.R);
            p.InputBuffer.Push(Command.RSkill);
            float g = Time.unscaledTime;
            while (!InSkill() && Time.unscaledTime - g < 1.5f) await UniTask.Yield(PlayerLoopTiming.Update, ct);
            float start = Time.unscaledTime;
            string mode = p.Anim != null ? p.Anim.updateMode.ToString() : "-";
            while (Time.unscaledTime - start < 4f)
            {
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
                if (GameObject.Find(SkillRangeRing.SectorName) != null) return (Time.unscaledTime - start, Time.timeScale, mode);
            }
            return (-1f, -1f, mode);
        }

        var normal = await CastAndTime();
        await WaitEnd(InSkill, ct);
        await Wait(1.0f, ct);
        if (t != null) t.Refill();

        // 저스트 회피 — 대시 중 피해 없는 판정으로 발동(TryEvadeHit) → 슬로모 + 플레이어 애니메이터 실제 시간
        p.InputBuffer.Push(Command.Dodge);
        float tw = Time.unscaledTime;
        while (p.LocoSM.CurrentId != LocoState.Dodge && Time.unscaledTime - tw < 0.4f) await UniTask.Yield(PlayerLoopTiming.Update, ct);
        bool evaded = p.TryEvadeHit(t != null ? t.M.gameObject : null);
        var witch = await CastAndTime();
        await WaitEnd(InSkill, ct);
        Check(sb, $"평소 최후의 일격 — 시작 → 내려찍기 {normal.real:0.00}초(실제) · 시간 배율 {normal.ts:0.00} · 애니메이터 {normal.mode}", normal.real > 0f);
        Check(sb, $"슬로모 중(저스트 회피 {evaded}) — 시작 → 내려찍기 {witch.real:0.00}초(실제) · 시간 배율 {witch.ts:0.00} · 애니메이터 {witch.mode} · 평소 대비 {(normal.real > 0f ? witch.real / normal.real : 0f):0.00}배",
              evaded && witch.real > 0f && normal.real > 0f && witch.real <= normal.real * 1.25f);
    }

    // ── 22 애니메이션 흐름 · 자연스러움(10-09 출시 전 점검) ─────────────
    // 무기 5종마다 평타 6번 → E → R → 회피를 돌린다. 1회차(기록)는 프레임마다 애니메이터 상태 · 뼈 위치를 적어
    //   ① 콤보 사이 대기 자세 깜빡임(공격 구간 안에서 이동 · 대기 상태로 빠짐) ② 뼈 튐(엉덩이 · 머리가 몸 기준 한 프레임에 10 cm 넘게 순간 이동)
    //   ③ 클립 없는 상태(T자세 위험)를 센다. 2회차(촬영)는 같은 순서를 0.3초마다 찍는다(촬영 멈춤이 뼈 튐으로 잡히지 않게 따로 돈다).
    // 결과 Temp/weapon_form_animflow.txt · 화면 Temp/ui_shots/WF_Anim_<무기>_<n>.png
    private static readonly string[] AnimStateNames =
    {
        "GroundLightAttack_01", "GroundLightAttack_02", "GroundLightAttack_03", "ESkill_01", "ESkill_02", "ESkill_03",
        "QSkill_01", "QSkill_02", "QSkill_03", "QSkill_Iasen", "Dodge", "MoveBlend", "Locomotion2D", "HitLaunch", "HitLand",
        "Empty", "Entrance", "RelicQ_Main", "RelicQ_Slash1", "RelicQ_Slash2", "RelicQ_Slash3", "HeavyCharge", "GroundHeavyAttack",
        "JumpBlend", "JumpLand", "RelicQ_Finish", "AttackReady", "New State",
    };

    // 실측 자리 — 방 안쪽 같은 곳으로 되돌리고 새로 나온 몬스터를 치운다. 1회차(10-09)는 이동 스킬 · 회피로 방 끝에서 떨어져
    // 낙하 사망(무적이어도 낙하는 죽는다) → 이후 입력이 막혀 나머지 무기가 아무것도 못 했고, 새 파도 몬스터도 섞였다.
    private static void GoHome(PlayerController p, Vector3 home, Quaternion rot)
    {
        HideStrays();
        if (p.TryGetComponent<Rigidbody>(out var rb)) { rb.position = home; rb.rotation = rot; rb.linearVelocity = Vector3.zero; }
        p.transform.SetPositionAndRotation(home, rot);
        p.RequestFacing(rot);
    }

    private static async UniTask AnimFlowAsync(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        var actField = typeof(PlayerController).GetField("actStateDebug", Inst);
        bool InSkill() => (actField?.GetValue(p)?.ToString() ?? "").Contains("Skill");
        var names = new Dictionary<int, string>();
        foreach (var n in AnimStateNames) names[Animator.StringToHash(n)] = n;
        string StateName(AnimatorStateInfo i) => names.TryGetValue(i.shortNameHash, out var n) ? n : "#" + i.shortNameHash;
        bool IsLoco(string n) => n == "MoveBlend" || n == "Locomotion2D" || n == "Empty";
        await Wait(5f, ct);   // 방 입장 연출(부감 · 봉인 약 2.6초)이 끝날 때까지 — 1회차는 첫 콤보가 입장 잠금에 끊겼다
        Vector3 home = p.transform.position + Flat(p.transform.forward) * 8f;
        Quaternion homeRot = Quaternion.LookRotation(Flat(p.transform.forward));
        Directory.CreateDirectory("Temp/ui_shots");
        var camGo = new GameObject("~AnimFlowCam");
        var cam = camGo.AddComponent<Camera>();
        if (Camera.main != null) cam.CopyFrom(Camera.main);
        cam.enabled = false;
        cam.fieldOfView = 40f;

        var weapons = new (string label, string path, int slot)[]
        {
            ("Nameless", Nameless, PlayerWeaponManager.Slot0), ("Katana", Katana, PlayerWeaponManager.Slot0), ("Great", Great, PlayerWeaponManager.Slot0),
            ("Bow", Bow, PlayerWeaponManager.Slot1), ("Crossbow", Crossbow, PlayerWeaponManager.Slot1),
        };
        try
        {
            foreach (var (label, path, slot) in weapons)
            {
                if (!await EquipAsync(p, path, ct, slot)) { sb.AppendLine($"{label}: 장착 실패"); continue; }
                await Wait(1f, ct);
                var hips = p.Anim.GetBoneTransform(HumanBodyBones.Hips);
                var head = p.Anim.GetBoneTransform(HumanBodyBones.Head);

                // 순서: 평타 6번(0.3초 간격) → E → R(활은 누르고 있기) → 회피
                async UniTask Sequence(Func<string, UniTask> tick)
                {
                    for (int i = 0; i < 6; i++)
                    {
                        p.InputBuffer.Push(Command.Light);
                        float t1 = Time.time;
                        while (Time.time - t1 < 0.3f) await tick("light");
                    }
                    float g = Time.time; while (Time.time - g < 1.0f) await tick("light_end");
                    p.CooldownTracker.ResetCooldown(SkillType.E); p.InputBuffer.Push(Command.ESkill);
                    g = Time.time; while (Time.time - g < 0.2f || (InSkill() && Time.time - g < 3f)) await tick("E");
                    p.CooldownTracker.ResetCooldown(SkillType.R);
                    if (label == "Bow") InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(Key.R));
                    else p.InputBuffer.Push(Command.RSkill);
                    g = Time.time; while (Time.time - g < 0.2f || (InSkill() && Time.time - g < 3.5f)) { await tick("R"); if (label == "Bow" && Time.time - g > 1.2f) InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState()); }
                    InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
                    p.InputBuffer.Push(Command.Dodge);
                    g = Time.time; while (Time.time - g < 0.9f) await tick("dodge");
                }

                // 1회차 — 기록(트레일 켬/끔 이벤트도 센다 — 무형검 AE_EndTrail이 클립 끝 뒤라 안 올 수 있다는 감사 지적)
                bool trailOn = false; int trailBegins = 0, trailEnds = 0, trailLeft = 0;
                Action onB = () => { trailOn = true; trailBegins++; };
                Action onE = () => { trailOn = false; trailEnds++; };
                p.EventReceiver.OnBeginTrail += onB;
                p.EventReceiver.OnEndTrail += onE;
                var visits = new List<string>();
                int locoInCombo = 0, pops = 0, empty = 0;
                var popNotes = new List<string>();
                Vector3 prevHip = Vector3.zero, prevHead = Vector3.zero;
                bool first = true, sawAttack = false;
                string last = "";
                async UniTask Record(string phase)
                {
                    await UniTask.Yield(PlayerLoopTiming.PostLateUpdate, ct);
                    var info = p.Anim.IsInTransition(0) ? p.Anim.GetNextAnimatorStateInfo(0) : p.Anim.GetCurrentAnimatorStateInfo(0);
                    string n = StateName(info);
                    if (n != last) { visits.Add(n); last = n; }
                    if (n.StartsWith("GroundLight")) sawAttack = true;
                    if (phase == "light" && sawAttack && IsLoco(n)) locoInCombo++;
                    if (phase == "light_end" && trailOn && IsLoco(n)) trailLeft++;
                    if (!p.Anim.IsInTransition(0) && p.Anim.GetCurrentAnimatorClipInfoCount(0) == 0 && !IsLoco(n)) empty++;
                    Vector3 hipL = p.transform.InverseTransformPoint(hips.position), headL = p.transform.InverseTransformPoint(head.position);
                    if (!first && Time.deltaTime > 0f && Time.deltaTime < 0.05f)
                    {
                        float dh = (hipL - prevHip).magnitude, dd = (headL - prevHead).magnitude;
                        if (dh > 0.10f || dd > 0.10f) { pops++; if (popNotes.Count < 4) popNotes.Add($"{phase}/{n} 엉덩이 {dh:0.00} · 머리 {dd:0.00}m"); }
                    }
                    prevHip = hipL; prevHead = headL; first = false;
                }
                GoHome(p, home, homeRot);
                await Wait(0.5f, ct);
                await Sequence(Record);
                p.EventReceiver.OnBeginTrail -= onB;
                p.EventReceiver.OnEndTrail -= onE;
                Check(sb, $"{label} 흐름 — 상태 {string.Join(">", visits)} · 콤보 중 대기 끼어듦 {locoInCombo}프레임 · 뼈 튐 {pops} · 클립 없음 {empty}" +
                          $" · 트레일 켬 {trailBegins}/끔 {trailEnds} · 평타 뒤 켜진 채 대기 {trailLeft}프레임" +
                          (popNotes.Count > 0 ? " · " + string.Join(" / ", popNotes) : ""),
                      locoInCombo == 0 && pops == 0 && empty == 0 && trailLeft == 0);
                await Wait(1.2f, ct);

                // 2회차 — 촬영(0.3초마다, 등 뒤 옆)
                int shot = 0; float lastShot = -1f;
                async UniTask Shoot(string phase)
                {
                    await UniTask.Yield(PlayerLoopTiming.PostLateUpdate, ct);
                    if (Time.time - lastShot < 0.3f || shot >= 20) return;
                    lastShot = Time.time;
                    Vector3 pos = p.transform.position, fwd = Flat(p.transform.forward), right = Vector3.Cross(Vector3.up, fwd);
                    cam.transform.position = pos + right * 2.4f - fwd * 1.6f + Vector3.up * 1.6f;
                    cam.transform.LookAt(pos + Vector3.up * 1.0f);
                    CaptureMain(cam, $"Temp/ui_shots/WF_Anim_{label}_{shot:00}_{phase}.png");
                    shot++;
                }
                GoHome(p, home, homeRot);
                await Wait(0.5f, ct);
                await Sequence(Shoot);
                await Wait(1.2f, ct);
            }
        }
        finally
        {
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
            UnityEngine.Object.Destroy(camGo);
        }
    }

    // ── 23 런 공용 이펙트 빈 칸 후보(10-09 출시 전 점검) ─────────────
    // RunFxSet에 Burst(찬란 기억 분출 · 기수 · 변덕 특성 · 쉼터 대장간)와 Orb(보스 자리에서 떠올라 날아오는 기억의 빛)가 처음부터 비어
    // 화면에 아무것도 안 나왔다. 게임과 같은 재생기(LichVfx.PlayEntry · 곱하기 틴트)에 금빛 틴트로 후보를 띄워 찍는다.
    // 결과 Temp/ui_shots/WF_RunFx_<n>_<a|b|c>.png
    private const string Efx = "Assets/RelicFairy/_Imported/EffectSource/";
    private static readonly (string path, float scale, bool loop)[] RunFxCandidates =
    {
        (Efx + "Spells Pack/LWRP(URP)/Particles_LWRP/Prefabs/Projectiles/Explosion/Explosion_Light_2_LWRP.prefab", 0.9f, false),
        (Efx + "Hovl Studio/AAA Projectiles Vol 1/Prefabs/Hit 17 nova violet.prefab", 0.9f, false),
        (Efx + "Hovl Studio/RPG VFX Bundle/Prefabs/Magic buffs and hits/Magic Sparks.prefab", 0.9f, false),
        (Efx + "Hovl Studio/RPG VFX Bundle/Prefabs/Magic buffs and hits/Lvl up.prefab", 0.9f, false),
        (Efx + "MasterStylizedProjectiles/Projectiles/SmallLightBullet/Prefabs/SmallLightBullet.prefab", 0.55f, true),
        (Efx + "Hovl Studio/RPG VFX Bundle/Random effect prefabs/Gold dot.prefab", 0.55f, true),
        (Efx + "INab Studio/Vfx Assets/Character Effects/Effect Prefabs/Holy.prefab", 0.55f, true),
    };

    private static async UniTask RunFxCandAsync(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        await Wait(4f, ct);
        Directory.CreateDirectory("Temp/ui_shots");
        var cam = Camera.main;
        var gold = new Color(1f, 0.82f, 0.45f, 1f);
        Vector3 fwd = Flat(p.transform.forward);
        for (int i = 0; i < RunFxCandidates.Length; i++)
        {
            var (path, scale, loop) = RunFxCandidates[i];
            HideStrays();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) { sb.AppendLine($"#{i} 로드 실패 {path}"); continue; }
            // 게임과 같은 높이 — 쓰러진 자리 0.4 m(분출) · 떠오른 빛은 1.6 m 위
            Vector3 at = p.transform.position + fwd * 2.5f + Vector3.up * (loop ? 1.6f : 0.4f);
            var entry = new LichVfxEntry { prefab = prefab, scale = 1f };
            var go = LichVfx.PlayEntry(entry, at, Quaternion.identity, scale, gold, loop);
            float[] times = loop ? new[] { 0.3f, 0.8f, 1.4f } : new[] { 0.08f, 0.2f, 0.45f };
            float prev = 0f;
            for (int k = 0; k < times.Length; k++)
            {
                await Wait(times[k] - prev, ct);
                prev = times[k];
                CaptureMain(cam, $"Temp/ui_shots/WF_RunFx_{i}_{(char)('a' + k)}.png");
            }
            if (loop) LichVfx.Stop(ref go);
            sb.AppendLine($"#{i} {Path.GetFileNameWithoutExtension(path)} ×{scale}{(loop ? " 반복" : "")}");
            await Wait(1.2f, ct);
        }
    }

    // ── 24 출시 점검 수정 확인(10-09) ─────────────────────────────
    // ① 일시정지 동안 쿨다운이 멈추는가 ② 회피 중 누른 스킬이 회피 중엔 시작하지 않는가 ③ 스킬 중 무기 교체가 막히는가
    // ④ 움직이며 쓴 스킬이 이동 동작으로 덮이지 않는가(카타나 E · 대검 E) ⑤ 석궁 강화를 서서 쓰면 시전 자세가 보이는가 ⑥ 움직이며 쓰면 바로 끝나는가
    private static async UniTask ReleaseFixAsync(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        var actField = typeof(PlayerController).GetField("actStateDebug", Inst);
        bool InSkill() => (actField?.GetValue(p)?.ToString() ?? "").Contains("Skill");
        var locoField = typeof(PlayerController).GetField("_locoSM", Inst);
        string Loco() { var sm = locoField?.GetValue(p); return sm?.GetType().GetProperty("CurrentId")?.GetValue(sm)?.ToString() ?? "?"; }
        int moveHash = Animator.StringToHash("MoveBlend");
        bool InMoveBlend() => (p.Anim.IsInTransition(0) ? p.Anim.GetNextAnimatorStateInfo(0) : p.Anim.GetCurrentAnimatorStateInfo(0)).shortNameHash == moveHash;
        void HoldW(bool on) => InputSystem.QueueStateEvent(Keyboard.current, on ? new KeyboardState(Key.W) : new KeyboardState());
        await Wait(5f, ct);   // 방 입장 연출이 끝날 때까지
        Vector3 home = p.transform.position + Flat(p.transform.forward) * 8f;
        Quaternion homeRot = Quaternion.LookRotation(Flat(p.transform.forward));
        try
        {
            // ① 일시정지 쿨다운
            p.CooldownTracker.StartCooldown(SkillType.E, 10f);
            float before = p.CooldownTracker.GetRemaining(SkillType.E);
            var pauseOwner = new object();
            TimeScaleArbiter.Acquire(pauseOwner, 0f, TimeScaleArbiter.Priority.Pause);
            float r0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - r0 < 1.5f) await UniTask.Yield(PlayerLoopTiming.Update, ct);
            float during = p.CooldownTracker.GetRemaining(SkillType.E);
            TimeScaleArbiter.Release(pauseOwner);
            Check(sb, $"① 일시정지 1.5초 동안 쿨다운 {before:0.00} → {during:0.00}", Mathf.Abs(before - during) < 0.05f);
            p.CooldownTracker.ResetCooldown(SkillType.E);
            await Wait(0.5f, ct);

            // ② 회피 중 스킬
            if (!await EquipAsync(p, Katana, ct)) { sb.AppendLine("카타나 장착 실패"); return; }
            GoHome(p, home, homeRot);
            await Wait(1f, ct);
            p.CooldownTracker.ResetCooldown(SkillType.E);
            p.InputBuffer.Push(Command.Dodge);
            await Wait(0.08f, ct);
            p.InputBuffer.Push(Command.ESkill);
            bool skillInDodge = false, sawDodge = false; float dodgeEnd = -1f, skillAt = -1f; float g0 = Time.time;
            while (Time.time - g0 < 1.6f)
            {
                await UniTask.Yield(PlayerLoopTiming.PostLateUpdate, ct);
                bool dodging = Loco() == "Dodge";
                if (dodging) sawDodge = true;
                if (dodging && InSkill()) skillInDodge = true;
                if (sawDodge && !dodging && dodgeEnd < 0f) dodgeEnd = Time.time - g0;
                if (InSkill() && skillAt < 0f) skillAt = Time.time - g0;
            }
            Check(sb, $"② 회피 중 누른 E — 회피 중 시작 {(skillInDodge ? "있음" : "없음")} · 회피 끝 {dodgeEnd:0.00}초 · 스킬 시작 {(skillAt < 0f ? "버려짐" : skillAt.ToString("0.00") + "초")}",
                  sawDodge && !skillInDodge);
            await WaitSkillEnd(InSkill, ct);
            await Wait(0.6f, ct);

            // ③ 스킬 중 무기 교체
            if (!await EquipAsync(p, Bow, ct, PlayerWeaponManager.Slot1)) { sb.AppendLine("활 장착 실패"); return; }
            await p.WeaponManager.SwitchToSlotAsync(PlayerWeaponManager.Slot0);
            GoHome(p, home, homeRot);
            await Wait(1f, ct);
            p.CooldownTracker.ResetCooldown(SkillType.E);
            p.InputBuffer.Push(Command.ESkill);
            g0 = Time.time; while (!InSkill() && Time.time - g0 < 1f) await UniTask.Yield(PlayerLoopTiming.Update, ct);
            bool started = InSkill();
            typeof(PlayerController).GetMethod("ChangeWeapon", Inst)?.Invoke(p, new object[] { 1 });
            await Wait(0.4f, ct);
            Check(sb, $"③ 스킬 중 무기 교체 — 스킬 시작 {started} · 지금 칸 {p.WeaponManager.CurrentSlotIndex}", started && p.WeaponManager.CurrentSlotIndex == PlayerWeaponManager.Slot0);
            await WaitSkillEnd(InSkill, ct);
            await Wait(0.6f, ct);

            // ④ 움직이며 스킬 — 스킬 동안 이동 블렌드로 덮인 프레임 수
            foreach (var (label, path) in new[] { ("카타나 E", Katana), ("대검 E", Great) })
            {
                if (!await EquipAsync(p, path, ct)) { sb.AppendLine($"{label} 장착 실패"); continue; }
                GoHome(p, home, homeRot);
                await Wait(1f, ct);
                HoldW(true);
                await Wait(0.5f, ct);
                p.CooldownTracker.ResetCooldown(SkillType.E);
                p.InputBuffer.Push(Command.ESkill);
                g0 = Time.time; while (!InSkill() && Time.time - g0 < 1f) await UniTask.Yield(PlayerLoopTiming.Update, ct);
                int skillFrames = 0, covered = 0;
                while (InSkill() && Time.time - g0 < 4f)
                {
                    await UniTask.Yield(PlayerLoopTiming.PostLateUpdate, ct);
                    if (!InSkill()) break;
                    skillFrames++;
                    if (InMoveBlend()) covered++;
                }
                HoldW(false);
                Check(sb, $"④ {label} 움직이며 — 스킬 {skillFrames}프레임 중 이동 동작 {covered}", skillFrames > 0 && covered == 0);
                await Wait(1f, ct);
            }

            // ⑤ ⑥ 석궁 강화(연사 E) — 서서 / 움직이며
            if (!await EquipAsync(p, Crossbow, ct, PlayerWeaponManager.Slot1)) { sb.AppendLine("석궁 장착 실패"); return; }
            GoHome(p, home, homeRot);
            await Wait(1f, ct);
            p.CooldownTracker.ResetCooldown(SkillType.E);
            p.InputBuffer.Push(Command.ESkill);
            g0 = Time.time; float skillLen = 0f; int poseFrames = 0;
            while (Time.time - g0 < 1.2f)
            {
                await UniTask.Yield(PlayerLoopTiming.PostLateUpdate, ct);
                if (InSkill()) { skillLen = Time.time - g0; if (!InMoveBlend()) poseFrames++; }
            }
            Check(sb, $"⑤ 석궁 강화 서서 — 스킬 상태 {skillLen:0.00}초 · 시전 자세 {poseFrames}프레임", skillLen > 0.25f && poseFrames > 5);
            GoHome(p, home, homeRot);
            await Wait(1f, ct);
            HoldW(true);
            await Wait(0.5f, ct);
            p.CooldownTracker.ResetCooldown(SkillType.E);
            p.InputBuffer.Push(Command.ESkill);
            g0 = Time.time; float moveSkillLen = 0f; int notWalking = 0;
            while (Time.time - g0 < 0.8f)
            {
                await UniTask.Yield(PlayerLoopTiming.PostLateUpdate, ct);
                if (InSkill()) moveSkillLen = Time.time - g0;
                if (Time.time - g0 > 0.2f && !InMoveBlend()) notWalking++;
            }
            HoldW(false);
            Check(sb, $"⑥ 석궁 강화 움직이며 — 스킬 상태 {moveSkillLen:0.00}초 · 0.2초 뒤 걷기 아닌 프레임 {notWalking}", moveSkillLen < 0.1f && notWalking == 0);
        }
        finally
        {
            HoldW(false);
        }
    }

    // ── 26 겹침 줄이기 — 동작 후보 미리보기(10-09) ─────────────────────
    // 겹침: 무형검 · 카타나 평타(사무라이 Attack01 계열) · 랜슬롯 Q 베기(카타나 평타 복제) · 카타나 E = 무형검 R(QSkill_Iasen) ·
    // 석궁 = 활(평타 1 · 2 · E · R) · 가웨인 Q = 랜슬롯 Q 마무리(SPAttack01).
    // 아무 무기도 안 쓰는 사무라이 동작(카타나를 쥐고) · 활 팩 동작(석궁을 쥐고)을 지상 1타 칸에 덮어 한 번씩 튼다 —
    // 오른손 속도로 휘두름 수 · 순간을 재고, 진행 0.1~0.85에서 6장씩 찍는다. 결과 Temp/ui_shots/WF_AnimCand_<후보>_<n>.png
    // 빠진 것: Attack04 · 05 · 06(104초 깨짐) · SPAttack05(환영베기) · SPAttack01(유물 Q) · Attack02_2(기본 E 칸 = 무형검 E) · Grruzam(뼈 깨짐)
    private const string BowT = "Assets/RelicFairy/Animations/Player/Test_01/Attack/GhostSamurai_Bow_";
    private const string BowP = "Assets/_ThirdParty/GhostSamurai_Animset/Animation/Bow/Attack/Inplace/GhostSamurai_Bow_";
    private static readonly (string label, string path, bool bow)[] AnimCandidates =
    {
        ("Attack02_1_ALL", GI + "Attack02_1_ALL_Inplace.FBX", false), ("Attack02_5", GI + "Attack02_5_Inplace.FBX", false),
        ("Attack02_6", GI + "Attack02_6_Inplace.FBX", false), ("Attack03_1_ALL", GI + "Attack03_1_ALL_Inplace.FBX", false),
        ("Attack03_2", GI + "Attack03_2_Inplace.FBX", false), ("Attack03_3", GI + "Attack03_3_Inplace.FBX", false),
        ("Attack03_4", GI + "Attack03_4_Inplace.FBX", false), ("Attack07", GI + "Attack07_Inplace.FBX", false),
        ("SPAttack04", GI + "SPAttack04_Inplace.FBX", false), ("SPAttack06", GI + "SPAttack06_Inplace.FBX", false),
        ("Bow_Shoot_SP01", BowT + "Shoot_SP01_Inplace.FBX", true), ("Bow_Shoot_SP02", BowT + "Shoot_SP02_Inplace.FBX", true),
        ("Bow_Shoot_Start", BowT + "Shoot_Start_Inplace.FBX", true), ("Bow_Shoot_End", BowT + "Shoot_End_Inplace.FBX", true),
        ("Bow_SP_Enhance", BowT + "Shoot_SP_Enhance_Inplace.FBX", true), ("Bow_SP_Start", BowT + "Shoot_SP_Start_Inplace.FBX", true),
        ("Bow_RainShoot_Burst", BowT + "RainShoot_Burst_Inplace.FBX", true), ("Bow_RainShoot_Enhance", BowP + "RainShoot_Enhance_Inplace.FBX", true),
        ("Bow_RainShoot_Start", BowP + "RainShoot_Start_Inplace.FBX", true),
    };

    private static AnimationClip LoadClipAt(string path)
    {
        if (!File.Exists(path)) path = path.Replace(GI, GS);
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
            if (o is AnimationClip c && !c.name.StartsWith("__preview__")) return c;
        return null;
    }

    private static async UniTask AnimCandidatesAsync(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        var svc = typeof(PlayerController).GetField("_animSvc", Inst)?.GetValue(p);
        var ovr = svc?.GetType().GetMethod("Override", new[] { typeof(string), typeof(AnimationClip) });
        var hand = p.Anim != null ? p.Anim.GetBoneTransform(HumanBodyBones.RightHand) : null;
        if (ovr == null || hand == null) { sb.AppendLine("덮어쓰기 서비스 · 오른손 뼈 없음"); return; }
        Directory.CreateDirectory("Temp/ui_shots");
        const string State = "GroundLightAttack_01";
        var camGo = new GameObject("~AnimCandCam");
        var cam = camGo.AddComponent<Camera>();
        if (Camera.main != null) cam.CopyFrom(Camera.main);
        cam.enabled = false;
        cam.fieldOfView = 45f;
        float[] shotsAt = { 0.1f, 0.25f, 0.4f, 0.55f, 0.7f, 0.85f };
        Vector3 home = p.transform.position + Flat(p.transform.forward) * 8f;
        Quaternion homeRot = Quaternion.LookRotation(Flat(p.transform.forward));

        async UniTask<List<(float nt, float v)>> PlayOnce(AnimationClip clip, string shotName)
        {
            ovr.Invoke(svc, new object[] { State, clip });
            GoHome(p, home, homeRot);
            await Wait(0.3f, ct);
            var samples = new List<(float, float)>();
            p.enabled = false;   // 미리보기 동안만 상태 기계를 끈다(켜 두면 곧바로 대기 자세로 되돌린다)
            try
            {
                p.Anim.speed = 1f;
                p.Anim.Play(State, 0, 0f);
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
                Vector3 prev = p.transform.InverseTransformPoint(hand.position);
                int shot = 0;
                float guard = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - guard < clip.length + 2f)
                {
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                    var info = p.Anim.GetCurrentAnimatorStateInfo(0);
                    if (!info.IsName(State) || info.normalizedTime >= 1f) break;
                    Vector3 cur = p.transform.InverseTransformPoint(hand.position);
                    if (shotName == null && Time.deltaTime > 0f) samples.Add((info.normalizedTime, (cur - prev).magnitude / Time.deltaTime));
                    prev = cur;
                    if (shotName != null && shot < shotsAt.Length && info.normalizedTime >= shotsAt[shot])
                    {
                        Vector3 pos = p.transform.position, fwd = Flat(p.transform.forward), right = Vector3.Cross(Vector3.up, fwd);
                        cam.transform.position = pos + right * 2.6f + fwd * 1.8f + Vector3.up * 1.5f;
                        cam.transform.LookAt(pos + Vector3.up * 1.0f + fwd * 0.5f);
                        CaptureMain(cam, $"Temp/ui_shots/WF_AnimCand_{shotName}_{shot}.png");
                        shot++;
                    }
                }
            }
            finally { p.enabled = true; }
            await Wait(0.4f, ct);
            return samples;
        }

        bool holdingBow = false;
        try
        {
            if (!await EquipAsync(p, Katana, ct)) { sb.AppendLine("카타나 장착 실패"); return; }
            await Wait(4f, ct);   // 방 입장 연출이 끝날 때까지
            home = p.transform.position + Flat(p.transform.forward) * 8f;
            homeRot = Quaternion.LookRotation(Flat(p.transform.forward));
            foreach (var (label, path, bow) in AnimCandidates)
            {
                if (bow && !holdingBow)
                {
                    if (!await EquipAsync(p, Crossbow, ct, PlayerWeaponManager.Slot1)) { sb.AppendLine("석궁 장착 실패"); break; }
                    holdingBow = true;
                    svc = typeof(PlayerController).GetField("_animSvc", Inst)?.GetValue(p);
                }
                var clip = LoadClipAt(path);
                if (clip == null) { sb.AppendLine($"{label}: 클립 없음 ({path})"); continue; }
                var (count, times) = CountSwings(await PlayOnce(clip, null), clip.length);
                sb.AppendLine($"{label}: {clip.name} · {clip.length:0.00}초 · 휘두름 {count}회 @ {times}초");
                await PlayOnce(clip, label);
            }
        }
        finally
        {
            UnityEngine.Object.Destroy(camGo);
            await EquipAsync(p, Katana, ct);   // 덮어쓴 클립을 원래대로
        }
    }

    // ── 27 동작 연결 점수(10-09) ─────────────────────────────────
    // 콤보는 앞 타의 끝 자세에서 다음 타의 첫 자세로 이어져야 한다(사용자 10-09). 후보마다 진행 0 · 0.6 · 0.75 · 0.9 · 0.98의 자세를
    // 즉시 평가(Play + Update(0))해 엉덩이 · 머리 · 양손 · 양발(몸 기준 위치)과 오른손 방향을 적는다. 결과는 표(CSV)로 —
    // 짝마다 「앞 타 끝(0.75 · 0.9 · 0.98 중 최소) ↔ 다음 타 0」 거리를 밖에서 계산해 이어지는 순서를 고른다. 대기 자세(MoveBlend)도 적는다.
    private static readonly string[] ConnClips =
    {
        "Attack02_1_ALL", "Attack02_5", "Attack02_5_ALL", "Attack02_6", "Attack02_6_ALL", "Attack03_1_ALL", "Attack03_2", "Attack03_2_ALL",
        "Attack03_3", "Attack03_3_ALL", "Attack03_4", "Attack03_4_ALL", "Attack07", "SPAttack04", "SPAttack06",
        "Attack01_1_ALL", "Attack01_2", "Attack01_4", "Attack02_3", "Attack02_4", "Attack03_5",
        "Attack01_3", "Attack01_3_ALL", "Attack01_2_ALL", "Attack02_2", "Attack02_3_ALL", "Attack02_4_ALL",   // 2차 — 연속기 빈 조각
    };

    private static async UniTask AnimConnAsync(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        if (!await EquipAsync(p, Katana, ct)) { sb.AppendLine("카타나 장착 실패"); return; }
        var svc = typeof(PlayerController).GetField("_animSvc", Inst)?.GetValue(p);
        var ovr = svc?.GetType().GetMethod("Override", new[] { typeof(string), typeof(AnimationClip) });
        if (ovr == null) { sb.AppendLine("덮어쓰기 서비스 없음"); return; }
        var bones = new[] { HumanBodyBones.Hips, HumanBodyBones.Head, HumanBodyBones.LeftHand, HumanBodyBones.RightHand, HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot };
        var tr = new Transform[bones.Length];
        for (int i = 0; i < bones.Length; i++) tr[i] = p.Anim.GetBoneTransform(bones[i]);
        const string State = "GroundLightAttack_01";
        // 끊는 지점을 찾으려고 0.02 간격으로 촘촘히(1차는 0.6 · 0.75 · 0.9 · 0.98만 — 모든 클립이 끝에서 대기 자세로 돌아가 구분이 안 됐다)
        var cuts = new float[50];
        for (int i = 0; i < cuts.Length; i++) cuts[i] = i * 0.02f;
        await Wait(3f, ct);

        string Pose()
        {
            var parts = new List<string>();
            foreach (var t in tr)
            {
                Vector3 v = p.transform.InverseTransformPoint(t.position);
                parts.Add($"{v.x:0.000};{v.y:0.000};{v.z:0.000}");
            }
            Vector3 f = p.transform.InverseTransformDirection(tr[3].forward), u = p.transform.InverseTransformDirection(tr[3].up);
            parts.Add($"{f.x:0.000};{f.y:0.000};{f.z:0.000};{u.x:0.000};{u.y:0.000};{u.z:0.000}");
            return string.Join(";", parts);
        }

        p.enabled = false;
        try
        {
            p.Anim.speed = 1f;
            p.Anim.Play("MoveBlend", 0, 0f); p.Anim.Update(0f); p.Anim.Update(0f);
            sb.AppendLine("IDLE;0;" + Pose());
            foreach (var n in ConnClips)
            {
                var path = GI + n + "_Inplace.FBX";
                var nameless = "Assets/RelicFairy/_Imported/GhostSamurai_Animset/Animation/katana/Nameless/GhostSamurai_APose_" + n + "_Inplace.FBX";
                if (n.StartsWith("Attack01") && File.Exists(nameless)) path = nameless;
                var clip = LoadClipAt(path);
                if (clip == null) { sb.AppendLine($"#없음 {n}"); continue; }
                ovr.Invoke(svc, new object[] { State, clip });
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
                foreach (var c in cuts)
                {
                    p.Anim.Play(State, 0, c); p.Anim.Update(0f); p.Anim.Update(0f);
                    sb.AppendLine($"{n};{c:0.00};{Pose()}");
                }
            }
        }
        finally
        {
            p.enabled = true;
        }

        // 석궁 — 활 팩 동작(석궁을 쥔 대기 자세 기준)
        if (!await EquipAsync(p, Crossbow, ct, PlayerWeaponManager.Slot1)) { sb.AppendLine("석궁 장착 실패"); return; }
        await Wait(1f, ct);
        svc = typeof(PlayerController).GetField("_animSvc", Inst)?.GetValue(p);
        p.enabled = false;
        try
        {
            p.Anim.Play("MoveBlend", 0, 0f); p.Anim.Update(0f); p.Anim.Update(0f);
            sb.AppendLine("IDLE_BOW;0;" + Pose());
            foreach (var n in new[] { "Shoot_Loop", "Shoot_SP_Burst", "Shoot_SP03", "Shoot_SP04", "Shoot_SP05", "Shoot_SP01", "Shoot_SP02",
                                      "Shoot_SP_Start", "Shoot_SP_Enhance", "RainShoot_Burst", "RainShoot_Start", "RainShoot_Enhance", "Shoot_Start", "Shoot_End" })
            {
                var path = BowT + n + "_Inplace.FBX";
                if (!File.Exists(path)) path = BowP + n + "_Inplace.FBX";
                var clip = LoadClipAt(path);
                if (clip == null) { sb.AppendLine($"#없음 Bow_{n}"); continue; }
                ovr.Invoke(svc, new object[] { State, clip });
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
                foreach (var c in cuts)
                {
                    p.Anim.Play(State, 0, c); p.Anim.Update(0f); p.Anim.Update(0f);
                    sb.AppendLine($"Bow_{n};{c:0.00};{Pose()}");
                }
            }
        }
        finally
        {
            p.enabled = true;
            await EquipAsync(p, Katana, ct);
        }
    }

    // ── 28 동작 연속기 확인(10-09) ─────────────────────────────────
    // 무기 동작 연속기 배정(WeaponAnimChains) 뒤: ① ② ③ 무형검 · 카타나 · 대검을 단계마다 바로 누르면(대기열) 다음 타가 끊는 지점(attackEndAt)에서
    // 넘어가는가(넘어갈 때 나가는 쪽 진행 ±0.04) ④ 카타나를 연결 지점 뒤에 눌러도(늦은 입력) 2타로 이어지는가 ⑤ 카타나 E · 무형검 E 새 클립
    // ⑥ 활을 들고 걸으면 활 이동 클립인가 ⑦ 랜슬롯 Q — 들어가기 → 둘째 ↔ 셋째 순환 → 셋째 → 마무리(1.9~2.1초).
    // 넘어가는 순간과 0.06초 뒤를 찍는다 — Temp/ui_shots/WF_Chain_<무기>_<n>.png
    private static string s_chainTrace = "";
    private static string ClipTag(Animator a)
    {
        var ci = a.GetCurrentAnimatorClipInfo(0);
        if (ci.Length == 0 || ci[0].clip == null) return "?";
        string n = ci[0].clip.name.Replace("GhostSamurai_APose_", "").Replace("_Inplace", "");
        return $"{n}:{ci[0].clip.length:0.00}";
    }
    private static readonly int[] ChainAttackHashes =
    {
        Animator.StringToHash("GroundLightAttack_01"), Animator.StringToHash("GroundLightAttack_02"), Animator.StringToHash("GroundLightAttack_03"),
    };

    private static async UniTask ChainsAsync(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        var actField = typeof(PlayerController).GetField("actStateDebug", Inst);
        await Wait(5f, ct);   // 방 입장 연출
        Vector3 home = p.transform.position + Flat(p.transform.forward) * 8f;
        Quaternion homeRot = Quaternion.LookRotation(Flat(p.transform.forward));
        Directory.CreateDirectory("Temp/ui_shots");
        var camGo = new GameObject("~ChainCam");
        var cam = camGo.AddComponent<Camera>();
        if (Camera.main != null) cam.CopyFrom(Camera.main);
        cam.enabled = false;
        cam.fieldOfView = 40f;
        void Shot(string name)
        {
            Vector3 pos = p.transform.position, fwd = Flat(p.transform.forward), right = Vector3.Cross(Vector3.up, fwd);
            cam.transform.position = pos + right * 2.6f + fwd * 1.4f + Vector3.up * 1.5f;
            cam.transform.LookAt(pos + Vector3.up * 1.0f + fwd * 0.4f);
            CaptureMain(cam, $"Temp/ui_shots/WF_Chain_{name}.png");
        }
        int StepOf(int hash) => Array.IndexOf(ChainAttackHashes, hash);

        // 콤보 한 번: late = 다음 입력을 연결 지점 뒤(진행 lateAt)에 넣는다. 넘어갈 때 나가는 쪽 진행을 돌려준다.
        async UniTask<List<(int from, int to, float nt)>> Combo(string label, bool late, float lateAt)
        {
            var trans = new List<(int, int, float)>();
            GoHome(p, home, homeRot);
            await Wait(0.6f, ct);
            p.InputBuffer.Clear();
            p.InputBuffer.Push(Command.Light);
            int pushedFor = 0, lastNext = 0, shots = 0;
            float t0 = Time.time;
            var trace = new StringBuilder();
            string lastSig = "";
            while (Time.time - t0 < 4.5f)
            {
                await UniTask.Yield(PlayerLoopTiming.PostLateUpdate, ct);
                var cur = p.Anim.GetCurrentAnimatorStateInfo(0);
                int curStep = StepOf(cur.shortNameHash);
                // 진단 — 행동 상태 · 콤보 단계 · 창 · 대기열 · 버퍼가 바뀌는 프레임만 적는다
                string nextName = "";
                if (p.Anim.IsInTransition(0)) { int nh = p.Anim.GetNextAnimatorStateInfo(0).shortNameHash; nextName = "→" + (Array.Find(AnimStateNames, n => Animator.StringToHash(n) == nh) ?? ("#" + nh)); }
                string sig = $"{actField?.GetValue(p)}/s{p.Combo.CurrentComboStep}/{(p.Combo.ComboWindowOpen ? "창" : "-")}/{(p.Combo.NextComboQueued ? "Q" : "-")}/b{p.InputBuffer.Count}/a{curStep}{nextName}/x{p.Anim.speed:0.0}/{ClipTag(p.Anim)}";
                if (sig != lastSig && trace.Length < 900) { trace.Append($"[{Time.time - t0:0.00} nt{cur.normalizedTime:0.00} {sig}] "); lastSig = sig; }
                if (p.Anim.IsInTransition(0))
                {
                    var nxt = p.Anim.GetNextAnimatorStateInfo(0);
                    int nStep = StepOf(nxt.shortNameHash);
                    if (nxt.shortNameHash != lastNext && nStep > 0 && curStep == nStep - 1)
                    {
                        trans.Add((curStep, nStep, cur.normalizedTime));
                        if (shots < 4) { Shot($"{label}_{shots++}_넘어감{nStep + 1}타"); await Wait(0.06f, ct); Shot($"{label}_{shots++}_뒤"); }
                    }
                    lastNext = nxt.shortNameHash;
                    continue;
                }
                lastNext = 0;
                // 다음 입력 — 빠르게: 단계가 시작되자마자 · 늦게: 진행 lateAt에서
                // 1회차: 진행 0.02(0.017초)에 누르면 공격 준비 상태가 입력을 먼저 가져가 대기열에 안 들어갔다 — 사람 손으론 안 나오는 박자
                bool inAttack = (actField?.GetValue(p)?.ToString() ?? "") == "Attack";
                if (curStep >= 0 && curStep < 2 && pushedFor <= curStep && inAttack && (!late ? cur.normalizedTime > 0.08f : cur.normalizedTime >= lateAt))
                {
                    p.InputBuffer.Push(Command.Light);
                    pushedFor = curStep + 1;
                }
                if (curStep < 0 && Time.time - t0 > 0.5f && trans.Count >= 2) break;
            }
            await Wait(1.0f, ct);
            s_chainTrace = label + ": " + trace;
            return trans;
        }

        try
        {
            // ① ② ③ 빠른 입력
            foreach (var (label, path, ends) in new[] { ("Nameless", Nameless, new[] { 0.24f, 0.20f }), ("Katana", Katana, new[] { 0.24f, 0.22f }), ("Great", Great, new[] { 0.22f, 0.30f }) })
            {
                if (!await EquipAsync(p, path, ct)) { sb.AppendLine($"{label}: 장착 실패"); continue; }
                await Wait(1f, ct);
                var tr = await Combo(label, false, 0f);
                bool ok = tr.Count == 2 && Mathf.Abs(tr[0].nt - ends[0]) <= 0.04f && Mathf.Abs(tr[1].nt - ends[1]) <= 0.04f;
                sb.AppendLine("   진단 " + s_chainTrace);
                Check(sb, $"{label} 빠른 입력 — 넘어감 " + string.Join(", ", tr.ConvertAll(x => $"{x.from + 1}→{x.to + 1}타 @{x.nt:0.00}")) + $" (계획 {ends[0]:0.00} · {ends[1]:0.00})", ok);
            }

            // ④ 카타나 늦은 입력(연결 0.24 뒤 진행 0.40에 누름 — 허용 끝 0.56)
            if (await EquipAsync(p, Katana, ct))
            {
                await Wait(1f, ct);
                var tr = await Combo("KatanaLate", true, 0.40f);
                Check(sb, "카타나 늦은 입력(진행 0.40에 누름) — 넘어감 " + string.Join(", ", tr.ConvertAll(x => $"{x.from + 1}→{x.to + 1}타 @{x.nt:0.00}")),
                      tr.Count >= 1 && tr[0].to == 1);
            }

            // ⑤ 새 스킬 클립(카타나 E = SPAttack06 · 무형검 E = Attack01_4)
            foreach (var (label, path, want) in new[] { ("카타나 E", Katana, "SPAttack06"), ("무형검 E", Nameless, "Attack01_4") })
            {
                if (!await EquipAsync(p, path, ct)) continue;
                GoHome(p, home, homeRot);
                await Wait(1f, ct);
                p.CooldownTracker.ResetCooldown(SkillType.E);
                p.InputBuffer.Push(Command.ESkill);
                string seen = "";
                float g0 = Time.time;
                while (Time.time - g0 < 1.5f)
                {
                    await UniTask.Yield(PlayerLoopTiming.PostLateUpdate, ct);
                    foreach (var ci in p.Anim.GetCurrentAnimatorClipInfo(0)) if (ci.clip != null && ci.weight > 0.5f && !seen.Contains(ci.clip.name)) seen += ci.clip.name + " ";
                    if (seen.Contains(want) && Time.time - g0 > 0.25f) { Shot($"{label.Replace(" ", "")}_스킬"); break; }
                }
                Check(sb, $"⑤ {label} 클립 — {seen.Trim()}", seen.Contains(want));
                await Wait(2f, ct);
            }

            // ⑥ 활 이동
            if (await EquipAsync(p, Bow, ct, PlayerWeaponManager.Slot1))
            {
                GoHome(p, home, homeRot);
                await Wait(1f, ct);
                InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(Key.W));
                await Wait(0.8f, ct);
                string clips = "";
                foreach (var ci in p.Anim.GetCurrentAnimatorClipInfo(0)) if (ci.clip != null && ci.weight > 0.1f) clips += ci.clip.name + " ";
                Shot("Bow_걷기");
                InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
                Check(sb, $"⑥ 활 걷기 클립 — {clips.Trim()}", clips.Contains("Bow_Common"));
                await Wait(0.6f, ct);
            }

            // ⑦ 랜슬롯 Q 순서
            var relic = p.RelicBehavior as LancelotMadnessRelic;
            if (relic == null) sb.AppendLine("⑦ 랜슬롯 유물이 아니다 — 건너뜀");
            else if (await EquipAsync(p, Katana, ct))
            {
                GoHome(p, home, homeRot);
                await Wait(1f, ct);
                relic.Madness.SetStacks(relic.Madness.MaxStacks);
                if (!relic.Madness.IsFrenzy) relic.Madness.EnterFrenzy(30f);
                await Wait(0.2f, ct);
                p.CooldownTracker.ResetCooldown(SkillType.Q);
                p.InputBuffer.Clear();
                p.InputBuffer.Push(Command.QSkill);
                var names = new Dictionary<int, string>();
                foreach (var n in new[] { "RelicQ_Slash1", "RelicQ_Slash2", "RelicQ_Slash3", "RelicQ_Finish", "RelicQ_Main" }) names[Animator.StringToHash(n)] = n;
                var seq = new List<string>();
                var trace = new StringBuilder();
                float q0 = Time.time, finishAt = -1f, finishStart = -1f;
                string last = "";
                int qShots = 0;
                float g0 = Time.time;
                while (Time.time - g0 < 4.5f)
                {
                    await UniTask.Yield(PlayerLoopTiming.PostLateUpdate, ct);
                    var info = p.Anim.IsInTransition(0) ? p.Anim.GetNextAnimatorStateInfo(0) : p.Anim.GetCurrentAnimatorStateInfo(0);
                    if (!names.TryGetValue(info.shortNameHash, out var n)) continue;
                    if (n != last)
                    {
                        seq.Add(n.Replace("RelicQ_", ""));
                        trace.Append($"{n.Replace("RelicQ_", "")}@{Time.time - q0:0.00}({info.normalizedTime:0.00}) ");
                        last = n;
                        if (n == "RelicQ_Finish") { finishAt = Time.time - q0; finishStart = info.normalizedTime; }
                        if (qShots < 6 && (n == "RelicQ_Finish" || seq.Count <= 4)) Shot($"LanceQ_{qShots++}_{n.Replace("RelicQ_", "")}");
                    }
                }
                bool order = seq.Count >= 4 && seq[0] == "Slash1" && seq[seq.Count - 1] == "Finish" && seq[seq.Count - 2] == "Slash3" && !seq.GetRange(1, seq.Count - 2).Contains("Slash1");
                Check(sb, $"⑦ 랜슬롯 Q 순서 {string.Join(">", seq)} · 마무리 {finishAt:0.00}초(시작 진행 {finishStart:0.00})", order && finishAt >= 1.8f && finishAt <= 2.2f);
                sb.AppendLine("   상태 흐름(Q 누른 때부터): " + trace);
                relic.Madness.SetStacks(0);
            }
        }
        finally
        {
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
            UnityEngine.Object.Destroy(camGo);
        }
    }

    // ── 29 무형검 연타 진단(10-10 사용자 「무형검 연타 시 1타만 빠르게 반복」) ───────────────
    // 0.15초마다 한 번씩 누르며 프레임마다 공격 상태 내부(재생 해시 · 칸 주소 · 창 열림 · 연결 · 회수)와 실제 재생 클립을 적는다.
    private static async UniTask NamelessComboDiagAsync(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        await Wait(5f, ct);
        foreach (var weaponPath in new[] { Nameless, Katana, Crossbow })
            await ComboDiagOneAsync(p, sb, ct, weaponPath);
    }

    private static async UniTask ComboDiagOneAsync(PlayerController p, StringBuilder sb, CancellationToken ct, string weaponPath)
    {
        if (!await EquipAsync(p, weaponPath, ct, weaponPath == Crossbow ? PlayerWeaponManager.Slot1 : PlayerWeaponManager.Slot0)) { sb.AppendLine("장착 실패 " + weaponPath); return; }
        await Wait(1f, ct);
        int shots = 0;
        Action<int> onStep = _ => shots++;
        p.EventReceiver.OnEffectStep += onStep;
        var actField = typeof(PlayerController).GetField("actStateDebug", Inst);
        var smField = typeof(PlayerController).GetField("_actSM", Inst);
        object sm = smField?.GetValue(p);
        var curField = sm?.GetType().GetField("_current", Inst);
        var wd = p.WeaponManager.CurrentWeaponData;
        var set = wd?.animationSet as WeaponAnimationSetSO;
        sb.AppendLine($"무기 {wd?.weaponSOKey} · 애니 세트 {set?.name} · 지상 평타 칸 " + (set == null ? "?" : string.Join(", ",
            System.Linq.Enumerable.Select(System.Linq.Enumerable.Where(set.GetAllMappings(), m => m.baseClipName.StartsWith("GroundLight")),
                m => $"{m.baseClipName}={m.addressableKey}(act{(int)m.actionType} c{m.comboIndex} 창{m.comboWindowOpen:0.00}~{m.comboWindowClose:0.00} 연결{m.attackEndAt:0.00})"))));
        foreach (var m in set.GetAllMappings())
            if (m.baseClipName.StartsWith("GroundLight")) { var c = Managers.AnimationResources.GetClip(m.addressableKey); sb.AppendLine($"   {m.addressableKey} → {(c != null ? c.name + ":" + c.length.ToString("0.00") : "없음")}"); }
        var trace = new StringBuilder();
        string lastSig = "";
        float t0 = Time.time, nextPress = 0f;
        while (Time.time - t0 < 3f)
        {
            if (Time.time - t0 >= nextPress && Time.time - t0 < 2.2f) { p.InputBuffer.Push(Command.Light); nextPress += 0.15f; }
            await UniTask.Yield(PlayerLoopTiming.PostLateUpdate, ct);
            var st = curField?.GetValue(sm);
            string inner = "";
            if (st != null && st.GetType().Name == "ActAttackState")
            {
                int h = (int)(st.GetType().GetField("_currentStateHash", Inst)?.GetValue(st) ?? 0);
                float open = (float)(st.GetType().GetField("_comboOpen", Inst)?.GetValue(st) ?? -1f);
                float end = (float)(st.GetType().GetField("_attackEnd", Inst)?.GetValue(st) ?? -1f);
                var map = st.GetType().GetField("_currentMapping", Inst)?.GetValue(st) as WeaponAnimationSetSO.ClipMapping;
                bool rec = (bool)(st.GetType().GetField("_inRecovery", Inst)?.GetValue(st) ?? false);
                bool wait = (bool)(st.GetType().GetField("_waitingForComboInput", Inst)?.GetValue(st) ?? false);
                inner = $"h{Array.IndexOf(ChainAttackHashes, h)}/{map?.addressableKey}/열{open:0.00}/연{end:0.00}{(rec ? "/회수" : "")}{(wait ? "/대기" : "")}";
            }
            var cur = p.Anim.GetCurrentAnimatorStateInfo(0);
            string sig = $"{actField?.GetValue(p)}/s{p.Combo.CurrentComboStep}/{(p.Combo.ComboWindowOpen ? "창" : "-")}/{(p.Combo.NextComboQueued ? "Q" : "-")}/a{Array.IndexOf(ChainAttackHashes, cur.shortNameHash)}/{ClipTag(p.Anim)}/{inner}";
            if (sig != lastSig && trace.Length < 3000) { trace.Append($"\n   [{Time.time - t0:0.00} nt{cur.normalizedTime:0.00} x{p.Anim.speed:0.0}] {sig}"); lastSig = sig; }
        }
        p.EventReceiver.OnEffectStep -= onStep;
        sb.AppendLine($"타격 · 쏘기 이벤트 {shots}회 · 흐름:" + trace);
        await Wait(1f, ct);
    }

    // ── 30 석궁 동작 — 쏘는 순간 찾기(10-10) ─────────────────────────
    // 석궁 평타 · 스킬 후보(활 팩 SP01 · SP02 · SP_Start · RainShoot_Burst)를 석궁을 쥔 채 진행 0.05 간격으로 자세를 고정해(Play + Update(0)) 찍는다.
    // 시위를 놓는 순간 = 화살이 나가야 할 이벤트 자리. 결과 Temp/ui_shots/WF_Xbow_<클립>_<진행×100>.png
    private static async UniTask CrossbowFramesAsync(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        await Wait(4f, ct);
        if (!await EquipAsync(p, Crossbow, ct, PlayerWeaponManager.Slot1)) { sb.AppendLine("석궁 장착 실패"); return; }
        // 1회차는 이벤트방 소품이 카메라를 가렸다 — 방 안쪽 빈 자리로 옮기고 옆 위에서 찍는다
        GoHome(p, p.transform.position + Flat(p.transform.forward) * 8f, Quaternion.LookRotation(Flat(p.transform.forward)));
        await Wait(1f, ct);
        var svc = typeof(PlayerController).GetField("_animSvc", Inst)?.GetValue(p);
        var ovr = svc?.GetType().GetMethod("Override", new[] { typeof(string), typeof(AnimationClip) });
        if (ovr == null) { sb.AppendLine("덮어쓰기 서비스 없음"); return; }
        Directory.CreateDirectory("Temp/ui_shots");
        var camGo = new GameObject("~XbowCam");
        var cam = camGo.AddComponent<Camera>();
        if (Camera.main != null) cam.CopyFrom(Camera.main);
        cam.enabled = false;
        cam.fieldOfView = 40f;
        const string State = "GroundLightAttack_01";
        p.enabled = false;
        try
        {
            foreach (var n in new[] { "Shoot_SP01", "Shoot_SP02", "Shoot_SP_Start", "RainShoot_Burst" })
            {
                var clip = LoadClipAt(BowT + n + "_Inplace.FBX");
                if (clip == null) { sb.AppendLine($"{n}: 클립 없음"); continue; }
                ovr.Invoke(svc, new object[] { State, clip });
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
                for (int i = 0; i <= 12; i++)
                {
                    float nt = i * 0.05f;
                    p.Anim.speed = 0f;
                    p.Anim.Play(State, 0, nt); p.Anim.Update(0f); p.Anim.Update(0f);
                    Vector3 pos = p.transform.position, fwd = Flat(p.transform.forward), right = Vector3.Cross(Vector3.up, fwd);
                    cam.transform.position = pos + right * 2.0f + fwd * 0.6f + Vector3.up * 2.0f;
                    cam.transform.LookAt(pos + Vector3.up * 1.0f + fwd * 0.4f);
                    HideStrays();
                    CaptureMain(cam, $"Temp/ui_shots/WF_Xbow_{n}_{i * 5:00}.png");
                }
                sb.AppendLine($"{n}: {clip.length:0.00}초 · 13장");
            }
        }
        finally
        {
            p.Anim.speed = 1f;
            p.enabled = true;
            UnityEngine.Object.Destroy(camGo);
        }
    }

    // ── 7 재련소 화면 ───────────────────────────────────────────
    // 계획 Task 5 Step 5 — 활을 든 채 「◆ 진화」 → 근접 칸만 진화(Review Focus #2 UI 경로) · 「◆ 형 단련 Ⅱ」 버튼 · 단련 창 카드 두 장 ·
    // 고르면 결과 줄과 강화재료 −12. 화면 4장 Temp/ui_shots/WF_Crucible_*.png. 임시 재련소 컨트롤러(CrucibleChooserProbeEditor와 같은 길).
    private static async UniTask CrucibleAsync(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        var wm  = p.WeaponManager;
        var run = GameRunBootstrapper.Instance.Run;
        if (!await EquipAsync(p, Nameless, ct)) { sb.AppendLine("무형검 장착 실패"); return; }
        wm.Weapon0Data.enhanceLevel = 5;
        wm.Weapon0Data.RecomputeEnhancedStats();
        if (wm.Weapon1Data == null)
        {
            var bow = AssetDatabase.LoadAssetAtPath<WeaponSO>("Assets/RelicFairy/Weapon/Bow/Data/T1_Bow.asset");
            if (bow != null) await wm.AcquireWeaponToSlotAsync(WeaponData.FromSO(bow), PlayerWeaponManager.Slot1, setActive: false, playAppear: false);
        }
        var bowBefore = wm.Weapon1Data;
        await wm.SwitchToSlotAsync(PlayerWeaponManager.Slot1);
        Check(sb, "활을 든 상태", bowBefore != null && wm.CurrentSlotIndex == PlayerWeaponManager.Slot1);
        run.FuelBank.Add(FuelKind.EnhanceMaterial, 40);

        var go = new GameObject("~WeaponFormCrucibleProbe");
        HubGui("HideTestHubGui");
        try
        {
            var ctrl = go.AddComponent<CrucibleRoomController>();
            ctrl.Initialize(run, await WeaponEnhanceService.EnsureLoadedAsync(), new System.Random(7), null);
            var panel = await Managers.UI.ShowPopupUIAndGetAsync<UI_CruciblePanel>();
            panel.Bind(ctrl);
            await Wait(0.8f, ct);
            PanelField<Button[]>(panel, "_chooseCard")?[0]?.onClick.Invoke();   // 첫 화면 → 근거리
            PanelCall(panel, "RefreshAll");
            await Wait(0.3f, ct);
            var btn = PanelField<Button>(panel, "_evolveBtn");
            var lbl = PanelField<TMP_Text>(panel, "_evolveLabel");
            string l1 = lbl != null ? lbl.GetParsedText() : null;
            Check(sb, $"+5 진화 버튼 「{l1}」", btn != null && btn.gameObject.activeInHierarchy && btn.interactable && l1 != null && (l1.Contains("진화 — 형을 고른다") || l1.Contains("깨어날 수 있다")));   // v3 형 칸 한 줄
            await ShotUi("WF_Crucible_1_Evolve", 300);

            // 활을 든 채 UI로 진화 — 카타나 갈래 카드를 고르고 확정
            PanelCall(panel, "ShowEvolvePanel");
            await Wait(0.5f, ct);
            await ShotUi("WF_Crucible_1b_EvolvePanel", 300);
            sb.AppendLine("   " + OverlayFit(panel, "_evolvePanel"));
            var branches = typeof(UI_CruciblePanel).GetProperty("CurrentBranches", Inst)?.GetValue(panel) as IReadOnlyList<WeaponEvolutionSO.Branch>;
            int ki = -1;
            for (int i = 0; branches != null && i < branches.Count; i++) if (branches[i].branchId == "katana") ki = i;
            PanelCall(panel, "PickEvolveCard", ki);
            PanelField<Button>(panel, "_evolveConfirm")?.onClick.Invoke();
            float t0 = Time.realtimeSinceStartup;
            while (wm.Weapon0Data?.form == null && Time.realtimeSinceStartup - t0 < 10f) await UniTask.Yield(PlayerLoopTiming.Update, ct);
            await Wait(3f, ct);   // 망치 · 공개 · 도장 연출이 끝날 때까지
            Check(sb, $"활을 든 채 진화 → 근접 칸 {wm.Weapon0Data?.displayName}(형 {wm.Weapon0Data?.form?.DisplayName}) · 활 그대로 {ReferenceEquals(wm.Weapon1Data, bowBefore)}",
                  wm.Weapon0Data?.form?.DisplayName == "칼바람" && ReferenceEquals(wm.Weapon1Data, bowBefore));
            PanelField<GameObject>(panel, "_engravePanel")?.SetActive(false);   // 진화 뒤 각인 제안 — 이 실측의 대상이 아니다
            PanelCall(panel, "HideEvolvePanel");
            PanelCall(panel, "RefreshAll");
            await Wait(0.3f, ct);
            string l2 = lbl != null ? lbl.GetParsedText() : null;
            Check(sb, $"단련 버튼 「{l2}」", btn != null && btn.gameObject.activeInHierarchy && l2 != null && l2.Contains("단련 Ⅱ") && l2.Contains("12"));   // 옛 「형 단련 Ⅱ」 · v3 「칼바람 Ⅰ — 단련 Ⅱ」
            await ShotUi("WF_Crucible_2_TemperButton", 300);

            btn?.onClick.Invoke();   // 단련 버튼 → 단련 창
            await Wait(0.4f, ct);
            var names = PanelField<TMP_Text[]>(panel, "_temperName");
            var tp    = PanelField<GameObject>(panel, "_temperPanel");
            string n0 = names?[0] != null ? names[0].GetParsedText() : null, n1 = names?[1] != null ? names[1].GetParsedText() : null;
            Check(sb, $"단련 창 카드 「{n0}」 · 「{n1}」", tp != null && tp.activeInHierarchy && n0 != null && n0.Contains("쌍풍") && n1 != null && n1.Contains("되감는 바람"));
            await ShotUi("WF_Crucible_3_TemperPanel", 300);
            sb.AppendLine("   " + OverlayFit(panel, "_temperPanel"));

            int fuel0 = run.FuelBank.EnhanceMaterial;
            PanelCall(panel, "OnTemperPicked", 0);
            await Wait(0.4f, ct);
            var rt  = PanelField<TMP_Text>(panel, "_resultText");
            string res = rt != null ? rt.GetParsedText() : null;
            var w = wm.Weapon0Data;
            Check(sb, $"결과 「{res}」 · 재료 {fuel0} → {run.FuelBank.EnhanceMaterial} · 단계 {w.formTier} · {w.formVariants}",
                  res != null && res.Contains("형 단련 — 쌍풍") && fuel0 - run.FuelBank.EnhanceMaterial == 12 && w.formTier == 2 && w.formVariants == "twin_wind");
            sb.AppendLine($"   다음 버튼 「{(lbl != null ? lbl.GetParsedText() : null)}」");
            await ShotUi("WF_Crucible_4_Result", 300);
        }
        finally
        {
            Managers.UI?.CloseAllPopupUI();
            UnityEngine.Object.Destroy(go);
            HubGui("RestoreTestHubGui");
        }
    }

    /// <summary>첫 발동 때만 세 장 — 0.06초 · 0.25초(발동 이펙트 · 3D 글자) · 0.7초(계정 첫 자막).</summary>
    private static Action<string> FirstTriggerShots(string prefix)
    {
        bool done = false;
        return _ =>
        {
            if (done) return;
            done = true;
            UniTask.Void(async () =>
            {
                await ShotUi(prefix + "_2_Trigger", 60);
                await ShotUi(prefix + "_3_Trigger", 150);
                await ShotUi(prefix + "_4_After", 400);
            });
        };
    }

    /// <summary>덮개가 v3 판 안에 있는가 — 덮개 화면 사각형이 판(V3) 화면 사각형 안이면 「안」(1 px 여유).</summary>
    private static string OverlayFit(UI_CruciblePanel panel, string field)
    {
        var go = PanelField<GameObject>(panel, field);
        RectTransform v3 = null;
        if (panel != null) foreach (var r in panel.GetComponentsInChildren<RectTransform>(true)) if (r.name == "V3") { v3 = r; break; }
        if (go == null || v3 == null) return $"{field}: 덮개 {go != null} · 판 {v3 != null}";
        var a = new Vector3[4]; var b = new Vector3[4];
        ((RectTransform)go.transform).GetWorldCorners(a);
        v3.GetWorldCorners(b);
        bool inside = a[0].x >= b[0].x - 1f && a[0].y >= b[0].y - 1f && a[2].x <= b[2].x + 1f && a[2].y <= b[2].y + 1f;
        return $"{field}: 덮개 {a[2].x - a[0].x:0}×{a[2].y - a[0].y:0} · 판 {b[2].x - b[0].x:0}×{b[2].y - b[0].y:0} · 판 안 {inside}";
    }

    private static T PanelField<T>(UI_CruciblePanel panel, string name) where T : class
        => typeof(UI_CruciblePanel).GetField(name, Inst)?.GetValue(panel) as T;

    private static void PanelCall(UI_CruciblePanel panel, string method, params object[] args)
        => typeof(UI_CruciblePanel).GetMethod(method, Inst)?.Invoke(panel, args.Length == 0 ? null : args);

    private static void HubGui(string method)
        => typeof(UILayoutRuntimeProbeEditor).GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)?.Invoke(null, null);

    private static UniTask ShotUi(string name, int settleMs)
    {
        var m = typeof(UILayoutRuntimeProbeEditor).GetMethod("ShotAsync", BindingFlags.Static | BindingFlags.NonPublic);
        return m != null ? (UniTask)m.Invoke(null, new object[] { name, settleMs }) : UniTask.CompletedTask;
    }

    // ── 표적 · 입력 도우미 ──────────────────────────────────────
    private const int TankHp = 50000;

    private static async UniTask<MonsterBase> SpawnAsync(Vector3 at, CancellationToken ct)
    {
        var m = await Managers.ObjectPooler.SpawnAsync<MonsterBase>("Slime/Slime", ObjectPoolerManager.PoolType.Monster, at, Quaternion.identity);
        if (m == null) return null;
        float t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < 6f && m.IsDamageImmuneNow) await UniTask.Yield(PlayerLoopTiming.Update, ct);
        m.Status.ApplyCc("probe_hold", 9999f);   // 걸어오거나 때리지 않게
        SetHp(m, TankHp);
        return m;
    }

    private static void SetHp(MonsterBase m, int hp)
    {
        var rt = typeof(MonsterBase).GetField("_runtime", Inst)?.GetValue(m);
        var f  = rt?.GetType().GetField("CurrentHp", Inst);
        if (f != null) f.SetValue(rt, Convert.ChangeType(hp, f.FieldType));
    }

    /// <summary>
    /// 표적 하나의 받은 피해 — OnHPChanged(현재, 최대)로 깎인 양을 더한다. 최대치로 채워지는 사건(스폰 뒤 초기화)은 세지 않는다.
    /// Refill이 체력을 TankHp로 다시 올려 연타에 죽지 않게 한다(SetHp는 사건을 내지 않는다).
    /// </summary>
    private sealed class HpTrack
    {
        public MonsterBase M;
        public float Dealt;
        private int _last;
        private readonly Vector3 _pin;   // 넉백으로 밀려도 매 틱 제자리로(예산 실측과 같은 이유)
        public Vector3 Pin => _pin;
        public HpTrack(MonsterBase m) { M = m; _pin = m.transform.position; M.OnHPChanged += OnChanged; Refill(); }
        private void OnChanged(int cur, int max)
        {
            if (cur == max) { _last = cur; return; }   // 최대치로 채워짐(스폰 뒤 초기화) — 피해 아님. 50000으로 올린 뒤의 피해는 cur > max라 여기 안 걸린다
            if (cur < _last) Dealt += _last - cur;
            _last = cur;
        }
        public void Refill()
        {
            if (M == null || M.IsDead) return;
            SetHp(M, TankHp);
            _last = TankHp;
            if ((M.transform.position - _pin).sqrMagnitude > 0.04f)
            {
                if (M.TryGetComponent<UnityEngine.AI.NavMeshAgent>(out var ag) && ag.isActiveAndEnabled && ag.isOnNavMesh) ag.Warp(_pin);
                else M.transform.position = _pin;
            }
        }
        public void Detach() { if (M != null) M.OnHPChanged -= OnChanged; }
    }

    private static readonly List<HpTrack> s_tracks = new();

    private static async UniTask<HpTrack> TrackAsync(Vector3 at, CancellationToken ct)
    {
        var m = await SpawnAsync(at, ct);
        if (m == null) return null;
        var t = new HpTrack(m);
        s_tracks.Add(t);
        return t;
    }

    // 전투방 파도 몬스터도 끈다 — 조준 보정이 그쪽으로 끌려가 줄 밖 표적이 맞거나 헛친다(실측 2 6 · 8 · 10회차, 실측 4 3~4회차)
    private static void RefillAll() { HideStrays(); foreach (var t in s_tracks) t.Refill(); }

    /// <summary>표적이 쓰러졌으면 같은 자리에 새로 부른다.</summary>
    private static async UniTask<HpTrack> EnsureAliveAsync(HpTrack t, CancellationToken ct)
    {
        if (t != null && t.M != null && !t.M.IsDead) return t;
        return t == null ? null : await TrackAsync(t.Pin, ct);
    }

    private static void DetachAll()
    {
        foreach (var t in s_tracks) t.Detach();
        s_tracks.Clear();
    }

    private static Vector3 s_home;
    private static string  s_equipNote = "";

    /// <summary>공격 클립 진행(0~1) — 크로스페이드 중이면 들어오는 쪽(다음 상태). 현재 상태만 읽으면 앞 클립 값이 섞였다(실측 8 4회차 0.54).</summary>
    private static float ClipNt(PlayerController p)
    {
        if (p.Anim == null) return -1f;
        return p.Anim.IsInTransition(0) ? p.Anim.GetNextAnimatorStateInfo(0).normalizedTime : p.Anim.GetCurrentAnimatorStateInfo(0).normalizedTime;
    }

    private static float NtAt(List<(float t, Vector3 local)> s, List<float> nt, float t)
    {
        for (int i = 0; i < s.Count; i++) if (s[i].t >= t) return nt[i];
        return nt.Count > 0 ? nt[nt.Count - 1] : -1f;
    }

    /// <summary>표적을 겨누고 제자리를 지킨다 — 평타의 조준 · 추적 전진은 마우스 쪽을 따르고, 피격 · 넉백으로 밀리면 표적에서 멀어진다
    /// (실측 2 3회차: 밀려난 3.7 m에서 8초 동안 1번 적중). 예산 실측(RelicGrowthV2BudgetProbeEditor)과 같은 방법.</summary>
    private static void Steer(PlayerController p, Vector3 at)
    {
        if ((p.transform.position - s_home).sqrMagnitude > 1f)
        {
            if (p.TryGetComponent<Rigidbody>(out var rb)) { rb.position = s_home; rb.linearVelocity = Vector3.zero; }
            p.transform.position = s_home;
        }
        AimAt(at);
        RequestFace(p, at);
    }

    /// <summary>진단(10-07 카타나 2 · 3타 옆돌기): 그 순간 마우스 방향 · 조준 보정이 고른 적 · 카메라 방향을 「가까운 표적 기준 각」으로.</summary>
    private static string AimDiag(PlayerController p, Vector3 toNear, HpTrack near, HpTrack far, HpTrack side)
    {
        var aim = typeof(PlayerController).GetField("_aim", Inst)?.GetValue(p) as PlayerAim;
        string mouse = aim != null && aim.TryComputeLookDir(p.transform, out var md) ? $"{Vector3.SignedAngle(md, toNear, Vector3.up):0}" : "?";
        var rot = p.ComputeMouseAimAssistRotation(7f, 40f, 1f, out Transform en, out _);
        string who = en == null ? "없음" : en == near.M.transform ? "가까운" : en == far.M.transform ? "먼" : en == side.M.transform ? "옆" : en.name;
        Vector3 cam = Camera.main != null ? Flat(Camera.main.transform.forward) : Vector3.zero;
        string ray = "";
        if (Camera.main != null && Mouse.current != null)
        {
            Vector2 read = Mouse.current.position.ReadValue();
            var r = Camera.main.ScreenPointToRay(read);
            ray = Physics.Raycast(r, out var h, 100f, LayerMask.GetMask("Ground")) ? $"{h.collider.name} {Vector3.Distance(h.point, p.transform.position):0.0}m" : "빗나감";
            ray = $" · 원함 ({s_aimScreen.x:0},{s_aimScreen.y:0}) 읽음 ({read.x:0},{read.y:0}) 화면 {Screen.width}x{Screen.height} · 바닥 {ray}";
        }
        return $" · 마우스 {mouse}° · 보정 {who} {Vector3.SignedAngle(rot * Vector3.forward, toNear, Vector3.up):0}° · 카메라 {Vector3.SignedAngle(cam, toNear, Vector3.up):0}°{ray}";
    }

    // ── 31 기본 이동 자세(10-10 사용자 「기본 이동 애니가 뼈가 안 맞는 듯 부자연스럽다」) ──────────
    // 근접 무기는 이동 · 대기를 사무라이 카타나 양손 자세(APose_*)로 덮는다 — 한 손 검이라 왼손이 칼자루 없이 뜬다.
    // 후보 묶음을 같은 이동 칸(기본 컨트롤러 클립 이름)에 덮어 대기 · 달리기(W)를 옆 · 앞에서 찍는다. 끝나면 무형검을 다시 들어 원래대로.
    private const string CgAnim = "Assets/CombatGirlsCharacterPack/CombatGirl_Shield/Animations/";
    private const string GsCommon = "Assets/RelicFairy/_Imported/GhostSamurai_Animset/Animation/katana/Common/Inplace/GhostSamurai_Common_";
    private static readonly (string tag, string idle, string walk, string run)[] LocoCandidates =
    {
        ("now",    null, null, null),
        ("SS",     CgAnim + "Normal/SS_Idle.fbx",     CgAnim + "Normal/SS_Walk.fbx",     CgAnim + "Normal/SS_Run.fbx"),
        ("SSSP",   CgAnim + "Special/SS_SP_Idle.fbx", CgAnim + "Special/SS_SP_Walk.fbx", CgAnim + "Special/SS_SP_Run.fbx"),
        ("Common", GsCommon + "Idle_Inplace.FBX",     GsCommon + "StrafeWalkF_Inplace.FBX", GsCommon + "StrafeRun_F_Inplace.FBX"),
    };

    private static async UniTask LocoPoseAsync(PlayerController p, StringBuilder sb, CancellationToken ct, string weapon, string prefix = "")
    {
        var svc = typeof(PlayerController).GetField("_animSvc", Inst)?.GetValue(p);
        var ovr = svc?.GetType().GetMethod("Override", new[] { typeof(string), typeof(AnimationClip) });
        if (ovr == null) { sb.AppendLine("덮어쓰기 서비스 없음"); return; }
        if (!await EquipAsync(p, weapon, ct)) { sb.AppendLine("장착 실패 " + weapon); return; }
        HideStrays();
        await Wait(5f, ct);   // 방 입장 연출 동안은 입력이 잠긴다(1회차: W가 안 먹었다)
        Vector3    home    = p.transform.position;
        Quaternion homeRot = Quaternion.LookRotation(Flat(p.transform.forward));
        void HoldW(bool on) => InputSystem.QueueStateEvent(Keyboard.current, on ? new KeyboardState(Key.W) : new KeyboardState());

        Directory.CreateDirectory("Temp/ui_shots");
        var camGo = new GameObject("~LocoProbeCam");
        var cam   = camGo.AddComponent<Camera>();
        if (Camera.main != null) cam.CopyFrom(Camera.main);
        cam.enabled = false;
        cam.fieldOfView = 30f;
        void Shoot(string view, string name)
        {
            Vector3 pos = p.transform.position, f = Flat(p.transform.forward), r = Vector3.Cross(Vector3.up, f);
            Vector3 off = view == "side" ? r * 4.2f : f * 4.2f;
            cam.transform.position = pos + off + Vector3.up * 1.0f;
            cam.transform.LookAt(pos + Vector3.up * 0.85f);
            CaptureMain(cam, $"Temp/ui_shots/WF_Loco_{prefix}{name}_{view}.png");
        }
        string Clips()
        {
            var s2 = new StringBuilder();
            foreach (var c in p.Anim.GetCurrentAnimatorClipInfo(0))
                if (c.weight > 0.05f) s2.Append($"{c.clip.name}×{c.weight:0.00} ");
            return s2.ToString();
        }
        try
        {
            foreach (var (tag, idle, walk, run) in LocoCandidates)
            {
                if (idle != null)
                {
                    var ci = LoadClipAt(idle); var cw = LoadClipAt(walk); var cr = LoadClipAt(run);
                    if (ci == null || cw == null || cr == null) { sb.AppendLine($"{tag}: 클립 없음"); continue; }
                    ovr.Invoke(svc, new object[] { "GhostSamurai_Common_Idle_Inplace", ci });
                    foreach (var d in new[] { "F", "FL", "FR" })
                        ovr.Invoke(svc, new object[] { $"GhostSamurai_Common_StrafeWalk{d}_Inplace", cw });
                    foreach (var d in new[] { "F", "FL", "FR" })
                        ovr.Invoke(svc, new object[] { $"GhostSamurai_Common_StrafeRun_{d}_Inplace", cr });
                }
                GoHome(p, home, homeRot);
                await Wait(1.2f, ct);
                sb.AppendLine($"{tag} 대기 — {Clips()}");
                Shoot("side", $"{tag}_idle"); Shoot("front", $"{tag}_idle");
                HoldW(true);
                await Wait(0.8f, ct);
                var rb = p.GetComponent<Rigidbody>();
                sb.AppendLine($"{tag} 달리기 — {Clips()} · 속도 {(rb != null ? rb.linearVelocity.magnitude : 0f):0.0} m/s");
                for (int k = 0; k < 3; k++) { await UniTask.Yield(PlayerLoopTiming.PostLateUpdate, ct); Shoot("side", $"{tag}_run{k}"); await Wait(0.11f, ct); }
                Shoot("front", $"{tag}_run3");
                HoldW(false);
                await Wait(0.5f, ct);
            }
        }
        finally
        {
            HoldW(false);
            UnityEngine.Object.Destroy(camGo);
            await EquipAsync(p, weapon, ct);   // 무기 애니 세트를 다시 입혀 덮어쓴 이동 칸을 되돌린다
            GoHome(p, home, homeRot);
        }
    }

    // ── 33 평타 이어짐 필름(10-11 사용자 「애니가 크로스페이드 안 되나 — 다 끊겨 나오고 연결된 느낌이 없다」) ──
    // 무형검으로 보통 박자(0.3초)에 3타를 두 바퀴 누르며 옆에서 1/30초마다 찍는다(1.6초 · 48장). 오른손 · 머리의 프레임당 이동이
    // 중앙값보다 크게 튄 프레임(자세가 한 번에 바뀐 곳)을 적는다. 결과 Temp/ui_shots/WF_Film_##.png
    private static async UniTask ComboFilmAsync(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        if (!await EquipAsync(p, Nameless, ct)) { sb.AppendLine("무형검 장착 실패"); return; }
        HideStrays();
        await Wait(5f, ct);
        Vector3 home = p.transform.position; Quaternion homeRot = Quaternion.LookRotation(Flat(p.transform.forward));
        GoHome(p, home, homeRot);
        await Wait(1f, ct);
        Directory.CreateDirectory("Temp/ui_shots");
        var camGo = new GameObject("~FilmCam");
        var cam = camGo.AddComponent<Camera>();
        if (Camera.main != null) cam.CopyFrom(Camera.main);
        cam.enabled = false; cam.fieldOfView = 34f;
        Vector3 f = Flat(p.transform.forward), r = Vector3.Cross(Vector3.up, f), aim = p.transform.position + f * 4f;
        var hand = p.Anim.GetBoneTransform(HumanBodyBones.RightHand);
        var head = p.Anim.GetBoneTransform(HumanBodyBones.Head);
        var moves = new List<(float t, float hand, float head, string tag)>();
        Vector3 lastH = Vector3.zero, lastHead = Vector3.zero;
        Time.captureFramerate = 30;   // 사진 저장이 느려도 게임 시간이 한 장에 1/30초씩만 가게(1회차: 한 장에 0.09초씩 건너뜀)
        try
        {
            float t0 = Time.time, nextClick = 0f; int clicks = 0, frame = 0; float nextShot = 0f;
            while (Time.time - t0 < 1.6f)
            {
                float el = Time.time - t0;
                if (clicks < 6 && el >= nextClick) { AimAt(aim); RequestFace(p, aim); p.InputBuffer.Push(Command.Light); clicks++; nextClick = el + 0.3f; }
                await UniTask.Yield(PlayerLoopTiming.PostLateUpdate, ct);
                Vector3 hl = p.transform.InverseTransformPoint(hand.position), hd = p.transform.InverseTransformPoint(head.position);
                if (frame > 0) moves.Add((Time.time - t0, (hl - lastH).magnitude / Mathf.Max(1e-4f, Time.deltaTime), (hd - lastHead).magnitude / Mathf.Max(1e-4f, Time.deltaTime), ClipTag(p.Anim)));
                lastH = hl; lastHead = hd; frame++;
                if (Time.time - t0 >= nextShot)
                {
                    nextShot += 1f / 30f;
                    Vector3 pos = p.transform.position;
                    cam.transform.position = pos + r * 4.2f + Vector3.up * 1.1f;
                    cam.transform.LookAt(pos + Vector3.up * 0.9f);
                    CaptureMain(cam, $"Temp/ui_shots/WF_Film_{frame:000}.png");
                }
            }
        }
        finally { Time.captureFramerate = 0; UnityEngine.Object.Destroy(camGo); }
        var hs = moves.ConvertAll(m => m.hand); hs.Sort();
        var ds = moves.ConvertAll(m => m.head); ds.Sort();
        float medH = hs.Count > 0 ? hs[hs.Count / 2] : 0f, medD = ds.Count > 0 ? ds[ds.Count / 2] : 0f;
        sb.AppendLine($"프레임 {moves.Count} · 오른손 속도 중앙 {medH:0.0} m/s · 머리 속도 중앙 {medD:0.00} m/s");
        int jumps = 0;
        foreach (var m in moves)
            if (m.head > Mathf.Max(0.8f, medD * 6f)) { jumps++; sb.AppendLine($"   머리 튐 {m.t:0.00}초 {m.head:0.00} m/s ({m.tag})"); }
        sb.AppendLine($"머리 튐(중앙 ×6 이상 · 0.8 m/s 이상) {jumps}프레임");
    }

    // ── 35 평타 전수(10-12 사용자 「플레이어 무기의 일반 공격을 예전 방식과 체크 — 좀 더 개선이 필요」) ──────
    // 무기 5종을 차례로 들고 2.2 m 앞 표적(체력 큰 슬라임 · 붙듦)에 3초 동안 연타한다 — 빠른 연타(0.1초) · 보통 박자(0.3초).
    // 타마다: 시작 시각 · 콤보 칸 · 클립 · 애니 속도 · 첫 피해까지 · 전진. 묶음: 피해 횟수 · 초당 · 피해 사이 최대 빈틈 · 공격 상태가 끊긴 횟수.
    private static readonly (string tag, string path, int slot)[] NormalAttackWeapons =
    {
        ("무형검", Nameless, PlayerWeaponManager.Slot0), ("카타나", Katana, PlayerWeaponManager.Slot0), ("대검", Great, PlayerWeaponManager.Slot0),
        ("활", Bow, PlayerWeaponManager.Slot1), ("석궁", Crossbow, PlayerWeaponManager.Slot1),
    };

    private sealed class NaStep { public float Start, FirstHit = -1f, Speed, Lunge; public int Step; public string Clip = "?"; }

    private static async UniTask NormalAttackAuditAsync(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        HideStrays();
        await Wait(5f, ct);
        Vector3 home = p.transform.position;
        Quaternion homeRot = Quaternion.LookRotation(Flat(p.transform.forward));
        Vector3 f = Flat(p.transform.forward);
        Vector3 aim = home + f * 2.2f;
        foreach (var (tag, path, slot) in NormalAttackWeapons)
        {
            if (!await EquipAsync(p, path, ct, slot)) { sb.AppendLine($"■ {tag} 장착 실패"); continue; }
            var wd = p.WeaponManager.CurrentWeaponData;
            var set = wd?.animationSet as WeaponAnimationSetSO;
            sb.AppendLine($"■ {tag} — {s_equipNote} · 무기 템포(attack_speed) {wd?.attackSpeed:0.00} · 애니 보정 {set?.lightAttackAnimSpeed:0.00} · 콤보 칸 {wd?.groundEndCount}");
            foreach (float gap in new[] { 0.1f, 0.3f, 9f })   // 9 = 한 번만 누르고 기다림
            {
                GoHome(p, home, homeRot);
                await Wait(0.8f, ct);
                var target = await SpawnAsync(aim, ct);
                if (target == null) { sb.AppendLine("   표적 소환 실패"); break; }
                var track = new HpTrack(target);
                s_tracks.Add(track);
                try { await NormalAttackPatternAsync(p, sb, track, gap, aim, ct); }
                finally { s_tracks.Remove(track); if (target != null) target.gameObject.SetActive(false); }
                await Wait(1.2f, ct);
            }
        }
    }

    private static async UniTask NormalAttackPatternAsync(PlayerController p, StringBuilder sb, HpTrack track, float gap, Vector3 aim, CancellationToken ct)
    {
        var steps = new List<NaStep>();
        NaStep cur = null;
        float t0 = Time.time, nextClick = 0f, lastDealt = track.Dealt, lastHit = -1f, maxGap = 0f, firstHitAt = -1f;
        int clicks = 0, hits = 0, breaks = 0, lastStep = -1, clipFrames = 0;
        float attackTime = 0f, slowHold = 0f;   // 공격 상태 지속 · 그중 느린 대기(애니 속도 ≤ 0.51 — ComboHoldAnimSpeed)
        bool wasAttacking = false;
        Vector3 lastPos = p.transform.position;
        while (Time.time - t0 < 3f)
        {
            float el = Time.time - t0;
            if (el >= nextClick) { AimAt(aim); RequestFace(p, aim); p.InputBuffer.Push(Command.Light); clicks++; nextClick = el + gap; }
            await UniTask.Yield(PlayerLoopTiming.PostLateUpdate, ct);
            float t = Time.time - t0;
            bool attacking = p.Combo.IsAttacking;
            int step = p.Combo.CurrentComboStep;
            if (attacking) { attackTime += Time.deltaTime; if (p.Anim.speed <= 0.51f) slowHold += Time.deltaTime; }
            if (wasAttacking && !attacking) breaks++;
            if (attacking && (!wasAttacking || step != lastStep))
            {
                cur = new NaStep { Start = t, Step = step };
                steps.Add(cur);
                clipFrames = 0;
            }
            wasAttacking = attacking;
            lastStep = step;
            if (cur != null)
            {
                Vector3 d = p.transform.position - lastPos; d.y = 0f;
                cur.Lunge += d.magnitude;
                if (++clipFrames == 3) { cur.Clip = ClipTag(p.Anim); cur.Speed = p.Anim.speed; }
            }
            lastPos = p.transform.position;
            if (track.Dealt > lastDealt + 0.01f)
            {
                hits++;
                if (firstHitAt < 0f) firstHitAt = t;
                if (lastHit >= 0f) maxGap = Mathf.Max(maxGap, t - lastHit);
                lastHit = t;
                if (cur != null && cur.FirstHit < 0f) cur.FirstHit = t - cur.Start;
                lastDealt = track.Dealt;
            }
        }
        float span = lastHit > firstHitAt && firstHitAt >= 0f ? lastHit - firstHitAt : 0f;
        sb.AppendLine($"   □ {(gap > 3f ? "한 번만 누름" : $"연타 {gap:0.0}초")} — 공격 상태 {attackTime:0.00}초 · 그중 느린 대기 {slowHold:0.00}초 · 누름 {clicks} · 피해 {hits}번(첫 피해 {firstHitAt:0.00}초 · 이후 초당 {(span > 0f ? (hits - 1) / span : 0f):0.0}) · 피해 사이 최대 빈틈 {maxGap:0.00}초 · 공격 상태 끊김 {breaks} · 총 피해 {track.Dealt:0}");
        foreach (var st in steps)
            sb.AppendLine($"      {st.Start:0.00}초 칸 {st.Step + 1} · {st.Clip} · 애니 속도 {st.Speed:0.00} · 첫 피해 +{(st.FirstHit >= 0f ? st.FirstHit.ToString("0.00") : "—")}초 · 전진 {st.Lunge:0.00} m");
    }

    // ── 34 이펙트 톤 촬영(10-11 사용자 「이펙트의 무분별한 사용 — 톤에 맞게」 · feedback_vfx_tone_restraint) ──────
    // 79 담당 무기 · 유물 이펙트를 게임 카메라로 플레이어 앞 1.5 m · 가슴 높이에 게임과 같은 배율로 띄워 0.05 · 0.15 · 0.35 · 0.7초에 찍는다.
    // 사라질 때까지(입자 전부 꺼짐) 시간 · 최대 크기(렌더러 경계)를 적는다. 밝기는 사진에서(띄우기 전 배경 대비) 따로 잰다.
    private const string Ifx = "Assets/RelicFairy/_Imported/EffectSource/";
    private static readonly (string tag, string path, float scale)[] ToneFx =
    {
        ("nameless_slash", "Assets/RelicFairy/Weapon/Nameless/Prefabs/NamelessSlash.prefab", 0.7f),
        ("katana_slash",   "Assets/RelicFairy/Prefabs/UseEffect/PlayerEffect/SlashAttack.prefab", 1f),
        ("great_slash",    "Assets/RelicFairy/_Imported/Matthew Guz/Slash Effects FREE/Prefab/Basic Slash Blue.prefab", 1f),
        ("hit_small",      "Assets/RelicFairy/Prefabs/UseEffect/PlayerEffect/SwordHitImpact.prefab", 0.29f),
        ("hit_great",      "Assets/RelicFairy/Prefabs/UseEffect/PlayerEffect/SwordHitImpact.prefab", 0.84f),   // 대검 3타(쓰는 값 · 1.26은 폐기한 내려찍기 데이터)
        ("arrow_hit",      Ifx + "Hovl Studio/AAA Projectiles Vol 1/Prefabs/Hit 4 yellow arrow.prefab", 0.5f),
        ("lancelot_hit",   Ifx + "MasterStylizedProjectiles/Projectiles/RedSwordBeam/Prefabs/Par_RedSwordBeam_Hit.prefab", 0.3f),   // 연타(10-11 0.5 → 0.3)
        ("lancelot_hit_last", Ifx + "MasterStylizedProjectiles/Projectiles/RedSwordBeam/Prefabs/Par_RedSwordBeam_Hit.prefab", 0.5f),
        ("gawain_hit",     Ifx + "Hovl Studio/AAA Projectiles Vol 1/Prefabs/Hit 16 fire.prefab", 1f),
        ("lancelot_finish", Ifx + "SpecialSkillsEffectsPack/AllEffects/EffectsSet_1(NotScriptBased)/Effects/Effect_36_MadnessSlash/Effect_36_MadnessSlash.prefab", 1f),
        ("memory_orb",     Ifx + "Hovl Studio/RPG VFX Bundle/Random effect prefabs/Gold dot.prefab", 1f),
    };

    // 34b: 이름 끝 「_H」 = 입자 전부 Hierarchy(배율이 자식 입자까지 듣게) — 게임에 넣을 고침을 미리 본다
    private const string SwordHit = "Assets/RelicFairy/Prefabs/UseEffect/PlayerEffect/SwordHitImpact.prefab";
    private const string LanceHit = "Assets/RelicFairy/_Imported/EffectSource/MasterStylizedProjectiles/Projectiles/RedSwordBeam/Prefabs/Par_RedSwordBeam_Hit.prefab";
    private static readonly (string tag, string path, float scale)[] ToneFxHier =
    {
        ("hit029",   SwordHit, 0.29f), ("hit029_H",  SwordHit, 0.29f), ("hit05_H",  SwordHit, 0.5f), ("hit084_H", SwordHit, 0.84f),
        ("lance03",  LanceHit, 0.3f),  ("lance03_H", LanceHit, 0.3f),  ("lance05_H", LanceHit, 0.5f), ("lance07_H", LanceHit, 0.7f),
    };

    private static async UniTask ToneFxAsync(PlayerController p, StringBuilder sb, CancellationToken ct, (string tag, string path, float scale)[] list = null)
    {
        list ??= ToneFx;
        HideStrays();
        await Wait(5f, ct);
        Directory.CreateDirectory("Temp/ui_shots");
        var cam = Camera.main;
        if (cam == null) { sb.AppendLine("게임 카메라 없음"); return; }
        Vector3 f = Flat(p.transform.forward);
        Vector3 at = p.transform.position + f * 1.5f + Vector3.up * 1f;
        CaptureMain(cam, "Temp/ui_shots/TF_base.png");
        Vector3 sp = cam.WorldToScreenPoint(at);
        sb.AppendLine($"화면 위치(960×540 기준) {sp.x / cam.pixelWidth * 960f:0} , {(1f - sp.y / cam.pixelHeight) * 540f:0} · 카메라 거리 {Vector3.Distance(cam.transform.position, at):0.0} m");
        foreach (var (tag, path, scale) in list)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) { sb.AppendLine($"{tag}: 프리팹 없음"); continue; }
            HideStrays();   // 1회차: 파도 몬스터가 몰려와 화면을 가렸다
            foreach (var mb in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None)) if (mb != null && !mb.IsDead) mb.gameObject.SetActive(false);
            await Wait(0.2f, ct);
            var go = UnityEngine.Object.Instantiate(prefab, at, Quaternion.LookRotation(f));
            go.transform.localScale *= scale;
            var systems = go.GetComponentsInChildren<ParticleSystem>(true);
            if (tag.EndsWith("_H"))
                foreach (var ps in systems) { var mm = ps.main; mm.scalingMode = ParticleSystemScalingMode.Hierarchy; }
            float t0 = Time.time, life = -1f, maxSize = 0f;
            float[] shots = { 0.05f, 0.15f, 0.35f, 0.7f };
            int k = 0;
            while (Time.time - t0 < 4f)
            {
                await UniTask.Yield(PlayerLoopTiming.PostLateUpdate, ct);
                if (go == null) { life = Time.time - t0; break; }
                bool alive = false;
                foreach (var ps in systems) if (ps != null && ps.IsAlive(true)) { alive = true; break; }
                var b = new Bounds(at, Vector3.zero); bool any = false;
                foreach (var r in go.GetComponentsInChildren<Renderer>()) { if (!r.enabled) continue; if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
                if (any) maxSize = Mathf.Max(maxSize, b.size.magnitude);
                if (k < shots.Length && Time.time - t0 >= shots[k]) { CaptureMain(cam, $"Temp/ui_shots/TF_{tag}_{k}.png"); k++; }
                if (systems.Length > 0 && !alive && Time.time - t0 > 0.1f) { life = Time.time - t0; break; }
            }
            sb.AppendLine($"{tag,-18} 배율 {scale} · 지속 {(life < 0f ? "4초 넘음(반복?)" : life.ToString("0.00") + "초")} · 최대 크기 {maxSize:0.0} m · 입자 시스템 {systems.Length}");
            if (go != null) UnityEngine.Object.Destroy(go);
            await Wait(0.4f, ct);
        }
    }

    // ── 32 랜슬롯 Q 연타 중 회전(10-10 사용자 「Q를 잘못 쓰거나 몬스터가 움직이면 헛치기가 길어진다 — 중간중간 회전」) ──
    // 앞을 보고 Q → 0.3초 뒤 마우스를 오른쪽 90°로 옮긴다. 연타 동안 몸이 그쪽으로 도는지(마무리 전에 닿는지) 잰다.
    private static async UniTask QTurnAsync(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        var relic = p.RelicBehavior as LancelotMadnessRelic;
        if (relic == null) { sb.AppendLine("랜슬롯 유물이 아니다"); return; }
        if (!await EquipAsync(p, Katana, ct)) { sb.AppendLine("카타나 장착 실패"); return; }
        HideStrays();
        Vector3    home    = p.transform.position;
        Quaternion homeRot = Quaternion.LookRotation(Flat(p.transform.forward));
        GoHome(p, home, homeRot);
        await Wait(1f, ct);
        Vector3 f = Flat(p.transform.forward), r = Vector3.Cross(Vector3.up, f);
        AimAt(p.transform.position + f * 6f);
        await Wait(0.3f, ct);
        relic.Madness.SetStacks(relic.Madness.MaxStacks);
        if (!relic.Madness.IsFrenzy) relic.Madness.EnterFrenzy(30f);
        await Wait(0.4f, ct);   // 광란 진입 직후 바로 누르면 Q가 시작되지 않았다(1회차 · 장비 형/28은 0.2초 기다렸다)
        p.CooldownTracker.ResetCooldown(SkillType.Q);
        p.InputBuffer.Clear();
        p.InputBuffer.Push(Command.QSkill);
        float q0 = Time.time, yaw0 = p.transform.eulerAngles.y, targetYaw = Quaternion.LookRotation(r).eulerAngles.y;
        int qHash1 = Animator.StringToHash("RelicQ_Slash1"), qHash2 = Animator.StringToHash("RelicQ_Slash2"), qHash3 = Animator.StringToHash("RelicQ_Slash3");
        bool sawQ = false;
        await Wait(0.3f, ct);
        AimAt(p.transform.position + r * 6f);
        float reachedAt = -1f, at1 = 0f;
        var trace = new StringBuilder();
        while (Time.time - q0 < 1.75f)
        {
            await UniTask.Yield(PlayerLoopTiming.PostLateUpdate, ct);
            AimAt(p.transform.position + r * 6f);   // 카메라가 따라 움직여도 같은 쪽을 가리키게
            int sh = p.Anim.GetCurrentAnimatorStateInfo(0).shortNameHash;
            if (sh == qHash1 || sh == qHash2 || sh == qHash3) sawQ = true;
            float d = Mathf.Abs(Mathf.DeltaAngle(p.transform.eulerAngles.y, targetYaw));
            if (reachedAt < 0f && d < 12f) reachedAt = Time.time - q0;
            if (Time.time - at1 >= 0.15f)
            {
                at1 = Time.time;
                string m = p.TryGetMouseLookDir(out var md) ? $"{Mathf.DeltaAngle(yaw0, Quaternion.LookRotation(md).eulerAngles.y):0}" : "없음";
                trace.Append($"{Time.time - q0:0.00}:{Mathf.DeltaAngle(yaw0, p.transform.eulerAngles.y):0}°(마우스 {m}°) ");
            }
        }
        Check(sb, $"연타 중 오른쪽 90°로 회전 — 닿은 시각 {(reachedAt < 0f ? "못 닿음" : reachedAt.ToString("0.00") + "초")}(Q 누른 때부터 · 마우스는 0.3초에 옮김)",
              reachedAt > 0.3f && reachedAt < 1.7f);
        sb.AppendLine($"   Q 연타 상태 봄 {sawQ}");
        sb.AppendLine("   몸 방향(처음 대비): " + trace);
        await Wait(2.5f, ct);
        relic.Madness.SetStacks(0);
    }

    private static Vector2 s_aimScreen;

    private static void AimAt(Vector3 world)
    {
        var mouse = Mouse.current;
        var cam   = Camera.main;
        if (mouse == null || cam == null) return;
        Vector3 screen = cam.WorldToScreenPoint(world + Vector3.up * 0.8f);
        if (screen.z <= 0f) return;
        s_aimScreen = screen;
        InputSystem.QueueDeltaStateEvent(mouse.position, (Vector2)screen);
    }

    private static void RequestFace(PlayerController p, Vector3 at)
    {
        Vector3 d = Flat(at - p.transform.position);
        p.RequestFacing(Quaternion.LookRotation(d, Vector3.up));
    }

    private static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward;
    }

    // ── 공통 ────────────────────────────────────────────────────
    private static void Check(StringBuilder sb, string name, bool ok) => sb.AppendLine($"{(ok ? "통과" : "실패")} — {name}");

    private static async UniTask<bool> EquipAsync(PlayerController p, string soPath, CancellationToken ct, int slot = PlayerWeaponManager.Slot0)
    {
        var wm = p.WeaponManager;
        string key = Path.GetFileNameWithoutExtension(soPath);
        var so = AssetDatabase.LoadAssetAtPath<WeaponSO>(soPath);
        if (so == null) return false;
        var runtime = WeaponData.FromSO(so);
        // 실제 게임(GameRunBootstrapper)처럼 무기 클립을 먼저 캐시에 올린다 — 안 올리면 장착 때 애니 덮어쓰기가 조용히 건너뛰어
        // 기본(카타나) 클립으로 휘두른다(10-07 최종 검토 I1 — 그 전 대검 · 무형검 실측은 실제 모션이 아니었다)
        var preload = typeof(PlayerWeaponManager).GetMethod("PreloadWeaponClipsAsync", BindingFlags.Static | BindingFlags.NonPublic);
        if (preload != null) await (UniTask)preload.Invoke(null, new object[] { runtime, ct });
        int clipOk = 0, clipAll = 0;
        if (runtime.animationSet != null)
            foreach (var m in runtime.animationSet.GetAllMappings())
                if (m.actionType == WeaponActionType.GroundLight && !string.IsNullOrEmpty(m.addressableKey))
                { clipAll++; if (Managers.AnimationResources.GetClip(m.addressableKey) != null) clipOk++; }
        s_equipNote = $"{key}: 지상 콤보 클립 {clipOk}/{clipAll} 캐시";
        Debug.Log("[WeaponFormProbe] " + s_equipNote);
        // 슬롯 인스턴스가 있으면 장착이 데이터만 바꾸고 모델은 그대로 둔다 — 먼저 해제한다(LancelotQSwordProbeEditor와 같은 길)
        typeof(PlayerWeaponManager).GetMethod("DestroySlotInstance", BindingFlags.Static | BindingFlags.NonPublic)
            ?.Invoke(null, new object[] { wm.slots[slot] });
        await UniTask.Yield(PlayerLoopTiming.Update, ct);
        await wm.AcquireWeaponToSlotAsync(runtime, slot, setActive: true, playAppear: false);
        if (wm.CurrentSlotIndex != slot) await wm.SwitchToSlotAsync(slot);
        float t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < 8f)
        {
            var inst = wm.CurrentWeaponInstance;
            if (inst != null && inst.name.StartsWith(key) && inst.activeInHierarchy) return true;
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
        }
        return false;
    }

    private static UniTask Wait(float s, CancellationToken ct)
        => UniTask.Delay(TimeSpan.FromSeconds(s), DelayType.Realtime, cancellationToken: ct);
}
#endif
