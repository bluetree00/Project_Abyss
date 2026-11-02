using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 멀린 룬 보관 고르기(11-02 Q26) — 사망 → 결과창 → 멀린 부활 연출 뒤, 판에 놓았던 룬 중 하나를 맡긴다.
/// 코드로 짓는 덮개(부활 연출과 같은 검은 바탕 · 정렬 RunBoundary 위) — 카드를 눌러 고르고 [이 룬을 맡긴다] · [맡기지 않는다].
/// 반환 = 고른 룬(맡기지 않으면 null). 저장은 부르는 쪽이 <see cref="MerlinRuneKeep.Keep"/>.
/// </summary>
public static class MerlinRuneKeepView
{
    private const int   PerRow = 8;
    private const float CardW = 132f, CardH = 176f, Gap = 16f;
    private static readonly Color Lilac  = new(0.78f, 0.74f, 0.92f, 1f);
    private static readonly Color CardBg = new(0.07f, 0.07f, 0.09f, 0.92f);

    /// <summary>고르기 창이 열려 있나 — ESC 처리기가 비킨다(11-02 Q32).</summary>
    public static bool IsOpen { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => IsOpen = false;

    public static async UniTask<RuntimeItemData> PickAsync(IReadOnlyList<RuntimeItemData> runes, CancellationToken ct)
    {
        if (runes == null || runes.Count == 0) return null;

        var (go, root, group) = UICodeOverlay.Create("@MerlinRuneKeep", UISortingOrder.RunBoundary + 1);
        IsOpen = true;
        int picked = -1;
        bool done = false, keep = false;
        var frames = new List<Image>(runes.Count);
        try
        {
            UICodeOverlay.Img(root, "BG", Vector2.zero, Vector2.zero, new Color(0f, 0f, 0f, 0.94f), stretch: true);
            string merlin = StoryProgress.IsMerlinNamed ? Loc.T("멀린") : "???";
            UICodeOverlay.Txt(root, $"{merlin} —  {Loc.T("「네가 놓았던 룬 하나는 내가 맡아 둘게. 다음 길에서 돌려줄게.」")}", 28f, Lilac, new Vector2(0f, 330f), 1500f, FontStyles.Italic);
            UICodeOverlay.Txt(root, "고른 룬은 다음 런을 시작할 때 보관함으로 돌아온다", 20f, UICodeOverlay.Mist, new Vector2(0f, 282f), 1200f);

            int rows = (runes.Count + PerRow - 1) / PerRow;
            const float top = 180f;
            for (int i = 0; i < runes.Count; i++)
            {
                int row = i / PerRow, col = i % PerRow;
                int inRow = Mathf.Min(PerRow, runes.Count - row * PerRow);
                float x = (col - (inRow - 1) * 0.5f) * (CardW + Gap);
                float y = top - CardH * 0.5f - row * (CardH + Gap);
                int index = i;
                frames.Add(Card(root, runes[i], new Vector2(x, y), () => picked = index));
            }

            float footY = Mathf.Min(top - rows * (CardH + Gap) - 40f, -330f);
            var keepBtn = UICodeOverlay.Btn(root, "이 룬을 맡긴다", new Vector2(0f, footY), new Vector2(280f, 54f), 24f, () => { keep = true; done = true; });
            UICodeOverlay.Btn(root, "맡기지 않는다", new Vector2(0f, footY - 62f), new Vector2(220f, 40f), 18f, () => { keep = false; done = true; }, quiet: true);

            for (float t = 0f; t < 0.35f; t += Time.unscaledDeltaTime) { group.alpha = t / 0.35f; await UniTask.Yield(PlayerLoopTiming.Update, ct); }
            group.alpha = 1f;

            int shown = -2;
            while (!done)
            {
                if (picked != shown)
                {
                    shown = picked;
                    for (int i = 0; i < frames.Count; i++) frames[i].color = i == picked ? UIPalette.Gold : RarityLine(runes[i]);
                    keepBtn.interactable = picked >= 0;
                }
                if (picked >= 0 && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))) { keep = true; done = true; }
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }

            for (float t = 0f; t < 0.25f; t += Time.unscaledDeltaTime) { group.alpha = 1f - t / 0.25f; await UniTask.Yield(PlayerLoopTiming.Update, ct); }
            return keep && picked >= 0 ? runes[picked] : null;
        }
        catch (System.OperationCanceledException) { return null; }
        finally
        {
            IsOpen = false;
            if (go != null) Object.Destroy(go);
        }
    }

    private static Image Card(RectTransform root, RuntimeItemData rune, Vector2 pos, System.Action onClick)
    {
        var card = UICodeOverlay.Img(root, "Card", pos, new Vector2(CardW, CardH), CardBg);
        var btn = card.gameObject.AddComponent<Button>();
        btn.targetGraphic = card;
        btn.onClick.AddListener(() => onClick());
        var rt = (RectTransform)card.transform;
        var frame = UICodeOverlay.Line(rt, RarityLine(rune));

        var art = rune.icon != null ? rune.icon : RuneArt.GetArt(rune.rarity);
        var icon = UICodeOverlay.Img(rt, "Icon", new Vector2(0f, 34f), new Vector2(76f, 76f), art != null ? Color.white : Color.clear);
        icon.sprite = art;
        icon.preserveAspect = true;
        icon.raycastTarget  = false;

        var e = ElementDef.GetById(rune.element);
        UICodeOverlay.Txt(rt, rune.displayName, 16f, UICodeOverlay.Ink, new Vector2(0f, -32f), CardW - 12f);
        string sub = (e != null ? $"<color=#{ColorUtility.ToHtmlStringRGB(e.Color)}>{e.Name}</color> · " : "") + rune.rarity;
        UICodeOverlay.Txt(rt, sub, 14f, UICodeOverlay.Mist, new Vector2(0f, -66f), CardW - 12f);
        return frame;
    }

    private static Color RarityLine(RuntimeItemData rune)
    {
        var c = RewardPresentation.FrameColor(rune.rarity);
        c.a = 0.55f;
        return c;
    }
}
