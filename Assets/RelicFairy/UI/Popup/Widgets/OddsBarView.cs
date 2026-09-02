using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 정제소 등급 확률 막대 3단 — 의뢰서 R7.
///
/// Rare / Epic / Legendary 비율을 <b>길이로</b> 보여준다. 예전엔 "Rare 57% / Epic 33% ..." 텍스트라
/// 피버가 쌓여 확률이 오르는 것이 눈에 안 들어왔다. 과열(다음 회 2배)이 걸리면 상위 두 줄이 눈에 띄게 늘어난다.
/// </summary>
public sealed class OddsBarView : MonoBehaviour
{
    private const int Tiers = 3;                 // 0=Rare 1=Epic 2=Legendary
    private const float TopPad = 12f;            // 완성본엔 제목이 없다 — 막대 3줄이 패널을 채운다
    private const float RowGap = 6f;
    private const float LabelW = 74f;
    private const float PctW   = 46f;

    private static readonly ItemRarity[] TierRarity =
        { ItemRarity.Rare, ItemRarity.Epic, ItemRarity.Legendary };
    private static readonly string[] TierName = { "Rare", "Epic", "Legend" };
    private static readonly Color TrackColor = new(0.09f, 0.09f, 0.12f, 1f);

    [SerializeField] private RectTransform[] _fillRT;
    [SerializeField] private Image[]   _fillImg;
    [SerializeField] private TMP_Text[] _pct;
    // 막대 폭은 Create에서 한 번 계산된다 — 직렬화하지 않으면 구운 프리팹에서 0으로
    // 되살아나 모든 확률 막대가 폭 0이 된다(막대가 아예 안 보인다).
    [SerializeField] private float _trackW;

    public static OddsBarView Create(Transform parent, Vector2 anchorMin, Vector2 anchorMax,
                                     Vector2 pivot, Vector2 pos, Vector2 size)
    {
        var skin = UISkin.Refinery;

        var frame = ShopUIStyle.MakeFrame(parent, "OddsBar",
            ShopUIStyle.CardBorder, ShopUIStyle.CardFill, 2f);
        var rootRT = (RectTransform)frame.transform.parent;
        ShopUIStyle.Anchor(rootRT, anchorMin, anchorMax, pivot, pos, size);
        // 확률막대 바탕 — 패널 전체 배경 아트
        if (rootRT.TryGetComponent<Image>(out var oddsBg))
            ShopUIStyle.Skin(oddsBg, skin != null ? skin.oddsPanel : null, sliced: true);

        // 바탕 아트는 바깥 프레임에 깔린다 — 안쪽 채움을 비워야 아트가 가려지지 않는다.
        if (oddsBg != null && oddsBg.sprite != null) frame.color = Color.clear;

        var view = rootRT.gameObject.AddComponent<OddsBarView>();
        var inner = frame.transform;

        float padX  = 14f;
        float rowH  = Mathf.Max(16f, (size.y - TopPad * 2f - RowGap * (Tiers - 1)) / Tiers);
        view._trackW = size.x - padX * 2f - LabelW - PctW;

        view._fillRT  = new RectTransform[Tiers];
        view._fillImg = new Image[Tiers];
        view._pct     = new TMP_Text[Tiers];

        for (int i = 0; i < Tiers; i++)
        {
            float y = -(TopPad + i * (rowH + RowGap));
            var rar = TierRarity[i];

            var lbl = ShopUIStyle.MakeText(inner, $"L{i}", 12f, FontStyles.Bold,
                TextAlignmentOptions.Left, ShopUIStyle.RarityGlow(rar));
            ShopUIStyle.Anchor(lbl.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(padX, y), new Vector2(LabelW, rowH));
            lbl.text = TierName[i];

            var track = ShopUIStyle.MakeImage(inner, $"T{i}", TrackColor);
            ShopUIStyle.Anchor(track.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(padX + LabelW, y - 2f), new Vector2(view._trackW, rowH - 6f));
            ShopUIStyle.Skin(track, skin != null ? skin.barTrack : null, sliced: true);

            // 채움 — 좌측 기준으로 폭만 바꾼다(스케일 X, 아트가 늘어나지 않게).
            var fill = ShopUIStyle.MakeImage(track.transform, "Fill", ShopUIStyle.RarityGlow(rar));
            ShopUIStyle.Anchor(fill.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f),
                Vector2.zero, new Vector2(0f, 0f));
            view._fillRT[i]  = fill.rectTransform;
            view._fillImg[i] = fill;

            // 확률 막대 테두리 — 채움 위에 얹는 칸별 프레임(테두리가 항상 보이게)
            if (skin != null && skin.barFrame != null)
            {
                var bf = ShopUIStyle.MakeImage(track.transform, "BarFrame", Color.white);
                ShopUIStyle.Stretch(bf.rectTransform);
                bf.raycastTarget = false;
                ShopUIStyle.Skin(bf, skin.barFrame, sliced: true);
            }

            var pct = ShopUIStyle.MakeText(inner, $"P{i}", 12f, FontStyles.Bold,
                TextAlignmentOptions.Right, ShopUIStyle.TextPrimary);
            ShopUIStyle.Anchor(pct.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(padX + LabelW + view._trackW, y), new Vector2(PctW, rowH));
            view._pct[i] = pct;
        }

