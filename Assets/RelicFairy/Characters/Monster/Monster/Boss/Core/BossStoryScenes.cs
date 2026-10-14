using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using RelicFairy.UI;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// 보스 시나리오 장면 — 숲의 수호자 · 화룡 · 죽음의 기사 공용(09-29 설계 「보스_시기별페이지·시나리오연출」 §4).
///   · <b>봉인 장면</b>(봉인기 · HP 0) — 발밑 봉인진 · 사방에서 솟는 사슬 넷 · 감기는 순간 금빛 폭발 · 몸이 사라질 때 빛기둥 · 멀린 한 줄.
///   · <b>각성 장면</b>(해방기 첫 전환 · 보스마다 계정 1회) — 몸에 감긴 옛 봉인 사슬이 보라로 역류하다 깨진다 · 멀린 한 줄.
///   · <b>처치 장면</b>(해방기 · 악몽 모드 · HP 0) — 보스 빛깔 충격파 · 멀린 한 줄(처음 / 이후).
///   · <b>악몽 첫 조우</b>(악몽 모드 · 보스마다 계정 1회) — 보스 한 줄.
/// 사망 판정 · 방 클리어 · 봉인/처치 기록은 지금 흐름 그대로 — 여기선 보여 주기만 한다.
/// 이펙트는 리치 목록(<see cref="LichVfx"/>)의 봉인 칸을 빌린다. 대사는 CSV 키가 먼저, 없으면 아래 폴백 줄(차트 업로드 전에도 나온다).
/// </summary>
public static class BossStoryScenes
{
    // ── Constants ──────────────────────────────────────────────
    private const float ChainWidth    = 0.9f;    // 사슬 텍스처는 가운데 약 30%만 사슬 — 리치 봉인 사슬과 같은 값
    // 봉인 장면 v2(10-03) — 지침 → 멀린의 봉인진 → 사슬 넷이 하나씩 내리꽂힘 → 주저앉음 → 빛기둥 → 봉인된 채 남는다
    private const float SealCastAt        = 0.25f;   // 머리 위 봉인진 · 멀린 한 줄
    private const float SealFirstChainAt  = 0.7f;    // 첫 사슬
    private const float SealChainGap      = 0.45f;   // 사슬 사이
    private const float SealChainDrop     = 0.22f;   // 사슬이 봉인진에서 몸까지 내리꽂히는 시간
    private const float SealCompleteDelay = 0.8f;    // 마지막 사슬 뒤 빛기둥까지
    private const float SealSkyHeight     = 4f;      // 봉인진 높이 = 몸 높이 + 이만큼
    private const float SealHoldAfter     = 2.5f;    // 빛기둥 뒤 봉인된 채 남는 시간(사용자 「2~4초 뒤 정리」)
    private const float SealDissolve      = 1.2f;    // 그 뒤 몸 · 사슬 · 봉인진이 함께 디졸브로 사라지는 시간
    private const float SealLeaveDissolve = 0.3f;    // 그 전에 방을 나가면(챕터 게이트) — 로딩 화면이 덮기 전에 치운다
    private const string SealCastLine     = "지금이야… 봉인을 내릴게!";
    private const float KillLineAt    = 0.6f;
    private const float EndShotIn     = 0.5f;    // 끝 장면: 카메라가 보스로 가는 시간
    private const float EndShotOut    = 0.5f;    // 끝 장면: 빛기둥 · 한 줄 뒤 머무는 시간
    private const float KillShotHold  = 1.4f;
    private const float UnbindHoldFirst = 1.6f;  // 해방기 재조우: 포효 · 한 줄을 보는 시간
    private const float UnbindHoldAgain = 0.7f;
    // 해방기 재조우(10-13) — 묶임 없이 · 발밑 옛 봉인 자국이 비쳤다 깨진다
    private const float ReturnTraceFirst = 0.45f;
    private const float ReturnTraceAgain = 0.2f;
    // 1페이지 끝 — 다시 봉인 → 보스가 끊는다(10-13)
    private const float ResealCastHold  = 0.35f;   // 머리 위 봉인진 · 사슬이 감긴 채 버티는 시간
    private const float ResealStrain    = 0.16f;   // 사슬 하나를 당기는 시간
    private const float ResealGap       = 0.06f;
    private const float ResealBreakHold = 0.55f;   // 봉인진이 깨진 뒤 · 멀린 한 줄
    private const string ResealCastLine = "지금이야… 다시 묶을게!";

    /// <summary>다시 봉인 → 깨뜨림 장면 길이 — 전환 패턴이 플레이어 무적 시간에 더한다(10-13).</summary>
    public const float ResealSeconds = ResealCastHold + 4 * (ResealStrain + ResealGap) + ResealBreakHold;

    private const string SeenAwaken    = "p2_";
    private const string SeenAwakenNm  = "p2nm_";   // 악몽 모드 각성은 따로 한 번 — 해방기에 이미 봤어도 악몽의 한 줄이 나온다
    private const string SeenKill      = "kill_";
    private const string SeenNightmare = "nm_";
    private const string SeenReturn    = "return_";
    // 등장 · 전환 연출(10-14 몰입 개선) — 시기마다 첫 관람 따로(해방기 · 악몽은 보스가 달라졌다)
    private const string SeenEntrance  = "entr_";
    private const string SeenPageView  = "p2view_";
    /// <summary>반복 관람 — 등장 자막을 이만큼만 보고 전투로(자막은 전투 중에도 끝까지 뜬다).</summary>
    public  const float  RepeatIntroHold      = 1.6f;
    private const float  IntroFallbackSeconds = 4.6f;   // UI_BossBark BossIntro 길이(0.15 + 4.0 + 0.45)
    // 처치 순간 「보스 멈춤」(리서치 §5-3) — 짧은 슬로모 · 보스 곡을 끊고 정적
    private const float  KillSlowScale   = 0.35f;
    private const float  KillSlowSeconds = 0.5f;
    private const float  KillMusicFade   = 0.8f;
    private const float  PageMusicFade   = 0.8f;

    /// <summary>보스 봉인진 · 봉인 완성(Hovl 문양)을 바닥에 눕히는 회전 — 프리팹 루트가 -90° 눕혀 있는데 재생 회전이 루트 회전을 덮어쓴다(10-10).</summary>
    internal static readonly Quaternion SigilFlat = Quaternion.Euler(-90f, 0f, 0f);
    // 「깨진다」 폭발(SSEP HolyRedemption)은 재질이 빨강 · 주황 HDR(15.8, 3.5, 1.0)이라 역류 보라를 곱해도 진한 분홍 · 자홍 구체가 됐다
    // (10-01 f5 악몽 모드 시뮬) — 빨강을 거의 지워 라벤더 보라로.
    private static readonly Color ReversedBurstTint = new Color(0.05f, 0.1f, 1f, 0.6f);
    private const float AwakenShotYaw = 35f;   // 각성 카메라 — 플레이어 쪽에서 비껴 선다(플레이어가 보스를 가리지 않게)

