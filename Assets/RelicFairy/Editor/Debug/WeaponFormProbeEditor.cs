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

    [MenuItem(Root + "8e 평타 이펙트 방향 — 무형검만 (10-19 ④, 테스트 허브, 플레이 중)")]
    private static void BeginSlashNameless() => Begin("slash_nameless");

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

    [MenuItem(Root + "56 룬 · 서약 다 낀 빌드 + 활 최대 다발 — 단일 · 무리 · 타격 트리거 (테스트 허브, 플레이 중, 약 4분)")]
    private static void BeginLoadout() => Begin("loadout");

    [MenuItem(Root + "58 근접 강화 증강 2차 — 진화 변환 · 제시 거름 · 평타 · 스킬 · 돌진 (테스트 허브, 플레이 중)")]
    private static void BeginAugment2() => Begin("augment2");

    [MenuItem(Root + "63 무형검 3타 두 번 베기 · 이동 클립 (테스트 허브, 플레이 중)")]
    private static void BeginNamelessFinisher() => Begin("namelessfin");

    [MenuItem(Root + "64 유물 Q 대 평타 — 가웨인 낙일 (테스트 허브, 플레이 중)")]
    private static void BeginRelicQGawain() => Begin("relicq_g");

    [MenuItem(Root + "64b 유물 Q 대 평타 — 랜슬롯 심판의 일격 (테스트 허브, 플레이 중)")]
    private static void BeginRelicQLancelot() => Begin("relicq_l");

    [MenuItem(Root + "64c 유물 Q 대 평타 — 가웨인 · 바꾼 수치 Temp/relicq_override.txt (테스트 허브, 플레이 중)")]
    private static void BeginRelicQGawain2() => Begin("relicq_g2");

    [MenuItem(Root + "64d 유물 Q 대 평타 — 랜슬롯 · 바꾼 수치 Temp/relicq_override.txt (테스트 허브, 플레이 중)")]
    private static void BeginRelicQLancelot2() => Begin("relicq_l2");

    [MenuItem(Root + "65 분열 대칭 · 무형참 검기 폭 · 형 없는 연타 길이 (10-18, 테스트 허브, 플레이 중)")]
    private static void BeginSplit65() => Begin("split65");

    [MenuItem(Root + "66 평타 누름 → 베기 · 근접 스킬 E · R 묶임 · 피해 · 비켜 선 적 (10-18, 테스트 허브, 플레이 중)")]
    private static void BeginSkills66() => Begin("skills66");

    [MenuItem(Root + "67 근접 스킬 애니 맞물림 — 적중 대 손 봉우리 · 클립 끝 대 스킬 끝 (10-18, 테스트 허브, 플레이 중)")]
    private static void BeginSkillSync67() => Begin("skillsync67");

    [MenuItem(Root + "68 룬 · 서약 이펙트 — 보이는 크기 대 판정 · 사진 (10-18, 테스트 허브, 플레이 중)")]
    private static void BeginRuneFx68() => Begin("runefx68");

    [MenuItem(Root + "69 무형검 증강 환영 이식 — 진화 뒤 남음 · 환영 발동 · 보이기 (10-18, 테스트 허브, 플레이 중)")]
    private static void BeginPhantom69() => Begin("phantom69");

    [MenuItem(Root + "70 재련 개편 — 근접 +3 · 증강 · 비용 · 띠 · 화로 · 원거리 카드 · 쉼터 (10-18, 테스트 허브, 플레이 중)")]
    private static void BeginReforge70() => Begin("reforge70");

    [MenuItem(Root + "71 증강 추가타 검기 — 보이는 크기 대 판정(길이 · 반폭) (10-18, 테스트 허브, 플레이 중)")]
    private static void BeginAugWave71() => Begin("augwave71");

    [MenuItem(Root + "64f 추가 피해 판정 = 몸에 닿으면 — 큰 몬스터 직선 · 원뿔 (테스트 허브, 플레이 중)")]
    private static void BeginBodyJudge() => Begin("bodyjudge");

    [MenuItem(Root + "64e 평타 바닥 선 — 증강 칼자국 · 거인의 걸음 · 천둥 (테스트 허브, 플레이 중)")]
    private static void BeginSwingGuides() => Begin("swingguides");

    [MenuItem(Root + "62 아이템 직접 피해 집계 — 추가 타격 복사 = 룬 몫 (테스트 허브, 플레이 중)")]
    private static void BeginItemDirectSource() => Begin("itemsource");

    [MenuItem(Root + "61 원소의 날 · 형 「쉬었다 친 첫 타」 (테스트 허브, 플레이 중)")]
    private static void BeginElementEdge() => Begin("elementedge");

    [MenuItem(Root + "60 같은 투자 비교 — 활 최대 다발 대 근접 +20 · 증강 (테스트 허브, 플레이 중, 약 4분)")]
    private static void BeginEqualInvest() => Begin("equalinvest");

    [MenuItem(Root + "60b 같은 투자 비교 — 스킬 포함(E · R 쿨마다) (테스트 허브, 플레이 중, 약 5분)")]
    private static void BeginEqualInvestSkills() => Begin("equalinvest2");

    [MenuItem(Root + "59 규칙형 증강 — 기습 · 튕김 · X자 · 칼자국 · 표식 · 처형 · 연쇄 · 마지막 의지 · 철벽 (테스트 허브, 플레이 중)")]
    private static void BeginAugment3() => Begin("augment3");

    [MenuItem(Root + "59b 규칙형 증강 — 마지막 의지 칸만 (테스트 허브, 플레이 중)")]
    private static void BeginAugment3b() => Begin("augment3b");

    [MenuItem(Root + "58b 근접 강화 증강 2차 — 돌진 · 무형검 칸만 (테스트 허브, 플레이 중)")]
    private static void BeginAugment2b() => Begin("augment2b");

    [MenuItem(Root + "57 근접 강화 증강 — 등급 확률 · 뽑기 · 효과 · 강화 표 (테스트 허브, 플레이 중)")]
    private static void BeginAugment() => Begin("augment");

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

    [MenuItem(Root + "72 랜슬롯 광기 수급 — 행동 단위 · 잡몹 1 · 3 · E (10-19 ⑤, 테스트 허브, 플레이 중)")]
    private static void BeginMadness72() => Begin("madness72");

    [MenuItem(Root + "73 타격 이펙트 후보 사진 — 검 섬광 · 추가타 표시 (10-19 ⑫⑭, 테스트 허브, 플레이 중)")]
    private static void BeginHitFx73() => Begin("hitfx73");

    [MenuItem(Root + "74 검 쥐는 자리 — 주먹 구멍 대 손잡이 · 손 사진 (10-19 ③, 테스트 허브, 플레이 중)")]
    private static void BeginGrip74() => Begin("grip74");

    [MenuItem(Root + "75 타격 표시 실전 — 검 섬광 · 추가타 표시 · 숫자 (10-19 ⑫⑭, 테스트 허브, 플레이 중)")]
    private static void BeginHitFx75() => Begin("hitfx75");

    [MenuItem(Root + "76 무형검 E 발사 · R 끝 — 게임 시간 · 실제 시간 (10-19 W3, 테스트 허브, 플레이 중)")]
    private static void BeginSkillClock76() => Begin("skillclock76");

    [MenuItem(Root + "77 Shift 대시 — 바인딩 · 키 눌러 대시 (10-20, 테스트 허브, 플레이 중)")]
    private static void BeginShiftDash77() => Begin("shiftdash77");

    [MenuItem(Root + "78 대검 · 카타나 스킬 동작 후보 미리보기 (10-22, 테스트 허브, 플레이 중)")]
    private static void BeginSkillCand78() => Begin("skillcand78");

    [MenuItem(Root + "79 대검 충격 · 카타나 베기 이펙트 후보 사진 (10-22, 테스트 허브, 플레이 중)")]
    private static void BeginImpactFx79() => Begin("impactfx79");

    [MenuItem(Root + "80 스킬 개편 맞춤값 — 클립 타이밍 · 이펙트 크기 (10-22, 테스트 허브, 플레이 중)")]
    private static void BeginSkillFit80() => Begin("skillfit80");

    [MenuItem(Root + "81 개편 스킬 실전 — 대검 · 카타나 R · E 사진 · 적중 (10-22, 테스트 허브, 플레이 중)")]
    private static void BeginSkillShots81() => Begin("skillshots81");

    [MenuItem(Root + "82 대검 평타 무게 · 범위 + 내려찍기 충격 후보 (10-22 B1 B5, 테스트 허브, 플레이 중)")]
    private static void BeginGsBasic82() => Begin("gsbasic82");

    [MenuItem(Root + "83 룬 타격 크기 · 무형검 추가 검기 · 분신 (10-22 B2 B3 B6, 테스트 허브, 플레이 중)")]
    private static void BeginRuneHit83() => Begin("runehit83");

    [MenuItem(Root + "84 쉼터 무료 재련 — 근접 · 원거리 (10-22 B7, 테스트 허브, 플레이 중)")]
    private static void BeginRestRanged84() => Begin("restranged84");

    [MenuItem(Root + "85 증강 검기 분신 · 판정 · 무형참 관통 · 대검 평타 크기 (10-22, 테스트 허브, 플레이 중)")]
    private static void BeginWaveGhost85() => Begin("waveghost85");

    [MenuItem(Root + "85b 무형참만 — 적중 · 투사체 자리 (10-22, 테스트 허브, 플레이 중)")]
    private static void BeginBeam85b() => Begin("beam85b");

    [MenuItem(Root + "86 검기 재생 · 판정 시점 · 분신 박자 (10-23 W1 W2, 테스트 허브, 플레이 중)")]
    private static void BeginWaveTime86() => Begin("wavetime86");

    [MenuItem(Root + "88 회피로 스킬 끊기 — E · R · 유물 Q (10-23 밤, 테스트 허브, 플레이 중)")]
    private static void BeginDodgeCancel88() => Begin("dodgecancel88");

    [MenuItem(Root + "89 공격 멈칫 켬 · 끔 — 평타 · E · R · 랜슬롯 Q · 처치 (10-24, 테스트 허브, 플레이 중)")]
    private static void BeginHitStop89L() => Begin("hitstop89l");

    [MenuItem(Root + "89g 공격 멈칫 켬 · 끔 — 가웨인 Q (10-24, 테스트 허브, 플레이 중)")]
    private static void BeginHitStop89G() => Begin("hitstop89g");

    [MenuItem(Root + "90 전설 증강 6종 · 잔상 일섬 · 전설 굴림 (10-24 W7, 테스트 허브, 플레이 중)")]
    private static void BeginLegend90() => Begin("legend90");

    [MenuItem(Root + "91 막타 시점 · 풍절 실림 · 분신 오류 · 원거리 룬 거름 (10-24 점검 고침, 테스트 허브, 플레이 중)")]
    private static void BeginFinisher91() => Begin("finisher91");

    [MenuItem(Root + "92 일섬 처치 → E 초기화 진단 (10-24, 테스트 허브, 플레이 중)")]
    private static void BeginIssen92() => Begin("issen92");

    [MenuItem(Root + "93 과전류 포박 장판 중심 · 그림자 분신 사진 · 끝없는 칼날 여러 적 (10-24, 테스트 허브, 플레이 중)")]
    private static void BeginLegendShots93() => Begin("legendshots93");

    [MenuItem(Root + "94 전설 룬 놓기 · 존 단계 정하기 (10-24 전설 개편, 테스트 허브, 플레이 중)")]
    private static void BeginLegend94() => Begin("legend94");

    [MenuItem(Root + "95 전설 룬 개편 1 · 2차 화력 몫 — 17종 × 존 2 · 4 (10-24, 테스트 허브, 플레이 중, Temp/legend95_only.txt로 고름)")]
    private static void BeginLegend95() => Begin("legend95");

    [MenuItem(Root + "96 평타 꾹 누르기 — 짧게 1번 · 꾹 2.5초 · 뗀 뒤 멈춤 · 연타 대조 · 멈춘 화면 · 활 (10-25 ④, 테스트 허브, 플레이 중)")]
    private static void BeginHoldAttack96() => Begin("holdatk96");

    [MenuItem(Root + "97 무기 개편 — 걷은 증강 · 재련 자람 · 그림자 · 검풍 · 대검 범위 · 거인의 검 · 속성 자국 (10-26, 테스트 허브, 플레이 중, Temp/weapon97_only.txt로 고름)")]
    private static void BeginWeapon97() => Begin("weapon97");

    [MenuItem(Root + "98 무기 개편 화력 몫 — 카타나 그림자 · 검풍 · 대검 범위 ×1 대 ×1.4 (10-26, 테스트 허브, 플레이 중, 약 8분)")]
    private static void BeginWeapon98() => Begin("weapon98");

    [MenuItem(Root + "99 카타나 · 대검 재련 · 스킬 · 증강 실전 (10-26, 테스트 허브, 플레이 중, Temp/weapon99_only.txt로 고름 — A 재련 · B 스킬 · C 증강 · K 카타나 · G 대검)")]
    private static void BeginWeapon99() => Begin("weapon99");

    private static void Begin(string job)
    {
        if (!Application.isPlaying) { Debug.LogWarning("[WeaponFormProbe] 플레이 모드에서만 동작한다."); return; }
        var launcher = UnityEngine.Object.FindFirstObjectByType<TestHubLauncher>();
        if (launcher == null) { Debug.LogWarning("[WeaponFormProbe] 테스트 허브에서 시작한다."); return; }
        // 예산은 유물 없이 — 가웨인 「정오」 주기 · 낙인 누적이 칸 순서대로 피해를 키워 형 몫을 가렸다(실측 5 1회차 대검 318 → 405 → 552).
        // 인덱스 −1은 허브 화면이 0으로 되돌린다(2회차도 가웨인) → 목록을 「없음」 하나로 바꾼다(Pick → null → 유물 없음).
        if (job.StartsWith("budget") || job == "r2" || job == "swingguides" || job == "bodyjudge" || job == "split65" || job == "skills66" || job == "skillsync67" || job == "runefx68" || job == "hitfx73" || job == "grip74" || job == "hitfx75" || job == "skillclock76" || job == "shiftdash77" || job == "skillcand78" || job == "impactfx79" || job == "skillfit80" || job == "skillshots81" || job == "gsbasic82" || job == "runehit83" || job == "restranged84" || job == "waveghost85" || job == "beam85b" || job == "wavetime86" || job == "legend90" || job == "finisher91" || job == "issen92" || job == "legendshots93" || job == "legend94" || job == "legend95" || job == "weapon97" || job == "weapon98" || job == "weapon99" || job == "phantom69" || job == "reforge70" || job == "augwave71" || (job.StartsWith("bs") && job != "bs6" && !job.StartsWith("bs3b"))) typeof(TestHubLauncher).GetField("relics", Inst)?.SetValue(launcher, new RelicClassSO[] { null });   // R2 · 64e도 유물 없이(10-13 · 10-16)
        if (job == "chains" || job == "qturn" || job == "bs3bl" || job.StartsWith("relicq_") || job == "madness72" || job.StartsWith("hitstop89"))   // 랜슬롯 Q — 허브 유물을 랜슬롯으로(LancelotQSwordProbeEditor와 같은 길)
        {
            string want = job.StartsWith("relicq_g") || job == "hitstop89g" ? "Gawain" : "Lancelot";   // 64 · 64c는 가웨인
            var relics = typeof(TestHubLauncher).GetField("relics", Inst)?.GetValue(launcher) as RelicClassSO[];
            int idx = relics == null ? -1 : Array.FindIndex(relics, r => r != null && r.Id.ToString() == want);
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
        if (msg.StartsWith("[WeaponForm]") || msg.StartsWith("[MeleeAugment]")) s_formLog.Add(msg);
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
                case "slash_nameless": await SlashDirAsync(p, sb, ct, onlyPath: Nameless); break;
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
                case "loadout": await LoadoutAsync(p, sb, ct); break;
                case "augment": await AugmentAsync(p, sb, ct); break;
                case "augment2": await Augment2Async(p, sb, ct); break;
                case "augment2b": await Augment2Async(p, sb, ct, dashOnly: true); break;
                case "augment3": await Augment3Async(p, sb, ct); break;
                case "equalinvest": await EqualInvestAsync(p, sb, ct); break;
                case "elementedge": await ElementEdgeAsync(p, sb, ct); break;
                case "itemsource": await ItemDirectSourceAsync(p, sb, ct); break;
                case "namelessfin": await NamelessFinisherAsync(p, sb, ct); break;
                case "relicq_g": case "relicq_l": case "relicq_g2": case "relicq_l2": await RelicQAsync(p, sb, ct); break;
                case "swingguides": await SwingGuidesAsync(p, sb, ct); break;
                case "bodyjudge": await BodyJudgeAsync(p, sb, ct); break;
                case "split65": await Split65Async(p, sb, ct); break;
                case "skills66": await Skills66Async(p, sb, ct); break;
                case "skillsync67": await SkillSync67Async(p, sb, ct); break;
                case "runefx68": await RuneFx68Async(p, sb, ct); break;
                case "phantom69": await Phantom69Async(p, sb, ct); break;
                case "reforge70": await Reforge70Async(p, sb, ct); break;
                case "augwave71": await AugWave71Async(p, sb, ct); break;
                case "madness72": await Madness72Async(p, sb, ct); break;
                case "hitfx73": await HitFx73Async(p, sb, ct); break;
                case "grip74": await Grip74Async(p, sb, ct); break;
                case "hitfx75": await HitFx75Async(p, sb, ct); break;
                case "skillclock76": await SkillClock76Async(p, sb, ct); break;
                case "shiftdash77": await ShiftDash77Async(p, sb, ct); break;
                case "skillcand78": await SkillCand78Async(p, sb, ct); break;
                case "impactfx79": await ImpactFx79Async(p, sb, ct); break;
                case "skillfit80": await SkillFit80Async(p, sb, ct); break;
                case "skillshots81": await SkillShots81Async(p, sb, ct); break;
                case "gsbasic82": await GsBasic82Async(p, sb, ct); break;
                case "runehit83": await RuneHit83Async(p, sb, ct); break;
                case "restranged84": await RestRanged84Async(p, sb, ct); break;
                case "waveghost85": await WaveGhost85Async(p, sb, ct); break;
                case "beam85b": await WaveGhost85Async(p, sb, ct, onlyBeam: true); break;
                case "wavetime86": await WaveTime86Async(p, sb, ct); break;
                case "dodgecancel88": await DodgeCancel88Async(p, sb, ct); break;
                case "hitstop89l": case "hitstop89g": await HitStop89Async(p, sb, ct, s_job == "hitstop89l"); break;
                case "legend90": await Legend90Async(p, sb, ct); break;
                case "finisher91": await Finisher91Async(p, sb, ct); break;
                case "issen92": await Issen92Async(p, sb, ct); break;
                case "legendshots93": await LegendShots93Async(p, sb, ct); break;
                case "legend94": await Legend94Async(p, sb, ct); break;
                case "legend95": await Legend95Async(p, sb, ct); break;
                case "holdatk96": await HoldAttack96Async(p, sb, ct); break;
                case "weapon97": await Weapon97Async(p, sb, ct); break;
                case "weapon98": await Weapon98Async(p, sb, ct); break;
                case "weapon99": await Weapon99Async(p, sb, ct); break;
                case "equalinvest2": await EqualInvestAsync(p, sb, ct, skills: true); break;
                case "augment3b": await Augment3Async(p, sb, ct, lastWillOnly: true); break;
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
            SkillRangeRing.PlayerGuides = false;   // 사진용으로 켠 플레이어 범위 표시를 끈다(정적 값이라 다음 플레이에 남지 않게)
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

    // ── 58 근접 강화 증강 2차(10-15 사용자 「새로운 근접 무기에만 맞는 효과」) ──
    private static int AugLog(string key) => s_formLog.FindAll(l => l.Contains(key)).Count;
    private static string s_augSwingDiag = "";

    /// <summary>앞 1.5 m 슬라임을 seconds 동안 평타 — 휘두름(슬래시 이벤트) 수를 돌려준다. 로그는 시작 때 비운다.</summary>
    private static async UniTask<int> AugSwingAsync(PlayerController p, float seconds, CancellationToken ct, Action tick = null)
    {
        s_home = p.transform.position;   // 앞 칸(돌진)이 옮긴 자리 — 옛 자리로 끌려가지 않게
        var t = await TrackAsync(p.transform.position + Flat(p.transform.forward) * 1.5f, ct);
        if (t == null) return -1;
        int swings = 0, hits = 0, lastHits = 0;
        var recv = p.GetComponentInChildren<PlayerAnimationEventReceiver>();
        var rt = WeaponFormRuntime.Of(p);
        int lastStep = Mathf.Max(1, p.WeaponManager.Weapon0Data.groundEndCount) - 1;
        Action<int> onSlash = _ => swings++;
        Action<HitInfo> onHit = h =>
        {
            if (h.Attacker != p.gameObject || h.ActionType != WeaponActionType.GroundLight) return;
            hits++;
            if (rt != null && rt.SwingStep >= lastStep) lastHits++;
        };
        if (recv != null) recv.OnEffectStep += onSlash;
        HitFeedbackService.OnHit += onHit;
        s_formLog.Clear();
        try
        {
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < seconds)
            {
                RefillAll();
                t = await EnsureAliveAsync(t, ct);
                Steer(p, t.M.transform.position);
                p.InputBuffer.Push(Command.Light);
                tick?.Invoke();
                await Wait(0.25f, ct);
            }
        }
        finally { if (recv != null) recv.OnEffectStep -= onSlash; HitFeedbackService.OnHit -= onHit; }
        p.InputBuffer.Clear();
        s_augSwingDiag = $"적중 {hits} · 마지막 타 적중 {lastHits} · 표적 피해 {t.Dealt:0} · 거리 {Vector3.Distance(p.transform.position, t.M != null ? t.M.transform.position : p.transform.position):0.0} m · 받는 쪽 {recv != null} · 구동기 {rt != null}";
        await Wait(1.4f, ct);   // 늦게 터지는 효과(지진 1초 · 형 없는 연타 0.2초)
        DetachAll();
        return swings;
    }

    // ── 59 규칙형 증강(10-15 사용자 「증강은 의미 있는 증강이어야 해 — 그냥 스탯 증가만이 아닌」) ──
    private static async UniTask Augment3Async(PlayerController p, StringBuilder sb, CancellationToken ct, bool lastWillOnly = false)
    {
        sb.AppendLine("규칙형 증강 — 스탯 한 줄 대신 화면에서 일어나는 규칙");
        await Wait(3f, ct);
        var run = GameRunBootstrapper.Instance.Run;
        foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
            if (m != null && !m.IsDead) m.gameObject.SetActive(false);
        if (!await EquipAsync(p, Katana, ct)) { sb.AppendLine("카타나 장착 실패"); return; }
        var k = p.WeaponManager.Weapon0Data;
        k.enhanceLevel = 0;
        void Set(WeaponData w, string augs) { w.augments = augs; run.EffectManager.Rebuild(); }
        await Wait(0.5f, ct);
        s_home = p.transform.position;
        if (lastWillOnly) goto LastWill;   // ⑤만(바뀐 칸만 다시)
        Vector3 f = Flat(p.transform.forward), r = Vector3.Cross(Vector3.up, f);

        // ① 기습 + 튕기는 칼끝 II — 2초 쉬고 친 첫 타 = 확정 치명 → 옆 두 적(3.5 m)에게 튄다 · 3번
        Set(k, "aug_ambush:1,aug_ricochet:2");
        var main = await TrackAsync(p.transform.position + f * 1.5f, ct);
        var sideA = await TrackAsync(p.transform.position + f * 1.5f + r * 3.5f, ct);
        var sideB = await TrackAsync(p.transform.position + f * 1.5f - r * 3.5f, ct);
        if (main == null || sideA == null || sideB == null) { sb.AppendLine("슬라임 부르기 실패"); return; }
        s_formLog.Clear();
        for (int i = 0; i < 3; i++)
        {
            p.InputBuffer.Clear();
            await Wait(2.4f, ct);
            RefillAll();
            Steer(p, main.M.transform.position);
            p.InputBuffer.Push(Command.Light);
            await Wait(0.8f, ct);
        }
        p.InputBuffer.Clear();
        int ambAll = AugLog("] 기습"), ambCrit = AugLog("] 기습 — 치명 True"), ric = AugLog("] 튕기는 칼끝 — 2명");
        Check(sb, $"① 기습 {ambCrit}/{ambAll}(치명) · 튕기는 칼끝 2명 {ric} · 옆 표적 피해 {sideA.Dealt:0} · {sideB.Dealt:0}",
              ambAll == 3 && ambCrit == 3 && ric >= 3 && sideA.Dealt > 0 && sideB.Dealt > 0);
        DetachAll();

        // ② 세 번째 칼 · 칼자국 · 연격 표식 — 8초 평타
        Set(k, "aug_katana_third:1,aug_blade_scar:1,aug_combo_mark:1");
        int ksw = await AugSwingAsync(p, 8f, ct);
        int third = AugLog("] 세 번째 칼"), scar = AugLog("] 칼자국"), mark = AugLog("] 연격 표식");
        sb.AppendLine($"   평타 진단 — {s_augSwingDiag}");
        Check(sb, $"② 카타나 — 휘두름 {ksw} · X자 베기 {third}(≈ 휘두름/3) · 칼자국 적중 {scar} · 연격 표식 {mark}",
              ksw >= 8 && Mathf.Abs(third - ksw / 3f) <= 2.5f && scar >= ksw / 2 && mark >= 2);

        // ③ 주문의 날(E 뒤 평타 3번) · 회피 베기(지나간 길) · 물 흐르듯(회피 뒤 첫 적중 = 회피 1칸)
        Set(k, "aug_spell_blade:1,aug_dodge_riposte:1,aug_katana_flow:1");
        s_home = p.transform.position;
        var t3 = await TrackAsync(p.transform.position + Flat(p.transform.forward) * 1.5f, ct);
        if (t3 == null) { sb.AppendLine("슬라임 부르기 실패"); return; }
        s_formLog.Clear();
        p.CooldownTracker.ResetCooldown(SkillType.E);
        p.InputBuffer.Push(Command.ESkill);
        await Wait(1.6f, ct);
        for (float t = 0f; t < 3f; t += 0.25f) { RefillAll(); Steer(p, t3.M.transform.position); p.InputBuffer.Push(Command.Light); await Wait(0.25f, ct); }
        p.InputBuffer.Clear();
        await Wait(0.8f, ct);
        int spell = AugLog("] 주문의 날");
        DetachAll();
        // 회피 베기 · 물 흐르듯 — 앞 2 m 새 표적을 향해 회피(지나간다) → 회피가 끝나면 곧 평타
        s_home = p.transform.position;
        var t3b = await TrackAsync(p.transform.position + Flat(p.transform.forward) * 2f, ct);
        if (t3b == null) { sb.AppendLine("슬라임 부르기 실패"); return; }
        Vector3 toT = Flat(t3b.M.transform.position - p.transform.position);
        AimAt(t3b.M.transform.position);
        p.RequestFacing(Quaternion.LookRotation(toT));
        await Wait(0.35f, ct);
        float rollEndAt = -1f, firstHitAt = -1f;
        Action onRollEnd = () => { if (rollEndAt < 0f) rollEndAt = Time.time; };
        Action<HitInfo> onHit3 = h => { if (h.Attacker == p.gameObject && h.ActionType == WeaponActionType.GroundLight && rollEndAt > 0f && firstHitAt < 0f) firstHitAt = Time.time; };
        p.OnDodgeEnd += onRollEnd;
        HitFeedbackService.OnHit += onHit3;
        Vector3 dodgeFrom = p.transform.position, dodgeVec = Vector3.zero;
        try
        {
            p.InputBuffer.Push(Command.Dodge);
            float w3 = Time.time;
            while (rollEndAt < 0f && Time.time - w3 < 1.5f) await UniTask.Yield(PlayerLoopTiming.Update, ct);
            dodgeVec = Flat(p.transform.position - dodgeFrom) * Vector3.Distance(Flat(p.transform.position), Flat(dodgeFrom));
            s_home = p.transform.position;   // 회피한 자리 — 되돌려 끌지 않는다
            Steer(p, t3b.M.transform.position);
            p.InputBuffer.Push(Command.Light);
            await Wait(1.0f, ct);
        }
        finally { p.OnDodgeEnd -= onRollEnd; HitFeedbackService.OnHit -= onHit3; }
        p.InputBuffer.Clear();
        int cut = AugLog("] 회피 베기"), flow = AugLog("] 물 흐르듯");
        string cutLine = s_formLog.Find(l => l.Contains("회피 베기")) ?? "-";
        sb.AppendLine($"   회피 진단 — 회피 {dodgeVec.magnitude:0.0} m · 표적 쪽과 {Vector3.Angle(dodgeVec, toT):0}° · 회피 끝 → 첫 평타 적중 {(firstHitAt > 0f ? (firstHitAt - rollEndAt).ToString("0.00") + "초" : "없음")}");
        Check(sb, $"③ 주문의 날 {spell}/3 · 회피 베기 {cut}({cutLine}) · 물 흐르듯 {flow}", spell == 3 && cut >= 1 && !cutLine.Contains("맞힘 0") && flow == 1);
        DetachAll();

        // ④ 처형인 → 처치 → 사냥꾼의 숨(둘레 사냥감) · 학살의 연쇄(칼바람이 체력 1 둘을 쓰러뜨려 다시 일어난다)
        Set(k, "aug_executioner:1,aug_hunter_breath:1,aug_slaughter_chain:1");
        s_home = p.transform.position;
        Vector3 f4 = Flat(p.transform.forward), r4 = Vector3.Cross(Vector3.up, f4), c4 = p.transform.position + f4 * 1.5f;
        var a4 = await TrackAsync(c4, ct);
        var b4 = await TrackAsync(c4 + r4 * 1.5f, ct);
        var d4 = await TrackAsync(c4 - r4 * 1.5f, ct);
        if (a4 == null || b4 == null || d4 == null) { sb.AppendLine("슬라임 부르기 실패"); return; }
        SetHp(a4.M, Mathf.Max(1, Mathf.RoundToInt(a4.M.EffectiveMaxHp * 0.1f)));   // 처형 문턱(15%) 아래
        SetHp(b4.M, 1);
        SetHp(d4.M, 1);
        s_formLog.Clear();
        // 처형은 그 적중의 결과를 본다 — 한 타에 죽으면 처형할 것이 없으니 효과를 직접 부른다(피해 1 적중)
        var mctx = typeof(ItemEffectManager).GetField("_ctx", Inst)?.GetValue(run.EffectManager) as ItemEffectContext;
        foreach (var e in run.EffectManager.ActiveEffects)
            if (e is ExecutionerAugment ex)
                ex.OnPostDealDamage(mctx, new DamageReport { Target = a4.M.gameObject, Attacker = p.gameObject, DamageDealt = 1f, ActionType = WeaponActionType.GroundLight });
        await Wait(1.8f, ct);
        int exec = AugLog("] 처형인"), hunt = AugLog("] 사냥꾼의 숨"), chain = AugLog("] 학살의 연쇄");
        bool allDead = (a4.M == null || a4.M.IsDead) && (b4.M == null || b4.M.IsDead) && (d4.M == null || d4.M.IsDead);
        Check(sb, $"④ 처치 규칙 — 처형 {exec} · 사냥감 {hunt} · 학살의 연쇄 {chain} · 셋 다 쓰러짐 {allDead}", exec == 1 && hunt >= 1 && chain >= 2 && allDead);
        DetachAll();

        // ⑤ 마지막 의지 — 40% 아래로 떨어지면 1.5초 무적 · 둘레 경직, 같은 방에선 한 번
        LastWill:
        Set(k, "aug_last_will:2");
        s_home = p.transform.position;
        var t5 = await TrackAsync(p.transform.position + Flat(p.transform.forward) * 1.5f, ct);
        if (t5 == null) { sb.AppendLine("슬라임 부르기 실패"); return; }
        s_formLog.Clear();
        int maxHp = p.RuntimeStats.MaxHp;
        var dbgInv = typeof(PlayerController).GetField("debugInvincible", Inst);
        dbgInv?.SetValue(p, false);   // 실측 시작 때 켠 무적 — 이 칸만 끈다
        typeof(PlayerController).GetField("_invincibleEnd", Inst)?.SetValue(p, 0f);
        await Wait(0.3f, ct);
        p.RuntimeStats.SetHp(Mathf.RoundToInt(maxHp * 0.45f));
        p.TakeDamage(Mathf.RoundToInt(maxHp * 0.2f), t5.M.gameObject, true);   // 방어로 줄어도 40% 아래
        await UniTask.DelayFrame(2, cancellationToken: ct);
        bool inv = p.IsInvincible, stunned = t5.M != null && t5.M.Status.HasCc("stun");
        await Wait(1.8f, ct);
        p.TakeDamage(1, t5.M.gameObject, true);
        await Wait(0.3f, ct);
        int lw = AugLog("] 마지막 의지");
        Check(sb, $"⑤ 마지막 의지 — 발동 {lw} · 무적 {inv} · 둘레 경직 {stunned} · 체력 {p.RuntimeStats.Hp}/{maxHp}", lw == 1 && inv && stunned);
        dbgInv?.SetValue(p, true);
        p.RuntimeStats.SetHp(maxHp);
        DetachAll();
        if (lastWillOnly) return;

        // ⑥ 철벽 자세(대검) — 평타 동작 중 자세(경직 면역 · 방어 +25%) · 쉬면 내려놓는다
        if (!await EquipAsync(p, Great, ct)) { sb.AppendLine("대검 장착 실패"); return; }
        var g = p.WeaponManager.Weapon0Data;
        g.enhanceLevel = 0;
        Set(g, "aug_great_bulwark:3");
        await Wait(0.5f, ct);
        var stOwner = typeof(PlayerController).GetField("_stanceOwner", Inst);
        var stDef   = typeof(PlayerController).GetField("_stanceDefenseAdd", Inst);
        string ownerIn = "-";
        float defIn = -1f;
        await AugSwingAsync(p, 4f, ct, () =>
        {
            if (defIn >= 0f || !p.IsInAttackAction || p.IsInSkillAction) return;
            var o = stOwner?.GetValue(p);
            if (o == null) return;
            ownerIn = o.GetType().Name;
            defIn = (float)stDef.GetValue(p);
        });
        var ownerAfter = stOwner?.GetValue(p);
        Check(sb, $"⑥ 철벽 자세 — 동작 중 자세 {ownerIn} · 방어 +{defIn:0.00} · 쉴 때 {(ownerAfter == null ? "없음" : ownerAfter.GetType().Name)}",
              ownerIn == "BulwarkAugment" && Mathf.Abs(defIn - 0.25f) < 0.001f && ownerAfter == null);
        Set(g, "");

        // ⑦ 표식 퍼뜨리기(카타나 · 마지막 타) · 안개 칼날(무형검 E)
        if (!await EquipAsync(p, Katana, ct)) { sb.AppendLine("카타나 장착 실패"); return; }
        var k7 = p.WeaponManager.Weapon0Data;
        k7.enhanceLevel = 0;
        Set(k7, "aug_mark_spread:2");
        s_home = p.transform.position;
        Vector3 f7 = Flat(p.transform.forward), r7 = Vector3.Cross(Vector3.up, f7), c7 = p.transform.position + f7 * 1.5f;
        var m7 = await TrackAsync(c7, ct);
        var n7a = await TrackAsync(c7 + f7 * 2f + r7 * 1f, ct);   // 표적 뒤 — 평타가 닿지 않는 자리(3 m 안)
        var n7b = await TrackAsync(c7 + f7 * 2f - r7 * 1f, ct);
        if (m7 == null || n7a == null || n7b == null) { sb.AppendLine("슬라임 부르기 실패"); return; }
        int sunA0 = RelicMarkStatus.Of(n7a.M.gameObject, false)?.Sunmark ?? 0, sunB0 = RelicMarkStatus.Of(n7b.M.gameObject, false)?.Sunmark ?? 0;
        s_formLog.Clear();
        for (float t = 0f; t < 4f; t += 0.25f)
        {
            RefillAll();
            RelicMarkStatus.Of(m7.M.gameObject, true)?.SetSunmark(1, RelicMarkStatus.DefaultSunmarkCap);   // 표적엔 늘 태양흔 1
            Steer(p, m7.M.transform.position);
            p.InputBuffer.Push(Command.Light);
            await Wait(0.25f, ct);
        }
        p.InputBuffer.Clear();
        await Wait(0.6f, ct);
        int sunA = RelicMarkStatus.Of(n7a.M.gameObject, false)?.Sunmark ?? 0, sunB = RelicMarkStatus.Of(n7b.M.gameObject, false)?.Sunmark ?? 0;
        int spread = AugLog("] 표식 퍼뜨리기");
        DetachAll();
        Set(k7, "");

        if (!await EquipAsync(p, Nameless, ct)) { sb.AppendLine("무형검 장착 실패"); return; }
        var n7 = p.WeaponManager.Weapon0Data;
        n7.enhanceLevel = 0;
        Set(n7, "aug_nameless_mist:1");
        s_home = p.transform.position;
        var t7 = await TrackAsync(p.transform.position + Flat(p.transform.forward) * 4f, ct);
        int mist = 0;
        if (t7 != null)
        {
            s_formLog.Clear();
            AimAt(t7.M.transform.position);
            p.RequestFacing(Quaternion.LookRotation(Flat(t7.M.transform.position - p.transform.position)));
            await Wait(0.3f, ct);
            p.CooldownTracker.ResetCooldown(SkillType.E);
            p.InputBuffer.Push(Command.ESkill);
            await Wait(2.4f, ct);
            mist = AugLog("] 안개 칼날");
        }
        DetachAll();
        Set(n7, "");
        Check(sb, $"⑦ 표식 퍼뜨리기 {spread}회(뒤 두 적 태양흔 {sunA0} → {sunA} · {sunB0} → {sunB}) · 안개 칼날 적중 {mist}(0.5초마다)",
              spread >= 1 && sunA > sunA0 && sunB > sunB0 && mist >= 2);
    }

    private static async UniTask Augment2Async(PlayerController p, StringBuilder sb, CancellationToken ct, bool dashOnly = false)
    {
        sb.AppendLine("근접 강화 증강 2차 — 진화 변환 · 제시 거름 · 카타나 평타 · 대검 평타 · 대검 스킬 · 카타나 돌진 · 무형검");
        await Wait(3f, ct);
        var run = GameRunBootstrapper.Instance.Run;
        foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
            if (m != null && !m.IsDead) m.gameObject.SetActive(false);

        if (dashOnly) goto Dash;   // ⑥ · ⑦만(바뀐 칸만 다시)

        // ① 진화 변환 — 무형검 전용은 남는다(환영 몫 · 10-18) · 비어 있는 칼집 II = 새 전용 2개(I)
        {
            var nl = WeaponData.FromSO(AssetDatabase.LoadAssetAtPath<WeaponSO>(Nameless));
            var ka = WeaponData.FromSO(AssetDatabase.LoadAssetAtPath<WeaponSO>(Katana));
            var gs = WeaponData.FromSO(AssetDatabase.LoadAssetAtPath<WeaponSO>(Great));
            nl.augments = "aug_nameless_mist:2,aug_nameless_sheath:2,aug_ricochet:1";
            ka.augments = MeleeAugmentCatalog.ConvertForEvolution(nl, ka);
            gs.augments = MeleeAugmentCatalog.ConvertForEvolution(nl, gs);
            sb.AppendLine($"   무형검 {nl.augments} → 카타나 {ka.augments} · 대검 {gs.augments}");
            int kc = 0, gc = 0;
            foreach (var _ in ka.AugmentList()) kc++;
            foreach (var _ in gs.AugmentList()) gc++;
            // 10-18 ⑧-기획 A: 무형검 전용은 바꾸지 않고 남는다(진화 뒤 환영 몫) · 칼집만 새 전용으로
            bool kOk = kc == 4 && ka.AugmentRank("aug_nameless_mist") == 2 && ka.AugmentRank("aug_katana_gale") == 1 && ka.AugmentRank("aug_ricochet") == 1 && !ka.augments.Contains("sheath");
            bool gOk = gc == 4 && gs.AugmentRank("aug_nameless_mist") == 2 && gs.AugmentRank("aug_great_giant") == 1 && !gs.augments.Contains("sheath");
            Check(sb, "① 진화 변환", kOk && gOk);
        }

        // ② 제시 거름 — 대검 +1 → +24: 묵직한 손(대검은 이미 강타) · 다른 무기 전용 0
        if (!await EquipAsync(p, Great, ct)) { sb.AppendLine("대검 장착 실패"); return; }
        var g = p.WeaponManager.Weapon0Data;
        g.augments = ""; g.highestEnhanceReached = 0; g.augmentSilverStreak = 0; g.augmentAfterEvolve = false;
        MeleeAugmentDraft.ResetForRun();
        int bad = 0, heavyOffered = 0, gsOnly = 0;
        Action<MeleeAugmentOffer> onOffer = o =>
        {
            foreach (var c in o.Cards)
            {
                var d = c.Def;
                if (d == null) continue;
                if (d.Id == MeleeAugmentCatalog.HeavyHand) heavyOffered++;
                if (d.Weapon == MeleeWeaponKind.Greatsword) gsOnly++;
                else if (d.Weapon != MeleeWeaponKind.None) bad++;
            }
            MeleeAugmentDraft.Choose(0);
        };
        MeleeAugmentDraft.Offered += onOffer;
        try { for (int lv = 1; lv <= 24; lv++) { g.enhanceLevel = lv; MeleeAugmentDraft.TryOfferForEnhance(g, lv, false, new System.Random(lv * 17)); } }
        finally { MeleeAugmentDraft.Offered -= onOffer; }
        sb.AppendLine($"   대검 가진 증강: {g.augments}");
        Check(sb, $"② 대검 제시 — 묵직한 손 {heavyOffered} · 다른 무기 전용 {bad} · 대검 전용 카드 {gsOnly}", heavyOffered == 0 && bad == 0 && gsOnly > 0);
        g.augments = ""; g.enhanceLevel = 0; g.highestEnhanceReached = 0;

        // ③ 카타나 평타 — 묵직한 손 III(2번째 휘두름마다 강타) · 검기 · 쌍검기 · 충격파 · 천둥
        if (!await EquipAsync(p, Katana, ct)) { sb.AppendLine("카타나 장착 실패"); return; }
        var k = p.WeaponManager.Weapon0Data;
        k.enhanceLevel = 0;
        k.augments = "aug_heavy_hand:3,aug_sword_wave:1,aug_twin_wave:1,aug_impact:1,aug_thunder:1";
        run.EffectManager.Rebuild();
        await Wait(0.5f, ct);
        s_home = p.transform.position;
        int ksw = await AugSwingAsync(p, 10f, ct);
        int heavy = AugLog("] 묵직한 손"), wave = AugLog("] 검기 —"), twin = AugLog("] 쌍검기"), impact = AugLog("] 충격파"), thunder = AugLog("] 천둥");
        sb.AppendLine($"   휘두름 {ksw} · 강타 예약 {heavy} · 검기 {wave} · 쌍검기 {twin} · 충격파 {impact} · 천둥 {thunder}");
        Check(sb, "③ 카타나 평타 — 강타 ≈ 휘두름/2 · 검기 = 마지막 타 · 쌍검기 = 검기 · 충격파 ≥ 강타 · 천둥 ≈ 강타",
              ksw >= 8 && Mathf.Abs(heavy - ksw / 2f) <= 2f && wave >= 1 && wave <= ksw / 3 + 1 && twin == wave && impact >= heavy - 1 && impact <= ksw && thunder >= heavy - 1 && thunder <= heavy);

        // ④ 대검 평타 — 거인의 걸음(휘두름마다) · 지진(마지막 타 1초 뒤) · 철벽 자세 III(동작 중 −25%)
        if (!await EquipAsync(p, Great, ct)) { sb.AppendLine("대검 장착 실패"); return; }
        var g2 = p.WeaponManager.Weapon0Data;
        g2.enhanceLevel = 0;
        g2.augments = "aug_great_giant:1,aug_great_quake:1,aug_great_bulwark:3";
        run.EffectManager.Rebuild();
        await Wait(0.5f, ct);
        var stOwner = typeof(PlayerController).GetField("_stanceOwner", Inst);
        var stDef   = typeof(PlayerController).GetField("_stanceDefenseAdd", Inst);
        string ownerIn = "-";
        float defIn = -1f;
        int gsw = await AugSwingAsync(p, 8f, ct, () =>
        {
            if (defIn >= 0f || !p.IsInAttackAction || p.IsInSkillAction) return;
            var o = stOwner?.GetValue(p);
            if (o == null) return;
            ownerIn = o.GetType().Name;
            defIn = (float)stDef.GetValue(p);
        });
        var ownerAfter = stOwner?.GetValue(p);
        int giant = AugLog("] 거인의 걸음"), quake = AugLog("] 지진");
        Check(sb, $"④ 대검 평타 — 휘두름 {gsw} · 거인의 걸음 {giant} · 지진 {quake} · 철벽(동작 중 자세 {ownerIn} · 방어 +{defIn:0.00} · 쉴 때 {(ownerAfter == null ? "없음" : ownerAfter.GetType().Name)})",
              gsw >= 3 && Mathf.Abs(giant - gsw) <= 1 && quake >= 1 && quake <= gsw / 3 + 1 && ownerIn == "BulwarkAugment" && Mathf.Abs(defIn - 0.25f) < 0.001f && ownerAfter == null);

        // ⑤ 대검 스킬 — 주변경계 연장 III(+3회) · 최후의 일격 충전 III(0.6 → 0.3초)
        async UniTask<float> SkillTime(SkillType slot, Command cmd)
        {
            p.InputBuffer.Clear();
            await Wait(0.8f, ct);
            p.CooldownTracker.ResetCooldown(slot);
            p.InputBuffer.Push(cmd);
            float w0 = Time.time;
            while (!p.IsInSkillAction && Time.time - w0 < 1.5f) await UniTask.Yield(PlayerLoopTiming.Update, ct);
            float s0 = Time.time;
            while (p.IsInSkillAction && Time.time - s0 < 8f) await UniTask.Yield(PlayerLoopTiming.Update, ct);
            return Time.time - s0;
        }
        g2.augments = ""; run.EffectManager.Rebuild();
        float e0 = await SkillTime(SkillType.E, Command.ESkill), r0 = await SkillTime(SkillType.R, Command.RSkill);
        g2.augments = "aug_great_perimeter:3,aug_great_final:3"; run.EffectManager.Rebuild();
        float e1 = await SkillTime(SkillType.E, Command.ESkill), r1 = await SkillTime(SkillType.R, Command.RSkill);
        var peri = g2.skillE != null ? g2.skillE.behavior : null;
        float interval = peri != null ? (float)(peri.GetType().GetField("castInterval", Inst)?.GetValue(peri) ?? 0f) : 0f;
        Check(sb, $"⑤ 대검 스킬 — 주변경계 {e0:0.00} → {e1:0.00}초(+3회 × {interval:0.00}) · 최후의 일격 {r0:0.00} → {r1:0.00}초",
              interval > 0f && e1 - e0 > interval * 2.5f && r0 - r1 > 0.2f);
        g2.augments = ""; run.EffectManager.Rebuild();

        // ⑥ 카타나 성광베기 — 발도 연속 III(돌진 +65%)
        Dash:
        if (!await EquipAsync(p, Katana, ct)) { sb.AppendLine("카타나 장착 실패"); return; }
        var k2 = p.WeaponManager.Weapon0Data;
        k2.enhanceLevel = 0;
        Vector3 pos = p.transform.position, open = Vector3.forward;
        float best = -1f;
        for (int i = 0; i < 12; i++)
        {
            var d = Quaternion.Euler(0f, i * 30f, 0f) * Vector3.forward;
            float wa = WallAhead(p, pos, d, 15f);
            if (wa < 0f) wa = 15f;   // −1 = 15 m 안에 벽 없음(가장 트임)
            if (wa > best) { best = wa; open = d; }
        }
        async UniTask<float> DashLen()
        {
            var rb = p.Rigid;
            if (rb != null) { rb.linearVelocity = Vector3.zero; rb.position = pos; }
            p.transform.position = pos;
            s_home = pos;
            await Wait(0.5f, ct);
            AimAt(pos + open * 5f);
            p.RequestFacing(Quaternion.LookRotation(open));
            await Wait(0.3f, ct);
            p.CooldownTracker.ResetCooldown(SkillType.E);
            p.InputBuffer.Push(Command.ESkill);
            await Wait(1.4f, ct);
            return Vector3.Dot(p.transform.position - pos, open);
        }
        k2.augments = ""; run.EffectManager.Rebuild();
        float d0 = await DashLen();
        k2.augments = "aug_katana_draw:3"; run.EffectManager.Rebuild();
        float d1 = await DashLen();
        k2.augments = ""; run.EffectManager.Rebuild();
        Check(sb, $"⑥ 카타나 성광베기 — 돌진 {d0:0.00} → {d1:0.00} m(발도 연속 III +65% · 트인 거리 {best:0.0} m)",
              d0 > 1f && (best < 8.8f ? d1 > d0 + 1f : Mathf.Abs(d1 / d0 - 1.65f) < 0.15f));

        // ⑦ 무형검 — 형 없는 연타(마지막 타 0.2초 뒤) · 무형일섬 잔상(지나간 길 0.4초 뒤)
        if (!await EquipAsync(p, Nameless, ct)) { sb.AppendLine("무형검 장착 실패"); return; }
        var n0 = p.WeaponManager.Weapon0Data;
        n0.enhanceLevel = 0;
        n0.augments = "aug_nameless_afterimage:1,aug_nameless_formless:1";
        run.EffectManager.Rebuild();
        await Wait(0.5f, ct);
        int nsw = await AugSwingAsync(p, 6f, ct);
        int formless = AugLog("] 형 없는 연타");
        sb.AppendLine($"   무형검 평타 진단 — {s_augSwingDiag}");
        var tgt = await TrackAsync(p.transform.position + Flat(p.transform.forward) * 4f, ct);
        string afterLine = "-";
        if (tgt != null)
        {
            s_formLog.Clear();
            AimAt(tgt.M.transform.position);
            p.RequestFacing(Quaternion.LookRotation(Flat(tgt.M.transform.position - p.transform.position)));
            await Wait(0.3f, ct);
            p.CooldownTracker.ResetCooldown(SkillType.R);
            p.InputBuffer.Push(Command.RSkill);
            await Wait(2f, ct);
            afterLine = s_formLog.Find(l => l.Contains("무형일섬 잔상")) ?? "-";
        }
        int after = AugLog("] 무형일섬 잔상");
        DetachAll();
        n0.augments = ""; run.EffectManager.Rebuild();
        int dashAt = afterLine.IndexOf("— ", StringComparison.Ordinal), mAt = afterLine.IndexOf(" m ", StringComparison.Ordinal);
        float afterLen = dashAt >= 0 && mAt > dashAt && float.TryParse(afterLine.Substring(dashAt + 2, mAt - dashAt - 2), out var al) ? al : 0f;
        Check(sb, $"⑦ 무형검 — 휘두름 {nsw} · 형 없는 연타 {formless} · 무형일섬 잔상 {after}({afterLine})",
              formless >= 1 && formless <= nsw / 3 + 1 && after == 1 && afterLen >= 3f && afterLine.Contains("맞힘") && !afterLine.Contains("맞힘 0"));
    }

    // ── 57 근접 강화 증강(10-15 사용자 「강화마다 2지선다 랜덤 · 중복 = 강화 · 등급도 랜덤 · 무기마다 전용 · 단계를 잘게」) ──
    private static async UniTask AugmentAsync(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        sb.AppendLine("근접 강화 증강 — 등급 확률 · 뽑기 · 효과 · 강화 표");
        // ① 등급 확률(2만 번)
        var rng = new System.Random(15);
        bool tierOk = true;
        foreach (var (lv, ex) in new[] { (4, (0.70f, 0.25f)), (12, (0.45f, 0.40f)), (22, (0.20f, 0.50f)) })
        {
            int s = 0, g = 0, pr = 0;
            for (int i = 0; i < 20000; i++)
            {
                var t = MeleeAugmentDraft.RollTier(lv, false, 0, rng);
                if (t == AugmentTier.Silver) s++; else if (t == AugmentTier.Gold) g++; else pr++;
            }
            float fs = s / 20000f, fg = g / 20000f;
            bool ok = Mathf.Abs(fs - ex.Item1) < 0.02f && Mathf.Abs(fg - ex.Item2) < 0.02f;
            tierOk &= ok;
            sb.AppendLine($"   +{lv}: 은 {fs:0.00} · 금 {fg:0.00} · 프리즘 {pr / 20000f:0.00} (기대 {ex.Item1:0.00} · {ex.Item2:0.00})");
        }
        int pitySilver = 0, evoSilver = 0;
        for (int i = 0; i < 5000; i++)
        {
            if (MeleeAugmentDraft.RollTier(4, false, 3, rng) == AugmentTier.Silver) pitySilver++;
            if (MeleeAugmentDraft.RollTier(4, true, 0, rng) == AugmentTier.Silver) evoSilver++;
        }
        sb.AppendLine($"   바닥 보장(은 3연속 뒤) 은 {pitySilver}/5000 · 진화 직후 은 {evoSilver}/5000");
        Check(sb, "① 등급 확률 · 바닥 보장 · 진화 직후 금 이상", tierOk && pitySilver == 0 && evoSilver == 0);

        // ② 뽑기 — 카타나를 +1 → +24(창 없음 = 왼쪽 카드 자동)
        await Wait(3f, ct);
        if (!await EquipAsync(p, Katana, ct)) { sb.AppendLine("카타나 장착 실패"); return; }
        var w = p.WeaponManager.Weapon0Data;
        w.augments = ""; w.highestEnhanceReached = 0; w.augmentSilverStreak = 0; w.augmentAfterEvolve = false;
        MeleeAugmentDraft.ResetForRun();
        int offers = 0, upgrades = 0, exclusive = 0, wrongWeapon = 0;
        var tiers = new int[3];
        Action<MeleeAugmentOffer> onOffer = o =>
        {
            offers++; tiers[(int)o.Tier]++;
            foreach (var c in o.Cards)
            {
                var d = c.Def;
                if (c.IsUpgrade) upgrades++;
                if (d != null && d.Weapon != MeleeWeaponKind.None) exclusive++;
                if (d != null && d.Weapon != MeleeWeaponKind.None && d.Weapon != MeleeWeaponKind.Katana) wrongWeapon++;
            }
            MeleeAugmentDraft.Choose(0);
        };
        MeleeAugmentDraft.Offered += onOffer;
        try
        {
            for (int lv = 1; lv <= 24; lv++)
            {
                w.enhanceLevel = lv;
                MeleeAugmentDraft.TryOfferForEnhance(w, lv, false, new System.Random(lv * 31));
            }
            // 하락 뒤 다시 닿으면 안 준다
            int before = offers;
            w.enhanceLevel = 20;
            MeleeAugmentDraft.TryOfferForEnhance(w, 20, false, new System.Random(99));
            sb.AppendLine($"   화면 {offers}(+1 ~ +24) · 등급 은 {tiers[0]} · 금 {tiers[1]} · 프리즘 {tiers[2]} · 강화 카드 {upgrades} · 전용 카드 {exclusive} · 다른 무기 전용 {wrongWeapon} · 하락 뒤 다시 닿음 화면 {offers - before}");
            sb.AppendLine($"   가진 증강: {w.augments}");
            int maxRank = 0;
            foreach (var (_, r) in w.AugmentList()) maxRank = Mathf.Max(maxRank, r);
            Check(sb, $"② 3단계마다 화면 8(+3 · +6 … +24) · 다른 무기 전용 0 · 하락 뒤 0 · 단계 ≤ III(최고 {maxRank})", offers == 24 / MeleeAugmentDraft.LevelStep && wrongWeapon == 0 && offers - before == 0 && maxRank <= 3);
        }
        finally { MeleeAugmentDraft.Offered -= onOffer; }

        // ③ 효과 — 지금 든 카타나에 직접 단다
        var rs = p.RuntimeStats;
        w.augments = "";
        GameRunBootstrapper.Instance.Run.EffectManager.Rebuild();
        await Wait(0.3f, ct);
        float crit0 = rs.CritChanceBonus, heavy0 = ProcCoefficient.HeavyThreshold();
        w.augments = "aug_ricochet:3,aug_heavy_hand:2,aug_ambush:1,aug_katana_phantom:2";
        GameRunBootstrapper.Instance.Run.EffectManager.Rebuild();
        await Wait(0.3f, ct);
        float crit1 = rs.CritChanceBonus, heavy1 = ProcCoefficient.HeavyThreshold();
        float phantom = MeleeAugments.SkillDamageMult(w, SkillType.R), phantomE = MeleeAugments.SkillDamageMult(w, SkillType.E);
        sb.AppendLine($"   치명 {crit0:0.0} → {crit1:0.0}%(규칙형 — 스탯 그대로) · 강타 문턱 {heavy0:0.00} → {heavy1:0.00} · 환영베기 ×{phantom:0.00} · E ×{phantomE:0.00}");
        Check(sb, "③ 효과 — 스탯 그대로(치명 · 문턱) · 환영베기(R) ×1.35 · E는 그대로", Mathf.Abs(crit1 - crit0) < 0.01f && Mathf.Abs(heavy0 - heavy1) < 0.001f
                                                                                    && Mathf.Abs(phantom - 1.35f) < 0.001f && Mathf.Abs(phantomE - 1f) < 0.001f);
        w.augments = "";
        GameRunBootstrapper.Instance.Run.EffectManager.Rebuild();

        // ④ 강화 표
        var t2 = WeaponEnhanceService.Table;
        sb.AppendLine($"   상한 일반 {t2.MaxFor(ItemRarity.Common)} · 전설 {t2.MaxFor(ItemRarity.Legendary)} · 진화 1번 일반 {t2.MaxFor(ItemRarity.Common, 1)} · +12 공격 ×{t2.AttackMult(12, null):0.00} · 스킬 단계 {string.Join("/", t2.MeleeSkillTierMilestones)} · +12→13 성공 {t2.SuccessAt(12):0.00} · +24→25 하락 {t2.DropAt(24)}");
        Check(sb, "④ 강화 표 — 상한 12 · 30 · 24 · +12 공격 ×1.48 · 스킬 6/12", t2.MaxFor(ItemRarity.Common) == 12 && t2.MaxFor(ItemRarity.Legendary) == 30 && t2.MaxFor(ItemRarity.Common, 1) == 24
                                                                         && Mathf.Abs(t2.AttackMult(12, null) - 1.48f) < 0.001f && t2.MeleeSkillTierMilestones.Length == 2 && t2.MeleeSkillTierMilestones[0] == 6);
    }

    // ── 63 무형검 3타 두 번 베기 · 이동 클립(10-16 사용자 「마지막 타가 2타 애니메이션이라 실제 타격도 2타」 · 「방패 팩 → 사무라이 팩 이동」) ──
    private static async UniTask NamelessFinisherAsync(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        sb.AppendLine("무형검 3타 두 번 베기 · 이동 클립");
        await Wait(3f, ct);
        foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
            if (m != null && !m.IsDead) m.gameObject.SetActive(false);
        if (!await EquipAsync(p, Nameless, ct)) { sb.AppendLine("무형검 장착 실패"); return; }
        var n = p.WeaponManager.Weapon0Data;
        n.augments = "";
        GameRunBootstrapper.Instance.Run.EffectManager.Rebuild();
        await Wait(0.5f, ct);

        // ① 이동 클립 — 지금 오버라이드에 방패 팩(SS_) 클립이 남아 있지 않다
        var anim = p.GetComponentInChildren<Animator>();
        var aoc = anim != null ? anim.runtimeAnimatorController as AnimatorOverrideController : null;
        var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>();
        aoc?.GetOverrides(pairs);
        int ss = 0, apose = 0;
        string idle = "-";
        foreach (var kv in pairs)
        {
            if (kv.Key == null || kv.Value == null || !kv.Key.name.StartsWith("GhostSamurai_Common_")) continue;
            if (kv.Value.name.Contains("SS_") || kv.Value.name.Contains("Shield")) ss++;
            else apose++;
            if (kv.Key.name == "GhostSamurai_Common_Idle_Inplace") idle = kv.Value.name;
        }
        Check(sb, $"① 이동 클립 — 방패 팩 {ss} · 그 밖 {apose} · 대기 = {idle}", aoc != null && ss == 0 && apose >= 17);

        // ② 3타 두 번 베기 — 3타(슬래시 2)가 나오면 손을 뗀다 → 회수의 내려 베기(슬래시 3)가 나오고 맞힌다 · 3번
        //    (계속 누르면 연결 지점 0.36에서 1타로 넘어가 내려 베기가 재생되지 않는다 — 보이는 만큼 맞는다)
        int ev2 = 0, ev3 = 0, hitsAfter3 = 0, cycles = 0;
        float last3At = -9f;
        var recv = p.GetComponentInChildren<PlayerAnimationEventReceiver>();
        var diag3 = new StringBuilder();
        HpTrack t2 = null;
        int anyAfter3 = 0;
        Action<int> onStep = s =>
        {
            if (s == 2) ev2++;
            if (s != 3) return;
            ev3++; last3At = Time.time;
            if (t2?.M != null) diag3.Append($" [무적 {t2.M.IsDamageImmuneNow} · 근접 무적 {t2.M.IsMeleeImmuneNow} · 거리 {Vector3.Distance(new Vector3(p.transform.position.x, 0f, p.transform.position.z), new Vector3(t2.M.transform.position.x, 0f, t2.M.transform.position.z)):0.0}·각 {Vector3.Angle(Flat(p.transform.forward), Flat(t2.M.transform.position - p.transform.position)):0}° · 단계 {p.Combo.CurrentComboStep} · 공격 {p.CurrentAttackTypeForEffect}]");
        };
        Action<HitInfo> onHit = h =>
        {
            if (h.Attacker != p.gameObject || Time.time - last3At >= 0.4f) return;
            anyAfter3++;
            if (h.ActionType == WeaponActionType.GroundLight) hitsAfter3++;
        };
        s_home = p.transform.position;
        t2 = await TrackAsync(p.transform.position + Flat(p.transform.forward) * 1.5f, ct);
        if (t2 == null) { sb.AppendLine("슬라임 부르기 실패"); return; }
        if (recv != null) recv.OnEffectStep += onStep;
        HitFeedbackService.OnHit += onHit;
        try
        {
            for (int c = 0; c < 3; c++)
            {
                int before = ev2;
                float t0 = Time.time;
                while (p.Combo.CurrentComboStep < 2 && Time.time - t0 < 4f) { RefillAll(); Steer(p, t2.M.transform.position); p.InputBuffer.Push(Command.Light); await Wait(0.2f, ct); }
                p.InputBuffer.Clear();   // 3타가 시작됐다 — 더 누르면 연결 지점(0.36)에서 1타로 넘어간다
                float t1 = Time.time;
                while (ev2 == before && Time.time - t1 < 2f) await UniTask.Yield(PlayerLoopTiming.Update, ct);
                if (ev2 > before) cycles++;
                float dealt0 = t2.Dealt;
                int ev3Before = ev3;
                bool probed = false;
                for (float w = 0f; w < 2.2f; w += 0.1f)
                {
                    RefillAll();
                    if (!probed && ev3 > ev3Before)
                    {
                        probed = true;
                        await UniTask.DelayFrame(3, cancellationToken: ct);
                        diag3.Append(DescribeLiveColliders(p, t2.M));
                    }
                    await Wait(0.1f, ct);
                }
                diag3.Append($" <손 뗀 뒤 표적 피해 {t2.Dealt - dealt0:0}>");
            }
        }
        finally { if (recv != null) recv.OnEffectStep -= onStep; HitFeedbackService.OnHit -= onHit; }
        DetachAll();
        sb.AppendLine($"   둘째 베기 진단 —{diag3} · 아무 종류 적중 {anyAfter3}");

        // ③ 평타 연결 — 무기 3종 6초 · 0.05초마다 누름 · 초당 휘두름 · 휘두름당 적중
        var rate = new StringBuilder();
        foreach (var (path, label) in new[] { (Nameless, "무형검"), (Katana, "카타나"), (Great, "대검") })
        {
            if (!await EquipAsync(p, path, ct)) { rate.Append($" {label} 장착 실패"); continue; }
            p.WeaponManager.Weapon0Data.augments = "";
            GameRunBootstrapper.Instance.Run.EffectManager.Rebuild();
            await Wait(0.5f, ct);
            s_home = p.transform.position;
            var tr = await TrackAsync(p.transform.position + Flat(p.transform.forward) * 1.5f, ct);
            if (tr == null) { rate.Append($" {label} 슬라임 실패"); continue; }
            int slashes = 0, hitsR = 0;
            var recvR = p.GetComponentInChildren<PlayerAnimationEventReceiver>();
            Action<int> onS = _ => slashes++;
            Action<HitInfo> onH = h => { if (h.Attacker == p.gameObject && h.ActionType == WeaponActionType.GroundLight) hitsR++; };
            if (recvR != null) recvR.OnEffectStep += onS;
            HitFeedbackService.OnHit += onH;
            float r0 = Time.time;
            try
            {
                while (Time.time - r0 < 6f) { RefillAll(); Steer(p, tr.M.transform.position); p.InputBuffer.Push(Command.Light); await Wait(0.05f, ct); }
            }
            finally { if (recvR != null) recvR.OnEffectStep -= onS; HitFeedbackService.OnHit -= onH; }
            p.InputBuffer.Clear();
            float secs = Mathf.Max(0.01f, Time.time - r0);
            rate.Append($" {label} {slashes / secs:0.0}회/초 · 적중 {hitsR}/{slashes} ·");
            await Wait(0.8f, ct);
            DetachAll();
        }
        sb.AppendLine($"   ③ 초당 휘두름 —{rate}");
        Check(sb, $"② 3타 손 뗌 {cycles}번 — 첫 베기 이벤트 {ev2} · 둘째 베기(내려 베기) 이벤트 {ev3} · 둘째 베기 적중 {hitsAfter3}", cycles == 3 && ev3 >= 3 && hitsAfter3 >= 3);
    }

    /// <summary>63 진단 — 플레이어가 낸 살아 있는 판정마다 위치 · 회전 · 크기 · 공격 종류, 같은 상자로 직접 겹친 것에 표적이 있나.</summary>
    private static string DescribeLiveColliders(PlayerController p, MonsterBase target)
    {
        var sbx = new StringBuilder();
        foreach (var ci in UnityEngine.Object.FindObjectsByType<ColliderInstance>(FindObjectsSortMode.None))
        {
            if (ci == null || ci.owner != p.gameObject || !ci.gameObject.activeInHierarchy) continue;
            var box = ci.GetComponent<BoxCollider>();
            string inside = "-";
            if (box != null)
            {
                Vector3 s = box.transform.lossyScale;
                Vector3 half = new Vector3(box.size.x * 0.5f * Mathf.Abs(s.x), box.size.y * 0.5f * Mathf.Abs(s.y), box.size.z * 0.5f * Mathf.Abs(s.z));
                var hits = Physics.OverlapBox(box.transform.TransformPoint(box.center), half, box.transform.rotation, MonsterBase.HitLayerMask, QueryTriggerInteraction.Collide);
                bool hasT = false;
                foreach (var h in hits) if (target != null && (h.gameObject == target.gameObject || h.transform.IsChildOf(target.transform))) hasT = true;
                inside = $"겹침 {hits.Length} · 표적 {hasT}";
            }
            Vector3 lp = p.transform.InverseTransformPoint(ci.transform.position);
            sbx.Append($" {{{ci.name} 앞 {lp.z:0.0} 위 {lp.y:0.0} 옆 {lp.x:0.0} · 회전 {ci.transform.eulerAngles.z:0} · 크기 {ci.transform.lossyScale.x:0.00} · 공격 {ci.actionType} · id {ci.attackId} · {inside}}}");
        }
        return sbx.Length > 0 ? sbx.ToString() : " {살아 있는 판정 없음}";
    }

    // ── 64 유물 Q 대 평타(10-16) ─────────────────────────────────
    // 사용자 10-16 「가웨인 Q나 랜슬롯 Q가 기본 공격에 비해 크게 DPS에서 이점이 없어 보인다」.
    // 단일 표적 · 같은 상태(가웨인 정오 / 랜슬롯 광란 · 광기 가득)에서 ① 평타 6초 초당 피해 ② Q 2번 — 누름부터 다시 움직일 수 있을 때까지(묶인 시간)
    // 받은 피해 · 가웨인은 화상 · 작열 지대가 끝날 때까지(6.5초 더, 입력 없이) 받은 피해까지. 배수 = Q 피해 ÷ (평타 초당 × 묶인 시간).
    // 랜슬롯은 막타(2.4초) 뒤 공격을 눌러 본다 — 끊을 수 있으면 묶인 시간이 줄어든다.
    private const string RelicQOverridePath = "Temp/relicq_override.txt";   // 줄마다 「유물 칸 값」(예: gawain 11 9)
    private static readonly List<float> s_basicHitTimes = new();   // 평타 적중 시각(64 휘두름 진단)

    private static async UniTask RelicQAsync(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        var g = p.RelicBehavior as GawainZenithRelic;
        var l = p.RelicBehavior as LancelotMadnessRelic;
        if (g == null && l == null) { sb.AppendLine($"유물이 가웨인 · 랜슬롯이 아니다({p.RelicBehavior?.GetType().Name ?? "없음"})"); return; }
        var restore = new List<(RelicStatEntry e, float v)>();
        var run = GameRunBootstrapper.Instance.Run;
        var placed = typeof(RunItemInventory).GetField("_placedItems", Inst)?.GetValue(run.ItemInventory) as List<RuntimeItemData>;
        var savedPlaced = placed != null ? new List<RuntimeItemData>(placed) : null;
        int basicHits = 0, qHits = 0;   // 발동 계수 합 = 적중 수 × 계수(평타 카타나 0.7 · 대검 1.6 / Q는 공격 종류별)
        Action<HitInfo> onHit = h =>
        {
            if (h.Attacker != p.gameObject) return;
            if (h.ActionType == WeaponActionType.GroundLight) { basicHits++; s_basicHitTimes.Add(Time.realtimeSinceStartup); }
            else if (h.ActionType == WeaponActionType.QSkill) qHits++;
        };
        HitFeedbackService.OnHit += onHit;
        // 진단 — 연타가 표적을 못 찾은 순간(「적중 0」 로그)의 거리 · 각 · 높이 · 판정 콜라이더
        MonsterBase diagTarget = null;
        var missDiag = new StringBuilder();
        int missLogged = 0;
        Application.LogCallback onMissLog = (msg, st, type) =>
        {
            if (diagTarget == null || missLogged >= 4 || !msg.StartsWith("[랜슬롯Q]") || !msg.Contains("적중 0")) return;
            missLogged++;
            Vector3 to = diagTarget.transform.position - p.transform.position;
            float up = to.y;
            to.y = 0f;
            bool colOn = false;
            foreach (var col in diagTarget.GetComponentsInChildren<Collider>())
                if (col.enabled && ((1 << col.gameObject.layer) & (int)MonsterBase.HitLayerMask) != 0) colOn = true;
            int cut = msg.IndexOf('|');
            missDiag.Append($" [{(cut > 0 ? msg.Substring(0, cut).Trim() : msg)} 거리 {to.magnitude:0.0} · 각 {Vector3.Angle(Flat(p.AimForward), to):0} · 높이 {up:0.0}" +
                            $" · 판정 콜라이더 {colOn} · 플레이어 집에서 {Vector3.Distance(p.transform.position, s_home):0.0}]");
        };
        Application.logMessageReceived += onMissLog;
        try
        {
            if (s_job.EndsWith("2")) ApplyRelicOverride(sb, restore);
            sb.AppendLine($"유물 {(g != null ? "가웨인 낙일" : "랜슬롯 심판의 일격")} — 단일 표적 · {(g != null ? "정오" : "광란 · 광기 가득")} · 평타 6초 vs Q 2번(실시간) · 깨끗한 칸 = 형 끔 · 강화 0 · 룬 0");
            if (g != null)
            {
                typeof(ZenithGauge).GetField("_chargeTime", Inst)?.SetValue(g.Gauge, 0.3f);
                typeof(ZenithGauge).GetField("_noonTime", Inst)?.SetValue(g.Gauge, 999f);
                typeof(ZenithGauge).GetField("_cooldownTime", Inst)?.SetValue(g.Gauge, 0.3f);
                for (float w = 0f; w < 6f && !g.Gauge.IsNoon; w += 0.1f) { KeepRelicQState(g, l); await Wait(0.1f, ct); }
                if (!g.Gauge.IsNoon) { sb.AppendLine("정오가 안 열렸다"); return; }
            }
            foreach (var (path, label, build) in new[] { (Katana, "카타나", false), (Great, "대검", false), (Katana, "카타나 + 룬 빌드(시뮬 P1 Ch2 중앙)", true) })
            {
                Managers.UI?.CloseAllPopupUI();   // 런 시작 팝업(유물 기억 고르기 등)이 입력을 막는다(예산 6 · 7회차)
                Time.timeScale = 1f;
                if (build)
                {
                    if (placed == null || !await ApplyRelicQBuildAsync(p, run, placed, sb, ct)) { sb.AppendLine($"  {label} 빌드 적용 실패"); continue; }
                }
                else
                {
                    if (!await EquipAsync(p, path, ct)) { sb.AppendLine($"  {label} 장착 실패"); continue; }
                    var wq = p.WeaponManager.Weapon0Data;
                    wq.augments = "";
                    wq.formTier = 0;   // 형 끔 — 평타만 잰다
                    WeaponFormRuntime.Of(p).Rebuild();
                    run.EffectManager.Rebuild();
                }
                KeepRelicQState(g, l);
                await Wait(0.5f, ct);
                s_home = p.transform.position;
                var tr = await TrackAsync(p.transform.position + Flat(p.transform.forward) * 1.8f, ct);
                if (tr == null) { sb.AppendLine($"  {label} 표적 실패"); continue; }
                diagTarget = tr.M;
                missDiag.Clear();
                missLogged = 0;
                await Wait(0.3f, ct);

                // ① 평타 6초 — 0.05초마다 누름
                RefillAll();
                tr.Dealt = 0f;
                basicHits = 0;
                int swings = 0;
                var recvB = p.GetComponentInChildren<PlayerAnimationEventReceiver>();
                var swingLog = new List<(float t, int step, float ang, float dist, float off)>();
                s_basicHitTimes.Clear();
                Action<int> onSwing = _ =>
                {
                    swings++;
                    Vector3 to = tr.M.transform.position - p.transform.position;
                    to.y = 0f;
                    swingLog.Add((Time.realtimeSinceStartup, p.Combo != null ? p.Combo.CurrentComboStep : -1,
                                  Vector3.Angle(Flat(p.AimForward), to), to.magnitude, Vector3.Distance(p.transform.position, s_home)));
                };
                if (recvB != null) recvB.OnEffectStep += onSwing;
                float r0 = Time.realtimeSinceStartup;
                try
                {
                    while (Time.realtimeSinceStartup - r0 < 6f)
                    {
                        RefillAll(); KeepRelicQState(g, l); Steer(p, tr.M.transform.position);
                        p.InputBuffer.Push(Command.Light);
                        await Wait(0.05f, ct);
                    }
                }
                finally { if (recvB != null) recvB.OnEffectStep -= onSwing; }
                p.InputBuffer.Clear();
                float basicSecs = Time.realtimeSinceStartup - r0, basicDealt = tr.Dealt, dps = basicDealt / Mathf.Max(0.01f, basicSecs);
                float basicHitRate = basicHits / Mathf.Max(0.01f, basicSecs);
                // 평타가 남긴 화상 등이 Q 몫에 섞이지 않게 쉰다(화상 6초)
                for (float w = 0f; w < 7f; w += 0.1f) { RefillAll(); KeepRelicQState(g, l); Steer(p, tr.M.transform.position); await Wait(0.1f, ct); }

                // ② Q 2번
                float qSum = 0f, lockSum = 0f;
                int qOk = 0, qHitSum = 0;
                var note = new StringBuilder();
                for (int k = 0; k < 2; k++)
                {
                    KeepRelicQState(g, l);
                    p.CooldownTracker.ResetCooldown(SkillType.Q);
                    if (g != null) typeof(GawainZenithRelic).GetField("_skillUsedThisNoon", Inst)?.SetValue(g, false);
                    if (l != null && l.JudgmentCharges <= 0) l.GrantJudgmentCharge(1);
                    RefillAll(); Steer(p, tr.M.transform.position);
                    await Wait(0.3f, ct);
                    tr.Dealt = 0f;
                    qHits = 0;
                    float q0 = Time.realtimeSinceStartup;
                    p.InputBuffer.Clear();
                    p.InputBuffer.Push(Command.QSkill);
                    bool began = false;
                    while (Time.realtimeSinceStartup - q0 < 1f)
                    {
                        if (p.IsInSkillAction) { began = true; break; }
                        Steer(p, tr.M.transform.position);
                        await UniTask.Yield(PlayerLoopTiming.Update, ct);
                    }
                    if (!began) { note.Append($" {k + 1}회 시전 안 됨"); continue; }
                    float lastPush = 0f;
                    while (p.IsInSkillAction && Time.realtimeSinceStartup - q0 < 8f)
                    {
                        RefillAll(); KeepRelicQState(g, l); Steer(p, tr.M.transform.position);
                        float el = Time.realtimeSinceStartup - q0;
                        if (l != null && el > 2.6f && el - lastPush >= 0.05f) { lastPush = el; p.InputBuffer.Push(Command.Light); }
                        await UniTask.Yield(PlayerLoopTiming.Update, ct);
                    }
                    float lockSecs = Time.realtimeSinceStartup - q0, dealt = tr.Dealt;
                    p.InputBuffer.Clear();
                    float direct = dealt;
                    if (g != null)
                    {
                        for (float w = 0f; w < 6.5f; w += 0.1f) { RefillAll(); KeepRelicQState(g, l); await Wait(0.1f, ct); }
                        dealt = tr.Dealt;
                    }
                    qSum += dealt; lockSum += lockSecs; qOk++; qHitSum += qHits;
                    note.Append($" {k + 1}회 {dealt:0}(묶인 동안 {direct:0} + 뒤 화상 · 지대 {dealt - direct:0}) / {lockSecs:0.00}초 · Q 적중 {qHits}");
                    await Wait(1f, ct);
                }
                float lockAvg = lockSum / Mathf.Max(1, qOk), qAvg = qSum / Mathf.Max(1, qOk), same = dps * lockAvg;
                sb.AppendLine($"  {label}: 공격력 {p.RuntimeStats.AttackPower} · 평타 {dps:0}/초({basicDealt:0} / {basicSecs:0.0}초 · 적중 {basicHitRate:0.0}/초 · 휘두름 {swings} 대 적중 {basicHits}) · Q 평균 {qAvg:0}" +
                              $" · 묶인 시간 {lockAvg:0.00}초 → 같은 시간 평타 {same:0} · 배수 ×{(same > 0f ? qAvg / same : 0f):0.00}" +
                              $" · 적중 수 Q {qHitSum / (float)Mathf.Max(1, qOk):0.0} 대 같은 시간 평타 {basicHitRate * lockAvg:0.0} ·{note}");
                if (missDiag.Length > 0) sb.AppendLine($"     연타 빗나감 진단 —{missDiag}");
                {
                    var miss = new StringBuilder();
                    int missN = 0;
                    float hitAng = 0f; int hitN = 0;
                    foreach (var s in swingLog)
                    {
                        bool hit = s_basicHitTimes.Exists(t => t >= s.t - 0.05f && t <= s.t + 0.25f);
                        if (hit) { hitAng += s.ang; hitN++; continue; }
                        missN++;
                        if (missN <= 5) miss.Append($" [단계 {s.step} · 각 {s.ang:0} · 거리 {s.dist:0.0} · 집에서 {s.off:0.0}]");
                    }
                    sb.AppendLine($"     평타 휘두름 진단 — 맞은 휘두름 {hitN}(평균 각 {(hitN > 0 ? hitAng / hitN : 0f):0}) · 빗나간 휘두름 {missN}{miss}");
                }
                // ③ 같은 무기 E · R 2번씩(10-23 W3) — 누름부터 다시 움직일 수 있을 때까지 + 늦게 오는 여진 1.5초
                if (!build)
                {
                    var er = new StringBuilder();
                    foreach (var (slot, cmd, name) in new[] { (SkillType.E, Command.ESkill, "E"), (SkillType.R, Command.RSkill, "R") })
                    {
                        float sum = 0f, lockS = 0f; int ok = 0;
                        for (int k = 0; k < 2; k++)
                        {
                            KeepRelicQState(g, l);
                            GoHomeAfterSkill(p);
                            p.CooldownTracker.ResetCooldown(slot);
                            RefillAll(); Steer(p, tr.M.transform.position);
                            await Wait(0.4f, ct);
                            tr.Dealt = 0f;
                            float s0 = Time.realtimeSinceStartup;
                            p.InputBuffer.Clear();
                            p.InputBuffer.Push(cmd);
                            bool began = false;
                            while (Time.realtimeSinceStartup - s0 < 1f)
                            {
                                if (p.IsInSkillAction) { began = true; break; }
                                Steer(p, tr.M.transform.position);
                                await UniTask.Yield(PlayerLoopTiming.Update, ct);
                            }
                            if (!began) { er.Append($" {name}{k + 1} 시전 안 됨"); continue; }
                            while (p.IsInSkillAction && Time.realtimeSinceStartup - s0 < 6f)
                            {
                                RefillAll(); KeepRelicQState(g, l); Steer(p, tr.M.transform.position);
                                await UniTask.Yield(PlayerLoopTiming.Update, ct);
                            }
                            float ls = Time.realtimeSinceStartup - s0;
                            for (float w = 0f; w < 1.5f; w += 0.1f) { RefillAll(); KeepRelicQState(g, l); await Wait(0.1f, ct); }
                            sum += tr.Dealt; lockS += ls; ok++;
                            await Wait(0.6f, ct);
                        }
                        float avg = sum / Mathf.Max(1, ok), lk = lockS / Mathf.Max(1, ok);
                        er.Append($" · {name} 평균 {avg:0} / 묶인 {lk:0.00}초(같은 시간 평타 {dps * lk:0} · ×{(dps * lk > 0f ? avg / (dps * lk) : 0f):0.00})");
                        if (name == "R") er.Append($" · Q ÷ R = {(avg > 0f ? qAvg / avg : 0f):0.00}");
                    }
                    sb.AppendLine($"     같은 무기 스킬(10-23 W3){er}");
                }
                DetachAll();
                await Wait(0.5f, ct);
            }
        }
        finally
        {
            HitFeedbackService.OnHit -= onHit;
            Application.logMessageReceived -= onMissLog;
            foreach (var (e, v) in restore) e.value = v;
            if (placed != null && savedPlaced != null) { placed.Clear(); placed.AddRange(savedPlaced); }
            MerlinRuneBridge.Instance?.OnZoneCellsUpdated(new Dictionary<string, int>());
            run.CovenantHandler?.Cleanup();
            run.EffectManager?.Rebuild();
        }
    }

    /// <summary>스킬이 옮긴 자리(대검 R 도약 · 카타나 E 돌진)에서 다음 스킬 전에 제자리로 — 표적과 거리를 같게(10-23 W3).</summary>
    private static void GoHomeAfterSkill(PlayerController p)
    {
        if (p.TryGetComponent<Rigidbody>(out var rb)) { rb.position = s_home; rb.linearVelocity = Vector3.zero; }
        p.transform.position = s_home;
    }

    // 64 룬 빌드 칸 — 레벨디자인 시뮬 대표 빌드(builds_a.json 사본 Temp/relicq_build.json)를 입힌다. 룬은 시뮬 도구(LevelSimBuildProbeEditor.ProjectRune)로 만든다.
    private const string RelicQBuildPath = "Temp/relicq_build.json";
    private const string RelicQBuildLabel = "Ch2 보스 앞";
    [Serializable] private class RqRune   { public string id; public string el; }
    [Serializable] private class RqZone   { public string zone; public int cells; }
    [Serializable] private class RqClause { public string id; public int tier; }
    [Serializable] private class RqBuild
    {
        public string label, weapon, variants, augs, cause;
        public int enh, evo, form, causeTier;
        public List<RqRune> runes = new();
        public List<RqZone> zones = new();
        public List<RqClause> clauses = new();
    }
    [Serializable] private class RqBuildList { public List<RqBuild> builds = new(); }

    private static async UniTask<bool> ApplyRelicQBuildAsync(PlayerController p, GameRunSession run, List<RuntimeItemData> placed, StringBuilder sb, CancellationToken ct)
    {
        if (!File.Exists(RelicQBuildPath)) { sb.AppendLine($"  빌드 파일 없음({RelicQBuildPath})"); return false; }
        var b = JsonUtility.FromJson<RqBuildList>(File.ReadAllText(RelicQBuildPath))?.builds?.Find(x => x.label != null && x.label.Contains(RelicQBuildLabel));
        if (b == null || b.weapon != "katana") { sb.AppendLine("  빌드를 못 찾았다"); return false; }
        if (!await EquipAsync(p, Katana, ct)) return false;
        var w = p.WeaponManager.Weapon0Data;
        typeof(PlayerWeaponManager).GetMethod("ApplyServerOverrideIfAvailable", BindingFlags.Static | BindingFlags.NonPublic)
            ?.Invoke(null, new object[] { w, Path.GetFileNameWithoutExtension(Katana) });
        w.evolutionStage = b.evo;
        w.enhanceLevel = b.enh;
        w.highestEnhanceReached = b.enh;
        w.formTier = b.form;
        w.formVariants = b.variants ?? "";
        w.augments = b.augs ?? "";
        w.RecomputeEnhancedStats();
        WeaponFormRuntime.Of(p).Rebuild();
        p.WeaponManager.RaiseEquippedWeaponRefreshed();
        // 시뮬 도구는 다른 세션(28) 몫이라 이름으로 찾는다(없어도 컴파일은 된다)
        var project = typeof(WeaponFormProbeEditor).Assembly.GetType("LevelSimBuildProbeEditor")?.GetMethod("ProjectRune", BindingFlags.Static | BindingFlags.NonPublic);
        if (project == null) { sb.AppendLine("  시뮬 도구(LevelSimBuildProbeEditor.ProjectRune)를 못 찾았다"); return false; }
        placed.Clear();
        int runes = 0;
        foreach (var r in b.runes)
        {
            if (project?.Invoke(null, new object[] { r.id }) is not RuntimeItemData d) { sb.AppendLine($"   룬 없음 {r.id}"); continue; }
            if (string.IsNullOrEmpty(d.element)) d.element = r.el;
            placed.Add(d);
            runes++;
        }
        var clauses = new List<(string, CovenantTier, ClauseLink)>();
        foreach (var c in b.clauses) clauses.Add((c.id, (CovenantTier)c.tier, ClauseLink.Immediate));
        bool wrote = clauses.Count > 0 && run.CovenantHandler != null
                     && run.CovenantHandler.TryWriteSentence(CovenantSentence.MakeId(b.cause, (CovenantTier)b.causeTier, clauses));
        run.EffectManager.Rebuild();
        var bridge = MerlinRuneBridge.Instance;
        bridge?.OnZoneCellsUpdated(new Dictionary<string, int>());
        var counts = new Dictionary<string, int>();
        foreach (var z in b.zones) counts[z.zone] = z.cells;
        bridge?.OnZoneCellsUpdated(counts);
        sb.AppendLine($"  빌드 「{b.label}」 — 강화 +{b.enh} · 형 {b.form} · 증강 {b.augs} · 룬 {runes}개 · 구역 {counts.Count}곳 · 서약 {(wrote ? "맺음" : "없음")}");
        await Wait(0.5f, ct);
        return true;
    }

    // 64e — 평타마다 나오는 증강이 바닥 선을 그리지 않는다(10-16 사용자 「기본 공격마다 가이드라인」). 증강을 낀 채 4초 휘두르며
    // 새로 생긴 바닥 선(FormGroundStrip · FormGroundRing)과 증강 발동 로그를 센다. 형 끔 · 유물 없음.
    private static async UniTask SwingGuidesAsync(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        sb.AppendLine("평타 바닥 선 — 증강을 낀 채 4초 휘두름 · 새로 생긴 바닥 선 · 증강 발동 로그(형 끔 · 유물 없음)");
        var cases = new (string path, string augs, string label, string[] keys)[]
        {
            (Katana, "aug_blade_scar:3", "카타나 칼자국", new[] { "칼자국" }),
            (Great,  "aug_great_giant:1,aug_thunder:1", "대검 거인의 걸음 · 천둥", new[] { "거인의 걸음", "천둥" }),
        };
        foreach (var c in cases)
        {
            Managers.UI?.CloseAllPopupUI();
            Time.timeScale = 1f;
            if (!await EquipAsync(p, c.path, ct)) { sb.AppendLine($"  {c.label} 장착 실패"); continue; }
            var w = p.WeaponManager.Weapon0Data;
            w.formTier = 0;
            WeaponFormRuntime.Of(p).Rebuild();
            w.augments = c.augs;
            GameRunBootstrapper.Instance.Run.EffectManager.Rebuild();
            await Wait(0.5f, ct);
            s_home = p.transform.position;
            var tr = await TrackAsync(p.transform.position + Flat(p.transform.forward) * 1.8f, ct);
            if (tr == null) { sb.AppendLine($"  {c.label} 표적 실패"); continue; }
            await Wait(0.3f, ct);
            var seen = new HashSet<int>();
            int guides = 0, log0 = s_formLog.Count;
            float r0 = Time.realtimeSinceStartup, nextPush = 0f;
            while (Time.realtimeSinceStartup - r0 < 4f)
            {
                RefillAll(); Steer(p, tr.M.transform.position);
                float el = Time.realtimeSinceStartup - r0;
                if (el >= nextPush) { nextPush = el + 0.05f; p.InputBuffer.Push(Command.Light); }
                foreach (var lr in UnityEngine.Object.FindObjectsByType<LineRenderer>(FindObjectsSortMode.None))
                    if (lr != null && (lr.name == "FormGroundStrip" || lr.name == "FormGroundRing") && seen.Add(lr.GetInstanceID())) guides++;
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            p.InputBuffer.Clear();
            var fired = new StringBuilder();
            bool all = true;
            foreach (var k in c.keys)
            {
                int n = 0;
                for (int i = log0; i < s_formLog.Count; i++) if (s_formLog[i].Contains(k)) n++;
                fired.Append($" {k} {n}번");
                all &= n > 0;
            }
            Check(sb, $"{c.label} — 4초 동안 바닥 선 {guides}개 ·{fired}(발동은 그대로)", guides == 0 && all);
            w.augments = "";
            GameRunBootstrapper.Instance.Run.EffectManager.Rebuild();
            DetachAll();
            await Wait(0.8f, ct);
        }
    }

    // 64f — 추가 피해 판정이 「몸에 닿으면」인가(10-16 사용자 「이펙트 판정이 안 되는 이펙트들이 가끔 있다」).
    private static async UniTask BodyJudgeAsync(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        sb.AppendLine("추가 피해 판정 = 몸에 닿으면 — 직선(반폭 0.9 · 6 m · FormStrike.Line) · 원뿔(6 m · ±45° · CombatQuery.GetDamageablesInCone)");
        string[] kinds = { "Slime/Slime", "Golem/Golem", "BlackKnight/BlackKnight", "BishopKnight/BishopKnight", "Cyclops/Cyclops", "DemonKing/DemonKing", "Dragon/Dragon", "FlyingDemon/FlyingDemon" };
        s_home = p.transform.position;
        Vector3 fwd = Flat(p.transform.forward), right = Vector3.Cross(Vector3.up, fwd);
        var mbuf = new List<MonsterBase>(16);
        var gbuf = new List<GameObject>(16);
        foreach (var kind in kinds)
        {
            var m = await Managers.ObjectPooler.SpawnAsync<MonsterBase>(kind, ObjectPoolerManager.PoolType.Monster, s_home + fwd * 3f, Quaternion.identity);
            if (m == null) { sb.AppendLine($"  {kind} 부르기 실패"); continue; }
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 6f && m.IsDamageImmuneNow) await UniTask.Yield(PlayerLoopTiming.Update, ct);
            await Wait(0.5f, ct);
            m.Status.ApplyCc("probe_hold", 9999f);
            SetHp(m, TankHp);
            float r = m.BodyRadius;
            // 직선 — 몸 가장자리 걸침(중심은 옛 여유 0.4 밖, 몸 안) · 몸 밖
            float edge = 0.9f + Mathf.Max(0.4f, r) * 0.85f, outside = 0.9f + Mathf.Max(0.4f, r) + 0.35f;
            bool lineEdge = LineHits(p, m, s_home + fwd * 3f + right * edge, fwd, mbuf);
            bool lineOut  = LineHits(p, m, s_home + fwd * 3f + right * outside, fwd, mbuf);
            // 원뿔 — 거리 2.5 m · 각 = 45° + 몸이 걸치는 각의 70% · 몸 밖(45° + 걸치는 각 + 8°)
            float d = 2.5f, extra = Mathf.Asin(Mathf.Clamp01(r / d)) * Mathf.Rad2Deg;
            bool coneEdge = ConeHits(p, m, s_home + Quaternion.AngleAxis(45f + extra * 0.7f, Vector3.up) * fwd * d, fwd, gbuf);
            bool coneOut  = ConeHits(p, m, s_home + Quaternion.AngleAxis(45f + extra + 8f, Vector3.up) * fwd * d, fwd, gbuf);
            bool band = r > 0.45f;   // 옛 판정(중심 + 0.4)과 몸이 갈리는 몬스터
            Check(sb, $"{kind} — 몸 반경 {r:0.00} m · 직선 가장자리 걸침 {(lineEdge ? "맞음" : "빗나감")} · 몸 밖 {(lineOut ? "맞음" : "안 맞음")}" +
                      $" · 원뿔 가장자리 걸침 {(coneEdge ? "맞음" : "빗나감")} · 몸 밖 {(coneOut ? "맞음" : "안 맞음")}{(band ? " · (옛 판정은 걸침 빗나감)" : "")}",
                  lineEdge && !lineOut && coneEdge && !coneOut);
            m.gameObject.SetActive(false);
            await Wait(0.2f, ct);
        }
    }

    private static void PlaceAt(MonsterBase m, Vector3 at)
    {
        if (m.TryGetComponent<UnityEngine.AI.NavMeshAgent>(out var ag) && ag.isActiveAndEnabled && ag.isOnNavMesh) ag.Warp(at);
        else m.transform.position = at;
        Physics.SyncTransforms();
    }

    private static bool LineHits(PlayerController p, MonsterBase m, Vector3 at, Vector3 fwd, List<MonsterBase> buf)
    {
        PlaceAt(m, at);
        FormStrike.Line(p, s_home, fwd, 6f, 0.9f, 1f, buf);
        return buf.Contains(m) && FormStrike.DistToSegmentXZ(m.transform.position, s_home, s_home + fwd * 6f) <= 0.9f + CombatQuery.BodyReach(m);
    }

    private static bool ConeHits(PlayerController p, MonsterBase m, Vector3 at, Vector3 fwd, List<GameObject> buf)
    {
        PlaceAt(m, at);
        CombatQuery.GetDamageablesInCone(s_home, fwd, 6f, 45f, p.gameObject, 16, buf, showGuide: false);
        foreach (var go in buf) if (go != null && (go == m.gameObject || go.transform.IsChildOf(m.transform) || m.transform.IsChildOf(go.transform))) return true;
        return false;
    }

    // ── 65 분열 대칭 · 무형검 검기(10-18) ───────────────────────────
    // 사용자 10-18 ⑮ 「분열의 시위가 대칭이 아니라 한쪽씩 열려 어색」 · ⑯ 「무형검이 날리는 추가타 검기는 타격 판정이 거의 없는 것 같다」.
    private static async UniTask Split65Async(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        sb.AppendLine("65 — 분열 대칭 · 무형참 검기 폭 · 형 없는 연타 길이(유물 없음)");
        await Wait(3f, ct);
        foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
            if (m != null && !m.IsDead) m.gameObject.SetActive(false);

        // 65a 활 분열 — Lv 1 · 4 · 7 · 10(2 · 3 · 4 · 5발): 한 번 쏜 화살들의 각(조준 기준) · 첫 등장 프레임 차
        var parts = RangedPartsState.Current;
        var savedParts = new List<RangedPartsState.Equipped>(parts.Equipped_);
        try
        {
            if (await EquipAsync(p, Bow, ct, PlayerWeaponManager.Slot1))
            {
                s_home = p.transform.position;
                Vector3 fw = Flat(p.transform.forward), aim = s_home + fw * 8f;
                for (float w = 0f; w < 1.5f; w += 0.1f) { Steer(p, aim); p.InputBuffer.Push(Command.Light); await Wait(0.1f, ct); }   // 예열(풀 · 첫 장착)
                await Wait(1f, ct);
                float refYaw = float.NaN;   // 3발(Lv4) 가운데 화살 = 조준 — 화살 모델 forward가 비행 방향과 어긋나 있어 상대각으로 본다
                foreach (int lv in new[] { 4, 1, 7, 10 })
                {
                    parts.Restore(new[] { new RangedPartsState.Equipped { partId = "part_split", level = lv } });
                    await Wait(0.4f, ct);
                    foreach (var a in UnityEngine.Object.FindObjectsByType<BasicArrow>(FindObjectsSortMode.None)) if (a != null) a.gameObject.SetActive(false);
                    var seen = new Dictionary<int, (float yaw, int frame)>();
                    Steer(p, aim);
                    p.InputBuffer.Push(Command.Light);
                    float t0 = Time.realtimeSinceStartup;
                    while (Time.realtimeSinceStartup - t0 < 0.6f)
                    {
                        Steer(p, aim);
                        foreach (var a in UnityEngine.Object.FindObjectsByType<BasicArrow>(FindObjectsSortMode.None))
                        {
                            if (a == null || !a.isActiveAndEnabled || seen.ContainsKey(a.GetInstanceID())) continue;
                            seen[a.GetInstanceID()] = (Vector3.SignedAngle(Flat(fw), Flat(a.transform.forward), Vector3.up), Time.frameCount);
                        }
                        if (seen.Count > 0 && Time.realtimeSinceStartup - t0 > 0.35f) break;
                        await UniTask.Yield(PlayerLoopTiming.Update, ct);
                    }
                    var yaws = new List<float>();
                    int fMin = int.MaxValue, fMax = int.MinValue;
                    foreach (var v in seen.Values) { yaws.Add(v.yaw); fMin = Mathf.Min(fMin, v.frame); fMax = Mathf.Max(fMax, v.frame); }
                    yaws.Sort();
                    if (lv == 4 && yaws.Count == 3) refYaw = yaws[1];
                    if (!float.IsNaN(refYaw)) for (int i = 0; i < yaws.Count; i++) yaws[i] = Mathf.DeltaAngle(refYaw, yaws[i]);
                    float sum = 0f; foreach (var y in yaws) sum += y;
                    bool sym = yaws.Count > 0 && !float.IsNaN(refYaw) && Mathf.Abs(sum / yaws.Count) < 1f;
                    var ys = new StringBuilder();
                    foreach (var y in yaws) ys.Append($" {y:0.0}°");
                    Check(sb, $"65a 분열 Lv{lv} — 화살 {yaws.Count}발 · 각{ys} · 평균 {(yaws.Count > 0 ? sum / yaws.Count : 0f):0.0}° · 첫 등장 프레임 차 {(yaws.Count > 0 ? fMax - fMin : -1)}",
                          sym && yaws.Count > 0 && fMax - fMin <= 1);
                    await Wait(0.6f, ct);
                }
            }
            else sb.AppendLine("65a 활 장착 실패");
        }
        finally { parts.Restore(savedParts); }

        // 65b 무형참(E) — 정면 5 m 표적 A(조준 보조가 노린다) + 2.5 m 앞 옆으로 0.8 · 1.4 m 비켜 선 B: 검기가 지나가며 B를 맞히나 · 보이는 검기 반폭
        if (await EquipAsync(p, Nameless, ct))
        {
            var w = p.WeaponManager.Weapon0Data; w.augments = ""; w.formTier = 0;
            WeaponFormRuntime.Of(p).Rebuild(); GameRunBootstrapper.Instance.Run.EffectManager.Rebuild();
            await Wait(0.5f, ct);
            s_home = p.transform.position;
            Vector3 fw = Flat(p.transform.forward), right = Vector3.Cross(Vector3.up, fw);
            foreach (float off in new[] { 0.8f, 1.4f })
            {
                DetachAll();
                var a = await TrackAsync(s_home + fw * 5f, ct);
                var b = await TrackAsync(s_home + fw * 2.5f + right * off, ct);
                if (a == null || b == null) { sb.AppendLine("65b 표적 실패"); continue; }
                HideStrays();
                int hitA = 0, hitB = 0;
                Action<HitInfo> onHit = h =>
                {
                    if (h.Attacker != p.gameObject || h.ActionType != WeaponActionType.ESkill) return;
                    if (h.Target == a.M.gameObject) hitA++;
                    if (h.Target == b.M.gameObject) hitB++;
                };
                float halfW = 0f;
                HitFeedbackService.OnHit += onHit;
                try
                {
                    p.CooldownTracker.ResetCooldown(SkillType.E);
                    Steer(p, a.M.transform.position);
                    await Wait(0.2f, ct);
                    p.InputBuffer.Push(Command.ESkill);
                    float t0 = Time.realtimeSinceStartup;
                    while (Time.realtimeSinceStartup - t0 < 1.6f)
                    {
                        RefillAll();
                        foreach (var arr in UnityEngine.Object.FindObjectsByType<BasicArrow>(FindObjectsSortMode.None))
                        {
                            if (arr == null || !arr.isActiveAndEnabled) continue;
                            foreach (var ps in arr.GetComponentsInChildren<ParticleSystem>())
                            {
                                if (ps == null || !ps.isPlaying) continue;
                                var buf = new ParticleSystem.Particle[ps.particleCount];
                                int pn = ps.GetParticles(buf);
                                float k = ps.transform.lossyScale.x;
                                for (int pi = 0; pi < pn; pi++) halfW = Mathf.Max(halfW, buf[pi].GetCurrentSize3D(ps).x * 0.5f * Mathf.Abs(k));
                            }
                        }
                        await UniTask.Yield(PlayerLoopTiming.Update, ct);
                    }
                }
                finally { HitFeedbackService.OnHit -= onHit; }
                Check(sb, $"65b 무형참 — 비켜 선 적 {off:0.0} m: 맞음 {hitB} · 정면 적 맞음 {hitA} · 보이는 검기 반폭 ≈ {halfW:0.00} m(판정 반경 0.35 · 몸 반경 포함 ≈ 0.5)", hitB > 0 || off > halfW + 0.3f);
                await Wait(0.5f, ct);
            }
            DetachAll();
        }

        // 65c 형 없는 연타(증강) — 막타 뒤 앞 2.5 m 검기: 1.5 m 적(막타가 맞힘) · 3.5 m 적이 맞나 · 보이는 검기 길이
        if (await EquipAsync(p, Nameless, ct))
        {
            var w = p.WeaponManager.Weapon0Data; w.augments = "aug_nameless_formless:1"; w.formTier = 0;
            WeaponFormRuntime.Of(p).Rebuild(); GameRunBootstrapper.Instance.Run.EffectManager.Rebuild();
            await Wait(0.5f, ct);
            s_home = p.transform.position;
            Vector3 fw = Flat(p.transform.forward);
            DetachAll();
            var near = await TrackAsync(s_home + fw * 1.5f, ct);
            var far = await TrackAsync(s_home + fw * 3.5f, ct);
            if (near != null && far != null)
            {
                HideStrays();
                int log0 = s_formLog.Count;
                float farDealt0 = far.Dealt, reach = 0f;
                float t0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - t0 < 4f)
                {
                    RefillAll(); Steer(p, near.M.transform.position); p.InputBuffer.Push(Command.Light);
                    foreach (var go in UnityEngine.Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
                    {
                        if (go == null || !go.isPlaying || !(go.transform.root.name.Contains("windcut") || go.transform.root.name.Contains("WindCut") || go.transform.root.name.Contains("Windcut"))) continue;
                        foreach (var rd in go.GetComponentsInChildren<Renderer>())
                        {
                            if (rd == null || !rd.enabled) continue;
                            var bb = rd.bounds;
                            Vector3 c = bb.center - s_home;
                            reach = Mathf.Max(reach, Vector3.Dot(Flat(c), fw) + Mathf.Abs(bb.extents.x * fw.x) + Mathf.Abs(bb.extents.z * fw.z));
                        }
                    }
                    await Wait(0.05f, ct);
                }
                p.InputBuffer.Clear();
                int fired = 0; for (int i = log0; i < s_formLog.Count; i++) if (s_formLog[i].Contains("형 없는 연타")) fired++;
                Check(sb, $"65c 형 없는 연타 — 4초 발동 {fired}번 · 3.5 m 적 피해 {far.Dealt - farDealt0:0}(평타는 못 닿는 거리) · 보이는 검기 끝 ≈ {reach:0.0} m(판정 2.5 m)", fired > 0 && (reach <= 3.1f || far.Dealt - farDealt0 > 0f));
            }
            else sb.AppendLine("65c 표적 실패");
            p.WeaponManager.Weapon0Data.augments = "";
            GameRunBootstrapper.Instance.Run.EffectManager.Rebuild();
            DetachAll();
        }
    }

    // ── 66 평타 누름 → 베기 · 근접 스킬 E · R(10-18) ───────────────────────
    // 사용자 10-18 「공격이 공격속도에 맞게 나가는 느낌이 아니라 바로 나간다」 · 「근접 무기 스킬들은 조작감이 안 좋고 후딜이 의미 없이 길거나 범위가 좋지 않다」.
    /// <summary>
    /// 72 — 랜슬롯 광기 수급(10-19 ⑤ 「타격마다 쌓여 너무 바로 찬다 — 공격 횟수로」): 무형검 평타 6초 · E 1회를 잡몹 1 · 3마리에 —
    /// 얻은 광기(지금 규칙) · 같은 동안 적중 수(옛 규칙 = 일반 적중 1명 × 1타마다 +1) · 베기 이벤트 수. 광란에 들지 않게 최대를 크게 · 감쇠를 끈다.
    /// </summary>
    private static async UniTask Madness72Async(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        sb.AppendLine("72 — 랜슬롯 광기 수급(행동 단위) · 무형검 · 형 끔 · 증강 없음 · 잡몹 = 일반 등급");
        var ms = (p.RelicBehavior as LancelotMadnessRelic)?.Madness;
        if (ms == null) { sb.AppendLine("랜슬롯 유물이 아니다"); return; }
        await Wait(3f, ct);
        foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
            if (m != null && !m.IsDead) m.gameObject.SetActive(false);
        Managers.UI?.CloseAllPopupUI(); Time.timeScale = 1f;
        if (!await EquipAsync(p, Nameless, ct)) { sb.AppendLine("무형검 장착 실패"); return; }
        var w = p.WeaponManager.Weapon0Data; w.augments = ""; w.formTier = 0;
        WeaponFormRuntime.Of(p).Rebuild(); GameRunBootstrapper.Instance.Run.EffectManager.Rebuild();
        await Wait(0.5f, ct);

        var maxF = typeof(MadnessStack).GetField("_maxStacks", Inst);
        var delayF = typeof(MadnessStack).GetField("_decayDelay", Inst);
        int max0 = (int)maxF.GetValue(ms);
        float delay0 = (float)delayF.GetValue(ms);
        maxF.SetValue(ms, 100000); delayF.SetValue(ms, 9999f);
        var recv = p.GetComponentInChildren<PlayerAnimationEventReceiver>();
        int hits = 0, slashes = 0;
        Action<HitInfo> onH = h => { if (h.Attacker == p.gameObject && h.Damage > 0f) hits++; };
        Action<int> onS = _ => slashes++;
        try
        {
            foreach (int count in new[] { 1, 3 })
            {
                DetachAll();
                s_home = p.transform.position;
                Vector3 fw = Flat(p.transform.forward), right = Vector3.Cross(Vector3.up, fw);
                var ts = new List<HpTrack>();
                for (int i = 0; i < count; i++)
                {
                    var t = await TrackAsync(s_home + fw * 2f + right * ((i - (count - 1) * 0.5f) * 0.9f), ct);
                    if (t != null) ts.Add(t);
                }
                HideStrays();
                if (ts.Count < count) { sb.AppendLine($"  표적 {ts.Count}/{count} — 건너뜀"); continue; }
                for (float q = 0f; q < 1f; q += 0.1f) { RefillAll(); await Wait(0.1f, ct); }

                // ① 평타 6초(0.05초마다 누름)
                hits = 0; slashes = 0;
                HitFeedbackService.OnHit += onH;
                if (recv != null) recv.OnEffectStep += onS;
                ms.SetStacks(0);
                float t0 = Time.time;
                try
                {
                    while (Time.time - t0 < 6f) { RefillAll(); Steer(p, ts[0].M.transform.position); p.InputBuffer.Push(Command.Light); await Wait(0.05f, ct); }
                }
                finally { HitFeedbackService.OnHit -= onH; if (recv != null) recv.OnEffectStep -= onS; }
                int gained = ms.Stacks;
                sb.AppendLine($"  잡몹 {count} · 평타 6초: 광기 +{gained}(지금) · 적중 {hits}(옛 규칙 +{hits}) · 베기 {slashes} → 40까지 지금 {6f * 40f / Mathf.Max(1, gained):0.0}초 · 옛 {6f * 40f / Mathf.Max(1, hits):0.0}초");

                // ② E 1회(다타 스킬)
                for (float q = 0f; q < 1.2f; q += 0.1f) { RefillAll(); await Wait(0.1f, ct); }
                hits = 0;
                HitFeedbackService.OnHit += onH;
                ms.SetStacks(0);
                try
                {
                    p.CooldownTracker.ResetCooldown(SkillType.E);
                    Steer(p, ts[0].M.transform.position);
                    p.InputBuffer.Push(Command.ESkill);
                    for (float q = 0f; q < 3f; q += 0.05f) { RefillAll(); await Wait(0.05f, ct); }
                }
                finally { HitFeedbackService.OnHit -= onH; }
                sb.AppendLine($"  잡몹 {count} · E 1회: 광기 +{ms.Stacks}(지금) · 적중 {hits}(옛 규칙 +{hits})");
            }
        }
        finally
        {
            maxF.SetValue(ms, max0); delayF.SetValue(ms, delay0);
            ms.SetStacks(0);
            DetachAll();
        }
    }

    /// <summary>
    /// 73 — 타격 이펙트 후보 사진(10-19 ⑫ 「검 자체의 심플한 타격 이펙트」 · ⑭ 「추가타도 맞았는지 보이게」).
    /// 슬라임이 맞는 자리(몸 가운데)에 피(SwordHitImpact ×0.29 = 무형검 평타)와 후보를 겹쳐 +0.05 · +0.15초를 찍는다(Temp/ui_shots/HF_NN).
    /// 후보는 먼저 단독으로 띄워 보이는 양 90% 반경을 재고 목표 반경(0.45 m)에 맞춘다. 끝에 속성 점 타격 여섯(반경 0.5) — 추가타 표시 후보.
    /// </summary>
    private static async UniTask HitFx73Async(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        const float TargetR = 0.45f;
        const string Fx = "Assets/RelicFairy/_Imported/EffectSource/";
        sb.AppendLine($"73 — 타격 이펙트 후보 · 피 ×0.29 + 후보(반경 {TargetR} m로 맞춤) · 사진 Temp/ui_shots/HF_NN_a(+0.05초) · _b(+0.15초)");
        await Wait(3f, ct);
        foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
            if (m != null && !m.IsDead) m.gameObject.SetActive(false);
        Directory.CreateDirectory("Temp/ui_shots");
        ElementVfxPlayer.Warmup();
        s_home = p.transform.position;
        Vector3 f = Flat(p.transform.forward), right = Vector3.Cross(Vector3.up, f);
        DetachAll();
        var t = await TrackAsync(s_home + f * 2.5f, ct);
        if (t == null) { sb.AppendLine("표적 실패"); return; }
        HideStrays();
        Vector3 hit = t.M.transform.position + Vector3.up * 0.9f - f * 0.3f;   // 몸 가운데 앞면
        var camGo = new GameObject("~HitFxCam");
        var cam = camGo.AddComponent<Camera>();
        if (Camera.main != null) cam.CopyFrom(Camera.main);
        cam.enabled = false;
        cam.fieldOfView = 40f;
        cam.transform.position = hit - f * 3.2f + right * 1.0f + Vector3.up * 0.9f;
        cam.transform.LookAt(hit);
        var blood = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/RelicFairy/Prefabs/UseEffect/PlayerEffect/SwordHitImpact.prefab");
        int shot = 0;

        GameObject Spawn(GameObject prefab, Vector3 at, float scale)
        {
            var go = UnityEngine.Object.Instantiate(prefab, at, Quaternion.identity);
            go.transform.localScale = Vector3.one * scale;
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true)) { var main = ps.main; main.scalingMode = ParticleSystemScalingMode.Hierarchy; }
            return go;
        }

        async UniTask Row(string label, string path, bool withBlood)
        {
            RefillAll(); HideStrays();
            var prefab = path != null ? AssetDatabase.LoadAssetAtPath<GameObject>(path) : null;
            if (path != null && prefab == null) { sb.AppendLine($"  {label}: 프리팹 없음 {path}"); return; }
            float scale = 1f, native = 0f;
            if (prefab != null)
            {
                // 단독 크기 — 보이는 양 90% 반경
                var before = EffectReachProbe.Snapshot();
                var probe = Spawn(prefab, hit, 1f);
                var r = await EffectReachProbe.ObserveAsync(before, hit, f, 0.6f, ct);
                UnityEngine.Object.Destroy(probe);
                native = r.Radius;
                scale = native > 0.01f ? Mathf.Clamp(TargetR / native, 0.05f, 4f) : 1f;
                await Wait(0.6f, ct);
            }
            var b = withBlood && blood != null ? Spawn(blood, hit, 0.29f) : null;
            var c = prefab != null ? Spawn(prefab, hit, scale) : null;
            string name = $"HF_{++shot:00}";
            await Wait(0.05f, ct); CaptureMain(cam, $"Temp/ui_shots/{name}_a.png");
            await Wait(0.10f, ct); CaptureMain(cam, $"Temp/ui_shots/{name}_b.png");
            sb.AppendLine($"  {name} {label} · 원래 반경 {native:0.00} m → ×{scale:0.00}{(withBlood ? " · 피 함께" : "")} · {path ?? "-"}");
            await Wait(1.4f, ct);
            if (b != null) UnityEngine.Object.Destroy(b);
            if (c != null) UnityEngine.Object.Destroy(c);
            await Wait(0.3f, ct);
        }

        try
        {
            sb.AppendLine("\n■ ⑫ 검 섬광 후보(피 위에)");
            await Row("피만(지금)", null, true);
            await Row("SwordHitSpark(주소 있음)", "Assets/RelicFairy/Prefabs/UseEffect/PlayerEffect/SwordHitSpark.prefab", true);
            await Row("KnifeHit(Hovl · 주소 있음)", Fx + "Hovl Studio/AOE Magic spells Vol.1/Prefabs/Knife hit.prefab", true);
            await Row("Hit 8 dagger(Hovl)", Fx + "Hovl Studio/AAA Projectiles Vol 1/Prefabs/Hit 8 dagger.prefab", true);
            await Row("Slash2(MasterStylized)", Fx + "MasterStylizedProjectiles/Projectiles/Common/Prefabs/Slash2.prefab", true);
            await Row("Travis Hit_01", Fx + "Travis Game Assets/Hit Impact Effects/Prefabs/Hits/Hit_01.prefab", true);
            await Row("Travis Hit_02", Fx + "Travis Game Assets/Hit Impact Effects/Prefabs/Hits/Hit_02.prefab", true);
            await Row("Travis Hit_03(GreatswordHit 주소 있음)", Fx + "Travis Game Assets/Hit Impact Effects/Prefabs/Hits/Hit_03.prefab", true);
            await Row("Travis Hit_04", Fx + "Travis Game Assets/Hit Impact Effects/Prefabs/Hits/Hit_04.prefab", true);
            await Row("Effect_11_SlashHit", Fx + "SpecialSkillsEffectsPack/AllEffects/EffectsSet_2(ScriptBased)/Effects/Effect_11_IntangibleSlash/Effect_11_Parts/Effect_11_SlashHit.prefab", true);
            await Row("Effect_30_Hit", Fx + "SpecialSkillsEffectsPack/AllEffects/EffectsSet_2(ScriptBased)/Effects/Effect_30_SwordForce/Effect_30_Parts/Effect_30_Hit.prefab", true);
            await Row("Effect_34_Hit", Fx + "SpecialSkillsEffectsPack/AllEffects/EffectsSet_2(ScriptBased)/Effects/Effect_34_SwordDance/Effect_34_Parts/Effect_34_Hit.prefab", true);

            sb.AppendLine("\n■ ⑭ 추가타 표시 후보 — 속성 점 타격(ElementVfxPlayer · 반경 0.5 · 피 없이)");
            foreach (RuneElement el in new[] { RuneElement.Fire, RuneElement.Ice, RuneElement.Electric, RuneElement.Light, RuneElement.Dark, RuneElement.Grass })
            {
                RefillAll(); HideStrays();
                ElementVfxPlayer.PlayBurst(el, hit, 0.5f, atFeet: false);
                string name = $"HF_{++shot:00}";
                await Wait(0.05f, ct); CaptureMain(cam, $"Temp/ui_shots/{name}_a.png");
                await Wait(0.10f, ct); CaptureMain(cam, $"Temp/ui_shots/{name}_b.png");
                sb.AppendLine($"  {name} 속성 점 타격 {el}");
                await Wait(1.6f, ct);
            }
        }
        finally
        {
            UnityEngine.Object.Destroy(camGo);
            DetachAll();
        }
    }

    /// <summary>
    /// 74 — 검 쥐는 자리(10-19 ③ 「검 · 칼을 제대로 쥐고 있지 않다」): 무기마다 대기 1.5초 뒤 주먹 구멍(검지 · 가운데 · 약지 마디 원의 중심) 대
    /// 손잡이 가운데(메시 단면이 가장 가는 아래 구간)를 적고, 주먹 구멍을 WeaponMount 기준으로 Temp/weapon_grip74.json에 남긴다(맞추기 메뉴가 읽는다).
    /// 손 가까이 두 방향 사진 Temp/ui_shots/GR_NN_a · _b.
    /// </summary>
    private static async UniTask Grip74Async(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        sb.AppendLine("74 — 검 쥐는 자리 · 주먹 구멍 대 손잡이 가운데(cm) · 사진 Temp/ui_shots/GR_NN");
        await Wait(3f, ct);
        foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
            if (m != null && !m.IsDead) m.gameObject.SetActive(false);
        Directory.CreateDirectory("Temp/ui_shots");
        var anim = p.Anim;
        var camGo = new GameObject("~GripCam");
        var cam = camGo.AddComponent<Camera>();
        if (Camera.main != null) cam.CopyFrom(Camera.main);
        cam.enabled = false;
        cam.fieldOfView = 30f;
        cam.nearClipPlane = 0.02f;
        var json = new List<string>();
        int shot = 0;
        try
        {
            foreach (var (path, label, key) in new[] { (Nameless, "무형검", "Nameless"), (Katana, "카타나", "Katana"), (Great, "대검", "Greatsword") })
            {
                Managers.UI?.CloseAllPopupUI(); Time.timeScale = 1f;
                if (!await EquipAsync(p, path, ct)) { sb.AppendLine($"  {label} 장착 실패"); continue; }
                await Wait(1.5f, ct);
                var wgo = p.WeaponManager.CurrentWeaponInstance;
                var mf = wgo != null ? WeaponGripFit.LargestMesh(wgo) : null;
                if (anim == null || mf == null) { sb.AppendLine($"  {label} 애니메이터/메시 없음"); continue; }
                var mount = wgo.transform.parent;
                Vector3 fist = WeaponGripFit.FistCenter(anim, out string fistNote);
                float gripY = WeaponGripFit.GripCenterY(mf.sharedMesh, out float g0, out float g1);
                Vector3 grip = mf.transform.TransformPoint(new Vector3(0f, gripY, 0f));
                Vector3 axis = mf.transform.TransformDirection(Vector3.up).normalized;
                Vector3 d = fist - grip;
                float along = Vector3.Dot(d, axis);
                float side = (d - axis * along).magnitude;
                Vector3 fm = mount.InverseTransformPoint(fist);
                sb.AppendLine($"  {label} · 소켓 {mount.name} ← {(mount.parent != null ? mount.parent.name : "-")} · 메시 {mf.sharedMesh.name} 손잡이 y {g0:0.000}~{g1:0.000}(가운데 {gripY:0.000}{(WeaponGripFit.LastWasEstimate ? " · 경계로 어림" : "")}) · " +
                              $"손잡이 → 주먹 {d.magnitude * 100f:0.0} cm(칼날 따라 {along * 100f:+0.0;-0.0} · 옆 {side * 100f:0.0}) · {fistNote}");
                json.Add($"\"{key}\":[{fm.x.ToString("R", System.Globalization.CultureInfo.InvariantCulture)},{fm.y.ToString("R", System.Globalization.CultureInfo.InvariantCulture)},{fm.z.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}]");

                // 손 가까이 두 방향(오른쪽 앞 · 위 앞)
                Vector3 r = p.transform.right, f = p.transform.forward;
                string name = $"GR_{++shot:00}";
                cam.transform.position = fist + r * 0.45f + Vector3.up * 0.05f; cam.transform.LookAt(fist);   // 오른쪽 옆
                CaptureMain(cam, $"Temp/ui_shots/{name}_a.png");
                cam.transform.position = fist + f * 0.45f + r * 0.12f + Vector3.up * 0.05f; cam.transform.LookAt(fist);   // 앞
                CaptureMain(cam, $"Temp/ui_shots/{name}_b.png");
                cam.transform.position = fist + r * 1.2f + Vector3.up * 0.1f; cam.transform.LookAt(fist);   // 오른쪽 옆 멀리(손잡이 · 날밑 · 칼날 전체)
                CaptureMain(cam, $"Temp/ui_shots/{name}_c.png");
                sb.AppendLine($"     사진 {name} · 주먹(소켓 기준) {fm.ToString("F3")}");
            }
            File.WriteAllText("Temp/weapon_grip74.json", "{" + string.Join(",", json) + "}");
        }
        finally { UnityEngine.Object.Destroy(camGo); }
    }

    /// <summary>
    /// 75 — 타격 표시 실전(10-19 ⑫⑭): 무형검 · 대검 평타가 슬라임에 맞는 순간 +0.03 · +0.12초 사진(피 + 검 섬광 SwordStrikeFlash) ·
    /// 추가 피해(시너지 경로) 불 · 무속성을 같은 순간 주 피해와 함께 넣어 표시와 숫자가 따로 보이나(HX_NN).
    /// </summary>
    private static int ShowingPopups()
    {
        int n = 0;
        foreach (var x in UnityEngine.Object.FindObjectsByType<DamagePopup>(FindObjectsSortMode.None)) if (x.IsShowing) n++;
        return n;
    }

    private static async UniTask HitFx75Async(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        sb.AppendLine("75 — 타격 표시 실전 · 사진 Temp/ui_shots/HX_NN_a(+0.03초) · _b(+0.12초)");
        await Wait(3f, ct);
        foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
            if (m != null && !m.IsDead) m.gameObject.SetActive(false);
        Directory.CreateDirectory("Temp/ui_shots");
        var camGo = new GameObject("~HitFx75Cam");
        var cam = camGo.AddComponent<Camera>();
        if (Camera.main != null) cam.CopyFrom(Camera.main);
        cam.enabled = false;
        cam.fieldOfView = 40f;
        int shot = 0, flashes = 0;
        try
        {
            foreach (var (path, label) in new[] { (Nameless, "무형검"), (Great, "대검") })
            {
                Managers.UI?.CloseAllPopupUI(); Time.timeScale = 1f;
                if (!await EquipAsync(p, path, ct)) { sb.AppendLine($"  {label} 장착 실패"); continue; }
                var w = p.WeaponManager.Weapon0Data; w.augments = ""; w.formTier = 0;
                WeaponFormRuntime.Of(p).Rebuild(); GameRunBootstrapper.Instance.Run.EffectManager.Rebuild();
                await Wait(0.5f, ct);
                foreach (var old in s_tracks) if (old.M != null) old.M.gameObject.SetActive(false);   // 앞 무기 표적이 겹쳐 서지 않게
                DetachAll();
                s_home = p.transform.position;
                Vector3 f = Flat(p.transform.forward), right = Vector3.Cross(Vector3.up, f);
                var t = await TrackAsync(s_home + f * 2f, ct);
                if (t == null) { sb.AppendLine($"  {label} 표적 실패"); continue; }
                HideStrays();
                Vector3 body = t.M.transform.position + Vector3.up * 0.9f;
                cam.transform.position = body - f * 3.4f + right * 1.3f + Vector3.up * 1.0f;
                cam.transform.LookAt(body);
                for (float q = 0f; q < 1f; q += 0.1f) { RefillAll(); await Wait(0.1f, ct); }

                // ① 평타 첫 적중 순간
                float hitAt = -1f;
                Action<HitInfo> onH = h => { if (hitAt < 0f && h.Attacker == p.gameObject && h.Damage > 0f) hitAt = Time.time; };
                HitFeedbackService.OnHit += onH;
                try
                {
                    float t0 = Time.time, push = -1f;
                    while (hitAt < 0f && Time.time - t0 < 3f)
                    {
                        RefillAll();
                        if (Time.time - push > 0.15f) { Steer(p, t.M.transform.position); p.InputBuffer.Push(Command.Light); push = Time.time; }
                        await UniTask.Yield(PlayerLoopTiming.Update, ct);
                    }
                }
                finally { HitFeedbackService.OnHit -= onH; }
                string name = $"HX_{++shot:00}";
                if (hitAt >= 0f)
                {
                    await Wait(0.06f, ct); CaptureMain(cam, $"Temp/ui_shots/{name}_a.png");
                    flashes = 0;
                    foreach (var go in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
                        if (go.gameObject.activeInHierarchy && go.name.StartsWith("SwordStrikeFlash")) flashes++;
                    await Wait(0.09f, ct); CaptureMain(cam, $"Temp/ui_shots/{name}_b.png");
                }
                sb.AppendLine($"  {name} {label} 평타 적중 {(hitAt >= 0f ? "있음" : "없음")} · 검 섬광(Hit_02) 살아 있는 것 {flashes}");
                await Wait(1.2f, ct);
            }

            // ② 추가 피해(시너지 경로) — 같은 순간 주 피해 + 불 추가타 · 무속성 추가타
            foreach (var old in s_tracks) if (old.M != null) old.M.gameObject.SetActive(false);
            DetachAll();
            s_home = p.transform.position;
            Vector3 tf = Flat(p.transform.forward);
            var tt = await TrackAsync(s_home + tf * 2.5f, ct);
            HideStrays();
            if (tt != null && tt.M != null)
            {
                Vector3 tb = tt.M.transform.position + Vector3.up * 1.0f;
                cam.transform.position = tb - tf * 3.0f + Vector3.Cross(Vector3.up, tf) * 0.9f + Vector3.up * 0.7f;
                cam.transform.LookAt(tb);
                for (float q = 0f; q < 1f; q += 0.1f) { RefillAll(); await Wait(0.1f, ct); }
                foreach (var (el, label) in new[] { ((RuneElement?)RuneElement.Fire, "불"), ((RuneElement?)null, "무속성") })
                {
                    RefillAll(); HideStrays();
                    int before = ShowingPopups();
                    CombatQuery.DealSynergyDamage(tt.M, 7f, p.gameObject, 1f, false, el);
                    string name = $"HX_{++shot:00}";
                    await Wait(0.05f, ct); CaptureMain(cam, $"Temp/ui_shots/{name}_a.png");
                    await Wait(0.07f, ct); CaptureMain(cam, $"Temp/ui_shots/{name}_b.png");
                    int after = ShowingPopups();
                    sb.AppendLine($"  {name} 추가타 {label} · 숫자 {before} → {after}");
                    await Wait(1.5f, ct);
                }
                // 같은 프레임 주 피해 + 추가타 — 숫자 둘이 따로 뜨나
                RefillAll(); HideStrays();
                await Wait(1.2f, ct);
                int b0 = ShowingPopups();
                DamagePopupSpawner.Spawn(tt.M.transform.position + Vector3.up * 1.2f, 30f, false, tt.M.GetInstanceID(), DamageKind.Normal);
                CombatQuery.DealSynergyDamage(tt.M, 7f, p.gameObject, 1f, false, RuneElement.Fire);
                await Wait(0.3f, ct);
                int b1 = ShowingPopups();
                CaptureMain(cam, $"Temp/ui_shots/HX_{++shot:00}_a.png");
                sb.AppendLine($"  HX_{shot:00} 같은 순간 주 피해 30 + 불 추가타 7 → 떠 있는 숫자 {b0} → {b1}(2개 늘면 따로)");
            }
        }
        finally
        {
            UnityEngine.Object.Destroy(camGo);
            DetachAll();
        }
    }

    /// <summary>
    /// 76 — 무형검 E 발사 · R 끝(10-19 W3 · 74.md 「E 발사 0.35 대 베기 약 0.28 · R 게임 시간 실행의 시계 어긋남」):
    /// 표적 있음/없음마다 게임 시간 · 실제 시간을 함께 적는다 — E 발사(화살 생김) 순간의 클립 시각 · R 클립 끝(정규화 1) 대 스킬 끝 · 최저 timeScale(타격 정지) · 애니 갱신 방식.
    /// </summary>
    private static async UniTask SkillClock76Async(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        sb.AppendLine("76 — 무형검 E 발사 · R 끝 · 게임 시간(g) · 실제 시간(r) · E 휘두름 봉우리 = 클립 0.55초(편집 모드 실측)");
        var actField = typeof(PlayerController).GetField("actStateDebug", Inst);
        bool InSkill() => (actField?.GetValue(p)?.ToString() ?? "").Contains("Skill");
        await Wait(3f, ct);
        foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
            if (m != null && !m.IsDead) m.gameObject.SetActive(false);
        Managers.UI?.CloseAllPopupUI(); Time.timeScale = 1f;
        if (!await EquipAsync(p, Nameless, ct)) { sb.AppendLine("무형검 장착 실패"); return; }
        var w = p.WeaponManager.Weapon0Data; w.augments = ""; w.formTier = 0;
        WeaponFormRuntime.Of(p).Rebuild(); GameRunBootstrapper.Instance.Run.EffectManager.Rebuild();
        await Wait(0.5f, ct);
        var anim = p.Anim;
        foreach (var (cmd, slot, label) in new[] { (Command.ESkill, SkillType.E, "E 무형참"), (Command.RSkill, SkillType.R, "R 무형일섬") })
        {
            foreach (bool withTarget in new[] { true, false })
            {
                foreach (var old in s_tracks) if (old.M != null) old.M.gameObject.SetActive(false);
                DetachAll();
                s_home = p.transform.position;
                Vector3 fw = Flat(p.transform.forward);
                HpTrack t = null;
                if (withTarget)
                {
                    t = await TrackAsync(s_home + fw * (slot == SkillType.E ? 5f : 2f), ct);
                    HideStrays();
                    for (float q = 0f; q < 1f; q += 0.1f) { RefillAll(); await Wait(0.1f, ct); }
                    if (t != null) Steer(p, t.M.transform.position);
                }
                await Wait(0.6f, ct);
                p.CooldownTracker.ResetCooldown(slot);
                int arrows0 = UnityEngine.Object.FindObjectsByType<BasicArrow>(FindObjectsSortMode.None).Length;
                float g0 = Time.time, r0 = Time.realtimeSinceStartup, minScale = 1f;
                float fireG = -1f, fireR = -1f, fireClip = -1f, clipEndG = -1f, clipEndR = -1f, endG = -1f, endR = -1f;
                string clipName = null, mode = anim != null ? anim.updateMode.ToString() : "-";
                float clipLen = 0f;
                bool began = false;
                p.InputBuffer.Push(cmd);
                while (Time.realtimeSinceStartup - r0 < 6f)
                {
                    await UniTask.Yield(PlayerLoopTiming.PostLateUpdate, ct);
                    RefillAll();
                    float g = Time.time - g0, r = Time.realtimeSinceStartup - r0;
                    minScale = Mathf.Min(minScale, Time.timeScale);
                    bool inSkill = InSkill();
                    if (inSkill && !began) { began = true; mode = anim != null ? anim.updateMode.ToString() : "-"; }
                    if (began && anim != null)
                    {
                        var info = anim.GetCurrentAnimatorClipInfo(0);
                        var st = anim.GetCurrentAnimatorStateInfo(0);
                        if (clipName == null && info.Length > 0 && info[0].clip != null && !st.loop) { clipName = info[0].clip.name; clipLen = info[0].clip.length; }
                        if (clipName != null && clipEndG < 0f && info.Length > 0 && info[0].clip != null && info[0].clip.name == clipName && st.normalizedTime >= 1f) { clipEndG = g; clipEndR = r; }
                        if (clipName != null && clipEndG < 0f && (info.Length == 0 || info[0].clip == null || info[0].clip.name != clipName)) { clipEndG = g; clipEndR = r; clipName += "(다른 상태로)"; }
                        if (fireG < 0f && slot == SkillType.E && UnityEngine.Object.FindObjectsByType<BasicArrow>(FindObjectsSortMode.None).Length > arrows0)
                        { fireG = g; fireR = r; fireClip = info.Length > 0 && info[0].clip != null ? (st.normalizedTime % 1f) * info[0].clip.length : -1f; }
                    }
                    if (began && !inSkill && endG < 0f) { endG = g; endR = r; }
                    if (!began && r > 1.5f) break;
                    if (endG >= 0f && clipEndG >= 0f && r > endR + 0.2f) break;
                }
                string where = withTarget ? "표적 있음" : "표적 없음";
                if (!began) { sb.AppendLine($"  {label} · {where}: 시전 안 됨"); continue; }
                string fire = slot == SkillType.E ? $" · 발사 g{fireG:0.00} r{fireR:0.00}(클립 {fireClip:0.00}초 — 봉우리 0.55와 {fireClip - 0.55f:+0.00;-0.00})" : "";
                sb.AppendLine($"  {label} · {where} · 클립 {clipName}({clipLen:0.00}초) · 애니 {mode} · 최저 timeScale {minScale:0.00}{fire} · 클립 끝 g{clipEndG:0.00} r{clipEndR:0.00} · 스킬 끝 g{endG:0.00} r{endR:0.00} → 굳은 꼬리 g{endG - clipEndG:+0.00;-0.00} r{endR - clipEndR:+0.00;-0.00}");
                await Wait(1.0f, ct);
            }
        }
        DetachAll();
    }

    /// <summary>
    /// 77 — Shift 대시(10-20 사용자 「키 shift에 대시를 연결 — 마우스 우클릭과 같이」): 대시(Dodge) 액션의 바인딩 · 해석된 컨트롤을 적고,
    /// 왼 Shift · 오른 Shift · 우클릭(대조)을 장치 상태 이벤트로 눌러 대시 시작(OnDodgeStart)이 나가나 센다. 입력 없음(대조)도.
    /// </summary>
    private static async UniTask ShiftDash77Async(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        sb.AppendLine("77 — Shift 대시 · 바인딩 · 키 눌러 대시");
        await Wait(3f, ct);
        foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
            if (m != null && !m.IsDead) m.gameObject.SetActive(false);
        Managers.UI?.CloseAllPopupUI(); Time.timeScale = 1f;
        var actions = typeof(PlayerController).GetField("_inputActions", Inst)?.GetValue(p) as PlayerInputActions;
        if (actions == null) { sb.AppendLine("  입력 액션 없음"); return; }
        var dodge = actions.Player.Dodge;
        var binds = new List<string>();
        foreach (var b in dodge.bindings) binds.Add(b.effectivePath);
        var ctrls = new List<string>();
        foreach (var c in dodge.controls) ctrls.Add(c.path);
        bool hasShift = binds.Contains("<Keyboard>/shift");
        Check(sb, $"① 대시 바인딩 [{string.Join(", ", binds)}] · 해석된 컨트롤 [{string.Join(", ", ctrls)}] · 켜짐 {dodge.enabled}", hasShift && dodge.enabled);

        int dodges = 0;
        Action onD = () => dodges++;
        p.OnDodgeStart += onD;
        var kb = Keyboard.current;
        var mouse = Mouse.current;
        // 에디터 게임 창에 포커스가 없으면 넣은 키 · 마우스 이벤트가 버려진다(1회차: 우클릭 대조도 0) — 실측 동안만 포커스와 무관하게(설정 파일 없음 = 메모리 기본값 · 끝에 되돌림)
        var editorMode0 = InputSystem.settings.editorInputBehaviorInPlayMode;
        var background0 = InputSystem.settings.backgroundBehavior;
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        try
        {
            async UniTask<int> Press(string label, Action down, Action up)
            {
                await Wait(1.5f, ct);   // 대시 쿨 · 앞 대시가 끝나게
                int before = dodges;
                down();
                await Wait(0.12f, ct);
                up();
                await Wait(0.5f, ct);
                int got = dodges - before;
                sb.AppendLine($"  {label}: 대시 시작 {got}회");
                return got;
            }
            int none = await Press("입력 없음(대조)", () => { }, () => { });
            int left = kb != null ? await Press("왼 Shift", () => InputSystem.QueueStateEvent(kb, new KeyboardState(Key.LeftShift)), () => InputSystem.QueueStateEvent(kb, new KeyboardState())) : -1;
            int right = kb != null ? await Press("오른 Shift", () => InputSystem.QueueStateEvent(kb, new KeyboardState(Key.RightShift)), () => InputSystem.QueueStateEvent(kb, new KeyboardState())) : -1;
            int rmb = mouse != null ? await Press("우클릭(대조)", () => InputSystem.QueueStateEvent(mouse, new MouseState().WithButton(MouseButton.Right)), () => InputSystem.QueueStateEvent(mouse, new MouseState())) : -1;
            Check(sb, $"② 키 눌러 대시 — 왼 Shift {left} · 오른 Shift {right} · 우클릭 {rmb} · 입력 없음 {none}", left >= 1 && right >= 1 && rmb >= 1 && none == 0);
        }
        finally
        {
            p.OnDodgeStart -= onD;
            if (kb != null) InputSystem.QueueStateEvent(kb, new KeyboardState());
            if (mouse != null) InputSystem.QueueStateEvent(mouse, new MouseState());
            InputSystem.settings.editorInputBehaviorInPlayMode = editorMode0;
            InputSystem.settings.backgroundBehavior = background0;
        }
    }

    // 78 — 대검 · 카타나 스킬 동작 후보(10-22 사용자 「대검 스킬이 너무 가볍다」 · 「카타나 R 개편」). 무기 = 그 후보를 쥘 무기.
    private static readonly (string label, string path, bool great)[] SkillCandidates78 =
    {
        ("GR_now_R_SPAttack01",   "Assets/RelicFairy/Animations/Player/Test_01/Attack/GreatswordQSkill.FBX", true),
        ("GR_now_E_SPAttack03",   "Assets/RelicFairy/Animations/Player/Test_01/Attack/GreatswordESkill.FBX", true),
        ("GR_JumpAttack04",       "Assets/RelicFairy/_Imported/GhostSamurai_Animset/Animation/katana/Greatsword/GhostSamurai_APose_JumpAttack04_Inplace.FBX", true),
        ("GR_SPAttack04",         "Assets/_ThirdParty/GhostSamurai_Animset/Animation/katana/APose/Attack/Inplace/GhostSamurai_APose_SPAttack04_Inplace.FBX", true),
        ("GR_Attack02_6",         "Assets/_ThirdParty/GhostSamurai_Animset/Animation/katana/APose/Attack/Inplace/GhostSamurai_APose_Attack02_6_Inplace.FBX", true),
        ("GR_Attack03_4",         "Assets/_ThirdParty/GhostSamurai_Animset/Animation/katana/APose/Attack/Inplace/GhostSamurai_APose_Attack03_4_Inplace.FBX", true),
        ("KT_now_R_SPAttack05",   "Assets/RelicFairy/_Imported/GhostSamurai_Animset/Animation/katana/APose/Attack/Inplace/GhostSamurai_APose_SPAttack05_Inplace.FBX", false),
        ("KT_now_E_SPAttack06",   "Assets/RelicFairy/_Imported/GhostSamurai_Animset/Animation/katana/APose/Attack/Inplace/GhostSamurai_APose_SPAttack06_Inplace.FBX", false),
        ("KT_DodgeAttackF",       "Assets/_ThirdParty/GhostSamurai_Animset/Animation/katana/APose/Dodge/Inplace/GhostSamurai_APose_Dodge_Attack_F_Inplace.FBX", false),
        ("KT_Attack02_6_ALL",     "Assets/_ThirdParty/GhostSamurai_Animset/Animation/katana/APose/Attack/Inplace/GhostSamurai_APose_Attack02_6_ALL_Inplace.FBX", false),
        ("KT_Equip01",            "Assets/_ThirdParty/GhostSamurai_Animset/Animation/katana/Common/Unarm&Equip/Inplace/GhostSamurai_APose_Equip01_Inplace.FBX", false),
    };

    private static async UniTask SkillCand78Async(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        sb.AppendLine("78 — 대검 · 카타나 스킬 동작 후보 · 사진 Temp/ui_shots/SC78_<후보>_<0~5>(진행 0.1 · 0.25 · 0.4 · 0.55 · 0.7 · 0.85) · 휘두름 = 오른손 빠르기 봉우리");
        var hand = p.Anim != null ? p.Anim.GetBoneTransform(HumanBodyBones.RightHand) : null;
        if (hand == null) { sb.AppendLine("오른손 뼈 없음"); return; }
        Directory.CreateDirectory("Temp/ui_shots");
        const string State = "GroundLightAttack_01";
        var camGo = new GameObject("~SkillCand78Cam");
        var cam = camGo.AddComponent<Camera>();
        if (Camera.main != null) cam.CopyFrom(Camera.main);
        cam.enabled = false;
        cam.fieldOfView = 50f;
        float[] shotsAt = { 0.1f, 0.25f, 0.4f, 0.55f, 0.7f, 0.85f };
        await Wait(4f, ct);
        foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
            if (m != null && !m.IsDead) m.gameObject.SetActive(false);
        Vector3 home = p.transform.position + Flat(p.transform.forward) * 8f;
        Quaternion homeRot = Quaternion.LookRotation(Flat(p.transform.forward));
        bool? holdingGreat = null;
        object svc = null;
        System.Reflection.MethodInfo ovr = null;

        async UniTask<List<(float nt, float v)>> PlayOnce(AnimationClip clip, string shotName)
        {
            ovr.Invoke(svc, new object[] { State, clip });
            GoHome(p, home, homeRot);
            await Wait(0.3f, ct);
            var samples = new List<(float, float)>();
            p.enabled = false;
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
                        cam.transform.position = pos + right * 3.6f + fwd * 2.4f + Vector3.up * 2.2f;
                        cam.transform.LookAt(pos + Vector3.up * 1.2f + fwd * 0.6f);
                        CaptureMain(cam, $"Temp/ui_shots/SC78_{shotName}_{shot}.png");
                        shot++;
                    }
                }
            }
            finally { p.enabled = true; }
            await Wait(0.4f, ct);
            return samples;
        }

        try
        {
            foreach (var (label, path, great) in SkillCandidates78)
            {
                if (holdingGreat != great)
                {
                    if (!await EquipAsync(p, great ? Great : Katana, ct)) { sb.AppendLine((great ? "대검" : "카타나") + " 장착 실패"); break; }
                    holdingGreat = great;
                    svc = typeof(PlayerController).GetField("_animSvc", Inst)?.GetValue(p);
                    ovr = svc?.GetType().GetMethod("Override", new[] { typeof(string), typeof(AnimationClip) });
                    if (ovr == null) { sb.AppendLine("덮어쓰기 서비스 없음"); break; }
                    await Wait(0.5f, ct);
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

    // 79 — 대검 충격 · 카타나 베기 이펙트 후보(10-22). 지금 쓰는 것은 now_ 줄(대조).
    private static readonly (string label, string path)[] GreatImpact79 =
    {
        ("now_R_extra_Hit25orange", "Assets/RelicFairy/_Imported/EffectSource/Hovl Studio/AAA Projectiles Vol 1/Prefabs/Hit 25 orange explosion.prefab"),
        ("now_E_expand_Finsh", "Assets/RelicFairy/Weapon/Greatsword/Data/Greatsword_ESkile_Finsh.prefab"),
        ("EarthSlamSpikes", "Assets/RelicFairy/_Imported/EffectSource/PixPlays/ElementalAOE/EarthAOE/Version_BuiltIn/EarthSlamSpikesAoeVFX.prefab"),
        ("EarthBlast", "Assets/RelicFairy/_Imported/EffectSource/PixPlays/ElementalBlastVFX/EarthBlast/Version_BuiltIn/EarthBlast.prefab"),
        ("SS1_GroundSlash", "Assets/RelicFairy/_Imported/EffectSource/SpecialSkillsEffectsPack/AllEffects/EffectsSet_1(NotScriptBased)/Effects/Effect_08_GroundSlash/Effect_08_GroundSlash.prefab"),
        ("SS1_LumenCrash", "Assets/RelicFairy/_Imported/EffectSource/SpecialSkillsEffectsPack/AllEffects/EffectsSet_1(NotScriptBased)/Effects/Effect_29_LumenCrash/Effect_29_LumenCrash.prefab"),
        ("SS2_RuinExplosion", "Assets/RelicFairy/_Imported/EffectSource/SpecialSkillsEffectsPack/AllEffects/EffectsSet_2(ScriptBased)/Effects/Effect_14_RuinExplosion/Effect_14_RuinExplosion.prefab"),
        ("SS2_PlanetCrash", "Assets/RelicFairy/_Imported/EffectSource/SpecialSkillsEffectsPack/AllEffects/EffectsSet_2(ScriptBased)/Effects/Effect_44_PlanetCrash/Effect_44_PlanetCrash.prefab"),
        ("Travis_Hit01", "Assets/RelicFairy/_Imported/EffectSource/Travis Game Assets/Hit Impact Effects/Prefabs/Hits/Hit_01.prefab"),
        ("Travis_Hit04", "Assets/RelicFairy/_Imported/EffectSource/Travis Game Assets/Hit Impact Effects/Prefabs/Hits/Hit_04.prefab"),
        ("MSP_Ripple", "Assets/RelicFairy/_Imported/EffectSource/MasterStylizedProjectiles/Projectiles/Common/Prefabs/Ripple.prefab"),
        ("ShatteredGround", "Assets/RelicFairy/_Imported/EffectSource/Magic Pig Games (Infinity PBR)/Shared Files/Magic Spells & Particles/Particle Components/PComponent ShatteredGround.prefab"),
    };
    private static readonly (string label, string path)[] KatanaSlash79 =
    {
        ("now_R_ShinySlash", "Assets/RelicFairy/Prefabs/Effects/ShinySlash.prefab"),
        ("SS2_CriticalSlash", "Assets/RelicFairy/_Imported/EffectSource/SpecialSkillsEffectsPack/AllEffects/EffectsSet_2(ScriptBased)/Effects/Effect_25_CriticalSlash/Effect_25_CriticalSlash.prefab"),
        ("SS2_MassiveSlash", "Assets/RelicFairy/_Imported/EffectSource/SpecialSkillsEffectsPack/AllEffects/EffectsSet_2(ScriptBased)/Effects/Effect_06_MassiveSlash/Effect_06_MassiveSlash.prefab"),
        ("SS2_DimentionCutter", "Assets/RelicFairy/_Imported/EffectSource/SpecialSkillsEffectsPack/AllEffects/EffectsSet_2(ScriptBased)/Effects/Effect_07_DimentionCutter/Effect_07_DimentionCutter.prefab"),
        ("SS2_DarkDraw_Draw", "Assets/RelicFairy/_Imported/EffectSource/SpecialSkillsEffectsPack/AllEffects/EffectsSet_2(ScriptBased)/Effects/Effect_43_DarkDraw/Effect_43_Parts/Effect_43_Draw.prefab"),
        ("MSP_SlashLine", "Assets/RelicFairy/_Imported/EffectSource/MasterStylizedProjectiles/Projectiles/Common/Prefabs/SlashLine.prefab"),
        ("MSP_RedSwordBeamHit", "Assets/RelicFairy/_Imported/EffectSource/MasterStylizedProjectiles/Projectiles/RedSwordBeam/Prefabs/Par_RedSwordBeam_Hit.prefab"),
    };

    private static async UniTask ImpactFx79Async(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        sb.AppendLine("79 — 대검 충격(반경 3.5 m · 바닥) · 카타나 베기(반경 2 m · 몸 높이) 이펙트 후보 · 사진 Temp/ui_shots/VC_NN_a(+0.1) · _b(+0.35) · _c(+0.8)");
        await Wait(3f, ct);
        foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
            if (m != null && !m.IsDead) m.gameObject.SetActive(false);
        Directory.CreateDirectory("Temp/ui_shots");
        Vector3 f = Flat(p.transform.forward), right = Vector3.Cross(Vector3.up, f);
        Vector3 spot = p.transform.position + f * 5f;
        var camGo = new GameObject("~ImpactFx79Cam");
        var cam = camGo.AddComponent<Camera>();
        if (Camera.main != null) cam.CopyFrom(Camera.main);
        cam.enabled = false;
        cam.fieldOfView = 50f;
        int shot = 0;

        GameObject Spawn(GameObject prefab, Vector3 at, Quaternion rot, float scale)
        {
            var go = UnityEngine.Object.Instantiate(prefab, at, rot);
            go.transform.localScale = prefab.transform.localScale * scale;
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true)) { var main = ps.main; main.scalingMode = ParticleSystemScalingMode.Hierarchy; }
            return go;
        }

        async UniTask Row(string group, string label, string path, float targetR, Vector3 at, Quaternion rot)
        {
            HideStrays();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) { sb.AppendLine($"  {group} {label}: 프리팹 없음 {path}"); return; }
            var before = EffectReachProbe.Snapshot();
            var probe = Spawn(prefab, at, rot, 1f);
            var r = await EffectReachProbe.ObserveAsync(before, at, f, 1.2f, ct);
            UnityEngine.Object.Destroy(probe);
            float native = r.Radius;
            float scale = native > 0.01f ? Mathf.Clamp(targetR / native, 0.05f, 6f) : 1f;
            await Wait(0.8f, ct);
            var go = Spawn(prefab, at, rot, scale);
            string name = $"VC_{++shot:00}";
            await Wait(0.10f, ct); CaptureMain(cam, $"Temp/ui_shots/{name}_a.png");
            await Wait(0.25f, ct); CaptureMain(cam, $"Temp/ui_shots/{name}_b.png");
            await Wait(0.45f, ct); CaptureMain(cam, $"Temp/ui_shots/{name}_c.png");
            sb.AppendLine($"  {name} {group} {label} · 원래 반경 {native:0.00} m → ×{scale:0.00} · 입자계 {r.Systems} · {path}");
            await Wait(1.2f, ct);
            if (go != null) UnityEngine.Object.Destroy(go);
            await Wait(0.3f, ct);
        }

        try
        {
            // 대검 충격 — 바닥, 비스듬히 위에서
            cam.transform.position = spot - f * 7.5f + right * 3.0f + Vector3.up * 5.0f;
            cam.transform.LookAt(spot);
            sb.AppendLine("\n■ 대검 충격(목표 반경 3.5 m)");
            foreach (var (label, path) in GreatImpact79) await Row("대검", label, path, 3.5f, spot, Quaternion.LookRotation(f));
            // 카타나 베기 — 몸 높이, 옆에서
            Vector3 body = spot + Vector3.up * 1.0f;
            cam.transform.position = body - f * 4.5f + right * 2.5f + Vector3.up * 1.2f;
            cam.transform.LookAt(body);
            sb.AppendLine("\n■ 카타나 베기(목표 반경 2 m)");
            foreach (var (label, path) in KatanaSlash79) await Row("카타나", label, path, 2f, body, Quaternion.LookRotation(f));
        }
        finally { UnityEngine.Object.Destroy(camGo); }
    }

    // 80 — 대검 · 카타나 스킬 개편 맞춤값(10-22): 클립 타이밍(엉덩이 · 오른손 높이 · 손 빠르기 봉우리) · 이펙트 보이는 크기(주소로 찾음)
    private static readonly (string label, string path)[] Clips80 =
    {
        ("GR_JumpAttack04",  "Assets/RelicFairy/_Imported/GhostSamurai_Animset/Animation/katana/Greatsword/GhostSamurai_APose_JumpAttack04_Inplace.FBX"),
        ("GR_E_SPAttack03",  "Assets/RelicFairy/Animations/Player/Test_01/Attack/GreatswordESkill.FBX"),
        ("KT_SPAttack06",    "Assets/RelicFairy/_Imported/GhostSamurai_Animset/Animation/katana/APose/Attack/Inplace/GhostSamurai_APose_SPAttack06_Inplace.FBX"),
        ("KT_DodgeAttackF",  "Assets/RelicFairy/_Imported/GhostSamurai_Animset/Animation/katana/APose/Dodge/Inplace/GhostSamurai_APose_Dodge_Attack_F_Inplace.FBX"),
    };
    private static readonly string[] Fx80 = { "GreatswordGroundCrack", "GreatswordEFinish", "SwordStrikeFlash", "BasicSlashBlue", "SlashAttack", "KatanaDelayedCut" };

    private static async UniTask SkillFit80Async(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        sb.AppendLine("80 — 스킬 개편 맞춤값: 클립(0.05초마다 엉덩이 · 오른손 높이 m · 손 빠르기 m/s · 봉우리) · 이펙트 보이는 양 90% 반경(×1 · ×2 그대로 · ×2 계층 크기)");
        var hand = p.Anim != null ? p.Anim.GetBoneTransform(HumanBodyBones.RightHand) : null;
        var hips = p.Anim != null ? p.Anim.GetBoneTransform(HumanBodyBones.Hips) : null;
        if (hand == null || hips == null) { sb.AppendLine("뼈 없음"); return; }
        const string State = "GroundLightAttack_01";
        await Wait(4f, ct);
        foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
            if (m != null && !m.IsDead) m.gameObject.SetActive(false);
        if (!await EquipAsync(p, Great, ct)) { sb.AppendLine("대검 장착 실패"); return; }
        var svc = typeof(PlayerController).GetField("_animSvc", Inst)?.GetValue(p);
        var ovr = svc?.GetType().GetMethod("Override", new[] { typeof(string), typeof(AnimationClip) });
        if (ovr == null) { sb.AppendLine("덮어쓰기 서비스 없음"); return; }
        await Wait(0.5f, ct);

        try
        {
            foreach (var (label, path) in Clips80)
            {
                var clip = LoadClipAt(path);
                if (clip == null) { sb.AppendLine($"{label}: 클립 없음 ({path})"); continue; }
                ovr.Invoke(svc, new object[] { State, clip });
                await Wait(0.3f, ct);
                var rows = new List<(float t, float hy, float ny, float v)>();
                p.enabled = false;
                try
                {
                    p.Anim.speed = 1f;
                    p.Anim.Play(State, 0, 0f);
                    await UniTask.Yield(PlayerLoopTiming.PostLateUpdate, ct);
                    Vector3 prev = p.transform.InverseTransformPoint(hand.position);
                    float prevT = 0f, guard = Time.realtimeSinceStartup;
                    while (Time.realtimeSinceStartup - guard < clip.length + 2f)
                    {
                        await UniTask.Yield(PlayerLoopTiming.PostLateUpdate, ct);
                        var info = p.Anim.GetCurrentAnimatorStateInfo(0);
                        if (!info.IsName(State) || info.normalizedTime >= 1f) break;
                        float t = info.normalizedTime * clip.length;
                        if (t > 3.2f) break;   // 뒤는 선 자세
                        Vector3 cur = p.transform.InverseTransformPoint(hand.position);
                        if (t - prevT > 0.0001f) rows.Add((t, p.transform.InverseTransformPoint(hips.position).y, cur.y, (cur - prev).magnitude / (t - prevT)));
                        prev = cur; prevT = t;
                    }
                }
                finally { p.enabled = true; }
                if (rows.Count < 3) { sb.AppendLine($"{label}: 표본 부족"); continue; }

                float vmax = 0f, hy0 = rows[0].hy, apexT = 0f, apexY = float.MinValue;
                foreach (var r in rows) { vmax = Mathf.Max(vmax, r.v); if (r.hy > apexY) { apexY = r.hy; apexT = r.t; } }
                var peaks = new StringBuilder();
                float lastPk = -1f;
                for (int i = 1; i + 1 < rows.Count; i++)
                {
                    var r = rows[i];
                    if (r.v < vmax * 0.3f || r.v < rows[i - 1].v || r.v < rows[i + 1].v || r.t - lastPk < 0.08f) continue;
                    peaks.Append($" {r.t:0.00}({r.v:0.0})");
                    lastPk = r.t;
                }
                float up = -1f, land = -1f;
                foreach (var r in rows) { if (up < 0f && r.hy > hy0 + 0.15f) up = r.t; if (up >= 0f && r.t > apexT && land < 0f && r.hy < hy0 + 0.1f) land = r.t; }
                sb.AppendLine($"{label}: {clip.name} · {clip.length:0.00}초 · 손 봉우리(초(m/s)){peaks} · 엉덩이 시작 {hy0:0.00} · 뜸 {up:0.00} · 꼭대기 {apexT:0.00}({apexY:0.00}) · 내려옴 {land:0.00}");
                var line = new StringBuilder("   ");
                float next = 0f;
                foreach (var r in rows)
                {
                    if (r.t + 0.0001f < next) continue;
                    line.Append($" {r.t:0.00}:{r.hy:0.00}/{r.ny:0.00}/{r.v:0}");
                    next = r.t + 0.05f;
                }
                sb.AppendLine(line.ToString());
                await Wait(0.3f, ct);
            }
        }
        finally { await EquipAsync(p, Katana, ct); }

        // 이펙트 — 주소로 프리팹을 찾아 그대로 ×1 · ×2, 계층 크기 ×2의 보이는 반경
        var settings = UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject.Settings;
        Vector3 f = Flat(p.transform.forward), spot = p.transform.position + f * 5f;
        foreach (var key in Fx80)
        {
            string path = null;
            foreach (var g in settings.groups)
                if (g != null)
                    foreach (var e in g.entries)
                        if (e.address == key) path = e.AssetPath;
            var prefab = path != null ? AssetDatabase.LoadAssetAtPath<GameObject>(path) : null;
            if (prefab == null) { sb.AppendLine($"  {key}: 주소 · 프리팹 없음"); continue; }
            async UniTask<float> Reach(float scale, bool hier)
            {
                HideStrays();
                var before = EffectReachProbe.Snapshot();
                var go = UnityEngine.Object.Instantiate(prefab, spot, Quaternion.LookRotation(f));
                go.transform.localScale = prefab.transform.localScale * scale;
                if (hier) foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true)) { var main = ps.main; main.scalingMode = ParticleSystemScalingMode.Hierarchy; }
                var r = await EffectReachProbe.ObserveAsync(before, spot, f, 1.2f, ct);
                UnityEngine.Object.Destroy(go);
                await Wait(0.4f, ct);
                return r.RadiusMax;
            }
            float r1 = await Reach(1f, false), r2 = await Reach(2f, false), r3 = await Reach(2f, true);
            sb.AppendLine($"  {key}: ×1 {r1:0.00} m · ×2 그대로 {r2:0.00} m({(r1 > 0.01f ? r2 / r1 : 0f):0.00}배) · ×2 계층 {r3:0.00} m({(r1 > 0.01f ? r3 / r1 : 0f):0.00}배) · {path}");
        }
    }

    // 81 — 개편 스킬 실전(10-22): 대검 R · E · 카타나 R · E를 1 · 3단계로 쓰고 적중(표적별 · 마무리) · 진행 사진(누른 뒤 6순간 · 판정 고리 보임)
    private static async UniTask SkillShots81Async(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        sb.AppendLine("81 — 개편 스킬 실전 · 표적 앞 3 m · 앞 왼쪽 5.5 m · 오른쪽 2 m · 사진 Temp/ui_shots/SK81_<무기><스킬><단계>_<0~5>(누른 뒤 0.15 · 0.35 · 0.5 · 0.7 · 0.95 · 1.3초)");
        var actField = typeof(PlayerController).GetField("actStateDebug", Inst);
        bool InSkill() => (actField?.GetValue(p)?.ToString() ?? "").Contains("Skill");
        SkillRangeRing.PlayerGuides = true;
        await Wait(3f, ct);
        foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
            if (m != null && !m.IsDead) m.gameObject.SetActive(false);
        Directory.CreateDirectory("Temp/ui_shots");
        var camGo = new GameObject("~SkillShots81Cam");
        var cam = camGo.AddComponent<Camera>();
        if (Camera.main != null) cam.CopyFrom(Camera.main);
        cam.enabled = false;
        cam.fieldOfView = 55f;
        float[] shotsAt = { 0.15f, 0.35f, 0.5f, 0.7f, 0.95f, 1.3f };
        var recv = p.GetComponentInChildren<PlayerAnimationEventReceiver>();
        Vector3 open = p.transform.position + Flat(p.transform.forward) * 8f;   // 허브 시작점은 벽 옆 — 78처럼 앞 빈자리에서
        Quaternion openRot = Quaternion.LookRotation(Flat(p.transform.forward));
        Vector3 XZ(Vector3 v) => new Vector3(v.x, 0f, v.z);
        try
        {
            foreach (var (path, wl) in new[] { (Great, "GR"), (Katana, "KT") })
            {
                foreach (int enhance in new[] { 0, 12 })
                {
                    Managers.UI?.CloseAllPopupUI(); Time.timeScale = 1f;
                    if (!await EquipAsync(p, path, ct)) { sb.AppendLine($"{wl} 장착 실패"); break; }
                    var w = p.WeaponManager.Weapon0Data; w.augments = ""; w.formTier = 0; w.enhanceLevel = enhance; w.RecomputeEnhancedStats();
                    WeaponFormRuntime.Of(p).Rebuild(); GameRunBootstrapper.Instance.Run.EffectManager.Rebuild();
                    await Wait(0.6f, ct);
                    int tier = SkillTierResolver.Resolve(w);
                    foreach (var (cmd, type, nm) in new[] { (Command.RSkill, SkillType.R, "R"), (Command.ESkill, SkillType.E, "E") })
                    {
                        DetachAll();
                        GoHome(p, open, openRot);
                        await Wait(0.4f, ct);
                        s_home = p.transform.position;
                        Vector3 fw = Flat(p.transform.forward), right = Vector3.Cross(Vector3.up, fw);
                        var a = await TrackAsync(s_home + fw * 3f, ct);
                        var b = await TrackAsync(s_home + fw * 5f - right * 2.2f, ct);
                        var c = await TrackAsync(s_home + right * 2f + fw * 0.3f, ct);
                        if (a == null) { sb.AppendLine($"{wl}{nm}{tier} 표적 실패"); continue; }
                        HideStrays();
                        for (float q = 0f; q < 1.2f; q += 0.1f) { RefillAll(); Steer(p, a.M.transform.position); await Wait(0.1f, ct); }
                        p.CooldownTracker.ResetCooldown(type);
                        RefillAll();
                        var hits = new List<(string who, float t, float dmg, bool fin)>();
                        int fxSteps = 0;
                        float s0 = Time.time;
                        Action<HitInfo> onH = h =>
                        {
                            if (h.Attacker != p.gameObject || h.Target == null) return;
                            string who = h.Target == a.M.gameObject ? "앞" : (b != null && h.Target == b.M.gameObject ? "왼" : (c != null && h.Target == c.M.gameObject ? "옆" : "?"));
                            hits.Add((who, Time.time - s0, h.Damage, h.IsFinisher));
                        };
                        Action<int> onFx = _ => { if (InSkill()) fxSteps++; };
                        HitFeedbackService.OnHit += onH;
                        if (recv != null) recv.OnEffectStep += onFx;
                        float recAt = -1f, endAt = -1f;
                        Vector3 start = p.transform.position;
                        try
                        {
                            p.InputBuffer.Push(cmd);
                            int shot = 0;
                            bool began = false;
                            while (Time.time - s0 < 3f)
                            {
                                await UniTask.Yield(PlayerLoopTiming.PostLateUpdate, ct);
                                RefillAll();
                                float now = Time.time - s0;
                                bool inSkill = InSkill();
                                if (inSkill) began = true;
                                if (began && recAt < 0f && p.SkillRecovering) recAt = now;
                                if (began && !inSkill && endAt < 0f) endAt = now;
                                if (shot < shotsAt.Length && now >= shotsAt[shot])
                                {
                                    Vector3 mid = s_home + fw * 2.5f;
                                    cam.transform.position = mid - fw * 4.5f + right * 6f + Vector3.up * 6.5f;
                                    cam.transform.LookAt(mid + Vector3.up * 0.5f);
                                    CaptureMain(cam, $"Temp/ui_shots/SK81_{wl}{nm}{tier}_{shot}.png");
                                    shot++;
                                }
                                if (endAt >= 0f && now > endAt + 0.9f && shot >= shotsAt.Length) break;
                            }
                        }
                        finally { HitFeedbackService.OnHit -= onH; if (recv != null) recv.OnEffectStep -= onFx; }
                        var line = new StringBuilder();
                        foreach (var h in hits) line.Append($" {h.who}@{h.t:0.00}:{h.dmg:0}{(h.fin ? "막" : "")}");
                        float moved = Vector3.Distance(XZ(start), XZ(p.transform.position));
                        sb.AppendLine($"{wl} {nm} {tier}단계(+{enhance}): 후딜 {recAt:0.00} · 끝 {endAt:0.00} · 이동 {moved:0.0} m · 애니 이펙트 이벤트 {fxSteps} · 적중{line}");
                        await Wait(0.8f, ct);
                    }
                }
            }
        }
        finally
        {
            UnityEngine.Object.Destroy(camGo);
            SkillRangeRing.PlayerGuides = false;
            DetachAll();
        }
    }

    // 82 — 10-22 B1 대검 평타(휘두름 · 한 타 · 닿는 거리 · 사진) + B5 내려찍기 충격 후보 사진(보이는 반경 → 3.5 m로 맞춤)
    private static readonly (string label, string path)[] SlamFx82 =
    {
        ("now_plunge_DragonPunch", "Assets/RelicFairy/_Imported/EffectSource/Hovl Studio/RPG VFX Bundle/Prefabs/Magic buffs and hits/Dragon punch.prefab"),
        ("Travis_Hit04",       "Assets/RelicFairy/_Imported/EffectSource/Travis Game Assets/Hit Impact Effects/Prefabs/Hits/Hit_04.prefab"),
        ("now_crack",          "Assets/RelicFairy/_Imported/EffectSource/Magic Pig Games (Infinity PBR)/Shared Files/Magic Spells & Particles/Particle Components/PComponent ShatteredGround.prefab"),
        ("now_dust",           "Assets/RelicFairy/Weapon/Greatsword/Data/Greatsword_ESkile_Finsh.prefab"),
        ("now_flash_Hit02",    "Assets/RelicFairy/_Imported/EffectSource/Travis Game Assets/Hit Impact Effects/Prefabs/Hits/Hit_02.prefab"),
        ("SS1_OneHandSmash",   "Assets/RelicFairy/_Imported/EffectSource/SpecialSkillsEffectsPack/AllEffects/EffectsSet_1(NotScriptBased)/Effects/Effect_07_OneHandSmash/Effect_07_OneHandSmash.prefab"),
        ("SS1_GroundScatter",  "Assets/RelicFairy/_Imported/EffectSource/SpecialSkillsEffectsPack/AllEffects/EffectsSet_1(NotScriptBased)/Effects/Effect_21_GroundScatter/Effect_21_GroundScatter.prefab"),
        ("SS1_GroundScatterB", "Assets/RelicFairy/_Imported/EffectSource/SpecialSkillsEffectsPack/AllEffects/EffectsSet_1(NotScriptBased)/Effects/Effect_21_GroundScatter/Effect_21_GroundScatter/Effect_21_GroundScatter(Base).prefab"),
        ("SS1_InnercoreRelease","Assets/RelicFairy/_Imported/EffectSource/SpecialSkillsEffectsPack/AllEffects/EffectsSet_1(NotScriptBased)/Effects/Effect_21_GroundScatter/Effect_21_InnertcoreRelease.prefab"),
        ("SS2_OneHandSmash",   "Assets/RelicFairy/_Imported/EffectSource/SpecialSkillsEffectsPack/AllEffects/EffectsSet_2(ScriptBased)/Effects/Effect_02_FastestFist/Effect_02_OneHandSmash.prefab"),
        ("PP_EarthBlast",      "Assets/RelicFairy/_Imported/EffectSource/PixPlays/ElementalBlastVFX/EarthBlast/Version_BuiltIn/EarthBlast.prefab"),
        ("PP_ground_shatter2", "Assets/RelicFairy/_Imported/EffectSource/PixPlays/Components/Meshes/Objects/ground_shatter2.prefab"),
        ("Travis_Hit03",       "Assets/RelicFairy/_Imported/EffectSource/Travis Game Assets/Hit Impact Effects/Prefabs/Hits/Hit_03.prefab"),
        ("Travis_Hit01",       "Assets/RelicFairy/_Imported/EffectSource/Travis Game Assets/Hit Impact Effects/Prefabs/Hits/Hit_01.prefab"),
    };

    private static async UniTask GsBasic82Async(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        sb.AppendLine("82 — B1 대검 평타(4초 연타 · 표적 앞 2.5 m · 앞 4.2 m · 옆 2.5 m) · 사진 Temp/ui_shots/GB82_<0~5> · B5 충격 후보 SF82_NN_a/b/c(+0.1 · +0.35 · +0.8초)");
        await Wait(3f, ct);
        foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
            if (m != null && !m.IsDead) m.gameObject.SetActive(false);
        Directory.CreateDirectory("Temp/ui_shots");
        var camGo = new GameObject("~GsBasic82Cam");
        var cam = camGo.AddComponent<Camera>();
        if (Camera.main != null) cam.CopyFrom(Camera.main);
        cam.enabled = false;
        cam.fieldOfView = 55f;
        Vector3 open = p.transform.position + Flat(p.transform.forward) * 8f;
        Quaternion openRot = Quaternion.LookRotation(Flat(p.transform.forward));
        try
        {
            foreach (var (path, label) in new[] { (Great, "대검"), (Katana, "카타나") })
            {
                Managers.UI?.CloseAllPopupUI(); Time.timeScale = 1f;
                if (!await EquipAsync(p, path, ct)) { sb.AppendLine($"{label} 장착 실패"); continue; }
                var w = p.WeaponManager.Weapon0Data; w.augments = ""; w.formTier = 0;
                WeaponFormRuntime.Of(p).Rebuild(); GameRunBootstrapper.Instance.Run.EffectManager.Rebuild();
                DetachAll();
                GoHome(p, open, openRot);
                await Wait(0.5f, ct);
                s_home = p.transform.position;
                Vector3 fw = Flat(p.transform.forward), right = Vector3.Cross(Vector3.up, fw);
                var a = await TrackAsync(s_home + fw * 2.5f, ct);
                var b = await TrackAsync(s_home + fw * 4.2f, ct);
                var c = await TrackAsync(s_home + right * 2.5f + fw * 0.5f, ct);
                if (a == null) { sb.AppendLine($"{label} 표적 실패"); continue; }
                HideStrays();
                for (float q = 0f; q < 1.2f; q += 0.1f) { RefillAll(); Steer(p, a.M.transform.position); await Wait(0.1f, ct); }
                var recv = p.GetComponentInChildren<PlayerAnimationEventReceiver>();
                int swings = 0, ha = 0, hb = 0, hc = 0; float da = 0f;
                Action<int> onS = _ => swings++;
                Action<HitInfo> onH = h =>
                {
                    if (h.Attacker != p.gameObject || h.Target == null) return;
                    if (h.Target == a.M.gameObject) { ha++; if (!h.IsCritical) da += h.Damage; }
                    else if (b != null && h.Target == b.M.gameObject) hb++;
                    else if (c != null && h.Target == c.M.gameObject) hc++;
                };
                int crit = 0;
                Action<HitInfo> onC = h => { if (h.Attacker == p.gameObject && h.Target == a.M.gameObject && h.IsCritical) crit++; };
                if (recv != null) recv.OnEffectStep += onS;
                HitFeedbackService.OnHit += onH; HitFeedbackService.OnHit += onC;
                float t0 = Time.time; int shot = 0;
                float[] shotsAt = { 0.2f, 0.45f, 0.7f, 0.95f, 1.2f, 1.5f };
                try
                {
                    while (Time.time - t0 < 4f)
                    {
                        RefillAll(); Steer(p, a.M.transform.position); p.InputBuffer.Push(Command.Light);
                        await UniTask.Yield(PlayerLoopTiming.PostLateUpdate, ct);
                        if (label == "대검" && shot < shotsAt.Length && Time.time - t0 >= shotsAt[shot])
                        {
                            Vector3 mid = s_home + fw * 2f;
                            cam.transform.position = mid - fw * 4.5f + right * 5.5f + Vector3.up * 6f;
                            cam.transform.LookAt(mid + Vector3.up * 0.5f);
                            CaptureMain(cam, $"Temp/ui_shots/GB82_{shot}.png");
                            shot++;
                        }
                    }
                }
                finally { if (recv != null) recv.OnEffectStep -= onS; HitFeedbackService.OnHit -= onH; HitFeedbackService.OnHit -= onC; p.InputBuffer.Clear(); }
                float dur = Time.time - t0;
                int plain = ha - crit;
                sb.AppendLine($"  {label}: 휘두름 {swings / dur:0.00}/초 · 앞 2.5 m 적중 {ha}(치명 {crit}) · 한 타(치명 뺌) {(plain > 0 ? da / plain : 0f):0} · 초당 {(a.Dealt / dur):0} · 앞 4.2 m 적중 {hb} · 옆 2.5 m 적중 {hc} · 공격력 {p.RuntimeStats.MeleeAttack}");
                DetachAll();
                await Wait(0.6f, ct);
            }

            // B5 — 충격 후보: 보이는 반경을 재 3.5 m로 맞추고 바닥을 비스듬히 위에서
            GoHome(p, open, openRot);
            await Wait(0.4f, ct);
            Vector3 f = Flat(p.transform.forward), r2 = Vector3.Cross(Vector3.up, f);
            Vector3 spot = p.transform.position + f * 5f;
            cam.transform.position = spot - f * 7.5f + r2 * 3.0f + Vector3.up * 5.0f;
            cam.transform.LookAt(spot);
            int n = 0;
            foreach (var (label, path) in SlamFx82)
            {
                HideStrays();
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) { sb.AppendLine($"  {label}: 프리팹 없음"); continue; }
                var before = EffectReachProbe.Snapshot();
                var probe = UnityEngine.Object.Instantiate(prefab, spot, Quaternion.LookRotation(f));
                var rr = await EffectReachProbe.ObserveAsync(before, spot, f, 1.2f, ct);
                UnityEngine.Object.Destroy(probe);
                float native = rr.RadiusMax;
                float scale = native > 0.01f ? Mathf.Clamp(3.5f / native, 0.05f, 6f) : 1f;
                await Wait(0.6f, ct);
                var go = UnityEngine.Object.Instantiate(prefab, spot, Quaternion.LookRotation(f));
                go.transform.localScale = prefab.transform.localScale * scale;
                string name = $"SF82_{++n:00}";
                await Wait(0.10f, ct); CaptureMain(cam, $"Temp/ui_shots/{name}_a.png");
                await Wait(0.25f, ct); CaptureMain(cam, $"Temp/ui_shots/{name}_b.png");
                await Wait(0.45f, ct); CaptureMain(cam, $"Temp/ui_shots/{name}_c.png");
                sb.AppendLine($"  {name} {label} · 원래 반경 {native:0.00} m → ×{scale:0.00} · 입자계 {rr.Systems}");
                await Wait(1.0f, ct);
                if (go != null) UnityEngine.Object.Destroy(go);
                await Wait(0.3f, ct);
            }
        }
        finally { UnityEngine.Object.Destroy(camGo); DetachAll(); }
    }

    // 83 — 10-22 B2 룬 타격 크기 · B3 무형검 추가 검기 · B6 분신
    private static async UniTask RuneHit83Async(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        sb.AppendLine("83 — B2 원소 점 타격 · 전설 대상 타격 크기(보이는 양 90% 반경 · 구간 최대) · B3 형 없는 연타 · 무형일섬 2단계 마무리 · B6 분신 사진 Temp/ui_shots/GH83_NN");
        await Wait(3f, ct);
        foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
            if (m != null && !m.IsDead) m.gameObject.SetActive(false);
        Directory.CreateDirectory("Temp/ui_shots");
        ElementVfxPlayer.Warmup();
        LegendaryRuntime.EnsureExists();
        await Wait(1.5f, ct);
        Vector3 open = p.transform.position + Flat(p.transform.forward) * 8f;
        Quaternion openRot = Quaternion.LookRotation(Flat(p.transform.forward));
        GoHome(p, open, openRot);
        await Wait(0.4f, ct);
        Vector3 f = Flat(p.transform.forward), right = Vector3.Cross(Vector3.up, f);
        Vector3 spot = p.transform.position + f * 5f;

        async UniTask<float> Reach(Action spawn, Vector3 origin)
        {
            HideStrays();
            var before = EffectReachProbe.Snapshot();
            spawn();
            var r = await EffectReachProbe.ObserveAsync(before, origin, f, 1.2f, ct);
            await Wait(0.8f, ct);
            return r.RadiusMax;
        }

        // ① 원소 점 타격 — 원래(배율 1) · 게임 길(PlayBurst 반경 1, 맞춘 뒤)
        var reg = AssetDatabase.LoadAssetAtPath<ElementVfxRegistry>("Assets/RelicFairy/Systems/Combat/ElementVfxRegistry.asset");
        sb.AppendLine("\n■ 원소 점 타격(목표 0.6 m)");
        foreach (RuneElement e in Enum.GetValues(typeof(RuneElement)))
        {
            float lift = 0f, vis = 0f;
            GameObject prefab = reg != null ? reg.GetImpact(e, out lift, out vis) : null;
            if (prefab == null) { sb.AppendLine($"  {e}: 프리팹 없음"); continue; }
            Vector3 at = spot + Vector3.up * lift;
            float raw = await Reach(() => ElementVfxPlayer.PlayPrefab(prefab, at, 1f), at);
            float game = await Reach(() => ElementVfxPlayer.PlayBurst(e, spot, 1f), at);
            sb.AppendLine($"  {e}: {prefab.name} · 배율 1 {raw:0.00} m · 게임 길(반경 1) {game:0.00} m · 칸 값 {vis:0.00}");
        }

        // ② 전설 룬 대상 타격(…HitVfx) — 게임이 쓰는 배율 0.8
        var cat = AssetDatabase.LoadAssetAtPath<LegendaryVfxCatalog>("Assets/RelicFairy/Systems/Item/Effects/Legendary/LegendaryVfxCatalog.asset");
        sb.AppendLine("\n■ 전설 대상 타격(게임이 쓰는 배율 · 목표 ≈ 1 m · 무기 섬광 0.57 m)");
        var used = new Dictionary<string, float> {
            { "fireAoeHitVfx", 0.44f }, { "fireSingleHitVfx", 0.8f }, { "fireOrbHitVfx", 0.19f },
            { "iceHitVfx", 0.67f }, { "iceAoeHitVfx", 0.22f }, { "elecAoeHitVfx", 0.65f }, { "elecHitVfx", 0.4f },
            { "grassHitVfx", 1f }, { "grassAoeHitVfx", 0.52f }, { "grassSingleHitVfx", 0.65f },
            { "lightHitVfx", 0.5f }, { "lightAoeHitVfx", 0.49f }, { "lightSingleHitVfx", 0.51f },
            { "darkHitVfx", 0.5f }, { "darkAoeHitVfx", 0.45f }, { "darkSingleHitVfx", 0.19f } };
        if (cat != null)
            foreach (var fi in typeof(LegendaryVfxCatalog).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            {
                if (!fi.Name.EndsWith("HitVfx") || fi.GetValue(cat) is not GameObject hp || hp == null) continue;
                Vector3 at = spot + Vector3.up;
                if (!used.TryGetValue(fi.Name, out float us)) continue;   // 범위 이펙트(판정에 맞춘 것)는 뺀다
                float r = await Reach(() => LegendaryRuntime.SpawnVfx(hp, at, Quaternion.LookRotation(f), 1.2f, us), at);
                sb.AppendLine($"  {fi.Name}: {hp.name} ×{us:0.00} · {r:0.00} m");
            }

        // ③ 비교 — 무기 섬광(Hit_02 ×0.56) · 대검 타격 후보(Hit_04 ×1) · 무형일섬 2단계 공간 베기(×0.28)
        sb.AppendLine("\n■ 비교");
        foreach (var (label, path, scale) in new[] {
            ("무기 섬광 Hit_02 ×0.56", "Assets/RelicFairy/_Imported/EffectSource/Travis Game Assets/Hit Impact Effects/Prefabs/Hits/Hit_02.prefab", 0.56f),
            ("대검 타격 후보 Hit_04 ×1", "Assets/RelicFairy/_Imported/EffectSource/Travis Game Assets/Hit Impact Effects/Prefabs/Hits/Hit_04.prefab", 1f) })
        {
            var hp = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (hp == null) { sb.AppendLine($"  {label}: 없음"); continue; }
            Vector3 at = spot + Vector3.up;
            float r = await Reach(() => ElementVfxPlayer.PlayPrefab(hp, at, scale), at);
            sb.AppendLine($"  {label}: {r:0.00} m");
        }
        {
            Vector3 at = spot + Vector3.up;
            GameObject cut = null;
            float r = await Reach(() => { SpawnPooled("NamelessCut", at, Quaternion.LookRotation(f), 0.28f, go => cut = go); }, at);
            sb.AppendLine($"  무형일섬 2단계 공간 베기 NamelessCut ×0.28: {r:0.00} m (판정 반경 칸 2 m)");
            if (cut != null) Managers.ObjectPooler.Despawn(cut);
        }

        // ④ B3 · B6 — 무형검 「형 없는 연타 III」: 발동 · 맞힘 · 분신 사진
        Managers.UI?.CloseAllPopupUI(); Time.timeScale = 1f;
        if (!await EquipAsync(p, Nameless, ct)) { sb.AppendLine("무형검 장착 실패"); return; }
        var w = p.WeaponManager.Weapon0Data; w.formTier = 0; w.enhanceLevel = 0; w.augments = "aug_nameless_formless:3"; w.RecomputeEnhancedStats();
        WeaponFormRuntime.Of(p).Rebuild(); GameRunBootstrapper.Instance.Run.EffectManager.Rebuild();
        GoHome(p, open, openRot);
        await Wait(0.5f, ct);
        DetachAll();
        s_home = p.transform.position;
        var t = await TrackAsync(s_home + f * 1.8f, ct);
        if (t == null) { sb.AppendLine("표적 실패"); return; }
        HideStrays();
        for (float q = 0f; q < 1.2f; q += 0.1f) { RefillAll(); Steer(p, t.M.transform.position); await Wait(0.1f, ct); }
        var camGo = new GameObject("~RuneHit83Cam");
        var cam = camGo.AddComponent<Camera>();
        if (Camera.main != null) cam.CopyFrom(Camera.main);
        cam.enabled = false; cam.fieldOfView = 50f;
        int fires = 0, hitFires = 0, shots = 0; float shootAt = -1f;
        Application.LogCallback onLog = (msg, _, __) =>
        {
            if (!msg.StartsWith("[MeleeAugment] 형 없는 연타")) return;
            fires++;
            int k = msg.LastIndexOf(' ');
            if (k >= 0 && int.TryParse(msg.Substring(k + 1), out int n) && n > 0) hitFires++;
            if (shootAt < 0f) shootAt = Time.time;
        };
        Application.logMessageReceived += onLog;
        float t0 = Time.time; float dealt0 = t.Dealt;
        try
        {
            while (Time.time - t0 < 5f)
            {
                RefillAll(); Steer(p, t.M.transform.position); p.InputBuffer.Push(Command.Light);
                await UniTask.Yield(PlayerLoopTiming.PostLateUpdate, ct);
                if (shootAt > 0f && shots < 6 && Time.time - shootAt >= 0.06f * (shots % 3 + 1))
                {
                    Vector3 pos = p.transform.position;
                    cam.transform.position = pos + right * 3.2f + f * 2.2f + Vector3.up * 2.0f;
                    cam.transform.LookAt(pos + Vector3.up * 1.0f + f * 0.8f);
                    CaptureMain(cam, $"Temp/ui_shots/GH83_{shots:00}.png");
                    shots++;
                    if (shots % 3 == 0) shootAt = -1f;
                }
            }
        }
        finally { Application.logMessageReceived -= onLog; p.InputBuffer.Clear(); UnityEngine.Object.Destroy(camGo); }
        var ghost = p.GetComponent<PlayerGhostStrike>();
        sb.AppendLine($"\n■ 형 없는 연타 III: 5초 발동 {fires} · 맞힌 발동 {hitFires} · 표적 받은 피해 {t.Dealt - dealt0:0} · 분신 {(ghost != null ? "있음" : "없음")} · 사진 {shots}");

        // ⑤ 무형일섬 2단계(+6) — 마무리 베기가 맞히나(막타 · 시각)
        w.augments = ""; w.enhanceLevel = 6; w.RecomputeEnhancedStats();
        GameRunBootstrapper.Instance.Run.EffectManager.Rebuild();
        DetachAll();
        GoHome(p, open, openRot);
        await Wait(0.5f, ct);
        s_home = p.transform.position;
        var far = await TrackAsync(s_home + f * 7.5f, ct);
        HideStrays();
        for (float q = 0f; q < 1.2f; q += 0.1f) { RefillAll(); Steer(p, s_home + f * 9f); await Wait(0.1f, ct); }
        var rhits = new StringBuilder();
        float r0 = Time.time;
        Action<HitInfo> onH = h => { if (h.Attacker == p.gameObject) rhits.Append($" {Time.time - r0:0.00}{(h.IsFinisher ? "막" : "")}"); };
        HitFeedbackService.OnHit += onH;
        try
        {
            p.CooldownTracker.ResetCooldown(SkillType.R);
            p.InputBuffer.Push(Command.RSkill);
            while (Time.time - r0 < 3f) { RefillAll(); await UniTask.Yield(PlayerLoopTiming.Update, ct); }
        }
        finally { HitFeedbackService.OnHit -= onH; }
        sb.AppendLine($"■ 무형일섬 {SkillTierResolver.Resolve(w)}단계: 적중 시각{rhits}");
        w.enhanceLevel = 0; w.RecomputeEnhancedStats();
        DetachAll();
    }

    private static async void SpawnPooled(string key, Vector3 at, Quaternion rot, float scale, Action<GameObject> got)
    {
        var go = await Managers.ObjectPooler.SpawnAsync(key, ObjectPoolerManager.PoolType.Effect, at, rot);
        if (go == null) return;
        go.transform.localScale = Vector3.one * scale;
        got?.Invoke(go);
    }

    // 84 — 10-22 B7 쉼터 무료 재련: 근접(칸 0) + 원거리(칸 1)를 들고 쉼터 조작기를 세워 원거리 재련이 고를 수 있게 나오나 · 공짜인가
    private static async UniTask RestRanged84Async(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        sb.AppendLine("84 — B7 쉼터 무료 재련 근접 · 원거리(근접 든 채 들어옴)");
        await Wait(3f, ct);
        Managers.UI?.CloseAllPopupUI(); Time.timeScale = 1f;
        if (!await EquipAsync(p, Katana, ct)) { sb.AppendLine("카타나 장착 실패"); return; }
        if (!await EquipAsync(p, Bow, ct, PlayerWeaponManager.Slot1)) { sb.AppendLine("활 장착 실패"); return; }
        p.WeaponManager.Weapon0Data.enhanceLevel = 0;
        await Wait(0.5f, ct);
        sb.AppendLine($"  들고 있는 칸 {p.WeaponManager.GetCurrentSlotIndex()} · 칸 1 원거리 {SkillTierResolver.IsRanged(p.WeaponManager.Weapon1Data)}");

        var room = new GameObject("~RestProbe84");
        new GameObject("RestAnvil").transform.SetParent(room.transform, false);
        var ctrl = room.AddComponent<RestRoomController>();
        try
        {
            ctrl.Initialize(GameRunBootstrapper.Instance.Run, null, null);
            object[] args = { null };
            bool can = (bool)typeof(RestRoomController).GetMethod("CanForge", Inst).Invoke(ctrl, args);
            string detail = (args[0] as string ?? "").Replace("\n", " / ");
            Check(sb, $"무료 재련 가능 · 두 줄 「{detail}」", can && detail.Contains("무기 재련") && detail.Contains("파츠 재련"));

            int fuel0 = GameRunBootstrapper.Instance.Run.FuelBank?.EnhanceMaterial ?? -1;
            int total0 = RangedPartsState.Current.TotalLevel;
            typeof(RestRoomController).GetMethod("Forge", Inst).Invoke(ctrl, null);
            await Wait(0.3f, ct);
            Check(sb, "둘 다 되면 「무엇을 재련할까」 고르기 창", UI_ConfirmDialog.IsOpen);
            Managers.UI?.CloseAllPopupUI();
            await Wait(0.2f, ct);
            typeof(RestRoomController).GetMethod("ForgeSlot", Inst).Invoke(ctrl, new object[] { PlayerWeaponManager.Slot1 });
            await Wait(0.6f, ct);
            int offered = RangedPartsDraft.Offered.Count;
            int fuel1 = GameRunBootstrapper.Instance.Run.FuelBank?.EnhanceMaterial ?? -1;
            Check(sb, $"파츠 재련 — 카드 {offered}장 · 연료 {fuel0} → {fuel1}(공짜)", RangedPartsDraft.IsOpen && offered >= 3 && fuel0 == fuel1);
            if (RangedPartsDraft.IsOpen) RangedPartsDraft.Choose(0);
            await Wait(0.3f, ct);
            Check(sb, $"카드 고름 → 파츠 합 {total0} → {RangedPartsState.Current.TotalLevel}", RangedPartsState.Current.TotalLevel > total0);
            Managers.UI?.CloseAllPopupUI();
        }
        finally { UnityEngine.Object.Destroy(room); }
    }

    // 94 — 전설 룬 하나를 판에 놓고 그 존 단계를 정한다(e2 화면 확인 — 카드 각성 줄 · HUD 눈금 · 각성 띠) · 정리하지 않는다
    private static async UniTask Legend94Async(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        string type = "FireLegendSingle";
        int tier = 2;
        if (File.Exists("Temp/legend94.txt"))
        {
            var t = File.ReadAllText("Temp/legend94.txt").Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (t.Length > 0) type = t[0];
            if (t.Length > 1) int.TryParse(t[1], out tier);
        }
        tier = Mathf.Clamp(tier, 1, 4);
        sb.AppendLine($"94 — 전설 룬 {type} · 존 {tier}단계");
        await Wait(2f, ct);
        var inv = GameRunBootstrapper.Instance.Run.ItemInventory;
        RuntimeItemData item = null;
        foreach (var entry in Managers.ItemData.GetAllItems().Values)
        {
            var d = RuntimeItemData.FromServer(entry);
            if (d?.effects != null && d.effects.Exists(e => e != null && e.effectType == type)) { item = d; break; }
        }
        if (item == null) { sb.AppendLine("  그 effect_type의 룬이 표에 없다"); return; }
        inv.AddToStaging(item);
        if (UI_GridPanel.Instance == null) Managers.UI?.ShowOverlayUI<UI_GridPanel>();
        var panel = UI_GridPanel.Instance;
        panel.ShowWithNewItem(null);
        await UniTask.Delay(1500, ignoreTimeScale: true, cancellationToken: ct);
        bool placed = PlaceRune(panel, item);
        await UniTask.Delay(400, ignoreTimeScale: true, cancellationToken: ct);
        string zone = item.element;
        int got = SetZoneTier(zone, tier, keepOccupied: true);
        await UniTask.Delay(400, ignoreTimeScale: true, cancellationToken: ct);
        string hud = "";
        foreach (var key in new[] { "heat", "dark", "orb", "soul" })
            if (LegendaryHud.TryGet(key, out int v, out int m)) hud += $" {key} {v}/{m}";
        sb.AppendLine($"  {item.displayName}({zone} · {RuneCardKit.CellCount(item)}칸) 놓임 {placed} · 존 점유 {got} · 단계 {LegendaryTuning.Tier(zone)} · 각성 {LegendaryTuning.Awakened(zone)} · HUD{(hud.Length > 0 ? hud : " 없음")}");
        sb.AppendLine("  정리하지 않는다 — 룬판 · HUD를 그대로 촬영하세요(플레이를 멈추면 원래대로).");
    }

    /// <summary>칸 누르기와 같은 길 — 판의 빈 칸마다 TryPlaceShapeAt를 시도해 처음 놓이는 곳에 놓는다(전설은 제 속성 존에만 들어간다).</summary>
    private static bool PlaceRune(UI_GridPanel panel, RuntimeItemData item)
    {
        var staging = typeof(UI_GridPanel).GetField("_stagingArea", Inst)?.GetValue(panel) as StagingAreaView;
        var shape = BoardManager.Instance?.GetSharedShapeByItem(item.instanceId) ?? staging?.GetShapeForItem(item);
        var gm = GridManager.Instance;
        if (shape == null || gm?.grid == null) return false;
        var host = BoardManager.Instance?.gridHost;
        if (host != null && shape.transform.parent != host) { shape.transform.SetParent(host, false); shape.transform.localScale = Vector3.one; }
        foreach (var sq in gm.grid.GetGridSquares())
        {
            if (sq == null || sq.isOccupied) continue;
            if (gm.TryPlaceShapeAt(shape, sq)) { BoardManager.Instance?.OnShapePlaced(shape); gm.ClearPreview(); return true; }
        }
        BoardManager.Instance?.ReSlotAndReturn(shape);
        return false;
    }

    /// <summary>그 존 칸을 단계 문턱까지 점유로 둔다(룬판 보기 — 이어하기와 같은 입구). keepOccupied면 지금 놓인 칸을 남긴다. 그 존 점유 수를 돌려준다.</summary>
    private static int SetZoneTier(string zone, int tier, bool keepOccupied)
    {
        var view = UnityEngine.Object.FindFirstObjectByType<MerlinRuneHexGridView>(FindObjectsInactive.Include);
        if (view == null) return -1;
        var zones = typeof(MerlinRuneHexGridView).GetField("_cellZones", Inst)?.GetValue(view) as Dictionary<Vector2Int, char>;
        var occ = typeof(MerlinRuneHexGridView).GetField("_occupiedPositions", Inst)?.GetValue(view) as HashSet<Vector2Int>;
        var entries = Managers.RuneData?.GetZoneSynergies(zone);
        if (zones == null || entries == null) return -1;
        var thresholds = new List<int>();
        foreach (var e in entries) if (e != null) thresholds.Add(e.threshold);
        thresholds.Sort();
        int want = tier <= 0 || thresholds.Count == 0 ? 0 : thresholds[Mathf.Min(tier, thresholds.Count) - 1];
        var cells = keepOccupied && occ != null ? new List<Vector2Int>(occ) : new List<Vector2Int>();
        int have = 0;
        foreach (var c in cells) if (zones.TryGetValue(c, out var z) && ElementDef.CodeToId(z) == zone) have++;
        foreach (var kv in zones)
        {
            if (have >= want) break;
            if (ElementDef.CodeToId(kv.Value) != zone || cells.Contains(kv.Key)) continue;
            cells.Add(kv.Key);
            have++;
        }
        view.RestoreOccupiedCells(cells);
        return have;
    }

    // 95 — 전설 룬 개편 1차 화력 몫: 축 시나리오 × 존 2 · 4단계 × 전설 없음/있음(같은 단계 룬 시너지가 함께 켜진다 — 전설 몫만 가른다)
    private sealed class LegendCase
    {
        public string Name, Type, Trigger, Zone, Weapon;
        public float V, V2, V3;
        public int Targets = 4;
        public int Slot = PlayerWeaponManager.Slot0;
        public bool Skills, Dodge, Hurt, Kills, Perfect;
    }

    private static async UniTask Legend95Async(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        sb.AppendLine("95 — 전설 룬 개편 1 · 2차 화력 몫(존 2 · 4단계 · 전설 없음 → 있음 · 6초 ABBA · 목표 존2 +25~35% · 각성 +50%)");
        s_shotTaken.Clear();
        await Wait(3f, ct);
        foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
            if (m != null && !m.IsDead) m.gameObject.SetActive(false);
        Vector3 open = p.transform.position + Flat(p.transform.forward) * 8f;
        Quaternion openRot = Quaternion.LookRotation(Flat(p.transform.forward));
        typeof(PlayerController).GetField("debugInvincible", Inst)?.SetValue(p, true);
        var cases = new[]
        {
            new LegendCase { Name = "얼려 깨는 주문", Type = "IceLegendAoe", Trigger = "OnSkillFreeze", Zone = "ICE", Weapon = Katana, V = 2f, V2 = 1.5f, V3 = 0.8f, Skills = true },
            new LegendCase { Name = "서리 처형(창)", Type = "IceLegendSingle", Trigger = "OnCritExecute", Zone = "ICE", Weapon = Great, V = 1f, V2 = 0.15f, V3 = 6f },
            new LegendCase { Name = "얼음 무대", Type = "IceLegendField", Trigger = "OnDashStage", Zone = "ICE", Weapon = Katana, V = 1.5f, V2 = 0.2f, V3 = 2.5f, Dodge = true },
            new LegendCase { Name = "번개 사슬", Type = "ElecLegendAoe", Trigger = "OnHitChain", Zone = "ELECTRIC", Weapon = Katana, V = 0.04f, V2 = 15f, V3 = 3f },
            new LegendCase { Name = "축전 포박", Type = "ElecLegendSingle", Trigger = "OnCritStore", Zone = "ELECTRIC", Weapon = Great, V = 1.5f, V2 = 6f, V3 = 0.4f },
            new LegendCase { Name = "포자 전염", Type = "GrassLegendAoe", Trigger = "OnHitSpread", Zone = "GRASS", Weapon = Katana, V = 0.4f, V2 = 0.04f, V3 = 3f },
            new LegendCase { Name = "빛의 반격", Type = "LightLegendAoe", Trigger = "OnHurtShield", Zone = "LIGHT", Weapon = Katana, V = 0.5f, V2 = 6f, V3 = 0.12f, Hurt = true },
            new LegendCase { Name = "심판의 표식(단일)", Type = "LightLegendSingle", Trigger = "OnCritMark", Zone = "LIGHT", Weapon = Great, V = 1.6f, V2 = 5f, V3 = 8f, Targets = 1 },
            new LegendCase { Name = "보복의 심연", Type = "DarkLegendAoe", Trigger = "OnHurtStore", Zone = "DARK", Weapon = Katana, V = 3f, V2 = 0.6f, V3 = 0.3f, Hurt = true },
            new LegendCase { Name = "불사조의 맥박(단일)", Type = "FireLegendSingle", Trigger = "OnHitHeat", Zone = "FIRE", Weapon = Katana, V = 6f, V2 = 20f, V3 = 1.2f, Targets = 1 },
            // 2차(10-24) — 처치는 1.2초마다 흉내(체력을 채워 실제로는 안 쓰러진다) · 저스트 회피는 2.5초마다 · 무리 칸은 대검(카타나 무리는 같은 조건이 1.5배 흔들렸다)
            new LegendCase { Name = "불바다", Type = "FireLegendAoe", Trigger = "OnKillPool", Zone = "FIRE", Weapon = Great, V = 0.35f, V2 = 4f, V3 = 2.5f, Kills = true },
            new LegendCase { Name = "불꽃 질주", Type = "FireLegendProjectile", Trigger = "OnDashTrail", Zone = "FIRE", Weapon = Great, V = 0.2f, V2 = 1.3f, V3 = 2f, Dodge = true },
            new LegendCase { Name = "뇌전 탄창", Type = "ElecLegendProjectile", Trigger = "OnSkillOrb", Zone = "ELECTRIC", Weapon = Great, V = 1.2f, V2 = 3f, V3 = 0.1f, Skills = true, Targets = 1 },
            new LegendCase { Name = "독안개 사수(활)", Type = "GrassLegendProjectile", Trigger = "OnRangedMist", Zone = "GRASS", Weapon = Bow, Slot = PlayerWeaponManager.Slot1, V = 0.35f, V2 = 6f, V3 = 2.5f },
            new LegendCase { Name = "호위 성검(활)", Type = "LightLegendProjectile", Trigger = "OnRangedGuard", Zone = "LIGHT", Weapon = Bow, Slot = PlayerWeaponManager.Slot1, V = 0.6f, V2 = 8f, V3 = 2f },
            new LegendCase { Name = "영혼 수확", Type = "DarkLegendProjectile", Trigger = "OnKillSoul", Zone = "DARK", Weapon = Great, V = 0.8f, V2 = 8f, V3 = 0.4f, Kills = true, Skills = true },
            new LegendCase { Name = "그림자 걸음", Type = "DarkLegendSingle", Trigger = "OnDodgeShadow", Zone = "DARK", Weapon = Great, V = 0.8f, V2 = 8f, V3 = 2.5f, Perfect = true, Targets = 1 },
        };
        string curWeapon = null;
        var only = File.Exists("Temp/legend95_only.txt") ? new HashSet<string>(File.ReadAllLines("Temp/legend95_only.txt")) : null;
        try
        {
            foreach (var c in cases)
            {
                if (only != null && only.Count > 0 && !only.Contains(c.Type)) continue;
                if (curWeapon != c.Weapon)
                {
                    if (!await EquipAsync(p, c.Weapon, ct, c.Slot)) { sb.AppendLine($"  장착 실패 {c.Weapon}"); continue; }
                    var w = p.WeaponManager.Weapon0Data;
                    w.augments = ""; w.formTier = 0; w.enhanceLevel = 0; w.RecomputeEnhancedStats();
                    WeaponFormRuntime.Of(p).Rebuild(); GameRunBootstrapper.Instance.Run.EffectManager.Rebuild();
                    curWeapon = c.Weapon;
                }
                int atk = p.RuntimeStats.GetEffectiveAttack(p.WeaponManager.CurrentWeaponData.weaponType.GetAttackStatKind());
                string line = $"  {c.Name}(공격력 {atk} · {c.Zone} · {(c.Targets > 1 ? "무리 " + c.Targets : "단일")}{(c.Skills ? " · 스킬" : "")}{(c.Dodge ? " · 대시" : "")}{(c.Hurt ? " · 피격" : "")}{(c.Kills ? " · 처치" : "")}{(c.Perfect ? " · 저스트" : "")}):";
                int[] tiers = File.Exists("Temp/legend95_tiers.txt") ? Array.ConvertAll(File.ReadAllText("Temp/legend95_tiers.txt").Trim().Split(','), int.Parse) : new[] { 2, 4 };
                foreach (int tier in tiers)
                {
                    float a1 = await Legend95Once(p, c, tier, false, open, openRot, ct);
                    float b1 = await Legend95Once(p, c, tier, true, open, openRot, ct);
                    float b2 = await Legend95Once(p, c, tier, true, open, openRot, ct);
                    float a2 = await Legend95Once(p, c, tier, false, open, openRot, ct);
                    float a = (a1 + a2) * 0.5f, b = (b1 + b2) * 0.5f;
                    line += $" 존{tier} {a:0} → {b:0}({(a > 0f ? (b / a - 1f) * 100f : 0f):+0;-0}% · 없음 {a1:0}/{a2:0} 있음 {b1:0}/{b2:0}){s_diag95}";
                }
                sb.AppendLine(line);
                Debug.Log("[WeaponFormProbe] 95 " + line);
            }
        }
        finally
        {
            MerlinRuneBridge.Instance?.OnZoneCellsUpdated(new Dictionary<string, int>());
            typeof(PlayerController).GetField("debugInvincible", Inst)?.SetValue(p, false);
            Time.timeScale = 1f;
            DetachAll();
        }
    }

    // 96 — 평타 꾹 누르기(10-25 ④): 왼 클릭을 넣어(게임 창 포커스와 무관하게) 휘두름(효과 단계) 수를 센다
    private static async UniTask HoldAttack96Async(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        sb.AppendLine("96 — 평타 꾹 누르기(짧게 1번 · 꾹 2.5초 · 뗀 뒤 멈춤 · 연타 대조 · 멈춘 화면 · 활)");
        await Wait(3f, ct);
        foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
            if (m != null && !m.IsDead) m.gameObject.SetActive(false);
        Managers.UI?.CloseAllPopupUI(); Time.timeScale = 1f;
        var mouse = Mouse.current;
        if (mouse == null) { sb.AppendLine("  마우스 없음"); return; }
        var editorMode0 = InputSystem.settings.editorInputBehaviorInPlayMode;
        var background0 = InputSystem.settings.backgroundBehavior;
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        var center = new Vector2(Screen.width * 0.5f, Screen.height * 0.6f);   // 화면 가운데 위 — 구석(0, 0)은 HUD 위라 공격 입력이 버려진다
        MouseState Up() => new MouseState { position = center };
        MouseState Down() => new MouseState { position = center }.WithButton(MouseButton.Left);
        int steps = 0;
        Action<int> onStep = _ => steps++;
        PlayerAnimationEventReceiver recv = null;
        try
        {
            async UniTask Hide(float seconds)   // 실시간 seconds 동안 매 프레임 떠도는 몬스터를 숨긴다(새로 불린 몬스터의 오류가 Error Pause로 시간을 멈췄다)
            {
                float t0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - t0 < seconds) { HideStrays(); await UniTask.Yield(PlayerLoopTiming.Update, ct); }
            }
            async UniTask Equip(string weapon, int slot)
            {
                await EquipAsync(p, weapon, ct, slot);
                await Hide(1f);
                if (recv != null) recv.OnEffectStepEarly -= onStep;
                recv = p.GetComponentInChildren<PlayerAnimationEventReceiver>();
                if (recv != null) recv.OnEffectStepEarly += onStep;
            }
            async UniTask<(int during, int after)> Hold(float seconds)
            {
                InputSystem.QueueStateEvent(mouse, Up());
                p.InputBuffer.Clear();
                await Hide(1f);
                steps = 0;
                InputSystem.QueueStateEvent(mouse, Down());
                await Hide(seconds);
                InputSystem.QueueStateEvent(mouse, Up());
                int during = steps;
                await Hide(1.5f);
                return (during, steps - during);
            }
            async UniTask<int> Mash(float seconds)
            {
                p.InputBuffer.Clear();
                await Hide(1f);
                steps = 0;
                float t0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - t0 < seconds) { p.InputBuffer.Push(Command.Light); await Hide(0.1f); }
                return steps;
            }

            await Equip(Katana, PlayerWeaponManager.Slot0);
            var tap = await Hold(0.08f);
            Check(sb, $"① 카타나 짧게 누름(0.08초) — 휘두름 {tap.during + tap.after}(누르는 동안 {tap.during} · 뗀 뒤 {tap.after})", tap.during + tap.after == 1);
            var hold = await Hold(2.5f);
            int mash = await Mash(2.5f);
            Check(sb, $"② 카타나 꾹 2.5초 — 누르는 동안 {hold.during} · 뗀 뒤 {hold.after} · 연타 대조 {mash}", hold.during >= 4 && hold.during >= mash * 0.85f && hold.after <= 2);
            // ③ 멈춘 화면(시간 0 — 룬판 · ESC 등)에서 누르고 떼면 풀린 뒤 평타가 나가면 안 된다(ca RouteInputs 버리기 조건과 함께 · 10-25)
            InputSystem.QueueStateEvent(mouse, Up());
            p.InputBuffer.Clear();
            await Hide(1f);
            steps = 0;
            Time.timeScale = 0f;
            InputSystem.QueueStateEvent(mouse, Down());
            await Hide(0.6f);
            InputSystem.QueueStateEvent(mouse, Up());
            await Hide(0.2f);
            Time.timeScale = 1f;
            await Hide(1.2f);
            Check(sb, $"③ 멈춘 화면에서 누름 0.6초 → 풀린 뒤 휘두름 {steps}(0이어야 · ca 버리기 조건이 들어간 뒤)", steps == 0);

            await Equip(Bow, PlayerWeaponManager.Slot1);
            var bowTap = await Hold(0.08f);
            var bowHold = await Hold(2.5f);
            int bowMash = await Mash(2.5f);
            Check(sb, $"④ 활 짧게 {bowTap.during + bowTap.after} · 꾹 2.5초 {bowHold.during}(뗀 뒤 {bowHold.after}) · 연타 대조 {bowMash}", bowTap.during + bowTap.after == 1 && bowHold.during >= 2 && bowHold.during >= bowMash * 0.85f && bowHold.after <= 2);
        }
        finally
        {
            if (recv != null) recv.OnEffectStepEarly -= onStep;
            Time.timeScale = 1f;
            InputSystem.QueueStateEvent(mouse, new MouseState());
            InputSystem.settings.editorInputBehaviorInPlayMode = editorMode0;
            InputSystem.settings.backgroundBehavior = background0;
            DetachAll();
        }
    }

    // 97 — 무기 개편(10-26 · 설계 기획/RelicFairy_무기개편_카타나·대검_설계_20261025.md · 계획 구현설계/RelicFairy_무기개편_구현계획_20261026.md)
    // 칸 번호를 Temp/weapon97_only.txt에 적으면 그 칸만(예: 「1 3」) · 없으면 전부
    private static async UniTask Weapon97Async(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        sb.AppendLine("97 — 무기 개편 실측(10-26)");
        await Wait(3f, ct);
        foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
            if (m != null && !m.IsDead) m.gameObject.SetActive(false);
        Managers.UI?.CloseAllPopupUI(); Time.timeScale = 1f;
        string only = File.Exists("Temp/weapon97_only.txt") ? File.ReadAllText("Temp/weapon97_only.txt") : "";
        bool Run(string n) => only.Trim().Length == 0 || only.Contains(n);
        try
        {
            if (Run("1")) W97Removed(sb);
            if (Run("2")) W97Growth(sb);
            if (Run("3")) await W97Shadow(p, sb, ct);
            if (Run("4")) await W97Gust(p, sb, ct);
            if (Run("5")) await W97Reach(p, sb, ct);
            if (Run("6")) await W97Giant(p, sb, ct);
            if (Run("7")) await W97Zones(p, sb, ct);
            if (Run("8")) await W97ZonePhotos(p, sb, ct);
            if (Run("9")) await W97Blade(p, sb, ct);
            if (Run("10")) await W97RSkill(p, sb, ct);
            if (Run("11")) await W97RangeRules(p, sb, ct);
            if (Run("12")) await W97ESword(p, sb, ct);
            // W97_NEXT
        }
        finally { Time.timeScale = 1f; DetachAll(); }
    }

    private static WeaponData W97Weapon(string path, int enhance)
    {
        var w = WeaponData.FromSO(AssetDatabase.LoadAssetAtPath<WeaponSO>(path));
        w.enhanceLevel = enhance;
        return w;
    }

    // ① 걷은 「추가 검기」 증강(D1 · D2) — 정의 없음 · 제시 안 됨 · 옛 세이브 정리 · 안개 칼날은 진화 뒤 환영으로 남음
    private static void W97Removed(StringBuilder sb)
    {
        int defs = 0;
        foreach (var id in MeleeAugmentCatalog.RemovedIds) if (MeleeAugmentCatalog.Get(id) != null) defs++;
        Check(sb, $"①-a 걷은 증강 {MeleeAugmentCatalog.RemovedIds.Count}개(추가 검기 10 + 레거시 정리 3) — 남은 정의 {defs}", MeleeAugmentCatalog.RemovedIds.Count == 13 && defs == 0);

        var draw   = typeof(MeleeAugmentDraft).GetMethod("Draw", BindingFlags.NonPublic | BindingFlags.Static);
        var legend = typeof(MeleeAugmentDraft).GetMethod("TryPickLegend", BindingFlags.NonPublic | BindingFlags.Static);
        int offered = 0, removed = 0;
        foreach (var path in new[] { Katana, Great, Nameless })
        {
            var w = W97Weapon(path, 12);
            var rng = new System.Random(97);
            for (int i = 0; i < 200; i++)
                foreach (var tier in new[] { AugmentTier.Silver, AugmentTier.Gold, AugmentTier.Prismatic })
                {
                    var cards = (List<MeleeAugmentCard>)draw.Invoke(null, new object[] { w, tier, rng, 3, null });
                    foreach (var c in cards) { offered++; if (MeleeAugmentCatalog.RemovedIds.Contains(c.Id)) removed++; }
                    var args = new object[] { w, rng, cards, null };
                    if ((bool)legend.Invoke(null, args)) { offered++; if (MeleeAugmentCatalog.RemovedIds.Contains(((MeleeAugmentCard)args[3]).Id)) removed++; }
                }
        }
        Check(sb, $"①-b 카타나 · 대검 · 무형검 제시 {offered}장 — 걷은 증강 {removed}장", offered > 0 && removed == 0);

        var s = W97Weapon(Katana, 12);
        s.augments = "aug_sword_wave:2,aug_ricochet:1,aug_katana_gale:1";
        s.reforgeLog = "aug_sword_wave:1,aug_katana_gale:1";
        s.SanitizeAugments();
        Check(sb, $"①-c 옛 세이브 정리 — 증강 「{s.augments}」 · 재련 기록 「{s.reforgeLog}」", s.augments == "aug_katana_gale:1" && s.reforgeLog == "aug_sword_wave:1,aug_katana_gale:1");

        var k = W97Weapon(Katana, 12);
        k.evolutionStage = 1;
        bool mist = MeleeAugmentCatalog.RunsAsPhantom(MeleeAugmentCatalog.Get("aug_nameless_mist"), k);
        Check(sb, $"①-d 진화 카타나의 안개 칼날 — 환영 몫 {mist}", mist);

        // ①-e 레거시 정리(10-26) — 옛 세이브의 지진 · 거인의 걸음(대검) · 칼자국(카타나)은 같은 등급 · 같은 무기의 없는 증강 I로
        var g = W97Weapon(Great, 12);
        g.augments = "aug_great_quake:3,aug_great_giant:1,aug_great_sweep:1,aug_great_bulwark:2";
        g.SanitizeAugments();
        var ks = W97Weapon(Katana, 12);
        ks.augments = "aug_blade_scar:3,aug_ambush:1";
        ks.SanitizeAugments();
        string gTier = "", kTier = "";
        foreach (var (id, rk) in g.AugmentList()) { var d = MeleeAugmentCatalog.Get(id); gTier += $" {d?.Name}({(d != null ? MeleeAugmentDef.TierName(d.Tier) : "?")} {rk})"; }
        foreach (var (id, rk) in ks.AugmentList()) { var d = MeleeAugmentCatalog.Get(id); kTier += $" {d?.Name}({(d != null ? MeleeAugmentDef.TierName(d.Tier) : "?")} {rk})"; }
        bool gOk = g.augments.Split(',').Length == 4 && g.AugmentRank(MeleeAugmentCatalog.GreatFinal) == 1 && g.AugmentRank("aug_great_quake") == 0 && g.AugmentRank("aug_great_giant") == 0;
        bool kOk = ks.augments.Split(',').Length == 2 && ks.AugmentRank("aug_blade_scar") == 0 && ks.AugmentRank("aug_ambush") == 1;
        Check(sb, $"①-e 옛 세이브 바꿈 — 대검:{gTier} · 카타나:{kTier}", gOk && kOk);

        // ①-f 칼집 진화 변환(10-26 사용자 「무작위」) — 칼집 III = 3장 · 카타나 전용 · 전설 없음 · 서로 다름 · 30번에 여러 가지
        var nm = W97Weapon(Nameless, 9);
        nm.augments = MeleeAugmentCatalog.NamelessSheath + ":3";
        var seen = new HashSet<string>();
        bool shapeOk = true;
        for (int i = 0; i < 30; i++)
        {
            var conv = MeleeAugmentCatalog.ConvertForEvolution(nm, k);
            var ids = new HashSet<string>();
            foreach (var part in conv.Split(','))
            {
                var id = part.Split(':')[0];
                var d = MeleeAugmentCatalog.Get(id);
                if (d == null || d.Weapon != MeleeWeaponKind.Katana || d.IsLegend) shapeOk = false;
                ids.Add(id); seen.Add(id);
            }
            if (ids.Count != 3) shapeOk = false;
        }
        var names = new StringBuilder();
        foreach (var id in seen) names.Append(' ').Append(MeleeAugmentCatalog.Get(id)?.Name);
        Check(sb, $"①-f 칼집 III 진화 변환 30번 — 모양 {shapeOk} · 나온 전용 {seen.Count}가지:{names}", shapeOk && seen.Count >= 4);

        // ①-g 속성 검 연장 제시(10-26 결함) — 대검 금 화면 200번에 한 번 이상
        var gd = W97Weapon(Great, 12);
        var drawG = typeof(MeleeAugmentDraft).GetMethod("Draw", BindingFlags.NonPublic | BindingFlags.Static);
        var rngG = new System.Random(26);
        int fin = 0;
        for (int i = 0; i < 200; i++)
            foreach (var c in (List<MeleeAugmentCard>)drawG.Invoke(null, new object[] { gd, AugmentTier.Gold, rngG, 2, null }))
                if (c.Id == MeleeAugmentCatalog.GreatFinal) fin++;
        Check(sb, $"①-g 대검 금 화면 200번 — 속성 검 연장 {fin}장(≥ 1)", fin >= 1);
    }

    // ② 재련 자람(D9) — 그림자 30 + 6n · 검풍 15 + 4n / 60 + 5n · 다음 재련 값 · 강화 카드 없음
    private static void W97Growth(StringBuilder sb)
    {
        var sh = MeleeAugmentCatalog.Get(MeleeAugmentCatalog.KatanaShadow);
        var gu = MeleeAugmentCatalog.Get(MeleeAugmentCatalog.KatanaGust);
        string vals = "";
        bool ok = sh != null && gu != null && sh.IsGrowth && gu.IsGrowth;
        foreach (var (lv, s, c, d) in new[] { (9, 12f, 15f, 60f), (15, 16f, 23f, 70f), (21, 20f, 31f, 80f) })   // 10-26 그림자 분신당 12 + 2n(분신 수가 재련으로 는다)
        {
            var w = W97Weapon(Katana, lv);
            float vs = sh != null ? sh.ValueFor(w) : -1f, vc = gu != null ? gu.ValueFor(w) : -1f, vd = gu != null ? gu.Value2For(w) : -1f;
            vals += $" +{lv}: {vs:0}/{vc:0}/{vd:0}";
            ok &= Mathf.Approximately(vs, s) && Mathf.Approximately(vc, c) && Mathf.Approximately(vd, d);
        }
        Check(sb, $"②-a 자람 값(그림자/검풍 확률/검풍 피해){vals}", ok);
        MeleeAugmentCatalog.TryGrowth(MeleeAugmentCatalog.KatanaShadow, 15, out float n15, out float x15, out _, out _);
        MeleeAugmentCatalog.TryGrowth(MeleeAugmentCatalog.KatanaShadow, 21, out float n21, out float x21, out _, out _);
        Check(sb, $"②-b 값 질의 — +15 지금 {n15:0} → 다음 {x15:0} · +21 지금 {n21:0} → 다음 {x21:0}", Mathf.Approximately(n15, 16f) && Mathf.Approximately(x15, 18f) && Mathf.Approximately(x21, 20f));
        var up = typeof(MeleeAugmentDraft).GetMethod("TryPickUpgrade", BindingFlags.NonPublic | BindingFlags.Static);
        var kw = W97Weapon(Katana, 15);
        kw.augments = MeleeAugmentCatalog.KatanaShadow + ":1," + MeleeAugmentCatalog.KatanaGust + ":1";
        int growthUps = 0;
        var rng = new System.Random(5);
        for (int i = 0; i < 200; i++)
        {
            var args = new object[] { kw, AugmentTier.Prismatic, rng, new HashSet<string>(), null };
            if ((bool)up.Invoke(null, args) && MeleeAugmentCatalog.Get(((MeleeAugmentCard)args[4]).Id)?.IsGrowth == true) growthUps++;
        }
        Check(sb, $"②-c 자람 증강 강화 카드 — {growthUps}장(0이어야)", growthUps == 0);
    }

    /// <summary>실측 97 칸 하나 — 근접 무기를 쥐고 증강 · 강화 단계를 정한 뒤 효과를 다시 짠다.</summary>
    private static async UniTask<bool> W97Setup(PlayerController p, string weapon, string augments, int enhance, CancellationToken ct, int formTier = 1)
    {
        if (!await EquipAsync(p, weapon, ct)) return false;
        var w = p.WeaponManager.Weapon0Data;
        w.augments = augments; w.formTier = formTier; w.enhanceLevel = enhance; w.RecomputeEnhancedStats();
        p.WeaponManager.RaiseEquippedWeaponRefreshed();   // 공격력 등 실행 스탯을 새 강화로(재련소 RefreshEquippedIfCurrent와 같은 길 · 10-26 실측 99에서 +21 평타가 +9 공격력이던 것)
        WeaponFormRuntime.Of(p).Rebuild(); GameRunBootstrapper.Instance.Run.EffectManager.Rebuild();
        await Wait(0.5f, ct);
        return true;
    }

    /// <summary>실시간 seconds 동안 평타를 넣고 표적 쪽을 본다 · 매 프레임 떠도는 몬스터 숨김 · 표적 체력 채움.</summary>
    private static async UniTask W97Swing(PlayerController p, Vector3 aim, float seconds, CancellationToken ct, Action perFrame = null)
    {
        float t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < seconds)
        {
            RefillAll(); Steer(p, aim);
            if (p.InputBuffer.Count < 2) p.InputBuffer.Push(Command.Light);
            perFrame?.Invoke();
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
        }
        p.InputBuffer.Clear();
    }

    // ③ 그림자 파트너 — 보임 · 피해 몫(그 타격의 42% @ +15) · 적중 효과 없음 · 멈춘 화면 · 무기 바꿈
    private static async UniTask W97Shadow(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        if (!await W97Setup(p, Katana, MeleeAugmentCatalog.KatanaShadow + ":1", 15, ct, 0)) { sb.AppendLine("  ③ 카타나 장착 실패"); return; }   // 형(칼바람 풍절)이 평타 몫에 섞이지 않게
        s_home = p.transform.position;
        Vector3 fw = Flat(p.transform.forward);
        var t = await TrackAsync(s_home + fw * 2f, ct);
        HideStrays();
        await Wait(1f, ct);
        var sp = p.GetComponent<ShadowPartner>();
        bool shownNear = sp != null && sp.Shown;
        float shadowSum = 0f; int shadows = 0, hits = 0;
        Application.LogCallback onLog = (msg, st, type) =>
        {
            const string Tag = "[MeleeAugment] 그림자 — ";
            if (!msg.StartsWith(Tag)) return;
            string rest = msg.Substring(Tag.Length);
            int cut = rest.IndexOf(' ');
            if (cut > 0) rest = rest.Substring(0, cut);   // 「12.3 · 분신 1」 — 첫 숫자만(10-26 분신 여러 명)
            if (float.TryParse(rest, out float v)) { shadowSum += v; shadows++; }
        };
        Action<HitInfo> onHit = h => { if (h.Attacker == p.gameObject && h.ActionType == WeaponActionType.GroundLight) hits++; };
        Application.logMessageReceived += onLog;
        HitFeedbackService.OnHit += onHit;
        if (t != null) t.Dealt = 0f;
        float shotAt = Time.realtimeSinceStartup + 1.2f;
        bool shot = false;
        try
        {
            await W97Swing(p, t != null ? t.M.transform.position : s_home + fw * 2f, 10f, ct, () =>
            {
                if (shot || Time.realtimeSinceStartup < shotAt || Camera.main == null) return;
                shot = true;
                Directory.CreateDirectory("Temp/ui_shots");
                CaptureMain(Camera.main, "Temp/ui_shots/W97_shadow.png");
            });
            await Wait(0.4f, ct);
        }
        finally { Application.logMessageReceived -= onLog; HitFeedbackService.OnHit -= onHit; }
        float basic = (t != null ? t.Dealt : 0f) - shadowSum;
        float ratio = basic > 0f ? shadowSum / basic : 0f;
        Check(sb, $"③-a 그림자 보임 — 표적 있을 때 {shownNear}", shownNear);
        int clones = ShadowPartnerAugment.CloneCount(p.WeaponManager.Weapon0Data);
        float wantShare = clones * MeleeAugmentCatalog.Get(MeleeAugmentCatalog.KatanaShadow).ValueFor(p.WeaponManager.Weapon0Data) * 0.01f;
        Check(sb, $"③-b 그림자 몫 — {shadowSum:0} / 평타 {basic:0} = {ratio:0.00}(분신 {clones} × 몫 = {wantShare:0.00} ± 0.04)", Mathf.Abs(ratio - wantShare) <= 0.04f);
        Check(sb, $"③-c 평타 적중 {hits} · 그림자 {shadows} — 그림자가 적중 신호를 내지 않는다(≈ 1:{clones})", hits > 0 && shadows >= hits * clones * 0.9f && shadows <= hits * clones * 1.1f);
        var clones0 = sp != null ? typeof(ShadowPartner).GetField("_clones", Inst)?.GetValue(sp) as Array : null;   // 10-26 분신 여러 명 — 첫 분신
        var c0 = clones0 != null && clones0.Length > 0 ? clones0.GetValue(0) : null;
        var anim = c0?.GetType().GetField("anim")?.GetValue(c0) as Animator;
        float before = anim != null ? anim.GetCurrentAnimatorStateInfo(0).normalizedTime : -1f;
        p.InputBuffer.Push(Command.Light);
        await Wait(0.2f, ct);
        Time.timeScale = 0f;
        await Wait(0.2f, ct);   // 멈춘 그 프레임은 아직 한 칸 따라간다
        float paused0 = anim != null ? anim.GetCurrentAnimatorStateInfo(0).normalizedTime : -1f;
        await Wait(1f, ct);
        float paused1 = anim != null ? anim.GetCurrentAnimatorStateInfo(0).normalizedTime : -2f;
        Time.timeScale = 1f;
        Check(sb, $"③-d 멈춘 화면 1초 — 그림자 동작 시각 {paused0:0.000} → {paused1:0.000}(그대로)", anim != null && Mathf.Abs(paused1 - paused0) < 0.001f && before >= 0f);
        if (t != null) t.M.gameObject.SetActive(false);
        await EquipAsync(p, Great, ct);
        await Wait(0.8f, ct);
        Check(sb, $"③-e 대검으로 바꿈 — 그림자 보임 {(sp != null && sp.Shown)}", sp != null && !sp.Shown);
        DetachAll();
    }

    // ④ 검풍 — 확률 31% · 검기 피해 공격력 80% @ +21 · 분신 없음 · 풍절과 같은 휘두름엔 안 냄
    private static async UniTask W97Gust(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        if (!await W97Setup(p, Katana, MeleeAugmentCatalog.KatanaGust + ":1", 21, ct)) { sb.AppendLine("  ④ 카타나 장착 실패"); return; }
        s_home = p.transform.position;
        Vector3 fw = Flat(p.transform.forward);
        var t = await TrackAsync(s_home + fw * 2f, ct);
        HideStrays();
        await Wait(0.5f, ct);
        int swings = 0, gusts = 0, ghosts = 0, winds = 0, skips = 0, rolls = 0;
        float gustShotAt = -1f;
        bool gustShot = false;
        float dmgSum = 0f, ratioSum = 0f;
        var recv = p.GetComponentInChildren<PlayerAnimationEventReceiver>();
        Action<int> onStep = _ => swings++;
        Application.LogCallback onLog = (msg, st, type) =>
        {
            const string Tag = "[MeleeAugment] 검풍 — 피해 ";
            if (msg.StartsWith(Tag))
            {
                gusts++;
                int sp = msg.IndexOf(' ', Tag.Length);
                int ai = msg.IndexOf("공격력 ", Tag.Length);
                if (float.TryParse(sp > 0 ? msg.Substring(Tag.Length, sp - Tag.Length) : msg.Substring(Tag.Length), out float v))
                {
                    dmgSum += v;
                    if (gustShotAt < 0f) gustShotAt = Time.realtimeSinceStartup;
                    int ae = ai > 0 ? msg.IndexOf(' ', ai + 4) : -1;
                    if (ai > 0 && ae > 0 && float.TryParse(msg.Substring(ai + 4, ae - ai - 4), out float a) && a > 0f) ratioSum += v / a;
                }
            }
            else if (msg == "[MeleeAugment] 검풍 굴림") rolls++;
            else if (msg.StartsWith("[MeleeAugment] 검풍 — 풍절")) skips++;
            else if (msg.Contains("풍절 — 맞힘")) winds++;
        };
        if (recv != null) recv.OnEffectStepEarly += onStep;
        Application.logMessageReceived += onLog;
        try
        {
            await W97Swing(p, t != null ? t.M.transform.position : s_home + fw * 2f, 40f, ct, () =>
            {
                foreach (var g in UnityEngine.Object.FindObjectsByType<Animator>(FindObjectsSortMode.None))
                    if (g != null && g.gameObject.name == "PlayerGhostStrike" && g.gameObject.activeInHierarchy) { ghosts++; break; }
                if (!gustShot && gustShotAt > 0f && Camera.main != null)
                {
                    gustShot = true;
                    Directory.CreateDirectory("Temp/ui_shots");
                    CaptureMain(Camera.main, "Temp/ui_shots/W97_gust.png");
                }
            });
            await Wait(0.5f, ct);
        }
        finally { if (recv != null) recv.OnEffectStepEarly -= onStep; Application.logMessageReceived -= onLog; }
        float rate = rolls > 0 ? gusts / (float)rolls : 0f;
        float ratio = gusts > 0 ? ratioSum / gusts : 0f;
        Check(sb, $"④-a 검풍 {gusts} / 굴림 {rolls} = {rate:0.00}(0.31 ± 0.10 · 2σ) · 휘두름 {swings}", rolls >= 60 && Mathf.Abs(rate - 0.31f) <= 0.10f);
        Check(sb, $"④-b 검기 피해 ÷ 그때 공격력 = {ratio:0.000}(0.8) · 평균 피해 {(gusts > 0 ? dmgSum / gusts : 0f):0.0}", gusts > 0 && Mathf.Abs(ratio - 0.8f) <= 0.005f);
        Check(sb, $"④-c 분신이 보인 프레임 {ghosts}(0이어야)", ghosts == 0);
        Check(sb, $"④-d 풍절 {winds}번 · 그 휘두름에 검풍 건너뜀 {skips}번(풍절이 있으면 1 이상)", winds == 0 || skips >= 1);
        DetachAll();
    }

    // ⑤ 대검 범위 — ×1 이면 4.5 m 표적 0 · ×1.4 면 4.5 m 맞고 6.5 m 0 · 「거대한 칼날」 III E 반경 · 카타나 = 1
    private static async UniTask W97Reach(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        if (!await W97Setup(p, Great, "", 12, ct, 0)) { sb.AppendLine("  ⑤ 대검 장착 실패"); return; }
        s_home = p.transform.position;
        Vector3 fw = Flat(p.transform.forward);
        var near = await TrackAsync(s_home + fw * 4.5f, ct);
        var far  = await TrackAsync(s_home + fw * 7.5f, ct);   // 평타가 앞으로 내딛는 몫(≈1 m)을 넘겨서
        HideStrays();
        if (near == null || far == null) { sb.AppendLine("  ⑤ 표적 실패"); return; }
        async UniTask<(float n, float f)> Hits(float baseOverride)
        {
            GreatswordReach.ProbeBaseOverride = baseOverride;
            await Wait(0.3f, ct);
            near.Dealt = 0f; far.Dealt = 0f;
            await W97Swing(p, near.M.transform.position, 5f, ct);
            await Wait(0.5f, ct);
            return (near.Dealt, far.Dealt);
        }
        try
        {
            var one = await Hits(1f);
            var b14 = await Hits(-1f);
            Check(sb, $"⑤-a 범위 ×1 — 4.5 m 표적 피해 {one.n:0}(0이어야)", one.n <= 0f);
            Check(sb, $"⑤-b 범위 ×1.4 — 4.5 m {b14.n:0}(맞아야) · 7.5 m {b14.f:0}(0이어야)", b14.n > 0f && b14.f <= 0f);
        }
        finally { GreatswordReach.ProbeBaseOverride = -1f; }

        // E 반경 3.5 m(1단계 — 고리 없음) · 「거대한 칼날」 III ×1.35 = 4.725 m → 4.4 m 표적
        async UniTask<float> ESkill(string augments)
        {
            await W97Setup(p, Great, augments, 0, ct, 0);
            s_home = p.transform.position;
            DetachAll();
            var t = await TrackAsync(s_home + Flat(p.transform.forward) * 4.4f, ct);
            HideStrays();
            if (t == null) return -1f;
            AimAt(t.M.transform.position); RequestFace(p, t.M.transform.position);
            await Wait(0.3f, ct);
            t.Dealt = 0f;
            p.CooldownTracker.ResetCooldown(SkillType.E);
            p.InputBuffer.Push(Command.ESkill);
            for (float q = 0f; q < 2f; q += 0.05f) { RefillAll(); await Wait(0.05f, ct); }
            return t.Dealt;
        }
        float e0 = await ESkill("");
        float e3 = await ESkill(MeleeAugmentCatalog.GreatReach + ":3");
        float skill3 = GreatswordReach.Skill(p);
        Check(sb, $"⑤-c E 4.4 m 표적 — 증강 없음 {e0:0}(0이어야) · 거대한 칼날 III {e3:0}(맞아야) · 배율 {skill3:0.00}", e0 <= 0f && e3 > 0f && Mathf.Abs(skill3 - 1.35f) < 0.01f);
        await EquipAsync(p, Katana, ct);
        await Wait(0.3f, ct);
        Check(sb, $"⑤-d 카타나 범위 배율 {GreatswordReach.Basic(p):0.00} · {GreatswordReach.Skill(p):0.00}(1이어야)", Mathf.Approximately(GreatswordReach.Basic(p), 1f) && Mathf.Approximately(GreatswordReach.Skill(p), 1f));
        DetachAll();
    }

    // ⑥ 거인의 검 — 지속 4 / 5초 · 칼 ×1.6 · HUD 신호 · 평타 ×1.82 · 무기 바꾸면 끝 · 멈춘 화면 · 착지 전 끊기
    private static async UniTask W97Giant(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        foreach (int lv in new[] { 9, 12 })
        {
            if (!await W97Setup(p, Great, "", lv, ct, 0)) { sb.AppendLine("  ⑥ 대검 장착 실패"); return; }
            s_home = p.transform.position;
            var t = await TrackAsync(s_home + Flat(p.transform.forward) * 3f, ct);
            HideStrays();
            var wi = p.WeaponManager.GetCurrentWeaponComponent<WeaponInstance>();
            Vector3 s0 = wi != null ? wi.transform.localScale : Vector3.one;
            float buffSeconds = -1f;
            Action<SkillType, float> onBuff = (slot, d) => { if (slot == SkillType.R) buffSeconds = d; };
            p.SkillBuffStarted += onBuff;
            float startAt = -1f, endAt = -1f, scale = 0f, basic = 0f;
            bool hudBar = false;
            try
            {
                if (t != null) { AimAt(t.M.transform.position); RequestFace(p, t.M.transform.position); }
                await Wait(0.3f, ct);
                p.CooldownTracker.ResetCooldown(SkillType.R);
                p.InputBuffer.Push(Command.RSkill);
                float t0 = Time.time;
                while (Time.time - t0 < 9f)
                {
                    RefillAll(); HideStrays();
                    bool on = GiantSwordBuff.IsActive(p);
                    if (on && startAt < 0f) startAt = Time.time;
                    if (on && Time.time - startAt > 0.5f && scale == 0f)
                    {
                        scale = wi != null ? wi.transform.localScale.x / Mathf.Max(0.0001f, s0.x) : 0f;
                        basic = GreatswordReach.Basic(p);
                        if (lv == 9 && Camera.main != null) { Directory.CreateDirectory("Temp/ui_shots"); CaptureMain(Camera.main, "Temp/ui_shots/W97_giant.png"); }
                        if (lv == 9) hudBar = W97HudBuffBarOn();
                    }
                    if (!on && startAt > 0f) { endAt = Time.time; break; }
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                }
            }
            finally { p.SkillBuffStarted -= onBuff; }
            float dur = startAt > 0f && endAt > 0f ? endAt - startAt : -1f;
            float want = GiantSwordBuff.DurationFor(p.WeaponManager.Weapon0Data);   // 4초 + 재련 n × 0.5초(10-26 R 개편)
            Check(sb, $"⑥-a 강화 +{lv} 속성 검 지속 {dur:0.00}초(목표 {want}) · HUD 신호 {buffSeconds:0.0}초", Mathf.Abs(dur - want) <= 0.15f && Mathf.Abs(buffSeconds - want) < 0.01f);
            if (lv == 9) Check(sb, $"⑥-b 칼 크기 ×{scale:0.00}(1.6) · 평타 범위 ×{basic:0.00}(1.82)", Mathf.Abs(scale - 1.6f) <= 0.02f && Mathf.Abs(basic - 1.82f) <= 0.01f);
            if (lv == 9) Check(sb, $"⑥-f HUD R 칸 남은 시간 막대 {(hudBar ? "켜짐" : "꺼짐")} · 사진 Temp/ui_shots/W97_giant.png(ca 확인용)", hudBar);
            DetachAll();
        }

        // ⑥-c 무기 바꾸면 끝 · ⑥-d 멈춘 화면에서 남은 시간 그대로
        await W97Setup(p, Great, "", 9, ct, 0);
        GiantSwordBuff.Begin(p, 4f);
        await Wait(0.5f, ct);
        var buff = p.GetComponent<GiantSwordBuff>();
        float r0 = buff != null ? buff.Remaining : -1f;
        Time.timeScale = 0f;
        await Wait(1f, ct);
        float r1 = buff != null ? buff.Remaining : -2f;
        Time.timeScale = 1f;
        Check(sb, $"⑥-d 멈춘 화면 1초 — 남은 시간 {r0:0.00} → {r1:0.00}(그대로)", buff != null && Mathf.Abs(r1 - r0) < 0.02f);
        await EquipAsync(p, Katana, ct);
        await Wait(0.4f, ct);
        Check(sb, $"⑥-c 카타나로 바꿈 — 거인의 검 {GiantSwordBuff.IsActive(p)}(꺼져야)", !GiantSwordBuff.IsActive(p));

        // ⑥-g 칼날 이펙트가 못 붙어도(목록 미로드 · 재질 없음) 끝나면 칼 크기 · 칼날 빛이 돌아와야(10-26 검토 I1)
        await W97Setup(p, Great, "", 9, ct, 0);
        var wiG = p.WeaponManager.GetCurrentWeaponComponent<WeaponInstance>();
        Vector3 sG = wiG != null ? wiG.transform.localScale : Vector3.one;
        var glowField = typeof(WeaponFormRuntime).GetField("_extraGlow", Inst);
        BladeWrapFx.ProbeFail = true;
        try
        {
            GiantSwordBuff.Begin(p, 1f);
            await Wait(0.4f, ct);
            bool glowOn = glowField?.GetValue(WeaponFormRuntime.Of(p)) is Color g0 && g0.maxColorComponent > 0f;
            for (float q = 0f; q < 2f && GiantSwordBuff.IsActive(p); q += 0.1f) await Wait(0.1f, ct);
            await Wait(0.6f, ct);
            float scaleG = wiG != null ? wiG.transform.localScale.x / Mathf.Max(0.0001f, sG.x) : -1f;
            bool glowOff = glowField?.GetValue(WeaponFormRuntime.Of(p)) is Color g1 && g1.maxColorComponent <= 0f;
            Check(sb, $"⑥-g 칼날 이펙트 못 붙음 — 켜진 동안 빛 {glowOn} · 끝난 뒤 칼 크기 ×{scaleG:0.00}(1) · 빛 꺼짐 {glowOff}", glowOn && Mathf.Abs(scaleG - 1f) < 0.02f && glowOff);
        }
        finally { BladeWrapFx.ProbeFail = false; }

        // ⑥-e R을 쓰고 착지 전에 회피 — 끊기면 거인의 검이 안 걸려야(R이 끊기지 않으면 알림만)
        await W97Setup(p, Great, "", 9, ct, 0);
        p.CooldownTracker.ResetCooldown(SkillType.R);
        p.InputBuffer.Push(Command.RSkill);
        await Wait(0.2f, ct);
        p.InputBuffer.Push(Command.Dodge);
        bool got = false;
        for (float q = 0f; q < 1.8f; q += 0.05f) { if (GiantSwordBuff.IsActive(p)) got = true; await Wait(0.05f, ct); }
        sb.AppendLine($"  ⑥-e R 0.2초에 회피 → 거인의 검 {(got ? "걸림(R이 회피로 안 끊겼다 — 착지까지 갔다)" : "안 걸림(끊김)")}");
        DetachAll();
    }

    // ⑨ 속성 검 칼날 이펙트(10-26 사용자 「검에 두르는 이펙트」) — 칼에 붙는가 · 입자가 나오는가 · 칼과 같이 커지는가 · 끝나면 거두는가 · 사진(서서 · 휘두름)
    // 후보: Temp/weapon97_blade.txt 줄마다 「속성|재질 경로」(없으면 목록 값 6속성) · 사진 Temp/ui_shots/W97_blade_<n>_<속성>_<이름>_a · _b
    private static async UniTask W97Blade(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        var rows = new List<(RuneElement el, Material mat, string tag)>();
        if (File.Exists("Temp/weapon97_blade.txt"))
            foreach (var line in File.ReadAllLines("Temp/weapon97_blade.txt"))
            {
                var f = line.Split('|');
                if (f.Length < 2 || !Enum.TryParse(f[0].Trim(), out RuneElement el)) continue;
                rows.Add((el, AssetDatabase.LoadAssetAtPath<Material>(f[1].Trim()), Path.GetFileNameWithoutExtension(f[1].Trim()).Replace(' ', '_')));
            }
        else
            foreach (RuneElement el in Enum.GetValues(typeof(RuneElement))) rows.Add((el, null, "list"));
        Directory.CreateDirectory("Temp/ui_shots");
        var camGo = new GameObject("~BladeWrapCam");
        var cam = camGo.AddComponent<Camera>();
        if (Camera.main != null) cam.CopyFrom(Camera.main);
        cam.enabled = false;
        cam.fieldOfView = 40f;
        try
        {
            int n = 0;
            foreach (var r in rows)
            {
                n++;
                if (!await W97Setup(p, Great, "", 9, ct, 0)) { sb.AppendLine("  ⑨ 대검 장착 실패"); return; }
                s_home = p.transform.position;
                HideStrays();
                var wi = p.WeaponManager.GetCurrentWeaponComponent<WeaponInstance>();
                float len0 = wi != null ? Vector3.Distance(wi.tipPoint.position, wi.rootPoint.position) : -1f;
                GiantSwordBuff.ProbeElement = (int)r.el;
                BladeWrapFx.ProbeMaterial = r.mat;
                BladeWrapFx.ProbeShowProxy = File.Exists("Temp/weapon97_blade_proxy.txt");
                GiantSwordBuff.Begin(p, 2.5f);
                await Wait(0.8f, ct);
                var fx = p.GetComponentInChildren<BladeWrapFx>();
                float len1 = wi != null ? Vector3.Distance(wi.tipPoint.position, wi.rootPoint.position) : -1f;
                float fxLen = fx != null ? fx.WorldLength : -1f;
                bool fxOn = fx != null;
                int alive = fx != null ? fx.AliveParticles : 0;
                string name = $"W97_blade_{n}_{r.el}_{r.tag}";
                Vector3 fw = Flat(p.transform.forward), right = Vector3.Cross(Vector3.up, fw);
                if (wi != null)
                {
                    Vector3 mid = (wi.tipPoint.position + wi.rootPoint.position) * 0.5f;
                    cam.transform.position = mid + right * 1.9f + fw * 1.3f + Vector3.up * 0.3f;
                    cam.transform.LookAt(mid);
                    CaptureMain(cam, $"Temp/ui_shots/{name}_a.png");
                }
                float sw0 = Time.realtimeSinceStartup;
                bool shot = false;
                int slashAt = -1;   // 베기 이벤트가 난 프레임 — 다음 프레임에 찍는다(베기 이펙트가 보이는 순간)
                var recvB = p.GetComponentInChildren<PlayerAnimationEventReceiver>();
                Action<int> onSlash = _ => { if (slashAt < 0) slashAt = Time.frameCount; };
                if (recvB != null) recvB.OnEffectStep += onSlash;
                try
                {
                    await W97Swing(p, s_home + fw * 3f, 1.2f, ct, () =>
                    {
                        if (shot || slashAt < 0 || Time.frameCount < slashAt + 2) return;
                        shot = true;
                        cam.transform.position = p.transform.position + right * 4.2f + fw * 1.5f + Vector3.up * 2.4f;
                        cam.transform.LookAt(p.transform.position + Vector3.up * 1.1f + fw * 0.6f);
                        CaptureMain(cam, $"Temp/ui_shots/{name}_b.png");
                    });
                }
                finally { if (recvB != null) recvB.OnEffectStep -= onSlash; }
                int tinted = 0;
                foreach (var tnt in UnityEngine.Object.FindObjectsByType<ElementSlashTint>(FindObjectsSortMode.None))
                {
                    var ps0 = tnt.GetComponentInChildren<ParticleSystem>();
                    if (ps0 == null) continue;
                    var sc = ps0.main.startColor;
                    Color cmax = sc.mode == ParticleSystemGradientMode.TwoColors ? sc.colorMax : sc.color;
                    if (Vector4.Distance(cmax, (Vector4)ElementPalette.Deep(r.el)) < 0.4f || Vector4.Distance(sc.colorMin, (Vector4)ElementPalette.Bright(r.el)) < 0.4f) tinted++;
                }
                sb.AppendLine($"    ⑨ {r.el} 베기 이펙트 속성 색 {tinted}개(≥ 1)");
                for (float q = 0f; q < 4f && GiantSwordBuff.IsActive(p); q += 0.1f) await Wait(0.1f, ct);
                await Wait(1.6f, ct);
                bool gone = p.GetComponentInChildren<BladeWrapFx>() == null;
                bool ok = fxOn && alive > 0 && Mathf.Abs(fxLen - len1) < 0.05f && Mathf.Abs(len1 / Mathf.Max(0.001f, len0) - 1.6f) < 0.03f && gone;
                Check(sb, $"⑨ {r.el} {r.tag} — 칼날 이펙트 {fxOn} · 입자 {alive} · 칼 {len0:0.00} → {len1:0.00} m · 이펙트 길이 {fxLen:0.00} m · 끝난 뒤 거둠 {gone} · 사진 {name}", ok);
            }
        }
        finally
        {
            GiantSwordBuff.ProbeElement = -1;
            BladeWrapFx.ProbeMaterial = null;
            BladeWrapFx.ProbeShowProxy = false;
            UnityEngine.Object.Destroy(camGo);
            DetachAll();
        }
    }

    // ⑩ R 속성 검 동작(10-26) — ⑩-a 검 뽑기 후보 클립 진행별 사진(대검을 쥔 채 · 팩 검증) · ⑩-b 실제 R: 뽑기 끝 → 버프 · 베기 이벤트 대 판정 시각 · 지속 4 + 0.5n · 사진
    // 후보: Temp/weapon97_draw.txt 줄마다 FBX 경로(없으면 사무라이 Equip01 · 02) · 사진 Temp/ui_shots/W97_draw_<클립>_<0~6> · W97_R_<강화>_<0~4>
    private static async UniTask W97RSkill(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        Directory.CreateDirectory("Temp/ui_shots");
        var camGo = new GameObject("~ElementSwordCam");
        var cam = camGo.AddComponent<Camera>();
        if (Camera.main != null) cam.CopyFrom(Camera.main);
        cam.enabled = false;
        cam.fieldOfView = 45f;
        void Shoot(string name)
        {
            Vector3 fw = Flat(p.transform.forward), right = Vector3.Cross(Vector3.up, fw);
            cam.transform.position = p.transform.position + right * 3.2f + fw * 2.6f + Vector3.up * 1.7f;
            cam.transform.LookAt(p.transform.position + Vector3.up * 1.0f);
            CaptureMain(cam, $"Temp/ui_shots/{name}.png");
        }
        try
        {
            // ⑩-a 뽑기 후보 — QSkill_01 상태에 후보 클립을 덮고 진행별로 찍는다
            const string Sam = "Assets/_ThirdParty/GhostSamurai_Animset/Animation/katana/Common/Unarm&Equip/Inplace/";
            string[] cands = File.Exists("Temp/weapon97_draw.txt") ? File.ReadAllLines("Temp/weapon97_draw.txt")
                           : new[] { Sam + "GhostSamurai_APose_Equip01_Inplace.FBX", Sam + "GhostSamurai_APose_Equip02_Inplace.FBX" };
            float[] at = { 0.1f, 0.25f, 0.4f, 0.55f, 0.7f, 0.9f, 1.2f };
            foreach (var path in cands)
            {
                if (string.IsNullOrWhiteSpace(path)) continue;
                AnimationClip clip = null;
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path.Trim()))
                    if (o is AnimationClip c && !c.name.StartsWith("__preview", StringComparison.Ordinal)) { clip = c; break; }
                if (clip == null) { sb.AppendLine($"  ⑩-a 클립 없음 {path}"); continue; }
                if (!await W97Setup(p, Great, "", 9, ct, 0)) { sb.AppendLine("  ⑩ 대검 장착 실패"); return; }
                HideStrays();
                var aoc = p.Anim.runtimeAnimatorController as AnimatorOverrideController;
                if (aoc == null) { sb.AppendLine("  ⑩-a 오버라이드 컨트롤러 없음"); return; }
                var prev = aoc["QSkill_01"];
                aoc["QSkill_01"] = clip;
                try
                {
                    p.Anim.CrossFadeInFixedTime("QSkill_01", 0.05f);
                    float t0 = Time.time;
                    for (int i = 0; i < at.Length; i++)
                    {
                        while (Time.time - t0 < at[i]) await UniTask.Yield(PlayerLoopTiming.PostLateUpdate, ct);
                        Shoot($"W97_draw_{clip.name}_{i}");
                    }
                    sb.AppendLine($"  ⑩-a 뽑기 후보 {clip.name} — 길이 {clip.length:0.00}초(상태 2배속 → {clip.length / 2f:0.00}초) · 사진 W97_draw_{clip.name}_0~{at.Length - 1}");
                }
                finally { aoc["QSkill_01"] = prev; }
                await Wait(1.2f, ct);
            }

            // ⑩-b 실제 R — 뽑기 끝(버프 시작) · 베기 이벤트 · 판정 · 지속
            var recv = p.GetComponentInChildren<PlayerAnimationEventReceiver>();
            foreach (int lv in new[] { 9, 15 })
            {
                if (!await W97Setup(p, Great, "", lv, ct, 0)) { sb.AppendLine("  ⑩ 대검 장착 실패"); return; }
                s_home = p.transform.position;
                Vector3 fw = Flat(p.transform.forward);
                var t = await TrackAsync(s_home + fw * 3.5f, ct);
                HideStrays();
                if (t != null) { AimAt(t.M.transform.position); RequestFace(p, t.M.transform.position); }
                await Wait(0.3f, ct);
                float s0 = -1f, buffAt = -1f, slashAt = -1f, hitAt = -1f, dur = -1f, dealt = 0f;
                Action<int> onSlash = _ => { if (slashAt < 0f && s0 >= 0f && p.IsInSkillAction) slashAt = Time.time - s0; };
                Action<HitInfo> onHit = h => { if (h.Attacker == p.gameObject && hitAt < 0f && s0 >= 0f) { hitAt = Time.time - s0; dealt = h.Damage; } };
                if (recv != null) recv.OnEffectStep += onSlash;
                HitFeedbackService.OnHit += onHit;
                try
                {
                    p.CooldownTracker.ResetCooldown(SkillType.R);
                    p.InputBuffer.Push(Command.RSkill);
                    s0 = Time.time;
                    int shot = 0;
                    float[] shotsAt = { 0.15f, 0.35f, 0.5f, 0.7f, 0.95f };
                    while (Time.time - s0 < 2f)
                    {
                        RefillAll();
                        float now = Time.time - s0;
                        if (buffAt < 0f && GiantSwordBuff.IsActive(p))
                        {
                            buffAt = now;
                            var b = p.GetComponent<GiantSwordBuff>();
                            dur = b != null ? b.Remaining : -1f;
                        }
                        if (lv == 9 && shot < shotsAt.Length && now >= shotsAt[shot]) Shoot($"W97_R_{lv}_{shot++}");
                        await UniTask.Yield(PlayerLoopTiming.PostLateUpdate, ct);
                    }
                }
                finally
                {
                    if (recv != null) recv.OnEffectStep -= onSlash;
                    HitFeedbackService.OnHit -= onHit;
                }
                int n = MeleeAugmentCatalog.ReforgesSinceEvolution(p.WeaponManager.Weapon0Data);
                float want = GiantSwordBuff.BaseSeconds + GiantSwordBuff.PerReforgeSeconds * n;
                bool sync = slashAt >= 0f && hitAt >= 0f && Mathf.Abs(hitAt - slashAt) <= 0.06f;
                Check(sb, $"⑩-b +{lv} R — 뽑기 끝 {buffAt:0.00}초 · 베기 이벤트 {slashAt:0.00} · 판정 {hitAt:0.00}(차이 ≤ 0.06) · 피해 {dealt:0} · 지속 {dur:0.00}초(4 + 0.5 × {n} = {want:0.0})",
                      buffAt > 0f && hitAt > buffAt && sync && Mathf.Abs(dur - want) < 0.15f);   // 지속은 버프가 켜진 다음 프레임에 읽는다
                DetachAll();
                await Wait(0.5f, ct);
            }
        }
        finally
        {
            UnityEngine.Object.Destroy(camGo);
            DetachAll();
        }
    }

    // ⑪ 범위 규칙 증강(10-26) — 칼끝 베기: 칼끝 띠 표적이 가까운 표적보다 더 맞는가 · 휩쓸기: 여럿 맞히면 R 대기가 줄고 · 속성 검 중엔 지속이 는다(최대 3초)
    private static async UniTask W97RangeRules(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        var lines = new List<string>();
        Application.LogCallback onLog = (msg, _, __) =>
        {
            if (msg.StartsWith("[MeleeAugment] 칼끝", StringComparison.Ordinal) || msg.StartsWith("[MeleeAugment] 휩쓸기", StringComparison.Ordinal)) lines.Add(msg);
        };
        Application.logMessageReceived += onLog;
        try
        {
            // ⑪-a 칼끝 — 가까운 1.0 m · 먼 3.0 m(첫 타 반경 3.5 m · 띠 2.27 m~) 표적, 증강 없음 대 있음
            async UniTask<(float near, float far, int procs)> Tip(string augments)
            {
                await W97Setup(p, Great, augments, 9, ct, 0);
                s_home = p.transform.position;
                Vector3 fw = Flat(p.transform.forward), right = Vector3.Cross(Vector3.up, fw);
                var near = await TrackAsync(s_home + fw * 1.0f + right * 0.6f, ct);
                var far  = await TrackAsync(s_home + fw * 3.0f - right * 0.6f, ct);
                HideStrays();
                await Wait(0.3f, ct);
                if (near == null || far == null) return (0f, 0f, 0);
                near.Dealt = 0f; far.Dealt = 0f; lines.Clear();
                await W97Swing(p, s_home + fw * 2f, 4f, ct);
                await Wait(0.4f, ct);
                int procs = lines.Count;
                DetachAll();
                return (near.Dealt, far.Dealt, procs);
            }
            var a0 = await Tip("");
            var a1 = await Tip(MeleeAugmentCatalog.GreatTip + ":1");
            float r0 = a0.near > 0f ? a0.far / a0.near : 0f, r1 = a1.near > 0f ? a1.far / a1.near : 0f;
            Check(sb, $"⑪-a 칼끝 베기 — 먼/가까운 피해 비 없음 {r0:0.00} → 있음 {r1:0.00} · 칼끝 발동 {a1.procs}회(기대 약 +25%)", a1.procs > 0 && r1 > r0 * 1.12f);

            // ⑪-b 휩쓸기 — 4명 무리 · R 대기 중 휘두름 → 첫 적 뒤 한 명마다 −0.2초(휘두름마다 최대 1초)
            await W97Setup(p, Great, MeleeAugmentCatalog.GreatSweep + ":1", 9, ct, 0);
            s_home = p.transform.position;
            Vector3 f2 = Flat(p.transform.forward), r2 = Vector3.Cross(Vector3.up, f2);
            for (int i = 0; i < 4; i++) await TrackAsync(s_home + f2 * 1.8f + r2 * ((i - 1.5f) * 0.8f), ct);
            HideStrays();
            await Wait(0.3f, ct);
            p.CooldownTracker.StartCooldown(SkillType.R, 9f);
            lines.Clear();
            await W97Swing(p, s_home + f2 * 2f, 0.9f, ct);
            await Wait(0.3f, ct);
            float cutSum = 0f;
            foreach (var l in lines)
            {
                int k = l.IndexOf("R 대기 −", StringComparison.Ordinal);
                if (k >= 0) cutSum += float.Parse(l.Substring(k + 6).Replace("초", ""));
            }
            Check(sb, $"⑪-b 휩쓸기 — R 대기 줄임 합 {cutSum:0.00}초(휘두름마다 ≤ 1초 · 줄 {lines.Count})", cutSum > 0.15f);

            // ⑪-c 속성 검 중 — 지속이 는다 · 한 번 시전에 최대 3초
            GiantSwordBuff.Begin(p, 4f);
            var buff = p.GetComponent<GiantSwordBuff>();
            lines.Clear();
            await W97Swing(p, s_home + f2 * 2f, 6f, ct);
            float extSum = 0f;
            foreach (var l in lines)
            {
                int k = l.IndexOf("속성 검 +", StringComparison.Ordinal);
                if (k >= 0) extSum += float.Parse(l.Substring(k + 6).Replace("초", ""));
            }
            Check(sb, $"⑪-c 휩쓸기 — 속성 검 지속 늘림 합 {extSum:0.00}초(> 0 · ≤ 3) · 남은 {(buff != null ? buff.Remaining : -1f):0.00}초", extSum > 0.1f && extSum <= GiantSwordBuff.MaxExtendSeconds + 0.001f);
            DetachAll();
        }
        finally
        {
            Application.logMessageReceived -= onLog;
            DetachAll();
        }
    }

    // ⑫ E 주변경계 × 속성 검(10-26 사용자 「e스킬도 r에 맞게」) — 4.4 m 표적(몸 둘레까지 닿으므로 ⑤-c와 같은 자리): 속성 검 없으면 안 맞고(3.5 m) · 있으면 맞는다(×1.3 = 4.55 m) · 벤 둘레에 속성 자국
    private static async UniTask W97ESword(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        async UniTask<(float dealt, int zones, float reach, bool active)> E(bool buff)
        {
            await W97Setup(p, Great, "", 0, ct, 0);   // 강화 0 = E 1단계(충격 고리 없음) — 회전 베기 판정만 견준다(⑤-c와 같은 조건)
            s_home = p.transform.position;
            DetachAll();
            var t = await TrackAsync(s_home + Flat(p.transform.forward) * 4.4f, ct);
            HideStrays();
            if (t == null) return (-1f, -1, 0f, false);
            AimAt(t.M.transform.position); RequestFace(p, t.M.transform.position);
            if (buff) { GiantSwordBuff.ProbeElement = (int)RuneElement.Fire; GiantSwordBuff.Begin(p, 8f); }
            await Wait(0.3f, ct);
            t.Dealt = 0f;
            bool active = GiantSwordBuff.IsActive(p);
            float reach = GreatswordReach.Skill(p);
            p.CooldownTracker.ResetCooldown(SkillType.E);
            p.InputBuffer.Push(Command.ESkill);
            int seen = 0;
            for (float q = 0f; q < 1.5f; q += 0.05f)
            {
                RefillAll();
                seen = Mathf.Max(seen, UnityEngine.Object.FindObjectsByType<LegendGroundField>(FindObjectsSortMode.None).Length);
                if (buff && q >= 0.6f && q < 0.65f && Camera.main != null) { Directory.CreateDirectory("Temp/ui_shots"); CaptureMain(Camera.main, "Temp/ui_shots/W97_E_ring.png"); }
                await Wait(0.05f, ct);
            }
            // 자국 수명(2초)은 게임 시간이라 느린 프레임이면 이 창이 끝나기 전에 사라진다 — 창 동안 나타난 최대 수로 본다
            return (t.Dealt, seen, reach, active);
        }
        try
        {
            var off = await E(false);
            var on  = await E(true);
            Check(sb, $"⑫ E × 속성 검 — 4.4 m 표적: 없음 {off.dealt:0}(0이어야 · 배율 {off.reach:0.00} · 버프 {off.active}) · 있음 {on.dealt:0}(맞아야 · 배율 {on.reach:0.00}) · 속성 자국 {on.zones}(1 이상)",
                  !off.active && off.dealt <= 0f && on.dealt > 0f && on.zones >= 1 && Mathf.Abs(on.reach - GiantSwordBuff.RangeMult) < 0.01f);
        }
        finally { GiantSwordBuff.ProbeElement = -1; DetachAll(); }
    }

    /// <summary>HUD 전투 칸의 R 강화 남은 시간 막대가 켜져 있나(석궁 강화와 같은 길 · RaiseSkillBuffStarted).</summary>
    private static bool W97HudBuffBarOn()
    {
        var cpv = UnityEngine.Object.FindFirstObjectByType<CombatPanelView>();
        var fills = cpv != null ? typeof(CombatPanelView).GetField("_buffFill", Inst)?.GetValue(cpv) as RectTransform[] : null;
        return fills != null && fills.Length > 1 && fills[1] != null && fills[1].parent != null && fills[1].parent.gameObject.activeInHierarchy;
    }

    // ⑦ 속성 자국 — 여섯 속성 상태 · 겹쳐도 한 번 · 「거대한 칼날」 III 반경 · 2초 뒤 사라짐 · 사진
    private static async UniTask W97Zones(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        if (!await W97Setup(p, Great, "", 9, ct, 0)) { sb.AppendLine("  ⑦ 대검 장착 실패"); return; }
        s_home = p.transform.position;
        Vector3 fw = Flat(p.transform.forward);
        GiantSwordBuff.Begin(p, 60f);
        var buff = p.GetComponent<GiantSwordBuff>();
        var elemProp = typeof(GiantSwordBuff).GetProperty("Element");
        var ampField = typeof(MonsterBase).GetField("_dmgTakenAmpSlots", Inst);
        float arcR = buff.SwingRadius - 0.75f;
        var t = await TrackAsync(s_home + fw * arcR, ct);
        HideStrays();
        if (t == null) { sb.AppendLine("  ⑦ 표적 실패"); return; }
        bool HasAmp(string id) => ampField?.GetValue(t.M) is System.Collections.IDictionary d && d.Contains(id);
        string res = "";
        bool all = true;
        foreach (var el in new[] { RuneElement.Fire, RuneElement.Ice, RuneElement.Electric, RuneElement.Grass, RuneElement.Light, RuneElement.Dark })
        {
            elemProp.SetValue(buff, el);
            bool froze = false, ok = false, seen = false;
            await W97Swing(p, t.M.transform.position, 3f, ct, () =>
            {
                var s0 = t.M.Status;
                if (s0.HasCc("freeze")) froze = true;
                if ((el == RuneElement.Fire && s0.HasDot("giant_burn")) || (el == RuneElement.Light && s0.IsOnCooldown("giant_light"))) seen = true;
                if (el == RuneElement.Fire && Camera.main != null && !File.Exists("Temp/ui_shots/W97_zone_fire.png")) { Directory.CreateDirectory("Temp/ui_shots"); CaptureMain(Camera.main, "Temp/ui_shots/W97_zone_fire.png"); }
            });
            var st = t.M.Status;
            switch (el)
            {
                case RuneElement.Fire:     ok = seen; break;   // 1틱 점화라 확인 순간엔 비어 있을 수 있다 — 휘두르는 동안 한 번이라도
                case RuneElement.Ice:      ok = st.GetSlowStacks("giant_frost") > 0 && froze; break;
                case RuneElement.Electric: ok = HasAmp("giant_shock"); break;
                case RuneElement.Grass:    ok = st.GetDotStacks("item_poison") >= 1; break;
                case RuneElement.Light:    ok = seen; break;
                case RuneElement.Dark:     ok = HasAmp("giant_dark"); break;
            }
            res += $" {el}:{(ok ? "O" : "X")}{(el == RuneElement.Ice ? (froze ? "(빙결)" : "(빙결 없음)") : "")}";
            all &= ok;
        }
        Check(sb, $"⑦-a 속성 자국 상태{res}", all);

        // ⑦-b 같은 자리 자국 둘을 겹쳐 1.9초 — 전류 틱 ≤ 5(겹쳐도 한 번)
        elemProp.SetValue(buff, RuneElement.Electric);
        await Wait(2.3f, ct);   // 앞 자국이 다 사라지게
        int ticks = 0;
        Application.LogCallback onLog = (msg, s, ty) => { if (msg == "[GiantSword] 전류 틱") ticks++; };
        Application.logMessageReceived += onLog;
        GoHome(p, s_home, Quaternion.LookRotation(fw));
        AimAt(t.M.transform.position); RequestFace(p, t.M.transform.position);
        await Wait(0.2f, ct);
        try
        {
            GiantSwordBuff.SpawnField(p, buff.SwingRadius);   // 10-26 — 평타는 장판을 안 깐다 · 장판 = E와 같은 원
            GiantSwordBuff.SpawnField(p, buff.SwingRadius);   // 10-26 — 평타는 장판을 안 깐다 · 장판 = E와 같은 원
            float nearest = 999f;
            int zonesNow = 0;
            foreach (var z in UnityEngine.Object.FindObjectsByType<LegendGroundField>(FindObjectsSortMode.None))
            {
                zonesNow++;
                if (typeof(LegendGroundField).GetField("_points", Inst)?.GetValue(z) is Vector3[] zp)
                    foreach (var q0 in zp) { var d = q0 - t.M.transform.position; d.y = 0f; nearest = Mathf.Min(nearest, d.magnitude); }
            }
            sb.AppendLine($"  ⑦-b 진단 — 자국 {zonesNow}개 · 표적과 가장 가까운 점 {nearest:0.00} m · 거인의 검 {buff.Active} · 속성 {buff.Element} · 겨눈 쪽 {Vector3.Angle(p.AimForward, t.M.transform.position - p.transform.position):0}°");
            for (float q = 0f; q < 1.9f; q += 0.05f) { RefillAll(); await Wait(0.05f, ct); }
        }
        finally { Application.logMessageReceived -= onLog; }
        Check(sb, $"⑦-b 자국 둘 겹침 1.9초 — 전류 틱 {ticks}(≤ 5)", ticks >= 1 && ticks <= 5);

        // ⑦-c 「거대한 칼날」 III — 판정 반경 ×1.35 · 속성 장판 반경 = 판정 반경(10-26 판정 원 전체 장판)
        float r0 = buff.SwingRadius;
        var w = p.WeaponManager.Weapon0Data;
        w.augments = MeleeAugmentCatalog.GreatReach + ":3";
        GameRunBootstrapper.Instance.Run.EffectManager.Rebuild();
        await Wait(0.2f, ct);
        float r3 = buff.SwingRadius;
        GiantSwordBuff.SpawnField(p, buff.SwingRadius);   // 10-26 — 평타는 장판을 안 깐다 · 장판 = E와 같은 원
        float arc = -1f;
        foreach (var z in UnityEngine.Object.FindObjectsByType<LegendGroundField>(FindObjectsSortMode.None)) arc = Mathf.Max(arc, z.Radius);   // 같은 자리의 작은 장판이 남아 있을 수 있다 — 가장 큰 것
        Check(sb, $"⑦-c 판정 반경 {r0:0.00} → {r3:0.00}(×{r3 / Mathf.Max(0.01f, r0):0.00}) · 속성 장판 반경 {arc:0.00}(= {r3:0.00})", Mathf.Abs(r3 / r0 - 1.35f) < 0.01f && Mathf.Abs(arc - r3) < 0.05f);

        // ⑦-d 2.2초 뒤 자국 0개(수명 2초 — 방을 옮겨도 남지 않는다)
        await Wait(2.2f, ct);
        int left = UnityEngine.Object.FindObjectsByType<LegendGroundField>(FindObjectsSortMode.None).Length;
        Check(sb, $"⑦-d 2.2초 뒤 남은 자국 {left}(0이어야)", left == 0);
        w.augments = "";
        GiantSwordBuff.Begin(p, 0.01f);
        DetachAll();
    }

    // 99 — 카타나 · 대검 재련 · 스킬 · 증강 실전(10-26 사용자 「에디터에서 카타나랑 대검의 재련 내용과 스킬을 실제 테스트」)
    // Temp/weapon99_only.txt에 A(재련) · B(평타 · 스킬) · C(증강 전수) · D(카타나 증강 × 스킬) · E(오크 더미 DPS) · F(카타나 분신 사진) · G(평타 빠르기 · 진짜 클릭 · 세 무기) · H(근접 버팀 · 시작 선물) · J(검풍 검기 다시 터짐) · L(전설 룬 이펙트 · 판정) · M(풀 수명 통일) · N(화살 사거리)와 무기(K · G)를 적으면 그것만(예: 「A B G」) · 없으면 전부
    private static Vector3 s_fw = Vector3.forward;   // 99 E — DPS 제자리 방향

    private static async UniTask Weapon99Async(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        sb.AppendLine("99 — 카타나 · 대검 재련 · 스킬 · 증강 실전(10-26)");
        await Wait(3f, ct);
        foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
            if (m != null && !m.IsDead) m.gameObject.SetActive(false);
        Managers.UI?.CloseAllPopupUI(); Time.timeScale = 1f;
        string only = File.Exists("Temp/weapon99_only.txt") ? File.ReadAllText("Temp/weapon99_only.txt") : "";
        bool Want(string k) => only.Trim().Length == 0 || only.Contains(k);
        bool anyWeapon = only.Contains("K") || only.Contains("G");
        try
        {
            foreach (var (path, name, key) in new[] { (Katana, "카타나", "K"), (Great, "대검", "G") })
            {
                if (anyWeapon && !only.Contains(key)) continue;
                sb.AppendLine($"── {name} ──");
                if (Want("A")) await W99Reforge(p, sb, path, name, ct);
                if (Want("B")) { await W99Basic(p, sb, path, name, ct); await W99Skills(p, sb, path, name, ct); }
                if (Want("C")) await W99Augments(p, sb, path, name, ct);
                if (Want("D") && path == Katana) await W99KatanaSkillAug(p, sb, ct);
                if (Want("E")) await W99Dps(p, sb, path, name, ct);
                if (Want("F") && path == Katana) await W99Photos(p, sb, ct);
            }
            if (Want("G")) await W99Tempo(p, sb, ct);
            if (Want("H")) await W99Ward(p, sb, ct);
            if (Want("I")) await W99ArrowBurst(p, sb, ct);
            if (Want("J")) await W99GustReplay(p, sb, ct);
            if (Want("L")) await W99LegendFx(p, sb, ct);
            if (Want("M")) await W99PoolLife(p, sb, ct);
            if (Want("N")) await W99ArrowRange(p, sb, ct);
        }
        finally
        {
            Time.timeScale = 1f;
            GiantSwordBuff.ProbeElement = -1;
            if (MeleeAugmentDraft.Pending != null) MeleeAugmentDraft.AutoChoose();
            Managers.UI?.CloseAllPopupUI();
            DetachAll();
        }
    }

    /// <summary>A — +9에서 실제 재련 4번(+21까지): 단계 +3 · 카드(무기에 맞음 · 걷은 증강 없음 · 자람 값) · 고름 · 스킬 단계.</summary>
    private static async UniTask W99Reforge(PlayerController p, StringBuilder sb, string path, string name, CancellationToken ct)
    {
        if (!await W97Setup(p, path, "", 9, ct, 0)) { sb.AppendLine($"  A {name} 장착 실패"); return; }
        var w = p.WeaponManager.Weapon0Data;
        w.highestEnhanceReached = 9;
        w.augmentAfterEvolve = false;
        w.evolutionStage = Mathf.Max(1, w.evolutionStage);   // 진화한 무기 — 강화 상한이 +21까지 열린다(진화 전 T1 상한 +12)
        w.reforgeLog = "";
        var table = WeaponEnhanceService.Table;
        var run = GameRunBootstrapper.Instance?.Run;
        var rng = new System.Random(99);
        for (int i = 0; i < 4; i++)
        {
            int lv0 = w.enhanceLevel, tier0 = SkillTierResolver.Resolve(w);
            float atk0 = p.RuntimeStats.GetEffectiveAttack(AttackStatKind.Melee);
            bool ok = WeaponReforgeService.ReforgeMelee(w, table, run, rng);
            await Wait(0.2f, ct);
            var offer = MeleeAugmentDraft.Pending;
            var cards = new StringBuilder();
            bool fits = offer != null, removed = false;
            if (offer != null)
                foreach (var c in offer.Cards)
                {
                    var d = c.Def;
                    if (d == null || !MeleeAugmentCatalog.FitsWeapon(d, w)) fits = false;
                    if (MeleeAugmentCatalog.RemovedIds.Contains(c.Id)) removed = true;
                    if (cards.Length > 0) cards.Append(" | ");
                    cards.Append(d != null ? $"{d.Name}({MeleeAugmentDef.TierName(d.Tier)}{(d.IsGrowth ? " · 자람 " + d.ValueFor(w).ToString("0.##") : "")})" : c.Id);
                }
            string picked = offer != null && offer.Cards.Length > 0 && offer.Cards[0].Def != null ? offer.Cards[0].Def.Name : "-";
            if (offer != null) MeleeAugmentDraft.Choose(0);
            Managers.UI?.CloseAllPopupUI(); Time.timeScale = 1f;
            p.WeaponManager.RaiseEquippedWeaponRefreshed();
            await Wait(0.3f, ct);
            float atk1 = p.RuntimeStats.GetEffectiveAttack(AttackStatKind.Melee);
            int tier1 = SkillTierResolver.Resolve(w);
            Check(sb, $"A {name} 재련 {i + 1}회 — +{lv0} → +{w.enhanceLevel} · 공격력 {atk0:0.0} → {atk1:0.0} · 스킬 단계 {tier0} → {tier1} · 카드 {cards} · 고름 {picked}",
                  ok && w.enhanceLevel == lv0 + 3 && offer != null && fits && !removed);
        }
        var grow = new StringBuilder();
        foreach (var d in MeleeAugmentCatalog.All)
        {
            if (d == null || !d.IsGrowth || !MeleeAugmentCatalog.FitsWeapon(d, w)) continue;
            grow.Append($" {d.Name} {d.ValueFor(w):0.##}" + (d.Value2For(w) != 0f ? $"/{d.Value2For(w):0.##}" : ""));
        }
        sb.AppendLine($"  A {name} +21 증강 「{w.augments}」 · 재련 기록 「{w.reforgeLog}」 · 자람 값(+21):{grow}");
        Check(sb, $"A {name} +21 스킬 단계 {SkillTierResolver.Resolve(w)}(3) · 재련 횟수 {MeleeAugmentCatalog.ReforgesSinceEvolution(w)}(4)",
              SkillTierResolver.Resolve(w) == 3 && MeleeAugmentCatalog.ReforgesSinceEvolution(w) == 4);
    }

    /// <summary>B — 평타 3초(+9 · +21): 앞 표적 · 무리 셋 · 먼 표적 피해 · 휘두름 · 적중(10-26 사용자 「평타도 같이 실측」).</summary>
    private static async UniTask W99Basic(PlayerController p, StringBuilder sb, string path, string name, CancellationToken ct)
    {
        var runs = path == Great ? new[] { (9, false), (9, true), (21, false), (21, true) } : new[] { (9, false), (21, false) };   // 대검은 속성 검(R) 중 평타도(10-26 사용자 「r 상태 평타가 메인 재미 — 체크」)
        foreach (var (lv, buffOn) in runs)
        {
            if (!await W97Setup(p, path, "", lv, ct, 0)) { sb.AppendLine($"  B {name} 장착 실패"); return; }
            if (buffOn) { GiantSwordBuff.ProbeElement = (int)RuneElement.Fire; GiantSwordBuff.Begin(p, 8f); }
            DetachAll();
            s_home = p.transform.position;
            Vector3 fw = Flat(p.transform.forward), right = Vector3.Cross(Vector3.up, fw);
            var a  = await TrackAsync(s_home + fw * 2.2f, ct);
            var b  = await TrackAsync(s_home + fw * 2.2f + right * 1.3f, ct);
            var c  = await TrackAsync(s_home + fw * 2.2f - right * 1.3f, ct);
            var fa = await TrackAsync(s_home + fw * 5.5f, ct);
            HideStrays();
            if (a == null) { sb.AppendLine($"  B {name} 표적 실패"); continue; }
            AimAt(a.M.transform.position); RequestFace(p, a.M.transform.position);
            await Wait(0.4f, ct);
            foreach (var t in new[] { a, b, c, fa }) if (t != null) t.Dealt = 0f;
            int hits = 0;
            Action<HitInfo> onHit = h => { if (h.Attacker == p.gameObject && h.Target != null && (h.ActionType == WeaponActionType.GroundLight || h.ActionType == WeaponActionType.AirLight)) hits++; };
            HitFeedbackService.OnHit += onHit;
            var rt = WeaponFormRuntime.Of(p);
            int sw0 = rt != null ? rt.SwingSerial : 0;
            try { await W97Swing(p, a.M.transform.position, 3f, ct); }
            finally { HitFeedbackService.OnHit -= onHit; }
            await Wait(0.3f, ct);
            int swings = rt != null ? rt.SwingSerial - sw0 : -1;
            float crowd = (a?.Dealt ?? 0f) + (b?.Dealt ?? 0f) + (c?.Dealt ?? 0f);
            Check(sb, $"B {name} +{lv}{(buffOn ? " 속성 검(불)" : "")} 평타 3초 — 앞 표적 {a.Dealt:0} · 무리 셋 합 {crowd:0}(초당 {crowd / 3f:0}) · 5.5 m {fa?.Dealt ?? 0f:0} · 휘두름 {swings} · 적중 {hits}", a.Dealt > 0f && swings >= 3);
            GiantSwordBuff.ProbeElement = -1;
            DetachAll();
        }
    }

    /// <summary>D — 카타나 그림자 파트너 · 검풍이 E · R에도 붙는가(10-26 사용자 「카타나도 증강 내용이 스킬에 적용」) — 평타 없이 스킬만 · 검풍 확률 100%로.</summary>
    private static async UniTask W99KatanaSkillAug(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        var lines = new List<string>();
        Application.LogCallback onLog = (msg, _, __) => { if (msg.StartsWith("[MeleeAugment] ", StringComparison.Ordinal)) lines.Add(msg); };
        Application.logMessageReceived += onLog;
        SwordGustAugment.ProbeChance = 100f;
        try
        {
            // D-0 재련 4회(+21) — 그림자 분신 3명 · 평타 중 사진 · 검풍 잔상 분신 사진(확률 100%)
            if (await W97Setup(p, Katana, MeleeAugmentCatalog.KatanaShadow + ":1," + MeleeAugmentCatalog.KatanaGust + ":1", 21, ct, 0))
            {
                DetachAll();
                s_home = p.transform.position;
                Vector3 f0 = Flat(p.transform.forward), r0 = Vector3.Cross(Vector3.up, f0);
                var t0 = await TrackAsync(s_home + f0 * 2.2f, ct);
                await TrackAsync(s_home + f0 * 2.4f + r0 * 1.2f, ct);
                HideStrays();
                await Wait(1.2f, ct);
                var sp = p.GetComponent<ShadowPartner>();
                var camGo = new GameObject("~W99CloneCam");
                var cam = camGo.AddComponent<Camera>();
                if (Camera.main != null) cam.CopyFrom(Camera.main);
                cam.enabled = false;
                cam.fieldOfView = 55f;
                int shot = 0;
                float sw0 = Time.realtimeSinceStartup;
                lines.Clear();
                try
                {
                    await W97Swing(p, t0 != null ? t0.M.transform.position : s_home + f0 * 2.2f, 2f, ct, () =>
                    {
                        float e = Time.realtimeSinceStartup - sw0;
                        if (shot < 3 && e >= 0.4f + shot * 0.45f)
                        {
                            cam.transform.position = s_home - f0 * 2.5f + r0 * 4f + Vector3.up * 4.5f;
                            cam.transform.LookAt(s_home + f0 * 1.2f + Vector3.up * 0.8f);
                            CaptureMain(cam, $"Temp/ui_shots/W99_K_clones_{shot++}.png");
                        }
                    });
                }
                finally { UnityEngine.Object.Destroy(camGo); }
                int waves = 0;
                foreach (var l in lines) if (l.Contains("검풍 — 피해")) waves++;
                Check(sb, $"D-0 카타나 +21 — 그림자 분신 {(sp != null ? sp.ShownCount : 0)}명(3) · 검풍 잔상 분신 검기 {waves}번(≥ 1) · 사진 W99_K_clones_0~2", sp != null && sp.ShownCount == 3 && waves >= 1);
                DetachAll();
            }

            foreach (var (cmd, type, nm) in new[] { (Command.ESkill, SkillType.E, "E"), (Command.RSkill, SkillType.R, "R") })
            {
                if (!await W97Setup(p, Katana, MeleeAugmentCatalog.KatanaShadow + ":1," + MeleeAugmentCatalog.KatanaGust + ":1", 15, ct, 0)) { sb.AppendLine("  D 카타나 장착 실패"); return; }
                DetachAll();
                s_home = p.transform.position;
                Vector3 fw = Flat(p.transform.forward), right = Vector3.Cross(Vector3.up, fw);
                var a = await TrackAsync(s_home + fw * 2.5f, ct);
                await TrackAsync(s_home + fw * 2.5f + right * 1.3f, ct);
                await TrackAsync(s_home + fw * 2.5f - right * 1.3f, ct);
                HideStrays();
                if (a == null) { sb.AppendLine("  D 표적 실패"); continue; }
                AimAt(a.M.transform.position); RequestFace(p, a.M.transform.position);
                await Wait(0.4f, ct);
                p.InputBuffer.Clear();
                lines.Clear();
                p.CooldownTracker.ResetCooldown(type);
                p.InputBuffer.Push(cmd);
                for (float q = 0f; q < 2.5f; q += 0.1f) { RefillAll(); await Wait(0.1f, ct); }
                int shadow = 0, rolls = 0, gust = 0;
                foreach (var l in lines)
                {
                    if (l.Contains("그림자 —")) shadow++;
                    if (l.Contains("검풍 굴림(스킬)")) rolls++;
                    if (l.Contains("검풍 — 피해")) gust++;
                }
                Check(sb, $"D 카타나 {nm} — 그림자 추가 피해 {shadow}번(≥ 1) · 검풍 굴림 {rolls}번(시전당 1) · 검풍 발사 {gust}번(확률 100%로 ≥ 1)", shadow >= 1 && rolls == 1 && gust >= 1);
                DetachAll();
            }
        }
        finally
        {
            SwordGustAugment.ProbeChance = -1f;
            Application.logMessageReceived -= onLog;
            DetachAll();
        }
    }

    /// <summary>
    /// F — 카타나 분신 사진(10-26 사용자 「매 공격 따라 하는 분신의 이펙트와 검기를 날리는 분신의 애니메이션과 검기」). +21(분신 3) · 오크 표적.
    ///   F-1 따라 하는 분신 — 평타 3타 동안 게임 시간 0.12초마다(옆 위 시점) + 위 뒤 시점 2장 · 분신 피해 줄 시각
    ///   F-2 검기 분신 — 검풍(확률 100%)만 켜고, 굴림이 붙을 때마다 0.05초 간격 8장(뒤 위 시점) · 두 번(왼쪽 · 오른쪽)
    ///   F-3 스킬 — 그림자만 켜고 E 한 번 동안 0.1초마다
    ///   F-4 베기 메아리(10-26 개선) — 맞힌 분신 자리의 옅은 어두운 베기 · 색 후보 A · B 사진 · 헛손질엔 없음 · 사본 충돌체 0
    /// 사진 Temp/ui_shots/W99F_*.png.
    /// </summary>
    private static async UniTask W99Photos(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        Directory.CreateDirectory("Temp/ui_shots");
        var lines = new List<(float t, string msg)>();
        Application.LogCallback onLog = (msg, _, __) =>
        {
            if (msg.StartsWith("[MeleeAugment] ", StringComparison.Ordinal) || msg.StartsWith("[ShadowPartner] ", StringComparison.Ordinal)) lines.Add((Time.time, msg));
        };
        Application.logMessageReceived += onLog;
        var camGo = new GameObject("~W99PhotoCam");
        var cam = camGo.AddComponent<Camera>();
        if (Camera.main != null) cam.CopyFrom(Camera.main);
        cam.enabled = false;
        cam.fieldOfView = 50f;
        Vector3 open = Vector3.zero;
        Quaternion face = Quaternion.identity;
        bool placed = false;
        void Place()   // 허브 시작 자리는 돌 아치 밑이라 상인방이 가린다 — 방 안 열린 곳(앞 4.5 m)으로
        {
            if (!placed) { var f = Flat(p.transform.forward); open = p.transform.position + f * 4.5f; face = Quaternion.LookRotation(f); placed = true; }
            if (p.TryGetComponent<Rigidbody>(out var rb)) { rb.position = open; rb.rotation = face; rb.linearVelocity = Vector3.zero; }
            p.transform.SetPositionAndRotation(open, face);
        }
        try
        {
            // F-1 따라 하는 분신
            if (await W97Setup(p, Katana, MeleeAugmentCatalog.KatanaShadow + ":1", 21, ct, 0))
            {
                DetachAll();
                Place();
                await Wait(0.2f, ct);
                s_home = p.transform.position;
                Vector3 f0 = Flat(p.transform.forward), r0 = Vector3.Cross(Vector3.up, f0);
                var t0 = await TrackOrcAsync(s_home + f0 * 2.4f, ct);
                await TrackOrcAsync(s_home + f0 * 2.0f - r0 * 1.6f, ct);
                HideStrays();
                await Wait(1.5f, ct);
                var sp = p.GetComponent<ShadowPartner>();
                Vector3 aim = t0 != null ? t0.M.transform.position : s_home + f0 * 2.4f;
                lines.Clear();
                float g0 = Time.time;
                int shot = 0;
                await W97Swing(p, aim, 2.2f, ct, () =>
                {
                    float e = Time.time - g0;
                    if (shot < 16 && e >= 0.1f + shot * 0.12f)
                    {
                        cam.transform.position = s_home - f0 * 3.6f + r0 * 2.4f + Vector3.up * 3.4f;
                        cam.transform.LookAt(s_home + f0 * 1.0f + Vector3.up * 0.8f);
                        CaptureMain(cam, $"Temp/ui_shots/W99F_shadow_{shot:00}_{e:0.00}.png");
                        shot++;
                    }
                });
                for (int k = 0; k < 2; k++)
                {
                    p.InputBuffer.Push(Command.Light);
                    await Wait(0.18f, ct);
                    cam.transform.position = k == 0 ? s_home + f0 * 1.0f - r0 * 6.5f + Vector3.up * 2.4f : s_home - f0 * 2.5f + Vector3.up * 7f;
                    cam.transform.LookAt(s_home + f0 * 1.0f + Vector3.up * (k == 0 ? 0.9f : 0f));
                    CaptureMain(cam, $"Temp/ui_shots/W99F_shadow_top_{k}.png");
                }
                var hits = new StringBuilder();
                foreach (var (t, msg) in lines) if (msg.Contains("그림자 —")) hits.Append($" {t - g0:0.00}");
                Check(sb, $"F-1 따라 하는 분신 {(sp != null ? sp.ShownCount : 0)}명 · 사진 {shot}장 W99F_shadow_00~ · 분신 피해 시각(초):{hits}", sp != null && sp.ShownCount == 3 && shot >= 10);
                DetachAll();
            }

            // F-2 검기 분신
            SwordGustAugment.ProbeChance = 100f;
            if (await W97Setup(p, Katana, MeleeAugmentCatalog.KatanaGust + ":1", 21, ct, 0))
            {
                DetachAll();
                Place();
                await Wait(0.2f, ct);
                s_home = p.transform.position;
                Vector3 f0 = Flat(p.transform.forward), r0 = Vector3.Cross(Vector3.up, f0);
                var t0 = await TrackOrcAsync(s_home + f0 * 2.2f, ct);
                await TrackOrcAsync(s_home + f0 * 5.2f, ct);
                HideStrays();
                await Wait(1.2f, ct);
                Vector3 aim = t0 != null ? t0.M.transform.position : s_home + f0 * 2.2f;
                lines.Clear();
                int burst = 0, k = 0, seen = 0;
                float burstAt = -1f;
                await W97Swing(p, aim, 3f, ct, () =>
                {
                    int rolls = 0;
                    foreach (var (_, msg) in lines) if (msg.Contains("검풍 굴림")) rolls++;
                    if (burstAt < 0f && burst < 2 && rolls > seen) { seen = rolls; burstAt = Time.time; k = 0; }
                    if (burstAt >= 0f && Time.time - burstAt >= k * 0.05f)
                    {
                        cam.transform.position = s_home - f0 * 3.2f + r0 * 2.2f + Vector3.up * 3.8f;
                        cam.transform.LookAt(s_home + f0 * 2.6f + Vector3.up * 0.6f);
                        CaptureMain(cam, $"Temp/ui_shots/W99F_gust{burst}_{k}_{Time.time - burstAt:0.00}.png");
                        if (++k >= 8) { burst++; burstAt = -1f; seen = rolls; }   // 연사 중에 붙은 굴림은 건너뛴다(다음 굴림부터)
                    }
                });
                int waves = 0;
                foreach (var (_, msg) in lines) if (msg.Contains("검풍 — 피해")) waves++;
                Check(sb, $"F-2 검기 분신 — 연사 {burst}번 × 8장 W99F_gust0 · 1 · 검기 피해 {waves}번", burst >= 2 && waves >= 1);
                DetachAll();
            }
            SwordGustAugment.ProbeChance = -1f;

            // F-3 스킬(E) — 분신도 따라 쓴다
            if (await W97Setup(p, Katana, MeleeAugmentCatalog.KatanaShadow + ":1", 21, ct, 0))
            {
                DetachAll();
                Place();
                await Wait(0.2f, ct);
                s_home = p.transform.position;
                Vector3 f0 = Flat(p.transform.forward), r0 = Vector3.Cross(Vector3.up, f0);
                var a = await TrackOrcAsync(s_home + f0 * 2.5f, ct);
                await TrackOrcAsync(s_home + f0 * 2.5f + r0 * 1.5f, ct);
                HideStrays();
                await Wait(1.5f, ct);
                if (a != null) { AimAt(a.M.transform.position); RequestFace(p, a.M.transform.position); }
                await Wait(0.3f, ct);
                p.InputBuffer.Clear();
                lines.Clear();
                p.CooldownTracker.ResetCooldown(SkillType.E);
                p.InputBuffer.Push(Command.ESkill);
                float g0 = Time.time;
                int shot = 0;
                while (Time.time - g0 < 1.8f)
                {
                    RefillAll();
                    float e = Time.time - g0;
                    if (shot < 14 && e >= 0.05f + shot * 0.12f)
                    {
                        cam.transform.position = s_home - f0 * 3.6f + r0 * 2.4f + Vector3.up * 3.4f;
                        cam.transform.LookAt(s_home + f0 * 1.0f + Vector3.up * 0.8f);
                        CaptureMain(cam, $"Temp/ui_shots/W99F_skillE_{shot:00}_{e:0.00}.png");
                        shot++;
                    }
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                }
                int shadow = 0;
                foreach (var (_, msg) in lines) if (msg.Contains("그림자 —")) shadow++;
                Check(sb, $"F-3 E — 분신 스킬 피해 {shadow}번 · 사진 {shot}장 W99F_skillE_00~", shot >= 8);
                DetachAll();
            }

            // F-4 베기 메아리 — 색 후보마다 사본을 새로 칠한다(그림자 컴포넌트를 지우면 증강이 다음 틱에 새로 붙인다)
            foreach (var (tag, br, dp, al) in new[] {
                ("A", new Color(0.09f, 0.07f, 0.14f), new Color(0.02f, 0.015f, 0.035f), 0.7f),
                ("B", new Color(0.20f, 0.17f, 0.28f), new Color(0.05f, 0.04f, 0.08f), 0.55f) })
            {
                ShadowPartner.ProbeSlashBright = br; ShadowPartner.ProbeSlashDeep = dp; ShadowPartner.ProbeSlashAlpha = al;
                if (!await W97Setup(p, Katana, MeleeAugmentCatalog.KatanaShadow + ":1", 21, ct, 0)) break;
                DetachAll();
                Place();
                if (p.TryGetComponent<ShadowPartner>(out var old)) UnityEngine.Object.Destroy(old);
                await Wait(0.3f, ct);
                s_home = p.transform.position;
                Vector3 f0 = Flat(p.transform.forward), r0 = Vector3.Cross(Vector3.up, f0);
                var t0 = await TrackOrcAsync(s_home + f0 * 2.4f, ct);
                await TrackOrcAsync(s_home + f0 * 2.0f - r0 * 1.6f, ct);
                HideStrays();
                await Wait(1.5f, ct);
                var sp = p.GetComponent<ShadowPartner>();
                Vector3 aim = t0 != null ? t0.M.transform.position : s_home + f0 * 2.4f;
                lines.Clear();
                float g0 = Time.time;
                int shot = 0, maxVis = 0;
                await W97Swing(p, aim, 1.8f, ct, () =>
                {
                    if (sp != null) maxVis = Mathf.Max(maxVis, sp.EchoVisible);
                    float e = Time.time - g0;
                    if (shot < 10 && e >= 0.2f + shot * 0.1f)
                    {
                        cam.transform.position = s_home - f0 * 3.6f + r0 * 2.4f + Vector3.up * 3.4f;
                        cam.transform.LookAt(s_home + f0 * 1.0f + Vector3.up * 0.8f);
                        CaptureMain(cam, $"Temp/ui_shots/W99F_echo{tag}_{shot:00}_{e:0.00}.png");
                        shot++;
                    }
                });
                int echoes = 0, hits = 0;
                foreach (var (_, msg) in lines) { if (msg.Contains("베기 메아리")) echoes++; if (msg.Contains("그림자 —")) hits++; }
                int cols = sp != null ? sp.EchoColliders : -1;
                Check(sb, $"F-4{tag} 베기 메아리 {echoes}번(분신 피해 {hits}번 중 베기마다 분신당 1) · 한 번에 보인 최대 {maxVis} · 사본 켜진 충돌체 {cols}(0) · 사진 W99F_echo{tag}_", echoes >= 3 && echoes <= hits && cols == 0);
                if (tag == "A")
                {
                    // 헛손질 — 적은 9 m(분신은 보임) · 반대쪽으로 휘두름 → 메아리 0
                    DetachAll();
                    await TrackOrcAsync(s_home + f0 * 9f, ct);
                    HideStrays();
                    await Wait(0.8f, ct);
                    lines.Clear();
                    await W97Swing(p, s_home - f0 * 3f, 1.2f, ct);
                    int miss = 0;
                    foreach (var (_, msg) in lines) if (msg.Contains("베기 메아리")) miss++;
                    Check(sb, $"F-4 헛손질 — 분신 보임 {sp != null && sp.Shown} · 베기 메아리 {miss}번(0이어야)", sp != null && sp.Shown && miss == 0);
                }
                DetachAll();
            }
            ShadowPartner.ProbeSlashAlpha = 0f;
        }
        finally
        {
            SwordGustAugment.ProbeChance = -1f;
            ShadowPartner.ProbeSlashAlpha = 0f;
            Application.logMessageReceived -= onLog;
            UnityEngine.Object.Destroy(camGo);
            DetachAll();
        }
    }

    /// <summary>
    /// G — 평타 빠르기(10-26 사용자 「카타나의 공격속도가 너무 빠른데 체크」 · 「입력 제한 문제 · 공격 속도 최대치 · 입력을 실제로 넣어 봐서」).
    /// 진짜 왼 클릭(입력 시스템 상태 이벤트 · 96과 같은 길 — 입력 정책 · 꾹 누르기 반복 · 버퍼를 다 지난다). 카타나 · 무형검 · 대검 +9 · 형 · 증강 없음 · 오크 하나 앞 2.2 m.
    /// 입력마다 4초: 꾹 누름 · 연타 0.25 / 0.12 / 0.06초(누름 0.04초) · 꾹 + 공속 +30% · 꾹 + 공속 최대(합 2.5 → 상한 2.0).
    /// 잼: 베기(애니 이벤트 「[AE] EffectStep n」 · 게임 시간) 수 · 초당 · 평균 간격 · 단계 넘김별 평균 · 뗀 뒤 1.2초 안 휘두름(입력 쌓임) ·
    /// 막타 전 1타로 돌아감(콤보 끊김) · 베기 뒤 0.25초 안 오크 체력이 안 준 휘두름(헛휘두름).
    /// </summary>
    private static async UniTask W99Tempo(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        sb.AppendLine("── 평타 빠르기(G · 진짜 왼 클릭) ──");
        var mouse = Mouse.current;
        if (mouse == null) { sb.AppendLine("  G 마우스 없음"); return; }
        var editorMode0 = InputSystem.settings.editorInputBehaviorInPlayMode;
        var background0 = InputSystem.settings.backgroundBehavior;
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        var center = new Vector2(Screen.width * 0.5f, Screen.height * 0.6f);   // 화면 가운데 위 — HUD 위 클릭은 공격으로 안 받는다
        MouseState Up() => new MouseState { position = center };
        MouseState Down() => new MouseState { position = center }.WithButton(MouseButton.Left);
        var slashes = new List<(float t, int step)>();
        var hits = new List<float>();
        Application.LogCallback onLog = (msg, _, __) =>
        {
            if (!msg.StartsWith("[AE] EffectStep ", StringComparison.Ordinal)) return;
            if (int.TryParse(msg.Substring(16).Trim(), out int st)) slashes.Add((Time.time, st));
        };
        Application.logMessageReceived += onLog;
        var rs = p.RuntimeStats;
        int gen = rs != null ? rs.SkillAttackSpeedGeneration : 0;
        float added = 0f;   // 지금 스킬 칸에 넣은 공속(끝나면 뺀다)
        try
        {
            foreach (var (path, name) in new[] { (Katana, "카타나"), (Nameless, "무형검"), (Great, "대검") })
            {
                if (!await W97Setup(p, path, "", 9, ct, 0)) { sb.AppendLine($"  G {name} 장착 실패"); continue; }
                DetachAll();
                s_home = p.transform.position;
                Vector3 f0 = Flat(p.transform.forward);
                var t0 = await TrackOrcAsync(s_home + f0 * 2.2f, ct);
                HideStrays();
                await Wait(0.8f, ct);
                Vector3 aim = t0 != null ? t0.M.transform.position : s_home + f0 * 2.2f;
                int lastStep = -1;
                foreach (var (mode, gap, atkSpd) in new[] {
                    ("꾹 누름", 0f, 0f), ("연타 0.25초", 0.25f, 0f), ("연타 0.12초", 0.12f, 0f), ("연타 0.06초", 0.06f, 0f),
                    ("꾹 · 공속 +30%", 0f, 0.3f), ("꾹 · 공속 최대", 0f, 1.5f) })
                {
                    // 공속 — 스킬 칸(가웨인 정오가 안 건드림) · 정오(zenith)는 매 프레임 0
                    if (added != 0f) { rs?.AddSkillAttackSpeed(-added, gen); added = 0f; }
                    if (atkSpd > 0f && rs != null) { rs.AddSkillAttackSpeed(atkSpd, gen); added = atkSpd; }
                    rs?.SetZenithAttackSpeed(0f);
                    InputSystem.QueueStateEvent(mouse, Up());
                    p.InputBuffer.Clear();
                    float w0 = Time.realtimeSinceStartup;
                    while (Time.realtimeSinceStartup - w0 < 1.2f) { rs?.SetZenithAttackSpeed(0f); RefillAll(); Steer(p, aim); await UniTask.Yield(PlayerLoopTiming.Update, ct); }   // 앞 콤보가 끝나게
                    slashes.Clear(); hits.Clear();
                    float dealt0 = t0 != null ? t0.Dealt : 0f, spdSum = 0f, spdMin = 99f, spdMax = 0f;
                    int spdN = 0;
                    float g0 = Time.time, nextTap = g0;
                    bool down = false;
                    if (gap <= 0f) { InputSystem.QueueStateEvent(mouse, Down()); down = true; }
                    while (Time.time - g0 < 4f)
                    {
                        rs?.SetZenithAttackSpeed(0f);
                        RefillAll(); Steer(p, aim);
                        if (t0 != null && t0.Dealt > dealt0 + 0.5f) { hits.Add(Time.time); dealt0 = t0.Dealt; }
                        if (rs != null) { float m = rs.AttackSpeedMultiplier; spdSum += m; spdN++; spdMin = Mathf.Min(spdMin, m); spdMax = Mathf.Max(spdMax, m); }
                        if (gap > 0f)
                        {
                            if (!down && Time.time >= nextTap) { InputSystem.QueueStateEvent(mouse, Down()); down = true; }
                            else if (down && Time.time >= nextTap + 0.04f) { InputSystem.QueueStateEvent(mouse, Up()); down = false; nextTap += gap; }
                        }
                        await UniTask.Yield(PlayerLoopTiming.Update, ct);
                    }
                    InputSystem.QueueStateEvent(mouse, Up());
                    float releasedAt = Time.time;
                    int before = slashes.Count;
                    float r0 = Time.realtimeSinceStartup;
                    while (Time.realtimeSinceStartup - r0 < 1.2f) { rs?.SetZenithAttackSpeed(0f); RefillAll(); Steer(p, aim); await UniTask.Yield(PlayerLoopTiming.Update, ct); }
                    int after = 0;
                    for (int i = before; i < slashes.Count; i++) if (slashes[i].t > releasedAt + 0.02f) after++;
                    // 단계 넘김별 · 콤보 끊김 · 헛휘두름 — 누르는 동안만
                    var sum = new SortedDictionary<string, (float total, int n)>();
                    float all = 0f; int nAll = 0, resets = 0, miss = 0;
                    if (lastStep < 0) foreach (var sl in slashes) lastStep = Mathf.Max(lastStep, sl.step);
                    for (int i = 0; i < before; i++)
                    {
                        bool hit = false;
                        foreach (var h in hits) if (h >= slashes[i].t - 0.05f && h <= slashes[i].t + 0.25f) { hit = true; break; }
                        if (!hit) miss++;
                        if (i == 0) continue;
                        float d = slashes[i].t - slashes[i - 1].t;
                        int a = slashes[i - 1].step, b = slashes[i].step;
                        string k = b == 0 ? $"{a + 1}→1" : $"{a + 1}→{b + 1}";
                        if (b == 0 && a < lastStep) resets++;
                        sum.TryGetValue(k, out var v);
                        sum[k] = (v.total + d, v.n + 1);
                        all += d; nAll++;
                    }
                    var detail = new StringBuilder();
                    foreach (var kv in sum) detail.Append($" {kv.Key} {kv.Value.total / kv.Value.n:0.00}");
                    float span = before > 1 ? slashes[before - 1].t - slashes[0].t : 0f;
                    float perSec = span > 0f ? (before - 1) / span : 0f;
                    sb.AppendLine($"  G {name} {mode} — 베기 {before}번 · 초당 {perSec:0.00} · 평균 간격 {(nAll > 0 ? all / nAll : 0f):0.00}초 · 공속 평균 {(spdN > 0 ? spdSum / spdN : 1f):0.00}({spdMin:0.00} ~ {spdMax:0.00})"
                                  + $"{(rs != null && rs.AttackSpeedOverflowDamage > 0f ? $"(넘친 몫 → 피해 +{rs.AttackSpeedOverflowDamage * 100f:0}%)" : "")}"
                                  + $" · 휘두름 공속 {(rs != null ? rs.SwingAttackSpeed : 1f):0.00}(상한 {(rs != null ? rs.SwingSpeedCap : 2f):0.00}){(rs != null && rs.SwingSpeedOverflowDamage > 0f ? $" → 근접 피해 +{rs.SwingSpeedOverflowDamage * 100f:0}%" : "")}"
                                  + $" · 뗀 뒤 휘두름 {after} · 막타 전 끊김 {resets} · 헛휘두름 {miss} · 단계별(초):{detail}");
                }
                if (added != 0f) { rs?.AddSkillAttackSpeed(-added, gen); added = 0f; }
                DetachAll();
            }
        }
        finally
        {
            if (added != 0f) rs?.AddSkillAttackSpeed(-added, gen);
            rs?.SetZenithAttackSpeed(0f);
            InputSystem.QueueStateEvent(mouse, new MouseState());
            InputSystem.settings.editorInputBehaviorInPlayMode = editorMode0;
            InputSystem.settings.backgroundBehavior = background0;
            Application.logMessageReceived -= onLog;
            DetachAll();
        }
    }

    /// <summary>
    /// H — 근접 버팀(10-29 설계 §2) · 시작 선물(§6-1). 제단 단계는 MeleeWard.ProbeAltarLevel = 3(세이브 안 건드림) · 수호 룬은 효과를 직접 붙인다.
    ///   a 상한(보호막 25% · 받는 피해 감소 40%) b 강철 의지 16% · 방을 옮겨도 쌓이지 않음 c 수호의 돌 +5% · 다음 방에 걷힘
    ///   d 근접 자세 −14%(카타나) e 모든 감소를 켜도 40% f 성벽 3장 = +12%(2겹까지) g 숨 고르기 4초 → 4% · 더 안 참 · 맞은 뒤 다시
    ///   h 맞지 않고 때리기만 3.5초 — 보호막이 늘지 않음 i 시작 선물 카타나 금 2장 · 고르면 증강 +1 · 강화 단계 그대로 / 활은 false j 활 들면 자세 감소 없음
    /// </summary>
    private static async UniTask W99Ward(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        sb.AppendLine("── 근접 버팀(H) ──");
        var run = GameRunBootstrapper.Instance.Run;
        var rs = p.RuntimeStats;
        var listField = typeof(ItemEffectManager).GetField("_activeEffects", BindingFlags.NonPublic | BindingFlags.Instance);
        var ctxField  = typeof(ItemEffectManager).GetField("_ctx", BindingFlags.NonPublic | BindingFlags.Instance);
        void ZeroShield() => rs.AbsorbWithShield(100000);
        float Sh() => (float)rs.Shield / Mathf.Max(1, rs.MaxHp);
        MeleeWard.ProbeAltarLevel = 3;
        try
        {
            if (!await W97Setup(p, Katana, "", 9, ct, 0)) { sb.AppendLine("  H 카타나 장착 실패"); return; }   // Rebuild — 제단 3칸이 붙는다
            DetachAll();
            var mgr = run.EffectManager;
            var list = (List<IItemEffect>)listField.GetValue(mgr);
            var ctx  = (ItemEffectContext)ctxField.GetValue(mgr);
            T Rune<T>(T e) where T : ItemEffectBase { list.Add(e); e.OnActivate(ctx); return e; }
            ItemEffectSlot Slot(string type, float v, float v2 = 0f, float dur = 0f, int stack = 0)
                => new ItemEffectSlot { effectType = type, trigger = "Always", value = v, value2 = v2, duration = dur, maxStack = stack };

            Check(sb, $"H-a 상한 — 보호막 {rs.ShieldCap}/{rs.MaxHp} = {(float)rs.ShieldCap / rs.MaxHp:0.00}(0.25) · 받는 피해 감소 상한 {PlayerRuntimeStats.MaxDamageReduction:0.00}(0.40)",
                  Mathf.Abs((float)rs.ShieldCap / rs.MaxHp - 0.25f) < 0.01f && Mathf.Approximately(PlayerRuntimeStats.MaxDamageReduction, 0.4f));

            ZeroShield();
            mgr.OnRoomEnter(); float b1 = Sh();
            mgr.OnRoomEnter(); float b2 = Sh();
            Check(sb, $"H-b 강철 의지 III — 방 입장 보호막 {b1:0.000}(0.16) · 다음 방 {b2:0.000}(0.16 · 쌓이지 않음)", Mathf.Abs(b1 - 0.16f) < 0.01f && Mathf.Abs(b2 - 0.16f) < 0.01f);

            Rune(new WardClearShieldEffect(Slot("WardClearShield", 0.05f)));
            mgr.OnRoomClear(); float c1 = Sh();
            mgr.OnRoomEnter(); float c2 = Sh();
            Check(sb, $"H-c 수호의 돌 — 클리어 {c1:0.000}(0.21) · 다음 방 {c2:0.000}(0.16 · 방까지 몫 걷힘 · 강철 의지 다시)", Mathf.Abs(c1 - 0.21f) < 0.01f && Mathf.Abs(c2 - 0.16f) < 0.01f);

            await Wait(0.3f, ct);
            float drK = rs.DamageReduction;
            Check(sb, $"H-d 근접 자세 III — 카타나 받는 피해 감소 {drK:0.00}(≥ 0.14)", drK >= 0.139f);

            var fin = Rune(new WardFinisherGuardEffect(Slot("WardFinisherGuard", 0.2f, 4f, 1f)));
            LastBreathEffect lb = null;
            foreach (var e in list) if (e is LastBreathEffect l) lb = l;
            typeof(WardFinisherGuardEffect).GetField("_until", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(fin, Time.time + 3f);
            if (lb != null) typeof(LastBreathEffect).GetField("_until", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(lb, Time.time + 3f);
            await Wait(0.3f, ct);
            float drAll = rs.DamageReduction;
            Check(sb, $"H-e 자세 0.14 + 맞받이 0.2 + 버티는 숨 0.5(버티는 숨 붙음 {lb != null}) — 받는 피해 감소 {drAll:0.00}(상한 0.40)", lb != null && Mathf.Approximately(drAll, 0.4f));
            typeof(WardFinisherGuardEffect).GetField("_until", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(fin, -999f);
            if (lb != null) typeof(LastBreathEffect).GetField("_until", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(lb, -999f);

            int hp0 = rs.MaxHp;
            for (int i = 0; i < 3; i++) Rune(new WardRampartEffect(Slot("WardRampart", 0.06f, stack: 2)));
            await Wait(0.3f, ct);
            float hpRatio = (float)rs.MaxHp / Mathf.Max(1, hp0);
            Check(sb, $"H-f 성벽 3장 — 최대 체력 {hp0} → {rs.MaxHp}(×{hpRatio:0.000} · 2겹까지 1.12)", hpRatio > 1.10f && hpRatio < 1.14f);

            ZeroShield();
            var breath = Rune(new WardSteadyBreathEffect(Slot("WardSteadyBreath", 0.04f, dur: 4f)));
            await Wait(4.4f, ct); float g1 = Sh();
            await Wait(4.4f, ct); float g2 = Sh();
            breath.OnPostTakeDamage(ctx, default);   // 맞음 — 4초 다시
            await Wait(2f, ct); float g3 = Sh();
            await Wait(2.6f, ct); float g4 = Sh();
            Check(sb, $"H-g 숨 고르기 — 4초 뒤 {g1:0.000}(0.04) · 또 4초 {g2:0.000}(그대로) · 맞고 2초 {g3:0.000}(그대로) · 4.6초 {g4:0.000}(0.08)",
                  Mathf.Abs(g1 - 0.04f) < 0.01f && Mathf.Abs(g2 - g1) < 0.005f && Mathf.Abs(g3 - g1) < 0.005f && Mathf.Abs(g4 - 0.08f) < 0.01f);

            // h 맞지 않고 때리기만 — 보호막이 늘면 안 된다(숨 고르기는 방금 맞은 것으로 다시 세게 하고 3.5초만)
            ZeroShield();
            breath.OnPostTakeDamage(ctx, default);
            s_home = p.transform.position;
            Vector3 f0 = Flat(p.transform.forward);
            var orc = await TrackOrcAsync(s_home + f0 * 2.2f, ct);
            HideStrays();
            int gains = 0, last = rs.Shield;
            await W97Swing(p, orc != null ? orc.M.transform.position : s_home + f0 * 2.2f, 3.5f, ct, () => { if (rs.Shield > last) gains++; last = rs.Shield; });
            Check(sb, $"H-h 때리기만 3.5초(적중 피해 {(orc != null ? orc.Dealt : 0f):0}) — 보호막 늘어남 {gains}번(0)", gains == 0 && orc != null && orc.Dealt > 0f);
            DetachAll();

            // i 시작 선물
            var w = p.WeaponManager.CurrentWeaponData;
            int augs0 = 0; foreach (var _ in w.AugmentList()) augs0++;
            int hi0 = w.highestEnhanceReached;
            bool ok = MeleeAugmentDraft.OfferGift(w, AugmentTier.Gold, 2, new System.Random(29));
            var offer = MeleeAugmentDraft.Pending;
            string cards = "";
            bool gold = offer != null && offer.Cards.Length == 2;
            if (offer != null) foreach (var c in offer.Cards) { cards += $" {c.Def?.Name}({(c.Def != null ? MeleeAugmentDef.TierName(c.Def.Tier) : "?")})"; if (c.Def == null || (c.Def.Tier != AugmentTier.Gold && !c.Def.IsLegend)) gold = false; }
            MeleeAugmentDraft.Choose(0);
            Managers.UI?.CloseAllPopupUI();
            int augs1 = 0; foreach (var _ in w.AugmentList()) augs1++;
            Check(sb, $"H-i 시작 선물 카타나 — 열림 {ok} · 카드{cards} · 고른 뒤 증강 {augs0} → {augs1} · 받은 강화 단계 {hi0} → {w.highestEnhanceReached}(그대로)",
                  ok && gold && augs1 == augs0 + 1 && w.highestEnhanceReached == hi0);

            // j 활 — 선물 false · 자세 감소 없음
            if (await EquipAsync(p, Bow, ct))
            {
                await Wait(0.5f, ct);
                bool bowGift = MeleeAugmentDraft.OfferGift(p.WeaponManager.CurrentWeaponData);
                float drB = rs.DamageReduction;
                Check(sb, $"H-j 활 — 시작 선물 {bowGift}(false) · 받는 피해 감소 {drB:0.00}(카타나 {drK:0.00}보다 0.14 낮아야)", !bowGift && drK - drB > 0.13f);
            }
        }
        finally
        {
            MeleeWard.ProbeAltarLevel = -1;
            ZeroShield();
            run.EffectManager?.Rebuild();
            DetachAll();
        }
    }

    /// <summary>
    /// I — 화살 폭발 파츠만(10-29 2a 「화룡 프레임 튐이 화살 폭발인지 운석인지」). 화룡 없이 허브 오크 다섯 마리(앞 6 m 둘레)에 활로 각 8초:
    ///   ① 파츠 없음 ② 폭발만 최대 ③ 파츠 전부 최대. 0.5초마다 프레임 평균 · 최대 ms · 폭발 그림 · 맞음 그림 · 화살 수. 첫 폭발(풀 첫 생성) 프레임도 따로.
    /// </summary>
    private static async UniTask W99ArrowBurst(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        sb.AppendLine("── 화살 폭발 파츠(I) ──");
        var state = RangedPartsState.Current;
        try
        {
            if (!await EquipAsync(p, Bow, ct)) { sb.AppendLine("  I 활 장착 실패"); return; }
            await Wait(0.8f, ct);
            DetachAll();
            s_home = p.transform.position;
            Vector3 f0 = Flat(p.transform.forward), r0 = Vector3.Cross(Vector3.up, f0);
            for (int i = 0; i < 5; i++) await TrackOrcAsync(s_home + f0 * 6f + r0 * ((i - 2) * 1.4f), ct);
            HideStrays();
            await Wait(1f, ct);
            Vector3 aim = s_home + f0 * 6f;
            foreach (var (title, parts) in new[] { ("① 파츠 없음", ""), ("② 폭발만 최대", "part_explode"), ("③ 파츠 전부 최대", "*") })
            {
                ResetParts(state);
                if (parts.Length > 0 && Managers.WeaponParts?.All != null)
                    foreach (var e in Managers.WeaponParts.All)
                        if (parts == "*" || e.part_id == parts) state.Equip(e.part_id, Mathf.Max(1, e.max_level));
                await Wait(0.5f, ct);
                sb.AppendLine($"  {title}");
                float t0 = Time.realtimeSinceStartup, next = 0.5f, sum = 0f, max = 0f, firstBurst = -1f;
                int frames = 0;
                while (Time.realtimeSinceStartup - t0 < 8f)
                {
                    float now = Time.realtimeSinceStartup - t0, dt = Time.unscaledDeltaTime * 1000f;
                    sum += dt; max = Mathf.Max(max, dt); frames++;
                    RefillAll(); Steer(p, aim);
                    if (p.InputBuffer.Count < 2) p.InputBuffer.Push(Command.Light);
                    if (now >= next)
                    {
                        next += 0.5f;
                        int burst = 0, hit = 0, arrows = 0;
                        foreach (var ps in UnityEngine.Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
                        {
                            if (ps == null || !ps.gameObject.activeInHierarchy || !ps.IsAlive(false) || ps.transform.parent != null && ps.transform.parent.GetComponent<ParticleSystem>() != null) continue;
                            string n = ps.name;
                            if (n.StartsWith("Effect_17_Explosion", StringComparison.Ordinal)) burst++;
                            else if (n.StartsWith("ArrowHitYellow", StringComparison.Ordinal)) hit++;
                        }
                        foreach (var a in UnityEngine.Object.FindObjectsByType<BasicArrow>(FindObjectsSortMode.None)) if (a != null && a.gameObject.activeInHierarchy) arrows++;
                        if (firstBurst < 0f && burst > 0) firstBurst = max;
                        sb.AppendLine($"    {now:0.0}초 · 프레임 평균 {(frames > 0 ? sum / frames : 0f):0.0}ms 최대 {max:0.0}ms · 폭발 그림 {burst} · 맞음 그림 {hit} · 화살 {arrows}");
                        sum = 0f; max = 0f; frames = 0;
                    }
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                }
                p.InputBuffer.Clear();
                await Wait(1.5f, ct);
            }
        }
        finally
        {
            ResetParts(state);
            DetachAll();
        }
    }

    private static void ResetParts(RangedPartsState state)
    {
        if (state == null || Managers.WeaponParts?.All == null) return;
        foreach (var e in Managers.WeaponParts.All)
        {
            if (state.LevelOf(e.part_id) > 0) state.Unequip(e.part_id);
        }
    }

    /// <summary>
    /// J — 카타나 검풍 검기 「가끔 한 번 더」(10-29 사용자 · 4c 전달). 검풍 확률 100% · 오크 셋 · 5초 휘두름 + 3.2초(마지막 그림이 지워질 때까지).
    /// 옛 동작(일회성 이펙트 반복 그대로 · <see cref="RelicStateVfx.ProbeKeepLoop"/>) ↔ 고친 동작: 검기 그림 인스턴스 · 보인 횟수(입자 0 → 생김) ·
    /// 다시 터짐 = 보인 횟수 − 인스턴스 · 굴림(휘두름 일련번호별) · 검기 판정 줄 · 맞힘. 다시 터진 순간 · 고친 쪽 같은 때 사진.
    /// </summary>
    private static async UniTask W99GustReplay(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        sb.AppendLine("── 검풍 검기 다시 터짐(J) ──");
        try
        {
            foreach (bool keepLoop in new[] { true, false })
            {
                RelicStateVfx.ProbeKeepLoop = keepLoop;
                SwordGustAugment.ProbeChance = 100f;
                if (!await W97Setup(p, Katana, MeleeAugmentCatalog.KatanaGust + ":1", 15, ct, 0)) { sb.AppendLine("  J 카타나 장착 실패"); return; }   // 형(칼바람 풍절)이 섞이지 않게
                DetachAll();
                s_home = p.transform.position;
                Vector3 fw = Flat(p.transform.forward), right = Vector3.Cross(Vector3.up, fw);
                for (int i = 0; i < 3; i++) await TrackOrcAsync(s_home + fw * 2.2f + right * ((i - 1) * 1.2f), ct);
                HideStrays();
                await Wait(0.5f, ct);
                var rt = WeaponFormRuntime.Of(p);
                int rolls = 0, waves = 0, waveHits = 0;
                var rollsBySerial = new Dictionary<int, int>();
                Application.LogCallback onLog = (msg, st, type) =>
                {
                    if (msg.StartsWith("[MeleeAugment] 검풍 굴림", StringComparison.Ordinal))
                    {
                        rolls++;
                        int s = rt != null ? rt.SwingSerial : -1;
                        rollsBySerial[s] = rollsBySerial.TryGetValue(s, out int c) ? c + 1 : 1;
                    }
                    else if (msg.StartsWith("[MeleeAugment] 검풍 — 피해", StringComparison.Ordinal))
                    {
                        waves++;
                        int k = msg.IndexOf("맞힘 ", StringComparison.Ordinal);
                        if (k >= 0 && int.TryParse(msg.Substring(k + 3).Trim(), out int n)) waveHits += n;
                    }
                };
                var census = new FxCensus(k => k.StartsWith("Slash wave", StringComparison.Ordinal));
                float firstSeen = -1f;
                bool shot = false;
                string photo = keepLoop ? "Temp/ui_shots/W99_J_old_replay.png" : "Temp/ui_shots/W99_J_fixed_2s.png";
                void Tick()
                {
                    census.Tick();
                    var stat = census.Total;
                    if (firstSeen < 0f && stat.Instances > 0) firstSeen = Time.realtimeSinceStartup;
                    bool when = keepLoop ? stat.Shows > stat.Instances : firstSeen > 0f && Time.realtimeSinceStartup - firstSeen >= 2.05f;
                    if (!shot && when && Camera.main != null)
                    {
                        shot = true;
                        Directory.CreateDirectory("Temp/ui_shots");
                        CaptureMain(Camera.main, photo);
                    }
                }
                Application.logMessageReceived += onLog;
                try
                {
                    await W97Swing(p, s_home + fw * 2.2f, 5f, ct, Tick);
                    float t0 = Time.realtimeSinceStartup;
                    while (Time.realtimeSinceStartup - t0 < 3.2f) { Tick(); RefillAll(); await UniTask.Yield(PlayerLoopTiming.Update, ct); }
                }
                finally { Application.logMessageReceived -= onLog; }
                int maxPerSwing = 0;
                foreach (var kv in rollsBySerial) maxPerSwing = Mathf.Max(maxPerSwing, kv.Value);
                var tot = census.Total;
                int replays = tot.Shows - tot.Instances;
                string line = $"{(keepLoop ? "옛 동작(반복 그대로)" : "고친 동작(한 번만)")} — 검기 그림 {tot.Instances}개 · 보임 {tot.Shows}번(다시 터짐 {replays}) · "
                            + $"굴림 {rolls}(휘두름 {rollsBySerial.Count} · 한 휘두름 최대 {maxPerSwing}) · 검기 판정 {waves}줄 · 맞힘 {waveHits} · 사진 {(shot ? photo : "없음")}";
                if (keepLoop) sb.AppendLine($"  J-1 {line}{(replays > 0 ? " ← 재현" : " ← 재현 안 됨")}");
                else Check(sb, "J-2 " + line, tot.Instances > 0 && replays == 0 && maxPerSwing <= 1 && waves == tot.Instances);
                DetachAll();
            }
        }
        finally
        {
            RelicStateVfx.ProbeKeepLoop = false;
            SwordGustAugment.ProbeChance = -1f;
            DetachAll();
        }
    }

    /// <summary>
    /// L — 전설 룬 18종 이펙트 리소스 · 판정(10-29 사용자 「레전더리 룬들의 이펙트 리소스가 제대로 나오는지 · 판정이 있다면 작동하는지」).
    /// 95와 같은 판(오크 무리 · 단일 · 스킬 · 대시 · 피격 · 처치 · 저스트 흉내)에서 존 2 · 4단계 각 6초:
    ///   추가타(<see cref="CombatQuery.ProbeSynergyHit"/> — 수 · 대상 수 · 합) · 걸린 상태(점화 · 독 · 빙결 · 속박) ·
    ///   새로 보인 이펙트(이름 · 인스턴스 · 보인 횟수 · 입자 최대 · 보인 시간 · 가로 반경 · 빈 재질 · 분홍 · 켜진 충돌체 · 판정 부품) · 존 4 첫 추가타 0.15초 뒤 사진.
    /// Temp/legendfx_only.txt에 effect_type을 줄마다 적으면 그것만.
    /// </summary>
    private static async UniTask W99LegendFx(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        sb.AppendLine("── 전설 룬 이펙트 · 판정(L · 존 2 · 4 · 각 6초) ──");
        Vector3 open = p.transform.position + Flat(p.transform.forward) * 8f;
        Quaternion openRot = Quaternion.LookRotation(Flat(p.transform.forward));
        typeof(PlayerController).GetField("debugInvincible", Inst)?.SetValue(p, true);
        var cases = new[]
        {
            new LegendCase { Name = "불바다", Type = "FireLegendAoe", Trigger = "OnKillPool", Zone = "FIRE", Weapon = Great, V = 0.35f, V2 = 4f, V3 = 2.5f, Kills = true },
            new LegendCase { Name = "불사조의 맥박", Type = "FireLegendSingle", Trigger = "OnHitHeat", Zone = "FIRE", Weapon = Katana, V = 6f, V2 = 20f, V3 = 1.2f, Targets = 1 },
            new LegendCase { Name = "불꽃 질주", Type = "FireLegendProjectile", Trigger = "OnDashTrail", Zone = "FIRE", Weapon = Great, V = 0.2f, V2 = 1.3f, V3 = 2f, Dodge = true },
            new LegendCase { Name = "얼려 깨는 주문", Type = "IceLegendAoe", Trigger = "OnSkillFreeze", Zone = "ICE", Weapon = Katana, V = 2f, V2 = 1.5f, V3 = 0.8f, Skills = true },
            new LegendCase { Name = "서리 처형", Type = "IceLegendSingle", Trigger = "OnCritExecute", Zone = "ICE", Weapon = Great, V = 1f, V2 = 0.15f, V3 = 6f },
            new LegendCase { Name = "얼음 무대", Type = "IceLegendField", Trigger = "OnDashStage", Zone = "ICE", Weapon = Katana, V = 1.5f, V2 = 0.2f, V3 = 2.5f, Dodge = true, Perfect = true },   // 저스트 회피 = 빙판이 깨짐(피해)
            new LegendCase { Name = "번개 사슬", Type = "ElecLegendAoe", Trigger = "OnHitChain", Zone = "ELECTRIC", Weapon = Katana, V = 0.04f, V2 = 15f, V3 = 3f },
            new LegendCase { Name = "축전 포박", Type = "ElecLegendSingle", Trigger = "OnCritStore", Zone = "ELECTRIC", Weapon = Great, V = 1.5f, V2 = 6f, V3 = 0.4f },
            new LegendCase { Name = "뇌전 탄창", Type = "ElecLegendProjectile", Trigger = "OnSkillOrb", Zone = "ELECTRIC", Weapon = Great, V = 1.2f, V2 = 3f, V3 = 0.1f, Skills = true, Targets = 1 },
            new LegendCase { Name = "포자 전염", Type = "GrassLegendAoe", Trigger = "OnHitSpread", Zone = "GRASS", Weapon = Katana, V = 0.4f, V2 = 0.04f, V3 = 3f },
            new LegendCase { Name = "뿌리 표적(활)", Type = "GrassLegendSingle", Trigger = "OnRangedMark", Zone = "GRASS", Weapon = Bow, Slot = PlayerWeaponManager.Slot1, V = 0.35f, V2 = 8f, V3 = 4f },
            new LegendCase { Name = "독안개 사수(활)", Type = "GrassLegendProjectile", Trigger = "OnRangedMist", Zone = "GRASS", Weapon = Bow, Slot = PlayerWeaponManager.Slot1, V = 0.35f, V2 = 6f, V3 = 2.5f },
            new LegendCase { Name = "빛의 반격", Type = "LightLegendAoe", Trigger = "OnHurtShield", Zone = "LIGHT", Weapon = Katana, V = 0.5f, V2 = 6f, V3 = 0.12f, Hurt = true },
            new LegendCase { Name = "심판의 표식", Type = "LightLegendSingle", Trigger = "OnCritMark", Zone = "LIGHT", Weapon = Great, V = 1.6f, V2 = 5f, V3 = 8f, Targets = 1 },
            new LegendCase { Name = "호위 성검(활)", Type = "LightLegendProjectile", Trigger = "OnRangedGuard", Zone = "LIGHT", Weapon = Bow, Slot = PlayerWeaponManager.Slot1, V = 0.6f, V2 = 8f, V3 = 2f },
            new LegendCase { Name = "보복의 심연", Type = "DarkLegendAoe", Trigger = "OnHurtStore", Zone = "DARK", Weapon = Katana, V = 3f, V2 = 0.6f, V3 = 0.3f, Hurt = true },
            new LegendCase { Name = "그림자 걸음", Type = "DarkLegendSingle", Trigger = "OnDodgeShadow", Zone = "DARK", Weapon = Great, V = 0.8f, V2 = 8f, V3 = 2.5f, Perfect = true, Targets = 1 },
            new LegendCase { Name = "영혼 수확", Type = "DarkLegendProjectile", Trigger = "OnKillSoul", Zone = "DARK", Weapon = Great, V = 0.8f, V2 = 8f, V3 = 0.4f, Kills = true, Skills = true },
        };
        var only = File.Exists("Temp/legendfx_only.txt") ? new HashSet<string>(File.ReadAllLines("Temp/legendfx_only.txt")) : null;
        int synHits = 0;
        float synSum = 0f;
        var synTargets = new HashSet<GameObject>();
        Action<GameObject, float, RuneElement?> onSyn = (go, amount, el) => { synHits++; synSum += amount; if (go != null) synTargets.Add(go); };
        CombatQuery.ProbeSynergyHit += onSyn;
        string curWeapon = null;
        try
        {
            // 카탈로그 칸마다 실행 중 재질 · 셰이더(빈 칸 · 재질 없음 · 지원 안 되는 셰이더) — 꼬리만 그리는 입자(본체 재질 비움)는 팩 설정이라 뺀다
            LegendaryRuntime.EnsureExists();
            for (float waited = 0f; LegendaryRuntime.Catalog == null && waited < 3f; waited += 0.1f) await Wait(0.1f, ct);
            var cat = LegendaryRuntime.Catalog;
            if (cat == null) sb.AppendLine("  카탈로그 못 읽음");
            else
            {
                var bad = new List<string>();
                int slots = 0;
                foreach (var f in typeof(LegendaryVfxCatalog).GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (f.FieldType != typeof(GameObject)) continue;
                    slots++;
                    var go = f.GetValue(cat) as GameObject;
                    if (go == null) { bad.Add($"{f.Name}(빈 칸)"); continue; }
                    foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                    {
                        if (!r.enabled) continue;
                        bool trailOnly = r is ParticleSystemRenderer pr && r.TryGetComponent<ParticleSystem>(out var ps0) && (ps0.trails.enabled || pr.renderMode == ParticleSystemRenderMode.None);
                        var mats = r.sharedMaterials;
                        for (int i = 0; i < mats.Length; i++)
                        {
                            var m = mats[i];
                            if (m == null) { if (!(trailOnly && i == 0)) bad.Add($"{f.Name}/{r.name}(재질 없음 · 칸 {i})"); }
                            else if (m.shader == null || !m.shader.isSupported) bad.Add($"{f.Name}/{r.name}(셰이더 {(m.shader != null ? m.shader.name : "없음")})");
                        }
                    }
                }
                sb.AppendLine($"  카탈로그 {slots}칸 — {(bad.Count == 0 ? "빈 칸 · 재질 없음 · 안 되는 셰이더 없음" : "★" + string.Join(" · ", bad))}");
            }
            foreach (var c in cases)
            {
                if (only != null && only.Count > 0 && !only.Contains(c.Type)) continue;
                if (curWeapon != c.Weapon)
                {
                    if (!await EquipAsync(p, c.Weapon, ct, c.Slot)) { sb.AppendLine($"  장착 실패 {c.Weapon}"); continue; }
                    var w = p.WeaponManager.Weapon0Data;
                    w.augments = ""; w.formTier = 0; w.enhanceLevel = 0; w.RecomputeEnhancedStats();
                    WeaponFormRuntime.Of(p).Rebuild(); GameRunBootstrapper.Instance.Run.EffectManager.Rebuild();
                    curWeapon = c.Weapon;
                }
                var baseCensus = new FxCensus(null);   // 전설 없음(존 4) — 평타 · 존 룬 이펙트 · 존 추가타를 빼고 전설 몫만 본다
                synHits = 0;
                await Legend95Once(p, c, 4, false, open, openRot, ct, _ => baseCensus.Tick());
                int baseHits = synHits;
                foreach (int tier in new[] { 2, 4 })
                {
                    var census = new FxCensus(null);
                    var states = new HashSet<string>();
                    synHits = 0; synSum = 0f; synTargets.Clear();
                    float firstHitAt = -1f;
                    int fields = 0, rings = 0, stages = 0;
                    bool shot = tier != 4;
                    await Legend95Once(p, c, tier, true, open, openRot, ct, t =>
                    {
                        census.Tick();
                        fields = Mathf.Max(fields, UnityEngine.Object.FindObjectsByType<LegendGroundField>(FindObjectsSortMode.None).Length);
                        rings  = Mathf.Max(rings,  UnityEngine.Object.FindObjectsByType<LegendOrbitRing>(FindObjectsSortMode.None).Length);
                        stages = Mathf.Max(stages, UnityEngine.Object.FindObjectsByType<IceZoneField>(FindObjectsSortMode.None).Length);
                        foreach (var tr in s_tracks)
                        {
                            var st = tr.M != null ? tr.M.Status : null;
                            if (st == null) continue;
                            if (st.HasDot("lava_pool")) states.Add("웅덩이 점화");
                            if (st.HasDot("ignite")) states.Add("점화");
                            if (st.HasDot("item_poison")) states.Add("독");
                            if (st.HasCc("freeze")) states.Add("빙결");
                            if (st.HasCc("bind")) states.Add("속박");
                        }
                        if (firstHitAt < 0f && synHits > 0) firstHitAt = t;
                        if (!shot && Camera.main != null && ((firstHitAt >= 0f && t >= firstHitAt + 0.15f) || t >= 4f))
                        {
                            shot = true;
                            Directory.CreateDirectory("Temp/ui_shots");
                            CaptureMain(Camera.main, $"Temp/ui_shots/W99_L_{c.Type}.png");
                        }
                    });
                    sb.AppendLine($"  {c.Name}({c.Type}) 존{tier}: 추가타 {synHits}번(전설 없음 존4 {baseHits}) · 대상 {synTargets.Count} · 합 {synSum:0}"
                                + $"{(states.Count > 0 ? " · 상태 " + string.Join("/", states) : "")} · 첫 추가타 {(firstHitAt >= 0f ? firstHitAt.ToString("0.0") + "초" : "없음")}"
                                + $" · 장판 {fields} · 고리 {rings} · 빙판 {stages}");
                    sb.Append(census.Report("    ", 10, baseCensus));
                    Debug.Log($"[WeaponFormProbe] 99 L {c.Type} 존{tier} 추가타 {synHits}");
                }
            }
        }
        finally
        {
            CombatQuery.ProbeSynergyHit -= onSyn;
            MerlinRuneBridge.Instance?.OnZoneCellsUpdated(new Dictionary<string, int>());
            typeof(PlayerController).GetField("debugInvincible", Inst)?.SetValue(p, false);
            Time.timeScale = 1f;
            DetachAll();
        }
    }

    /// <summary>
    /// 이펙트 집계(99 J · L) — 입자가 있는 오브젝트를 「(Clone)」 꼭대기(프리팹 인스턴스)로 묶어 이름별로 센다.
    /// 인스턴스 · 보인 횟수(입자 0 → 생김 — 풀 재사용 · 반복 재생도 한 번씩) · 입자 최대 · 한 번 보인 최대 시간 · 가로 반경(그 인스턴스 자리에서 입자 경계까지) ·
    /// 빈 재질 · 분홍(지원 안 되는 셰이더) · 켜진 충돌체 · 판정 부품(ColliderInstance · 행동 SO가 붙은 EffectBehaviour).
    /// </summary>
    private sealed class FxCensus
    {
        public sealed class Stat
        {
            public int Instances, Shows, MaxParticles, Colliders, Judges;
            public float MaxSeconds, MaxReach;
            public string NullMat;
            public bool Pink;
        }

        private sealed class Root
        {
            public Transform Tf;
            public string Key;
            public int Count;
            public bool Alive, HasBounds;
            public float Since;
            public Bounds B;
        }

        private readonly Func<string, bool> _filter;
        private readonly Dictionary<string, Stat> _stats = new();
        private readonly Dictionary<int, Root> _roots = new();
        private readonly Dictionary<int, Root> _rootOfPs = new();

        public FxCensus(Func<string, bool> filter) => _filter = filter;

        /// <summary>걸러진 이름 전체 합.</summary>
        public Stat Total
        {
            get
            {
                var s = new Stat();
                foreach (var v in _stats.Values) { s.Instances += v.Instances; s.Shows += v.Shows; s.MaxParticles = Mathf.Max(s.MaxParticles, v.MaxParticles); }
                return s;
            }
        }

        public void Tick()
        {
            float now = Time.realtimeSinceStartup;
            foreach (var r in _roots.Values) { r.Count = 0; r.HasBounds = false; }
            foreach (var ps in UnityEngine.Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
            {
                if (ps == null || !ps.gameObject.activeInHierarchy) continue;
                if (!_rootOfPs.TryGetValue(ps.GetInstanceID(), out var root) || root.Tf == null)
                {
                    var tf = ps.transform;
                    while (tf.parent != null && tf.parent.name.EndsWith("(Clone)", StringComparison.Ordinal)) tf = tf.parent;
                    if (!_roots.TryGetValue(tf.GetInstanceID(), out root))
                    {
                        string key = tf.name.Replace("(Clone)", "").Trim();
                        if (_filter != null && !_filter(key)) continue;
                        root = new Root { Tf = tf, Key = key };
                        _roots[tf.GetInstanceID()] = root;
                        var s0 = StatOf(key);
                        s0.Instances++;
                        int cols = 0, judges = 0;
                        foreach (var col in tf.GetComponentsInChildren<Collider>()) if (col.enabled) cols++;
                        foreach (var ci in tf.GetComponentsInChildren<ColliderInstance>()) if (ci.enabled) judges++;
                        foreach (var eb in tf.GetComponentsInChildren<EffectBehaviour>()) if (eb.enabled && eb.behaviorSO != null) judges++;
                        s0.Colliders = Mathf.Max(s0.Colliders, cols);
                        s0.Judges = Mathf.Max(s0.Judges, judges);
                    }
                    _rootOfPs[ps.GetInstanceID()] = root;
                }
                int n = ps.particleCount;
                if (n <= 0) continue;
                root.Count += n;
                if (!ps.TryGetComponent<ParticleSystemRenderer>(out var pr) || !pr.enabled) continue;
                var m = pr.sharedMaterial;
                var stat = StatOf(root.Key);
                if (m == null) { if (pr.renderMode != ParticleSystemRenderMode.None && !ps.trails.enabled) stat.NullMat = ps.name; }
                else if (m.shader == null || !m.shader.isSupported || m.shader.name == "Hidden/InternalErrorShader") stat.Pink = true;
                var b = pr.bounds;
                if (root.HasBounds) root.B.Encapsulate(b); else { root.B = b; root.HasBounds = true; }
            }
            foreach (var r in _roots.Values)
            {
                var s = StatOf(r.Key);
                bool alive = r.Count > 0;
                if (alive && !r.Alive) { s.Shows++; r.Since = now; }
                if (alive)
                {
                    s.MaxParticles = Mathf.Max(s.MaxParticles, r.Count);
                    s.MaxSeconds = Mathf.Max(s.MaxSeconds, now - r.Since);
                    if (r.HasBounds && r.Tf != null)
                    {
                        Vector3 c = r.Tf.position;
                        float reach = Mathf.Max(Mathf.Max(Mathf.Abs(r.B.min.x - c.x), Mathf.Abs(r.B.max.x - c.x)), Mathf.Max(Mathf.Abs(r.B.min.z - c.z), Mathf.Abs(r.B.max.z - c.z)));
                        s.MaxReach = Mathf.Max(s.MaxReach, reach);
                    }
                }
                r.Alive = alive;
            }
        }

        /// <summary>보인 이름들(많이 보인 순 max개). <paramref name="baseline"/>(전설 없음 판)이 있으면 거기서도 비슷하게 보인 이름은 뺀다(1.3배 + 1번 넘게 늘어난 것만 남김).</summary>
        public string Report(string indent, int max, FxCensus baseline = null)
        {
            var list = new List<KeyValuePair<string, Stat>>(_stats);
            list.RemoveAll(kv => kv.Value.Shows == 0
                              || baseline != null && baseline._stats.TryGetValue(kv.Key, out var b) && kv.Value.Shows <= b.Shows * 1.3f + 1f);
            list.Sort((a, b) => b.Value.Shows.CompareTo(a.Value.Shows));
            var sb = new StringBuilder();
            for (int i = 0; i < list.Count && i < max; i++)
            {
                var (k, s) = (list[i].Key, list[i].Value);
                int baseShows = baseline != null && baseline._stats.TryGetValue(k, out var bs) ? bs.Shows : 0;
                sb.AppendLine($"{indent}{k} — 인스턴스 {s.Instances} · 보임 {s.Shows}{(baseShows > 0 ? $"(전설 없음 {baseShows})" : "")} · 입자 최대 {s.MaxParticles} · 보인 시간 최대 {s.MaxSeconds:0.00}초 · 가로 {s.MaxReach:0.0} m"
                            + $"{(s.NullMat != null ? $" · ★빈 재질({s.NullMat})" : "")}{(s.Pink ? " · ★분홍" : "")}{(s.Colliders > 0 ? $" · ★켜진 충돌체 {s.Colliders}" : "")}{(s.Judges > 0 ? $" · ★판정 부품 {s.Judges}" : "")}");
            }
            if (list.Count > max) sb.AppendLine($"{indent}… 외 {list.Count - max}종");
            if (list.Count == 0) sb.AppendLine($"{indent}({(baseline != null ? "전설 없음 판보다 늘어난 이펙트 없음" : "보인 이펙트 없음")})");
            return sb.ToString();
        }

        private Stat StatOf(string key)
        {
            if (!_stats.TryGetValue(key, out var s)) { s = new Stat(); _stats[key] = s; }
            return s;
        }
    }

    /// <summary>
    /// M — 풀 수명 통일(10-29 풀 점검 4 · 5 · 스킬 9곳 DespawnAfter → PooledVfxLifetime). 무기 다섯(활 · 석궁 · 카타나 · 대검 · 무형검)마다
    /// E · R을 3초씩 쓰고 8초 쉰 뒤 @Pools/EffectPool의 켜진 자식(쓰는 중인 풀 이펙트)이 쓰기 전보다 늘지 않았나 · 쓰는 동안 몇 개 켜졌나 · 표적이 맞았나.
    /// </summary>
    private static async UniTask W99PoolLife(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        sb.AppendLine("── 풀 수명 통일(M) ──");
        var root = GameObject.Find("@Pools");
        var effects = root != null ? root.transform.Find("EffectPool") : null;
        if (effects == null) { sb.AppendLine("  M @Pools/EffectPool 없음"); return; }
        int Active()
        {
            int n = 0;
            foreach (Transform c in effects) if (c.gameObject.activeSelf) n++;
            return n;
        }
        string Names()
        {
            var names = new StringBuilder();
            int k = 0;
            foreach (Transform c in effects) if (c.gameObject.activeSelf && k++ < 6) names.Append(' ').Append(c.name);
            return names.ToString();
        }
        foreach (var (path, name, slot) in new[] { (Bow, "활", PlayerWeaponManager.Slot1), (Crossbow, "석궁", PlayerWeaponManager.Slot1),
                                                   (Katana, "카타나", PlayerWeaponManager.Slot0), (Great, "대검", PlayerWeaponManager.Slot0),
                                                   (Nameless, "무형검", PlayerWeaponManager.Slot0) })
        {
            if (!await EquipAsync(p, path, ct, slot)) { sb.AppendLine($"  M {name} 장착 실패"); continue; }
            DetachAll();
            s_home = p.transform.position;
            Vector3 fw = Flat(p.transform.forward);
            var orc = await TrackOrcAsync(s_home + fw * 4f, ct);
            HideStrays();
            await Wait(1.5f, ct);
            int before = Active(), peak = before;
            foreach (var cmd in new[] { Command.ESkill, Command.RSkill })
            {
                p.CooldownTracker.ResetCooldown(SkillType.E);
                p.CooldownTracker.ResetCooldown(SkillType.R);
                p.InputBuffer.Push(cmd);
                float t0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - t0 < 3f)
                {
                    RefillAll();
                    Steer(p, orc != null ? orc.M.transform.position : s_home + fw * 4f);
                    peak = Mathf.Max(peak, Active());
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                }
            }
            float t1 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t1 < 8f) { RefillAll(); await UniTask.Yield(PlayerLoopTiming.Update, ct); }
            int left = Active();
            Check(sb, $"M {name} — 쓰기 전 {before} · 쓰는 동안 최대 {peak} · 8초 쉰 뒤 {left}(쓰기 전 이하){(left > 0 ? " ·" + Names() : "")} · 표적 피해 {(orc != null ? orc.Dealt : 0f):0}",
                  left <= before && orc != null && orc.Dealt > 0f);
            DetachAll();
        }
    }

    /// <summary>
    /// N — 화살 사거리(10-29 사용자 「화살은 맞을 때까지 꽤 오래 남아 사거리가 무한에 가깝다」). 막힌 데 없는 쪽(16방향 중 벽까지 가장 먼 쪽)으로 활 평타 2.5초:
    ///   a 화살마다 처음 본 자리 → 사라진 자리 거리 최대(기본 20 m 근처 · 벽까지 거리가 더 짧으면 벽) · 사라지기 직전 크기 비율(1보다 작아야 — 흐려짐)
    ///   b 사거리 보너스 +5 m → 25 m 근처   c 다음 화살이 줄어든 채 나가지 않음(처음 본 크기)   d 플레이어 앞 · 옆 20 m 지점의 화면(뷰포트) 좌표
    /// </summary>
    private static readonly Collider[] s_diagCols = new Collider[64];

    private static async UniTask W99ArrowRange(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        sb.AppendLine("── 화살 사거리(N) ──");
        if (!await EquipAsync(p, Bow, ct, PlayerWeaponManager.Slot1)) { sb.AppendLine("  N 활 장착 실패"); return; }
        DetachAll();
        HideStrays();
        await Wait(1f, ct);
        s_home = p.transform.position;
        Vector3 best = Flat(p.transform.forward);
        float bestFree = 0f;
        for (int i = 0; i < 16; i++)
        {
            Vector3 d = Quaternion.Euler(0f, i * 22.5f, 0f) * Vector3.forward;
            float free = Physics.Raycast(s_home + Vector3.up, d, out var hit, 80f, ~0, QueryTriggerInteraction.Ignore) ? hit.distance : 80f;
            if (free > bestFree) { bestFree = free; best = d; }
        }
        var cam = Camera.main;
        string View(Vector3 w) { if (cam == null) return "-"; var v = cam.WorldToViewportPoint(w); return $"({v.x:0.00},{v.y:0.00}){(v.x >= 0f && v.x <= 1f && v.y >= 0f && v.y <= 1f && v.z > 0f ? "안" : "밖")}"; }
        Vector3 camFw = cam != null ? Flat(cam.transform.forward) : Vector3.forward, camRt = Vector3.Cross(Vector3.up, camFw);
        sb.AppendLine($"  N-d 화면 — 화면 위쪽 20 m {View(s_home + camFw * 20f)} · 옆 20 m {View(s_home + camRt * 20f)} · 위쪽 12 m(자동 조준) {View(s_home + camFw * 12f)} · 쏘는 쪽 벽까지 {bestFree:0.0} m");
        var bonusField = typeof(PlayerRuntimeStats).GetField("<RangedRangeBonus>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);
        float bonus0 = p.RuntimeStats.RangedRangeBonus;
        try
        {
            foreach (float bonus in new[] { 0f, 5f })
            {
                bonusField?.SetValue(p.RuntimeStats, bonus);
                var first = new Dictionary<BasicArrow, (Vector3 pos, float scale)>();
                var last = new Dictionary<BasicArrow, (Vector3 pos, float scale)>();
                float maxDist = 0f, minRatio = 1f, startScale = -1f, maxStart = 0f, endY = 0f, slope = 0f;
                int ended = 0;
                CombatSpawner.LogPitch = bonus == 0f;   // 첫 판만 — 수직 보정이 무엇을 노렸나
                var hitCols = new List<string>();
                int hn = Physics.OverlapSphereNonAlloc(s_home, 25f, s_diagCols, MonsterBase.HitLayerMask, QueryTriggerInteraction.Collide);
                for (int k = 0; k < hn && hitCols.Count < 6; k++)
                    if (s_diagCols[k] != null && Vector3.Dot(Flat(s_diagCols[k].transform.position - s_home), best) > 0.5f)
                        hitCols.Add($"{s_diagCols[k].name}@{Vector3.Distance(s_home, s_diagCols[k].transform.position):0.0}m{(s_diagCols[k].gameObject.activeInHierarchy ? "" : "(꺼짐)")}");
                Vector3 aim = s_home + best * 30f;
                float t0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - t0 < 4f)
                {
                    bool shoot = Time.realtimeSinceStartup - t0 < 2.5f;
                    Steer(p, aim);
                    if (shoot && p.InputBuffer.Count < 2) p.InputBuffer.Push(Command.Light);
                    var live = new HashSet<BasicArrow>();
                    foreach (var a in UnityEngine.Object.FindObjectsByType<BasicArrow>(FindObjectsSortMode.None))
                    {
                        if (a == null || !a.gameObject.activeInHierarchy) continue;
                        live.Add(a);
                        float sc = a.transform.localScale.x;
                        if (!first.ContainsKey(a)) { first[a] = (a.transform.position, sc); if (startScale < 0f) startScale = sc; maxStart = Mathf.Max(maxStart, sc); }
                        last[a] = (a.transform.position, sc);
                    }
                    foreach (var a in new List<BasicArrow>(first.Keys))
                    {
                        if (live.Contains(a)) continue;
                        var f = first[a]; var l = last[a];
                        if (Vector3.Distance(f.pos, l.pos) > maxDist) { maxDist = Vector3.Distance(f.pos, l.pos); endY = l.pos.y - s_home.y; slope = (l.pos - f.pos).normalized.y; }
                        if (f.scale > 0f) minRatio = Mathf.Min(minRatio, l.scale / f.scale);
                        ended++;
                        first.Remove(a); last.Remove(a);
                    }
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                }
                p.InputBuffer.Clear();
                CombatSpawner.LogPitch = false;
                sb.AppendLine($"    진단 — 가장 멀리 간 화살: 사라진 높이(발밑 기준) {endY:0.00} m · 기울기 y {slope:0.000} · 쏘는 쪽 25 m 안 피격 레이어 {(hitCols.Count > 0 ? string.Join(" ", hitCols) : "없음")}");
                float want = BasicArrow.BaseRange + bonus;
                bool wallFirst = bestFree < want;
                Check(sb, $"N-{(bonus > 0f ? "b" : "a")} 사거리 보너스 +{bonus:0} m — 화살 {ended}발 · 간 거리 최대 {maxDist:0.0} m(사거리 {want:0} · 처음 본 자리는 총구 뒤라 조금 짧다{(wallFirst ? " · 벽이 먼저" : "")}) · 사라지기 직전 크기 ×{minRatio:0.00}(1보다 작아야) · 처음 크기 {startScale:0.00}~{maxStart:0.00}",
                      ended > 0 && (wallFirst || (maxDist > want - 2.5f && maxDist <= want + 0.6f)) && (wallFirst || minRatio < 0.6f) && Mathf.Abs(maxStart - startScale) < 0.01f);
            }
        }
        finally
        {
            bonusField?.SetValue(p.RuntimeStats, bonus0);
            DetachAll();
        }
    }

    /// <summary>오크 표적(허수아비처럼 — 죽지 않음 · 제자리 · 안 움직임 · 안 때림) — TrainingDummyMonster가 흉내 내는 오크와 같은 몬스터.</summary>
    private static async UniTask<HpTrack> TrackOrcAsync(Vector3 at, CancellationToken ct)
    {
        var m = await Managers.ObjectPooler.SpawnAsync<MonsterBase>("Orc/Orc", ObjectPoolerManager.PoolType.Monster, at, Quaternion.identity);
        if (m == null) return null;
        float t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < 6f && m.IsDamageImmuneNow) await UniTask.Yield(PlayerLoopTiming.Update, ct);
        m.Status.ApplyCc("probe_hold", 9999f);
        m.HpFloorMin1 = true;
        SetHp(m, TankHp);
        var t = new HpTrack(m);
        s_tracks.Add(t);
        return t;
    }

    /// <summary>이 무기에 나올 수 있는 증강 전부(걷은 것 · 다른 무기 것 제외) — 단계 rank(자람 · 전설은 1).</summary>
    private static string W99AllAugments(WeaponData w, int rank)
    {
        var sbA = new StringBuilder();
        foreach (var d in MeleeAugmentCatalog.All)
        {
            if (d == null || MeleeAugmentCatalog.RemovedIds.Contains(d.Id) || !MeleeAugmentCatalog.FitsWeapon(d, w)) continue;
            if (sbA.Length > 0) sbA.Append(',');
            sbA.Append(d.Id).Append(':').Append(d.IsGrowth || d.IsLegend ? 1 : rank);
        }
        return sbA.ToString();
    }

    /// <summary>
    /// E — 오크 더미 DPS(10-26 사용자 「오크 더미로 dps · 첫 증강들 다 킨 상태와 다 강화한 상태 2가지로 무기별로」).
    /// 상태: +9 증강 없음 · +9 첫 증강 다 켬(전부 I) · +21 증강 없음 · +21 다 강화(전부 III · 자람은 재련 4회 값). 형 1단계 · 유물 없음.
    /// 15초씩 ① 평타만 ② 평타 + E · R을 쿨마다(스킬이 끝나면 제자리로). 오크 한 마리 정면 2.2 m.
    /// </summary>
    private static async UniTask W99Dps(PlayerController p, StringBuilder sb, string path, string name, CancellationToken ct)
    {
        const float Seconds = 15f;
        var actField = typeof(PlayerController).GetField("actStateDebug", Inst);
        bool InSkill() => (actField?.GetValue(p)?.ToString() ?? "").Contains("Skill");
        var w9 = W97Weapon(path, 9);
        string all1 = W99AllAugments(w9, 1), all3 = W99AllAugments(w9, 3);
        int count = all1.Length == 0 ? 0 : all1.Split(',').Length;

        async UniTask<(float dps, int skills)> Run(int lv, string aug, bool rotation)
        {
            await W97Setup(p, path, aug, lv, ct, 1);
            DetachAll();
            GoHome(p, s_home, Quaternion.LookRotation(s_fw));
            var orc = await TrackOrcAsync(s_home + s_fw * 2.2f, ct);
            HideStrays();
            if (orc == null) return (-1f, 0);
            Vector3 aim = orc.M.transform.position;
            AimAt(aim); RequestFace(p, aim);
            await Wait(0.5f, ct);
            orc.Dealt = 0f;
            int skills = 0;
            bool wasSkill = false;
            float t0 = Time.time;
            while (Time.time - t0 < Seconds)
            {
                RefillAll(); Steer(p, aim);
                bool inSkill = InSkill();
                if (wasSkill && !inSkill) GoHome(p, s_home, Quaternion.LookRotation(s_fw));   // 돌진 뒤 제자리로(실전에선 걸어 돌아온다)
                wasSkill = inSkill;
                if (rotation && !inSkill)
                {
                    if (p.CooldownTracker.IsReady(SkillType.E)) { p.InputBuffer.Clear(); p.InputBuffer.Push(Command.ESkill); skills++; }
                    else if (p.CooldownTracker.IsReady(SkillType.R)) { p.InputBuffer.Clear(); p.InputBuffer.Push(Command.RSkill); skills++; }
                }
                if (p.InputBuffer.Count < 2) p.InputBuffer.Push(Command.Light);
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            p.InputBuffer.Clear();
            await Wait(0.6f, ct);
            float dps = orc.Dealt / Seconds;
            DetachAll();
            GiantSwordBuff.ProbeElement = -1;
            return (dps, skills);
        }

        if (!await W97Setup(p, path, "", 9, ct, 1)) { sb.AppendLine($"  E {name} 장착 실패"); return; }
        s_home = p.transform.position;
        s_fw = Flat(p.transform.forward);
        sb.AppendLine($"  E {name} 오크 더미 DPS — 15초 · 형 1단계 · 유물 없음 · 증강 {count}개(다 켬)");
        foreach (var (label, lv, aug) in new[] { ("증강 없음", 9, ""), ("첫 증강 다 켬(전부 I)", 9, all1), ("증강 없음", 21, ""), ("다 강화(전부 III)", 21, all3) })
        {
            var basic = await Run(lv, aug, false);
            var rot   = await Run(lv, aug, true);
            Check(sb, $"E {name} +{lv} {label} — 평타만 DPS {basic.dps:0} · 평타 + E · R DPS {rot.dps:0}(스킬 {rot.skills}번)", basic.dps > 0f && rot.dps > 0f);
        }
    }

    /// <summary>B — +9 · +21에서 E · R 실전: 무리(앞 2.5 m 셋) · 먼 표적(5 m) 피해 · 맞힘 · 재사용 대기 · 동작 시간 · 대검 R 버프 · 사진(+21).</summary>
    private static async UniTask W99Skills(PlayerController p, StringBuilder sb, string path, string name, CancellationToken ct)
    {
        var actField = typeof(PlayerController).GetField("actStateDebug", Inst);
        bool InSkill() => (actField?.GetValue(p)?.ToString() ?? "").Contains("Skill");
        Directory.CreateDirectory("Temp/ui_shots");
        var camGo = new GameObject("~W99Cam");
        var cam = camGo.AddComponent<Camera>();
        if (Camera.main != null) cam.CopyFrom(Camera.main);
        cam.enabled = false;
        cam.fieldOfView = 55f;
        try
        {
            foreach (int lv in new[] { 9, 21 })
                foreach (var (cmd, type, nm) in new[] { (Command.ESkill, SkillType.E, "E"), (Command.RSkill, SkillType.R, "R") })
                {
                    if (!await W97Setup(p, path, "", lv, ct, 0)) { sb.AppendLine($"  B {name} 장착 실패"); return; }
                    DetachAll();
                    s_home = p.transform.position;
                    Vector3 fw = Flat(p.transform.forward), right = Vector3.Cross(Vector3.up, fw);
                    var a  = await TrackAsync(s_home + fw * 2.5f, ct);
                    var b  = await TrackAsync(s_home + fw * 2.5f + right * 1.3f, ct);
                    var c  = await TrackAsync(s_home + fw * 2.5f - right * 1.3f, ct);
                    var fa = await TrackAsync(s_home + fw * 5.5f, ct);
                    HideStrays();
                    if (a == null) { sb.AppendLine($"  B {name} 표적 실패"); continue; }
                    AimAt(a.M.transform.position); RequestFace(p, a.M.transform.position);
                    await Wait(0.4f, ct);
                    foreach (var t in new[] { a, b, c, fa }) if (t != null) t.Dealt = 0f;
                    int hits = 0, hitsA = 0;
                    float firstHit = -1f, s0 = -1f;
                    Action<HitInfo> onHit = h =>
                    {
                        if (h.Attacker != p.gameObject || h.Target == null || s0 < 0f) return;
                        hits++;
                        if (h.Target == a.M.gameObject) hitsA++;
                        if (firstHit < 0f) firstHit = Time.time - s0;
                    };
                    HitFeedbackService.OnHit += onHit;
                    float endAt = -1f, cd = -1f, buff = -1f;
                    try
                    {
                        p.CooldownTracker.ResetCooldown(type);
                        p.InputBuffer.Push(cmd);
                        s0 = Time.time;
                        bool began = false;
                        int shot = 0;
                        float[] shotsAt = { 0.25f, 0.55f, 0.9f };
                        while (Time.time - s0 < 3.5f)
                        {
                            RefillAll();
                            float now = Time.time - s0;
                            bool inSkill = InSkill();
                            if (inSkill && !began) began = true;
                            if (began && !inSkill && endAt < 0f) { endAt = now; cd = p.CooldownTracker.GetRemaining(type); }
                            if (buff < 0f && GiantSwordBuff.IsActive(p)) buff = p.GetComponent<GiantSwordBuff>().Remaining;
                            if (lv == 21 && shot < shotsAt.Length && now >= shotsAt[shot])
                            {
                                cam.transform.position = s_home - fw * 3.5f + right * 5f + Vector3.up * 5f;
                                cam.transform.LookAt(s_home + fw * 2f);
                                CaptureMain(cam, $"Temp/ui_shots/W99_{(path == Great ? "G" : "K")}_{nm}_{shot++}.png");
                            }
                            if (endAt > 0f && now > endAt + 0.6f && shot >= (lv == 21 ? shotsAt.Length : 0)) break;
                            await UniTask.Yield(PlayerLoopTiming.Update, ct);
                        }
                    }
                    finally { HitFeedbackService.OnHit -= onHit; }
                    if (cd < 0f) cd = p.CooldownTracker.GetRemaining(type);
                    float crowd = (a?.Dealt ?? 0f) + (b?.Dealt ?? 0f) + (c?.Dealt ?? 0f);
                    string extra = path == Great && type == SkillType.R ? $" · 속성 검 {buff:0.00}초(목표 {GiantSwordBuff.DurationFor(p.WeaponManager.Weapon0Data):0.0})" : "";
                    bool ok = (a?.Dealt ?? 0f) > 0f && endAt > 0f && endAt < 3f && cd > 0f
                              && (path != Great || type != SkillType.R || Mathf.Abs(buff - GiantSwordBuff.DurationFor(p.WeaponManager.Weapon0Data)) < 0.2f);
                    Check(sb, $"B {name} +{lv} {nm}(단계 {SkillTierResolver.Resolve(p.WeaponManager.Weapon0Data)}) — 앞 표적 {a?.Dealt ?? 0f:0}({hitsA}타) · 무리 셋 합 {crowd:0} · 5.5 m {fa?.Dealt ?? 0f:0} · 맞힘 {hits} · 첫 적중 {firstHit:0.00}초 · 끝 {endAt:0.00}초 · 재사용 {cd:0.0}초{extra}", ok);
                    DetachAll();
                    await Wait(0.6f, ct);
                }
        }
        finally { UnityEngine.Object.Destroy(camGo); DetachAll(); }
    }

    /// <summary>
    /// C — 이 무기에 나올 수 있는 증강을 하나씩(I · +15) 켜고 같은 실전 시나리오: 평타 2.5초 → 구르기 → 평타 0.8초 → E → R → 평타 1.5초.
    /// 「[MeleeAugment]」 발동 줄 수 · 피해 합(증강 없음 대비)을 적는다. 플레이어 무적 · 룬판 빈 판이라 피격 · 처치 · 속성 조건 증강은 안 걸릴 수 있다(표시만).
    /// </summary>
    private static async UniTask W99Augments(PlayerController p, StringBuilder sb, string path, string name, CancellationToken ct)
    {
        var lines = new List<string>();
        Application.LogCallback onLog = (msg, _, __) =>
        {
            if (msg.StartsWith("[MeleeAugment] ", StringComparison.Ordinal) && !msg.StartsWith("[MeleeAugment] +", StringComparison.Ordinal)
                && !msg.StartsWith("[MeleeAugment] 고름", StringComparison.Ordinal)) lines.Add(msg);
        };
        Application.logMessageReceived += onLog;

        async UniTask<(float dmg, int procs, string sample)> Scenario(string augments)
        {
            await W97Setup(p, path, augments, 15, ct, 0);
            DetachAll();
            s_home = p.transform.position;
            Vector3 fw = Flat(p.transform.forward), right = Vector3.Cross(Vector3.up, fw);
            var tracks = new List<HpTrack>();
            foreach (var at in new[] { s_home + fw * 2.2f, s_home + fw * 2.2f + right * 1.2f, s_home + fw * 2.2f - right * 1.2f, s_home + fw * 4.5f })
            {
                var t = await TrackAsync(at, ct);
                if (t != null) tracks.Add(t);
            }
            HideStrays();
            if (tracks.Count == 0) return (-1f, 0, "표적 실패");
            AimAt(tracks[0].M.transform.position); RequestFace(p, tracks[0].M.transform.position);
            await Wait(0.3f, ct);
            foreach (var t in tracks) t.Dealt = 0f;
            lines.Clear();
            Vector3 aim = tracks[0].M.transform.position;
            await W97Swing(p, aim, 2.5f, ct);
            p.InputBuffer.Push(Command.Dodge);
            await Wait(0.5f, ct);
            GoHome(p, s_home, Quaternion.LookRotation(fw));
            await W97Swing(p, aim, 0.8f, ct);
            p.CooldownTracker.ResetCooldown(SkillType.E);
            p.InputBuffer.Push(Command.ESkill);
            for (float q = 0f; q < 1.6f; q += 0.1f) { RefillAll(); await Wait(0.1f, ct); }
            GoHome(p, s_home, Quaternion.LookRotation(fw));
            p.CooldownTracker.ResetCooldown(SkillType.R);
            p.InputBuffer.Push(Command.RSkill);
            for (float q = 0f; q < 2.0f; q += 0.1f) { RefillAll(); await Wait(0.1f, ct); }
            await W97Swing(p, aim, 1.5f, ct);
            await Wait(0.4f, ct);
            float sum = 0f;
            foreach (var t in tracks) sum += t.Dealt;
            string sample = lines.Count > 0 ? lines[0].Replace("[MeleeAugment] ", "") : "";
            if (sample.Length > 40) sample = sample.Substring(0, 40);
            int procs = lines.Count;
            DetachAll();
            GiantSwordBuff.ProbeElement = -1;
            return (sum, procs, sample);
        }

        try
        {
            var w0 = W97Weapon(path, 15);
            var basis = await Scenario("");
            sb.AppendLine($"  C {name} 증강 없음 — 피해 합 {basis.dmg:0}");
            int n = 0, seen = 0;
            foreach (var d in MeleeAugmentCatalog.All)
            {
                if (d == null || MeleeAugmentCatalog.RemovedIds.Contains(d.Id) || !MeleeAugmentCatalog.FitsWeapon(d, w0)) continue;
                n++;
                var r = await Scenario(d.Id + ":1");
                float delta = basis.dmg > 0f ? (r.dmg / basis.dmg - 1f) * 100f : 0f;
                bool fired = r.procs > 0 || Mathf.Abs(delta) >= 3f;
                if (fired) seen++;
                sb.AppendLine($"  C {name} {(fired ? "작동" : "안 봄")} — {d.Name}({MeleeAugmentDef.TierName(d.Tier)}{(d.Weapon == MeleeWeaponKind.None ? " · 공용" : "")}) · 발동 줄 {r.procs}{(r.sample.Length > 0 ? " 「" + r.sample + "」" : "")} · 피해 {r.dmg:0}({delta:+0;-0}%)");
            }
            sb.AppendLine($"  C {name} — 증강 {n}개 중 작동 확인 {seen}개(안 봄 = 이 시나리오에 조건이 없음 · 아래 판단)");
        }
        finally
        {
            Application.logMessageReceived -= onLog;
            DetachAll();
        }
    }

    // 98 — 무기 개편 화력 몫(15초 창 · ABBA · 표적은 체력을 채우고 제자리 · 떠도는 몬스터 매 프레임 숨김)
    private static async UniTask Weapon98Async(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        sb.AppendLine("98 — 무기 개편 화력 몫(15초 창 · ABBA)");
        await Wait(3f, ct);
        foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
            if (m != null && !m.IsDead) m.gameObject.SetActive(false);
        Managers.UI?.CloseAllPopupUI(); Time.timeScale = 1f;
        const float Seconds = 15f;

        async UniTask<float> Window(string weapon, string augments, int enhance, int formTier, int targets, float reachOverride)
        {
            DetachAll();
            if (!await W97Setup(p, weapon, augments, enhance, ct, formTier)) return -1f;
            GreatswordReach.ProbeBaseOverride = reachOverride;
            s_home = p.transform.position;
            Vector3 fw = Flat(p.transform.forward), right = Vector3.Cross(Vector3.up, fw);
            var tracks = new List<HpTrack>();
            for (int i = 0; i < targets; i++)
            {
                Vector3 at = s_home + fw * 2f + right * ((i % 2 == 0 ? 1 : -1) * 0.9f * ((i + 1) / 2)) + fw * (i >= 3 ? 0.9f : 0f);
                var t = await TrackAsync(at, ct);
                if (t != null) tracks.Add(t);
            }
            HideStrays();
            await Wait(0.3f, ct);
            foreach (var t in tracks) t.Dealt = 0f;
            await W97Swing(p, tracks.Count > 0 ? tracks[0].M.transform.position : s_home + fw * 2f, Seconds, ct);
            await Wait(0.5f, ct);
            float sum = 0f;
            foreach (var t in tracks) sum += t.Dealt;
            GreatswordReach.ProbeBaseOverride = -1f;
            return sum / Seconds;
        }

        async UniTask Compare(string label, Func<bool, UniTask<float>> run)
        {
            float a1 = await run(false), b1 = await run(true), b2 = await run(true), a2 = await run(false);
            float a = (a1 + a2) * 0.5f, b = (b1 + b2) * 0.5f;
            string line = $"  {label}: {a:0} → {b:0}({(a > 0f ? (b / a - 1f) * 100f : 0f):+0;-0}% · 없음 {a1:0}/{a2:0} 있음 {b1:0}/{b2:0})";
            sb.AppendLine(line);
            Debug.Log("[WeaponFormProbe] 98 " + line);
        }

        try
        {
            foreach (int lv in new[] { 15, 21 })
            {
                await Compare($"카타나 그림자 파트너(+{lv} · 단일)", on => Window(Katana, on ? MeleeAugmentCatalog.KatanaShadow + ":1" : "", lv, 1, 1, -1f));
                await Compare($"카타나 검풍(+{lv} · 무리 4)", on => Window(Katana, on ? MeleeAugmentCatalog.KatanaGust + ":1" : "", lv, 1, 4, -1f));
            }
            await Compare("대검 범위 ×1 → ×1.4(+12 · 무리 4)", on => Window(Great, "", 12, 1, 4, on ? -1f : 1f));
            await Compare("대검 범위 ×1 → ×1.4(+12 · 단일)", on => Window(Great, "", 12, 1, 1, on ? -1f : 1f));
        }
        finally { GreatswordReach.ProbeBaseOverride = -1f; Time.timeScale = 1f; DetachAll(); }
    }

    // ⑧ 속성 자국 사진 — 제자리 · 앞을 보고 속성마다 자국 하나(Temp/ui_shots/W97_zone_<속성>.png)
    private static async UniTask W97ZonePhotos(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        if (!await W97Setup(p, Great, "", 9, ct, 0)) { sb.AppendLine("  ⑧ 대검 장착 실패"); return; }
        s_home = p.transform.position;
        Vector3 fw = Flat(p.transform.forward);
        GiantSwordBuff.Begin(p, 60f);
        var buff = p.GetComponent<GiantSwordBuff>();
        var elemProp = typeof(GiantSwordBuff).GetProperty("Element");
        Directory.CreateDirectory("Temp/ui_shots");
        string names = "";
        foreach (var el in new[] { RuneElement.Fire, RuneElement.Ice, RuneElement.Electric, RuneElement.Grass, RuneElement.Light, RuneElement.Dark })
        {
            await Wait(2.3f, ct);   // 앞 자국이 사라지게
            elemProp.SetValue(buff, el);
            GoHome(p, s_home, Quaternion.LookRotation(fw));
            AimAt(s_home + fw * 5f); RequestFace(p, s_home + fw * 5f);
            await Wait(0.3f, ct);
            GiantSwordBuff.SpawnField(p, buff.SwingRadius);   // 10-26 — 평타는 장판을 안 깐다 · 장판 = E와 같은 원
            await Wait(0.4f, ct);
            if (Camera.main != null) CaptureMain(Camera.main, $"Temp/ui_shots/W97_zone_{el}.png");
            names += $" {el}";
        }
        sb.AppendLine($"  ⑧ 속성 자국 사진 —{names} → Temp/ui_shots/W97_zone_<속성>.png");
        GiantSwordBuff.Begin(p, 0.01f);
        DetachAll();
    }

    private static readonly HashSet<string> s_shotTaken = new();
    private static string s_diag95 = "";

    private static async UniTask<float> Legend95Once(PlayerController p, LegendCase c, int tier, bool withLegend, Vector3 open, Quaternion openRot, CancellationToken ct,
                                                     Action<float> perTick = null)
    {
        float Seconds = File.Exists("Temp/legend95_secs.txt") && float.TryParse(File.ReadAllText("Temp/legend95_secs.txt").Trim(), out float secs) ? secs : 6f;   // 무리 칸은 존 폭발이 창에 드느냐로 흔들려 길게(10-24 2차)
        DetachAll();
        var bridge = MerlinRuneBridge.Instance;
        bridge?.OnZoneCellsUpdated(new Dictionary<string, int>());
        var entries = Managers.RuneData?.GetZoneSynergies(c.Zone);
        var th = new List<int>();
        if (entries != null) foreach (var e in entries) if (e != null) th.Add(e.threshold);
        th.Sort();
        if (th.Count > 0) bridge?.OnZoneCellsUpdated(new Dictionary<string, int> { { c.Zone, th[Mathf.Min(tier, th.Count) - 1] } });
        IItemEffect fx = withLegend ? Legend(c.Type, c.Trigger, c.V, c.V2, c.V3) : null;
        Managers.UI?.CloseAllPopupUI(); Time.timeScale = 1f;
        GoHome(p, open, openRot);
        await Wait(0.4f, ct);
        s_home = p.transform.position;
        Vector3 fw = Flat(p.transform.forward), right = Vector3.Cross(Vector3.up, fw);
        var tracks = new List<HpTrack>();
        for (int i = 0; i < c.Targets; i++)
        {
            Vector3 at = s_home + fw * 2f + right * ((i % 2 == 0 ? 1 : -1) * 0.9f * ((i + 1) / 2)) + fw * (i >= 3 ? 0.9f : 0f);
            var t = await TrackAsync(at, ct);
            if (t != null) tracks.Add(t);
        }
        HideStrays();
        Vector3 aim = tracks.Count > 0 ? tracks[0].M.transform.position : s_home + fw * 2f;
        foreach (var t in tracks) t.Dealt = 0f;
        float t0 = Time.realtimeSinceStartup, nextSkill = 0.5f, nextDodge = 1.5f, nextHurt = 1f, nextKill = 0.8f, nextPerfect = 1f;
        var mgr = GameRunBootstrapper.Instance.Run.EffectManager;
        bool shots = withLegend && File.Exists("Temp/legend95_shots.txt") && s_shotTaken.Add(c.Type + tier);
        int shot = 0;
        int killIdx = 0;
        while (Time.realtimeSinceStartup - t0 < Seconds)
        {
            float t = Time.realtimeSinceStartup - t0;
            RefillAll();
            if (c.Dodge) { AimAt(aim); RequestFace(p, aim); }   // 대시 칸은 제자리로 당기지 않는다 — 대시 길(불꽃 질주 · 얼음 무대)이 실제 길이로
            else Steer(p, aim);
            if (c.Kills && t >= nextKill && tracks.Count > 0) { nextKill = t + 1.2f; mgr?.OnKill(tracks[killIdx++ % tracks.Count].M.gameObject); }
            if (c.Perfect && t >= nextPerfect && tracks.Count > 0)
            {
                nextPerfect = t + 2.5f;
                var pd = typeof(PlayerController).GetProperty("PerfectDodge", Inst)?.GetValue(p);
                pd?.GetType().GetField("_armed", Inst)?.SetValue(pd, true);
                pd?.GetType().GetMethod("Trigger", Inst)?.Invoke(pd, new object[] { "실측 95", tracks[0].M.transform });
            }
            if (shots && shot < 3 && t >= 1f + shot && Camera.main != null)
            {
                Directory.CreateDirectory("Temp/ui_shots");
                CaptureMain(Camera.main, $"Temp/ui_shots/LG95_{c.Type}_z{tier}_{shot}.png");
                shot++;
            }
            p.Stamina?.Refund(9999f, p.RuntimeStats.MaxStamina);
            if (p.InputBuffer.Count < 2) p.InputBuffer.Push(Command.Light);
            if (c.Skills && t >= nextSkill)
            {
                nextSkill = t + 2.5f;
                p.CooldownTracker.ResetCooldown(SkillType.E); p.CooldownTracker.ResetCooldown(SkillType.R);
                p.InputBuffer.Push(Command.ESkill);
            }
            if (c.Dodge && t >= nextDodge) { nextDodge = t + 2.5f; p.InputBuffer.Push(Command.Dodge); }
            if (c.Dodge && Time.realtimeSinceStartup - t0 > 0f && (p.transform.position - s_home).sqrMagnitude > 9f) GoHome(p, s_home, openRot);
            if (c.Hurt && t >= nextHurt)
            {
                nextHurt = t + 5f;
                mgr?.OnPostTakeDamage(new DamageReport { DamageDealt = p.RuntimeStats.MaxHp * 0.08f, Attacker = tracks.Count > 0 ? tracks[0].M.gameObject : null, Target = p.gameObject });
            }
            perTick?.Invoke(t);   // 실측 99 L — 이펙트 · 추가타 집계
            await Wait(0.05f, ct);
        }
        p.InputBuffer.Clear();
        if (withLegend)   // 진단 — 장판 · 고리가 실제로 있나 · 표적에 무엇이 걸렸나(10-24 2차 존 4 몫 0 확인)
        {
            int fields = UnityEngine.Object.FindObjectsByType<LegendGroundField>(FindObjectsSortMode.None).Length;
            int rings = UnityEngine.Object.FindObjectsByType<LegendOrbitRing>(FindObjectsSortMode.None).Length;
            var st = tracks.Count > 0 && tracks[0].M != null ? tracks[0].M.Status : null;
            string dots = st == null ? "-" : $"lava {(st.HasDot("lava_pool") ? st.GetDotDamagePerTick("lava_pool") : 0f):0} ignite {(st.HasDot("ignite") ? st.GetDotDamagePerTick("ignite") : 0f):0}";
            int dead = 0;
            foreach (var t in tracks) if (t.M == null || t.M.IsDead) dead++;
            s_diag95 = $" [장판 {fields} · 고리 {rings} · {dots} · 쓰러짐 {dead}]";
        }
        await Wait(0.5f, ct);
        float sum = 0f;
        foreach (var t in tracks) sum += t.Dealt;
        if (fx != null) RemoveEffect(fx);
        if (p.RuntimeStats.HasShield) p.RuntimeStats.AbsorbWithShield(99999);   // 빛의 반격 보호막을 다음 칸에 남기지 않는다
        DetachAll();
        return sum / Seconds;
    }

    // 93 — 과전류 포박 장판 중심(보정 셋 비교) · 그림자 분신(저스트 회피) 사진 · 끝없는 칼날 여러 적(휘두름 단위)
    private static async UniTask LegendShots93Async(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        sb.AppendLine("93 — 포박 장판 중심 · 그림자 분신 · 끝없는 칼날 여러 적");
        await Wait(3f, ct);
        foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
            if (m != null && !m.IsDead) m.gameObject.SetActive(false);
        Directory.CreateDirectory("Temp/ui_shots");
        try
        {
            await EquipAsync(p, Great, ct);
            await Wait(0.3f, ct);
            s_home = p.transform.position;
            Vector3 fw = Flat(p.transform.forward);
            var t = await TrackAsync(s_home + fw * 3f, ct);
            HideStrays();
            await Wait(0.5f, ct);
            // ① 포박 장판 — 지금(0,0,−3) · 배율 맞춤(×0.6 = −1.8) · 발생점 가운데(−(−6.25, 0, 2.5) × 0.6) 세 자리
            var cat = LegendaryRuntime.Catalog;
            var offsets = new[] { ("now", new Vector3(0f, 0f, -3f)), ("scaled", new Vector3(0f, 0f, -1.8f)), ("centroid", new Vector3(3.75f, 0f, -1.5f)) };
            foreach (var (tag, off) in offsets)
            {
                RefillAll();
                LegendaryRuntime.SpawnVfx(cat?.elecSingleVfx, t.M.transform.position + off, Quaternion.identity, 1.2f, 0.6f);
                await Wait(0.45f, ct);
                if (Camera.main != null) CaptureMain(Camera.main, $"Temp/ui_shots/LG93_bind_{tag}.png");
                await Wait(1.0f, ct);
            }
            sb.AppendLine($"  포박 장판 사진 — Temp/ui_shots/LG93_bind_now · scaled · centroid.png(표적 = {t.M.transform.position})");
            // ② 그림자 분신 — 저스트 회피(실제 발동 경로) → 0.15 · 0.35 · 0.6초 사진
            var shadow = Legend("DarkLegendSingle", "OnPerfectDodge", 1.5f, 8f);
            await Wait(0.2f, ct);
            var pd = typeof(PlayerController).GetProperty("PerfectDodge", Inst)?.GetValue(p);
            pd?.GetType().GetField("_armed", Inst)?.SetValue(pd, true);
            pd?.GetType().GetMethod("Trigger", Inst)?.Invoke(pd, new object[] { "실측 93", t.M.transform });
            float t0 = Time.realtimeSinceStartup;
            foreach (float at in new[] { 0.15f, 0.35f, 0.6f })
            {
                while (Time.realtimeSinceStartup - t0 < at) { RefillAll(); await UniTask.Yield(PlayerLoopTiming.Update, ct); }
                if (Camera.main != null) CaptureMain(Camera.main, $"Temp/ui_shots/LG93_shadow_{at:0.00}.png");
            }
            int clones = 0;
            foreach (var go in UnityEngine.Object.FindObjectsByType<PlayerController>(FindObjectsSortMode.None)) if (go != p && go.name.Contains("ShadowClone")) clones++;
            sb.AppendLine($"  그림자 분신 — 보이는 분신 {clones} · 사진 Temp/ui_shots/LG93_shadow_*.png · darkCloneVfx {(cat?.darkCloneVfx != null ? "있음" : "빈칸")}");
            RemoveEffect(shadow);
            for (float q = 0f; q < 2f; q += 0.1f) { RefillAll(); await Wait(0.1f, ct); }
            Time.timeScale = 1f;
        }
        finally { DetachAll(); Time.timeScale = 1f; }

        // ③ 끝없는 칼날 — 카타나 · 표적 셋(한 휘두름에 여럿) · 치명 확률만 올림(확정 아님) — 휘두름 단위 쌓임이 깎이지 않고 오르나
        if (await EquipAsync(p, Katana, ct))
        {
            var w = p.WeaponManager.Weapon0Data;
            w.augments = MeleeAugmentCatalog.LegendEndlessEdge + ":1"; w.formTier = 0; w.enhanceLevel = 0; w.RecomputeEnhancedStats();
            WeaponFormRuntime.Of(p).Rebuild(); GameRunBootstrapper.Instance.Run.EffectManager.Rebuild();
            await Wait(0.5f, ct);
            s_home = p.transform.position;
            Vector3 fw = Flat(p.transform.forward), right = Vector3.Cross(Vector3.up, fw);
            var a = await TrackAsync(s_home + fw * 1.8f, ct);
            await TrackAsync(s_home + fw * 1.8f + right * 0.9f, ct);
            await TrackAsync(s_home + fw * 1.8f - right * 0.9f, ct);
            HideStrays();
            EndlessEdgeLegend fx = null;
            foreach (var e in GameRunBootstrapper.Instance.Run.EffectManager.ActiveEffects) if (e is EndlessEdgeLegend el) fx = el;
            var rs = p.RuntimeStats;
            int max = 0;
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 6f)
            {
                RefillAll(); Steer(p, a.M.transform.position);
                if (Time.frameCount % 2 == 0 && !rs.ForceCritArmed && UnityEngine.Random.value < 0.5f) rs.ArmForceCrit(0f);   // 휘두름의 일부 적중만 치명 — 약 절반
                if (p.InputBuffer.Count < 2) p.InputBuffer.Push(Command.Light);
                if (fx != null) max = Mathf.Max(max, fx.Stacks);
                await Wait(0.03f, ct);
            }
            p.InputBuffer.Clear();
            Check(sb, $"③ 끝없는 칼날(여러 적 · 치명 섞임) — 최고 쌓임 {max}/10 · 끝 {(fx != null ? fx.Stacks : -1)}", max >= 6);
            DetachAll();
        }
    }

    // 92 — 일섬 처치 → E 초기화 진단(카타나 · 표적 3 m · 돌진 뒤 회피로 끊고 체력 1 · 다시 채우지 않음)
    private static async UniTask Issen92Async(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        sb.AppendLine("92 — 일섬 처치 → E 초기화");
        await Wait(3f, ct);
        foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
            if (m != null && !m.IsDead) m.gameObject.SetActive(false);
        var lines = new List<string>();
        Application.LogCallback onLog = (msg, _, __) => { if (msg.StartsWith("[MeleeAugment]")) lines.Add(msg); };
        Application.logMessageReceived += onLog;
        try
        {
            if (!await EquipAsync(p, Katana, ct)) { sb.AppendLine("카타나 장착 실패"); return; }
            var w = p.WeaponManager.Weapon0Data;
            w.augments = MeleeAugmentCatalog.LegendIssen + ":1"; w.formTier = 0; w.enhanceLevel = 0; w.RecomputeEnhancedStats();
            WeaponFormRuntime.Of(p).Rebuild(); GameRunBootstrapper.Instance.Run.EffectManager.Rebuild();
            await Wait(0.5f, ct);
            s_home = p.transform.position;
            Vector3 fw = Flat(p.transform.forward);
            var k = await TrackAsync(s_home + fw * 3f, ct);
            HideStrays();
            for (float q = 0f; q < 0.6f; q += 0.1f) { RefillAll(); Steer(p, k.M.transform.position); await Wait(0.1f, ct); }
            Vector3 start = k.M.transform.position;
            p.CooldownTracker.ResetCooldown(SkillType.E);
            lines.Clear();
            k.M.HpFloorMin1 = true;
            p.InputBuffer.Push(Command.ESkill);
            await Wait(0.25f, ct);
            sb.AppendLine($"  0.25초 — 표적 이동 {Vector3.Distance(start, k.M.transform.position):0.0} m · 죽음 {k.M.IsDead} · 활성 {k.M.gameObject.activeInHierarchy} · 스킬 중 {p.IsInSkillAction}");
            p.Stamina?.Refund(9999f, p.RuntimeStats.MaxStamina);
            p.InputBuffer.Push(Command.Dodge);
            await Wait(0.12f, ct);
            sb.AppendLine($"  회피 뒤 — 스킬 중 {p.IsInSkillAction} · 표적 이동 {Vector3.Distance(start, k.M.transform.position):0.0} m");
            if (!k.M.IsDead) { k.M.HpFloorMin1 = false; SetHp(k.M, 1); }
            await Wait(1.5f, ct);
            sb.AppendLine($"  끝 — 표적 죽음 {k.M == null || k.M.IsDead} · E 준비 {p.CooldownTracker.IsReady(SkillType.E)}");
            foreach (var l in lines) sb.AppendLine("  " + l);
        }
        finally { Application.logMessageReceived -= onLog; DetachAll(); }
    }

    // 91 — 10-24 점검 고침 확인
    private static async UniTask Finisher91Async(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        sb.AppendLine("91 — 막타 시점 · 풍절 실림 · 분신 오류 · 원거리 룬 거름(유물 없음)");
        await Wait(3f, ct);
        foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
            if (m != null && !m.IsDead) m.gameObject.SetActive(false);
        Vector3 open = p.transform.position + Flat(p.transform.forward) * 8f;
        Quaternion openRot = Quaternion.LookRotation(Flat(p.transform.forward));
        var lines = new List<string>();
        int destroyErr = 0;
        Application.LogCallback onLog = (msg, _, type) =>
        {
            if (msg.StartsWith("[MeleeAugment]") || msg.StartsWith("[WeaponForm]")) lines.Add(msg);
            if (msg.Contains("Destroying components immediately is not permitted")) destroyErr++;
        };
        int Count(string key) => lines.FindAll(l => l.Contains(key)).Count;
        Application.logMessageReceived += onLog;
        try
        {
            // ① 막타 검기 — 발사 순간 단계 · 콤보 수
            foreach (var (path, name) in new[] { (Nameless, "무형검"), (Katana, "카타나"), (Great, "대검") })
            {
                DetachAll();
                if (!await EquipAsync(p, path, ct)) { sb.AppendLine($"  {name} 장착 실패"); continue; }
                var w = p.WeaponManager.Weapon0Data;
                w.augments = MeleeAugmentCatalog.SwordWave + ":1"; w.formTier = 0; w.enhanceLevel = 0; w.RecomputeEnhancedStats();
                WeaponFormRuntime.Of(p).Rebuild(); GameRunBootstrapper.Instance.Run.EffectManager.Rebuild();
                Managers.UI?.CloseAllPopupUI(); Time.timeScale = 1f;
                GoHome(p, open, openRot); await Wait(0.5f, ct); s_home = p.transform.position;
                var a = await TrackAsync(s_home + Flat(p.transform.forward) * 2f, ct);
                HideStrays();
                lines.Clear();
                int last = Mathf.Max(1, w.groundEndCount) - 1, finishers = 0, prevStep = -1;
                float t0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - t0 < 5f)
                {
                    RefillAll(); Steer(p, a.M.transform.position);
                    if (p.InputBuffer.Count < 2) p.InputBuffer.Push(Command.Light);
                    int st = p.Combo.CurrentComboStep;
                    if (st == last && prevStep != last) finishers++;
                    prevStep = st;
                    await Wait(0.03f, ct);
                }
                p.InputBuffer.Clear();
                await Wait(0.8f, ct);
                var launches = lines.FindAll(l => l.Contains("검기 발사"));
                int atLast = launches.FindAll(l => l.Contains($"단계 {last} ")).Count;
                Check(sb, $"① {name} 막타 검기 — 발사 {launches.Count} · 그중 막타 단계({last}) {atLast} · 막타 도달 {finishers}", launches.Count > 0 && atLast == launches.Count && launches.Count <= finishers + 1);
            }
            // ③ 풍절 + 검기 — 가득 찬 칼바람 막타: 검기 증강은 풍절에 실림
            DetachAll();
            if (await EquipAsync(p, Katana, ct))
            {
                var w = p.WeaponManager.Weapon0Data;
                w.augments = MeleeAugmentCatalog.SwordWave + ":1"; w.formTier = 1; w.enhanceLevel = 0; w.RecomputeEnhancedStats();
                WeaponFormRuntime.Of(p).Rebuild(); GameRunBootstrapper.Instance.Run.EffectManager.Rebuild();
                GoHome(p, open, openRot); await Wait(0.5f, ct); s_home = p.transform.position;
                var a = await TrackAsync(s_home + Flat(p.transform.forward) * 2f, ct);
                HideStrays();
                lines.Clear();
                var wf = WeaponFormRuntime.Of(p)?.Current as WindbladeForm;
                float t0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - t0 < 5f)
                {
                    RefillAll(); Steer(p, a.M.transform.position);
                    if (wf != null && !wf.IsFull) wf.AddStacks(5);
                    if (p.InputBuffer.Count < 2) p.InputBuffer.Push(Command.Light);
                    await Wait(0.03f, ct);
                }
                p.InputBuffer.Clear();
                await Wait(0.8f, ct);
                int ride = Count("검기 — 풍절에 실림"), launched = Count("검기 발사"), wind = lines.FindAll(l => l.Contains("풍절 — 맞힘") || l.Contains("쌍풍 — 맞힘")).Count;
                Check(sb, $"③ 풍절 + 검기 — 형 {(wf != null ? "칼바람" : "없음")} · 풍절 {wind} · 검기 실림 {ride} · 따로 발사 {launched}", wf != null && ride > 0 && launched == 0);
            }
            else sb.AppendLine("  카타나 장착 실패");
            // ④ 분신 만들기 오류
            Check(sb, $"④ 분신 만들기 오류(DestroyImmediate 거부) {destroyErr}건", destroyErr == 0);
            // ⑤ 원거리 전용 룬 거름 — 지금 무기(근접만?)로
            var inv = GameRunBootstrapper.Instance?.Run?.ItemInventory;
            var pool = new List<ItemSO>();
            foreach (ItemRarity r in Enum.GetValues(typeof(ItemRarity)))
            {
                var got = ItemSORegistry.GetByRarity(r);
                if (got != null) pool.AddRange(got);
            }
            var into = new List<ItemSO>();
            inv?.FilterObtainable(pool, into);
            var wm = p.WeaponManager;
            bool ranged = SkillTierResolver.IsRanged(wm.Weapon0Data) || SkillTierResolver.IsRanged(wm.Weapon1Data);
            string[] rangedOnly = { "item_t2_trig_ranged", "item_t2_aim_fury", "item_t4_grass_single", "item_t4_grass_proj", "item_t4_light_proj" };   // 꿰뚫는 얼음은 무형참 관통에도 붙어 남는다 · 호위 성검은 10-24 2차부터 원거리 전용
            int inPool = 0, kept = 0;
            foreach (var id in rangedOnly)
            {
                if (pool.Exists(s => s != null && s.itemId == id)) inPool++;
                if (into.Exists(s => s != null && s.itemId == id)) kept++;
            }
            Check(sb, $"⑤ 원거리 전용 룬 — 원거리 무기 {ranged} · 풀 {inPool}/5 · 남음 {kept} · 후보 {pool.Count} → {into.Count}", inPool > 0 && (ranged ? kept == inPool : kept == 0));
        }
        finally { Application.logMessageReceived -= onLog; Time.timeScale = 1f; DetachAll(); }
    }

    // 90 — 10-24 W7 전설 증강 6종 · 잔상 일섬(어느 R에나) · 전설 굴림
    private static async UniTask Legend90Async(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        sb.AppendLine("90 — 전설 증강 6종 · 잔상 일섬 · 전설 굴림(유물 없음)");
        await Wait(3f, ct);
        foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
            if (m != null && !m.IsDead) m.gameObject.SetActive(false);
        Directory.CreateDirectory("Temp/ui_shots");
        Vector3 open = p.transform.position + Flat(p.transform.forward) * 8f;
        Quaternion openRot = Quaternion.LookRotation(Flat(p.transform.forward));
        var lines = new List<string>();
        Application.LogCallback onLog = (msg, _, __) => { if (msg.StartsWith("[MeleeAugment]")) lines.Add(msg); };
        int Count(string key) => lines.FindAll(l => l.Contains(key)).Count;
        string Last(string key) => lines.FindLast(l => l.Contains(key)) ?? "-";
        Vector3 fw = Vector3.forward;

        async UniTask<bool> Setup(string path, string augs)
        {
            DetachAll();
            if (!await EquipAsync(p, path, ct)) { sb.AppendLine($"  장착 실패 {path}"); return false; }
            var w = p.WeaponManager.Weapon0Data;
            w.augments = augs; w.formTier = 0; w.enhanceLevel = 0; w.RecomputeEnhancedStats();
            WeaponFormRuntime.Of(p).Rebuild(); GameRunBootstrapper.Instance.Run.EffectManager.Rebuild();
            Managers.UI?.CloseAllPopupUI(); Time.timeScale = 1f;
            GoHome(p, open, openRot);
            await Wait(0.5f, ct);
            s_home = p.transform.position;
            fw = Flat(p.transform.forward);
            lines.Clear();
            return true;
        }
        async UniTask Spam(Vector3 aim, float seconds, Func<bool> shotWhen = null, string shot = null)
        {
            bool taken = false;
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < seconds)
            {
                RefillAll(); Steer(p, aim);
                if (p.InputBuffer.Count < 2) p.InputBuffer.Push(Command.Light);
                if (!taken && shot != null && shotWhen != null && shotWhen()) { taken = true; if (Camera.main != null) CaptureMain(Camera.main, shot); }
                await Wait(0.05f, ct);
            }
            p.InputBuffer.Clear();
            for (float q = 0f; q < 0.8f; q += 0.1f) { RefillAll(); await Wait(0.1f, ct); }
        }
        T Fx<T>() where T : class
        {
            foreach (var e in GameRunBootstrapper.Instance.Run.EffectManager.ActiveEffects) if (e is T t) return t;
            return null;
        }

        Application.logMessageReceived += onLog;
        try
        {
            // ① 검의 폭풍(카타나) — 평타 3초 · 표적 2 · 4 · 6 m
            if (await Setup(Katana, MeleeAugmentCatalog.LegendStorm + ":1"))
            {
                var a = await TrackAsync(s_home + fw * 2f, ct); var b = await TrackAsync(s_home + fw * 4f, ct); var c = await TrackAsync(s_home + fw * 6f, ct);
                HideStrays();
                int sw0 = WeaponFormRuntime.Of(p).SwingSerial;
                await Spam(a.M.transform.position, 3f, () => Count("검의 폭풍") > 0, "Temp/ui_shots/LG_storm.png");
                int swings = WeaponFormRuntime.Of(p).SwingSerial - sw0, storm = Count("검의 폭풍");
                Check(sb, $"① 검의 폭풍 — 휘두름 {swings} · 검기 {storm} · 피해 2/4/6 m {a.Dealt:0}/{b.Dealt:0}/{c.Dealt:0} · {Last("검의 폭풍")}", storm > 0 && storm >= swings - 1 && c.Dealt > 0);
            }
            // ①b 검의 폭풍 + 검기 증강 — 막타 휘두름은 검기 증강 하나만(합 ≤ 휘두름)
            if (await Setup(Katana, MeleeAugmentCatalog.LegendStorm + ":1," + MeleeAugmentCatalog.SwordWave + ":1"))
            {
                var a = await TrackAsync(s_home + fw * 2f, ct);
                HideStrays();
                int sw0 = WeaponFormRuntime.Of(p).SwingSerial;
                await Spam(a.M.transform.position, 3f);
                int swings = WeaponFormRuntime.Of(p).SwingSerial - sw0, storm = Count("검의 폭풍"), wave = Count("] 검기 — ");
                Check(sb, $"①b 검의 폭풍 + 검기 — 휘두름 {swings} · 폭풍 {storm} · 막타 검기 {wave} · 합 {storm + wave}(≤ 휘두름 + 1)", wave > 0 && storm + wave <= swings + 1);
            }
            // ② 멈춘 시간(카타나) — 저스트 회피(실제 발동 경로) → 8 m 안 기절 · 2초 동안 평타 두 번 베기(같은 1.5초 평타 피해 비)
            if (await Setup(Katana, MeleeAugmentCatalog.LegendStoppedTime + ":1"))
            {
                var a = await TrackAsync(s_home + fw * 2f, ct); var far = await TrackAsync(s_home + fw * 6f, ct);
                HideStrays();
                await Spam(a.M.transform.position, 1.5f);
                float base15 = a.Dealt;
                for (float q = 0f; q < 2.5f; q += 0.1f) { RefillAll(); await Wait(0.1f, ct); }
                var pd = typeof(PlayerController).GetProperty("PerfectDodge", Inst)?.GetValue(p);
                pd?.GetType().GetField("_armed", Inst)?.SetValue(pd, true);   // 회피 시작에 무장되는 값 — 실측은 회피 없이 부른다
                pd?.GetType().GetMethod("Trigger", Inst)?.Invoke(pd, new object[] { "실측 90", a.M.transform });
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
                bool stunA = a.M.Status.HasCc("stun"), stunFar = far.M.Status.HasCc("stun");
                float d0 = a.Dealt;
                await Spam(a.M.transform.position, 1.5f);
                float during = a.Dealt - d0;
                Check(sb, $"② 멈춘 시간 — 발동 {Count("멈춘 시간")} · 기절 2 m {stunA} · 6 m {stunFar} · 평타 1.5초 피해 평소 {base15:0} → 멈춘 동안 {during:0}(×{(base15 > 0 ? during / base15 : 0):0.00})", Count("멈춘 시간") > 0 && stunA && stunFar && during > base15 * 1.4f);
                for (float q = 0f; q < 2f; q += 0.1f) { RefillAll(); await Wait(0.1f, ct); }
                Time.timeScale = 1f;
            }
            // ③ 끝없는 칼날(카타나) — 확정 치명으로 10번 쌓임 · 치명 아닌 적중이 절반
            if (await Setup(Katana, MeleeAugmentCatalog.LegendEndlessEdge + ":1"))
            {
                var a = await TrackAsync(s_home + fw * 2f, ct);
                HideStrays();
                var fx = Fx<EndlessEdgeLegend>();
                float cd0 = p.RuntimeStats.CritDamageBonus;
                float t0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - t0 < 4f)
                {
                    RefillAll(); Steer(p, a.M.transform.position);
                    if (!p.RuntimeStats.ForceCritArmed) p.RuntimeStats.ArmForceCrit(0f);
                    if (p.InputBuffer.Count < 2) p.InputBuffer.Push(Command.Light);
                    await Wait(0.05f, ct);
                }
                p.InputBuffer.Clear();
                await Wait(0.6f, ct);
                int full = fx != null ? fx.Stacks : -1;
                float cd1 = p.RuntimeStats.CritDamageBonus;
                Check(sb, $"③ 끝없는 칼날 — 쌓임 {full}/10 · 치명 피해 보너스 {cd0:0.00} → {cd1:0.00}", full == 10 && cd1 - cd0 > 1.4f);
            }
            // ④ 천 개의 칼(무형검) — 앞 · 뒤 옆 ±120° 표적 · 막타마다 분신 셋
            if (await Setup(Nameless, MeleeAugmentCatalog.LegendThousand + ":1"))
            {
                var a = await TrackAsync(s_home + fw * 1.8f, ct);
                var l = await TrackAsync(s_home + Quaternion.Euler(0f, 120f, 0f) * fw * 2f, ct);
                var r = await TrackAsync(s_home + Quaternion.Euler(0f, -120f, 0f) * fw * 2f, ct);
                HideStrays();
                await Spam(a.M.transform.position, 3f, () => Count("천 개의 칼") > 0, "Temp/ui_shots/LG_thousand.png");
                Check(sb, $"④ 천 개의 칼 — 발동 {Count("천 개의 칼")} · 피해 앞 {a.Dealt:0} · 뒤 옆 {l.Dealt:0} / {r.Dealt:0} · {Last("천 개의 칼")}", Count("천 개의 칼") > 0 && l.Dealt > 0 && r.Dealt > 0);
            }
            // ⑤ 일섬(카타나) — E 돌진 길 0.4초 뒤 한 번 더 · 그 길에서 처치하면 E 초기화
            if (await Setup(Katana, MeleeAugmentCatalog.LegendIssen + ":1"))
            {
                var a = await TrackAsync(s_home + fw * 3f, ct);
                HideStrays();
                for (float q = 0f; q < 0.6f; q += 0.1f) { RefillAll(); Steer(p, a.M.transform.position); await Wait(0.1f, ct); }
                p.CooldownTracker.ResetCooldown(SkillType.E);
                p.InputBuffer.Push(Command.ESkill);
                float t0 = Time.realtimeSinceStartup; bool taken = false;
                while (Time.realtimeSinceStartup - t0 < 2f)
                {
                    RefillAll();
                    if (!taken && Count("일섬") > 0) { taken = true; if (Camera.main != null) CaptureMain(Camera.main, "Temp/ui_shots/LG_issen.png"); }
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                }
                string first = Last("일섬");
                // 처치 칸 — 돌진 직후 표적 체력을 1로(다시 채우지 않는다) → 일섬이 처치 → E 초기화
                GoHome(p, open, openRot); await Wait(0.5f, ct); s_home = p.transform.position;
                DetachAll();
                var k = await TrackAsync(s_home + fw * 3f, ct);
                HideStrays();
                for (float q = 0f; q < 0.6f; q += 0.1f) { RefillAll(); Steer(p, k.M.transform.position); await Wait(0.1f, ct); }
                p.CooldownTracker.ResetCooldown(SkillType.E);
                lines.Clear();
                if (k.M != null) k.M.HpFloorMin1 = true;   // 성광베기 돌진 타로는 안 죽게
                p.InputBuffer.Push(Command.ESkill);
                await Wait(0.25f, ct);
                p.Stamina?.Refund(9999f, p.RuntimeStats.MaxStamina);
                p.InputBuffer.Push(Command.Dodge);   // 돌진 직후 회피로 끊는다 — 남은 추가타 · 폭발이 표적을 먼저 죽이지 않게
                var issen = Fx<IssenLegend>();
                var atField = typeof(IssenLegend).GetField("_at", Inst);
                float w0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - w0 < 1.5f)
                {
                    float at = issen != null && atField != null ? (float)atField.GetValue(issen) : -1f;
                    if (at < 0f || Time.time >= at - 0.02f) break;   // 일섬이 막 나간다(이미 나갔으면 -1)
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                }
                sb.AppendLine($"   처치 칸 — 일섬 직전 표적 {(k.M == null ? "없음" : k.M.IsDead ? "죽음" : "삶")} · 거리 {(k.M != null ? Vector3.Distance(k.M.transform.position, p.transform.position) : -1f):0.0} m · 스킬 중 {p.IsInSkillAction}");
                if (k.M != null && !k.M.IsDead) { k.M.HpFloorMin1 = false; SetHp(k.M, 1); }
                await Wait(1.5f, ct);
                bool ready = p.CooldownTracker.IsReady(SkillType.E);
                Check(sb, $"⑤ 일섬 — 첫 칸 {first} · 처치 칸 {Last("일섬")} · E 준비 {ready}", first.Contains("맞힘") && !first.Contains("맞힘 0") && ready && Last("일섬").Contains("E 초기화"));
            }
            // ⑥ 대지 가르기(대검) — 막타 앞 8 m 균열 · 0.6초 뒤 솟구침 · 7.5 m 표적 · 띄움(기절)
            if (await Setup(Great, MeleeAugmentCatalog.LegendRift + ":1"))
            {
                var a = await TrackAsync(s_home + fw * 2f, ct); var b = await TrackAsync(s_home + fw * 5f, ct); var c = await TrackAsync(s_home + fw * 7.5f, ct);
                HideStrays();
                bool stunned = false;
                float t0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - t0 < 5f && Count("대지 가르기") == 0)
                {
                    RefillAll(); Steer(p, a.M.transform.position);
                    if (p.InputBuffer.Count < 2) p.InputBuffer.Push(Command.Light);
                    await Wait(0.05f, ct);
                }
                p.InputBuffer.Clear();
                if (Count("대지 가르기") > 0)
                {
                    stunned = c.M != null && c.M.Status.HasCc("stun");
                    if (Camera.main != null) CaptureMain(Camera.main, "Temp/ui_shots/LG_rift.png");
                }
                for (float q = 0f; q < 0.8f; q += 0.1f) { RefillAll(); await Wait(0.1f, ct); }
                Check(sb, $"⑥ 대지 가르기 — {Last("대지 가르기")} · 피해 2/5/7.5 m {a.Dealt:0}/{b.Dealt:0}/{c.Dealt:0} · 7.5 m 띄움 {stunned}", Count("대지 가르기") > 0 && c.Dealt > 0 && stunned);
            }
            // ⑦ 잔상 일섬(무형검 R 돌진) — R 한 번에 잔상 한 번(조준 예약 + 돌진 길이 겹치지 않게)
            if (await Setup(Nameless, "aug_nameless_afterimage:1"))
            {
                var a = await TrackAsync(s_home + fw * 4f, ct);
                HideStrays();
                for (float q = 0f; q < 0.6f; q += 0.1f) { RefillAll(); Steer(p, a.M.transform.position); await Wait(0.1f, ct); }
                p.CooldownTracker.ResetCooldown(SkillType.R);
                p.InputBuffer.Push(Command.RSkill);
                for (float q = 0f; q < 2.5f; q += 0.1f) { RefillAll(); await Wait(0.1f, ct); }
                Check(sb, $"⑦ 잔상 일섬(무형검 R) — 잔상 {Count("무형일섬 잔상")}번 · {Last("무형일섬 잔상")} · 글 「{MeleeAugmentCatalog.Get("aug_nameless_afterimage")?.Text(1)}」", Count("무형일섬 잔상") == 1);
            }
            // ⑧ 전설 굴림 — 확률 · 무기별 후보(제시 거름)
            {
                var pick = typeof(MeleeAugmentDraft).GetMethod("TryPickLegend", BindingFlags.Static | BindingFlags.NonPublic);
                string Pool(string path)
                {
                    var w = p.WeaponManager.Weapon0Data;
                    var seen = new SortedSet<string>();
                    var rng = new System.Random(7);
                    for (int i = 0; i < 300; i++)
                    {
                        var args = new object[] { w, rng, new List<MeleeAugmentCard>(), null };
                        if (pick != null && (bool)pick.Invoke(null, args)) seen.Add(((MeleeAugmentCard)args[3]).Id.Replace("aug_legend_", ""));
                    }
                    return string.Join(" · ", seen);
                }
                string pools = "";
                foreach (var (path, name) in new[] { (Katana, "카타나"), (Great, "대검"), (Nameless, "무형검") })
                    if (await EquipAsync(p, path, ct)) { p.WeaponManager.Weapon0Data.augments = ""; pools += $" {name}[{Pool(path)}]"; }
                float c0 = MeleeAugmentDraft.LegendChance(false), c1 = MeleeAugmentDraft.LegendChance(true);
                Check(sb, $"⑧ 전설 굴림 — 화면마다 {c0 * 100f:0.#}% · 진화 직후 {c1 * 100f:0.#}% · 런(7화면) {MeleeAugmentDraft.LegendChancePerRun(7) * 100f:0}% · 후보{pools}",
                      Mathf.Approximately(c0, 0.02f) && pools.Contains("issen") && pools.Contains("rift") && pools.Contains("thousand") && !pools.Contains("카타나[" + "rift"));
            }
        }
        finally { Application.logMessageReceived -= onLog; Time.timeScale = 1f; DetachAll(); }
    }

    // 89 — 10-24 「공격 자체의 경직 연출을 꺼 보자」 — 같은 공격을 멈칫 켬(예전) / 끔(지금)으로 · 시간 배율 < 0.99 프레임 · 최저 · 잡은 쪽
    private static async UniTask HitStop89Async(PlayerController p, StringBuilder sb, CancellationToken ct, bool weapons)
    {
        sb.AppendLine("89 — 공격 멈칫 켬(예전) / 끔(지금): 피해 · 시간 배율 < 0.99 프레임(실시간) · 최저 배율 · 잡은 쪽");
        await Wait(3f, ct);
        foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
            if (m != null && !m.IsDead) m.gameObject.SetActive(false);
        Vector3 open = p.transform.position + Flat(p.transform.forward) * 8f;
        Quaternion openRot = Quaternion.LookRotation(Flat(p.transform.forward));
        var g = p.RelicBehavior as GawainZenithRelic;
        var l = p.RelicBehavior as LancelotMadnessRelic;
        bool was = HitFeelService.DealtHitStop;

        async UniTask<string> One(string label, float seconds, Command first, bool spamLight, bool kill)
        {
            Managers.UI?.CloseAllPopupUI(); Time.timeScale = 1f;
            GoHome(p, open, openRot);
            await Wait(0.4f, ct);
            s_home = p.transform.position;
            Vector3 at = s_home + Flat(p.transform.forward) * 2.2f;
            HpTrack tr = null; MonsterBase victim = null;
            tr = await TrackAsync(at, ct);   // 처치 칸도 추적 목록에 둔다 — 목록 밖이면 HideStrays가 끈다
            if (kill && tr != null) { victim = tr.M; SetHp(victim, 1); }
            HideStrays();
            for (float q = 0f; q < 0.5f; q += 0.1f) { if (!kill) RefillAll(); KeepRelicQState(g, l); Steer(p, at); await Wait(0.1f, ct); }
            foreach (var st in new[] { SkillType.E, SkillType.R, SkillType.Q }) p.CooldownTracker.ResetCooldown(st);
            if (g != null) typeof(GawainZenithRelic).GetField("_skillUsedThisNoon", Inst)?.SetValue(g, false);
            if (l != null && l.JudgmentCharges <= 0) l.GrantJudgmentCharge(1);
            p.Stamina?.Refund(9999f, p.RuntimeStats.MaxStamina);
            p.InputBuffer.Clear();
            if (first != Command.Light) p.InputBuffer.Push(first);
            int slow = 0, frames = 0; float min = 1f, slowSec = 0f; var holders = new HashSet<string>();
            float dealt0 = tr != null ? tr.Dealt : 0f;
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < seconds)
            {
                if (!kill) RefillAll(); else HideStrays();
                Steer(p, at);
                if (spamLight && p.InputBuffer.Count < 2) p.InputBuffer.Push(Command.Light);
                frames++;
                if (Time.timeScale < 0.99f)
                {
                    slow++; slowSec += Time.unscaledDeltaTime; min = Mathf.Min(min, Time.timeScale);
                    holders.Add(TimeScaleArbiter.DescribeHolders());
                }
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            string hit = kill ? (victim == null ? "표적 없음" : victim.IsDead ? "처치함" : "안 죽음")
                              : $"피해 {(tr != null ? tr.Dealt - dealt0 : 0f):0}";
            DetachAll();
            if (victim != null && !victim.IsDead) victim.gameObject.SetActive(false);
            Time.timeScale = 1f;
            return $"{label}: {hit} · 멈춘 프레임 {slow}/{frames}({slowSec:0.00}초) · 최저 {min:0.00}{(holders.Count > 0 ? " · 잡은 쪽 " + string.Join(" / ", holders) : "")}";
        }

        try
        {
            foreach (bool on in new[] { true, false })
            {
                HitFeelService.DealtHitStop = on;
                sb.AppendLine(on ? "[멈칫 켬 — 예전]" : "[멈칫 끔 — 지금]");
                if (weapons)
                    foreach (var (path, name) in new[] { (Nameless, "무형검"), (Katana, "카타나"), (Great, "대검") })
                    {
                        if (!await EquipAsync(p, path, ct)) { sb.AppendLine($"  {name} 장착 실패"); continue; }
                        var w = p.WeaponManager.Weapon0Data;
                        w.augments = ""; w.formTier = 0; w.enhanceLevel = 0; w.RecomputeEnhancedStats();
                        WeaponFormRuntime.Of(p).Rebuild(); GameRunBootstrapper.Instance.Run.EffectManager.Rebuild();
                        sb.AppendLine("  " + await One($"{name} 평타 3초", 3f, Command.Light, true, false));
                        for (int k = 1; k <= 2; k++) sb.AppendLine("  " + await One($"{name} E #{k}", 2.5f, Command.ESkill, false, false));
                        for (int k = 1; k <= 2; k++) sb.AppendLine("  " + await One($"{name} R #{k}", 3f, Command.RSkill, false, false));
                        if (name == "카타나") sb.AppendLine("  " + await One($"{name} 처치(체력 1)", 1.5f, Command.Light, true, true));
                    }
                if (g != null || l != null)
                    sb.AppendLine("  " + await One($"유물 Q({(g != null ? "가웨인" : "랜슬롯")})", 4.2f, Command.QSkill, false, false));
            }
        }
        finally { HitFeelService.DealtHitStop = was; Time.timeScale = 1f; }
    }

    // 88 — 10-23 밤 「플레이어의 스킬들이 대시(회피)로 캔슬」 — 누른 뒤 d초에 회피 · 끊김까지 · 1.2초 뒤 남은 상태
    private static async UniTask DodgeCancel88Async(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        sb.AppendLine("88 — 회피로 스킬 끊기(누른 뒤 d초에 회피) · 끊겨 회피까지 · 1.2초 뒤: 스킬 상태 · 이동 배율 · 자세 · 시간 · 애니 속도 · 쿨");
        await Wait(3f, ct);
        foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
            if (m != null && !m.IsDead) m.gameObject.SetActive(false);
        Vector3 open = p.transform.position + Flat(p.transform.forward) * 8f;
        Quaternion openRot = Quaternion.LookRotation(Flat(p.transform.forward));
        var stanceField = typeof(PlayerController).GetField("_stanceOwner", Inst);
        var g = p.RelicBehavior as GawainZenithRelic;
        var l = p.RelicBehavior as LancelotMadnessRelic;

        async UniTask<string> One(SkillType slot, Command cmd, float d)
        {
            Managers.UI?.CloseAllPopupUI(); Time.timeScale = 1f;
            GoHome(p, open, openRot);
            await Wait(0.4f, ct);
            s_home = p.transform.position;
            Vector3 fw = Flat(p.transform.forward);
            var tr = await TrackAsync(s_home + fw * 3f, ct);
            HideStrays();
            for (float q = 0f; q < 0.6f; q += 0.1f) { RefillAll(); KeepRelicQState(g, l); Steer(p, s_home + fw * 3f); await Wait(0.1f, ct); }
            p.CooldownTracker.ResetCooldown(slot);
            if (slot == SkillType.Q && g != null) typeof(GawainZenithRelic).GetField("_skillUsedThisNoon", Inst)?.SetValue(g, false);
            if (slot == SkillType.Q && l != null && l.JudgmentCharges <= 0) l.GrantJudgmentCharge(1);
            p.Stamina?.Refund(9999f, p.RuntimeStats.MaxStamina);
            p.InputBuffer.Clear();
            float dealt0 = tr != null ? tr.Dealt : 0f;
            p.InputBuffer.Push(cmd);
            float s0 = Time.realtimeSinceStartup;
            bool began = false;
            while (Time.realtimeSinceStartup - s0 < 1f)
            {
                if (p.IsInSkillAction) { began = true; break; }
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            if (!began) { DetachAll(); return "시전 안 됨"; }
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < d) { RefillAll(); await UniTask.Yield(PlayerLoopTiming.Update, ct); }
            bool lockedAtPress = p.SkillCancelLocked;
            float press = Time.realtimeSinceStartup;
            p.InputBuffer.Push(Command.Dodge);
            float cut = -1f;
            while (Time.realtimeSinceStartup - press < 1.5f)
            {
                RefillAll();
                if (p.LocoSM.CurrentId == LocoState.Dodge) { cut = Time.realtimeSinceStartup - press; break; }
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            float hitBeforeCut = (tr != null ? tr.Dealt : 0f) - dealt0;
            await Wait(1.2f, ct);
            string state = $"스킬 상태 {(p.IsInSkillAction ? "남음!" : "끝")} · 이동 {p.MoveScale:0.00} · 자세 {(stanceField?.GetValue(p) == null ? "없음" : "남음!")} · 시간 {Time.timeScale:0.00}{(Time.timeScale < 0.99f ? $"(쥔 쪽 {TimeScaleArbiter.DescribeHolders()} · 회피 상태 {p.LocoSM.CurrentId})" : "")} · 애니 {p.Anim.speed:0.00} · 쿨 {(p.CooldownTracker.IsReady(slot) ? "안 씀" : "씀")}";
            DetachAll();
            return $"d {d:0.00}초{(lockedAtPress ? "(못 끊는 구간)" : "")} → 회피까지 {(cut < 0f ? "안 끊김" : cut.ToString("0.00") + "초")} · 끊기 전 피해 {hitBeforeCut:0} · {state}";
        }

        try
        {
            foreach (var (path, name) in new[] { (Nameless, "무형검"), (Katana, "카타나"), (Great, "대검") })
            {
                if (!await EquipAsync(p, path, ct)) { sb.AppendLine($"{name} 장착 실패"); continue; }
                var w = p.WeaponManager.Weapon0Data;
                w.augments = ""; w.formTier = 0; w.enhanceLevel = 0; w.RecomputeEnhancedStats();
                WeaponFormRuntime.Of(p).Rebuild(); GameRunBootstrapper.Instance.Run.EffectManager.Rebuild();
                foreach (var (slot, cmd, sname) in new[] { (SkillType.E, Command.ESkill, "E"), (SkillType.R, Command.RSkill, "R") })
                    foreach (float d in new[] { 0.05f, 0.35f })
                        sb.AppendLine($"  {name} {sname} " + await One(slot, cmd, d));
            }
            // 대검 R 공중(못 끊는 구간)에서 누른 회피 — 착지 뒤에 끊기고 남는 것이 없나(3번)
            if (await EquipAsync(p, Great, ct))
                foreach (float d in new[] { 0.2f, 0.35f, 0.45f })
                    sb.AppendLine("  대검 R 공중 " + await One(SkillType.R, Command.RSkill, d));
            if (g != null || l != null)
                foreach (float d in new[] { 0.1f, 1.0f })
                    sb.AppendLine($"  유물 Q({(g != null ? "가웨인" : "랜슬롯")}) " + await One(SkillType.Q, Command.QSkill, d));
        }
        finally { Time.timeScale = 1f; }
    }

    // 86 — 10-23 W1 「추가 검기가 한 번 더 재생되는 것 같고 검기가 데미지를 주지 못한다」 · W2 「검기마다 분신이 휘두르는 느낌」
    private static async UniTask WaveTime86Async(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        sb.AppendLine("86 — 검기 재생 · 판정 시점 · 분신 박자(발동 = 0초)");
        await Wait(3f, ct);
        foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
            if (m != null && !m.IsDead) m.gameObject.SetActive(false);
        Vector3 open = p.transform.position + Flat(p.transform.forward) * 8f;
        Quaternion openRot = Quaternion.LookRotation(Flat(p.transform.forward));
        var logs = new List<string>();
        Application.LogCallback onLog = (msg, _, __) => { if (msg.StartsWith("[MeleeAugment]") || msg.StartsWith("[WeaponForm]")) logs.Add(msg); };
        Application.logMessageReceived += onLog;

        static bool IsWave(GameObject go) => go != null && (go.name.Contains("Slash wave") || go.name.Contains("windcut"));
        // 보이는 검기 그림 — 살아 있는 입자가 있는 「Slash wave」 뿌리들(인스턴스 · 앞 끝 = 렌더러 경계를 fw로 잰 최대값)
        List<(int id, float front)> Waves(Vector3 origin, Vector3 fw)
        {
            var roots = new Dictionary<int, float>();
            foreach (var ps in UnityEngine.Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
            {
                if (ps == null || !ps.gameObject.activeInHierarchy || ps.particleCount <= 0) continue;
                var root = ps.transform;
                while (root.parent != null && IsWave(root.parent.gameObject)) root = root.parent;
                if (!IsWave(root.gameObject)) continue;
                if (!ps.TryGetComponent<ParticleSystemRenderer>(out var r) || !r.enabled) continue;
                var b = r.bounds;
                float best = float.MinValue;
                for (int c = 0; c < 8; c++)
                {
                    var corner = new Vector3((c & 1) == 0 ? b.min.x : b.max.x, b.center.y, (c & 4) == 0 ? b.min.z : b.max.z);
                    best = Mathf.Max(best, Vector3.Dot(corner - origin, fw));
                }
                int id = root.gameObject.GetInstanceID();
                roots[id] = roots.TryGetValue(id, out var f) ? Mathf.Max(f, best) : best;
            }
            var list = new List<(int, float)>();
            foreach (var kv in roots) list.Add((kv.Key, kv.Value));
            return list;
        }

        try
        {
            // (A) 그림만 — 붉은 검기를 6 · 8 m 판정 모양으로 · 입자 자리(중심 · 가장 앞)를 시간마다(렌더러 경계는 0 m · 31.8 m로 틀렸다)
            var particles = new ParticleSystem.Particle[1024];
            (float center, float tip, int n) Front(Vector3 origin, Vector3 fw)
            {
                double sum = 0; int cnt = 0; float tip = float.MinValue;
                foreach (var ps in UnityEngine.Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
                {
                    if (ps == null || !ps.gameObject.activeInHierarchy || ps.particleCount <= 0) continue;
                    var root = ps.transform;
                    while (root.parent != null && IsWave(root.parent.gameObject)) root = root.parent;
                    if (!IsWave(root.gameObject)) continue;
                    int k = ps.GetParticles(particles);
                    bool local = ps.main.simulationSpace == ParticleSystemSimulationSpace.Local;
                    for (int i = 0; i < k; i++)
                    {
                        Vector3 w = local ? ps.transform.TransformPoint(particles[i].position) : particles[i].position;
                        float d = Vector3.Dot(w - origin, fw);
                        sum += d; cnt++; tip = Mathf.Max(tip, d);
                    }
                }
                return cnt > 0 ? ((float)(sum / cnt), tip, cnt) : (-1f, -1f, 0);
            }
            foreach (float L in new[] { 6f, 8f })
            {
                GoHome(p, open, openRot);
                await Wait(0.6f, ct);
                Vector3 o = p.transform.position, fw = Flat(p.transform.forward);
                FormStrike.PlayWave("vfx_form_windcut_red", o + Vector3.up, fw, L, 1.3f);
                float t0 = Time.time, end = -1f;
                var marks = new[] { 0.05f, 0.1f, 0.15f, 0.2f, 0.3f, 0.4f, 0.6f };
                int mi = 0;
                var line = new StringBuilder();
                while (Time.time - t0 < 2f)
                {
                    await UniTask.Yield(PlayerLoopTiming.PostLateUpdate, ct);
                    float t = Time.time - t0;
                    var (c, tip, cnt) = Front(o, fw);
                    if (cnt == 0) { if (t > 0.1f) { end = t; break; } continue; }
                    while (mi < marks.Length && t >= marks[mi]) { line.Append($" {marks[mi]:0.00}초 중심 {c:0.0} · 앞 {tip:0.0} m({cnt})"); mi++; }
                }
                sb.AppendLine($"(A) 그림만 {L:0} m:{line} · 사라짐 {(end < 0f ? ">2" : end.ToString("0.00"))}초");
            }

            // (B) 실제 발동
            async UniTask<string> Fire(string weaponPath, int evo, int form, string augs, float[] dists)
            {
                Managers.UI?.CloseAllPopupUI(); Time.timeScale = 1f;
                if (!await EquipAsync(p, weaponPath, ct)) return "장착 실패";
                var w = p.WeaponManager.Weapon0Data;
                w.formTier = form; w.enhanceLevel = 0; w.evolutionStage = evo; w.augments = augs; w.RecomputeEnhancedStats();
                WeaponFormRuntime.Of(p).Rebuild(); GameRunBootstrapper.Instance.Run.EffectManager.Rebuild();
                if (form > 0) WeaponFormRuntime.Of(p).Current?.AddStacks(99);   // 가득 — 다음 막타가 바로 풍절
                DetachAll();
                GoHome(p, open, openRot);
                await Wait(0.5f, ct);
                s_home = p.transform.position;
                Vector3 fw = Flat(p.transform.forward);
                var tracks = new List<HpTrack>();
                foreach (var d in dists) tracks.Add(await TrackAsync(s_home + fw * d, ct));
                HideStrays();
                for (float q = 0f; q < 1f; q += 0.1f) { RefillAll(); Steer(p, s_home + fw * 3f); await Wait(0.1f, ct); }
                logs.Clear();
                var seenWave = new Dictionary<int, float>();
                var seenGhost = new Dictionary<int, float>();
                var hits = new List<string>[tracks.Count];
                for (int i = 0; i < hits.Length; i++) hits[i] = new List<string>();
                var last = new float[tracks.Count];
                for (int i = 0; i < tracks.Count; i++) last[i] = tracks[i] != null ? tracks[i].Dealt : 0f;
                float t0 = -1f, tStart = Time.time;
                string ghostEvents = "";
                while (Time.time - tStart < 8f)
                {
                    RefillAll(); Steer(p, s_home + fw * 3f);
                    if (t0 < 0f) p.InputBuffer.Push(Command.Light);   // 발동 전까지만 친다 — 발동 뒤엔 그 한 번만 본다
                    await UniTask.Yield(PlayerLoopTiming.PostLateUpdate, ct);
                    if (t0 < 0f && logs.Count > 0) { t0 = Time.time; p.InputBuffer.Clear(); }
                    if (t0 < 0f) { for (int i = 0; i < tracks.Count; i++) last[i] = tracks[i] != null ? tracks[i].Dealt : 0f; continue; }
                    float t = Time.time - t0;
                    foreach (var ws in Waves(s_home, fw)) if (!seenWave.ContainsKey(ws.id)) seenWave[ws.id] = t;
                    foreach (var a in UnityEngine.Object.FindObjectsByType<Animator>(FindObjectsSortMode.None))
                    {
                        if (a == null || a.gameObject.name != "PlayerGhostStrike" || !a.gameObject.activeInHierarchy) continue;
                        int id = a.gameObject.GetInstanceID();
                        if (seenGhost.ContainsKey(id)) continue;
                        seenGhost[id] = t;
                        if (ghostEvents.Length == 0)
                        {
                            var info = a.GetCurrentAnimatorClipInfo(0);
                            if (info.Length > 0 && info[0].clip != null)
                            {
                                var ev = new StringBuilder();
                                foreach (var e in info[0].clip.events) ev.Append($" {e.functionName}@{e.time:0.00}");
                                var st = a.GetCurrentAnimatorStateInfo(0);
                                ghostEvents = $"분신 클립 {info[0].clip.name} 길이 {info[0].clip.length:0.00}초 · 속도 {a.speed * st.speed:0.00} · 이벤트{ev}";
                            }
                        }
                    }
                    for (int i = 0; i < tracks.Count; i++)
                    {
                        if (tracks[i] == null) continue;
                        float dd = tracks[i].Dealt - last[i];
                        if (dd > 0.5f) hits[i].Add($"{t:0.00}초 {dd:0}");
                        last[i] = tracks[i].Dealt;
                    }
                    if (t > 1.5f) break;
                }
                p.InputBuffer.Clear();
                var r = new StringBuilder();
                r.Append($"발동 로그 {logs.Count}(첫 「{(logs.Count > 0 ? logs[0] : "없음")}」)");
                r.Append($"\n      검기 그림 {seenWave.Count}개 @");
                foreach (var kv in seenWave) r.Append($" {kv.Value:0.00}");
                r.Append($"\n      분신 {seenGhost.Count} @");
                foreach (var kv in seenGhost) r.Append($" {kv.Value:0.00}");
                if (ghostEvents.Length > 0) r.Append($" · {ghostEvents}");
                for (int i = 0; i < tracks.Count; i++) r.Append($"\n      표적 {dists[i]:0.0} m 피해: {(hits[i].Count > 0 ? string.Join(" · ", hits[i]) : "없음")}");
                await Wait(0.6f, ct);
                DetachAll();
                return r.ToString();
            }

            sb.AppendLine("(B1) 진화 카타나 + 검기 III + 쌍검기 III: " + await Fire(Katana, 1, 0, "aug_sword_wave:3,aug_twin_wave:3", new[] { 2f, 4f, 6f, 7.5f }));
            sb.AppendLine("(B2) 진화 카타나 형 1(풍절 8 m): " + await Fire(Katana, 1, 1, "", new[] { 2f, 4f, 6f, 8f }));
            sb.AppendLine("(B3) 무형검 형 없는 연타 III: " + await Fire(Nameless, 0, 0, "aug_nameless_formless:3", new[] { 1.5f, 2.4f, 3.5f }));
            sb.AppendLine("(B4) 진화 카타나 형 1(풍절) + 검기 III — 겹침: " + await Fire(Katana, 1, 1, "aug_sword_wave:3", new[] { 2f, 4f, 6f, 8f }));
        }
        finally
        {
            Application.logMessageReceived -= onLog;
            Time.timeScale = 1f;
        }
    }

    // 85 — 10-22 사용자 「추가 검기 · 복수의 투명 플레이어 · 판정 체크」 · 「무형검 E 관통 · 크게」 · 대검 평타 크기(판정에 먹나)
    private static async UniTask WaveGhost85Async(PlayerController p, StringBuilder sb, CancellationToken ct, bool onlyBeam = false)
    {
        sb.AppendLine("85 — 증강 검기 분신 · 판정(안 · 밖 표적) · 무형참 관통 · 대검 평타 크기 · 사진 Temp/ui_shots/WG85_<줄>_<0~2>");
        await Wait(3f, ct);
        foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
            if (m != null && !m.IsDead) m.gameObject.SetActive(false);
        Directory.CreateDirectory("Temp/ui_shots");
        Vector3 open = p.transform.position + Flat(p.transform.forward) * 8f;
        Quaternion openRot = Quaternion.LookRotation(Flat(p.transform.forward));
        var camGo = new GameObject("~WaveGhost85Cam");
        var cam = camGo.AddComponent<Camera>();
        if (Camera.main != null) cam.CopyFrom(Camera.main);
        cam.enabled = false; cam.fieldOfView = 55f;
        var logs = new List<string>();
        Application.LogCallback onLog = (msg, _, __) => { if (msg.StartsWith("[MeleeAugment]") || msg.StartsWith("[WeaponForm]")) logs.Add(msg.Length > 15 ? msg.Substring(msg.IndexOf(']') + 2) : msg); };
        Application.logMessageReceived += onLog;

        // 표적을 세우고 평타를 seconds초 누른다 — 증강 로그 · 표적별 받은 피해 · 첫 발동 뒤 사진 3장
        async UniTask<string> Swing(string tag, string weaponPath, int evo, string augs, (float fwd, float side)[] spots, float seconds, int form = 0)
        {
            Managers.UI?.CloseAllPopupUI(); Time.timeScale = 1f;
            if (!await EquipAsync(p, weaponPath, ct)) return "장착 실패";
            var w = p.WeaponManager.Weapon0Data;
            w.formTier = form; w.enhanceLevel = 0; w.evolutionStage = evo; w.augments = augs; w.RecomputeEnhancedStats();
            WeaponFormRuntime.Of(p).Rebuild(); GameRunBootstrapper.Instance.Run.EffectManager.Rebuild();
            DetachAll();
            GoHome(p, open, openRot);
            await Wait(0.5f, ct);
            s_home = p.transform.position;
            Vector3 fw = Flat(p.transform.forward), right = Vector3.Cross(Vector3.up, fw);
            var tracks = new List<HpTrack>();
            foreach (var (fwd, side) in spots) tracks.Add(await TrackAsync(s_home + fw * fwd + right * side, ct));
            HideStrays();
            for (float q = 0f; q < 1.2f; q += 0.1f) { RefillAll(); Steer(p, s_home + fw * 3f); await Wait(0.1f, ct); }
            logs.Clear();
            var dealt0 = new float[tracks.Count];
            for (int i = 0; i < tracks.Count; i++) dealt0[i] = tracks[i] != null ? tracks[i].Dealt : 0f;
            float t0 = Time.time; int shot = 0; float firstAt = -1f;
            while (Time.time - t0 < seconds)
            {
                RefillAll(); Steer(p, s_home + fw * 3f); p.InputBuffer.Push(Command.Light);
                await UniTask.Yield(PlayerLoopTiming.PostLateUpdate, ct);
                if (firstAt < 0f && logs.Count > 0) firstAt = Time.time;
                if (firstAt > 0f && shot < 3 && Time.time - firstAt >= 0.05f + 0.12f * shot)
                {
                    Vector3 pos = s_home + fw * 2f;
                    cam.transform.position = pos - fw * 4.5f + right * 4.5f + Vector3.up * 4.2f;
                    cam.transform.LookAt(pos + Vector3.up * 0.8f);
                    CaptureMain(cam, $"Temp/ui_shots/WG85_{tag}_{shot}.png");
                    shot++;
                }
            }
            p.InputBuffer.Clear();
            var r = new StringBuilder();
            for (int i = 0; i < tracks.Count; i++)
                r.Append($" ({spots[i].fwd:0.0},{spots[i].side:0.0}) {(tracks[i] != null ? tracks[i].Dealt - dealt0[i] : -1f):0}");
            var ghost = p.GetComponent<PlayerGhostStrike>();
            string first = logs.Count > 0 ? logs[0] : "없음";
            int n = logs.Count;
            await Wait(0.6f, ct);
            DetachAll();
            return $"증강 로그 {n}(첫 「{first}」) · 표적별 받은 피해{r} · 분신 {(ghost != null ? "있음" : "없음")}";
        }

        try
        {
            if (!onlyBeam) {
            // ① 진화 전 무형검 — 형 없는 연타(앞 2.5 m · 반폭 1) 안 2.0 m · 밖 3.6 m
            sb.AppendLine("① 무형검 형 없는 연타 III: " + await Swing("formless", Nameless, 0, "aug_nameless_formless:3",
                new[] { (2.0f, 0f), (3.6f, 0f) }, 4f));
            // ② 진화한 카타나 + 무형검 증강(환영 몫) + 검기 III + 쌍검기 III — 분신 여럿 · 갈래 판정(정면 5 m · ±20° 5 m · 밖 7.5 m)
            sb.AppendLine("② 진화 카타나 형 없는 연타 + 검기 + 쌍검기: " + await Swing("katana", Katana, 1,
                "aug_nameless_formless:3,aug_sword_wave:3,aug_twin_wave:3",
                new[] { (5f, 0f), (4.7f, 1.71f), (4.7f, -1.71f), (7.5f, 0f), (2.0f, 0f) }, 5f));
            // ③ 진화한 대검 + 튕기는 칼끝 III(치명 갈래 — 둘레 5 m)
            sb.AppendLine("③ 진화 대검 튕기는 칼끝: " + await Swing("ricochet", Great, 1, "aug_ricochet:3",
                new[] { (2.5f, 0f), (4.5f, 2.5f), (4.5f, -2.5f), (8f, 4f) }, 5f));
            // ④ 대검 평타 크기가 판정에 먹나(옆 2.5 m · 앞 3.6 m)
            sb.AppendLine("④ 대검 평타(증강 없음): " + await Swing("gsbasic", Great, 0, "",
                new[] { (2.5f, 0f), (0.5f, 2.5f), (3.6f, 0f) }, 4f));
            // ⑥ ~ ⑧ 무기 계열 색(사진만 — 증강 로그가 처음 나온 뒤 3장)
            sb.AppendLine("⑥ 대검 지진 III(노바 = 흙먼지 고리): " + await Swing("quake", Great, 0, "aug_great_quake:3",
                new[] { (2.5f, 0f), (3.5f, 1.5f) }, 4f));
            sb.AppendLine("⑦ 진화 카타나 형 1(풍절 = 붉은 검기): " + await Swing("windblade", Katana, 1, "",
                new[] { (2.5f, 0f), (6f, 0f) }, 6f, 1));
            sb.AppendLine("⑧ 진화 대검 형 1(무게 내려찍기 = 고리): " + await Swing("weight", Great, 1, "",
                new[] { (2.5f, 0f), (3f, 1.5f) }, 6f, 1));
            }

            // ⑤ 무형참 1단계 — 한 줄 3 · 6 · 9 m + 5 m 옆 1.4 m(관통 · 폭)
            Managers.UI?.CloseAllPopupUI();
            if (await EquipAsync(p, Nameless, ct))
            {
                var w = p.WeaponManager.Weapon0Data; w.augments = ""; w.enhanceLevel = 0; w.evolutionStage = 0; w.RecomputeEnhancedStats();
                GameRunBootstrapper.Instance.Run.EffectManager.Rebuild();
                DetachAll();
                GoHome(p, open, openRot);
                await Wait(0.5f, ct);
                s_home = p.transform.position;
                Vector3 fw = Flat(p.transform.forward), right = Vector3.Cross(Vector3.up, fw);
                var line = new List<HpTrack>();
                foreach (var (fwd, side) in new[] { (3f, 0f), (6f, 0f), (9f, 0f), (5f, 1.4f), (5f, 2.4f) })
                    line.Add(await TrackAsync(s_home + fw * fwd + right * side, ct));
                HideStrays();
                foreach (var t in line)   // 10-23 — 9 m 0타가 표적 자리(NavMesh 보정 · 방 밖) 탓인지 가린다
                    if (t?.M != null)
                    {
                        var d = t.M.transform.position - s_home;
                        sb.AppendLine($"   표적 앞 {Vector3.Dot(d, fw):0.0} m · 옆 {Vector3.Dot(d, right):0.0} · 높이 {d.y:0.00} · 켜짐 {t.M.isActiveAndEnabled} · 무적 {t.M.IsDamageImmuneNow}");
                    }
                var editorMode0 = InputSystem.settings.editorInputBehaviorInPlayMode;
                var background0 = InputSystem.settings.backgroundBehavior;
                InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                var aimObj = typeof(PlayerController).GetField("_aim", Inst)?.GetValue(p) as PlayerAim;
                float aimErr = 999f;
                try
                {
                    for (int k = 0; k < 40; k++)
                    {
                        RefillAll(); Steer(p, s_home + fw * 9f);
                        await UniTask.DelayFrame(2, cancellationToken: ct);
                        if (aimObj != null && aimObj.TryComputeLookDir(p.transform, out var look)) aimErr = Vector3.Angle(look, fw);
                        if (k > 8 && aimErr < 3f) break;
                    }
                }
                finally { }
                sb.AppendLine($"   조준 오차 {aimErr:0.0}°");
                var d0 = line.ConvertAll(t => t != null ? t.Dealt : 0f);
                var hitLog = new StringBuilder();
                float e0 = Time.time;
                Action<HitInfo> onBeam = h =>
                {
                    if (h.Attacker != p.gameObject || h.Target == null) return;
                    float dist = Vector3.Distance(s_home, h.Target.transform.position);
                    hitLog.Append($" [{Time.time - e0:0.00}초 {h.Target.name} {dist:0.0} m {h.Damage:0}]");
                };
                HitFeedbackService.OnHit += onBeam;
                p.CooldownTracker.ResetCooldown(SkillType.E);
                p.InputBuffer.Push(Command.ESkill);
                int shot = 0;
                var seenArrows = new Dictionary<int, (Vector3 first, Vector3 last, float born, float gone)>();
                while (Time.time - e0 < 1.6f)
                {
                    RefillAll();
                    await UniTask.Yield(PlayerLoopTiming.PostLateUpdate, ct);
                    foreach (var a in UnityEngine.Object.FindObjectsByType<BasicArrow>(FindObjectsSortMode.None))
                    {
                        if (a == null || !a.gameObject.activeInHierarchy) continue;
                        int id = a.GetInstanceID();
                        if (!seenArrows.TryGetValue(id, out var rec)) rec = (a.transform.position, a.transform.position, Time.time - e0, -1f);
                        rec.last = a.transform.position; rec.gone = Time.time - e0;
                        seenArrows[id] = rec;
                    }
                    if (shot < 3 && Time.time - e0 >= 0.4f + 0.12f * shot)
                    {
                        Vector3 pos = s_home + fw * 5f;
                        cam.transform.position = pos - fw * 5f + right * 5f + Vector3.up * 5f;
                        cam.transform.LookAt(pos);
                        CaptureMain(cam, $"Temp/ui_shots/WG85_beam_{shot}.png");
                        shot++;
                    }
                }
                var r = new StringBuilder();
                string[] names = { "3m", "6m", "9m", "5m옆1.4", "5m옆2.4" };
                for (int i = 0; i < line.Count; i++) r.Append($" {names[i]} {(line[i] != null ? line[i].Dealt - d0[i] : -1f):0}");
                InputSystem.settings.editorInputBehaviorInPlayMode = editorMode0;
                InputSystem.settings.backgroundBehavior = background0;
                HitFeedbackService.OnHit -= onBeam;
                sb.AppendLine($"⑤ 무형참 {SkillTierResolver.Resolve(w)}단계: 받은 피해{r}");
                sb.AppendLine($"   적중{hitLog}");
                foreach (var kv in seenArrows)
                    sb.AppendLine($"   투사체 {kv.Key}: {kv.Value.born:0.00} → {kv.Value.gone:0.00}초 · 처음 {Vector3.Distance(s_home, kv.Value.first):0.0} m → 마지막 {Vector3.Distance(s_home, kv.Value.last):0.0} m(앞 {Vector3.Dot(kv.Value.last - s_home, fw):0.0} · 옆 {Vector3.Dot(kv.Value.last - s_home, right):0.0} · 높이 처음 {kv.Value.first.y - s_home.y:0.00} → 마지막 {kv.Value.last.y - s_home.y:0.00})");
                DetachAll();

                // 검기 그림 폭(보이는 양) — 판정 반경 1.5 m와 견준다
                Vector3 at = s_home + fw * 4f + Vector3.up;
                HideStrays();
                var before = EffectReachProbe.Snapshot();
                GameObject vis = null;
                SpawnPooled("NamelessBeam", at, Quaternion.LookRotation(fw), 0.52f, go => vis = go);
                var rr = await EffectReachProbe.ObserveAsync(before, at, fw, 0.8f, ct);
                if (vis != null) Managers.ObjectPooler.Despawn(vis);
                sb.AppendLine($"   무형참 검기 그림 ×0.52 — 옆으로 보이는 폭(반) {rr.Lateral:0.00} m · 앞뒤 {rr.Forward:0.00} m(판정 반경 1.5 m)");
            }
        }
        finally
        {
            Application.logMessageReceived -= onLog;
            UnityEngine.Object.Destroy(camGo);
            DetachAll();
        }
    }

    private static async UniTask Skills66Async(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        sb.AppendLine("66 — 평타 누름 → 베기 · 스킬 묶임 · 같은 시간 평타 대비 피해 · 1.2 m 비켜 선 적(유물 없음 · 형 끔 · 강화 0 · 표적 2 m)");
        var actField = typeof(PlayerController).GetField("actStateDebug", Inst);
        bool InSkill() => (actField?.GetValue(p)?.ToString() ?? "").Contains("Skill");
        await Wait(3f, ct);
        foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
            if (m != null && !m.IsDead) m.gameObject.SetActive(false);
        var recv = p.GetComponentInChildren<PlayerAnimationEventReceiver>();
        foreach (var (path, label) in new[] { (Nameless, "무형검"), (Katana, "카타나"), (Great, "대검") })
        {
            Managers.UI?.CloseAllPopupUI(); Time.timeScale = 1f;
            if (!await EquipAsync(p, path, ct)) { sb.AppendLine($"  {label} 장착 실패"); continue; }
            var w = p.WeaponManager.Weapon0Data; w.augments = ""; w.formTier = 0;
            WeaponFormRuntime.Of(p).Rebuild(); GameRunBootstrapper.Instance.Run.EffectManager.Rebuild();
            await Wait(0.5f, ct);
            s_home = p.transform.position;
            Vector3 fw = Flat(p.transform.forward), right = Vector3.Cross(Vector3.up, fw);
            DetachAll();
            var t = await TrackAsync(s_home + fw * 2f, ct);
            if (t == null) { sb.AppendLine($"  {label} 표적 실패"); continue; }
            HideStrays();

            // ① 한 번 누름 → 첫 베기 이벤트 · 첫 적중(2번 평균)
            float slashSum = 0f, hitSum = 0f; int ok = 0;
            for (int k = 0; k < 2; k++)
            {
                for (float q = 0f; q < 1.2f; q += 0.1f) { RefillAll(); Steer(p, t.M.transform.position); await Wait(0.1f, ct); }
                float t0 = Time.realtimeSinceStartup, slashAt = -1f, hitAt = -1f;
                Action<int> onS = _ => { if (slashAt < 0f) slashAt = Time.realtimeSinceStartup - t0; };
                Action<HitInfo> onH = h => { if (hitAt < 0f && h.Attacker == p.gameObject) hitAt = Time.realtimeSinceStartup - t0; };
                if (recv != null) recv.OnEffectStep += onS;
                HitFeedbackService.OnHit += onH;
                try
                {
                    p.InputBuffer.Push(Command.Light);
                    while (Time.realtimeSinceStartup - t0 < 1f && (slashAt < 0f || hitAt < 0f)) { RefillAll(); await UniTask.Yield(PlayerLoopTiming.Update, ct); }
                }
                finally { if (recv != null) recv.OnEffectStep -= onS; HitFeedbackService.OnHit -= onH; }
                if (slashAt >= 0f) { slashSum += slashAt; hitSum += Mathf.Max(0f, hitAt); ok++; }
            }
            // ② 평타 4초(0.05초마다 누름) 초당 피해
            for (float q = 0f; q < 1f; q += 0.1f) { RefillAll(); await Wait(0.1f, ct); }
            RefillAll(); t.Dealt = 0f;
            float b0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - b0 < 4f) { RefillAll(); Steer(p, t.M.transform.position); p.InputBuffer.Push(Command.Light); await Wait(0.05f, ct); }
            p.InputBuffer.Clear();
            float dps = t.Dealt / Mathf.Max(0.01f, Time.realtimeSinceStartup - b0);
            sb.AppendLine($"  {label}: 누름 → 첫 베기 {(ok > 0 ? slashSum / ok : -1f):0.00}초 · 첫 적중 {(ok > 0 ? hitSum / ok : -1f):0.00}초 · 평타 {dps:0}/초 · 공격력 {p.RuntimeStats.MeleeAttack}");

            // ③ E · R — 묶임(누름 → 스킬 끝) · 회피로 끊을 수 있게 되는 시각 · 피해(끝 + 1초까지) · 같은 시간 평타 대비 · 적중 수
            foreach (var (cmd, type, nm) in new[] { (Command.ESkill, SkillType.E, "E"), (Command.RSkill, SkillType.R, "R") })
            {
                for (float q = 0f; q < 1.5f; q += 0.1f) { RefillAll(); Steer(p, t.M.transform.position); await Wait(0.1f, ct); }
                p.CooldownTracker.ResetCooldown(type);
                RefillAll(); t.Dealt = 0f;
                int hits = 0;
                Action<HitInfo> onH = h => { if (h.Attacker == p.gameObject) hits++; };
                HitFeedbackService.OnHit += onH;
                float lockS = -1f, recAt = -1f, s0 = Time.realtimeSinceStartup;
                try
                {
                    p.InputBuffer.Push(cmd);
                    bool began = false;
                    while (Time.realtimeSinceStartup - s0 < 1f && !(began = InSkill())) { RefillAll(); Steer(p, t.M.transform.position); await UniTask.Yield(PlayerLoopTiming.Update, ct); }
                    if (!began) { sb.AppendLine($"     {nm}: 시전 안 됨"); continue; }
                    while (InSkill() && Time.realtimeSinceStartup - s0 < 6f)
                    {
                        RefillAll();
                        if (recAt < 0f && p.SkillRecovering) recAt = Time.realtimeSinceStartup - s0;
                        await UniTask.Yield(PlayerLoopTiming.Update, ct);
                    }
                    lockS = Time.realtimeSinceStartup - s0;
                    for (float q = 0f; q < 1f; q += 0.1f) { RefillAll(); await Wait(0.1f, ct); }
                }
                finally { HitFeedbackService.OnHit -= onH; }
                float same = dps * lockS;
                sb.AppendLine($"     {nm}: 묶임 {lockS:0.00}초 · 회피로 끊기 가능 {(recAt >= 0f ? recAt.ToString("0.00") + "초" : "없음")} · 피해 {t.Dealt:0} · 같은 시간 평타 {same:0} · 배수 ×{(same > 0f ? t.Dealt / same : 0f):0.00} · 적중 {hits}");
            }

            // ④ 비켜 선 적 — 정면 2 m 표적 + 1.2 m 옆 표적: 스킬이 옆 표적도 맞히나(E · R)
            DetachAll();
            var c = await TrackAsync(s_home + fw * 2f, ct);
            var side = await TrackAsync(s_home + fw * 2f + right * 1.2f, ct);
            if (c != null && side != null)
            {
                HideStrays();
                var res = new StringBuilder();
                foreach (var (cmd, type, nm) in new[] { (Command.ESkill, SkillType.E, "E"), (Command.RSkill, SkillType.R, "R") })
                {
                    for (float q = 0f; q < 1.2f; q += 0.1f) { RefillAll(); Steer(p, c.M.transform.position); await Wait(0.1f, ct); }
                    p.CooldownTracker.ResetCooldown(type);
                    int sideHits = 0;
                    Action<HitInfo> onH = h => { if (h.Attacker == p.gameObject && h.Target == side.M.gameObject) sideHits++; };
                    HitFeedbackService.OnHit += onH;
                    try
                    {
                        p.InputBuffer.Push(cmd);
                        float s1 = Time.realtimeSinceStartup;
                        while (Time.realtimeSinceStartup - s1 < 3f) { RefillAll(); await UniTask.Yield(PlayerLoopTiming.Update, ct); }
                    }
                    finally { HitFeedbackService.OnHit -= onH; }
                    res.Append($" {nm} {sideHits}타");
                }
                sb.AppendLine($"     1.2 m 비켜 선 적 맞음 —{res}");
            }
            DetachAll();
        }
    }

    /// <summary>
    /// 67 — 근접 스킬 애니 맞물림(10-18): 적중 시각 대 오른손 빠르기 봉우리(타격 프레임 대용 · 몸 기준이라 돌진은 빠진다) ·
    /// 클립 끝 대 스킬 끝 · 마지막 적중 뒤 손이 멈춘 시간(굳은 대기 · 의미 없는 후딜).
    /// </summary>
    private static async UniTask SkillSync67Async(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        sb.AppendLine("67 — 근접 스킬 애니 맞물림(유물 없음 · 형 끔 · 강화 0 · 표적 2 m) — 게임 시간(애니 · 스킬 타이머와 같은 시계) · 손 빠르기 = 몸 기준 · 봉우리 = 최고의 50% 이상 · 0.08초 간격 · 멈춤 = 0.4 m/s 아래");
        var actField = typeof(PlayerController).GetField("actStateDebug", Inst);
        bool InSkill() => (actField?.GetValue(p)?.ToString() ?? "").Contains("Skill");
        string J(List<float> xs) { var b = new StringBuilder(); foreach (var x in xs) b.Append(' ').Append(x.ToString("0.00")); return b.ToString(); }
        await Wait(3f, ct);
        foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
            if (m != null && !m.IsDead) m.gameObject.SetActive(false);
        var anim = p.Anim;
        var hand = anim != null && anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.RightHand) : null;
        if (hand == null) sb.AppendLine("  오른손 뼈 없음 — 손 봉우리 생략");
        foreach (var (path, label) in new[] { (Nameless, "무형검"), (Katana, "카타나"), (Great, "대검") })
        {
            Managers.UI?.CloseAllPopupUI(); Time.timeScale = 1f;
            if (!await EquipAsync(p, path, ct)) { sb.AppendLine($"  {label} 장착 실패"); continue; }
            var w = p.WeaponManager.Weapon0Data; w.augments = ""; w.formTier = 0;
            WeaponFormRuntime.Of(p).Rebuild(); GameRunBootstrapper.Instance.Run.EffectManager.Rebuild();
            await Wait(0.5f, ct);
            s_home = p.transform.position;
            Vector3 fw = Flat(p.transform.forward);
            DetachAll();
            var t = await TrackAsync(s_home + fw * 2f, ct);
            if (t == null) { sb.AppendLine($"  {label} 표적 실패"); continue; }
            HideStrays();
            sb.AppendLine($"  {label}");
            foreach (var (cmd, type, nm) in new[] { (Command.ESkill, SkillType.E, "E"), (Command.RSkill, SkillType.R, "R") })
            {
                for (float q = 0f; q < 1.5f; q += 0.1f) { RefillAll(); Steer(p, t.M.transform.position); await Wait(0.1f, ct); }
                p.CooldownTracker.ResetCooldown(type);
                RefillAll();
                var hits = new List<float>();
                var samples = new List<(float t, float v)>();
                var clips = new StringBuilder();
                var lastClip = new string[anim != null ? anim.layerCount : 0];
                var clipEnded = new HashSet<string>();
                float s0 = Time.time, recAt = -1f, endAt = -1f;
                Action<HitInfo> onH = h =>
                {
                    if (h.Attacker != p.gameObject) return;
                    float now = Time.time - s0;
                    if (hits.Count == 0 || now - hits[hits.Count - 1] > 0.02f) hits.Add(now);   // 한 프레임 여러 적중 = 한 번
                };
                HitFeedbackService.OnHit += onH;
                try
                {
                    p.InputBuffer.Push(cmd);
                    Vector3 prev = hand != null ? p.transform.InverseTransformPoint(hand.position) : Vector3.zero;
                    float prevT = 0f;
                    bool began = false;
                    while (Time.time - s0 < 6f)
                    {
                        await UniTask.Yield(PlayerLoopTiming.PostLateUpdate, ct);   // 애니 평가가 끝난 뒤 — Update에서 읽으면 앞 프레임 자세를 이번 프레임 간격으로 나눠 타격 정지 때 손 빠르기가 튀었다
                        RefillAll();
                        float now = Time.time - s0;
                        if (hand != null)
                        {
                            Vector3 cur = p.transform.InverseTransformPoint(hand.position);
                            if (now - prevT > 0.0001f) samples.Add((now, (cur - prev).magnitude / (now - prevT)));
                            prev = cur; prevT = now;
                        }
                        for (int l = 0; anim != null && l < anim.layerCount; l++)
                        {
                            if (l > 0 && anim.GetLayerWeight(l) < 0.5f) continue;
                            var info = anim.GetCurrentAnimatorClipInfo(l);
                            if (info.Length == 0 || info[0].clip == null) continue;
                            string c = info[0].clip.name;
                            if (c != lastClip[l]) { clips.Append($" L{l}:{c}@{now:0.00}"); lastClip[l] = c; }
                            var st = anim.GetCurrentAnimatorStateInfo(l);
                            if (!st.loop && st.normalizedTime >= 1f && clipEnded.Add(l + c)) clips.Append($"(끝@{now:0.00})");
                        }
                        bool inSkill = InSkill();
                        if (inSkill) began = true;
                        if (began && recAt < 0f && p.SkillRecovering) recAt = now;
                        if (began && !inSkill && endAt < 0f) endAt = now;
                        if (!began && now > 1f) break;
                        if (endAt >= 0f && now > endAt + 0.3f) break;
                    }
                }
                finally { HitFeedbackService.OnHit -= onH; }
                if (endAt < 0f) { sb.AppendLine($"    {nm}: 시전 안 됨 또는 6초 넘음"); continue; }

                float vmax = 0f;
                foreach (var s in samples) if (s.t <= endAt) vmax = Mathf.Max(vmax, s.v);
                var peaks = new List<float>();
                for (int i = 1; i + 1 < samples.Count; i++)
                {
                    var s = samples[i];
                    if (s.t > endAt || s.v < vmax * 0.5f || s.v < samples[i - 1].v || s.v < samples[i + 1].v) continue;
                    if (peaks.Count > 0 && s.t - peaks[peaks.Count - 1] < 0.08f) continue;
                    peaks.Add(s.t);
                }
                var off = new StringBuilder();
                foreach (var h in hits)
                {
                    if (h > endAt + 0.3f) continue;
                    float best = float.MaxValue;
                    foreach (var pk in peaks) if (Mathf.Abs(h - pk) < Mathf.Abs(best)) best = h - pk;
                    off.Append(peaks.Count > 0 ? $" {best:+0.00;-0.00}" : " ?");
                }
                float lastHit = hits.Count > 0 ? hits[hits.Count - 1] : 0f, still = 0f;
                for (int i = 1; i < samples.Count; i++)
                    if (samples[i].t > lastHit && samples[i].t <= endAt && samples[i].v < 0.4f) still += samples[i].t - samples[i - 1].t;
                sb.AppendLine($"    {nm}: 적중{J(hits)} · 손 봉우리{J(peaks)} (최고 {vmax:0.0} m/s) · 적중−봉우리{off}");
                sb.AppendLine($"       후딜 {(recAt >= 0f ? recAt.ToString("0.00") : "없음")} · 스킬 끝 {endAt:0.00} · 마지막 적중 뒤 손 멈춤 {still:0.00}초 · 클립{clips}");
            }
            DetachAll();
        }
    }

    /// <summary>
    /// 68 — 룬 · 서약 이펙트(10-18): 게임과 같은 재생 함수 · 인자로 띄워 보이는 양 90% 도달(EffectReachProbe)을 판정과 견주고,
    /// 판정 외곽선과 함께 0.3초에 찍는다(Temp/ui_shots/RF_NN.png). 판정이 없는 줄(한 대상 · 줄기)은 사진으로만 본다.
    /// </summary>
    private static async UniTask RuneFx68Async(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        sb.AppendLine("68 — 룬 · 서약 이펙트(보이는 양 90% 도달 대 판정 · 0.8~1.25배 맞음) · 사진 Temp/ui_shots/RF_NN");
        SkillRangeRing.PlayerGuides = true;   // 사진에 판정 고리가 보이게(게임에선 꺼짐 · 10-19 ⑮)
        await Wait(3f, ct);
        foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
            if (m != null && !m.IsDead) m.gameObject.SetActive(false);
        Directory.CreateDirectory("Temp/ui_shots");
        CovenantFxService.Preload();
        LegendaryRuntime.EnsureExists();
        ElementVfxPlayer.Warmup();
        await Wait(1.5f, ct);
        int ok = 0, bad = 0, shot = 0;
        Vector3 o = p.transform.position, f = Flat(p.transform.forward), right = Vector3.Cross(Vector3.up, f);
        var cat = LegendaryRuntime.Catalog;

        async UniTask Row(string label, Action play, Vector3 origin, float hitF, float hitL, float hitR, string hitText, Action ring = null, float shotAt = 0.3f)
        {
            foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))   // 허브가 새로 부른 몬스터 이펙트가 섞였다(1회차)
                if (m != null) m.gameObject.SetActive(false);
            ring?.Invoke();
            var before = EffectReachProbe.Snapshot();
            play();
            var watch = EffectReachProbe.ObserveAsync(before, origin, f, 1.2f, ct);
            await Wait(shotAt, ct);
            string name = $"RF_{++shot:00}";
            if (Camera.main != null) CaptureMain(Camera.main, $"Temp/ui_shots/{name}.png");
            var r = await watch;
            var parts = new List<string>();
            bool judged = hitF > 0f || hitL > 0f || hitR > 0f, fine = r.Any;
            if (hitF > 0f) { parts.Add($"앞 {r.Forward:0.0}/{hitF:0.#} m({EffectReachProbe.Ratio(r.Forward, hitF)})"); fine &= EffectReachProbe.Ok(r.Forward, hitF); }
            if (hitL > 0f) { parts.Add($"옆 ±{r.Lateral:0.0}/{hitL:0.#}({EffectReachProbe.Ratio(r.Lateral, hitL)})"); fine &= EffectReachProbe.Ok(r.Lateral, hitL); }
            if (hitR > 0f) { parts.Add($"반경 {r.Radius:0.0}/{hitR:0.#} m({EffectReachProbe.Ratio(r.Radius, hitR)})"); fine &= EffectReachProbe.Ok(r.Radius, hitR); }
            if (!judged) parts.Add($"앞 {r.Forward:0.0} · 옆 ±{r.Lateral:0.0} · 반경 {r.Radius:0.0}");
            string verdict = !r.Any ? "이펙트 없음" : !judged ? "사진으로" : fine ? "맞음" : "어긋남";
            sb.AppendLine($"  {name} {verdict} — {label} · 판정 {hitText} · {string.Join(" · ", parts)} · 입자계 {r.Systems}({r.Names})");
            if (judged) { if (fine) ok++; else bad++; }
            await Wait(0.9f, ct);
        }

        sb.AppendLine("\n■ 룬");
        await Row("빛 룬 광폭발(10-18 — 원형 폭발 앞 3.5 m · 반경 3.5)", () => ElementVfxPlayer.PlayBurst(RuneElement.Light, o + f * 3.5f, 3.5f), o, 7f, 5.4f, 0f, "부채꼴 7 m · ±50°(끝 옆 5.4)", () => SkillRangeRing.Fan(o, f, 7f, 100f));
        foreach (var (at, rad) in new[] { (3.5f, 2.3f), (3.5f, 1.8f), (3.2f, 2.0f) })
            await Row($"빛 룬 광폭발 후보 — 앞 {at:0.0} m · 반경 {rad:0.0}", () => ElementVfxPlayer.PlayBurst(RuneElement.Light, o + f * at, rad), o, 7f, 5.4f, 0f, "부채꼴 7 m · ±50°(끝 옆 5.4)", () => SkillRangeRing.Fan(o, f, 7f, 100f));
        await Row("번개 광역 폭발(공칭)", () => ElementVfxPlayer.PlayBurst(RuneElement.Electric, o, 3f), o, 0f, 0f, 3f, "원 3 m", () => SkillRangeRing.Circle(o, 3f));
        await Row("불 룬 작열 폭발", () => ElementVfxPlayer.PlayBurst(RuneElement.Fire, o, 4f), o, 0f, 0f, 4f, "원 4 m", () => SkillRangeRing.Circle(o, 4f));
        foreach (var (tag, el) in new[] { ("얼음", RuneElement.Ice), ("풀", RuneElement.Grass), ("어둠", RuneElement.Dark) })
            await Row($"{tag} 광역 폭발(공칭)", () => ElementVfxPlayer.PlayBurst(el, o, 3f), o, 0f, 0f, 3f, "원 3 m", () => SkillRangeRing.Circle(o, 3f));
        Vector3 corpse = o + f * 2f;
        await Row("독 퍼뜨리기 줄기(10-18 새 — 시체 → 받는 적 2)", () =>
        {
            ElementVfxPlayer.PlayBeam(RuneElement.Grass, corpse + Vector3.up * 0.8f, corpse + f * 2.5f + right * 1.5f + Vector3.up * 0.8f, 0.3f);
            ElementVfxPlayer.PlayBeam(RuneElement.Grass, corpse + Vector3.up * 0.8f, corpse + f * 1.5f - right * 2.5f + Vector3.up * 0.8f, 0.3f);
        }, corpse, 0f, 0f, 0f, "옮김(판정 없음 · 4 m 안 2명)", null, 0.08f);
        await Row("불꽃 반사 충격(10-18 새 — 때린 적 자리)", () => ElementVfxPlayer.PlayBurst(RuneElement.Fire, o + f * 2f, 1f), o + f * 2f, 0f, 0f, 0f, "때린 적 하나");

        sb.AppendLine("\n■ 전설");
        if (cat != null)
        {
            Vector3 a = o + f * 2f + Vector3.up, b = o + f * 5f + right * 2f + Vector3.up;
            await Row("번개 T-1 연쇄 줄기(10-18 새)", () => ElementVfxPlayer.PlayBeam(RuneElement.Electric, a, b, 0.2f), o + f * 2f, 0f, 0f, 0f, "다음 적 하나", null, 0.08f);
            Vector3 t = o + f * 3f;
            await Row("화염 F-2 마지막 튄 피해(×0.42)", () => LegendaryRuntime.SpawnVfx(cat.fireHitVfx, t, Quaternion.identity, 2f, 0.42f), t, 0f, 0f, 2f, "원 2 m", () => SkillRangeRing.Circle(t, 2f));
            await Row("얼음 I-3 빙판(×0.13)", () => LegendaryRuntime.SpawnVfx(cat.iceFieldVfx, t, Quaternion.identity, 2f, 0.13f), t, 0f, 0f, 2f, "원 2 m", () => SkillRangeRing.Circle(t, 2f));
            Vector3 spawn = t + Quaternion.Euler(-40f, 30f, 0f) * Vector3.forward * 1.2f;
            await Row("얼음 I-2 얼음 창(Icicle ×0.15)", () => LegendaryRuntime.SpawnVfx(cat.iceSingleVfx, t + Vector3.up * 0.6f, Quaternion.LookRotation(t - spawn), 1f, 0.15f), t, 0f, 0f, 0f, "한 대상(창 5개)");
            await Row("번개 T-3 구체 폭발(×3)", () => LegendaryRuntime.SpawnVfx(cat.elecOrbHitVfx, t, Quaternion.identity, 2f, 3f), t, 0f, 0f, 2f, "원 2 m", () => SkillRangeRing.Circle(t, 2f));
        }
        else sb.AppendLine("  전설 목록 없음(LegendaryRuntime.Catalog null)");

        sb.AppendLine("\n■ 서약(CovenantVfxSet · 반경 맞춤 항목은 3 · 5 m 둘 다)");
        Vector3 c = o + f * 3f;
        foreach (var key in new[] { "supernova", "detonate", "stasis" })
            foreach (float rad in new[] { 3f, 5f })
                await Row($"서약 {key} 반경 {rad:0}", () => CovenantFxService.Burst(key, c, rad), c, 0f, 0f, rad, $"원 {rad:0} m", () => SkillRangeRing.Circle(c, rad));
        foreach (var key in new[] { "detonate_hit", "execute", "harvest", "hemorrhage" })
            await Row($"서약 {key}", () => CovenantFxService.Burst(key, c), c, 0f, 0f, 0f, "한 대상");

        // ── 리소스 교체 후보(10-18 2회차 · VFX 미리보기에서 고름) ──
        sb.AppendLine("\n■ 후보");
        const string SpellsFx = "Assets/RelicFairy/_Imported/EffectSource/Spells Pack/LWRP(URP)/Particles_LWRP/Prefabs/";
        void Cand(string path, Vector3 pos, Quaternion rot, float scale, Color tint, float life)
        {
            var pf = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (pf == null) { sb.AppendLine($"  후보 프리팹 없음 {path}"); return; }
            var go = UnityEngine.Object.Instantiate(pf, pos, rot);
            go.transform.localScale = pf.transform.localScale * scale;
            foreach (var col in go.GetComponentsInChildren<Collider>(true)) col.enabled = false;
            var cache = go.AddComponent<RelicFairy.Monster.LichVfxScaleCache>();
            cache.EnsureHierarchyScaling();
            if (tint.a > 0f) { cache.Prepare(false); cache.ApplyTint(tint); }
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true)) { ps.Clear(true); ps.Play(true); }
            UnityEngine.Object.Destroy(go, life);
        }
        Vector3 k = o + f * 3f;
        foreach (float rad in new[] { 3f, 5f })
            await Row($"후보 서약 폭발 = Spell_Fire_4(불덩이 고리) 공칭 3 · 반경 {rad:0}", () => Cand(SpellsFx + "Spells/Spell_Fire_4_LWRP.prefab", k, Quaternion.identity, rad / 3f, default, 2f), k, 0f, 0f, rad, $"원 {rad:0} m", () => SkillRangeRing.Circle(k, rad));
        foreach (float rad in new[] { 3f, 5f })
            await Row($"후보 서약 초신성 = Explosion_Light(빛 반짝임) 공칭 2 · 반경 {rad:0}", () => Cand(SpellsFx + "Projectiles/Explosion/Explosion_Light_LWRP.prefab", k, Quaternion.identity, rad / 2f, default, 2f), k, 0f, 0f, rad, $"원 {rad:0} m", () => SkillRangeRing.Circle(k, rad));
        const string SlashWave = "Assets/RelicFairy/_Imported/EffectSource/Hovl Studio 2/Sword slash VFX/Prefabs/Slash wave.prefab";
        foreach (var (tag, tint) in new[] { ("금색", new Color(1f, 0.8f, 0.35f, 1f)), ("밝은 금색(HDR)", new Color(2.6f, 1.9f, 0.7f, 1f)) })
            await Row($"후보 빛 룬 광폭발 = Hovl Slash wave ×0.45 {tag}", () => Cand(SlashWave, o + Vector3.up * 0.8f, Quaternion.LookRotation(f), 0.45f, tint, 1f), o, 7f, 5.4f, 0f, "부채꼴 7 m · ±50°(끝 옆 5.4)", () => SkillRangeRing.Fan(o, f, 7f, 100f));
        const string Icicle = "Assets/RelicFairy/_Imported/EffectSource/MasterStylizedProjectiles/Projectiles/Common/Prefabs/Icicle.prefab";
        await Row("후보 얼음 I-2 창 = Icicle ×0.15 · 다섯 방향에서 박힘", () =>
        {
            for (int i = 0; i < 5; i++)
            {
                Vector3 sp = k + Quaternion.Euler(-40f, i * 72f, 0f) * Vector3.forward * 1.2f;
                Cand(Icicle, k + Vector3.up * 0.6f, Quaternion.LookRotation(k - sp), 0.15f, default, 1f);
            }
        }, k, 0f, 0f, 0f, "한 대상(창 5개)");

        sb.Insert(0, $"맞음 {ok} · 어긋남 · 없음 {bad}\n");
    }

    /// <summary>
    /// 69 — 무형검 증강 환영 이식(⑧-기획 A · 10-18): 안개 II · 형 없는 연타 I · 잔상 I · 칼집 II를 단 무형검 +10 → 카타나 진화.
    /// 무형검 증강 3개가 남고 칼집은 카타나 증강 2개가 되는지 · 등 뒤 환영이 보이는지 · E · 콤보 끝 · R에서 환영 몫이 나가는지 · 다른 무기에선 숨는지.
    /// </summary>
    private static async UniTask Phantom69Async(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        sb.AppendLine("69 — 무형검 증강 환영 이식(유물 없음 · 표적 2 m)");
        await Wait(3f, ct);
        foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
            if (m != null && !m.IsDead) m.gameObject.SetActive(false);
        Directory.CreateDirectory("Temp/ui_shots");
        var wm = p.WeaponManager;
        if (!await EquipAsync(p, Nameless, ct)) { sb.AppendLine("무형검 장착 실패"); return; }
        var w0 = wm.Weapon0Data;
        w0.augments = "aug_nameless_mist:2,aug_nameless_formless:1,aug_nameless_afterimage:1,aug_nameless_sheath:2";
        w0.enhanceLevel = 10; w0.formTier = 0;
        WeaponEvolutionSO.Branch katana = null;
        foreach (var b in w0.evolution.Branches) if (b.branchId == "katana") katana = b;
        bool evolved = katana != null && await wm.EvolveCurrentWeaponAsync(katana, ct);
        var w1 = wm.Weapon0Data;
        Check(sb, $"진화 성공 — {w1?.displayName} · 증강 {w1?.augments}", evolved && w1 != null);
        if (!evolved || w1 == null) return;
        int phantoms = 0, katanaOwn = 0;
        foreach (var (id, _) in w1.AugmentList())
        {
            var d = MeleeAugmentCatalog.Get(id);
            if (MeleeAugmentCatalog.RunsAsPhantom(d, w1)) phantoms++;
            else if (d != null && d.Weapon == MeleeWeaponKind.Katana) katanaOwn++;
        }
        Check(sb, $"① 무형검 증강 3개 남음(환영 몫 {phantoms}) · 칼집 II → 카타나 증강 {katanaOwn}개 · 칼집 없음", phantoms == 3 && katanaOwn == 2 && !w1.augments.Contains("sheath"));
        var mist = MeleeAugmentCatalog.Get("aug_nameless_mist");
        sb.AppendLine($"   안개 칼날 진화 뒤 글: {mist?.PhantomText(2)}");

        w1.formTier = 0;
        WeaponFormRuntime.Of(p).Rebuild(); GameRunBootstrapper.Instance.Run.EffectManager.Rebuild();
        await Wait(1.5f, ct);
        var ph = p.GetComponent<NamelessPhantom>();
        var holder = GameObject.Find("NamelessPhantom");
        Check(sb, $"② 등 뒤 환영 — 컴포넌트 {ph != null} · 모델 보임 {holder != null && holder.activeInHierarchy}", ph != null && holder != null && holder.activeInHierarchy);
        if (Camera.main != null) CaptureMain(Camera.main, "Temp/ui_shots/PH_idle.png");

        int mistN = 0, formN = 0, afterN = 0;
        Application.LogCallback onLog = (msg, _, __) =>
        {
            if (!msg.StartsWith("[MeleeAugment]") || !msg.Contains("(환영)")) return;
            if (msg.Contains("안개 칼날")) mistN++;
            else if (msg.Contains("형 없는 연타")) formN++;
            else if (msg.Contains("무형일섬 잔상")) afterN++;
        };
        Application.logMessageReceived += onLog;
        try
        {
            s_home = p.transform.position;
            Vector3 fw = Flat(p.transform.forward);
            DetachAll();
            var t = await TrackAsync(s_home + fw * 2f, ct);
            if (t == null) { sb.AppendLine("표적 실패"); return; }
            HideStrays();
            // ③ E(성광베기) — 맞힌 자리 안개(환영)
            for (float q = 0f; q < 1.2f; q += 0.1f) { RefillAll(); Steer(p, t.M.transform.position); await Wait(0.1f, ct); }
            p.CooldownTracker.ResetCooldown(SkillType.E);
            p.InputBuffer.Push(Command.ESkill);
            for (float q = 0f; q < 2.5f; q += 0.1f) { RefillAll(); await Wait(0.1f, ct); }
            // ④ 평타 3초 — 콤보 마지막 타 뒤 한 번 더(환영)
            DetachAll();   // 성광베기 돌진으로 표적을 5 m 지나쳤다 — 평타 앞에 다시 세운다(1회차 형 없는 연타 0회)
            s_home = p.transform.position;
            t = await TrackAsync(s_home + Flat(p.transform.forward) * 1.8f, ct);
            if (t == null) { sb.AppendLine("평타 표적 실패"); return; }
            HideStrays();
            float b0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - b0 < 3f) { RefillAll(); Steer(p, t.M.transform.position); p.InputBuffer.Push(Command.Light); await Wait(0.05f, ct); }
            p.InputBuffer.Clear();
            for (float q = 0f; q < 1.2f; q += 0.1f) { RefillAll(); await Wait(0.1f, ct); }
            // ⑤ R(환영베기) — 0.5초 뒤 조준 방향 6 m 일섬(환영) · 그 순간 사진
            p.CooldownTracker.ResetCooldown(SkillType.R);
            Steer(p, t.M.transform.position);
            p.InputBuffer.Push(Command.RSkill);
            float r0 = Time.realtimeSinceStartup; bool shot = false;
            while (Time.realtimeSinceStartup - r0 < 3f)
            {
                RefillAll();
                if (!shot && afterN > 0) { shot = true; if (Camera.main != null) CaptureMain(Camera.main, "Temp/ui_shots/PH_swing.png"); }
                await UniTask.Yield(PlayerLoopTiming.PostLateUpdate, ct);
            }
            DetachAll();
        }
        finally { Application.logMessageReceived -= onLog; }
        Check(sb, $"③ E → 안개 칼날(환영) 맞힘 {mistN}회", mistN > 0);
        Check(sb, $"④ 콤보 끝 → 형 없는 연타(환영) {formN}회", formN > 0);
        Check(sb, $"⑤ R → 무형일섬 잔상(환영) {afterN}회 · 사진 Temp/ui_shots/PH_swing", afterN > 0);

        // ⑥ 다른 무기(진화 전 카타나 새로) — 환영이 숨는다
        if (await EquipAsync(p, Katana, ct))
        {
            await Wait(0.8f, ct);
            holder = GameObject.Find("NamelessPhantom");
            Check(sb, $"⑥ 환영 몫 없는 무기에선 숨음(보임 {holder != null && holder.activeInHierarchy})", holder == null || !holder.activeInHierarchy);
        }
    }

    /// <summary>
    /// 70 — 재련 개편(10-18 · 기획/RelicFairy_재련개편_설계_20261018.md): 임시 재련소 컨트롤러(NPC 없음)로 규칙만 잰다.
    /// 근접 재련 +3 · 증강 고르기 · 비용 5 · 10 · 15 · 띠 ×3(+12~) · 열기 카드 +1 · 반값 · 원거리 카드 3장 · 다시 뽑기 · 눈금 1 · 4 · 7 · 10 · 쉼터 무료 재련 · 무형검 +9 진화.
    /// </summary>
    private static async UniTask Reforge70Async(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        sb.AppendLine("70 — 재련 개편(임시 재련소 · NPC 없음)");
        await Wait(2f, ct);
        var run = GameRunBootstrapper.Instance.Run;
        var table = await WeaponEnhanceService.EnsureLoadedAsync();
        if (run == null || table == null) { sb.AppendLine("런 · 표 없음"); return; }
        if (!await EquipAsync(p, Nameless, ct)) { sb.AppendLine("무형검 장착 실패"); return; }
        bool rangedOk = await EquipAsync(p, Bow, ct, PlayerWeaponManager.Slot1);
        await EquipAsync(p, Nameless, ct);   // 근접을 들고 있게
        var w = p.WeaponManager.Weapon0Data;
        w.enhanceLevel = 0; w.highestEnhanceReached = 0; w.augments = ""; w.augmentSilverStreak = 0; w.augmentAfterEvolve = false; w.RecomputeEnhancedStats();
        RangedPartsState.Current = new RangedPartsState();
        MeleeAugmentDraft.ResetForRun();
        run.CrucibleRollIndex = 0;
        run.FuelBank.Add(FuelKind.EnhanceMaterial, 500);

        var go = new GameObject("ReforgeProbe");
        go.transform.position = p.transform.position + Vector3.right * 30f;
        var cr = go.AddComponent<CrucibleRoomController>();
        var evField = typeof(CrucibleRoomController).GetField("_event", BindingFlags.Instance | BindingFlags.NonPublic);
        try
        {
            cr.Initialize(run, table, new System.Random(1234), null);
            evField?.SetValue(cr, CrucibleEvent.None);
            // ① 근접 재련 3번 — +3 · +6 · +9 · 비용 5 · 10 · 15 · 증강 3장
            var costs = new List<int>(); var levels = new List<int>();
            for (int i = 0; i < 3; i++)
            {
                int fuel0 = run.FuelBank.EnhanceMaterial, c0 = cr.ReforgeCost(false);
                bool ok = cr.TryReforge(false);
                MeleeAugmentDraft.AutoChoose();
                costs.Add(fuel0 - run.FuelBank.EnhanceMaterial); levels.Add(w.enhanceLevel);
                if (!ok || c0 != costs[i]) sb.AppendLine($"   {i + 1}번째 재련 실패 또는 비용 표시 {c0} ≠ 낸 {costs[i]}");
            }
            int augN = 0; foreach (var _ in w.AugmentList()) augN++;
            // 비용 기대값은 표에서 읽는다(10-23 U3 — 5 · 10 · 15 → 10 · 20 · 40처럼 표가 바뀌어도 확인이 그대로 맞게)
            string wantCosts = $"{WeaponReforgeService.Cost(1, 1f, 1f)},{WeaponReforgeService.Cost(2, 1f, 1f)},{WeaponReforgeService.Cost(3, 1f, 1f)}";
            Check(sb, $"① 근접 재련 3번 — 단계 {string.Join(" · ", levels)} · 비용 {string.Join(" · ", costs)}(표 {wantCosts}) · 증강 {augN}장 · 이번 방문 {cr.ReforgeCountThisVisit}번",
                  string.Join(",", levels) == "3,6,9" && string.Join(",", costs) == wantCosts && augN == 3 && cr.ReforgeCountThisVisit == 3);
            Check(sb, $"② 무형검 +9에서 진화 가능 {cr.CanEvolve(PlayerWeaponManager.Slot0)}", cr.CanEvolve(PlayerWeaponManager.Slot0));

            // ③ 띠 — +12에서 다음 비용 = 15 × 3 · 옛 세이브 +13 → 다음 재련 +15
            w.enhanceLevel = 12; w.highestEnhanceReached = 12; w.evolutionStage = 1; w.RecomputeEnhancedStats();
            int band = cr.ReforgeCost(false);
            w.enhanceLevel = 13; w.highestEnhanceReached = 12;
            int next = WeaponReforgeService.NextMeleeLevel(w, table);
            int max = WeaponEnhanceService.MaxEnhance(w, table);
            int wantBand = WeaponReforgeService.Cost(cr.ReforgeCountThisVisit + 1, WeaponReforgeService.BandMult1, 1f);
            Check(sb, $"③ 띠 +12 비용 {band}(표 {wantBand} = 이번 방문 {cr.ReforgeCountThisVisit + 1}번째 × {WeaponReforgeService.BandMult1:0}) · +13 → 다음 재련 +{next} · 진화 뒤 상한 +{max}", band == wantBand && next == 15 && max == 21);
            w.enhanceLevel = 0; w.highestEnhanceReached = 0; w.evolutionStage = 0; w.augments = ""; w.RecomputeEnhancedStats();

            // ④ 화로 열기 — 증강 카드 3장 · 반값 — 비용 절반
            evField?.SetValue(cr, CrucibleEvent.Fever);
            int cardN = 0;
            Action<MeleeAugmentOffer> onOffer = o => cardN = o.Cards.Length;
            MeleeAugmentDraft.Offered += onOffer;
            try { cr.TryReforge(false); }
            finally { MeleeAugmentDraft.Offered -= onOffer; }
            MeleeAugmentDraft.AutoChoose();
            evField?.SetValue(cr, CrucibleEvent.Discount);
            int half = cr.ReforgeCost(false);
            evField?.SetValue(cr, CrucibleEvent.None);
            Check(sb, $"④ 열기 증강 카드 {cardN}장 · 반값 이번 방문 5번째 비용 {half}(15 × 0.5 → 8)", cardN == 3 && half == 8);

            // ⑤ 원거리 — 카드 3장 · 다시 뽑기 · 고르면 Lv1 또는 다음 눈금 · 4번 고른 뒤 이 런 재련 4
            if (rangedOk)
            {
                var state = RangedPartsState.Current;
                int offered = 0, firstCards = 0; bool rerollChanged = false, levelsOk = true;
                Action onR = () => offered++;
                RangedPartsDraft.OnOffered += onR;
                try
                {
                    for (int i = 0; i < 4; i++)
                    {
                        run.CrucibleRollIndex = 0;   // 비용 띠만 보려고 방문 순번은 매번 처음으로
                        if (!cr.TryReforge(true)) { sb.AppendLine($"   원거리 {i + 1}번째 재련 실패"); levelsOk = false; break; }
                        if (i == 0)
                        {
                            firstCards = RangedPartsDraft.Offered.Count;
                            var before = new HashSet<string>(); foreach (var o in RangedPartsDraft.Offered) before.Add(o.partId);
                            if (RangedPartsDraft.Reroll()) foreach (var o in RangedPartsDraft.Offered) if (!before.Contains(o.partId)) rerollChanged = true;
                        }
                        var pick = RangedPartsDraft.Offered[0];
                        RangedPartsDraft.Choose(0);
                        int now = state.LevelOf(pick.partId);
                        bool okStep = pick.isNew ? now == 1 : (now == 4 || now == 7 || now == 10);
                        if (!okStep) { levelsOk = false; sb.AppendLine($"   {pick.partId} Lv{pick.fromLevel} → Lv{now}(눈금 아님)"); }
                    }
                }
                finally { RangedPartsDraft.OnOffered -= onR; }
                state.ReforgeCount = 4; run.CrucibleRollIndex = 0;
                int rband = cr.ReforgeCost(true);
                Check(sb, $"⑤ 원거리 카드 {firstCards}장 · 다시 뽑기로 바뀜 {rerollChanged} · 고르면 Lv1 / 다음 눈금 {levelsOk} · 5번째 비용 {rband}(5 × 3)",
                      firstCards == 3 && rerollChanged && levelsOk && rband == 15);
            }
            else sb.AppendLine("   원거리 무기 장착 실패 — ⑤ 생략");

            // ⑥ 쉼터 무료 재련(지금 든 무기 = 근접) — +3 · 증강 · 재료 그대로
            int fuelBefore = run.FuelBank.EnhanceMaterial, lvBefore = w.enhanceLevel, expect = WeaponReforgeService.NextMeleeLevel(w, table);
            bool free = WeaponReforgeService.GrantFreeReforge(PlayerWeaponManager.Slot0);
            MeleeAugmentDraft.AutoChoose();
            Check(sb, $"⑥ 쉼터 무료 재련 {free} · +{lvBefore} → +{w.enhanceLevel} · 재료 {fuelBefore} → {run.FuelBank.EnhanceMaterial}",
                  free && w.enhanceLevel == expect && run.FuelBank.EnhanceMaterial == fuelBefore);
        }
        finally
        {
            UnityEngine.Object.Destroy(go);
            MeleeAugmentDraft.AutoChoose();
            RangedPartsDraft.AutoChoose();
        }
    }

    /// <summary>
    /// 71 — 증강 추가타 검기(⑯ · 10-18): 증강 · 형이 같은 검기 그림(vfx_form_windcut)을 길이 × 1.3/8로 고르게 키워 쓴다.
    /// 길이 · 반폭마다 보이는 양 90% 도달(앞 · 옆)을 판정(앞 = 길이 · 옆 = 반폭 + 몸 0.4)과 견주고 0.15초에 찍는다(Temp/ui_shots/AW_NN).
    /// </summary>
    private static bool s_wave71Fit = true;   // false = 옛 고른 배율(비교용)

    private static async UniTask AugWave71Async(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        sb.AppendLine("71 — 증강 추가타 검기(보이는 양 90% 도달 대 판정 · 0.8~1.25배 맞음 · 옆 판정 = 반폭 + 몸 0.4)");
        SkillRangeRing.PlayerGuides = true;   // 사진에 판정 띠가 보이게(게임에선 꺼짐 · 10-19 ⑮)
        await Wait(2.5f, ct);
        Directory.CreateDirectory("Temp/ui_shots");
        const string Wave = "vfx_form_windcut";
        const float PerMeter = 1.3f / 8f, Body = 0.4f;
        int ok = 0, bad = 0, shot = 0;
        foreach (var (label, len, half) in new[] {
            ("형 없는 연타(무형검 · 콤보 끝)", 2.5f, 1.0f), ("무형일섬 잔상(무형검 R 길)", 8f, 1.2f), ("무형일섬 잔상(환영 · 6 m)", 6f, 1.2f),
            ("검기(콤보 끝 6 m)", 6f, 0.9f), ("주문의 날(E · R 뒤 3 m)", 3f, 0.9f), ("천둥 같은 일격(4 m)", 4f, 0.8f),
            ("세 번째 칼 X자(2 m)", 2f, 0.7f), ("회피 베기(4 m 가정)", 4f, 0.9f), ("칼바람 풍절(형 8 m)", 8f, 0.9f) })
        {
            foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
                if (m != null) m.gameObject.SetActive(false);
            Vector3 o = p.transform.position, f = Flat(p.transform.forward);
            FormStrike.GroundStrip(o, f, len, half + Body, 0.6f);
            var before = EffectReachProbe.Snapshot();
            if (s_wave71Fit) FormStrike.PlayWave(Wave, o + Vector3.up, f, len, half + Body);   // 10-18 ⑯ 고친 뒤 — 앞 · 옆 따로
            else             FormStrike.PlayVfx(Wave, o + Vector3.up, f, PerMeter * len);
            var watch = EffectReachProbe.ObserveAsync(before, o, f, 1.2f, ct);
            await Wait(0.15f, ct);
            string name = $"AW_{++shot:00}";
            if (Camera.main != null) CaptureMain(Camera.main, $"Temp/ui_shots/{name}.png");
            var r = await watch;
            bool fine = r.Any && EffectReachProbe.Ok(r.Forward, len) && EffectReachProbe.Ok(r.Lateral, half + Body);
            if (fine) ok++; else bad++;
            sb.AppendLine($"  {name} {(fine ? "맞음" : r.Any ? "어긋남" : "이펙트 없음")} — {label} · {(s_wave71Fit ? $"앞 · 옆 따로({len / FormStrike.WaveForwardPerScale:0.00} · {(half + Body) / FormStrike.WaveLateralPerScale:0.00})" : $"배율 {PerMeter * len:0.00}")} · 앞 {r.Forward:0.0}/{len:0.#} m({EffectReachProbe.Ratio(r.Forward, len)}) · 옆 ±{r.Lateral:0.0}/{half + Body:0.0}({EffectReachProbe.Ratio(r.Lateral, half + Body)}) · 입자계 {r.Systems}({r.Names})");
            await Wait(0.8f, ct);
        }
        sb.Insert(0, $"맞음 {ok} · 어긋남 · 없음 {bad}\n");
    }

    /// <summary>64 — 정오 · 광란이 실측 내내 이어지게(가웨인 정오 999초 · 랜슬롯 광기 가득 + 광란 120초).</summary>
    private static void KeepRelicQState(GawainZenithRelic g, LancelotMadnessRelic l)
    {
        if (g != null && !g.Gauge.IsNoon && g.Gauge.CurrentPhase == ZenithGauge.ZPhase.Charging) g.Gauge.OpenNoonNow();
        if (l != null && !l.Madness.IsFrenzy) { l.Madness.SetStacks(l.Madness.MaxStacks); l.Madness.EnterFrenzy(120f); }
    }

    /// <summary>64c · 64d — Temp/relicq_override.txt의 「유물 칸 값」 줄로 RELIC_STAT_DATA 값을 실행 중에만 바꾼다(끝나면 되돌린다).</summary>
    private static void ApplyRelicOverride(StringBuilder sb, List<(RelicStatEntry e, float v)> restore)
    {
        if (!File.Exists(RelicQOverridePath)) { sb.AppendLine($"바꿀 수치 파일 없음({RelicQOverridePath})"); return; }
        var map = typeof(RelicStatDataManager).GetField("_map", Inst)?.GetValue(Managers.RelicStatData)
                  as Dictionary<string, Dictionary<int, RelicStatEntry>>;
        if (map == null) { sb.AppendLine("유물 수치 표를 못 읽었다"); return; }
        foreach (var line in File.ReadAllLines(RelicQOverridePath))
        {
            var t = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (t.Length < 3 || !int.TryParse(t[1], out int slot) || !float.TryParse(t[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v)) continue;
            if (!map.TryGetValue(t[0], out var slots) || !slots.TryGetValue(slot, out var e)) { sb.AppendLine($"  칸 없음 {t[0]} {slot}"); continue; }
            restore.Add((e, e.value));
            sb.AppendLine($"  바꾼 수치 {t[0]} {slot}: {e.value} → {v}");
            e.value = v;
        }
    }

    // ── 62 아이템 직접 피해 집계(10-16 — 추가 타격 복사가 평타 · 스킬 몫으로 잡혔다) ──
    private static async UniTask ItemDirectSourceAsync(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        sb.AppendLine("아이템 직접 피해 집계 — 추가 타격(적중 피해 복사 · 매 적중) 5초 평타");
        await Wait(3f, ct);
        var run = GameRunBootstrapper.Instance.Run;
        foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
            if (m != null && !m.IsDead) m.gameObject.SetActive(false);
        if (!await EquipAsync(p, Katana, ct)) { sb.AppendLine("카타나 장착 실패"); return; }
        var k = p.WeaponManager.Weapon0Data;
        k.enhanceLevel = 0;
        k.augments = "";
        k.formTier = 0;
        WeaponFormRuntime.Of(p).Rebuild();
        run.EffectManager.Rebuild();
        Legend("ExtraAttack", "OnHit", 2f);   // 확률 = 값 × 발동 계수(카타나 0.7) → 늘 복사
        await Wait(0.3f, ct);
        long b0 = run.RunStats.DamageOf(DamageSource.Basic), r0 = run.RunStats.DamageOf(DamageSource.Rune);
        int sw = await AugSwingAsync(p, 5f, ct);
        long b1 = run.RunStats.DamageOf(DamageSource.Basic), r1 = run.RunStats.DamageOf(DamageSource.Rune);
        run.EffectManager.Rebuild();   // 넣은 효과를 뺀다
        float basic = b1 - b0, rune = r1 - r0;
        sb.AppendLine($"   평타 진단 — {s_augSwingDiag}");
        Check(sb, $"추가 타격 복사 — 평타 몫 {basic:0} · 룬 몫 {rune:0}(휘두름 {sw} · 복사가 룬으로 · 룬 ≈ 평타)", sw >= 6 && basic > 0f && rune >= basic * 0.5f && rune <= basic * 1.3f);
    }

    // ── 61 원소의 날(규칙형) · 형 「쉬었다 친 첫 타」(슬래시보다 먼저 맞은 타도 한 휘두름 · 10-15) ──
    private static async UniTask ElementEdgeAsync(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        sb.AppendLine("원소의 날 · 형 쉬었다 친 첫 타");
        await Wait(3f, ct);
        var run = GameRunBootstrapper.Instance.Run;
        foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
            if (m != null && !m.IsDead) m.gameObject.SetActive(false);
        var placed = typeof(RunItemInventory).GetField("_placedItems", Inst)?.GetValue(run.ItemInventory) as List<RuntimeItemData>;
        if (placed == null) { sb.AppendLine("룬 판 목록을 못 읽음"); return; }
        var savedPlaced = new List<RuntimeItemData>(placed);
        try
        {
            // ① 원소의 날 III — 카타나 연타 룬(56과 같은 5개)으로 6초씩 · 룬 피해/초 · 발동 수
            if (!await EquipAsync(p, Katana, ct)) { sb.AppendLine("카타나 장착 실패"); return; }
            var k = p.WeaponManager.Weapon0Data;
            k.enhanceLevel = 0;
            k.formTier = 0;
            WeaponFormRuntime.Of(p).Rebuild();
            var b = Loadouts[0];
            placed.Clear();
            foreach (var id in b.ids)
            {
                var d = ProjectRune(id);
                if (d == null) continue;
                if (string.IsNullOrEmpty(d.element)) d.element = b.el;
                placed.Add(d);
            }
            async UniTask<(float rune, int swings)> RuneRateAsync(string augs)
            {
                k.augments = augs;
                run.EffectManager.Rebuild();
                await Wait(0.4f, ct);
                long r0 = run.RunStats.DamageOf(DamageSource.Rune);
                float t0 = Time.time;
                int sw = await AugSwingAsync(p, 6f, ct);
                float secs = Mathf.Max(0.01f, Time.time - t0);
                return ((run.RunStats.DamageOf(DamageSource.Rune) - r0) / secs, sw);
            }
            var (rune0, sw0) = await RuneRateAsync("");
            var (rune1, sw1) = await RuneRateAsync("aug_element_edge:3");
            int edge = AugLog("] 원소의 날");
            string edgeLine = s_formLog.Find(l => l.Contains("원소의 날")) ?? "-";
            sb.AppendLine($"   평타 진단 — {s_augSwingDiag}");
            Check(sb, $"① 원소의 날 III — 룬 · 2차 피해 {rune0:0} → {rune1:0}/초 · 발동 {edge}(휘두름 {sw1} · 마지막 타 ≈ 휘두름/3) · {edgeLine}",
                  rune1 > rune0 && edge >= sw1 / 3 - 2 && edge <= sw1 / 3 + 2 && edgeLine.Contains("맞힘") && !edgeLine.Contains("맞힘 0"));
            k.augments = "";
            run.EffectManager.Rebuild();
            placed.Clear();
            run.EffectManager.Rebuild();

            // ② ③ 형 — 2.4초 쉬었다 한 번 치기 × 3 → 쌓임 3(슬래시보다 먼저 맞은 첫 타도 한 휘두름)
            async UniTask<(int stacks, string trace)> RestedAsync(string path, string label)
            {
                if (!await EquipAsync(p, path, ct)) return (-1, label + " 장착 실패");
                var w = p.WeaponManager.Weapon0Data;
                w.enhanceLevel = 0;
                w.formTier = 1;
                w.formVariants = "";
                w.augments = "";
                run.EffectManager.Rebuild();
                WeaponFormRuntime.Of(p).Rebuild();
                var form = WeaponFormRuntime.Of(p)?.Current;
                if (form == null) return (-1, label + " 형 없음");
                typeof(WeaponFormBase).GetMethod("SetStacks", Inst)?.Invoke(form, new object[] { 0, WeaponFormBase.StackReason.Gain });
                s_home = p.transform.position;
                var t = await TrackAsync(p.transform.position + Flat(p.transform.forward) * 1.5f, ct);
                if (t == null) return (-1, label + " 슬라임 부르기 실패");
                var trace = new StringBuilder();
                for (int i = 0; i < 3; i++)
                {
                    p.InputBuffer.Clear();
                    await Wait(2.4f, ct);
                    RefillAll();
                    Steer(p, t.M.transform.position);
                    p.InputBuffer.Push(Command.Light);
                    await Wait(0.9f, ct);
                    trace.Append($" {form.Stacks}");
                }
                p.InputBuffer.Clear();
                int st = form.Stacks;
                DetachAll();
                w.formTier = 0;
                WeaponFormRuntime.Of(p).Rebuild();
                return (st, $"{label} 쌓임 차례:{trace}");
            }
            var (wind, windTrace) = await RestedAsync(Katana, "칼바람(카타나)");
            Check(sb, $"② {windTrace} → {wind}/3", wind == 3);
            var (weight, weightTrace) = await RestedAsync(Great, "무게(대검)");
            Check(sb, $"③ {weightTrace} → {weight}/3", weight == 3);
        }
        finally
        {
            placed.Clear();
            placed.AddRange(savedPlaced);
            run.EffectManager.Rebuild();
            DetachAll();
        }
    }

    // ── 60 같은 투자 비교(설계 §4 · 10-15 사용자 「근접 강화에도 메리트 — 활에 맞게끔」) ──
    private const int EqualInvestLevel = 20;   // 쪼갠 강화표 기대 재료 ≈ 152(활 최대 다발 파츠 ≈ 212보다 적게 — 근접에 불리한 쪽으로)

    private static async UniTask EqualInvestAsync(PlayerController p, StringBuilder sb, CancellationToken ct, bool skills = false)
    {
        if (skills) sb.AppendLine("   ★스킬 포함 — E · R을 쿨이 돌 때마다 쓴다(스킬 중엔 제자리로 끌지 않는다)");
        sb.AppendLine($"같은 투자 비교 — 활 최대 다발(파츠 재료 ≈ 212) 대 근접 +{EqualInvestLevel}(≈ 152) · 증강 = 3단계마다 왼쪽 카드(시드) · 룬 · 각인 · 서약은 56과 같다 · 단일 10초 + 무리 5마리 10초");
        sb.AppendLine("   ※ 실측은 평타만 · 표적 체력을 늘 채운다 — 처치 · 피격 · 회피 · 쉬기가 필요한 증강(학살 · 사냥감 · 처형 · 마지막 의지 · 회피 베기 · 기습)은 값이 0으로 잡힌다");
        var run    = GameRunBootstrapper.Instance.Run;
        var placed = typeof(RunItemInventory).GetField("_placedItems", Inst)?.GetValue(run.ItemInventory) as List<RuntimeItemData>;
        if (placed == null) { sb.AppendLine("룬 판 목록을 못 읽음"); return; }
        var savedPlaced = new List<RuntimeItemData>(placed);
        var parts = RangedPartsState.Current;
        var savedParts = new List<RangedPartsState.Equipped>(parts.Equipped_);
        string savedSentence = run.CovenantHandler?.Sentence?.CovenantId;
        foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
            if (m != null && !m.IsDead) m.gameObject.SetActive(false);
        DetachAll();
        await Wait(5f, ct);   // 방 입장 입력 잠금

        void Act(Vector3 aim)
        {
            RefillAll();
            if (p.IsInSkillAction) return;   // 돌진 스킬이 표적을 지나가게 — 제자리로 끌지 않는다
            Steer(p, aim);
            if (skills && p.CooldownTracker.IsReady(SkillType.E)) p.InputBuffer.Push(Command.ESkill);
            else if (skills && p.CooldownTracker.IsReady(SkillType.R)) p.InputBuffer.Push(Command.RSkill);
            else p.InputBuffer.Push(Command.Light);
        }
        async UniTask<float> MeasureAsync(List<HpTrack> ts, Vector3 aim)
        {
            float s0t = Time.time;
            while (Time.time - s0t < 2f) { RefillAll(); Steer(p, aim); p.InputBuffer.Push(Command.Light); await Wait(0.1f, ct); }
            if (skills) { p.CooldownTracker.ResetCooldown(SkillType.E); p.CooldownTracker.ResetCooldown(SkillType.R); }
            foreach (var t in ts) t.Dealt = 0f;
            float t0 = Time.time;
            while (Time.time - t0 < 10f) { Act(aim); await Wait(0.1f, ct); }
            float secs = Mathf.Max(0.01f, Time.time - t0);
            float sum = 0f;
            foreach (var t in ts) sum += t.Dealt;
            return sum / secs;
        }

        // (빌드 칸 번호 · 강화 · 증강 시드(−1 = 증강 없음))
        var cases = new (int build, int level, int seed)[]
        {
            (4, 0, -1),
            (0, EqualInvestLevel, -1), (0, EqualInvestLevel, 1), (0, EqualInvestLevel, 2),
            (1, EqualInvestLevel, -1), (1, EqualInvestLevel, 1), (1, EqualInvestLevel, 2),
        };
        var rows = new List<(string label, float single, float group)>();
        try
        {
            foreach (var (bi, level, seed) in cases)
            {
                var b = Loadouts[bi];
                Managers.UI?.CloseAllPopupUI();
                Time.timeScale = 1f;
                DetachAll();
                foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
                    if (m != null && !m.IsDead) m.gameObject.SetActive(false);
                bool ranged = b.fam == BuildFamily.Ranged;
                int slot = ranged ? PlayerWeaponManager.Slot1 : PlayerWeaponManager.Slot0;
                if (!await EquipAsync(p, b.weapon, ct, slot)) { sb.AppendLine($"장착 실패 {b.label}"); continue; }
                var w = slot == PlayerWeaponManager.Slot1 ? p.WeaponManager.Weapon1Data : p.WeaponManager.Weapon0Data;
                typeof(PlayerWeaponManager).GetMethod("ApplyServerOverrideIfAvailable", BindingFlags.Static | BindingFlags.NonPublic)
                    ?.Invoke(null, new object[] { w, Path.GetFileNameWithoutExtension(b.weapon) });
                w.formTier = 0;
                w.formVariants = "";
                w.augments = "";
                w.highestEnhanceReached = 0;
                w.augmentSilverStreak = 0;
                w.augmentAfterEvolve = false;

                placed.Clear();
                foreach (var id in b.ids)
                {
                    var d = ProjectRune(id);
                    if (d == null) continue;
                    if (string.IsNullOrEmpty(d.element)) d.element = b.el;
                    placed.Add(d);
                }
                var eq = new List<RangedPartsState.Equipped>();
                foreach (var (pid, lv) in b.parts) eq.Add(new RangedPartsState.Equipped { partId = pid, level = lv });
                parts.Restore(eq.ToArray());
                string sentence = CovenantSentence.MakeId(b.cause, CovenantTier.Gold, new List<(string, CovenantTier, ClauseLink)>
                    { ("supernova", CovenantTier.Gold, ClauseLink.Immediate), ("ember", CovenantTier.Gold, ClauseLink.Immediate) });
                run.CovenantHandler?.TryWriteSentence(sentence);
                run.EffectManager.Rebuild();
                ForceImprint(b.fam, 4);

                // 증강 — 강화가 처음 닿는 단계마다 한 화면 · 왼쪽 카드(플레이어가 고르지 않은 평균에 가깝게)
                if (seed >= 0)
                {
                    MeleeAugmentDraft.ResetForRun();
                    Action<MeleeAugmentOffer> pick = _ => MeleeAugmentDraft.Choose(0);
                    MeleeAugmentDraft.Offered += pick;
                    try
                    {
                        for (int lv = 1; lv <= level; lv++)
                        {
                            w.enhanceLevel = lv;
                            MeleeAugmentDraft.TryOfferForEnhance(w, lv, false, new System.Random(seed * 1000 + lv));
                        }
                    }
                    finally { MeleeAugmentDraft.Offered -= pick; }
                }
                w.enhanceLevel = level;
                w.RecomputeEnhancedStats();
                WeaponFormRuntime.Of(p).Rebuild();
                p.WeaponManager.RaiseEquippedWeaponRefreshed();
                run.EffectManager.Rebuild();
                ForceImprint(b.fam, 4);
                await Wait(0.5f, ct);

                string label = ranged ? b.label : $"{b.label} +{level} {(seed < 0 ? "증강 없음" : "시드 " + seed)}";
                s_home = p.transform.position;
                Vector3 fw = Flat(p.transform.forward);
                Vector3 c = p.transform.position + fw * b.range;
                var one = await TrackAsync(c, ct);
                if (one == null) { sb.AppendLine($"슬라임 부르기 실패 {label}"); continue; }
                float single = await MeasureAsync(new List<HpTrack> { one }, one.M.transform.position);
                one.M.gameObject.SetActive(false);
                DetachAll();
                var grp = new List<HpTrack>();
                var center = await TrackAsync(c, ct);
                if (center != null) grp.Add(center);
                for (int k = 0; k < 4; k++)
                {
                    float a = k * Mathf.PI * 0.5f + Mathf.PI * 0.25f;
                    var t = await TrackAsync(c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 1.8f, ct);
                    if (t != null) grp.Add(t);
                }
                float group = await MeasureAsync(grp, c);
                rows.Add((label, single, group));
                sb.AppendLine($"{label}: 단일 {single:0}/초 · 무리 {group:0}/초 · 공격력 {p.RuntimeStats.GetEffectiveAttack(ranged ? AttackStatKind.Ranged : AttackStatKind.Melee)}{(seed >= 0 ? " · 증강 " + w.augments : "")}");
                foreach (var t in grp) if (t.M != null) t.M.gameObject.SetActive(false);
                w.augments = "";
            }
        }
        finally
        {
            placed.Clear();
            placed.AddRange(savedPlaced);
            parts.Restore(savedParts);
            if (!string.IsNullOrEmpty(savedSentence)) run.CovenantHandler?.TryWriteSentence(savedSentence);
            run.EffectManager.Rebuild();
            DetachAll();
        }
        if (rows.Count < cases.Length) { Check(sb, $"일곱 칸 모두 잼 — {rows.Count}/{cases.Length}", false); return; }
        var bow = rows[0];
        string Ratio((string label, float single, float group) r) => $"단일 ×{(bow.single > 0f ? r.single / bow.single : 0f):0.00} · 무리 ×{(bow.group > 0f ? r.group / bow.group : 0f):0.00}";
        for (int i = 1; i < rows.Count; i++) sb.AppendLine($"   {rows[i].label} 대 활 최대 다발 — {Ratio(rows[i])}");
        float kS = (rows[2].single + rows[3].single) * 0.5f, kG = (rows[2].group + rows[3].group) * 0.5f;
        float gS = (rows[5].single + rows[6].single) * 0.5f, gG = (rows[5].group + rows[6].group) * 0.5f;
        float kSr = kS / Mathf.Max(1f, bow.single), kGr = kG / Mathf.Max(1f, bow.group), gSr = gS / Mathf.Max(1f, bow.single), gGr = gG / Mathf.Max(1f, bow.group);
        // 목표(10-16 사용자 「근접이 원거리보다 세야」): 근접이 단일에서 앞서고(1.1 ~ 1.4) 무리에서도 크게 밀리지 않는다(0.8 ~ 1.1). 옛 목표 단일 0.8 ~ 1.2 · 무리 0.6 ~ 1.0.
        Check(sb, $"① 카타나 +{EqualInvestLevel} · 증강(시드 평균) — 단일 ×{kSr:0.00}(목표 1.1 ~ 1.4) · 무리 ×{kGr:0.00}(목표 0.8 ~ 1.1)", kSr >= 1.1f && kSr <= 1.4f && kGr >= 0.8f && kGr <= 1.1f);
        Check(sb, $"② 대검 +{EqualInvestLevel} · 증강(시드 평균) — 단일 ×{gSr:0.00}(목표 1.1 ~ 1.4) · 무리 ×{gGr:0.00}(목표 0.8 ~ 1.1)", gSr >= 1.1f && gSr <= 1.4f && gGr >= 0.8f && gGr <= 1.1f);
        Check(sb, $"③ 증강 몫 — 카타나 단일 {rows[1].single:0} → {kS:0}/초 · 대검 {rows[4].single:0} → {gS:0}/초(증강이 더한다)", kS > rows[1].single && gS > rows[4].single);
    }

    // ── 56 룬 · 서약 다 낀 빌드 + 활 최대 다발(10-15 사용자 「DPS · 조합 오버파워 · 룬과 서약 · 분열한 활의 타격 트리거」) ──
    private static readonly (string label, string weapon, BuildFamily fam, (string id, int lv)[] parts, float range, string el, string[] ids, string cause)[] Loadouts =
    {
        ("카타나 연타", Katana,   BuildFamily.Combo,  new (string, int)[0],                    1.6f, "ELECTRIC", new[] { "item_t4_fire_single",  "item_t3_elec_static",    "item_t3_fire_spark",    "item_t2_sharp_instinct", "item_t2_trig_same" },   "streak"),
        ("대검 한 방",  Great,    BuildFamily.Crit,   new (string, int)[0],                    1.8f, "LIGHT",    new[] { "item_t4_light_single", "item_t3_light_radiance", "item_t3_light_sanctum", "item_t2_heavy_weight",   "item_t2_fierce_blade" }, "streak"),
        ("활 다발",     Bow,      BuildFamily.Ranged, new[] { ("part_split", 2) },             6f,   "GRASS",    new[] { "item_t4_grass_proj",   "item_t3_elec_arc",       "item_t3_grass_venom",   "item_t2_trig_ranged",    "item_t2_sharp_instinct" }, "aim"),
        ("석궁 저격",   Crossbow, BuildFamily.Ranged, new[] { ("part_power", 2) },             6f,   "ICE",      new[] { "item_t4_ice_single",   "item_t3_light_radiance", "item_t3_fire_ember",    "item_t2_heavy_weight",   "item_t2_aim_fury" },     "aim"),
        ("활 최대 다발", Bow,     BuildFamily.Ranged, new[] { ("part_split", 10), ("part_pierce", 7), ("part_explode", 5), ("part_homing", 4) },
                                                                                                 6f,   "FIRE",     new[] { "item_t4_fire_single",  "item_t2_trig_ranged",    "item_t2_trig_crit_stack", "item_t3_grass_venom", "item_t2_sharp_instinct" }, "aim"),
    };

    private static async UniTask LoadoutAsync(PlayerController p, StringBuilder sb, CancellationToken ct)
    {
        sb.AppendLine("룬 · 서약 다 낀 빌드 — 룬 5 · 각인 4 · 서약 「조건(연격/겨눔) → 초신성 → 잔불」 금 · 단일 10초 + 무리 5마리 10초 · 입력 0.1초 · 유물 없음");
        var run    = GameRunBootstrapper.Instance.Run;
        var placed = typeof(RunItemInventory).GetField("_placedItems", Inst)?.GetValue(run.ItemInventory) as List<RuntimeItemData>;
        if (placed == null) { sb.AppendLine("룬 판 목록을 못 읽음"); return; }
        var savedPlaced = new List<RuntimeItemData>(placed);
        var parts = RangedPartsState.Current;
        var savedParts = new List<RangedPartsState.Equipped>(parts.Equipped_);
        string savedSentence = run.CovenantHandler?.Sentence?.CovenantId;
        int hits = 0;
        Action<HitInfo> onHit = h => { if (h.Attacker == p.gameObject) hits++; };
        var rows = new List<(string label, float single, float group)>();
        foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
            if (m != null && !m.IsDead) m.gameObject.SetActive(false);
        DetachAll();
        await Wait(5f, ct);   // 방 입장 입력 잠금

        string Split(float secs, long[] s0, int cov0)
        {
            var st = run.RunStats;
            float B(DamageSource k) => (st.DamageOf(k) - s0[(int)k]) / secs;
            return $"평타 {B(DamageSource.Basic):0} · 룬 {B(DamageSource.Rune):0} · 지속 {B(DamageSource.Dot):0} · 서약 {B(DamageSource.Covenant):0} · 서약 발동 {(st.CovenantFires - cov0) / secs:0.0}/초";
        }
        async UniTask<(float dps, string split, float hps)> MeasureAsync(List<HpTrack> ts, Vector3 aim)
        {
            float s0t = Time.time;
            while (Time.time - s0t < 2f) { RefillAll(); Steer(p, aim); p.InputBuffer.Push(Command.Light); await Wait(0.1f, ct); }
            foreach (var t in ts) t.Dealt = 0f;
            hits = 0;
            var st = run.RunStats;
            var src0 = new long[RunStats.SourceCount];
            for (int k = 0; k < RunStats.SourceCount; k++) src0[k] = st.DamageOf((DamageSource)k);
            int cov0 = st.CovenantFires;
            float t0 = Time.time;
            while (Time.time - t0 < 10f) { RefillAll(); Steer(p, aim); p.InputBuffer.Push(Command.Light); await Wait(0.1f, ct); }
            float secs = Mathf.Max(0.01f, Time.time - t0);
            float sum = 0f;
            foreach (var t in ts) sum += t.Dealt;
            return (sum / secs, Split(secs, src0, cov0), hits / secs);
        }

        HitFeedbackService.OnHit += onHit;
        try
        {
            foreach (var b in Loadouts)
            {
                Managers.UI?.CloseAllPopupUI();
                Time.timeScale = 1f;
                DetachAll();
                foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
                    if (m != null && !m.IsDead) m.gameObject.SetActive(false);
                bool ranged = b.fam == BuildFamily.Ranged;
                int slot = ranged ? PlayerWeaponManager.Slot1 : PlayerWeaponManager.Slot0;
                if (!await EquipAsync(p, b.weapon, ct, slot)) { sb.AppendLine($"장착 실패 {b.label}"); continue; }
                var w = slot == PlayerWeaponManager.Slot1 ? p.WeaponManager.Weapon1Data : p.WeaponManager.Weapon0Data;
                typeof(PlayerWeaponManager).GetMethod("ApplyServerOverrideIfAvailable", BindingFlags.Static | BindingFlags.NonPublic)
                    ?.Invoke(null, new object[] { w, Path.GetFileNameWithoutExtension(b.weapon) });
                w.enhanceLevel = 0;
                w.RecomputeEnhancedStats();
                w.formTier = 0;
                w.formVariants = "";
                WeaponFormRuntime.Of(p).Rebuild();
                p.WeaponManager.RaiseEquippedWeaponRefreshed();

                placed.Clear();
                foreach (var id in b.ids)
                {
                    var d = ProjectRune(id);
                    if (d == null) continue;
                    if (string.IsNullOrEmpty(d.element)) d.element = b.el;
                    placed.Add(d);
                }
                var eq = new List<RangedPartsState.Equipped>();
                foreach (var (pid, lv) in b.parts) eq.Add(new RangedPartsState.Equipped { partId = pid, level = lv });
                parts.Restore(eq.ToArray());
                string sentence = CovenantSentence.MakeId(b.cause, CovenantTier.Gold, new List<(string, CovenantTier, ClauseLink)>
                    { ("supernova", CovenantTier.Gold, ClauseLink.Immediate), ("ember", CovenantTier.Gold, ClauseLink.Immediate) });
                bool wrote = run.CovenantHandler != null && run.CovenantHandler.TryWriteSentence(sentence);
                run.EffectManager.Rebuild();
                ForceImprint(b.fam, 4);
                await Wait(0.5f, ct);

                s_home = p.transform.position;
                Vector3 fw = Flat(p.transform.forward);
                Vector3 c = p.transform.position + fw * b.range;
                var one = await TrackAsync(c, ct);
                if (one == null) { sb.AppendLine($"슬라임 부르기 실패 {b.label}"); continue; }
                var (single, sSplit, sHps) = await MeasureAsync(new List<HpTrack> { one }, one.M.transform.position);
                one.M.gameObject.SetActive(false);
                DetachAll();
                var grp = new List<HpTrack>();
                var center = await TrackAsync(c, ct);
                if (center != null) grp.Add(center);
                for (int k = 0; k < 4; k++)
                {
                    float a = k * Mathf.PI * 0.5f + Mathf.PI * 0.25f;
                    var t = await TrackAsync(c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 1.8f, ct);
                    if (t != null) grp.Add(t);
                }
                var (group, gSplit, gHps) = await MeasureAsync(grp, c);
                rows.Add((b.label, single, group));
                sb.AppendLine($"{b.label}: 단일 {single:0}/초 · 무리 {group:0}/초(배율 {(single > 0f ? group / single : 0f):0.00}) · 적중 단일 {sHps:0.0}/초 · 무리 {gHps:0.0}/초 · 서약 {(wrote ? sentence : "못 씀")}");
                sb.AppendLine($"   단일 — {sSplit}");
                sb.AppendLine($"   무리 — {gSplit}");
                foreach (var t in grp) if (t.M != null) t.M.gameObject.SetActive(false);
            }
        }
        finally
        {
            HitFeedbackService.OnHit -= onHit;
            placed.Clear();
            placed.AddRange(savedPlaced);
            parts.Restore(savedParts);
            if (!string.IsNullOrEmpty(savedSentence)) run.CovenantHandler?.TryWriteSentence(savedSentence);
            run.EffectManager.Rebuild();
            DetachAll();
        }
        if (rows.Count < 5) { Check(sb, $"다섯 칸 모두 잼 — {rows.Count}/5", false); return; }
        float mean = 0f;
        for (int i = 0; i < 4; i++) mean += rows[i].single;
        mean /= 4f;
        var spread = new StringBuilder();
        bool within = true;
        for (int i = 0; i < 4; i++)
        {
            float k = mean > 0f ? rows[i].single / mean : 0f;
            spread.Append($"{rows[i].label} {k:0.00} · ");
            if (k < 0.75f || k > 1.25f) within = false;
        }
        Check(sb, $"① 서약까지 낀 단일 — 평균 {mean:0}/초 대비 {spread}(0.75 ~ 1.25)", within);
        float meleeGroup = Mathf.Max(rows[0].group, rows[1].group);
        Check(sb, $"② 활 최대 다발 무리 {rows[4].group:0}/초 ≤ 근접 무리 최고 {meleeGroup:0}/초 × 1.5 (다발 최대치가 근접 광역을 크게 넘지 않는다)", rows[4].group <= meleeGroup * 1.5f);
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
