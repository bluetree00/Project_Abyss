using UnityEngine;
using TMPro;

public class TopPanelViewer : MonoBehaviour
{
    [SerializeField]
    private TextMeshProUGUI textNickname; // 닉네임 텍스트


    public void UpdateNickname()
    {
        Debug.Log($"닉네임: {UserInfo.Data.nickname}, 게이머 ID: {UserInfo.Data.gamerId}");

        textNickname.text = UserInfo.Data.nickname == null ?
                            UserInfo.Data.gamerId : UserInfo.Data.nickname;
    }
}
