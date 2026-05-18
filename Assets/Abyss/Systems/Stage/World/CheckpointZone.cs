using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 방 입구에 배치하는 체크포인트 트리거.
///
/// 플레이어가 처음 진입하면 RunProgressManager.SaveAsync()를 호출해 진행 상황을 저장한다.
/// 저장은 방 생성이 완전히 끝난 뒤(RiseComplete + DissolveEntrance 완료 신호 후)에만 유효.
/// 한 방당 한 번만 발동한다.
/// </summary>
[RequireComponent(typeof(Collider))]
public class CheckpointZone : MonoBehaviour
{
    // ─────────────────────────────────────────
    // SerializeField
    // ─────────────────────────────────────────

    [Header("체크포인트 설정")]
    [SerializeField] private int _slotIndex;

    // ─────────────────────────────────────────
    // Private Fields
    // ─────────────────────────────────────────

    private bool _ready;
    private bool _triggered;

    // ─────────────────────────────────────────
    // Lifecycle
    // ─────────────────────────────────────────

    private void Awake()
    {
        GetComponent<Collider>().isTrigger = true;
    }

    // ─────────────────────────────────────────
    // Public Methods
    // ─────────────────────────────────────────

    /// <summary>
    /// 방 생성(플랫폼 상승 + 맵 스폰)이 완료된 뒤 호출.
    /// 이 후 플레이어가 트리거에 들어오면 저장이 실행된다.
    /// </summary>
    public void SetReady(int slotIndex)
    {
        _slotIndex = slotIndex;
        _ready     = true;
    }

    // ─────────────────────────────────────────
    // Event Handlers
    // ─────────────────────────────────────────

    private void OnTriggerEnter(Collider other)
    {
        if (_triggered || !_ready) return;
        if (!other.CompareTag("Player"))   return;

        _triggered = true;
        SaveAsync().Forget();
    }

    // ─────────────────────────────────────────
    // Private Methods
    // ─────────────────────────────────────────

    private async UniTaskVoid SaveAsync()
    {
        var session = GameRunBootstrapper.Instance?.Run;
        var pm      = RunProgressManager.Instance;
        if (session == null || pm == null) return;

        try
        {
            await pm.SaveAsync(session, _slotIndex);
        }
        catch (System.OperationCanceledException) { }
    }
}
