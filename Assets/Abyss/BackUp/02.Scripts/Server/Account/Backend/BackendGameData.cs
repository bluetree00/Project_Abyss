using BackEnd;
using UnityEngine;

public class BackendGameData : MonoBehaviour
{
    [System.Serializable]
    public class GameDataLoadEvent : UnityEngine.Events.UnityEvent { }
    public GameDataLoadEvent ongameDataLoadEvent = new GameDataLoadEvent();

    private static BackendGameData instance;
    public static BackendGameData Instance
    {
        get
        {
            if (instance == null)
            {
                instance = FindObjectOfType<BackendGameData>();
                if (instance == null)
                {
                    GameObject obj = new GameObject("BackendGameData");
                    instance = obj.AddComponent<BackendGameData>();
                    DontDestroyOnLoad(obj); // 씬 전환 시에도 유지
                }
            }
            return instance;
        }
    }


    private UserGameData userGameData = new UserGameData();
    public UserGameData UsergameData => userGameData;

    private string gameDataRowInData = string.Empty;

    /// <summary>
    /// 서버의 테이블에 새로운 유저 정보 추가
    /// </summary>
    public void GameDataInsert()
    {

        // 유저 정보를 초기값으로 설정
        userGameData.Reset();

        //테이블에 추가할 데이터로 가공
        Param param = new Param()
        {
            {"level", userGameData.level },
            {"exp", userGameData.experience },
            {"gold", userGameData.gold },
            {"jewel", userGameData.jewel },
            {"heart", userGameData.heart },
        };

        //첫번쨰 매개변수는 서버의 콘솔의 "게임 정보 관리" 탭에 생성한 테이블 이름
        Backend.GameData.Insert("USER_DATA", param, callback =>
        {
            if (callback.IsSuccess())
            {
                gameDataRowInData = callback.GetInDate();

                Debug.Log("게임 데이터 추가 성공" + callback.GetInDate());
            }
            else
            {
                Debug.Log("게임 데이터 추가 실패" + callback.GetMessage());
            }
        });

    }
   
    //서버 테이블에서 유저 정보를 불러올떄 호출
    public void GameDataLoad()
    {
        Backend.GameData.GetMyData("USER_DATA", new Where(), callback =>
        {
            if (callback.IsSuccess())
            {
                Debug.Log($"게임 정보 데이터 불러오기 성공 : {callback}");
                
                try
                {
                    LitJson.JsonData gameDataJson = callback.FlattenRows();

                    if( gameDataJson.Count <= 0)
                    {
                        Debug.Log("게임 데이터가 없습니다.");
                    
                    }
                    else
                    {
                        gameDataRowInData = gameDataJson[0]["inDate"].ToString();
                        //불러온 게임 정보를 userGameData에 저장
                        userGameData.level = int.Parse(gameDataJson[0]["level"].ToString());
                        userGameData.experience = int.Parse(gameDataJson[0]["exp"].ToString());
                        userGameData.gold = int.Parse(gameDataJson[0]["gold"].ToString());
                        userGameData.jewel = int.Parse(gameDataJson[0]["jewel"].ToString());
                        userGameData.heart = int.Parse(gameDataJson[0]["heart"].ToString());

                        ongameDataLoadEvent?.Invoke();
                    }
                }
                catch (System.Exception e)
                {
                    userGameData.Reset();
                    Debug.LogError($"게임 데이터 불러오기 실패 : {e}");
                }
            }
            else
            {
                Debug.Log("게임 데이터 불러오기 실패" + callback.GetMessage());
            }
        });

    }
}
