using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 비전투 이벤트방 상호작용 챌린지 공용 베이스. 월드에 마커(TMP 라벨+프롬프트)를 세우고
/// 플레이어가 다가가 F로 활성화한다. 해결되면 이벤트마다 정한 특별 보상을 그 자리에서 주고(<see cref="GrantRewardAsync"/>) OnResolved 발행
/// → RunFlowController가 출구를 연다. 미해결 상태로는 방을 떠날 수 없다(챌린지 필수).
/// 10-29 룬 고르기 창(RoomClearGate)을 걷었다 — 보상을 받는 순간 문이 열린다(설계 §3 「룬이 아닌 특별한 보상」).
///
/// 서브클래스는 <see cref="PlaceMarkers"/>(마커 배치) · <see cref="OnActivate"/>(활성화 로직) · <see cref="GrantRewardAsync"/>(보상)를 정의한다.
/// GambleBoxChallenge의 절차 생성 패턴(프리팹 불필요·플레이어 앞 배치·빌보드)을 공유한다.
/// </summary>
public abstract class WorldInteractionChallenge : MonoBehaviour, IInteractionChallenge
{
    protected const float InteractRange = 2.8f;
    private const float LabelHeight  = 1.4f;
    private const float PromptHeight = 2.0f;

    protected GameRunSession  Run;

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
    private Transform _props;   // 이 챌린지가 세운 글자 · 상자 — 컴포넌트를 걷으면 함께 걷는다(10-29 B3)
    private Vector3 _stage;              // 10-29 무대 — 방 가운데 걸을 수 있는 면(MinigameArena와 같은 기준)
    private float   _stageRadius = 4f;   // 무대 가운데에서 가장 가까운 벽까지 − 여유
    private Vector3 _toEntry = Vector3.back;   // 무대 가운데 → 들어온 자리

    private const float StageWant     = 7f;    // 무대 반경 상한
    private const float PropWallClear = 1.0f;  // 소품 자리는 벽(걸을 수 있는 면 가장자리)에서 이만큼 떨어진다

    public event Action OnResolved;

    protected IReadOnlyList<Marker> AllMarkers => _markers;
    protected bool IsResolved => _resolved;

    /// <summary>
    /// 방 안쪽으로 <b>실제로 열린</b> 방향. <c>player.forward</c>를 그대로 쓰면 안 된다 —
    /// <c>RunFlowController.MovePlayer</c>는 위치만 옮기고 캐릭터 회전은 이전 방 값을 그대로 둔다(카메라만 정렬).
    /// 진입 직후 forward가 벽을 향할 수 있고, 그러면 마커가 벽 속에 생겨 도달 불가가 된다.
    /// </summary>
    protected Vector3 Inward => _inward;

    /// <summary>세운 것(글자 · 상자 모델)을 매다는 자리. 컴포넌트가 사라지면 함께 사라진다(이어하기로 끝난 방을 복원할 때 — RunFlowController).</summary>
    protected Transform Props
    {
        get
        {
            if (_props == null)
            {
                _props = new GameObject("ChallengeProps").transform;
                _props.SetParent(transform, false);
            }
            return _props;
        }
    }

    /// <summary>
    /// 이 시간이 지나면 자동 해결한다. <b>탈출구가 없으면 소프트락</b>이다 —
    /// 해결(OnResolved) 전까지 RunFlowController가 출구 게이트를 열지 않기 때문.
    /// 0 이하면 자동 해결 없음(자체 타이머를 가진 서브클래스용).
    /// </summary>
    protected virtual float AutoResolveSeconds => 60f;

    /// <summary>SetupEventRoom이 호출 — 세션 주입. (서브클래스별 추가 Init은 각자 정의)
    /// 10-29 행운표 · 보상 이펙트는 뺐다 — 보상을 RoomClearGate가 아니라 이벤트가 직접 준다.</summary>
    public void InitBase(GameRunSession run) => Run = run;

