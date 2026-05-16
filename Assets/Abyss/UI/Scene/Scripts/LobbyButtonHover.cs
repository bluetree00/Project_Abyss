using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 로비 버튼 hover 이미지 스왑 컴포넌트.
/// Normal 상태에서 offSprite, 마우스 진입 시 onSprite로 교체한다.
/// _targetImage가 지정되면 자신이 아닌 외부 Image를 제어한다.
/// </summary>
public class LobbyButtonHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    // ─────────────────────────────────────────────────────────
    // SerializeField
    // ─────────────────────────────────────────────────────────

    [Header("Hover 이미지")]
    [SerializeField] private Sprite _offSprite;
    [SerializeField] private Sprite _onSprite;

    [Header("외부 대상 (null이면 자신 Image 사용)")]
    [SerializeField] private Image _targetImage;

    // ─────────────────────────────────────────────────────────
    // Private
    // ─────────────────────────────────────────────────────────

    private Image _image;

    // ─────────────────────────────────────────────────────────
    // Lifecycle
    // ─────────────────────────────────────────────────────────

    private void Awake()
    {
        _image = _targetImage != null ? _targetImage : GetComponent<Image>();
        if (_offSprite != null && _image != null)
            _image.sprite = _offSprite;
    }

    // ─────────────────────────────────────────────────────────
    // Public Methods (IPointerEnterHandler, IPointerExitHandler)
    // ─────────────────────────────────────────────────────────

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (_onSprite != null && _image != null)
            _image.sprite = _onSprite;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (_offSprite != null && _image != null)
            _image.sprite = _offSprite;
    }
}
