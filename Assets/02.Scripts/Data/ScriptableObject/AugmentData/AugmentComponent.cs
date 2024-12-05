using UnityEngine;

public class AugmentComponent : MonoBehaviour
{
    // 단일 증강 데이터
    public AugmentData augmentData;

    // 증강을 가져오는 메서드
    public AugmentData GetAugment()
    {
        return augmentData;  // 증강 데이터 반환
    }

    // UI에서 증강 데이터를 설정할 때 사용
    public void SetAugmentInfo(UI_Augment_Item uiAugmentItem)
    {
        if (augmentData != null)
        {
            uiAugmentItem.SetInfo(augmentData);  // UI에 증강 데이터 전달
        }
        else
        {
            Debug.LogWarning("증강 데이터가 설정되지 않았습니다.");
        }
    }
}
