using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class UI_Inven_Item : UI_Base
{
    public enum GameObjects
    {
        ItemIcon,
        ItemNameText,
    }

    protected string _name;
    protected int _quantity;

    public override void Init()
    {
        Bind<GameObject>(typeof(GameObjects));

        // 이름과 수량을 UI에 설정
        Get<GameObject>((int)GameObjects.ItemNameText)
            .GetComponent<TextMeshProUGUI>().text = _name;

        // 아이템 아이콘 클릭 이벤트
        Get<GameObject>((int)GameObjects.ItemIcon)
            .BindEvent((PointerEventData) => { Debug.Log($"아이템 클릭! {_name}"); });
    }

    public virtual void SetInfo(string name)
    {
        _name = name;

    }
}