    private static readonly HashSet<MonsterBase> s_leaving = new();   // 방을 나가려 해 바로 치울 봉인 보스(FlushSealed)

    // 시기별 칭호(10-14 초안 · 92 용어집 검토 반영 · 사용자 확정 전 · 대사 표에 넣으면 CDN 업로드 전엔 안 나와 코드 기본값) — 이름은 체력바 이름 그대로, 칭호가 보스의 지금 상태를 알린다(봉인기 · 해방기 · 악몽)
    private static readonly Dictionary<string, (string seal, string free, string dream)> Titles = new()
    {
        { StoryProgress.ForestGuardian, ("황혼에 뒤틀린 고목", "다시 숨 쉬는 뿌리",  "끝나지 않는 꿈의 숲") },
        { StoryProgress.Dragon,         ("하늘을 태우는 불",   "사슬을 녹인 불꽃",   "꺼지지 않는 악몽의 불") },
        { StoryProgress.DeathKnight,    ("왕을 등진 검",       "사슬 없는 반역의 검", "부서진 맹세") },
        { StoryProgress.Lich,           ("대제단의 주인",     "봉인을 거꾸로 쓴 자", "잠들지 않는 주문") },
    };

    // ── 보스별 장면 표(§4-1 ~ §4-3) ──────────────────────────
    private sealed class Spec
    {
        public string Id;
        public int    Chapter;
        public Color  SealColor;     // 봉인 빛깔 — 리치 봉인 사슬(BoundChains)의 세 빛깔과 같다
        public Color  KillColor;     // 해방 페이지 빛깔
        public float  BodyHeight;    // 사슬이 감길 높이(뼈를 못 찾았을 때)
        public string[] ChainBones;  // 사슬 넷이 감길 뼈(차례로: 오른쪽 앞 · 오른쪽 뒤 · 왼쪽 뒤 · 왼쪽 앞 고리) — 몸을 따라간다(10-10)
        public float  ChainRadius;   // 사슬이 솟는 네 곳까지 거리
        public float  SigilScale;    // 봉인진 칸(반경 10 m) 배율
        public float  WrapScale;     // 몸에 감기는 고리 · 폭발 배율
        public float  ShotBack;      // 끝 장면 카메라 — 보스에서 플레이어 쪽으로 이만큼
        public float  ShotHeight;
        public DialogueSpeaker Speaker;
        public string SealLine;
        public string NightmareAwakenLine;   // 악몽 모드 각성(엔딩 뒤 — 봉인은 이미 오래전에 무너졌다)
        public string KillLineFirst;
        public string KillLineAgain;
        public string NightmareLine;
        public string NightmareTraitLine;   // 악몽 특성 소개(멀린, 악몽 첫 조우 — 레벨디자인 설계서 §6)
        public string UnboundRoar;   // 해방기 등장 — 보스 포효
        public string ReturnLine;    // 해방기 재조우 — 멀린 「이번엔 다르다」(첫 관람, 10-13)
        public string ResealFailLine;   // 1페이지 끝 — 다시 건 봉인이 안 통한다(멀린, 10-13)
        // 애니 피드백(10-03) — 애니메이터에 없는 상태는 건너뛴다
        public string   ExhaustAnim;    // 봉인: 지친 자세
        public string[] FlinchAnims;    // 봉인: 사슬이 꽂힐 때마다 움찔(차례로)
        public string   CollapseAnim;   // 봉인: 다 묶여 주저앉음
        public string   BoundAnim;      // 해방: 묶인 자세(비우면 그대로)
        public string[] StruggleAnims;  // 해방: 사슬을 당길 때마다 버둥(차례로)
        public string   BreakAnim;      // 해방: 마지막 사슬을 떨쳐내는 동작
    }

    /// <summary>묶임(<see cref="BossBinding"/>)이 쓰는 보스별 값.</summary>
    public readonly struct BindingLook
    {
        public readonly int    Chapter;
        public readonly Color  SealColor;
        public readonly float  BodyHeight;
        public readonly float  ChainRadius;
        public readonly float  SigilScale;
        public readonly float  WrapScale;
        public readonly string[] ChainBones;

        public BindingLook(int chapter, Color sealColor, float bodyHeight, float chainRadius, float sigilScale, float wrapScale,
                           string[] chainBones)
        {
            Chapter = chapter; SealColor = sealColor; BodyHeight = bodyHeight; ChainRadius = chainRadius;
            SigilScale = sigilScale; WrapScale = wrapScale; ChainBones = chainBones;
        }
    }

