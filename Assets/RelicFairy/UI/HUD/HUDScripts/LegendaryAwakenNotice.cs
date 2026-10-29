using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 전설 룬 각성 순간 띠(10-24 전설 룬 개편 §4) — 판에 놓인 전설의 속성 존이 4단계가 되는 순간 한 번
/// 「불바다 — 각성 / 각성(불 존 4단계): …」을 구역 이름 자리(<see cref="RoomEventNotice"/>)에 띄운다.
/// 각성은 룬판에서 룬을 놓을 때 일어나는데, 띠(정렬 150)는 룬판 아래에 깔려 보이지 않았다(10-24 실측 LEG24) —
/// 그래서 각성은 모아 두었다가 <b>룬판이 닫히고 시간이 흐를 때</b> 띄운다.
/// 룬판이 바뀔 때(<see cref="MerlinRuneBridge.OnSynergiesUpdated"/>)만 본다. 처음 본 상태(씬 진입 · 이어하기)는 띄우지 않는다.
/// 룬판 브리지는 씬마다 새로 생기므로 바뀌면 다시 구독한다(ZoneTitleBanner처럼 앱 시작 때 한 번 세운다).
/// </summary>
public sealed class LegendaryAwakenNotice : MonoBehaviour
{
    // ── Static ──
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        if (FindFirstObjectByType<LegendaryAwakenNotice>() != null) return;
        var go = new GameObject("LegendaryAwakenNotice");
        DontDestroyOnLoad(go);
        go.AddComponent<LegendaryAwakenNotice>();
    }

    // ── Private ──
    private MerlinRuneBridge _bridge;
    private readonly HashSet<string> _awake = new();   // 각성한 전설(인스턴스 id)
    private readonly List<(string title, string desc)> _pending = new();
    private bool _primed;

    // ── Lifecycle ──
    private void Update()
    {
        var b = MerlinRuneBridge.Instance;
        if (b != _bridge)
        {
            if (_bridge != null) _bridge.OnSynergiesUpdated -= HandleSynergiesUpdated;
            _bridge = b;
            _primed = false;
            _awake.Clear();
            _pending.Clear();
            if (_bridge != null)
            {
                _bridge.OnSynergiesUpdated += HandleSynergiesUpdated;
                HandleSynergiesUpdated();
            }
        }

        // 룬판이 닫히고 시간이 흐를 때 하나씩(겹치면 띠가 앞 것을 바로 덮는다 — 남은 것은 다음 틈에)
        if (_pending.Count == 0 || Time.timeScale <= 0f) return;
        var grid = UI_GridPanel.Instance;
        if (grid != null && grid.IsOpen) return;
        var (title, desc) = _pending[0];
        _pending.RemoveAt(0);
        RoomEventNotice.Raise(title, desc);
        Managers.Sound?.PlayUiEvent(SoundEvent.RuneLegend);   // 10-29 UI 소리 — 각성 띠가 뜨는 순간
    }

    private void OnDestroy()
    {
        if (_bridge != null) _bridge.OnSynergiesUpdated -= HandleSynergiesUpdated;
    }

    // ── Event Handlers ──
    private void HandleSynergiesUpdated()
    {
        var placed = GameRunBootstrapper.Instance != null ? GameRunBootstrapper.Instance.Run?.ItemInventory?.PlacedItems : null;
        if (placed == null) return;
        foreach (var item in placed)
        {
            if (item == null || item.rarity != ItemRarity.Legendary || string.IsNullOrEmpty(item.element)) continue;
            bool now = LegendaryTuning.Awakened(item.element);
            if (!now) { _awake.Remove(item.instanceId); continue; }
            if (!_awake.Add(item.instanceId) || !_primed) continue;
            string clause = LegendaryAwaken.AwakenClause(item);
            if (string.IsNullOrEmpty(clause))   // 카드 글에 각성 줄이 아직 없다(표 업로드 전) — 존 단계만이라도
                clause = $"{ElementDef.GetById(item.element)?.Name ?? item.element} 존 4단계";
            _pending.Add(($"{item.displayName} — 각성", clause));
        }
        _primed = true;
    }
}
