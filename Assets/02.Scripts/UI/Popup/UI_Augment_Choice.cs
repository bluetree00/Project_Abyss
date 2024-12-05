using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class UI_Augment_Choice : UI_Popup
{
    enum GameObjects
    {
        GridPanel
    }

    private List<AugmentData> availableAugments; // 사용 가능한 증강 데이터 리스트

    public void InitAugments(List<AugmentData> availableAugments)
    {
        if (availableAugments == null || availableAugments.Count == 0)
        {
            // 랜덤 증강 생성
            List<string> augmentNames = new List<string>
            {
                AugmentSelector.GetRandomAugmentName(),
                AugmentSelector.GetRandomAugmentName(),
                AugmentSelector.GetRandomAugmentName()
            };

            // 증강 이름을 바탕으로 AugmentData를 로드하여 리스트에 추가
            this.availableAugments = new List<AugmentData>();

            foreach (string augmentName in augmentNames)
            {
               
                AugmentData augmentData = Managers.Resource.Load<AugmentData>($"Prefabs/UI/Augments/{augmentName}");

                if (augmentData != null)
                {
                    this.availableAugments.Add(augmentData);
                }
            }
        }
        else
        {
            // 외부에서 제공된 증강 리스트 사용
            this.availableAugments = availableAugments;
        }
    }

    public override void Init()
    {
        base.Init();
    }

    public void ShowAugmentChoices()
    {
        Bind<GameObject>(typeof(GameObjects));
        GameObject gridPanel = Get<GameObject>((int)GameObjects.GridPanel);

        // 기존 자식 객체들 삭제
        foreach (Transform child in gridPanel.transform)
            Managers.Resource.Destroy(child.gameObject);

        // availableAugments 기반으로 UI 생성
        if (availableAugments != null && availableAugments.Count > 0)
        {
            foreach (AugmentData augment in availableAugments)
            {
                // 증강 데이터를 바탕으로 UI 항목 생성
                GameObject augments = Managers.UI.MakeAugment<UI_Augment_Item>(gridPanel.transform).gameObject;
                UI_Augment_Item augmentItem = augments.GetOrAddComponent<UI_Augment_Item>();
                augmentItem.SetInfo(augment); // 증강 데이터 설정
            }
        }
        else
        {
            Debug.LogWarning("No available augments to display.");
        }
    }

}
