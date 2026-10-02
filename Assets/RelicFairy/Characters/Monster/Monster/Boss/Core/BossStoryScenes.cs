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
    private const float AwakenGold    = 0.55f;   // 각성: 금빛 사슬 고리
    private const float AwakenReverse = 0.6f;    // 각성: 보라 역류
    private const float AwakenBreak   = 0.35f;   // 각성: 파쇄 뒤 숨
    private const float KillLineAt    = 0.6f;
    private const float EndShotIn     = 0.5f;    // 끝 장면: 카메라가 보스로 가는 시간
    private const float EndShotOut    = 0.5f;    // 끝 장면: 빛기둥 · 한 줄 뒤 머무는 시간
    private const float KillShotHold  = 1.4f;
    private const float UnbindStrainFirst = 0.3f;   // 해방기 등장: 사슬 하나를 당기는 시간(첫 관람)
    private const float UnbindStrainAgain = 0.12f;
    private const float UnbindGapFirst    = 0.2f;   // 끊긴 뒤 다음 사슬까지
    private const float UnbindGapAgain    = 0.08f;
    private const float UnbindHoldFirst = 1.6f;  // 끊어진 뒤 포효 · 한 줄을 보는 시간
    private const float UnbindHoldAgain = 0.7f;
    private const float StreamDistance  = 6f;    // 2페이지 역류: 봉인석 자리 = 보스에서 플레이어 반대쪽으로
    private const float StreamWidth     = 1.6f;

    /// <summary>각성 장면 길이 — 전환 패턴이 플레이어 무적 시간에 더한다.</summary>
    public const float AwakenSeconds = AwakenGold + AwakenReverse + AwakenBreak;

    private const string SeenAwaken    = "p2_";
    private const string SeenAwakenNm  = "p2nm_";   // 악몽 모드 각성은 따로 한 번 — 해방기에 이미 봤어도 악몽의 한 줄이 나온다
    private const string SeenKill      = "kill_";
    private const string SeenNightmare = "nm_";
    private const string SeenUnbound   = "unbound_";

    private static readonly Color Reversed = new Color(0.62f, 0.25f, 0.95f);
    // 역류 사슬(SSEP DarkChainSwamp)은 원래 빛깔이 진한 자홍(HDR ×4)이라 깨진 재질(분홍)처럼 읽혔다(10-01 f5 전주기 시뮬) —
    // 빨강을 눌러 보라 쪽으로, 밝기도 낮춘다(입자 시작색에 곱한다).
    private static readonly Color ReversedChainTint = new Color(0.32f, 0.3f, 1f, 0.65f);
    // 「깨진다」 폭발(SSEP HolyRedemption)은 재질이 빨강 · 주황 HDR(15.8, 3.5, 1.0)이라 역류 보라를 곱해도 진한 분홍 · 자홍 구체가 됐다
    // (10-01 f5 악몽 모드 시뮬) — 빨강을 거의 지워 라벤더 보라로.
    private static readonly Color ReversedBurstTint = new Color(0.05f, 0.1f, 1f, 0.6f);
    private const float AwakenShotYaw = 35f;   // 각성 카메라 — 플레이어 쪽에서 비껴 선다(플레이어가 보스를 가리지 않게)

    private static readonly HashSet<MonsterBase> s_leaving = new();   // 방을 나가려 해 바로 치울 봉인 보스(FlushSealed)

    // ── 보스별 장면 표(§4-1 ~ §4-3) ──────────────────────────
    private sealed class Spec
    {
        public string Id;
        public int    Chapter;
        public Color  SealColor;     // 봉인 빛깔 — 리치 봉인 사슬(BoundChains)의 세 빛깔과 같다
        public Color  KillColor;     // 해방 페이지 빛깔
        public float  BodyHeight;    // 사슬이 감길 높이
        public float  ChainRadius;   // 사슬이 솟는 네 곳까지 거리
        public float  SigilScale;    // 봉인진 칸(반경 10 m) 배율
        public float  WrapScale;     // 몸에 감기는 고리 · 폭발 배율
        public float  ShotBack;      // 끝 장면 카메라 — 보스에서 플레이어 쪽으로 이만큼
        public float  ShotHeight;
        public DialogueSpeaker Speaker;
        public string SealLine;
        public string AwakenLine;
        public string NightmareAwakenLine;   // 악몽 모드 각성(엔딩 뒤 — 봉인은 이미 오래전에 무너졌다)
        public string KillLineFirst;
        public string KillLineAgain;
        public string NightmareLine;
        public string NightmareTraitLine;   // 악몽 특성 소개(멀린, 악몽 첫 조우 — 레벨디자인 설계서 §6)
        public string YankLine;      // 봉인기 — 사슬이 기술을 처음 끊었을 때(멀린)
        public string UnboundLine;   // 해방기 등장 — 사슬이 끊길 때(멀린, 첫 관람)
        public string UnboundRoar;   // 해방기 등장 — 보스 포효
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
        public readonly string YankLine;

        public BindingLook(int chapter, Color sealColor, float bodyHeight, float chainRadius, float sigilScale, float wrapScale, string yankLine)
        {
            Chapter = chapter; SealColor = sealColor; BodyHeight = bodyHeight; ChainRadius = chainRadius;
            SigilScale = sigilScale; WrapScale = wrapScale; YankLine = yankLine;
        }
    }

    private static readonly Spec[] Specs =
    {
        new Spec
        {
            Id = StoryProgress.ForestGuardian, Chapter = 1,
            SealColor = new Color(0.75f, 0.85f, 0.35f), KillColor = new Color(1f, 0.55f, 0.45f),
            BodyHeight = 3f, ChainRadius = 6.5f, SigilScale = 0.55f, WrapScale = 2.2f, ShotBack = 10f, ShotHeight = 4.5f,
            Speaker       = DialogueSpeaker.ForestGuardian,
            SealLine      = "뿌리까지 묶었어. …잠들어, 숲의 수호자.",
            AwakenLine    = "봉인석에 갇혀 있던 힘이… 거꾸로 흘러 들어가!",
            NightmareAwakenLine = "꿈속에서도… 뿌리가 다시 일어나.",
            KillLineFirst = "이번엔 봉인이 아니야. …편히 쉬어.",
            KillLineAgain = "숲이 조용해졌어.",
            NightmareLine = "꿈속의 숲은… 끝나지 않는다.",
            NightmareTraitLine = "돌진이 지나간 자리에 가시가 남아 — 되건너지 마!",
            YankLine      = "사슬이 아직 버티고 있어 — 지금이야!",
            UnboundLine   = "사슬이… 다 끊겼어. 저게 저 녀석의 원래 힘이야.",
            UnboundRoar   = "…뿌리가 다시 숨을 쉰다.",
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
            Speaker       = DialogueSpeaker.Dragon,
            SealLine      = "불길이 사슬 아래 잠들었어.",
            AwakenLine    = "봉인석의 불이… 심연이랑 섞였어!",
            NightmareAwakenLine = "악몽의 불이야… 한 번 더 타올라!",
            KillLineFirst = "불이 꺼졌어. 이번엔 다시 타오르지 않아.",
            KillLineAgain = "불씨 하나 남지 않았어.",
            NightmareLine = "꿈속에서도 불은 꺼지지 않는다.",
            NightmareTraitLine = "불비가 두 번 내려 — 옅은 칸이 다음이야!",
            YankLine      = "날개에 감긴 사슬이 끌어내렸어!",
            UnboundLine   = "날개를 묶던 사슬이 없어. 이제 제대로 날 거야!",
            UnboundRoar   = "드디어… 하늘이 내 것이다.",
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
            Speaker       = DialogueSpeaker.DeathKnight,
            SealLine      = "검을 내려놔, 기사여. …이제 쉬게 하자.",
            AwakenLine    = "봉인석이 갈라져… 검으로 흘러들어!",
            NightmareAwakenLine = "꿈속의 검도… 둘로 갈라져!",
            KillLineFirst = "…모르드레드. 이제야 네 검을 내려놓는구나.",
            KillLineAgain = "검이 멈췄어.",
            NightmareLine = "꿈속에서도… 맹세는 부서진 채다.",
            NightmareTraitLine = "기수가 서 있으면 기사가 거세져 — 기수부터 베어!",
            YankLine      = "사슬이 검을 붙잡았어!",
            UnboundLine   = "사슬 없는 모르드레드… 조심해, 검이 끝까지 와.",
            UnboundRoar   = "사슬 없는 검을… 받아라.",
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
            : new BindingLook(spec.Chapter, spec.SealColor, spec.BodyHeight, spec.ChainRadius, spec.SigilScale, spec.WrapScale, spec.YankLine);
        return spec != null;
    }

    /// <summary>봉인기 — 사슬이 기술을 처음 끊었을 때 멀린 한 줄.</summary>
    public static void SayYank(BindingLook look)
        => Say($"Bound_Yank_Ch{look.Chapter}", look.YankLine, DialogueSpeaker.Merlin, BossBarkType.MerlinNarration);

    /// <summary>
    /// 해방기 등장 — 묶인 모습으로 나타난 보스의 사슬이 달아오르다 끊어지고 포효(10-03 S2-3). 등장 시퀀스가 카메라를 보스에 둔 채
    /// 플레이어 카메라로 돌아가기 직전에 await한다(입력 잠금 · 전투 시작 전). 첫 관람 약 3초 · 이후 약 1.5초.
    /// </summary>
    public static async UniTask UnbindAsync(MonsterBase boss, string bossId, CancellationToken ct)
    {
        var spec = Find(bossId);
        if (boss == null || spec == null || !IsStoryFight(spec) || !StoryProgress.IsLiberated) return;
        var binding = BossBinding.Of(boss) ?? BossBinding.Attach(boss, bossId);
        if (binding == null) return;

        bool first = !StoryProgress.IsSeen(SeenUnbound + bossId);
        Debug.Log($"[BossStory] 해방기 등장 — 사슬이 끊어진다 · {bossId} (첫 관람 {first})");
        var   anim   = boss.GetComponentInChildren<Animator>();
        float strain = first ? UnbindStrainFirst : UnbindStrainAgain;
        float gap    = first ? UnbindGapFirst    : UnbindGapAgain;
        try
        {
            await binding.ReadyAsync();
            PlayState(anim, spec.BoundAnim);
            // 사슬을 하나씩 당겨 끊는다 — 당길 때마다 버둥 · 사슬이 팽팽히 번쩍 → 파편과 함께 끊어진다
            for (int i = 0; i < binding.Count; i++)
            {
                PlayState(anim, Pick(spec.StruggleAnims, i));
                binding.Strain(i, strain);
                await Delay(strain, ct);
                binding.Snap(i);
                BossImpactFeedback.TriggerCameraShake(0.04f, 0.1f);
                await Delay(gap, ct);
            }
            // 떨쳐낸다 — 강한 동작 · 충격파
            PlayState(anim, spec.BreakAnim);
            binding.BreakFree();
            LichCinematics.Chroma(0.3f, 0.25f);
            Say($"Unbound_Roar_Ch{spec.Chapter}", spec.UnboundRoar, spec.Speaker, BossBarkType.Bark);
            if (first)
            {
                StoryProgress.MarkSeen(SeenUnbound + bossId);
                Say($"Unbound_Ch{spec.Chapter}", spec.UnboundLine, DialogueSpeaker.Merlin, BossBarkType.MerlinNarration);
            }
            await Delay(first ? UnbindHoldFirst : UnbindHoldAgain, ct);
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>해방기 첫 전환인가(보스마다 계정 1회) — 전환 패턴이 각성 장면을 넣을지 정한다.</summary>
    public static bool IsAwakenDue(string bossId)
    {
        var spec = Find(bossId);
        return spec != null && IsStoryFight(spec) && StoryProgress.IsLiberated && !StoryProgress.IsSeen(AwakenSeenKey(bossId));
    }

    /// <summary>
    /// 각성 장면 — 몸에 감긴 옛 봉인(금빛 사슬 고리)이 보라로 역류하다 깨진다 · 멀린 한 줄. 끝까지 보면 봤다고 남긴다.
    /// 전환 패턴이 휘청 뒤, 로우 앵글과 함께 await한다(<see cref="AwakenSeconds"/>).
    /// </summary>
    public static async UniTask AwakenAsync(Transform body, string bossId, float bodyHeight, CancellationToken ct)
    {
        var spec = Find(bossId);
        if (spec == null || body == null) return;

        await LichVfx.LoadAsync();
        Debug.Log($"[BossStory] 각성 장면 — {bossId} (이펙트 목록 {(LichVfx.IsReady ? "있음" : "없음")})");
        Vector3 at = body.position + Vector3.up * bodyHeight;
        // 역류(10-03 S2-3) — 바닥 아래 봉인석 자리(보스에서 플레이어 반대쪽)가 깨지며 보라 물줄기가 몸으로 흘러든다
        Vector3 src = StreamSource(body);
        LichVfx.PlayTinted(LichVfxSlot.SealBurst, src + Vector3.up * 0.3f, Quaternion.identity, spec.WrapScale * 0.25f, ReversedBurstTint);
        var stream = LichChainLine.Create(src, src + Vector3.up * 0.1f, StreamWidth, Reversed);
        GameObject wrap = null;
        try
        {
            for (float t = 0f; t < AwakenGold; t += Time.unscaledDeltaTime)
            {
                if (stream != null) stream.SetEnds(src, Vector3.Lerp(src, at, t / AwakenGold));
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            if (stream != null) { stream.SetEnds(src, at); stream.Flash(0.3f); }

            // 역류가 몸에 감긴다
            wrap = LichVfx.PlayLoopTinted(LichVfxSlot.ChainReversed, at, Quaternion.identity, spec.WrapScale * 0.45f, ReversedChainTint);   // 리치 붕괴용이라 크다
            if (StoryProgress.IsNightmareMode)
                Say($"NM_Awaken_Ch{spec.Chapter}", spec.NightmareAwakenLine, DialogueSpeaker.Merlin, BossBarkType.MerlinNarration);
            else
                Say($"Awaken_Ch{spec.Chapter}", spec.AwakenLine, DialogueSpeaker.Merlin, BossBarkType.MerlinNarration);
            await Delay(AwakenReverse, ct);

            // 깨진다
            LichVfx.Stop(ref wrap);
            LichVfx.PlayTinted(LichVfxSlot.ChainShockwave, body.position + Vector3.up * 0.2f, Quaternion.identity, spec.WrapScale, Reversed);
            LichVfx.PlayTinted(LichVfxSlot.SealBurst, at, Quaternion.identity, spec.WrapScale * 0.3f, ReversedBurstTint);
            // 화면 섬광(UI 층)은 레터박스 띠까지 보라로 물들였다(10-01 f5) — 3D 화면만 흔드는 색수차 맥동으로
            LichCinematics.Chroma(0.4f, 0.3f);
            await Delay(AwakenBreak, ct);
        }
        finally
        {
            LichVfx.Stop(ref wrap);
            if (stream != null) stream.Dispose();
        }
        StoryProgress.MarkSeen(AwakenSeenKey(bossId));
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
    private static bool IsStoryFight(Spec spec)
    {
        if (IntroBootstrapper.Instance != null) return false;
        var run = GameRunBootstrapper.Instance?.Run;
        return run != null && StoryProgress.BossIdForChapter(run.CurrentChapter) == spec.Id;
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
            PlayState(anim, spec.ExhaustAnim);

            // ② 멀린의 마법 — 머리 위에 봉인진이 펼쳐진다
            await DelayUntil(t0 + SealCastAt, ct);
            Vector3 skyAt = feet + Vector3.up * (spec.BodyHeight + SealSkyHeight);
            sky = LichVfx.PlayLoopTinted(LichVfxSlot.SealArray, skyAt, Quaternion.Euler(180f, 0f, 0f), spec.SigilScale * 0.8f, spec.SealColor);
            Say($"Seal_Ch{spec.Chapter}_Cast", SealCastLine, DialogueSpeaker.Merlin, BossBarkType.MerlinNarration);

            // ③ 사슬이 하나씩 내리꽂힌다 — 꽂힐 때마다 금빛 섬광 · 흔들림 · 움찔
            float yaw = body.eulerAngles.y;
            for (int i = 0; i < chains.Length; i++)
            {
                await DelayUntil(t0 + SealFirstChainAt + i * SealChainGap, ct);
                Vector3 dir  = Quaternion.Euler(0f, yaw + 45f + i * 90f, 0f) * Vector3.forward;
                Vector3 from = skyAt + dir * (spec.ChainRadius * 0.6f);
                chains[i] = LichChainLine.Create(from, from + Vector3.down * 0.1f, ChainWidth, spec.SealColor);
                await DropChainAsync(chains[i], from, body, lift, ct);
                if (chains[i] != null) { chains[i].Tension(1f); chains[i].Flash(0.3f); }
                LichVfx.PlayTinted(LichVfxSlot.SealBurst, body.position + lift, Quaternion.identity, spec.WrapScale * 0.25f, spec.SealColor);
                BossImpactFeedback.TriggerCameraShake(0.05f, 0.12f);
                PlayState(anim, Pick(spec.FlinchAnims, i));
            }

            // ④ 조인다 — 주저앉고 발밑 봉인진 · 멀린 한 줄
            await Delay(0.2f, ct);
            foreach (var c in chains) if (c != null) { c.Tension(1f); c.Flash(0.4f); }
            PlayState(anim, spec.CollapseAnim);
            floor = LichVfx.PlayLoopTinted(LichVfxSlot.SealArray, feet + Vector3.up * 0.05f, Quaternion.identity, spec.SigilScale, spec.SealColor);
            LichVfx.PlayTinted(LichVfxSlot.SealBurst, body.position + lift, Quaternion.identity, spec.WrapScale * 0.5f, spec.SealColor);
            Say($"Seal_Ch{spec.Chapter}_Scene", spec.SealLine, DialogueSpeaker.Merlin, BossBarkType.MerlinNarration);

            // ⑤ 봉인 완성 — 빛기둥. 봉인진 둘과 사슬은 남는다(사슬이 허공에 매달려 보이지 않게 머리 위 봉인진도)
            await DelayUntil(t0 + doneAt, ct);
            LichVfx.PlayTinted(LichVfxSlot.SealComplete, feet + Vector3.up * 0.05f, Quaternion.identity, spec.SigilScale, spec.SealColor);
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
            var (pos, at) = BossPageTransitionState.ClearShot(look, b + dir * spec.ShotBack + Vector3.up * spec.ShotHeight, look);
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
        }
    }

    /// <summary>역류 물줄기 출발점 — 보스에서 플레이어 반대쪽 바닥(봉인석 자리).</summary>
    private static Vector3 StreamSource(Transform body)
    {
        var player = GameRunBootstrapper.Instance?.Run?.Player?.transform;
        Vector3 away = player != null ? body.position - player.position : -body.forward;
        away.y = 0f;
        away = away.sqrMagnitude > 0.01f ? away.normalized : -body.forward;
        Vector3 p = body.position + away * StreamDistance;
        if (Physics.Raycast(p + Vector3.up * 3f, Vector3.down, out var hit, 40f, 1 << 3, QueryTriggerInteraction.Ignore))
            return hit.point;
        return new Vector3(p.x, body.position.y, p.z);
    }

    /// <summary>대사 CSV 키가 먼저, 없으면 폴백 줄.</summary>
    private static void Say(string key, string fallback, DialogueSpeaker speaker, BossBarkType type)
    {
        if (UI_BossBark.ShowDialogue(key, type)) return;
        if (!string.IsNullOrEmpty(fallback)) UI_BossBark.Show(fallback, type, speaker);
    }

    private static UniTask Delay(float seconds, CancellationToken ct)
        => UniTask.Delay(TimeSpan.FromSeconds(Mathf.Max(0f, seconds)), DelayType.UnscaledDeltaTime, cancellationToken: ct);

    private static UniTask DelayUntil(float unscaledTime, CancellationToken ct)
        => Delay(unscaledTime - Time.unscaledTime, ct);
}
}
