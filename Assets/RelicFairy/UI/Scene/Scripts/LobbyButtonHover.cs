using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 로비 버튼 hover 이미지 스왑 컴포넌트.
/// Normal 상태에서 offSprite, 마우스 진입 시 onSprite로 교체한다.
/// _targetImage가 지정되면 자신이 아닌 외부 Image를 제어한다.
/// _onPositionOffset: on 스프라이트가 off와 캔버스 크기가 달라 위치가 어긋날 때 보정값 (예: x=-10 → 왼쪽으로 10px)
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

    [Header("On 상태 위치 보정 (x 음수 = 왼쪽)")]
    [SerializeField] private Vector2 _onPositionOffset;

    // ─────────────────────────────────────────────────────────
    // Private
    // ─────────────────────────────────────────────────────────

    private Image _image;
    private RectTransform _rectTransform;
    private Vector2 _baseAnchoredPosition;

    // ─────────────────────────────────────────────────────────
    // Lifecycle
    // ─────────────────────────────────────────────────────────

    private void Awake()
    {
        _image = _targetImage != null ? _targetImage : GetComponent<Image>();
        _rectTransform = GetComponent<RectTransform>();
        _baseAnchoredPosition = _rectTransform.anchoredPosition;
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
        if (_onPositionOffset != Vector2.zero)
            _rectTransform.anchoredPosition = _baseAnchoredPosition + _onPositionOffset;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (_offSprite != null && _image != null)
            _image.sprite = _offSprite;
        if (_onPositionOffset != Vector2.zero)
            _rectTransform.anchoredPosition = _baseAnchoredPosition;
    }
}
