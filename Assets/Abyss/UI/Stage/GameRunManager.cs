using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using Cysharp.Threading.Tasks;
using System;

public sealed class GameRunManager
{
    private const string ROOMS_KEY = "STAGEDATA_ROOMS";
    private const string STAGE_KEY = "STAGEDATA_STAGE";
    public ChapterId CurrentChapter { get; private set; }
    public bool IsRunning { get; private set; }
    public RoomManager RoomManager { get; private set; }
    public StagePointManager StagePointManager { get; private set; }

    private Dictionary<int, StageData> stageDataCache;

    public async UniTask StartNewRunAsync(ChapterId chapter)
    {
        IsRunning = true;
        CurrentChapter = chapter;

        // 1) Stage 데이터(선택)
        await LoadStageDataAsync(STAGE_KEY);

        // 2) Rooms 데이터(필수)
        RoomManager = new RoomManager();
        await RoomManager.InitializeAsync(ROOMS_KEY);

        if (!RoomManager.IsInitialized)
        {
            Debug.LogError($"[GameRun] RoomManager init failed. Check Addressables Address='{ROOMS_KEY}'");
            return;
        }

        // 3) StagePoint
        StagePointManager = new StagePointManager();
        StagePointManager.Initialize(chapter, RoomManager);

        // 4) UI -> Register
        var points = UnityEngine.Object.FindObjectsOfType<StagePointUI>();
        foreach (var ui in points)
            ui.Register(StagePointManager, RoomManager);


        // 5) Resolve + Start
        StagePointManager.ResolveAll();
        StagePointManager.SetStartAsCurrent();
    }

    public void RegisterPoints(IEnumerable<StagePointUI> points)
    {
        foreach (var ui in points)
            ui.Register(StagePointManager, RoomManager);
    }

    public void ResolveAllPointsAndSetStart()
    {
        StagePointManager.ResolveAll();
        StagePointManager.SetStartAsCurrent();
    }


    private async UniTask LoadStageDataAsync(string key)
    {
        TextAsset textAsset = null;

        try
        {
            textAsset = await Managers.AddressableManager.LoadAssetAsync<TextAsset>(key);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[GameRun] Stage data load failed (optional). key={key}, err={e.Message}");
            return;
        }

        if (textAsset == null)
        {
            Debug.LogWarning($"[GameRun] Stage data TextAsset is null (optional). key={key}");
            return;
        }

        var root = JsonUtility.FromJson<StageDataRoot>(textAsset.text);
        if (root?.stages == null)
        {
            Debug.LogWarning("[GameRun] StageDataRoot.stages is null");
            return;
        }

        stageDataCache = root.stages.ToDictionary(s => s.stageId, s => s);
        Debug.Log($"[GameRun] StageData Loaded: {stageDataCache.Count}");
    }
}
