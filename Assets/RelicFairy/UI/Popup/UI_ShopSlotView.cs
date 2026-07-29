using System;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 상점 UI 슬롯 카드 1칸. UI_ShopPanel이 절차적으로 생성한다.
///
/// 비주얼: 등급 위계(테두리/리본/후광 색), 아이콘 프레임, 이름(등급색), 골드 코인+가격,
///         구매 버튼(브론즈), 품절/보유 스탬프 오버레이.
/// UX: hover 확대, 구매 성공 팝(scale punch), 골드부족/거부 시 흔들림+빨강 플래시.
/// 스프라이트/사운드는 ShopUIStyle 훅으로 나중에 교체 가능.
/// </summary>
public sealed class UI_ShopSlotView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private const float HoverScale = 1.045f;

    private int _index;
    private Action<int> _onBuy;
    private bool _busy;
    private bool _interactable;
    private bool _lastAffordable;
    private Color _baseBorder = ShopUIStyle.CardBorder;

    private Image _border;        // 카드 테두리(등급색)
    private RectTransform _body;  // 애니메이션 대상(hover/pop/shake)
    private Image _ribbon;
    private TMP_Text _ribbonLabel;
    private Image _iconGlow;
    private Image _icon;
    private TMP_Text _nameText;
    private TMP_Text _priceText;
    private Image _buyBg;
    private Button _buyButton;
    private TMP_Text _buyLabel;
    private GameObject _overlay;
    private TMP_Text _stamp;

    // ── 생성 ────────────────────────────────────────────────

    public static UI_ShopSlotView Create(Transform parent, Vector2 cellSize)
    {
        // Root = 등급 테두리 + hover 레이캐스트
        var border = ShopUIStyle.MakeImage(parent, "ShopCard", ShopUIStyle.CardBorder, raycast: true);
        var view = border.gameObject.AddComponent<UI_ShopSlotView>();
        view._border = border;
        view.BuildChildren(border.transform, cellSize);
        return view;
    }

    private void BuildChildren(Transform root, Vector2 cellSize)
    {
        // Body (테두리 4px 노출 위해 약간 인셋, 중앙 고정 → shake/scale 가능)
        var bodyImg = ShopUIStyle.MakeImage(root, "Body", ShopUIStyle.CardFill, raycast: true);
        _body = bodyImg.rectTransform;
        ShopUIStyle.Anchor(_body, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                           Vector2.zero, cellSize - new Vector2(8f, 8f));

        var vlg = _body.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(10, 10, 0, 12);
        vlg.spacing = 7f;
        vlg.childControlWidth = true;
        // 자식 높이를 LayoutElement.preferredHeight(Ribbon24/IconFrame116/Name44/Price28/Buy40)로
        // 결정적으로 제어한다. (false면 자식의 sizeDelta.y를 써 의도한 높이가 적용되지 않음)
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childAlignment = TextAnchor.UpperCenter;

        // 1) 등급 리본 (상단 풀폭 밴드)
        _ribbon = ShopUIStyle.MakeImage(_body, "Ribbon", ShopUIStyle.CardBorder);
        AddLayout(_ribbon.gameObject, 24f);
        _ribbonLabel = ShopUIStyle.MakeText(_ribbon.transform, "Label", 13f, FontStyles.Bold,
                                            TextAlignmentOptions.Center, new Color(0.08f, 0.07f, 0.1f, 1f));
        ShopUIStyle.Stretch(_ribbonLabel.rectTransform);

        // 2) 아이콘 프레임 (후광 + 아이콘)
        var iconFrame = ShopUIStyle.MakeImage(_body, "IconFrame", ShopUIStyle.IconBg);
        AddLayout(iconFrame.gameObject, 116f);
        _iconGlow = ShopUIStyle.MakeImage(iconFrame.transform, "Glow", new Color(1, 1, 1, 0f));
        ShopUIStyle.Stretch(_iconGlow.rectTransform, 10f);
        _icon = ShopUIStyle.MakeImage(iconFrame.transform, "Icon", Color.white);
        _icon.preserveAspect = true;
        ShopUIStyle.Stretch(_icon.rectTransform, 18f);

        // 3) 이름
        _nameText = ShopUIStyle.MakeText(_body, "Name", 19f, FontStyles.Bold,
                                         TextAlignmentOptions.Center, ShopUIStyle.TextPrimary);
        AddLayout(_nameText.gameObject, 44f);
        _nameText.textWrappingMode = TextWrappingModes.Normal;

        // 4) 가격 행 (코인 + 가격)
        var priceRow = ShopUIStyle.MakeRect(_body, "PriceRow", typeof(HorizontalLayoutGroup));
        AddLayout(priceRow, 28f);
        var hlg = priceRow.GetComponent<HorizontalLayoutGroup>();
        hlg.spacing = 6f;
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = true; hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = false;
        ShopUIStyle.MakeCoin(priceRow.transform, 20f);
        _priceText = ShopUIStyle.MakeText(priceRow.transform, "Price", 20f, FontStyles.Bold,
                                          TextAlignmentOptions.MidlineLeft, ShopUIStyle.Gold);

        // 5) 구매 버튼
        var buyGo = ShopUIStyle.MakeRect(_body, "BuyButton", typeof(Image), typeof(Button));
        AddLayout(buyGo, 40f);
        _buyBg = buyGo.GetComponent<Image>();
        _buyBg.color = ShopUIStyle.BuyFill;
        _buyButton = buyGo.GetComponent<Button>();
        var cb = _buyButton.colors;
        cb.normalColor = ShopUIStyle.BuyFill;
        cb.highlightedColor = ShopUIStyle.BuyHover;
        cb.pressedColor = ShopUIStyle.BuyHover;
        cb.disabledColor = ShopUIStyle.BuyDisabled;
        cb.fadeDuration = 0.08f;
        _buyButton.colors = cb;
        _buyButton.onClick.AddListener(() => _onBuy?.Invoke(_index));
        _buyLabel = ShopUIStyle.MakeText(buyGo.transform, "Label", 17f, FontStyles.Bold,
                                         TextAlignmentOptions.Center, ShopUIStyle.Gold);
        _buyLabel.text = "구매";
        ShopUIStyle.Stretch(_buyLabel.rectTransform);

        // 6) 품절/보유 오버레이 (레이아웃 제외, Body 전체 덮음)
        _overlay = ShopUIStyle.MakeImage(_body, "Overlay", ShopUIStyle.SoldVeil, raycast: false).gameObject;
        var ole = _overlay.AddComponent<LayoutElement>();
        ole.ignoreLayout = true;
        ShopUIStyle.Stretch(_overlay.GetComponent<RectTransform>());
        _stamp = ShopUIStyle.MakeText(_overlay.transform, "Stamp", 30f, FontStyles.Bold,
                                      TextAlignmentOptions.Center, ShopUIStyle.SoldStamp);
        ShopUIStyle.Stretch(_stamp.rectTransform);
        _stamp.rectTransform.localRotation = Quaternion.Euler(0, 0, 12f);
        _overlay.SetActive(false);
    }

    private static void AddLayout(GameObject go, float preferredHeight)
    {
        var le = go.GetComponent<LayoutElement>();
        if (le == null) le = go.AddComponent<LayoutElement>();
        le.preferredHeight = preferredHeight;
        le.minHeight = preferredHeight;
    }

    // ── 바인딩/갱신 ─────────────────────────────────────────

    public void Bind(ShopSlot slot, int index, Action<int> onBuy)
    {
        _index = index;
        _onBuy = onBuy;
    }

    public void Refresh(ShopSlot slot, bool affordable)
    {
        if (slot == null) return;
        bool hasGoods = slot.Entry != null || slot.LegacyItem != null || slot.Service.HasValue;
        _lastAffordable = affordable;

        // 등급 위계: 테두리/리본/후광/이름색
        Color rarity = hasGoods ? ShopUIStyle.Rarity(slot.Rarity) : ShopUIStyle.CardBorder;
        _baseBorder = rarity;
        if (_border != null) _border.color = rarity;
        if (_ribbon != null) _ribbon.color = rarity;
        if (_ribbonLabel != null)
        {
            _ribbonLabel.text = hasGoods ? ShopUIStyle.RarityLabel(slot.Rarity) : "";
            _ribbon.gameObject.SetActive(hasGoods);
        }
        if (_iconGlow != null) _iconGlow.color = hasGoods ? ShopUIStyle.RarityGlow(slot.Rarity) : new Color(1, 1, 1, 0f);

        if (_icon != null)
        {
            _icon.sprite = slot.Icon;
            _icon.enabled = slot.Icon != null;
        }

        if (_nameText != null)
        {
            _nameText.text = slot.DisplayName;
            _nameText.color = hasGoods ? rarity : ShopUIStyle.TextDim;
        }

        if (_priceText != null)
        {
            _priceText.text = hasGoods ? slot.Price.ToString() : "-";
            _priceText.color = affordable ? ShopUIStyle.Gold : ShopUIStyle.RejectRed;
        }

        _interactable = slot.Purchasable;
        if (_buyButton != null) _buyButton.interactable = _interactable;
        if (_buyLabel != null) _buyLabel.color = _interactable ? ShopUIStyle.Gold : ShopUIStyle.TextDim;

        bool blocked = slot.Sold || slot.Owned || slot.Pending || slot.Locked;
        if (_overlay != null)
        {
            _overlay.SetActive(blocked);
            if (blocked && _stamp != null)
            {
                if (slot.Sold) { _stamp.text = "SOLD"; _stamp.color = ShopUIStyle.SoldStamp; }
                else if (slot.Owned) { _stamp.text = "보유 중"; _stamp.color = ShopUIStyle.OwnedStamp; }
                else if (slot.Locked) { _stamp.text = "준비 중"; _stamp.color = ShopUIStyle.TextDim; }
                else { _stamp.text = "구매 중…"; _stamp.color = ShopUIStyle.TextPrimary; }
            }
        }
    }

    // ── 피드백 ──────────────────────────────────────────────

    public void PlayPurchasePop()
    {
        ShopUIStyle.PlaySfx("shop_buy");
        PopAsync().Forget();
    }

    public void PlayRejectShake()
    {
        ShopUIStyle.PlaySfx("shop_reject");
        ShakeAsync().Forget();
    }

    private async UniTaskVoid PopAsync()
    {
        if (_body == null) return;
        _busy = true;
        float t = 0f;
        try
        {
            while (t < 1f)
            {
                t = Mathf.Min(t + Time.unscaledDeltaTime / 0.20f, 1f);
                float s = 1f + 0.13f * Mathf.Sin(t * Mathf.PI);
                _body.localScale = Vector3.one * s;
                await UniTask.Yield(PlayerLoopTiming.Update, destroyCancellationToken);
            }
        }
        catch (OperationCanceledException) { return; }
        _busy = false;
        _body.localScale = Vector3.one;
    }

    private async UniTaskVoid ShakeAsync()
    {
        if (_body == null) return;
        if (_priceText != null) _priceText.color = ShopUIStyle.RejectRed;
        const float dur = 0.34f;
        float t = 0f;
        try
        {
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                float damp = 1f - (t / dur);
                _body.anchoredPosition = new Vector2(Mathf.Sin(t * 60f) * 11f * damp, 0f);
                await UniTask.Yield(PlayerLoopTiming.Update, destroyCancellationToken);
            }
        }
        catch (OperationCanceledException) { return; }
        _body.anchoredPosition = Vector2.zero;
        if (_priceText != null)
            _priceText.color = _lastAffordable ? ShopUIStyle.Gold : ShopUIStyle.RejectRed;
    }

    // ── Hover ───────────────────────────────────────────────

    public void OnPointerEnter(PointerEventData e)
    {
        if (_body == null || _busy) return;
        _body.localScale = Vector3.one * HoverScale;
        if (_border != null)
            _border.color = Color.Lerp(_baseBorder, Color.white, 0.22f);
    }

    public void OnPointerExit(PointerEventData e)
    {
        if (_border != null) _border.color = _baseBorder;
        if (_body == null || _busy) return;
        _body.localScale = Vector3.one;
    }
}
