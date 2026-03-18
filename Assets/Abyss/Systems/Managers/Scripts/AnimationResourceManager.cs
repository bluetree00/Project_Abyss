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
            if (_clipCache.ContainsKey(key)) continue;

            // 키 존재 여부 먼저 확인 (InvalidKeyException 방지)
            var locHandle = Addressables.LoadResourceLocationsAsync(key);
            await locHandle.Task;
            var locations = locHandle.Result;
            Addressables.Release(locHandle);

            if (locations == null || locations.Count == 0)
            {
                Debug.LogWarning($"[AnimResource] 키 없음, 건너뜀: {key}");
                continue;
            }

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

        IsInitialized = true;
    }

    public AnimationClip GetClip(string key)
    {
        _clipCache.TryGetValue(key, out var clip);
        return clip;
    }
}
