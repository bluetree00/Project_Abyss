using UnityEngine;

/// <summary>
/// Game_Intro 씬 완료 여부를 슬롯별 PlayerPrefs로 관리.
/// BaseCampOnboardingDirector와 동일한 관례를 따른다.
/// </summary>
public static class IntroCompletionTracker
{
    private const string KeyPrefix = "game_intro_done_v1_slot";

    private static string Key => KeyPrefix + (RunProgressManager.Instance?.ActiveSlotIndex ?? 0);

    public static bool IsCompleted => PlayerPrefs.GetInt(Key, 0) == 1;

    public static void MarkCompleted()
    {
        PlayerPrefs.SetInt(Key, 1);
        PlayerPrefs.Save();
    }

    public static void ClearForSlot(int slot)
    {
        PlayerPrefs.DeleteKey(KeyPrefix + slot);
        PlayerPrefs.Save();
    }
}
