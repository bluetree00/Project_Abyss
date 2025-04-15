using BackEnd;
using UnityEngine;

public class LoginSample : MonoBehaviour
{
    private void Awake()
    {
        string ID = "user01"; // 사용자 ID
        string PW = "1234"; // 사용자 비밀번호
        string email = "user01@gmail.com"; // 사용자 이메일    
        string nickname = "첫번째유저"; // 사용자 닉네임

        Backend.BMember.CustomSignUp(ID,PW);

        Backend.BMember.UpdateCustomEmail(email);
        
        Backend.BMember.CustomLogin(ID, PW);


        Backend.BMember.FindCustomID(email);

        Backend.BMember.ResetPassword(ID, email);

        Backend.BMember.CreateNickname(nickname);

        Backend.BMember.UpdateNickname(nickname);
    }
}
