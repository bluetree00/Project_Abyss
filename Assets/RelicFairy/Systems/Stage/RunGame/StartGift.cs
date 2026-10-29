using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 해방기 · 악몽 시작 선물(10-29 설계 §6-1 A · 사용자 「챕터1에서 유저가 아무 내용도 없이 게임을 진행해야하기에 추가 장치가 필요」).
/// 런 시작 대기방(Ch1) 대사가 끝나면 세 갈래 중 하나를 고른다 — 고른 갈래에서 한 번 더 고른다.
///   · 룬 — 희귀 이상 3장 중 하나(룬 고르기 창 · 넘기면 원석)
///   · 근접 증강 — 금 2장 중 하나(<see cref="MeleeAugmentDraft.OfferGift"/> · 근접 무기를 들었을 때만 칸이 뜬다)
///   · 유물 기억 — 「되살아난 기억」 2장 중 하나(보스 창과 같은 창 · 진화 카드는 빠진다 — 진화는 보스 보상)
/// 봉인기엔 아무것도 하지 않는다. 창은 코드로 짠다(<see cref="NightmareGateChoice"/>와 같은 결) — 시간을 멈추고 입력을 막는다.
/// </summary>
public static class StartGift
{
    // ── Constants ─────────────────────────────────────────────
    private const float CardW = 340f;
    private const float CardH = 230f;
    private const float CardGap = 28f;
    private const int   RuneChoices    = 3;
    private const int   AugmentChoices = 2;
    private const int   EpicChance     = 35;   // 룬 갈래 — 희귀 65 · 영웅 35(제단 등급 잠금이면 희귀로 내린다)
    private static readonly Color NightmareAccent = new(0.70f, 0.30f, 0.60f, 1f);

    private enum Branch { None, Rune, Augment, Relic }

    // ── Static ────────────────────────────────────────────────
    private static readonly object s_timeOwner = new();
    private static readonly List<ItemSO> s_pool = new(32);

