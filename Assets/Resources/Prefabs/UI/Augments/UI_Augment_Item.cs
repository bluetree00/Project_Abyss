using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;  // TextMeshPro namespace 추가

public class UI_Augment_Item : UI_Base
{
    enum GameObjects
    {
        AugmentIcon,
        AugmentNameText,
        AugmentDescriptionText
    }

    private AugmentData _augmentData;  // AugmentData를 사용

    public override void Init()
    {
        Bind<GameObject>(typeof(GameObjects));

        // 아이콘 클릭 이벤트 바인딩
        Get<GameObject>((int)GameObjects.AugmentIcon).BindEvent(OnClickAugment);
    }

    // 이제 SetInfo는 AugmentComponent가 아닌 AugmentData를 받습니다
    public void SetInfo(AugmentData augmentData)
    {
        _augmentData = augmentData;

        // UI 업데이트 - TextMeshProUGUI 사용
        if (_augmentData != null)
        {
            //Get<GameObject>((int)GameObjects.AugmentNameText).GetComponent<TextMeshProUGUI>().text = _augmentData.Name;
            //Get<GameObject>((int)GameObjects.AugmentDescriptionText).GetComponent<TextMeshProUGUI>().text = _augmentData.Description;
           // Get<GameObject>((int)GameObjects.AugmentIcon).GetComponent<Image>().sprite = _augmentData.Icon;
        }
    }

    private void OnClickAugment(PointerEventData eventData)
    {
        if (_augmentData != null)
        {
            _augmentData.UseAugment(gameObject);  // AugmentData의 Use 호출
            CloseParentUI();  // 부모 UI 닫기
        }
        else
        {
            Debug.LogWarning("증강 데이터가 설정되지 않았습니다.");
        }
    }

    private void CloseParentUI()
    {
        // 부모 UI 닫기 로직 추가
        Debug.Log("부모 UI 닫기");
        Managers.UI.ClosePopupUI();  // UI 관리자에서 UI 클리어
    }
}
