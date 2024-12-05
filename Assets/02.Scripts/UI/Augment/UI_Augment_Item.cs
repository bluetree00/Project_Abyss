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
        //Bind<GameObject>(typeof(GameObjects));

        // 아이콘 클릭 이벤트 바인딩
        Get<GameObject>((int)GameObjects.AugmentIcon).BindEvent(OnClickAugment);
        
    }

    // 이제 SetInfo는 AugmentComponent가 아닌 AugmentData를 받습니다
    public void SetInfo(AugmentData augmentData)
    {
        _augmentData = augmentData;
        Bind<GameObject>(typeof(GameObjects));

        if (_augmentData != null)
        {
            // UI 업데이트
            Get<GameObject>((int)GameObjects.AugmentNameText).GetComponent<TextMeshProUGUI>().text = _augmentData.Name;
            Get<GameObject>((int)GameObjects.AugmentDescriptionText).GetComponent<TextMeshProUGUI>().text = _augmentData.Description;
            Get<GameObject>((int)GameObjects.AugmentIcon).GetComponent<Image>().sprite = _augmentData.Icon;
        }
        
    }


    private void OnClickAugment(PointerEventData eventData)
    {
        if (_augmentData != null)
        {
            _augmentData.UseAugment(gameObject);  // AugmentData의 Use 호출
        }
        else
        {
            Debug.LogWarning("증강 데이터가 설정되지 않았습니다.");
        }

         // UI_Augment_Choice 오브젝트 삭제
        Transform augmentChoiceTransform = transform.parent;  // UI_Augment_Item의 부모 (UI_Augment_Choice)
        if (augmentChoiceTransform != null)
        {
            Destroy(augmentChoiceTransform.gameObject);  // 부모 오브젝트(UI_Augment_Choice) 삭제
        }
    }

}
