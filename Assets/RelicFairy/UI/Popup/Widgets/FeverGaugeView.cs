using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 정제소 피버(열기) 게이지 — 의뢰서 R6.
///
/// 연속으로 돌릴수록 상위 등급 확률이 오른다. 그 사실을 <b>숫자가 아니라 온도로</b> 읽히게 하는 것이 목적이다
/// (예전엔 "열기 3연속" 텍스트 한 줄이라 정제소의 재미 축 하나가 통째로 묻혀 있었다).
///
/// 패널에 인라인하지 않고 독립 컴포넌트로 둔다 — 룬판 상시 탭 등 다른 화면에서도 같은 표시가 필요해진다.
/// </summary>
public sealed class FeverGaugeView : MonoBehaviour
{
    /// <summary>
    /// 게이지 칸 수 = <b>확률이 실제로 포화하는 피버 값</b>.
    /// RefineryService.Odds 기준 Epic은 fever 7에서(0.15+0.06f → 0.55 상한), Legendary는
    /// <b>fever 9에서</b>(0.03+0.025f → 0.25 상한) 멈춘다. 7로 두면 7단계에서 "최고조"라 표시하는
    /// 동안 전설 확률이 아직 오르는 중이라, 게이지가 거짓말을 한다. 느린 쪽(9)에 맞춘다.
    /// ⚠️ Odds의 계수를 바꾸면 이 값도 같이 맞춰야 한다.
    /// </summary>
    public const int MaxLevel = 9;

    private const float LabelH = 20f;
    private const float CellGap = 4f;

    // 미할당 아트 폴백 — 단계가 오를수록 뜨거워지는 색 계단.
    private static readonly Color[] HeatSteps =
    {
        new(0.16f, 0.17f, 0.21f, 1f),   // 0 — 식은 상태
        new(0.29f, 0.20f, 0.15f, 1f),
        new(0.46f, 0.27f, 0.14f, 1f),
        new(0.63f, 0.32f, 0.12f, 1f),
        new(0.76f, 0.35f, 0.16f, 1f),
        new(0.88f, 0.42f, 0.16f, 1f),
        new(1.00f, 0.54f, 0.23f, 1f),
        new(1.00f, 0.80f, 0.35f, 1f),   // 7 — 최대
    };
    private static readonly Color EmptyCell = new(0.14f, 0.14f, 0.18f, 1f);

    // 아트가 있을 때 쓰는 <b>게이지 전체 한 장</b>. 납품본 「피버0~7」은 전부 405×70으로
    // 크기가 같다 — 칸 하나가 아니라 단계별 전체 그림이라는 뜻이다.
    [SerializeField] private Image _strip;
    // 아트가 없을 때만 쓰는 폴백 칸 7개.
    [SerializeField] private Image[] _cells;
    // 구운 프리팹에서도 살아남아야 한다 — 직렬화하지 않으면 null이라
    // 피버가 올라도 안내문이 "0/7"에 영구히 멈춘다.
    [SerializeField] private TMP_Text _label;
    [SerializeField] private TMP_Text _hint;
    private int _shownLevel = -1;

