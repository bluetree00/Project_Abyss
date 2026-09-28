using UnityEngine;

/// <summary>
/// 런 중 심연의 정수(RelicFairy Essence)를 적립하는 컴포넌트.
/// GameRunBootstrapper가 소유하며 Bind() 후 GameRunSession 이벤트에 연결된다.
///
/// 적립 시점:
///   - 몬스터 처치 N마리마다 소량  → OnMonsterKilled() 호출 (PlayerController 측에서)
///   - 일반 방 클리어              → GameRunSession.OnRoomCleared(false)
///   - 보스 처치                   → GameRunSession.OnRoomCleared(true)
/// </summary>
public sealed class EssenceTracker : MonoBehaviour
{
    // ── 설정 ────────────────────────────────────────────────────────────
    // 수급값은 정본 §7 기준. 첫 해금(300)이 첫 사망 직후에 오도록 잡혀 있다 — 사망런 500~800 / 완주 ~1,400.
    // [09-28 사용자 결정] +10% — 기억의 제단에 원거리 갈래(5노드)가 더해져 전부 해금이 38 → 41런이 됐다. 완주 구성
    // (방 26×15 · 보스 4×120 · 처치 약 68배치×8)에서 처치 8→9 · 방 15→16 · 보스 120→132 = +142(+10.0%) → 약 38런으로 되돌린다
    // (시뮬 `RelicFairy_문서/기획/시뮬/altar_sim_20260928.py`, 베이스캠프 재설계 설계서 §2-1).
    // ⚠️ 이 컴포넌트는 씬·프리팹에 없고 GameRunBootstrapper가 AddComponent로 붙인다 — 여기 기본값이 곧 실제 값이다.
    [Header("처치 적립")]
    [SerializeField] private int killsPerBatch    = 10;  // N마리 처치마다 1회 지급
    [SerializeField] private int essencePerBatch  = 9;   // 처치 배치 지급량

    [Header("방 클리어 적립")]
    [SerializeField] private int essencePerRoom   = 16;
    [SerializeField] private int essencePerBoss   = 132;

    // ── 상태 ────────────────────────────────────────────────────────────
    private GameRunSession _session;
    private int _killCount;
    private Vector3 _lastKillPos;
    private bool    _hasKillPos;

    // ── 초기화 ──────────────────────────────────────────────────────────

    /// <summary>GameRunSession에 연결. 씬 진입 후 한 번만 호출.</summary>
    public void Bind(GameRunSession session)
    {
        Unbind();
        _session   = session;
        _killCount = 0;

        if (_session != null)
            _session.OnRoomCleared += HandleRoomCleared;

        QuestEvents.OnMonsterKilled += HandleMonsterKilled;
    }

    private void Unbind()
    {
        if (_session != null)
            _session.OnRoomCleared -= HandleRoomCleared;
        _session = null;

        QuestEvents.OnMonsterKilled -= HandleMonsterKilled;
    }

    private void OnDestroy() => Unbind();

    // ── 이벤트 핸들러 ────────────────────────────────────────────────────

    /// <summary>몬스터가 죽은 자리를 기억해 둔다 — 정수 조각을 그 자리에 떨어뜨리기 위해.
    /// <see cref="QuestEvents.OnMonsterKilled"/>는 이름만 주므로 위치는 사망 처리(DieState)가 알려준다.</summary>
    public void ReportKillPosition(Vector3 pos)
    {
        _lastKillPos = pos;
        _hasKillPos  = true;
    }

    private void HandleMonsterKilled(string _)
    {
        if (_session == null) return;

        _killCount++;
        if (_killCount < killsPerBatch) return;

        _killCount -= killsPerBatch;
        DropEssence(essencePerBatch, _hasKillPos ? _lastKillPos : PlayerPos());
    }

    private void HandleRoomCleared(bool isBossRoom)
    {
        if (_session == null) return;

        int amount = isBossRoom ? essencePerBoss : essencePerRoom;
        DropEssence(amount, PlayerPos());
    }

    /// <summary>
    /// 정수를 <b>줍는 물건</b>으로 떨어뜨린다. 조용히 수치만 올리면 플레이어가 재화의 존재를 모른다.
    /// <para>적립은 조각을 주울 때 <see cref="GameRunSession.AddEssence"/>에서 일어난다 —
    /// 깊이 보상 배율도 거기서 걸리므로 여기서는 원값만 넘긴다.</para>
    /// </summary>
    private void DropEssence(int amount, Vector3 pos)
    {
        if (amount <= 0) return;
        EssenceShardPickup.SpawnDrops(pos, amount);
    }

    private Vector3 PlayerPos()
    {
        var tf = Managers.Player?.PlayerTransform;
        return tf != null ? tf.position : Vector3.zero;
    }
}