    private static readonly Spec[] Specs =
    {
        new Spec
        {
            Id = StoryProgress.ForestGuardian, Chapter = 1,
            SealColor = new Color(0.75f, 0.85f, 0.35f), KillColor = new Color(1f, 0.55f, 0.45f),
            BodyHeight = 3f, ChainRadius = 6.5f, SigilScale = 0.55f, WrapScale = 2.2f, ShotBack = 10f, ShotHeight = 4.5f,
            ChainBones = new[] { "TreantRForearm", "TreantSpine3", "TreantNeck", "TreantLForearm" },   // 팔 · 몸통
            Speaker       = DialogueSpeaker.ForestGuardian,
            SealLine      = "뿌리까지 묶었어. …잠들어, 숲의 수호자.",
            NightmareAwakenLine = "꿈속에서도… 뿌리가 다시 일어나.",
            KillLineFirst = "이번엔 봉인이 아니야. …편히 쉬어.",
            KillLineAgain = "숲이 조용해졌어.",
            NightmareLine = "꿈속의 숲은… 끝나지 않는다.",
            NightmareTraitLine = "돌진이 지나간 자리에 가시가 남아 — 되건너지 마!",
            UnboundRoar   = "…뿌리가 다시 숨을 쉰다.",
            ReturnLine    = "봉인은 떨어질 때 다 끊어졌어. 이번엔 처음부터 전부야.",
            ResealFailLine = "안 묶여… 봉인이 뿌리째 뽑혔어!",
            ExhaustAnim   = "Groggy",
            FlinchAnims   = new[] { "GetHit_Left", "GetHit_Right", "GetHit_Front", "GetHit_Heavy" },
            CollapseAnim  = "Die",
            BoundAnim     = "Groggy",
            StruggleAnims = new[] { "GetHit_Left", "GetHit_Right", "GetHit_Back", "GetHit_Front" },
            BreakAnim     = "MagicAttackS",
        },
        new Spec
        {
            Id = StoryProgress.Dragon, Chapter = 2,
            SealColor = new Color(1f, 0.6f, 0.2f), KillColor = new Color(0.55f, 0.3f, 0.8f),
            BodyHeight = 3f, ChainRadius = 8f, SigilScale = 0.75f, WrapScale = 2.8f, ShotBack = 16f, ShotHeight = 7f,
            ChainBones = new[] { "Wing R02", "Spine1", "Neck2", "Wing L02" },   // 날개 · 목
            Speaker       = DialogueSpeaker.Dragon,
            SealLine      = "불길이 사슬 아래 잠들었어.",
            NightmareAwakenLine = "악몽의 불이야… 한 번 더 타올라!",
            KillLineFirst = "불이 꺼졌어. 이번엔 다시 타오르지 않아.",
            KillLineAgain = "불씨 하나 남지 않았어.",
            NightmareLine = "꿈속에서도 불은 꺼지지 않는다.",
            NightmareTraitLine = "불비가 두 번 내려 — 옅은 칸이 다음이야!",
            UnboundRoar   = "드디어… 하늘이 내 것이다.",
            ReturnLine    = "사슬이 하나도 없어. 이번엔 처음부터 날아올 거야.",
            ResealFailLine = "사슬이 녹아 버려… 봉인이 안 통해!",
            ExhaustAnim   = "UGetHit Back Right",
            FlinchAnims   = new[] { "UGetHit Front Left 1", "UGetHit Front Right 1", "UGetHit Back Right", "GetHit" },
            CollapseAnim  = "USleep Start",
            BoundAnim     = "",
            StruggleAnims = new[] { "UGetHit Front Left 1", "UGetHit Front Right 1", "UGetHit Back Right", "GetHit" },
            BreakAnim     = "UAttackWindHighStart",
        },
        new Spec
        {
            Id = StoryProgress.DeathKnight, Chapter = 3,
            SealColor = new Color(0.95f, 0.9f, 0.7f), KillColor = new Color(0.62f, 0.62f, 0.68f),
            BodyHeight = 1.4f, ChainRadius = 4.5f, SigilScale = 0.4f, WrapScale = 1.3f, ShotBack = 6.5f, ShotHeight = 2.8f,
            ChainBones = new[] { "hand_r", "spine_03", "neck_01", "hand_l" },   // 검 손목 · 갑옷
            Speaker       = DialogueSpeaker.DeathKnight,
            SealLine      = "검을 내려놔, 기사여. …이제 쉬게 하자.",
            NightmareAwakenLine = "꿈속의 검도… 둘로 갈라져!",
            KillLineFirst = "…모르드레드. 이제야 네 검을 내려놓는구나.",
            KillLineAgain = "검이 멈췄어.",
            NightmareLine = "꿈속에서도… 맹세는 부서진 채다.",
            NightmareTraitLine = "기수가 서 있으면 기사가 거세져 — 기수부터 베어!",
            UnboundRoar   = "사슬 없는 검을… 받아라.",
            ReturnLine    = "이번엔 묶인 데가 없어… 검이 처음부터 진짜야.",
            ResealFailLine = "봉인째 베어 냈어… 이번엔 안 통해!",
            ExhaustAnim   = "Idle2",
            FlinchAnims   = new[] { "GetHit1", "GetHit2", "GetHit3", "GetHit1" },
            CollapseAnim  = "Die",
            BoundAnim     = "Idle2",
            StruggleAnims = new[] { "GetHit1", "GetHit2", "GetHit3", "GetHit2" },
            BreakAnim     = "Attack1",
        },
    };

    // ── Public Methods ─────────────────────────────────────────

    /// <summary>
    /// 전투 시작(등장 연출 끝) — 장면 이펙트를 미리 읽고, 악몽 모드면 보스 한 줄(처음 한 번).
    /// </summary>
    public static void NoteCombatReady(string bossId)
    {
        var spec = Find(bossId);
        if (spec == null || !IsStoryFight(spec)) return;

        LichVfx.LoadAsync().Forget();   // 봉인 · 각성 · 처치 장면이 쓸 칸 — 쓰러뜨리는 순간 읽으면 늦다
        if (!StoryProgress.IsNightmareMode || StoryProgress.IsSeen(SeenNightmare + bossId)) return;
        StoryProgress.MarkSeen(SeenNightmare + bossId);
        Say($"NM_Ch{spec.Chapter}_Boss", spec.NightmareLine, spec.Speaker, BossBarkType.Bark);
        if (!string.IsNullOrEmpty(spec.NightmareTraitLine))
            Say($"NM_Ch{spec.Chapter}_Trait", spec.NightmareTraitLine, DialogueSpeaker.Merlin, BossBarkType.MerlinNarration);
        Debug.Log($"[BossStory] 악몽 첫 조우 — {bossId}");
    }

    /// <summary>
    /// HP 0 — 봉인기면 봉인 장면, 해방기부터는 처치 장면. 보스 사망 상태가 <c>base.Enter</c> 뒤에 부른다.
    /// 봉인기 보스는 봉인된 채 잠시 그 자리에 남았다가 디졸브로 사라진다(10-03) — 몸 치우기는 봉인 장면이 맡는다.
    /// </summary>
    public static void PlayEnd(MonsterContext ctx, string bossId)
    {
        var spec = Find(bossId);
        if (ctx?.Monster == null || spec == null || !IsStoryFight(spec)) return;

        bool seal = !StoryProgress.IsLiberated;
        Debug.Log($"[BossStory] {(seal ? "봉인" : "처치")} 장면 — {bossId} (시기 {StoryProgress.Era})");
        // 처치 순간 「보스 멈춤」 — 짧은 슬로모 · 보스 곡을 끊고 정적. 끝 장면이 끝나면 이 챕터 길 곡(10-14 · 리서치 §5-3)
        LichCinematics.SlowMo(KillSlowScale, KillSlowSeconds);
        Managers.Sound?.FadeOutBgmAsync(KillMusicFade).Forget();
        Debug.Log($"[BossStory] 음악 — 처치 · 보스 곡을 끊는다({bossId})");
        // 앞 말풍선(2페이지 간판 등)을 걷는다 — 장면 대사가 그 뒤에 줄 섰다가 끝 장면 걷기에 같이 버려졌다(10-01 f5: 숲 처치 대사 0회).
        UI_BossBark.Dismiss(0f);
        if (seal)
        {
            DieState.SceneDespawn.Add(ctx.Monster);   // 사망 지연(despawnDelay)보다 먼저 — 같은 프레임에 넘겨받는다
            SealSceneAsync(ctx, spec).Forget();
        }
        else KillSceneAsync(ctx, spec).Forget();
    }