        view.SetOdds(1f, 0f, 0f, heated: false);
        return view;
    }

    // ── Public Methods ──

    /// <summary>확률 갱신. heated=과열(다음 1회 상위 등급 2배)이면 상위 두 줄을 강조한다.</summary>
    public void SetOdds(float rare, float epic, float legendary, bool heated)
    {
        var skin = UISkin.Refinery;
        float[] v = { rare, epic, legendary };

        for (int i = 0; i < Tiers; i++)
        {
            float p = Mathf.Clamp01(v[i]);

            var sprite = skin != null ? skin.BarFill(i, heated) : null;

            var f = _fillRT[i];
            if (f != null)
            {
                // 앵커를 매번 좌측 기준으로 되돌린다 — 비율 앵커로 구워지면 sizeDelta가
                // '더해지는 값'이 되어 폭 계산이 통째로 무너진다(굳은 비율 + 준 폭).
                f.anchorMin        = new Vector2(0f, 0f);
                f.anchorMax        = new Vector2(sprite != null ? 1f : 0f, 1f);
                f.pivot            = new Vector2(0f, 0.5f);
                f.anchoredPosition = Vector2.zero;

                if (sprite != null)
                {
                    // 아트가 있으면 폭은 트랙에 꽉 채우고 잘라내기는 fillAmount가 한다.
                    // 폭 자체를 줄이면 막대 아트가 가로로 눌린다(30%에서 1.78배 왜곡).
                    f.sizeDelta = Vector2.zero;
                }
                else
                {
                    // 폴백(단색)은 잘라낼 무늬가 없으니 예전처럼 폭으로 표현한다.
                    // 트랙은 매번 실측한다 — 베이크 상수만 믿으면 창이 커졌을 때 100%가 다 못 찬다.
                    float trackW = f.parent is RectTransform tr && tr.rect.width > 1f
                                 ? tr.rect.width : _trackW;
                    f.sizeDelta = new Vector2(trackW * p, 0f);
                }
            }

            if (_fillImg[i] != null)
            {
                if (sprite != null)
                {
                    ShopUIStyle.Skin(_fillImg[i], sprite, sliced: true);
                    _fillImg[i].type       = Image.Type.Filled;
                    _fillImg[i].fillMethod = Image.FillMethod.Horizontal;
                    _fillImg[i].fillOrigin = (int)Image.OriginHorizontal.Left;
                    _fillImg[i].fillAmount = p;
                }
                else
                {
                    // 폴백: 등급색. 과열이면 상위 두 줄만 밝게 띄워 "지금 유리하다"를 알린다.
                    var c = ShopUIStyle.RarityGlow(TierRarity[i]);
                    _fillImg[i].sprite = null;
                    _fillImg[i].type   = Image.Type.Simple;   // 아트가 있다 없어지면 Filled가 남는다
                    _fillImg[i].color  = (heated && i > 0) ? Color.Lerp(c, Color.white, 0.35f) : c;
                }
            }

            if (_pct[i] != null)
                _pct[i].text = $"{Mathf.RoundToInt(p * 100f)}%";
        }
    }
}
