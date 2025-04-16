using BackEnd;
using UnityEngine;

public class BackendGameData : MonoBehaviour
{
    private static BackendGameData instance = null;
    public static BackendGameData instanece
    {
        get
        {
            if (instance == null)
            {
                instance = new BackendGameData();
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
            { " level", userGameData.level },
            { "exp", userGameData.experience },
            { "gold", userGameData.gold },
            { "jewel", userGameData.jewel },
            { "heart", userGameData.heart },
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
   
}
