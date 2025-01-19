using System;
using System.Collections;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

public class AddressablesManager : MonoBehaviour
{
    private static AddressablesManager _instance;
    public static AddressablesManager Instance
    {
        get
        {
            if (_instance == null)
            {
                GameObject go = new GameObject("AddressablesManager");
                _instance = go.AddComponent<AddressablesManager>();
                DontDestroyOnLoad(go);
            }
            return _instance;
        }
    }

    private void Start()
    {
        StartCoroutine(InitAddressables());
    }

    IEnumerator InitAddressables()
    {
        var Init = Addressables.InitializeAsync();
        yield return Init;
    }
    
    public void LoadAsset<T>(string key, Action<T> onSuccess, Action onFailure = null) where T : UnityEngine.Object
    {
        Addressables.LoadAssetAsync<T>(key).Completed += handle =>
        {
            if (handle.Status == AsyncOperationStatus.Succeeded)
            {
                onSuccess?.Invoke(handle.Result);
            }
            else
            {
                Debug.LogError($"Failed to load asset with key: {key}");
                onFailure?.Invoke();
            }
        };
    }

    public void InstantiateAsync(string key, Action<GameObject> onSuccess)
    {
        Addressables.InstantiateAsync(key).Completed += handle =>
        {
            if (handle.Status == AsyncOperationStatus.Succeeded)
            {
                onSuccess?.Invoke(handle.Result);
                Debug.Log($"<color=green>어드레서블 시스템으로 성공적으로 생성 : {key}</color>");
            }
            else
            {
                Debug.LogError($"Failed to instantiate prefab with key: {key}");
                onSuccess?.Invoke(null);
            }
        };
    }
}