    /// <summary>
    /// 방을 나가기 직전(챕터 게이트) — 봉인된 채 남은 보스를 짧은 디졸브로 바로 치운다(10-03 사용자: 「방을 먼저 나갈 수도 있으니 나가기 전에 치워야」).
    /// 다른 출구(층계 회랑 등)를 만들면 거기서도 부른다.
    /// </summary>
    public static void FlushSealed()
    {
        foreach (var m in DieState.SceneDespawn)
            if (m != null) s_leaving.Add(m);
        if (s_leaving.Count > 0) Debug.Log($"[BossStory] 방을 나간다 — 봉인된 보스 {s_leaving.Count}을 바로 치운다");
    }

    /// <summary>
    /// 각성 장면 카메라 — 끝 장면과 같은 거리 · 높이(보스 몸 전체가 화면 가운데)로, 플레이어 쪽에서 <see cref="AwakenShotYaw"/>° 비껴 선다.
    /// 10-01 f5 전주기 시뮬: 전환 로우 앵글(보스 4.5 m 앞 0.6 m 높이)은 큰 보스를 화면 끝에 잘랐고(숲), 플레이어 등 뒤라 플레이어가 앞을 가렸다(화룡).
    /// </summary>
    public static (Vector3 pos, Vector3 look) AwakenShot(string bossId, Vector3 bossPos, Vector3 towardPlayer)
    {
        var spec = Find(bossId);
        float back   = spec != null ? spec.ShotBack   : 10f;
        float height = spec != null ? spec.ShotHeight : 4.5f;
        float bodyH  = spec != null ? spec.BodyHeight : 2f;
        Vector3 dir  = Quaternion.Euler(0f, AwakenShotYaw, 0f) * towardPlayer;
        Vector3 look = bossPos + Vector3.up * bodyH;
        return BossPageTransitionState.ClearShot(look, bossPos + dir * back + Vector3.up * height, look);
    }

    // ── 등장 · 전환 연출 공용(10-14 몰입 개선 — 기획/RelicFairy_보스연출_몰입리서치_20261014.md) ──────

    /// <summary>테스트 · 시뮬 — 반복 관람으로 본다(테스트 오버라이드는 기록을 저장하지 않아 늘 첫 관람이었다).</summary>
    public static bool DebugForceRepeat;

    /// <summary>이 보스의 이번 시기 등장을 처음 보는가(계정 · 시기마다) — 이야기 전투가 아니면(인트로 · 테스트 씬) 늘 처음(전체 연출).</summary>
    public static bool IsFirstEntrance(string bossId) => IsFirstView(SeenEntrance, bossId);

    /// <summary>이번 시기 2페이지 전환을 처음 보는가 — 이야기 전투가 아니면 null(부르는 쪽이 세션 기준으로).</summary>
    public static bool? IsFirstPageView(string bossId)
        => IsStoryFightId(bossId) ? IsFirstView(SeenPageView, bossId) : (bool?)null;

    public static void MarkPageViewSeen(string bossId)
    {
        if (IsStoryFightId(bossId)) StoryProgress.MarkSeen(ViewKey(SeenPageView, bossId));
    }

    /// <summary>
    /// 등장 시작 — 레터박스 · 전투 HUD를 걷는다(전환 · 끝 장면과 같은 문법 · 리서치 §5-6). 이야기 전투만(인트로는 그대로) —
    /// true면 끝에 반드시 <see cref="EndEntrance"/>(끊겨도 — 레터박스가 남지 않게).
    /// </summary>
    public static bool BeginEntrance(string bossId, CancellationToken ct)
    {
        if (!IsStoryFightId(bossId)) return false;
        LichCinematics.EndSceneHud(true);
        LichCinematics.LetterboxInAsync(ct).Forget();
        Debug.Log($"[BossStory] 등장 — {bossId} (시기 {StoryProgress.Era} · {(IsFirstEntrance(bossId) ? "첫 관람" : "반복")})");
        return true;
    }

    /// <summary>등장 끝 — 레터박스 · HUD를 되돌린다. 끝까지 봤으면(<paramref name="completed"/>) 이번 시기 첫 관람을 기록.</summary>
    public static void EndEntrance(string bossId, bool completed)
    {
        LichCinematics.LetterboxOutAsync(CancellationToken.None).Forget();
        LichCinematics.EndSceneHud(false);
        if (completed) StoryProgress.MarkSeen(ViewKey(SeenEntrance, bossId));
    }

    /// <summary>
    /// 등장 자막(방문 변형 한 줄) + 이름 · 칭호. 이야기 전투면 방 입장 내레이션이 줄에 남아 있어도 걷고 바로 띄우고(그 뒤에 줄 서 등장이
    /// 늘어질 수 있었다), 반복 관람은 <see cref="RepeatIntroHold"/>초만 보고 넘어간다. 인트로 · 테스트 씬은 예전 그대로(끝까지 기다림).
    /// </summary>
    public static async UniTask IntroLineAsync(MonsterBase boss, string bossId, string line, CancellationToken ct)
    {
        bool story = IsStoryFightId(bossId);
        bool first = !story || IsFirstEntrance(bossId);
        if (story) ShowTitle(bossId, boss != null ? boss.BossName : null);
        if (UI_BossBark.Instance == null) { await Delay(first ? IntroFallbackSeconds : RepeatIntroHold, ct); return; }
        if (story) UI_BossBark.Dismiss(0f);
        if (first) { await UI_BossBark.ShowAndWaitAsync(line, BossBarkType.BossIntro); return; }
        UI_BossBark.Show(line, BossBarkType.BossIntro);
        await Delay(RepeatIntroHold, ct);
    }

    /// <summary>이번 시기 칭호 — 없으면 null.</summary>
    public static string TitleFor(string bossId)
    {
        if (bossId == null || !Titles.TryGetValue(bossId, out var t)) return null;
        return StoryProgress.IsNightmareMode ? t.dream : StoryProgress.IsLiberated ? t.free : t.seal;
    }

    /// <summary>2페이지 전환 — 보스 곡을 끊는다(정적 속에 다시 봉인 · 깨뜨림 · 리서치 §5-5).</summary>
    public static void PageMusicOut(string bossId)
    {
        var spec = Find(bossId);
        if (spec == null || !IsStoryFight(spec)) return;
        Managers.Sound?.FadeOutBgmAsync(PageMusicFade).Forget();
        Debug.Log($"[BossStory] 음악 — 2페이지 전환 · 보스 곡을 끊는다({bossId})");
    }

