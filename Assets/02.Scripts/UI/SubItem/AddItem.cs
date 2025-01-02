using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AddItem : MonoBehaviour
{
    private void OnTriggerEnter(Collider other) 
    {
        if(other.tag =="Player")
        {
            Managers.UI.InvenPushItem("UI_EquipmentItem", "UI_EquipmentItem");

            Destroy(this.gameObject);
        }
        
    }
}
