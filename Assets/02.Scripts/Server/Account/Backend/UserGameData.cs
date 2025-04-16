[System.Serializable]
public class UserGameData
{
    public int level; //로비 씬에 보이는 플레이어 레벨
    public float experience; //로비 씬에 보이는 플레이어 경험치
    public int gold; //로비 씬에 보이는 무료 재화
    public int jewel; //로비 씬에 보이는 유료 재화
    public int heart; //로비 씬에 보이는 게임 플레이에 소모되는 재화

    public void Reset()
    {
        level = 1;
        experience = 0;
        gold = 0;
        jewel = 0;
        heart = 30;
    }
    
}
