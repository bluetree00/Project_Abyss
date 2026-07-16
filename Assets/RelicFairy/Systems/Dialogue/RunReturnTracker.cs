using UnityEngine;

/// <summary>
/// 런 종료(사망/클리어) 사유·누적 횟수를 PlayerPrefs로 기록하고 BaseCamp 진입 시 소비한다.
/// 복귀 대사(GetCountLines)의 랜덤 풀/마일스톤 선택에 쓰인다. (기존 방문 대사 GetVisitLines와 동일한 PlayerPrefs 관례.)
/// </summary>
public static class RunReturnTracker
{
    private const string ReasonKey     = "run_return_reason";
    private const string DeathCountKey = "run_death_count";
    private const string ClearCountKey = "run_clear_count";

    public enum Reason { None = 0, Death = 1, Clear = 2 }

    /// <summary>런 종료 시 호출 — 해당 카운트 +1, 복귀 사유 기록.</summary>
    public static void RecordRunEnd(bool isCleared)
    {
        if (isCleared)
        {
            PlayerPrefs.SetInt(ClearCountKey, PlayerPrefs.GetInt(ClearCountKey, 0) + 1);
            PlayerPrefs.SetInt(ReasonKey, (int)Reason.Clear);
        }
        else
        {
            PlayerPrefs.SetInt(DeathCountKey, PlayerPrefs.GetInt(DeathCountKey, 0) + 1);
            PlayerPrefs.SetInt(ReasonKey, (int)Reason.Death);
        }
        PlayerPrefs.Save();
    }

    /// <summary>BaseCamp 진입 시 호출 — 사유를 읽고 1회성 소비. count=해당 사유의 누적 횟수(없으면 0).</summary>
    public static Reason ConsumeReason(out int count)
    {
        var reason = (Reason)PlayerPrefs.GetInt(ReasonKey, 0);
        PlayerPrefs.SetInt(ReasonKey, (int)Reason.None);
        count = reason switch
        {
            Reason.Death => PlayerPrefs.GetInt(DeathCountKey, 1),
            Reason.Clear => PlayerPrefs.GetInt(ClearCountKey, 1),
            _            => 0,
        };
        PlayerPrefs.Save();
        return reason;
    }
}