    /// <summary>2페이지 포효 — 보스 곡을 처음부터 다시(달라진 보스).</summary>
    public static void PageMusicIn(string bossId)
    {
        var spec = Find(bossId);
        if (spec == null || !IsStoryFight(spec)) return;
        Managers.Sound?.CrossfadeBgmAsync($"Ch{spec.Chapter}_Boss", 0.05f).Forget();
        Debug.Log($"[BossStory] 음악 — 2페이지 포효 · 보스 곡을 처음부터({bossId})");
    }

    /// <summary>이 보스가 지금 런의 이야기 전투 보스인가 — 묶임은 이야기 전투에만 감는다.</summary>
    public static bool IsStoryBoss(string bossId)
    {
        var spec = Find(bossId);
        return spec != null && IsStoryFight(spec);
    }

    /// <summary>묶임 표현 값(보스 표).</summary>
    public static bool TryGetLook(string bossId, out BindingLook look)
    {
        var spec = Find(bossId);
        look = spec == null ? default
            : new BindingLook(spec.Chapter, spec.SealColor, spec.BodyHeight, spec.ChainRadius, spec.SigilScale, spec.WrapScale,
                              spec.ChainBones);
        return spec != null;
    }

    /// <summary>
    /// 사슬이 감길 뼈 — 보이는 메시를 움직이는 뼈(스킨 렌더러의 bones)에서 이름으로 찾는다(기사는 뼈대가 두 벌). 못 찾은 칸은 null(10-10).
    /// 장면 · 묶임을 만들 때 한 번만 부른다.
    /// </summary>
    internal static Transform[] ResolveChainBones(Transform root, string[] names, int count)
    {
        var result = new Transform[count];
        if (root == null || names == null || names.Length == 0) return result;
        var renderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (int i = 0; i < count; i++)
        {
            string want = names[i % names.Length];
            foreach (var r in renderers)
            {
                foreach (var b in r.bones)
                    if (b != null && b.name == want) { result[i] = b; break; }
                if (result[i] != null) break;
            }
            if (result[i] == null) Debug.LogWarning($"[BossStory] 사슬 뼈 없음 「{want}」 — 몸 높이로({root.name})");
        }
        return result;
    }

    /// <summary>뼈들의 가운데(하나도 없으면 루트 + 몸 높이) — 몸에서 터지는 섬광 자리.</summary>
    internal static Vector3 BodyPoint(Transform[] bones, Transform root, Vector3 lift)
    {
        Vector3 sum = Vector3.zero;
        int n = 0;
        if (bones != null)
            foreach (var b in bones)
                if (b != null) { sum += b.position; n++; }
        return n > 0 ? sum / n : root.position + lift;
    }


    /// <summary>
    /// 해방기 재조우(10-13 사용자 — 「봉인 상태에서 시작은 레거시 · 다시 만나면 이번엔 다르다」). 봉인은 리치에게 떨어질 때 이미 깨졌다 —
    /// 묶임 없이 처음부터 풀린 모습으로 나와, 발밑에 남은 옛 봉인 자국이 보스 빛깔로 잠깐 비쳤다 깨져 흩어지며 포효 · 첫 관람이면 멀린 한 줄.
    /// 등장 시퀀스가 카메라를 보스에 둔 채 플레이어 카메라로 돌아가기 직전에 await한다(입력 잠금 · 전투 시작 전).
    /// </summary>
    public static async UniTask ReturnAsync(MonsterBase boss, string bossId, CancellationToken ct)
    {
        var spec = Find(bossId);
        if (boss == null || spec == null || !IsStoryFight(spec) || !StoryProgress.IsLiberated) return;
        bool first = !StoryProgress.IsSeen(SeenReturn + bossId);
        Debug.Log($"[BossStory] 해방기 재조우 — 묶임 없이 · 옛 봉인 자국이 깨진다 · {bossId} (첫 관람 {first})");
        GameObject trace = null;
        try
        {
            await LichVfx.LoadAsync();
            var     anim  = boss.GetComponentInChildren<Animator>();
            Vector3 floor = boss.transform.position;
            trace = LichVfx.PlayLoopTinted(LichVfxSlot.BossSealSigil, floor + Vector3.up * 0.05f, SigilFlat, spec.SigilScale * 0.6f, spec.SealColor);
            await Delay(first ? ReturnTraceFirst : ReturnTraceAgain, ct);
            LichVfx.Stop(ref trace, 0.2f);
            PlayState(anim, spec.BreakAnim);
            LichVfx.PlayTinted(LichVfxSlot.ChainShockwave, floor + Vector3.up * 0.2f, Quaternion.identity, spec.WrapScale * 0.6f, spec.SealColor);
            LichVfx.PlayTinted(LichVfxSlot.BossSealBurst, floor + Vector3.up * (spec.BodyHeight * 0.5f), Quaternion.identity, spec.WrapScale * 0.4f, spec.SealColor);
            BossImpactFeedback.TriggerCameraShake(0.04f, 0.12f);
            Say($"Unbound_Roar_Ch{spec.Chapter}", spec.UnboundRoar, spec.Speaker, BossBarkType.Bark);
            if (first)
            {
                StoryProgress.MarkSeen(SeenReturn + bossId);
                Say($"Return_Ch{spec.Chapter}", spec.ReturnLine, DialogueSpeaker.Merlin, BossBarkType.MerlinNarration);
            }
            await Delay(first ? UnbindHoldFirst : UnbindHoldAgain, ct);
        }
        catch (OperationCanceledException) { }
        finally { LichVfx.Stop(ref trace); }
    }

