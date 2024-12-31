using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class UI_Inven_Item : UI_Base
{
    enum GameObjects
    {
        ItemIcon,
        ItemNameText,
        ItemQuantityText
    }

    private ItemData _itemData;

    public override void Init()
    {
        Bind<GameObject>(typeof(GameObjects));

        if (_itemData != null)
        {
            Get<GameObject>((int)GameObjects.ItemNameText)
                .GetComponent<TextMeshProUGUI>().text = _itemData.Name;

            Get<GameObject>((int)GameObjects.ItemIcon)
                .GetComponent<Image>().sprite = _itemData.Icon;

            Get<GameObject>((int)GameObjects.ItemQuantityText)
                .GetComponent<TextMeshProUGUI>().text = $"x{_itemData.Quantity}";

            Get<GameObject>((int)GameObjects.ItemIcon)
                .BindEvent((PointerEventData) => Debug.Log($"아이템 클릭: {_itemData.Name}"));
        }
    }

    public void SetInfo(ItemData itemData)
    {
        _itemData = itemData;
    }
}
