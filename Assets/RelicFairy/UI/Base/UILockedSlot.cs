using TMPro;
using UnityEngine;

/// <summary>
/// 드래프트 화면의 <b>잠긴 자리</b>. 해금하면 여기에 후보가 하나 더 놓인다는 것을 미리 보여준다.
///
/// <para><b>왜 필요한가</b> — 해금은 후보를 1→2→3, 3→4로 늘리고 카드 배치도 실제로 다시 잡는다.
/// 그런데 해금 <b>전</b>에는 그냥 카드가 적을 뿐이라, 넓은 창에 카드 한 장이 덩그러니 놓인다.
/// 무엇이 늘어날 수 있는지 화면에 없으니 확장이 일어나도 <b>비교할 대상이 없다</b> —
/// 플레이어는 "늘었다"를 느끼지 못하고 그냥 "원래 이랬나" 한다.</para>
///
/// <para>빈 자리를 미리 그려 두면 해금 전에는 <b>목표</b>가 되고, 해금 후에는 그 자리가 채워지는 것으로
/// <b>보상</b>이 된다. 같은 픽셀이 두 번 일한다.</para>
///
/// <para><b>조용해야 한다</b> — 진짜 카드와 경쟁하면 고르는 일을 방해한다. 그래서 테두리만 흐리게 남기고
/// 채움은 거의 투명하며, 클릭도 받지 않는다(<c>raycastTarget = false</c>).</para>
/// </summary>
public static class UILockedSlot
{
    // ── Constants ────────────────────────────────────────────
    private static readonly Color Border  = new(0.32f, 0.29f, 0.24f, 0.55f);
    private static readonly Color Fill    = new(0.06f, 0.05f, 0.04f, 0.35f);
    private static readonly Color Ink     = new(0.62f, 0.57f, 0.47f, 0.75f);
    private static readonly Color InkDim  = new(0.52f, 0.47f, 0.39f, 0.60f);

    /// <summary>
    /// 잠긴 자리 한 칸을 만든다. 위치·크기는 진짜 카드와 <b>같은 규칙</b>으로 넘겨야
    /// 줄이 흐트러지지 않는다.
    /// </summary>
    /// <param name="caption">아랫줄 안내. 어디서 해금하는지를 적는다.</param>
    public static GameObject Build(Transform parent, string name, Vector2 pos, Vector2 size,
                                   string caption = "기억의 제단에서 해금")
    {
        var frame = ShopUIStyle.MakeFrame(parent, name, Border, Fill, 2f, raycast: false);
        var rt    = (RectTransform)frame.transform.parent;   // 위치·크기는 테두리(outer)가 갖는다
        ShopUIStyle.Anchor(rt,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, size);

        var title = ShopUIStyle.MakeText(frame.transform, "LockTitle", 20f, FontStyles.Bold,
                                         TextAlignmentOptions.Center, Ink);
        title.text = "잠긴 자리";
        title.raycastTarget = false;
        ShopUIStyle.Anchor(title.rectTransform,
            new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, 12f), new Vector2(0f, 26f));

        var sub = ShopUIStyle.MakeText(frame.transform, "LockCaption", 14f, FontStyles.Normal,
                                       TextAlignmentOptions.Center, InkDim);
        sub.text = caption;
        sub.raycastTarget = false;
        ShopUIStyle.Anchor(sub.rectTransform,
            new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, -14f), new Vector2(0f, 22f));

        return rt.gameObject;
    }
}
