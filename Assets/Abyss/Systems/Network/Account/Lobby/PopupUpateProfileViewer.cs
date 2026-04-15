using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class PopupUpateProfileViewer : MonoBehaviour
{
    [SerializeField]
    private TextMeshProUGUI textNickname; // 닉네임 텍스트
    [SerializeField]
    private TextMeshProUGUI textGamerId; // 게이머 ID 텍스트

    public void UpdateNickname()
    {

        textNickname.text = UserInfo.Data.nickname == null ?
                            UserInfo.Data.gamerId : UserInfo.Data.nickname;

        textGamerId.text = UserInfo.Data.gamerId;
    }
    
}
