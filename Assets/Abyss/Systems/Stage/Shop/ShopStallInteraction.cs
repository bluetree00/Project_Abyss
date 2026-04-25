using System;
using TMPro;
using UnityEngine;

/// <summary>
/// 상점 진열대 1개의 상호작용 컴포넌트.
/// 플레이어가 트리거 영역 안에서 F키를 누르면 ShopRoomController에 구매 요청.
///
/// BuffTileInteraction과 동일한 월드 프롬프트 방식.
/// 상품 할당(Bind) → 플레이어 진입 → F키 → 구매 요청 → 성공 시 SOLD OUT 표시.
/// </summary>
public class ShopStallInteraction : MonoBehaviour
{
    // ── 상수 ────────────────────────────────────────────────
    private const float PromptOffsetY = 1.4f;

    // ── 비공개 필드 ─────────────────────────────────────────
    private ShopItemSO _item;
    private bool _sold;
    private bool _playerInRange;
    private GameObject _promptGo;
    private TextMeshPro _promptText;

    // ── Properties ──────────────────────────────────────────
    public ShopItemSO Item => _item;
    public bool IsSold => _sold;

    /// <summary>구매 요청 시 호출. bool 반환값은 구매 성공 여부.</summary>
    public event Func<ShopStallInteraction, bool> OnPurchaseRequested;

    // ── Lifecycle ───────────────────────────────────────────

    private void Update()
    {
        if (_promptGo != null && _promptGo.activeSelf && Camera.main != null)
            _promptGo.transform.rotation = Camera.main.transform.rotation;

        if (_sold || !_playerInRange) return;

        if (Input.GetKeyDown(KeyCode.F))
            TryPurchase();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_sold) return;
        if (!IsPlayer(other)) return;

        _playerInRange = true;
        ShowPrompt(true);
    }

    private void OnTriggerExit(Collider other)
    {
        if (!IsPlayer(other)) return;
        _playerInRange = false;
        ShowPrompt(false);
    }

    private void OnDestroy()
    {
        if (_promptGo != null)
            Destroy(_promptGo);
    }

    // ── Public Methods ──────────────────────────────────────

    /// <summary>진열대에 상품을 주입. ShopRoomController가 호출.</summary>
    public void Bind(ShopItemSO item)
    {
        _item = item;
        _sold = false;
        RefreshPromptText();
    }

    /// <summary>구매 성공 처리 (외부에서 강제 마킹할 때 사용).</summary>
    public void MarkSold()
    {
        if (_sold) return;
        _sold = true;
        ShowPrompt(false);
        ApplySoldVisual();
    }

    // ── Private Methods ─────────────────────────────────────

    private void TryPurchase()
    {
        if (_sold || _item == null) return;

        bool success = OnPurchaseRequested?.Invoke(this) ?? false;
        if (success)
            MarkSold();
    }

    private void ApplySoldVisual()
    {
        var renderers = GetComponentsInChildren<Renderer>();
        foreach (var r in renderers)
        {
            var mat = r.material;
            if (mat.HasProperty("_Color"))
            {
                var c = mat.color;
                c.a = 0.3f;
                mat.color = c;
            }
        }
    }

    // ── 월드 프롬프트 ([F] 구매) ────────────────────────────

    private void ShowPrompt(bool show)
    {
        if (show && _promptGo == null)
            CreatePrompt();

        if (_promptGo != null)
            _promptGo.SetActive(show && !_sold);
    }

    private void CreatePrompt()
    {
        _promptGo = new GameObject("ShopStallPrompt");
        _promptGo.transform.SetParent(transform, false);
        _promptGo.transform.localPosition = Vector3.up * PromptOffsetY;

        _promptText = _promptGo.AddComponent<TextMeshPro>();
        _promptText.fontSize = 4f;
        _promptText.alignment = TextAlignmentOptions.Center;
        _promptText.color = Color.white;
        _promptText.enableWordWrapping = false;

        var rect = _promptGo.GetComponent<RectTransform>();
        if (rect != null) rect.sizeDelta = new Vector2(5f, 1.4f);

        TMPOutlineHelper.ApplyDefault(_promptText);

        RefreshPromptText();
        _promptGo.SetActive(false);
    }

    private void RefreshPromptText()
    {
        if (_promptText == null) return;

        if (_item == null || _item.Item == null)
        {
            _promptText.text = "<color=#888888>비어있음</color>";
            return;
        }

        string name = string.IsNullOrEmpty(_item.Item.displayName) ? _item.Item.itemId : _item.Item.displayName;
        _promptText.text =
            $"<color=#FFFFFF>{name}</color>\n" +
            $"<color=#FFCC00>{_item.Price}G</color>\n" +
            $"<color=#FFD700>[F]</color> 구매";
    }

    private static bool IsPlayer(Collider col)
    {
        return col.GetComponentInParent<PlayerController>() != null;
    }
}
