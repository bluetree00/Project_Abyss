using UnityEngine;
using BackEnd;
using LitJson;

public class UserInfo : MonoBehaviour
{
    [System.Serializable]
    public class UserInfoEvent : UnityEngine.Events.UnityEvent {}
    public UserInfoEvent onUserInfoEvent = new UserInfoEvent();

    private static UserInfoData data = new UserInfoData();
    public static UserInfoData Data => data;


    public void GetUserInfoFromBackend()
    {
        Backend.BMember.GetUserInfo(callback =>
        {
            if(callback.IsSuccess())
            {
                try
                {
                    JsonData json = callback.GetReturnValuetoJSON()["row"];
  
                    data.gamerId                     = json["gamerId"].ToString();
                    data.countryCode                 = json["countryCode"]?.ToString();
                    data.nickname                    = json["nickname"]?.ToString();
                    data.inDate                      = json["inDate"].ToString();
                    data.emailForFindPassword        = json["emailForFindPassword"]?.ToString();
                    data.subscriptionType            = json["subscriptionType"].ToString();
                    data.federationId                = json["federationId"]?.ToString();
                    
                }

                catch ( System.Exception e)
                {
                    data.Reset();

                    Debug.LogError(e);
                }
            }
            else
            {
                //유저 정보를 기본으로 사용 
                //오프라인 상태를 대비헤 기본정보를 저장하고 오프라인일떄 불러와서 사용
                data.Reset();
                Debug.LogError(callback.GetMessage());
            }

            onUserInfoEvent.Invoke(); // 유저 정보 이벤트 호출
        });
    }
    
}

public class UserInfoData
{
    public string gamerId; 
    public string countryCode; 
    public string nickname; 
    public string inDate; 
    public string emailForFindPassword;
    public string subscriptionType;
    public string federationId;

    public void Reset()
    {
        gamerId                        = "Offline";
        countryCode                    = "Unknown";
        nickname                       = "Noname";
        inDate                         = string.Empty;
        emailForFindPassword           = string.Empty;
        subscriptionType               = string.Empty;
        federationId                   = string.Empty;
    }
}