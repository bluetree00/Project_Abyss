using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.VisualScripting;
using BackEnd;

public class TopPanelViewer : MonoBehaviour
{
    [SerializeField]
    private TextMeshProUGUI textNickname; // 닉네임 텍스트

    [SerializeField]
    private TextMeshProUGUI textLevel; // 레벨 텍스트
    [SerializeField]
    private Slider sliderExperience; // 경험치 슬라이더
    [SerializeField]
    private TextMeshProUGUI textHeart; // 하트 텍스트
    [SerializeField]
    private TextMeshProUGUI textGold; // 골드 텍스트
    [SerializeField]
    private TextMeshProUGUI textJewel; // 보석 텍스트

    private void Awake()
    {
        BackendGameData.Instance.ongameDataLoadEvent.AddListener(UpdateGameData);
    }

    public void UpdateNickname()
    {
        Debug.Log($"닉네임: {UserInfo.Data.nickname}, 게이머 ID: {UserInfo.Data.gamerId}");

        textNickname.text = UserInfo.Data.nickname == null ?
                            UserInfo.Data.gamerId : UserInfo.Data.nickname;
    }

    public void UpdateGameData()
    {
        textLevel.text = $"{BackendGameData.Instance.UsergameData.level}";
        sliderExperience.value = BackendGameData.Instance.UsergameData.experience / 100f; // 경험치 슬라이더 값 설정
        textHeart.text = $"{BackendGameData.Instance.UsergameData.heart} /30";
        textGold.text = $"{BackendGameData.Instance.UsergameData.gold}";
        textJewel.text = $"{BackendGameData.Instance.UsergameData.jewel}";

    }
}
