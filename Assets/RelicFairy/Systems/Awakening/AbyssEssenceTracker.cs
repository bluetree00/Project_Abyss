using UnityEngine;

/// <summary>
/// 런 중 심연의 정수(Abyss Essence)를 적립하는 컴포넌트.
/// GameRunBootstrapper가 소유하며 Bind() 후 GameRunSession 이벤트에 연결된다.
///
/// 적립 시점:
///   - 몬스터 처치 N마리마다 소량  → OnMonsterKilled() 호출 (PlayerController 측에서)
///   - 일반 방 클리어              → GameRunSession.OnRoomCleared(false)
///   - 보스 처치                   → GameRunSession.OnRoomCleared(true)
/// </summary>
public sealed class AbyssEssenceTracker : MonoBehaviour
{
    // ── 설정 ────────────────────────────────────────────────────────────
    [Header("처치 적립")]
    [SerializeField] private int killsPerBatch    = 10;  // N마리 처치마다 1회 지급
    [SerializeField] private int essencePerBatch  = 5;   // 처치 배치 지급량

    [Header("방 클리어 적립")]
    [SerializeField] private int essencePerRoom   = 10;
    [SerializeField] private int essencePerBoss   = 50;

    // ── 상태 ────────────────────────────────────────────────────────────
    private GameRunSession _session;
    private int _killCount;

    // ── 초기화 ──────────────────────────────────────────────────────────

    /// <summary>GameRunSession에 연결. 씬 진입 후 한 번만 호출.</summary>
    public void Bind(GameRunSession session)
    {
        Unbind();
        _session   = session;
        _killCount = 0;

        if (_session != null)
            _session.OnRoomCleared += HandleRoomCleared;
    }

    private void Unbind()
    {
        if (_session != null)
            _session.OnRoomCleared -= HandleRoomCleared;
        _session = null;
    }

    private void OnDestroy() => Unbind();

    // ── Public API ───────────────────────────────────────────────────────

    /// <summary>몬스터 처치 시 PlayerController에서 호출.</summary>
    public void OnMonsterKilled()
    {
        if (_session == null) return;

        _killCount++;
        if (_killCount < killsPerBatch) return;

        _killCount -= killsPerBatch;
        _session.AddEssence(essencePerBatch);
        Debug.Log($"[AbyssEssenceTracker] 처치 배치 +{essencePerBatch} 정수");
    }

    // ── 이벤트 핸들러 ────────────────────────────────────────────────────

    private void HandleRoomCleared(bool isBossRoom)
    {
        if (_session == null) return;

        int amount = isBossRoom ? essencePerBoss : essencePerRoom;
        _session.AddEssence(amount);
        Debug.Log($"[AbyssEssenceTracker] {(isBossRoom ? "보스" : "방")} 클리어 +{amount} 정수");
    }
}
