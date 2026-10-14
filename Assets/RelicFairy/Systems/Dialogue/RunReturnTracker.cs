using UnityEngine;

/// <summary>
/// 런 종료(사망/클리어) 사유·누적 횟수를 PlayerPrefs로 기록하고 BaseCamp 진입 시 소비한다.
/// 복귀 대사(GetCountLines)의 랜덤 풀/마일스톤 선택에 쓰인다. (기존 방문 대사 GetVisitLines와 동일한 PlayerPrefs 관례.)
/// </summary>
public static class RunReturnTracker
{
    // 슬롯별 — 0번은 예전 이름 그대로(기존 진행 보존), 1 · 2번은 꼬리표. 예전엔 전역이라 한 슬롯의 사망 수가 다른 슬롯의
    // 멀린 이름 공개를 앞당기고, 한 슬롯의 복귀 사유를 다른 슬롯 거점이 소비했다(10-09 출시 점검).
    private static string SlotTag => (RunProgressManager.Instance?.ActiveSlotIndex ?? 0) is var s && s > 0 ? "_s" + s : string.Empty;
    private static string ReasonKey     => "run_return_reason" + SlotTag;
    private static string DeathCountKey => "run_death_count" + SlotTag;
    private static string ClearCountKey => "run_clear_count" + SlotTag;

    public enum Reason { None = 0, Death = 1, Clear = 2 }

    /// <summary>누적 사망 횟수(읽기 전용). 이야기 진행 판정(멀린 이름 공개 등)에 쓴다.</summary>
    public static int DeathCount => PlayerPrefs.GetInt(DeathCountKey, 0);

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

    /// <summary>새 게임 시작 — 활성 슬롯의 사유·누적 횟수를 전부 초기화한다. 이게 없으면 이전 플레이의
    /// 사망/클리어 횟수가 남아 첫 복귀부터 반복 대사가 나온다.</summary>
    public static void ResetAll()
    {
        PlayerPrefs.DeleteKey(ReasonKey);
        PlayerPrefs.DeleteKey(DeathCountKey);
        PlayerPrefs.DeleteKey(ClearCountKey);
        PlayerPrefs.Save();
    }

    /// <summary>사유를 소비하지 않고 읽는다 — 복귀 소환 연출 색(사망 보랏빛 / 클리어 금빛)을 대사보다 먼저 정할 때.</summary>
    public static Reason PeekReason() => (Reason)PlayerPrefs.GetInt(ReasonKey, 0);

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
