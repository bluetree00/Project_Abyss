using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// 비전투 이벤트방 상호작용 챌린지 공용 베이스. 월드에 마커(TMP 라벨+프롬프트)를 세우고
/// 플레이어가 다가가 F로 활성화한다. 해결되면 <see cref="RoomClearGate"/>로 보상을 주고 OnResolved 발행
/// → RunFlowController가 출구를 연다. 미해결 상태로는 방을 떠날 수 없다(챌린지 필수).
///
/// 서브클래스는 <see cref="PlaceMarkers"/>(마커 배치)와 <see cref="OnActivate"/>(활성화 로직)만 정의한다.
/// GambleBoxChallenge의 절차 생성 패턴(프리팹 불필요·플레이어 앞 배치·빌보드)을 공유한다.
/// </summary>
public abstract class WorldInteractionChallenge : MonoBehaviour, IInteractionChallenge
{
    protected const float InteractRange = 2.8f;
    private const float LabelHeight  = 1.4f;
    private const float PromptHeight = 2.0f;

    protected GameRunSession  Run;
    protected LuckRollTableSO LuckTable;
    protected GameObject      EndEffect, EndEffect2;

    protected sealed class Marker
    {
        public Vector3      Pos;
        public TextMeshPro  Label, Prompt;
        public bool         Consumed;
        public object       Tag;
    }

    private readonly List<Marker> _markers = new();
    private Transform _camT;
    private bool _placed;
    private bool _resolved;
    private float _elapsed;
    private Vector3 _inward = Vector3.forward;

    public event Action OnResolved;

    protected IReadOnlyList<Marker> AllMarkers => _markers;
    protected bool IsResolved => _resolved;

    /// <summary>
    /// 방 안쪽으로 <b>실제로 열린</b> 방향. <c>player.forward</c>를 그대로 쓰면 안 된다 —
    /// <c>RunFlowController.MovePlayer</c>는 위치만 옮기고 캐릭터 회전은 이전 방 값을 그대로 둔다(카메라만 정렬).
    /// 진입 직후 forward가 벽을 향할 수 있고, 그러면 마커가 벽 속에 생겨 도달 불가가 된다.
    /// </summary>
    protected Vector3 Inward => _inward;

    /// <summary>
    /// 이 시간이 지나면 자동 해결한다. <b>탈출구가 없으면 소프트락</b>이다 —
    /// 해결(OnResolved) 전까지 RunFlowController가 출구 게이트를 열지 않기 때문.
    /// 0 이하면 자동 해결 없음(자체 타이머를 가진 서브클래스용).
    /// </summary>
    protected virtual float AutoResolveSeconds => 60f;

    /// <summary>SetupEventRoom이 호출 — 세션·보상 이펙트 주입. (서브클래스별 추가 Init은 각자 정의)</summary>
    public void InitBase(GameRunSession run, LuckRollTableSO table, GameObject endEffect, GameObject endEffect2)
    {
        Run = run; LuckTable = table; EndEffect = endEffect; EndEffect2 = endEffect2;
    }

    private void Update()
    {
        var pt = Managers.Player?.PlayerTransform;
        if (pt == null) return;

        if (!_placed)
        {
            _camT = Camera.main != null ? Camera.main.transform : null;
            ServiceRoomDecorPlacer.SyncPhysics();   // 방 블록이 막 생성됨 — 물리 쿼리 전 콜라이더 등록 보장
            _inward = ServiceRoomDecorPlacer.ResolveOpenDirection(pt.position, pt.forward);
            PlaceMarkers(pt);
            _placed = true;
        }

        Billboard();
        Tick();
        if (_resolved) return;

        // 자동 해결 — 마커에 닿지 못하는 상황에서도 방을 떠날 수 있게 하는 안전장치.
        _elapsed += Time.deltaTime;
        if (AutoResolveSeconds > 0f && _elapsed >= AutoResolveSeconds)
        {
            Notice("<color=#B8C0CC>시간이 다 됐다</color> — 출구가 열렸다");
            FinishWith(ChallengeGrade.Bronze, pt.position);
            return;
        }

        // 범위 내 가장 가까운 미소비 마커 → F 활성화.
        int hot = -1;
        float best = InteractRange * InteractRange;
        for (int i = 0; i < _markers.Count; i++)
        {
            var m = _markers[i];
            if (m.Consumed) continue;
            float d = (pt.position - m.Pos).sqrMagnitude;
            bool inRange = d <= InteractRange * InteractRange;
            if (m.Prompt != null) m.Prompt.gameObject.SetActive(inRange);
            if (inRange && d <= best) { best = d; hot = i; }
        }
        if (hot >= 0 && !UIInputGate.Blocked && Input.GetKeyDown(KeyCode.F)) OnActivate(hot);
    }

