#if UNITY_EDITOR
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 보스 클리어 후처리(파츠 드래프트 → 이어지는 길/챕터 게이트)를 <b>플레이 중</b> 강제로 발화시키는 점검 메뉴.
/// 메뉴: RelicFairy/Debug/Simulate Boss Clear (Play) · RelicFairy/Debug/Confirm Draft Popup (Play)
///
/// 왜 — "보스를 잡아도 다음 스테이지 포탈이 안 생긴다"를 재현하려면 보스방까지 가서 보스를 잡아야 한다.
/// 첫 메뉴는 <see cref="GameRunSession.NotifyBossRoomCleared"/>를 현재 방에서 직접 발행해
/// 그 뒤 시퀀스(GameRunBootstrapper.BossClearSequenceAsync)만 떼어 검증한다.
/// 커스텀 아레나가 아니면 BossExitPath가 폴백으로 플레이어 위치에 ChapterGate만 세운다 — 그게 보이면 후처리는 정상.
/// 둘째 메뉴는 열린 파츠 드래프트 팝업의 첫 카드를 고르고 「장착」을 눌러 시퀀스를 다음 단계로 넘긴다(무인 검증용).
/// </summary>
public static class BossClearDebugMenu
{
    [MenuItem("RelicFairy/Debug/Simulate Boss Clear (Play)")]
    public static void Simulate()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[BossClearDebug] 플레이 모드에서만 동작한다."); return; }

        var grb = GameRunBootstrapper.Instance;
        var run = grb != null ? grb.Run : null;
        if (run == null) { Debug.LogError("[BossClearDebug] 런 없음 — 게임 씬(런 진행 중)에서 실행해야 한다."); return; }

        Vector3 center = run.Player != null ? run.Player.transform.position : Vector3.zero;
        Debug.Log($"[BossClearDebug] NotifyBossRoomCleared 발행 center={center} chapter={run.CurrentChapter} hasNext={run.HasNextChapter()} running={run.IsRunning}");
        run.NotifyBossRoomCleared(center);
    }

    /// <summary>로드아웃에 유물이 없으면(에디터 직접 실행) 랜슬롯을 얹고 발행 — 파츠 드래프트 경로까지 태운다.</summary>
    [MenuItem("RelicFairy/Debug/Simulate Boss Clear with Relic (Play)")]
    public static void SimulateWithRelic()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[BossClearDebug] 플레이 모드에서만 동작한다."); return; }
        var loadout = AppBootstrapper.Instance != null ? AppBootstrapper.Instance.Loadout : null;
        if (loadout != null && loadout.Relic == null)
        {
            var relic = AssetDatabase.LoadAssetAtPath<RelicClassSO>("Assets/RelicFairy/Systems/Relic/Data/RelicClass_Lancelot.asset");
            if (relic != null) { loadout.SetRelic(relic); Debug.Log("[BossClearDebug] 테스트용 유물 장착: 랜슬롯"); }
        }
        Simulate();
    }

    [MenuItem("RelicFairy/Debug/Confirm Draft Popup (Play)")]
    public static void ConfirmDraft()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[BossClearDebug] 플레이 모드에서만 동작한다."); return; }

        var popup = Object.FindFirstObjectByType<UI_RelicPartDraftPopup>(FindObjectsInactive.Exclude);
        if (popup == null) { Debug.LogWarning("[BossClearDebug] 열린 파츠 드래프트 팝업이 없다."); return; }

        var t = typeof(UI_RelicPartDraftPopup);
        var setSelected = t.GetMethod("SetSelected", BindingFlags.Instance | BindingFlags.NonPublic);
        var confirm     = t.GetMethod("OnConfirmClicked", BindingFlags.Instance | BindingFlags.NonPublic);
        if (setSelected == null || confirm == null) { Debug.LogError("[BossClearDebug] 리플렉션 실패 — 팝업 메서드 이름이 바뀌었다."); return; }

        setSelected.Invoke(popup, new object[] { 0 });
        confirm.Invoke(popup, null);
        Debug.Log("[BossClearDebug] 드래프트 첫 카드 선택 + 장착 호출");
    }
}
#endif
