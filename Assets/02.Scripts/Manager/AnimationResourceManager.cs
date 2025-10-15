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

            var handle = Addressables.LoadAssetAsync<AnimationClip>(key);
            await handle.Task;

            if (handle.Status == AsyncOperationStatus.Succeeded && handle.Result != null)
            {
                _clipCache[key] = handle.Result;
                Debug.Log($"[AnimResource] Loaded {key}");
            }
            else
            {
                Debug.LogWarning($"[AnimResource] Failed to load {key}");
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