    /// <summary>
    /// 1페이지 끝 · 해방기 첫 전환(10-13 사용자 — 「봉인을 푼다는 틀은 안 맞다 · 다시 봉인하려는데 풀어 버린다 · 봉인이 안 통한다」):
    /// ① 멀린이 다시 묶는다 — 머리 위 봉인진 · 사슬 넷이 감긴다 ② 보스가 하나씩 당겨 끊는다 ③ 떨쳐내며 봉인진을 깬다(깨진 봉인이 보라로 역류 —
    /// 그래서 2페이지가 더 세다) · 멀린 「안 통해」. 전환 패턴이 휘청 · 카메라 뒤에 await한다(<see cref="ResealSeconds"/>). 끝까지 보면 봤다고 남긴다.
    /// </summary>
    public static async UniTask ResealFailAsync(MonsterBase boss, string bossId, CancellationToken ct)
    {
        var spec = Find(bossId);
        if (spec == null || boss == null) return;
        await LichVfx.LoadAsync();
        Debug.Log($"[BossStory] 다시 봉인 → 깨뜨림 — {bossId}");
        var     anim = boss.GetComponentInChildren<Animator>();
        Vector3 top  = boss.transform.position + Vector3.up * (spec.BodyHeight + SealSkyHeight);
        GameObject  sky     = null;
        BossBinding binding = null;
        try
        {
            // ① 다시 묶는다
            sky = LichVfx.PlayLoopTinted(LichVfxSlot.BossSealSigil, top, SigilFlat, spec.SigilScale, spec.SealColor);
            // 끊고 띄운다 — 1페이지를 빨리 깨면 재조우 멀린 줄 뒤에 줄 서 있다가 「안 통해」에 함께 지워졌다(10-13 전주기 시뮬 4회차)
            SayNow($"Reseal_Cast_Ch{spec.Chapter}", ResealCastLine, DialogueSpeaker.Merlin, BossBarkType.MerlinNarration);
            binding = BossBinding.Attach(boss, bossId);
            if (binding != null) await binding.ReadyAsync();
            PlayState(anim, spec.BoundAnim);
            await Delay(ResealCastHold, ct);

            // ② 하나씩 당겨 끊는다
            if (binding != null)
                for (int i = 0; i < binding.Count; i++)
                {
                    PlayState(anim, Pick(spec.StruggleAnims, i));
                    binding.Strain(i, ResealStrain);
                    await Delay(ResealStrain, ct);
                    binding.Snap(i);
                    BossImpactFeedback.TriggerCameraShake(0.04f, 0.1f);
                    await Delay(ResealGap, ct);
                }

            // ③ 떨쳐내며 봉인진을 깬다 — 깨진 봉인이 보라로 역류
            PlayState(anim, spec.BreakAnim);
            binding?.BreakFree();
            LichVfx.Stop(ref sky);
            LichVfx.PlayTinted(LichVfxSlot.BossSealBurst, top, Quaternion.identity, spec.WrapScale * 0.5f, ReversedBurstTint);
            LichCinematics.Chroma(0.4f, 0.3f);
            // 깨는 순간 「다시 묶을게!」를 끊고 「안 통해」로 바꾼다 — 줄 서면 깨진 뒤에도 앞 줄이 떠 있었고,
            // 걷기(Dismiss)는 앞 줄 페이드아웃(0.45초)만큼 늦었다(10-13 실측 두 판)
            if (StoryProgress.IsNightmareMode)
                SayNow($"NM_Awaken_Ch{spec.Chapter}", spec.NightmareAwakenLine, DialogueSpeaker.Merlin, BossBarkType.MerlinNarration);
            else
                SayNow($"Reseal_Fail_Ch{spec.Chapter}", spec.ResealFailLine, DialogueSpeaker.Merlin, BossBarkType.MerlinNarration);
            await Delay(ResealBreakHold, ct);
        }
        finally
        {
            LichVfx.Stop(ref sky);
            if (binding != null) binding.Release(false);
        }
        StoryProgress.MarkSeen(AwakenSeenKey(bossId));
    }

    /// <summary>해방기 첫 전환인가(보스마다 계정 1회) — 전환 패턴이 각성 장면을 넣을지 정한다.</summary>
    public static bool IsAwakenDue(string bossId)
    {
        var spec = Find(bossId);
        return spec != null && IsStoryFight(spec) && StoryProgress.IsLiberated && !StoryProgress.IsSeen(AwakenSeenKey(bossId));
    }

    // ── Private Methods ────────────────────────────────────────

    private static string AwakenSeenKey(string bossId) => (StoryProgress.IsNightmareMode ? SeenAwakenNm : SeenAwaken) + bossId;

    private static Spec Find(string bossId)
    {
        foreach (var s in Specs)
            if (s.Id == bossId) return s;
        return null;
    }

    /// <summary>이야기 전투인가 — 그 챕터의 보스를 런에서 만났다. 인트로 연출 액터 · 다른 챕터에 옮겨 둔 보스는 빼고.</summary>
    private static bool IsStoryFight(Spec spec) => IsStoryFightId(spec.Id);

    private static bool IsStoryFightId(string bossId)
    {
        if (string.IsNullOrEmpty(bossId) || IntroBootstrapper.Instance != null) return false;
        var run = GameRunBootstrapper.Instance?.Run;
        return run != null && StoryProgress.BossIdForChapter(run.CurrentChapter) == bossId;
    }

    private static bool IsFirstView(string prefix, string bossId)
    {
        if (!IsStoryFightId(bossId)) return true;
        if (DebugForceRepeat) return false;
        return !StoryProgress.IsSeen(ViewKey(prefix, bossId));
    }

    private static string ViewKey(string prefix, string bossId) => $"{prefix}{StoryProgress.Era}_{bossId}";

    /// <summary>이름 · 칭호 카드(92 <c>BossTitleCard</c> · 화면 아래 3분의 1 · 등장 자막 아래) — 등장 클로즈업 순간.</summary>
    private static void ShowTitle(string bossId, string displayName)
    {
        string title = TitleFor(bossId);
        if (string.IsNullOrEmpty(displayName) && string.IsNullOrEmpty(title)) return;
        BossTitleCard.Show(displayName, title);
        Debug.Log($"[BossStory] 칭호 — {displayName} · {title}");
    }

