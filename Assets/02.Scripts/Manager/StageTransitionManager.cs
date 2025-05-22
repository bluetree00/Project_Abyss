using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using MapGeneratorManager;
using System.Threading.Tasks; // MapGeneratorManager에서 정의된 Graph, Node, Edge 클래스 사용

public class StageTransitionManager
{
    [SerializeField] private StageData stageData; // StageData SO
    [SerializeField] private string stageDataAddress; // Addressable Asset의 주소
    private StageManager stageManager;

    async public void Init(string address)
    {
        stageDataAddress = address; // Addressable Asset의 주소를 설정

        if (string.IsNullOrEmpty(stageDataAddress))
        {
            Debug.LogError("StageData address is not assigned!");
            return;
        }

        // StageData 로드
        // StageData stageData = await LoadStageDataAsync(stageDataAddress);
        // if (stageData == null)
        // {
        //     Debug.LogError("Failed to load StageData!");
        //     return;
        // }

        // StageManager 초기화
        //stageManager = new StageManager(stageData);

        // 연결된 노드 출력 (디버깅용)
        // var connectedNodes = stageManager.GetConnectedNodes();
        // foreach (var node in connectedNodes)
        // {
        //     Debug.Log($"Connected Node: {node.Id} ({node.GetLabel()})");
        // }
    }

    private async Task<StageData> LoadStageDataAsync(string address)
    {
        AsyncOperationHandle<StageData> handle = Addressables.LoadAssetAsync<StageData>(address);
        await handle.Task;

        if (handle.Status == AsyncOperationStatus.Succeeded)
        {
            Debug.Log($"Successfully loaded StageData from address: {address}");
            return handle.Result;
        }
        else
        {
            Debug.LogError($"Failed to load StageData from address: {address}");
            return null;
        }
    }

    void Update()
    {
        
    }
}