    /// <summary>세 갈래 창이 떠 있다 — ESC 듣개가 그 밑에 일시정지 메뉴를 열지 않는다.</summary>
    public static bool IsOpen { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => IsOpen = false;

    // ── Public Methods ────────────────────────────────────────

    /// <summary>런 시작 대기방 — 해방기 · 악몽이면 선물 하나를 고르게 한다(봉인기 · 런 없음이면 바로 돌아온다).</summary>
    public static async UniTask OfferAsync(GameRunSession run, CancellationToken ct)
    {
        if (run == null || StoryProgress.Era == StoryEra.Sealed) return;
        try
        {
            var weapon = run.Player != null && run.Player.WeaponManager != null ? run.Player.WeaponManager.CurrentWeaponData : null;
            bool augment = MeleeAugmentDraft.CanOfferGift(weapon);
            bool relic = AppBootstrapper.Instance?.Loadout?.Relic != null;

            // 기억의 제단 시기 칸 — 넓은 선물(1단 장 수 +1 · 2단 유물 기억 두 번) · 악몽의 선물(악몽 룬 갈래에 전설 한 장)
            int wide = MemoryAltarService.Level(MemoryAltarCatalog.LibGiftWide);
            int runeCount = RuneChoices + (wide >= 1 ? 1 : 0);
            int augmentCount = AugmentChoices + (wide >= 1 ? 1 : 0);
            int relicTimes = wide >= 2 ? 2 : 1;
            bool legend = StoryProgress.IsNightmareMode && MemoryAltarService.IsUnlocked(MemoryAltarCatalog.NmGift);

            var branch = await ChooseAsync(augment, relic, runeCount, augmentCount, relicTimes, legend, ct);
            Debug.Log($"[StartGift] {StoryProgress.Era} 시작 선물 — {branch} (넓은 선물 {wide} · 전설 {legend})");
            switch (branch)
            {
                case Branch.Rune:    await GiveRuneAsync(run, runeCount, legend, ct); break;
                case Branch.Augment: MeleeAugmentDraft.OfferGift(weapon, AugmentTier.Gold, augmentCount); break;
                case Branch.Relic:
                    for (int i = 0; i < relicTimes && GameRunBootstrapper.Instance != null; i++)
                        await GameRunBootstrapper.Instance.OfferRelicGiftAsync(ct);
                    break;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { Debug.LogError($"[StartGift] 선물 예외 — 건너뛴다: {e}"); }
    }

    // ── Private Methods ───────────────────────────────────────

    private static async UniTask<Branch> ChooseAsync(bool augment, bool relic, int runeCount, int augmentCount, int relicTimes, bool legend,
                                                     CancellationToken outer)
    {
        var picked = Branch.None;
        bool nightmare = StoryProgress.IsNightmareMode;
        Color accent = nightmare ? NightmareAccent : UITheme.Gold;
        var player = GameRunBootstrapper.Instance?.Run?.Player;

        var go = new GameObject("@StartGift", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CanvasGroup));
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(outer, go.GetCancellationTokenOnDestroy());
        var ct = cts.Token;
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = UISortingOrder.RunChoice;
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight  = 0.5f;
        var group = go.GetComponent<CanvasGroup>();
        group.alpha = 0f;

        TimeScaleArbiter.Acquire(s_timeOwner, 0f, TimeScaleArbiter.Priority.Pause);
        if (player != null) player.SetInputEnabled(false);   // 카드를 누르는 클릭이 공격으로 새지 않게
        IsOpen = true;
        try
        {
            var veil = NewRect(go.transform, "Veil", Vector2.zero, Vector2.zero);
            veil.anchorMin = Vector2.zero; veil.anchorMax = Vector2.one;
            veil.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.72f);

            NewText(go.transform, nightmare ? "악몽의 문턱 — 선물 하나를 고른다" : "해방의 선물 — 하나를 고른다", 34f, accent,
                    new Vector2(0f, 240f), new Vector2(1000f, 46f));
            NewText(go.transform, "해방기 · 악몽 런은 첫 방에 들어가기 전에 선물 하나를 들고 간다", 20f, UITheme.Mute,
                    new Vector2(0f, 188f), new Vector2(1100f, 30f));

            int n = 1 + (augment ? 1 : 0) + (relic ? 1 : 0);
            float x = -(n - 1) * (CardW + CardGap) * 0.5f;
            NewCard(go.transform, new Vector2(x, -10f), "룬 하나",
                    $"{(legend ? "전설 한 장을 포함해 " : "희귀 이상 ")}{runeCount}장 중 하나를 고른다\n룬판에 바로 놓는다", accent,
                    () => picked = Branch.Rune);
            x += CardW + CardGap;
            if (augment)
            {
                NewCard(go.transform, new Vector2(x, -10f), "근접 증강 하나", $"금 증강 {augmentCount}장 중 하나를 고른다\n지금 든 근접 무기에 붙는다", accent,
                        () => picked = Branch.Augment);
                x += CardW + CardGap;
            }
            if (relic)
            {
                string relicName = AppBootstrapper.Instance?.Loadout?.Relic?.DisplayName ?? "유물";
                NewCard(go.transform, new Vector2(x, -10f), relicTimes > 1 ? "유물 기억 둘" : "유물 기억 하나",
                        $"되살아난 기억 2장 중 하나를 {(relicTimes > 1 ? "두 번 " : "")}고른다\n{relicName}에 새겨진다", accent,
                        () => picked = Branch.Relic);
            }

            NewText(go.transform, "카드를 눌러 고른다", 18f, UITheme.Mute, new Vector2(0f, -188f), new Vector2(800f, 28f));

            FadeAsync(group, 1f, UIFader.OpenSec, ct).Forget();
            Managers.Sound?.PlayUiEvent(SoundEvent.UiOpen);      // 10-29 UI 소리 — 열기
            while (picked == Branch.None) await UniTask.Yield(PlayerLoopTiming.Update, ct);
            Managers.Sound?.PlayUiEvent(SoundEvent.UiConfirm);   // 10-29 UI 소리 — 카드 고름
            group.blocksRaycasts = false;
            await FadeAsync(group, 0f, UIFader.CloseSec, ct);
        }
        finally
        {
            IsOpen = false;
            TimeScaleArbiter.Release(s_timeOwner);
            if (player != null) player.SetInputEnabled(true);
            if (go != null) UnityEngine.Object.Destroy(go);
        }
        return picked;
    }

    /// <summary>룬 갈래 — 희귀 이상 <paramref name="count"/>장(겹치지 않게 · 가진 룬 빼고)으로 룬 고르기 창. <paramref name="legend"/>면 첫 장은 전설(제단 등급 잠금이면 내린다).
    /// 넘기면 원석(<see cref="ClearRewardTrigger"/>와 같은 규칙).</summary>
    private static async UniTask GiveRuneAsync(GameRunSession run, int count, bool legend, CancellationToken ct)
    {
        var candidates = new List<(RuntimeItemData data, ItemSO so)>(count);
        var used = new HashSet<string>();
        for (int attempt = 0; attempt < count * 8 && candidates.Count < count; attempt++)
        {
            var rolled = legend && candidates.Count == 0 ? ItemRarity.Legendary
                       : UnityEngine.Random.Range(0, 100) < EpicChance ? ItemRarity.Epic : ItemRarity.Rare;
            var rarity = MemoryAltarService.ClampRarity(rolled);
            run.ItemInventory?.FilterObtainable(ItemSORegistry.GetByRarity(rarity), s_pool);
            if (s_pool.Count == 0) continue;
            var so = s_pool[UnityEngine.Random.Range(0, s_pool.Count)];
            if (so == null || !used.Add(so.itemId)) continue;
            var data = RuntimeItemData.FromSO(so);
            if (data != null) candidates.Add((data, so));
        }
        if (candidates.Count == 0) { Debug.LogWarning("[StartGift] 룬 후보 없음 — 건너뜀"); return; }

        var popup = await Managers.UI.ShowPopupUIAndGetAsync<UI_RuneSelectPopup>();
        if (popup == null)
        {
            run.EffectManager?.OnItemPickup(candidates[0].data);
            run.ItemInventory?.AddToStagingOverflow(candidates[0].data);
            return;
        }
        var wait = popup.WaitForInteractionAsync(ct);
        popup.Setup(candidates, run.ItemInventory, -1, 0, 0);
        await wait;
        if (popup.Skipped) { run.FuelBank?.Add(FuelKind.RuneOre, Mathf.Max(ClearRewardTrigger.SkipOreReward, popup.SkipOre)); return; }
        if (popup.Result != null) run.EffectManager?.OnItemPickup(popup.Result);
    }

    /// <summary>선택 카드 — 제목 + 두 줄 설명.</summary>
    private static void NewCard(Transform parent, Vector2 pos, string title, string desc, Color accent, Action onClick)
    {
        var rt = NewRect(parent, $"Card_{title}", new Vector2(CardW, CardH), pos);
        var img = rt.gameObject.AddComponent<Image>();
        UITheme.StylePanel(img, UITheme.Band, new Color(accent.r, accent.g, accent.b, 0.7f));

        NewText(rt, title, 28f, accent, new Vector2(0f, 56f), new Vector2(CardW - 30f, 40f));
        var d = NewText(rt, desc, 17f, UITheme.Mute, new Vector2(0f, -26f), new Vector2(CardW - 40f, 100f));
        d.textWrappingMode = TextWrappingModes.Normal;

        var btn = rt.gameObject.AddComponent<Button>();
        btn.transition    = Selectable.Transition.ColorTint;
        btn.targetGraphic = img;
        var cb = btn.colors; cb.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f); cb.fadeDuration = 0.08f;
        btn.colors = cb;
        btn.onClick.AddListener(() => onClick?.Invoke());
        rt.gameObject.AddComponent<UIButtonFeedback>();
    }

    private static RectTransform NewRect(Transform parent, string name, Vector2 size, Vector2 pos)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;
        return rt;
    }

    private static TextMeshProUGUI NewText(Transform parent, string text, float size, Color color, Vector2 pos, Vector2 box)
    {
        var rt = NewRect(parent, "Text", box, pos);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.text             = text;
        t.fontSize         = size;
        t.color            = color;
        t.alignment        = TextAlignmentOptions.Center;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.raycastTarget    = false;
        TMPOutlineHelper.ApplySoftShadow(t);
        return t;
    }

    private static async UniTask FadeAsync(CanvasGroup g, float to, float seconds, CancellationToken ct)
    {
        float from = g.alpha;
        for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
        {
            if (g == null) return;
            g.alpha = Mathf.Lerp(from, to, t / seconds);
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
        }
        if (g != null) g.alpha = to;
    }
}