    /// <summary>
    /// 봉인 장면(10-03 v2) — ① 지쳐 버틴다 ② 멀린의 마법: 머리 위 봉인진 ③ 사슬 넷이 하나씩 내리꽂히고 그때마다 움찔
    /// ④ 다 묶여 주저앉음 · 발밑 봉인진 · 멀린 한 줄 ⑤ 빛기둥 → 봉인된 채 잠시 남는다(그사이 출구 · 보상은 평소대로)
    /// ⑥ 몸 · 사슬 · 봉인진이 함께 디졸브로 사라진다. 장면이 어디서 끊겨도 finally가 몸을 치운다(봉인된 보스가 남지 않게).
    /// </summary>
    private static async UniTaskVoid SealSceneAsync(MonsterContext ctx, Spec spec)
    {
        var monster = ctx.Monster;
        var body    = ctx.Transform;
        var anim    = ctx.Animator;
        // 파괴 + 풀 반환(방 정리 등) 어느 쪽이든 장면을 끊는다 — 사망 상태의 디졸브와 같은 묶음(이중 반환 차단)
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(monster.destroyCancellationToken, monster.ActivationToken);
        var ct = cts.Token;
        var chains = new LichChainLine[4];
        GameObject sky = null, floor = null;
        float t0     = Time.unscaledTime;
        float doneAt = SealFirstChainAt + chains.Length * SealChainGap + SealCompleteDelay;
        try
        {
            BossBinding.Of(monster)?.Release(true);   // 싸우는 내내 감겨 있던 묶임 사슬 → 멀린의 봉인 사슬이 새로 내려온다
            EndShotAsync(ctx, spec, doneAt + EndShotOut, monster.GetCancellationTokenOnDestroy()).Forget();   // 카메라 · 입력 복귀는 풀 반환과 무관하게 끝까지
            await LichVfx.LoadAsync();
            Vector3 feet = body.position;
            Vector3 lift = Vector3.up * spec.BodyHeight;
            var bones = ResolveChainBones(body, spec.ChainBones, chains.Length);   // 사슬 끝 = 그 뼈(움찔 · 주저앉음을 따라간다, 10-10)
            PlayState(anim, spec.ExhaustAnim);

            // ② 멀린의 마법 — 머리 위에 봉인진이 펼쳐진다
            await DelayUntil(t0 + SealCastAt, ct);
            Vector3 skyAt = feet + Vector3.up * (spec.BodyHeight + SealSkyHeight);
            sky = LichVfx.PlayLoopTinted(LichVfxSlot.BossSealSigil, skyAt, Quaternion.Euler(180f, 0f, 0f) * SigilFlat, spec.SigilScale * 0.8f, spec.SealColor);
            Say($"Seal_Ch{spec.Chapter}_Cast", SealCastLine, DialogueSpeaker.Merlin, BossBarkType.MerlinNarration);

            // ③ 사슬이 하나씩 내리꽂힌다 — 꽂힐 때마다 금빛 섬광 · 흔들림 · 움찔
            float yaw = body.eulerAngles.y;
            for (int i = 0; i < chains.Length; i++)
            {
                await DelayUntil(t0 + SealFirstChainAt + i * SealChainGap, ct);
                Vector3 dir  = Quaternion.Euler(0f, yaw + 45f + i * 90f, 0f) * Vector3.forward;
                Vector3 from = skyAt + dir * (spec.ChainRadius * 0.6f);
                chains[i] = LichChainLine.Create(from, from + Vector3.down * 0.1f, ChainWidth, spec.SealColor);
                Transform grip    = bones[i] != null ? bones[i] : body;
                Vector3   gripOff = bones[i] != null ? Vector3.zero : lift;
                await DropChainAsync(chains[i], from, grip, gripOff, ct);
                if (chains[i] != null) { chains[i].Tension(1f); chains[i].Flash(0.3f); }
                LichVfx.PlayTinted(LichVfxSlot.BossSealBurst, grip.position + gripOff, Quaternion.identity, spec.WrapScale * 0.25f, spec.SealColor);
                BossImpactFeedback.TriggerCameraShake(0.05f, 0.12f);
                PlayState(anim, Pick(spec.FlinchAnims, i));
            }

            // ④ 조인다 — 주저앉고 발밑 봉인진 · 멀린 한 줄
            await Delay(0.2f, ct);
            foreach (var c in chains) if (c != null) { c.Tension(1f); c.Flash(0.4f); }
            PlayState(anim, spec.CollapseAnim);
            floor = LichVfx.PlayLoopTinted(LichVfxSlot.BossSealSigil, feet + Vector3.up * 0.05f, SigilFlat, spec.SigilScale, spec.SealColor);
            LichVfx.PlayTinted(LichVfxSlot.BossSealBurst, BodyPoint(bones, body, lift), Quaternion.identity, spec.WrapScale * 0.5f, spec.SealColor);
            // 바로 — 시전 줄(5초) 뒤에 줄 섰다가 끝 장면 걷기(+3.8초)에 버려져 한 번도 안 떴다(10-14 리서치 · 10-11 사진)
            SayNow($"Seal_Ch{spec.Chapter}_Scene", spec.SealLine, DialogueSpeaker.Merlin, BossBarkType.MerlinNarration);

            // ⑤ 봉인 완성 — 빛기둥. 봉인진 둘과 사슬은 남는다(사슬이 허공에 매달려 보이지 않게 머리 위 봉인진도)
            await DelayUntil(t0 + doneAt, ct);
            LichVfx.PlayTinted(LichVfxSlot.BossSealComplete, feet + Vector3.up * 0.05f, SigilFlat, spec.SigilScale, spec.SealColor);
            Debug.Log($"[BossStory] 봉인 완성 +{Time.unscaledTime - t0:0.00}초 — {SealHoldAfter:0.0}초 묶여 있다가 디졸브({spec.Id})");

            // ⑥ 잠시 묶여 있다가 몸 · 사슬 · 봉인진이 함께 디졸브로 사라진다 — 그 전에 방을 나가면(FlushSealed) 바로
            float holdEnd = Time.unscaledTime + SealHoldAfter;
            while (Time.unscaledTime < holdEnd && !s_leaving.Contains(monster))
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            float dissolve = s_leaving.Contains(monster) ? SealLeaveDissolve : SealDissolve;
            LichVfx.Stop(ref floor, dissolve);
            LichVfx.Stop(ref sky, dissolve);
            var vanish = DissolveEffect.PlayDeathDissolveAsync(monster.gameObject, dissolve,
                () => { if (monster != null) Managers.ObjectPooler.Despawn(monster.gameObject); }, ct);
            for (float t = 0f; t < dissolve; t += Time.deltaTime)   // 디졸브와 같은 시간축(게임 시간)
            {
                foreach (var c in chains)
                    if (c != null) c.SetColor(spec.SealColor * (1f - t / dissolve));   // RGB까지 — 가산 셰이더는 알파만으론 안 옅어진다
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            await vanish;
        }
        catch (OperationCanceledException) { }
        finally
        {
            foreach (var c in chains)
                if (c != null) c.Dispose();
            LichVfx.Stop(ref sky, 0.3f);
            LichVfx.Stop(ref floor, 0.3f);
            DieState.SceneDespawn.Remove(monster);
            s_leaving.Remove(monster);
            // 장면이 중간에 끊겼는데 몸이 남았으면 바로 치운다
            if (monster != null && monster.gameObject.activeInHierarchy) Managers.ObjectPooler.Despawn(monster.gameObject);
        }
    }

    /// <summary>사슬이 머리 위 봉인진에서 몸까지 내리꽂힌 뒤 몸을 따라간다.</summary>
    private static async UniTask DropChainAsync(LichChainLine line, Vector3 from, Transform body, Vector3 bodyOffset, CancellationToken ct)
    {
        if (line == null || body == null) return;
        for (float t = 0f; t < SealChainDrop; t += Time.unscaledDeltaTime)
        {
            if (line == null || body == null) return;
            float k = t / SealChainDrop;
            line.SetEnds(from, Vector3.Lerp(from, body.position + bodyOffset, k * k));   // 떨어지듯 가속
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
        }
        if (line != null && body != null) line.Follow(null, Vector3.zero, body, bodyOffset);
    }

    /// <summary>애니메이터에 있는 상태면 건넨다(없으면 건너뜀 — 보스마다 클립 이름이 다르다).</summary>
    private static void PlayState(Animator anim, string state)
    {
        if (anim == null || string.IsNullOrEmpty(state)) return;
        int hash = Animator.StringToHash(state);
        if (!anim.HasState(0, hash)) { Debug.LogWarning($"[BossStory] 애니 상태 없음 「{state}」 — 건너뜀"); return; }
        anim.speed = 1f;
        anim.CrossFade(hash, 0.12f, 0, 0f);
    }

    private static string Pick(string[] list, int i) => list == null || list.Length == 0 ? null : list[i % list.Length];

    /// <summary>처치 장면 — 해방 페이지 빛깔 충격파 · 멀린 한 줄(처음 / 이후). 카메라가 보스를 비춘다.</summary>
    private static async UniTaskVoid KillSceneAsync(MonsterContext ctx, Spec spec)
    {
        var ct = ctx.Monster.GetCancellationTokenOnDestroy();
        EndShotAsync(ctx, spec, KillLineAt + KillShotHold, ct).Forget();
        try
        {
            await LichVfx.LoadAsync();
            LichVfx.PlayTinted(LichVfxSlot.ChainShockwave, ctx.Transform.position + Vector3.up * 0.2f, Quaternion.identity, spec.WrapScale * 1.2f, spec.KillColor);
            await Delay(KillLineAt, ct);

            // 첫 처치 문구(「이번엔 봉인이 아니야」)는 해방기 첫 처치에만 — 악몽 모드는 늘 「다시」 쪽
            bool first = !StoryProgress.IsNightmareMode && !StoryProgress.IsSeen(SeenKill + spec.Id);
            if (first)
            {
                StoryProgress.MarkSeen(SeenKill + spec.Id);
                Say($"Kill_Ch{spec.Chapter}_Scene", spec.KillLineFirst, DialogueSpeaker.Merlin, BossBarkType.MerlinNarration);
            }
            else
            {
                Say($"Kill_Ch{spec.Chapter}_Scene_R", spec.KillLineAgain, DialogueSpeaker.Merlin, BossBarkType.MerlinNarration);
            }
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>
    /// 끝 장면 카메라 — 플레이어 쪽에서 쓰러진 보스를 비춘다(입력 잠금 · 레터박스 · 전투 HUD 걷기). 장면 시작부터 <paramref name="until"/>초에 돌려준다.
    /// 끝나면 HUD는 페이드로 돌아오고 장면 대사도 같이 걷는다(10-01 f5 전주기 시뮬 — HUD가 레터박스 위에 남고 대사가 포탈 화면까지 따라왔다).
    /// 09-29 캡처: 카메라가 플레이어만 따라가 보스가 화면 밖에서 쓰러지면 봉인 · 처치 장면이 통째로 안 보였다.
    /// </summary>
    private static async UniTask EndShotAsync(MonsterContext ctx, Spec spec, float until, CancellationToken ct)
    {
        var   pc     = ctx.Runtime?.CachedPlayer;
        var   player = ctx.Runtime?.PlayerTarget;
        float t0     = Time.unscaledTime;
        pc?.SetInputEnabled(false);
        pc?.SetInvincible(until + 1.5f);
        LichCinematics.TakeCamera();
        LichCinematics.EndSceneHud(true);
        LichCinematics.LetterboxUnderHudAsync(ct).Forget();
        try
        {
            Vector3 b   = ctx.Transform.position;
            Vector3 dir = player != null ? player.position - b : ctx.Transform.forward;
            dir.y = 0f;
            dir   = dir.sqrMagnitude > 0.01f ? dir.normalized : ctx.Transform.forward;
            Vector3 look = b + Vector3.up * spec.BodyHeight;
            // 몸 크기만큼 좌우 · 위도 트인 자리 — 가운데만 보면 머리 · 날개가 기둥 뒤인 자리를 골랐다(10-14 실측)
            var (pos, at) = BossPageTransitionState.ClearShot(look, b + dir * spec.ShotBack + Vector3.up * spec.ShotHeight, look, spec.BodyHeight);
            await LichCinematics.ShotAsync(pos, at, EndShotIn, ct);
            await DelayUntil(t0 + until, ct);
        }
        catch (OperationCanceledException) { }
        finally
        {
            LichCinematics.LetterboxOutAsync(CancellationToken.None).Forget();
            LichCinematics.EndSceneHud(false);
            UI_BossBark.Dismiss();
            if (player != null) LichCinematics.ReturnToPlayerAsync(player, CancellationToken.None).Forget();
            pc?.SetInputEnabled(true);
            // 정적 뒤 — 이 챕터 길 곡(드래프트 · 계단은 잔잔하게 · 계단을 지나면 다음 챕터 곡)
            Managers.Sound?.CrossfadeBgmAsync($"Ch{spec.Chapter}_Map", 0.1f).Forget();
            Debug.Log($"[BossStory] 음악 — 끝 장면 뒤 Ch{spec.Chapter}_Map");
        }
    }

    /// <summary>대사 CSV 키가 먼저, 없으면 폴백 줄.</summary>
    private static void Say(string key, string fallback, DialogueSpeaker speaker, BossBarkType type)
    {
        if (UI_BossBark.ShowDialogue(key, type)) return;
        if (!string.IsNullOrEmpty(fallback)) UI_BossBark.Show(fallback, type, speaker);
    }

    /// <summary><see cref="Say"/>와 같되 대기열과 지금 줄을 끊고 바로 띄운다(한 줄 시퀀스용).</summary>
    private static void SayNow(string key, string fallback, DialogueSpeaker speaker, BossBarkType type)
    {
        if (UI_BossBark.ShowDialogueLine(key, 0, type, interrupt: true)) return;
        if (!string.IsNullOrEmpty(fallback)) UI_BossBark.ShowLatest(fallback, type, speaker);
    }

    private static UniTask Delay(float seconds, CancellationToken ct)
        => UniTask.Delay(TimeSpan.FromSeconds(Mathf.Max(0f, seconds)), DelayType.UnscaledDeltaTime, cancellationToken: ct);

    private static UniTask DelayUntil(float unscaledTime, CancellationToken ct)
        => Delay(unscaledTime - Time.unscaledTime, ct);
}
}
