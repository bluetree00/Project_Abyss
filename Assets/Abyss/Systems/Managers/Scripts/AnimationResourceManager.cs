using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

public class AnimationResourceManager
{
    private Dictionary<string, AnimationClip> _clipCache = new Dictionary<string, AnimationClip>();

    public bool IsInitialized { get; private set; } = false;

    public async UniTask PreloadClipsAsync(IEnumerable<string> keys)
    {
        foreach (var key in keys)
        {
            if (string.IsNullOrEmpty(key)) continue;
            if (_clipCache.ContainsKey(key)) continue;

            try
            {
                // 위치 확인
                var locHandle = Addressables.LoadResourceLocationsAsync(key, typeof(AnimationClip));
                await locHandle.Task;
                var locations = locHandle.Result;
                Addressables.Release(locHandle);

                if (locations == null || locations.Count == 0)
                {
                    Debug.LogWarning($"[AnimResource] 키 없음, 건너뜀: {key}");
                    continue;
                }

                // 클립 로드
                var handle = Addressables.LoadAssetAsync<AnimationClip>(key);
                await handle.Task;

                if (handle.Status == AsyncOperationStatus.Succeeded && handle.Result != null)
                {
                    _clipCache[key] = handle.Result;
                    Debug.Log($"[AnimResource] Loaded {key}");
                }
                else
                {
                    Addressables.Release(handle);
                    Debug.LogWarning($"[AnimResource] 로드 실패: {key}");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AnimResource] 건너뜀: {key} ({e.GetType().Name})");
            }
        }

        IsInitialized = true;
    }

    public AnimationClip GetClip(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        _clipCache.TryGetValue(key, out var clip);
        return clip;
    }
}