    private void OnDestroy() => OnDestroyChallenge();

    // ── 서브클래스 훅 ─────────────────────────────────────
    protected abstract void PlaceMarkers(Transform player);
    protected abstract void OnActivate(int markerIndex);
    protected virtual void Tick() {}
    protected virtual void OnDestroyChallenge() {}

    // ── 배치 안전장치 ──────────────────────────────────────

    /// <summary>
    /// 벽·장식을 피해 실제로 설 수 있는 자리를 찾는다. 전부 막히면 <paramref name="origin"/> 자체로 폴백한다
    /// (= 플레이어가 서 있는 곳이므로 반드시 도달 가능).
    /// <para>마커 좌표를 순수 수학으로 잡으면 방 입구가 벽 가장자리라 상당수가 벽 속에 박힌다.</para>
    /// </summary>
    protected Vector3 SafeSpot(Vector3 origin, Vector3 dir, float dist)
        => ServiceRoomDecorPlacer.TryFindSpot(origin, dir, dist, origin.y, out var p) ? p : origin;

    // ── 마커/보상 유틸 ────────────────────────────────────
    protected int AddMarker(Vector3 pos, string label, Color labelColor, string prompt, object tag = null)
    {
        var m = new Marker { Pos = pos, Tag = tag };
        m.Label  = MakeText(label,  pos + Vector3.up * LabelHeight,  3f, labelColor, 10);
        m.Prompt = MakeText(prompt, pos + Vector3.up * PromptHeight, 4f, Color.white, 11);
        m.Prompt.gameObject.SetActive(false);
        _markers.Add(m);
        return _markers.Count - 1;
    }

    protected Marker MarkerAt(int index) =>
        (index >= 0 && index < _markers.Count) ? _markers[index] : null;

    protected void SetLabel(int index, string text, Color color)
    {
        var m = MarkerAt(index);
        if (m?.Label != null) { m.Label.text = text; m.Label.color = color; }
    }

    protected void ConsumeMarker(int index)
    {
        var m = MarkerAt(index);
        if (m == null) return;
        m.Consumed = true;
        if (m.Prompt != null) m.Prompt.gameObject.SetActive(false);
    }

    /// <summary>챌린지 확정 — 등급 보상(RoomClearGate) 지급 + 출구 개방(OnResolved). 1회만.</summary>
    protected void FinishWith(ChallengeGrade grade, Vector3 center)
    {
        if (_resolved) return;
        _resolved = true;
        for (int i = 0; i < _markers.Count; i++)
            if (_markers[i].Prompt != null) _markers[i].Prompt.gameObject.SetActive(false);

        var gate = gameObject.AddComponent<RoomClearGate>();
        gate.Initialize(Run, LuckTable, EndEffect, EndEffect2, false, grade);
        gate.SetInteractionReward(true);   // 상호작용 천장 클램프(§6-3)
        gate.Activate(center);
        OnResolved?.Invoke();
    }

    protected static void Notice(string msg)
    {
        var hud = UnityEngine.Object.FindFirstObjectByType<HudPresenter>(FindObjectsInactive.Include);
        hud?.ShowBuffNotice(msg);
    }

    protected static string GradeName(ChallengeGrade g) => g switch
    {
        ChallengeGrade.Platinum => "플래티넘",
        ChallengeGrade.Gold     => "골드",
        ChallengeGrade.Silver   => "실버",
        ChallengeGrade.Bronze   => "브론즈",
        _                       => "실패",
    };

    protected TextMeshPro MakeText(string text, Vector3 worldPos, float size, Color color, int order)
    {
        var go = new GameObject("ChallengeText");
        go.transform.SetParent(transform, false);
        go.transform.position = worldPos;
        var t = go.AddComponent<TextMeshPro>();
        t.text = text;
        t.fontSize = size;
        t.alignment = TextAlignmentOptions.Center;
        t.color = color;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.sortingOrder = order;
        TMPOutlineHelper.ApplyDefault(t);
        return t;
    }

    private void Billboard()
    {
        if (_camT == null) return;
        foreach (var m in _markers)
        {
            if (m.Label  != null) m.Label.transform.rotation  = _camT.rotation;
            if (m.Prompt != null && m.Prompt.gameObject.activeSelf) m.Prompt.transform.rotation = _camT.rotation;
        }
    }
}