    /// <summary>부모 아래에 게이지를 짓는다. size = 트랙 전체 크기.</summary>
    public static FeverGaugeView Create(Transform parent, Vector2 anchorMin, Vector2 anchorMax,
                                        Vector2 pivot, Vector2 pos, Vector2 size)
    {
        var skin = UISkin.Refinery;

        var frame = ShopUIStyle.MakeFrame(parent, "FeverGauge",
            ShopUIStyle.BronzeLine, ShopUIStyle.BandFill, 2f);
        var rootRT = (RectTransform)frame.transform.parent;
        ShopUIStyle.Anchor(rootRT, anchorMin, anchorMax, pivot, pos, size);
        ShopUIStyle.Skin(frame, skin != null ? skin.feverTrack : null, sliced: true);
        // 트랙 아트가 자체 테두리를 갖고 있어 코드가 그린 청동선을 지운다(이중 테두리 방지).
        if (frame.sprite != null && rootRT.TryGetComponent<Image>(out var outer))
            outer.color = Color.clear;

        var view = rootRT.gameObject.AddComponent<FeverGaugeView>();
        var inner = frame.transform;

        view._label = ShopUIStyle.MakeText(inner, "Label", 16f, FontStyles.Bold,
            TextAlignmentOptions.Left, ShopUIStyle.RarityGlow(ItemRarity.Legendary));
        ShopUIStyle.Anchor(view._label.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(12f, -6f), new Vector2(90f, LabelH));
        view._label.text = "피버";

        view._hint = ShopUIStyle.MakeText(inner, "Hint", 14f, FontStyles.Normal,
            TextAlignmentOptions.Right, ShopUIStyle.TextDim);
        ShopUIStyle.Anchor(view._hint.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(-12f, -6f), new Vector2(size.x - 110f, LabelH));

        // 납품된 「피버0~7」은 <b>전부 405×70으로 크기가 같다</b> — 칸 하나가 아니라
        // 게이지 전체의 단계별 그림이다. 그걸 칸마다 넣으면 405×70이 37×98 칸에 눌려
        // 15배 찌그러진다. 아트가 있으면 <b>한 장</b>을 단계에 따라 갈아끼운다.
        float padX = 12f;
        float barW = size.x - padX * 2f;
        float barH = Mathf.Max(14f, size.y - LabelH - 22f);
        var art0 = skin != null ? skin.FeverCell(0) : null;

        if (art0 != null)
        {
            // 아트 비율을 지켜 트랙 안쪽에 앉힌다. 남는 세로는 위아래로 고르게 나눈다.
            float aspect = art0.rect.width / Mathf.Max(1f, art0.rect.height);
            float w = Mathf.Min(barW, barH * aspect);
            float h = w / aspect;
            view._strip = ShopUIStyle.MakeImage(inner, "Strip", Color.white);
            ShopUIStyle.Anchor(view._strip.rectTransform,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 10f + (barH - h) * 0.5f), new Vector2(w, h));
            view._strip.preserveAspect = true;
        }
        else
        {
            float cellW = (barW - CellGap * (MaxLevel - 1)) / MaxLevel;
            view._cells = new Image[MaxLevel];
            for (int i = 0; i < MaxLevel; i++)
            {
                var cell = ShopUIStyle.MakeImage(inner, $"Cell{i}", EmptyCell);
                ShopUIStyle.Anchor(cell.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
                    new Vector2(padX + i * (cellW + CellGap), 10f), new Vector2(cellW, barH));
                view._cells[i] = cell;
            }
        }

        view.SetLevel(0);
        return view;
    }

    // ── Public Methods ──

    /// <summary>현재 열기 단계를 반영한다. 상한을 넘으면 만렙 표시로 고정한다.</summary>
    public void SetLevel(int level)
    {
        int clamped = Mathf.Clamp(level, 0, MaxLevel);
        if (clamped == _shownLevel) return;
        _shownLevel = clamped;

        var skin = UISkin.Refinery;

        if (_strip != null)
        {
            // 단계 그림 한 장을 통째로 교체한다 — 늘리지 않으므로 비율이 보존된다.
            var sprite = skin != null ? skin.FeverCell(ArtIndex(clamped, skin.feverCell?.Length ?? 0)) : null;
            if (sprite != null) _strip.sprite = sprite;
        }
        else if (_cells != null)
        {
            for (int i = 0; i < _cells.Length; i++)
            {
                var img = _cells[i];
                if (img == null) continue;

                bool lit = i < clamped;
                img.sprite = null;
                img.color  = lit ? HeatSteps[Mathf.Min(i + 1, HeatSteps.Length - 1)] : EmptyCell;
            }
        }

        if (_hint != null)
        {
            // 숫자는 <b>그림에 켜진 칸 수</b>로 센다 — 납품 그림은 7칸인데 「n/9」를 쓰면 켜진 칸과 숫자가 어긋났다(09-27).
            int artCount = _strip != null && skin != null ? (skin.feverCell?.Length ?? 0) : 0;
            int shown = artCount > 1 ? ArtIndex(clamped, artCount) : clamped;
            int total = artCount > 1 ? artCount - 1 : MaxLevel;
            _hint.text = clamped >= MaxLevel
                ? "<color=#FFCB5A>최고조 — 상위 등급 최대</color>"
                : (shown > 0 ? $"{shown}/{total}  확률 상승 중" : $"0/{total}  연속으로 돌리면 오른다");
        }
    }

    // ── Private Methods ──

    /// <summary>
    /// 단계(0~MaxLevel) → 납품 그림 인덱스. 그림은 0~7의 8장인데 단계는 9까지라, 그대로 쓰면 7부터
    /// 꽉 찬 그림이 떠 「최고조」로 읽혔다(09-14에 고친 거짓이 아트 경로에서 되살아남).
    /// 0은 빈 그림, MaxLevel만 꽉 찬 그림, 그 사이는 1~(마지막-1)에 고르게 나눈다.
    /// </summary>
    private static int ArtIndex(int level, int artCount)
    {
        int last = artCount - 1;
        if (level <= 0 || last <= 0) return 0;
        if (level >= MaxLevel) return last;
        return 1 + (level - 1) * (last - 1) / Mathf.Max(1, MaxLevel - 1);
    }
}
