using BackEnd;
using UnityEngine;

public class LoginSample : MonoBehaviour
{
    private void Awake()
    {
        string ID = "testID"; // 사용자 ID
        string PW = "testPW"; // 사용자 비밀번호
        string email = "testEmail"; // 사용자 이메일
        string nickname = "testNickname"; // 사용자 닉네임

        Backend.BMember.CustomSignUp(ID,PW);

        Backend.BMember.UpdateCustomEmail(email);
        
        Backend.BMember.CustomLogin(ID, PW, (login) =>
        {
            if (login.IsSuccess())
            {
                Debug.Log("Login Success: " + login.GetMessage());
            }
            else
            {
                Debug.LogError("Login Failed: " + login.GetMessage());
            }
        });

        Backend.BMember.FindCustomID(email);

        Backend.BMember.ResetPassword(ID, email);

        Backend.BMember.CreateNickname(nickname);

        Backend.BMember.UpdateNickname(nickname);
    }
}
