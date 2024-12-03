using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class UI_Augment_Item : UI_Base
{
    enum GameObjects
    {
        ItemIcon,
        ItemNameText,
        ItemDescriptionText
    }

    private AugmentData _augmentData;

    public override void Init()
    {
        Bind<GameObject>(typeof(GameObjects));

        // 아이콘 클릭 이벤트 바인딩
        Get<GameObject>((int)GameObjects.ItemIcon).BindEvent(OnClickAugment);
    }

    public void SetInfo(AugmentData augmentData)
    {
        _augmentData = augmentData;

        // UI 업데이트
        Get<GameObject>((int)GameObjects.ItemNameText).GetComponent<Text>().text = _augmentData.Name;
        Get<GameObject>((int)GameObjects.ItemDescriptionText).GetComponent<Text>().text = _augmentData.Description;
        Get<GameObject>((int)GameObjects.ItemIcon).GetComponent<Image>().sprite = _augmentData.Icon;
    }

    private void OnClickAugment(PointerEventData eventData)
    {
        if (_augmentData != null)
        {
            _augmentData.Use(); // 증강 사용
            CloseParentUI();    // 부모 UI 닫기
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
    }
}
