using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class UI_Augment_Choice : UI_Popup
{
    enum GameObjects
    {
        GridPanel
    }

    private List<AugmentData> availableAugments; // Declare availableAugments here
    public void InitAugments(List<AugmentData> availableAugments, System.Action<AugmentData> onAugmentSelected)
    {
        if (availableAugments == null)
        {
            // 랜덤 증강 생성
            this.availableAugments = new List<AugmentData>
            {
                AugmentSelector.GetRandomAugment(),
                AugmentSelector.GetRandomAugment(),
                AugmentSelector.GetRandomAugment()
            };
        }
        else
        {
            // 외부에서 제공한 증강 리스트 사용
            this.availableAugments = availableAugments;
        }
    }

    public override void Init()
    {
        base.Init();
        Bind<GameObject>(typeof(GameObjects));
    }

    public void ShowAugmentChoices()
    {
        GameObject gridPanel = Get<GameObject>((int)GameObjects.GridPanel);

        // 기존 자식 제거
        foreach (Transform child in gridPanel.transform)
        {
            Managers.Resource.Destroy(child.gameObject);
        }

        // 3개의 증강을 선택
        for (int i = 0; i < 3; i++)
        {
            AugmentData randomAugment = AugmentSelector.GetRandomAugment();

            if (randomAugment != null)
            {
                GameObject item = Managers.UI.MakeSubItem<UI_Augment_Item>(gridPanel.transform).gameObject;
                UI_Augment_Item augmentItem = item.GetOrAddComponent<UI_Augment_Item>();
                augmentItem.SetInfo(randomAugment);
            }
            else
            {
                Debug.LogWarning("증강 로드 실패!");
            }
        }
    }
}
