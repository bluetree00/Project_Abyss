using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class LoginBase : MonoBehaviour
{
    [SerializeField]
    private TextMeshProUGUI textMessage; // 메시지 텍스트

    /// <summary>
    /// UI 초기화
    /// </summary>
    /// <param name="images"></param>
    protected void ResetUI(params Image[] images)
    {
        textMessage.text = string.Empty; // 메시지 초기화

        for (int i = 0; i < images.Length; i++)
        {
            images[i].color = Color.white; // 이미지 색상 초기화
        }
    }

    /// <summary>
    /// 메시지 설정
    /// </summary>
    /// <param name="message"></param>
    protected void SetMessage(string message)
    {
        textMessage.text = message; // 메시지 설정
    }

    protected void GuideForIncorrectlyEnteredData(Image image, string message)
    {
        textMessage.text = message; // 메시지 설정
        image.color = Color.red; // 이미지 색상 변경
    }

    /// <summary>
    /// 필드 데이터가 비어있는지 확인
    /// </summary>
    /// <param name="image"></param>
    /// <param name="fieId"></param>
    /// <param name="result"></param>
    /// <returns></returns>
    protected bool IsFieldDataEmpty(Image image, string fieId, string result)
    {
        if ( fieId.Trim().Equals(""))
        {
            GuideForIncorrectlyEnteredData(image, $"\"{result}\"필드를 채워주세요.");

            return true; // 필드 데이터가 비어있음
        }

        return false; // 필드 데이터가 비어있지 않음
    }
    
}
