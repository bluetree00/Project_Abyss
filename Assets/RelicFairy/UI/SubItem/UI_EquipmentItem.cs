using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class UI_EquipmentItem : UI_Inven_Item
{
    public override void SetInfo(string name)
    {
        base.SetInfo(name);

        Debug.Log($"{_name} 무기 아이템 설정 완료!");
    }

    public override void Init()
    {
        base.Init();

        // 무기 아이템 클릭 시 장착 또는 공격력 상승 등의 행동을 추가할 수 있습니다.
        Get<GameObject>((int)GameObjects.ItemIcon)
            .BindEvent((PointerEventData) => { Debug.Log($"무기 아이템 클릭: {_name}"); });
    }
}