    private void Update()
    {
        var pt = Managers.Player?.PlayerTransform;
        if (pt == null) return;

        if (!_placed)
        {
            _camT = Camera.main != null ? Camera.main.transform : null;
            ServiceRoomDecorPlacer.SyncPhysics();   // 방 블록이 막 생성됨 — 물리 쿼리 전 콜라이더 등록 보장
            _inward = ServiceRoomDecorPlacer.ResolveOpenDirection(pt.position, pt.forward);
            // 10-29 무대 — 입구 4~5 m 앞에 세우면 안쪽 벽(제물 제단 방은 가운데 띠가 좁다)에 박혔다. 방 가운데를 무대로 삼는다.
            MinigameArena.Resolve(transform.position, pt.position, StageWant, out _stage, out _stageRadius);
            FindOpenStage(transform.position, pt.position);
            Vector3 toEntry = pt.position - _stage; toEntry.y = 0f;
            _toEntry = toEntry.sqrMagnitude > 1f ? toEntry.normalized : -_inward;
            PlaceMarkers(pt);
            Debug.Log($"[WorldInteraction] {GetType().Name} 무대 {_stage} · 반경 {_stageRadius:0.0}m · 입구까지 {toEntry.magnitude:0.0}m");
            _placed = true;
        }

        Billboard();
        // 조작이 풀리기 전(방 입장 연출 · 화면이 덮인 동안)엔 시간도 F도 받지 않는다(10-29 B2 — 보물 창고 18초가 덮인 첫 프레임부터 흘렀다).
        // RunFlowController가 해결 알림을 구독하는 순간이 조작이 풀린 때다(EventMinigame.RunAsync와 같은 기준).
        if (OnResolved == null) return;
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

    private void OnDestroy()
    {
        OnDestroyChallenge();
        if (_props != null) Destroy(_props.gameObject);
    }

    // ── 서브클래스 훅 ─────────────────────────────────────
    protected abstract void PlaceMarkers(Transform player);
    protected abstract void OnActivate(int markerIndex);
    protected virtual void Tick() {}
    protected virtual void OnDestroyChallenge() {}

    /// <summary>
    /// 이벤트의 특별 보상(10-29). <paramref name="grade"/>는 끝낸 성적(시간이 다 되면 브론즈).
    /// 기다리는 보상(제물 제단의 유물 기억 창)은 끝날 때까지 출구를 묶는다 — 받은 뒤 OnResolved.
    /// </summary>
    protected abstract UniTask GrantRewardAsync(ChallengeGrade grade, Vector3 center, CancellationToken ct);

    // ── 배치 안전장치 ──────────────────────────────────────

    /// <summary>
    /// 벽·장식을 피해 실제로 설 수 있는 자리를 찾는다. 전부 막히면 <paramref name="origin"/> 자체로 폴백한다
    /// (= 플레이어가 서 있는 곳이므로 반드시 도달 가능).
    /// <para>마커 좌표를 순수 수학으로 잡으면 방 입구가 벽 가장자리라 상당수가 벽 속에 박힌다.</para>
    /// </summary>
    protected Vector3 SafeSpot(Vector3 origin, Vector3 dir, float dist)
        => ServiceRoomDecorPlacer.TryFindSpot(origin, dir, dist, origin.y, out var p) ? p : origin;

    /// <summary>무대 가운데(방 가운데 걸을 수 있는 면).</summary>
    protected Vector3 Stage => _stage;

    /// <summary>
    /// 무대 위 한 자리(10-29) — 들어온 쪽의 <b>반대편</b>(플레이어가 들어서며 바라보는 쪽)을 0°로 <paramref name="yaw"/>도 돌린 방향 <paramref name="dist"/> m.
    /// 걸을 수 있는 면 · 무대 가운데와 길로 이어진 곳 · 벽에서 1 m 이상만 받는다. 못 찾으면 가까이 당겨 다시, 끝내 없으면 무대 가운데.
    /// </summary>
    protected Vector3 StageSpot(float yaw, float dist)
    {
        Vector3 dir = Quaternion.Euler(0f, yaw, 0f) * -_toEntry;
        dist = Mathf.Min(dist, Mathf.Max(1.5f, _stageRadius));
        for (float r = dist; r >= 1f; r -= 0.5f)
        {
            if (!MinigameArena.Snap(_stage + dir * r, 0.6f, out var p) || !MinigameArena.IsLinked(_stage, p)) continue;
            if (NavMesh.FindClosestEdge(p, out var edge, MinigameArena.Filter) && edge.distance < PropWallClear) continue;
            return p;
        }
        return _stage;
    }

    /// <summary>
    /// 무대 고르기 — 방 한가운데가 기둥 곁이면 무대가 3 m로 줄어 신탁 문 셋이 1.5 ~ 3 m 안에 몰렸다(10-29 실측).
    /// 뿌리 둘레(±8 m · 2 m 간격)에서 반경 <see cref="StageGood"/> 이상 트인 자리 중 <b>뿌리에서 입구 쪽으로 35% 당긴 점에 가장 가까운 곳</b>(입구에서 보이게 —
    /// 가장 넓은 곳만 고르면 방 구석 기둥 뒤로 가 입구 화면에서 안 보였다). 그런 곳이 없으면 가장 넓은 곳. 들어온 자리와 길로 이어진 곳만.
    /// </summary>
    private void FindOpenStage(Vector3 root, Vector3 playerPos)
    {
        if (_stageRadius >= StageGood) return;
        Vector3 aim = root + (playerPos - root) * EntryPull;   // 방 가운데가 기둥 뒤면 입구 쪽 트인 자리가 잡힌다(10-29 신탁 방)
        float nearGood = float.MaxValue, widest = _stageRadius;
        Vector3 goodAt = _stage, wideAt = _stage;
        float goodR = 0f;
        for (float x = -OpenSearch; x <= OpenSearch; x += OpenStep)
        for (float z = -OpenSearch; z <= OpenSearch; z += OpenStep)
        {
            if (!MinigameArena.Snap(new Vector3(root.x + x, playerPos.y, root.z + z), 1f, out var p)) continue;
            if (!NavMesh.FindClosestEdge(p, out var edge, MinigameArena.Filter)) continue;
            float r = Mathf.Min(edge.distance - StageEdgeMargin, StageWant);
            float dx = p.x - aim.x, dz = p.z - aim.z, d = dx * dx + dz * dz;
            bool good = r >= StageGood && d < nearGood, wider = r > widest;
            if ((!good && !wider) || !MinigameArena.IsLinked(p, playerPos)) continue;
            if (good)  { nearGood = d; goodAt = p; goodR = r; }
            if (wider) { widest = r; wideAt = p; }
        }
        if (nearGood < float.MaxValue) { _stage = goodAt; _stageRadius = goodR; }
        else                           { _stage = wideAt; _stageRadius = widest; }
    }

    private const float StageGood       = 5f;   // 문 셋 · 상자 다섯이 겹치지 않는 무대 반경
    private const float EntryPull       = 0.35f;
    private const float OpenSearch      = 8f;
    private const float OpenStep        = 2f;
    private const float StageEdgeMargin = 0.5f;

    /// <summary>들어온 쪽을 바라보는 회전 — 소품 앞면이 플레이어를 본다.</summary>
    protected Quaternion FacingEntry(Vector3 at)
    {
        Vector3 look = (_stage + _toEntry * 6f) - at; look.y = 0f;
        return Quaternion.LookRotation(look.sqrMagnitude > 0.01f ? look.normalized : _toEntry);
    }

    /// <summary>
    /// 글자만 떠 있던 사물에 모델을 세운다(10-29) — Addressables <paramref name="key"/>를 읽어 렌더러 높이를 <paramref name="height"/> m로 맞추고
    /// 바닥에서 솟게 한다. 못 읽으면 글자만 남는다(진행은 그대로). <see cref="Props"/>에 매달려 챌린지와 함께 사라진다.
    /// </summary>
    protected async UniTaskVoid SpawnPropAsync(string key, Vector3 pos, Quaternion rot, float height, CancellationToken ct)
    {
        GameObject prefab = null;
        try { prefab = await Managers.AddressableManager.TryLoadAssetAsync<GameObject>(key); }
        catch (OperationCanceledException) { return; }
        catch (Exception e) { Debug.LogWarning($"[WorldInteraction] 소품 「{key}」 읽기 실패 — 글자만: {e.Message}"); }
        if (prefab == null || ct.IsCancellationRequested || this == null) return;

        var go = Instantiate(prefab, Props);
        go.transform.SetPositionAndRotation(pos, rot);
        var rs = go.GetComponentsInChildren<Renderer>(true);
        if (rs.Length > 0)
        {
            var b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            float s = height / Mathf.Max(0.1f, b.size.y);
            go.transform.localScale *= s;
            go.transform.position += Vector3.up * (pos.y - (pos.y + (b.min.y - pos.y) * s));   // 바닥을 무대 높이에 맞춘다
        }
        Vector3 home = go.transform.position;
        try
        {
            for (float t = 0f; t < 1f; )
            {
                t = Mathf.Min(1f, t + Time.deltaTime / PropRiseSeconds);
                float e = 1f - (1f - t) * (1f - t);
                if (go == null) return;
                go.transform.position = home + Vector3.down * (PropRiseDepth * (1f - e));
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
        }
        catch (OperationCanceledException) { }
    }

    private const float PropRiseSeconds = 0.45f;
    private const float PropRiseDepth   = 1.2f;

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

    /// <summary>챌린지 확정 — 특별 보상 지급(<see cref="GrantRewardAsync"/>) + 출구 개방(OnResolved). 1회만.</summary>
    protected void FinishWith(ChallengeGrade grade, Vector3 center)
    {
        if (_resolved) return;
        _resolved = true;
        for (int i = 0; i < _markers.Count; i++)
            if (_markers[i].Prompt != null) _markers[i].Prompt.gameObject.SetActive(false);

        FinishAsync(grade, center).Forget();
    }

    private async UniTaskVoid FinishAsync(ChallengeGrade grade, Vector3 center)
    {
        try { await GrantRewardAsync(grade, center, this.GetCancellationTokenOnDestroy()); }
        catch (OperationCanceledException) { return; }   // 방이 사라졌다 — 알릴 곳도 없다
        catch (Exception e)
        {
            // 보상이 깨져도 방에 가두지 않는다(소프트락 금지)
            Debug.LogError($"[Challenge] {GetType().Name} 보상 예외 — 출구는 연다: {e}");
        }
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
        go.transform.SetParent(Props, false);
        go.transform.position = worldPos;
        var t = go.AddComponent<TextMeshPro>();
        t.text = text;
        t.fontSize = size;
        t.alignment = TextAlignmentOptions.Center;
        t.color = color;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.sortingOrder = order;
        TMPOutlineHelper.ApplySoftShadow(t);
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
