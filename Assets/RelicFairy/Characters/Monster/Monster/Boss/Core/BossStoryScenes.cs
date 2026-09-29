using System;
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
    private const float ChainGrow     = 0.3f;    // 땅에서 몸까지 뻗는 시간
    private const float ChainStagger  = 0.12f;
    private const float SealBindAt    = 0.65f;   // 사슬이 감기는 순간(금빛 폭발 · 멀린 한 줄)
    private const float SealLinger    = 0.6f;    // 빛기둥 뒤 사슬이 남는 시간
    private const float AwakenGold    = 0.55f;   // 각성: 금빛 사슬 고리
    private const float AwakenReverse = 0.6f;    // 각성: 보라 역류
    private const float AwakenBreak   = 0.35f;   // 각성: 파쇄 뒤 숨
    private const float KillLineAt    = 0.6f;
    private const float SealCompleteCap = 1.6f;  // 빛기둥은 늦어도 이때 — 보상 창(보스 클리어 3초 뒤)보다 앞
    private const float EndShotIn     = 0.5f;    // 끝 장면: 카메라가 보스로 가는 시간
    private const float EndShotOut    = 0.5f;    // 끝 장면: 빛기둥 · 한 줄 뒤 머무는 시간
    private const float KillShotHold  = 1.4f;

    /// <summary>각성 장면 길이 — 전환 패턴이 플레이어 무적 시간에 더한다.</summary>
    public const float AwakenSeconds = AwakenGold + AwakenReverse + AwakenBreak;

    private const string SeenAwaken    = "p2_";
    private const string SeenKill      = "kill_";
    private const string SeenNightmare = "nm_";

    private static readonly Color Reversed = new Color(0.62f, 0.25f, 0.95f);

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
        public string KillLineFirst;
        public string KillLineAgain;
        public string NightmareLine;
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
            AwakenLine    = "봉인이… 녹아내렸어! 숲이 깨어나!",
            KillLineFirst = "이번엔 봉인이 아니야. …편히 쉬어.",
            KillLineAgain = "숲이 조용해졌어.",
            NightmareLine = "꿈속의 숲은… 끝나지 않는다.",
        },
        new Spec
        {
            Id = StoryProgress.Dragon, Chapter = 2,
            SealColor = new Color(1f, 0.6f, 0.2f), KillColor = new Color(0.55f, 0.3f, 0.8f),
            BodyHeight = 3f, ChainRadius = 8f, SigilScale = 0.75f, WrapScale = 2.8f, ShotBack = 16f, ShotHeight = 7f,
            Speaker       = DialogueSpeaker.Dragon,
            SealLine      = "불길이 사슬 아래 잠들었어.",
            AwakenLine    = "사슬이 녹았어… 저건 심연의 불이야!",
            KillLineFirst = "불이 꺼졌어. 이번엔 다시 타오르지 않아.",
            KillLineAgain = "불씨 하나 남지 않았어.",
            NightmareLine = "꿈속에서도 불은 꺼지지 않는다.",
        },
        new Spec
        {
            Id = StoryProgress.DeathKnight, Chapter = 3,
            SealColor = new Color(0.95f, 0.9f, 0.7f), KillColor = new Color(0.62f, 0.62f, 0.68f),
            BodyHeight = 1.4f, ChainRadius = 4.5f, SigilScale = 0.4f, WrapScale = 1.3f, ShotBack = 6.5f, ShotHeight = 2.8f,
            Speaker       = DialogueSpeaker.DeathKnight,
            SealLine      = "검을 내려놔, 기사여. …이제 쉬게 하자.",
            AwakenLine    = "봉인이 갈라졌어… 검이 둘로!",
            KillLineFirst = "…모르드레드. 이제야 네 검을 내려놓는구나.",
            KillLineAgain = "검이 멈췄어.",
            NightmareLine = "꿈속에서도… 맹세는 부서진 채다.",
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
        Debug.Log($"[BossStory] 악몽 첫 조우 — {bossId}");
    }

    /// <summary>
    /// HP 0 — 봉인기면 봉인 장면, 해방기부터는 처치 장면. 보스 사망 상태가 <c>base.Enter</c> 뒤에 부른다.
    /// 몸이 사라지기 시작하는 시각(사망 설정 despawnDelay) 직전에 빛기둥을 세운다.
    /// </summary>
    public static void PlayEnd(MonsterContext ctx, string bossId)
    {
        var spec = Find(bossId);
        if (ctx?.Monster == null || spec == null || !IsStoryFight(spec)) return;
        float vanishAt = ctx.Death?.despawnDelay ?? 3f;

        bool seal = !StoryProgress.IsLiberated;
        Debug.Log($"[BossStory] {(seal ? "봉인" : "처치")} 장면 — {bossId} (시기 {StoryProgress.Era}, 사라짐 {vanishAt:0.0}초)");
        if (seal) SealSceneAsync(ctx, spec, vanishAt).Forget();
        else      KillSceneAsync(ctx, spec).Forget();
    }

    /// <summary>해방기 첫 전환인가(보스마다 계정 1회) — 전환 패턴이 각성 장면을 넣을지 정한다.</summary>
    public static bool IsAwakenDue(string bossId)
    {
        var spec = Find(bossId);
        return spec != null && IsStoryFight(spec) && StoryProgress.IsLiberated && !StoryProgress.IsSeen(SeenAwaken + bossId);
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
        var wrap = LichVfx.PlayLoopTinted(LichVfxSlot.ChainGold, at, Quaternion.identity, spec.WrapScale, spec.SealColor);
        try
        {
            await Delay(AwakenGold, ct);

            // 봉인이 거꾸로 흐른다 — 금빛이 보라로
            LichVfx.Stop(ref wrap, 0.2f);
            wrap = LichVfx.PlayLoop(LichVfxSlot.ChainReversed, at, Quaternion.identity, spec.WrapScale * 0.45f);   // 리치 붕괴용이라 크다
            Say($"Awaken_Ch{spec.Chapter}", spec.AwakenLine, DialogueSpeaker.Merlin, BossBarkType.MerlinNarration);
            await Delay(AwakenReverse, ct);

            // 깨진다
            LichVfx.Stop(ref wrap);
            LichVfx.PlayTinted(LichVfxSlot.ChainShockwave, body.position + Vector3.up * 0.2f, Quaternion.identity, spec.WrapScale, Reversed);
            LichVfx.PlayTinted(LichVfxSlot.SealBurst, at, Quaternion.identity, spec.WrapScale * 0.3f, Reversed);
            LichCinematics.Flash(Reversed, 0.25f, 0.15f);   // 0.3은 화면 전체가 보랏빛으로 덮였다(09-29 캡처)
            await Delay(AwakenBreak, ct);
        }
        finally
        {
            LichVfx.Stop(ref wrap);
        }
        StoryProgress.MarkSeen(SeenAwaken + bossId);
    }

    // ── Private Methods ────────────────────────────────────────

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

    /// <summary>봉인 장면 — 봉인진 → 사슬 넷 → 감김(폭발 · 멀린) → 빛기둥 → 사슬이 사라진다. 카메라가 보스를 비춘다.</summary>
    private static async UniTaskVoid SealSceneAsync(MonsterContext ctx, Spec spec, float vanishAt)
    {
        var ct     = ctx.Monster.GetCancellationTokenOnDestroy();
        var body   = ctx.Transform;
        var chains = new LichChainLine[4];
        GameObject sigil = null;
        float t0 = Time.unscaledTime;
        float completeAt = Mathf.Clamp(vanishAt - 0.2f, SealBindAt + 0.3f, SealCompleteCap);
        EndShotAsync(ctx, spec, completeAt + EndShotOut, ct).Forget();
        try
        {
            await LichVfx.LoadAsync();
            Vector3 feet = body.position;
            Vector3 lift = Vector3.up * spec.BodyHeight;
            sigil = LichVfx.PlayLoopTinted(LichVfxSlot.SealArray, feet + Vector3.up * 0.05f, Quaternion.identity, spec.SigilScale, spec.SealColor);

            float yaw = body.eulerAngles.y;
            for (int i = 0; i < chains.Length; i++)
            {
                Vector3 dir = Quaternion.Euler(0f, yaw + 45f + i * 90f, 0f) * Vector3.forward;
                Vector3 anchor = feet + dir * spec.ChainRadius;
                chains[i] = LichChainLine.Create(anchor, anchor + Vector3.up * 0.1f, ChainWidth, spec.SealColor);
                GrowChainAsync(chains[i], anchor, body, lift, ct).Forget();
                await Delay(ChainStagger, ct);
            }

            await DelayUntil(t0 + SealBindAt, ct);
            foreach (var c in chains)
            {
                if (c == null) continue;
                c.Tension(1f);
                c.Flash(0.3f);
            }
            LichVfx.PlayTinted(LichVfxSlot.SealBurst, body.position + lift, Quaternion.identity, spec.WrapScale * 0.5f, spec.SealColor);
            Say($"Seal_Ch{spec.Chapter}_Scene", spec.SealLine, DialogueSpeaker.Merlin, BossBarkType.MerlinNarration);

            // 몸이 사라지기 직전 — 봉인 완성(금빛 고리 + 빛기둥)
            await DelayUntil(t0 + completeAt, ct);
            LichVfx.PlayTinted(LichVfxSlot.SealComplete, feet + Vector3.up * 0.05f, Quaternion.identity, spec.SigilScale, spec.SealColor);
            await Delay(SealLinger, ct);
        }
        catch (OperationCanceledException) { }
        finally
        {
            foreach (var c in chains)
                if (c != null) c.Dispose();
            LichVfx.Stop(ref sigil, 0.6f);
        }
    }

    /// <summary>사슬이 땅에서 몸까지 뻗은 뒤 몸을 따라간다.</summary>
    private static async UniTaskVoid GrowChainAsync(LichChainLine line, Vector3 anchor, Transform body, Vector3 bodyOffset, CancellationToken ct)
    {
        if (line == null || body == null) return;
        try
        {
            for (float t = 0f; t < ChainGrow; t += Time.unscaledDeltaTime)
            {
                if (line == null || body == null) return;
                line.SetEnds(anchor, Vector3.Lerp(anchor, body.position + bodyOffset, t / ChainGrow));
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            if (line != null && body != null) line.Follow(null, Vector3.zero, body, bodyOffset);
        }
        catch (OperationCanceledException) { }
    }

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

            bool first = !StoryProgress.IsSeen(SeenKill + spec.Id);
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
    /// 끝 장면 카메라 — 플레이어 쪽에서 쓰러진 보스를 비춘다(입력 잠금 · 레터박스). 장면 시작부터 <paramref name="until"/>초에 돌려준다.
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
            if (player != null) LichCinematics.ReturnToPlayerAsync(player, CancellationToken.None).Forget();
            pc?.SetInputEnabled(true);
        }
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
