using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using TMPro;

public class UITextInterAction : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    [System.Serializable]
    private class OnClickEvent : UnityEvent{ }

    [SerializeField]
    private OnClickEvent onClickEvent; // 클릭 이벤트

    private TextMeshProUGUI text; // 텍스트 컴포넌트

    private void Awake()
    {
        text = GetComponent<TextMeshProUGUI>(); // 텍스트 컴포넌트 가져오기
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        text.fontStyle = FontStyles.Bold; // 마우스 오버 시 밑줄 스타일 적용
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        text.fontStyle = FontStyles.Normal; // 마우스 아웃 시 일반 스타일로 변경
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        onClickEvent?.Invoke(); // 클릭 시 이벤트 호출
    }

}
