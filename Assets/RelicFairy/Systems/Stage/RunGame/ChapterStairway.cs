using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;

/// <summary>
/// S5 「층계 회랑」 — 보스 처치 뒤 다음 층으로 걸어 오른다(10-03 설계 · 구현 설계 §2). <see cref="ChapterGate"/>의 자리를 맡는다.
/// <list type="number">
/// <item>보스 광장 가운데에서 포탈 쪽으로 조금 앞(<see cref="TryArenaCenterSpot"/> — 10-18 사용자 「클리어 계단이 중앙 쪽에 나와야 통일감」, 네 아레나 같은 규칙).
///       가운데가 막혔으면 예전처럼 출구 자리(Next_Ch 마커 앞) — 그 벽이 디졸브로 걷힌다.
///       출구 카메라가 닿으면 돌 디딤판 계단이 아래 단부터 한 단씩 바닥에서 솟는다 —
///       층계참 · 꼭대기 돌 아치 · 아치 안 포탈이 차례로(10-10 사용자 「계단이 느낌 있게 생성되고 올라가는 느낌」, A안).
///       리소스 = <see cref="StairwaySetSO"/>(Addressables).
///       층계참 바닥 마법진 · 포탈 열림 분출 · 들어설 때 분출은 뺐다(10-11 이펙트 톤 점검 — 사용자 10-10 「다음 챕터 포탈 아래 이펙트」:
///       포탈 · 「다음 층으로」 글자와 같은 뜻을 같은 순간에 겹쳐 말했다).</item>
/// <item>계단 꼭대기에 닿으면 보스방을 떠난다 — 봉인 장면 정리(8b <c>BossStoryScenes.FlushSealed</c>) · 잉크 와이프 ·
///       같은 씬 높은 곳에 지어 둔 회랑으로 옮긴다 · 다음 챕터 씬을 미리 불러 둔다(<see cref="ScenePreloader"/>).</item>
/// <item>회랑 끝 문 → 덮고 다음 챕터(미리 불러 둔 씬을 로딩 화면 없이 켠다 · 대기방 둘러보기 생략).</item>
/// </list>
/// 회랑을 아레나 밖에 붙이지 않는다 — 옛 다리가 맵을 뚫었다(<see cref="BossExitPath"/> 주석).
/// </summary>
public sealed class ChapterStairway : MonoBehaviour
{
    // ── Constants ─────────────────────────────────────────────────
    private const float CellSize        = 1f;
    private const float CorridorLift    = 200f;   // 회랑을 지을 높이(아레나 위)
    // 돌계단(10-10) — 한 단 0.25 m(플레이어 StepClimb 0.35 m 아래 · 회랑과 같은 높이) × 8 = 2 m. 더 높이면 꼭대기에서 카메라가 벽 위로 나간다.
    private const int   StepCount       = 8;
    private const float StepRise        = 0.25f;
    private const float StepRun         = 0.75f;
    private const float StairWidth      = 3.6f;   // 디딤판 두 장(1.8 m)
    private const float LandingDepth    = 3f;
    private const int   LandingCells    = 3;
    private const float AssembleDelay   = 2.4f;   // 출구 카메라 이동(2.5초)이 닿을 무렵 솟기 시작 — 생성 순간이 화면에 잡히게
    private const float StepInterval    = 0.09f;
    private const float RiseDuration    = 0.35f;
    private const float LandingRise     = 0.45f;
    private const float ArchAppear      = 0.7f;
    private const float ArchInset       = 0.9f;   // 아치 = 층계참 가운데에서 바깥쪽으로 이만큼
    private const float PortalGrow      = 0.5f;
    private const float PortalScale     = 0.45f;  // 아치 안(트인 폭 ≈ 2 m)에 들어가게 — 0.7은 층계참에서 화면을 덮었다(10-10 실측)
    private const float WallOpenRadius  = 3.2f;   // 출구 앞에서 걷어낼 벽 반경
    private const float AppearDuration  = 0.9f;
    private const float WipeDuration    = 0.35f;
    private const float CorridorTimeout = 40f;    // 회랑에 들어선 뒤 이만큼 지나도 끝 문을 못 지나면 자동 진행(소프트락 방지 · 걸어서 ~8초)
    private const float WallPieceMax    = 8f;     // 걷어낼 벽 조각 최대 크기 — 커스텀 아레나의 통짜 벽(Ch2 50 m)은 건드리지 않는다
    private const float DoorTriggerSize = 3.2f;
    private const float LabelScale      = 0.012f; // 글자 ≈ 0.6 m — 0.02는 아치 위(카메라 높이 근처)에서 화면 위를 덮었다(10-10 실측)
    private const float LabelDilate     = 0.14f;  // Noto는 Thin 마스터 — 0.08은 이 크기(글자 ~1 m)에서 획이 끊겼다(10-02 실측) · 기본 0.18은 굵다
    // 광장 가운데 자리(10-18) — 층계참 가운데 = 아레나 바닥 가운데에서 오르는 쪽(포탈 쪽)으로 이만큼.
    // 디딤판 발치(−3.5 m) ~ 층계참 끝(+5.5 m)이 가운데를 감싸고 아치 · 포탈(+4.9 m)이 조금 앞에 선다.
    private const float CenterAhead     = 4f;
    private const float ClearLift       = 0.3f;   // 막힘 검사 상자 바닥 — 바닥 타일 · 낮은 장식(계단에 묻힌다)은 막힘으로 안 친다
    private const float ClearHeight     = 6f;     // 막힘 검사 높이 — 층계참 2 m + 아치 3.8 m
    private const float ClearSide       = 0.4f;   // 막힘 검사 폭 여유(한쪽) — 아치 기둥이 디딤판보다 조금 넓다
    private const float FloorSlabMin    = 12f;    // 가로 · 세로 모두 이보다 넓은 콜라이더는 바닥 판으로 본다(막힘 아님)

    private static readonly Color WipeColor = new(0.05f, 0.04f, 0.09f, 1f);
    private static readonly AnimationCurve WipeCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    /// <summary>회랑을 지나 다음 챕터에 들어왔다 — 새 씬 대기방이 둘러보기 대신 잉크 와이프를 걷는다(한 번 읽으면 꺼짐).</summary>
    public static bool ConsumeArrivedByStairs()
    {
        bool v = s_arrivedByStairs;
        s_arrivedByStairs = false;
        return v;
    }
    private static bool s_arrivedByStairs;

    // ── Private ───────────────────────────────────────────────────
    private Vector3      _outward;
    private Vector3      _rise;      // 계단이 오르는 방향 — 카메라가 보는 쪽(디딤판이 카메라 쪽에 놓여 보인다, 10-10 실측)
    private bool         _centered;  // 광장 가운데에 섰다 — 걷을 출구 벽이 없다(10-18)
    private BlockPalette _lower;
    private BlockPalette _upper;
    private string       _nextScene;
    private bool         _entered;
    private bool         _leaving;
    private Transform    _entry;
    private Transform    _corridor;
    private readonly Dictionary<GameObject, Bounds> _localBounds = new();   // 프리팹 → 단위 배치에서의 렌더 범위
    private GameObject   _doorFx;
    private float        _corridorEnteredAt = -1f;
    private Vector3      _doorCenter;
    private Vector3      _doorFwd;

    // ── Public Methods ────────────────────────────────────────────
    /// <param name="groundPos">출구 자리(바닥) — <see cref="BossExitPath"/>가 정한 곳. 광장 가운데가 막혔을 때만 여기에 선다.</param>
    /// <param name="outward">아레나 밖 쪽(마커 방향) — 출구 자리에 서면 그쪽 벽을 걷는다. 계단이 오르는 방향은 카메라 기준으로 따로 고른다(<see cref="ChooseRise"/>).</param>
    /// <param name="arena">보스 아레나 — 바닥 가운데를 잰다(10-18). 없으면(격자 보스방) 출구 자리.</param>
    public static ChapterStairway Spawn(Vector3 groundPos, Vector3 outward, Transform arena, BlockPalette lower, BlockPalette upper, string nextScene)
    {
        var axis      = SnapToAxis(outward);
        bool centered = TryArenaCenterSpot(arena, groundPos, out var pos, out var rise);
        if (!centered) { pos = groundPos; rise = ChooseRise(groundPos, axis); }
        var go = new GameObject("@ChapterStairway");
        go.transform.position = pos;
        var s = go.AddComponent<ChapterStairway>();
        s._outward   = axis;
        s._rise      = rise;
        s._centered  = centered;
        s._lower     = lower;
        s._upper     = upper;
        s._nextScene = nextScene;
        s.BuildAsync(s.GetCancellationTokenOnDestroy()).Forget();
        return s;
    }

    // ── Lifecycle ─────────────────────────────────────────────────
    private void Update()
    {
        // 끝 문을 못 지나가는 경우(트리거 미검출 · 막힘) — 회랑에 들어선 뒤 일정 시간이 지나면 문 앞으로 옮겨 진행한다.
        if (_corridorEnteredAt < 0f || _leaving) return;
        if (Time.unscaledTime - _corridorEnteredAt < CorridorTimeout) return;
        Debug.LogWarning("[ChapterStairway] 끝 문 미도달 — 자동 진행");
        LeaveAsync().Forget();
    }

    private void OnDestroy()
    {
        if (_corridor != null) Destroy(_corridor.gameObject);
        RunFx.Stop(ref _doorFx);
    }

    // ── Private Methods ───────────────────────────────────────────
    private async UniTaskVoid BuildAsync(CancellationToken ct)
    {
        try
        {
            var set = Managers.AddressableManager != null
                ? await Managers.AddressableManager.TryLoadAssetAsync<StairwaySetSO>(StairwaySetSO.Address) : null;
            await RunFx.LoadAsync();
            ct.ThrowIfCancellationRequested();
            if (set == null || set.StepSlabs == null || set.StepSlabs.Length == 0)
                throw new Exception($"계단 세트 없음 — {StairwaySetSO.Address}");

            // 1) 출구 앞 벽을 걷는다(렌더만 디졸브 — 콜라이더는 남겨 허공으로 떨어지지 않게). 광장 가운데면 걷을 벽이 없다(10-18).
            if (!_centered) OpenWall();

            // 2) 돌계단 — 바닥 아래에 지어 두고(바닥이 가린다) 카메라가 닿으면 아래 단부터 한 단씩 솟는다
            var columns = new List<Transform>(StepCount);
            var landing = BuildStone(set, columns);
            await UniTask.Delay(TimeSpan.FromSeconds(AssembleDelay), DelayType.UnscaledDeltaTime, cancellationToken: ct);
            for (int k = 0; k < columns.Count; k++)
            {
                RiseAsync(columns[k], RiseDuration, ct).Forget();
                if (k % 2 == 0) Managers.Sound?.PlayEvent(SoundEvent.StairRise);
                await UniTask.Delay(TimeSpan.FromSeconds(StepInterval), DelayType.UnscaledDeltaTime, cancellationToken: ct);
            }
            Managers.Sound?.PlayEvent(SoundEvent.StairRise);
            await RiseAsync(landing, LandingRise, ct);

            // 3) 꼭대기 — 돌 아치(디졸브) · 아치 안 포탈이 작게 열려 커진다.
            // 층계참 바닥 마법진(Hovl 원색 반복 · 굴절층 그대로)과 포탈 열림 분출은 뺐다 — 커지는 포탈 · 열림 소리 · 글자가 이미 「여기로」를 말한다(10-11 톤 점검)
            float top = StepCount * StepRise;
            Vector3 archBase = transform.position + _rise * ArchInset + Vector3.up * top;
            float archH = PlaceArch(set, archBase);
            await UniTask.Delay(TimeSpan.FromSeconds(ArchAppear * 0.5f), DelayType.UnscaledDeltaTime, cancellationToken: ct);
            Vector3 portalPos = archBase + Vector3.up * (archH * 0.45f);
            _doorFx = RunFx.PlayLoop(RunFxSlot.Portal, portalPos, FaceCamera(portalPos, -_rise), PortalScale, Color.clear);
            Managers.Sound?.PlayEvent(SoundEvent.PortalOpen);
            if (_doorFx != null)
            {
                _doorFx.AddComponent<FaceCameraYaw>();
                await GrowAsync(_doorFx.transform, PortalGrow, ct);
            }
            CreateLabel(archBase + Vector3.up * (archH + 0.35f));

            // 포탈 트리거 — 아치 안(계단을 다 지은 뒤에야 생긴다)
            var trig = new GameObject("TopTrigger");
            trig.transform.SetParent(transform, false);
            trig.transform.position = archBase + Vector3.up * 1.2f;
            trig.transform.rotation = Quaternion.LookRotation(_rise, Vector3.up);
            var col = trig.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.size = new Vector3(StairWidth * 0.75f, 2.4f, 1.2f);
            trig.AddComponent<StairTrigger>().Init(this, false);

            // 4) 회랑을 미리 지어 둔다(같은 씬 높은 곳) — 오르는 순간 바로 옮기도록. 카메라가 돌아가는 동안이라 지을 때의 끊김이 연출에 안 걸린다.
            _corridor = MapBuilder.BuildStairCorridor(_lower, _upper, transform.position + Vector3.up * CorridorLift, CellSize,
                                                      new System.Random(), out _doorCenter, out _doorFwd);
            var door = new GameObject("DoorTrigger");
            door.transform.SetParent(_corridor, false);
            door.transform.position = _doorCenter + Vector3.up * 1.2f;
            door.transform.rotation = Quaternion.LookRotation(_doorFwd, Vector3.up);
            var dcol = door.AddComponent<BoxCollider>();
            dcol.isTrigger = true;
            dcol.size = new Vector3(DoorTriggerSize, 2.5f, 1.5f);
            door.AddComponent<StairTrigger>().Init(this, true);
            // 회랑 끝 문 포탈은 회랑에 들어설 때 켠다(EnterCorridorAsync) — 미리 켜 두면 보이지도 않는 200 m 위에서 계속 돌았다(10-11 톤 2차 V-6)
            Debug.Log($"[ChapterStairway] 계단 · 회랑 준비 — 다음 {_nextScene}");
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { Debug.LogError($"[ChapterStairway] 짓기 예외 — 게이트로 대신한다: {e}"); FallbackGate(); }
    }

    /// <summary>
    /// 돌계단 · 층계참을 바닥 아래에 짓는다 — 단 k(아래 0 → 위)는 디딤판 (k+1)층 기둥, 층계참은 <see cref="StepCount"/>층.
    /// 디딤판은 폭 두 장 · 단마다 판 무늬를 섞고 반쯤은 뒤집는다. 밟는 판정은 기둥마다 상자 하나(Ground) — 판 자체 콜라이더는 걷는다.
    /// 반환 = 층계참(솟기 전).
    /// </summary>
    private Transform BuildStone(StairwaySetSO set, List<Transform> columns)
    {
        _entry = new GameObject("@StairEntry").transform;
        _entry.SetParent(transform, false);
        _entry.SetPositionAndRotation(transform.position, Quaternion.LookRotation(_rise, Vector3.up));
        var rng = new System.Random();
        for (int k = 0; k < StepCount; k++)
        {
            float z = -(LandingDepth * 0.5f + (StepCount - 1 - k + 0.5f) * StepRun);
            columns.Add(Column(set, $"Step_{k}", new Vector3(0f, 0f, z), StepRun, 1, k + 1, rng));
        }
        return Column(set, "Landing", Vector3.zero, LandingDepth, LandingCells, StepCount, rng);
    }

    private Transform Column(StairwaySetSO set, string name, Vector3 localCenter, float depth, int depthCells, int layers, System.Random rng)
    {
        var col = new GameObject(name).transform;
        col.SetParent(_entry, false);
        float h = layers * StepRise, cellD = depth / depthCells, halfW = StairWidth * 0.5f;
        for (int j = 0; j < layers; j++)
            for (int x = 0; x < 2; x++)
                for (int d = 0; d < depthCells; d++)
                {
                    var prefab = set.StepSlabs[rng.Next(set.StepSlabs.Length)];
                    if (prefab == null) continue;
                    var center = new Vector3((x - 0.5f) * halfW, (j + 0.5f) * StepRise, -depth * 0.5f + (d + 0.5f) * cellD);
                    PlaceFitted(prefab, col, center, new Vector3(halfW, StepRise, cellD), rng.Next(2) == 0);
                }
        var box = col.gameObject.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, h * 0.5f, 0f);
        box.size   = new Vector3(StairWidth, h, depth);
        col.gameObject.layer = LayerMask.NameToLayer("Ground");
        col.localPosition = localCenter + Vector3.down * (h + 0.1f);   // 바닥 아래에서 기다린다
        return col;
    }

    /// <summary>프리팹 하나를 상자 <paramref name="size"/>에 꼭 맞게(가운데 <paramref name="center"/>) 놓는다. 콜라이더는 걷는다.</summary>
    private void PlaceFitted(GameObject prefab, Transform parent, Vector3 center, Vector3 size, bool flip)
    {
        var b = LocalBounds(prefab);   // 원래 배율이 들어간 범위 — 그 위에 k를 더 곱한다
        var k = new Vector3(size.x / Mathf.Max(0.01f, b.size.x), size.y / Mathf.Max(0.01f, b.size.y), size.z / Mathf.Max(0.01f, b.size.z));
        var rot = flip ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity;
        var go = Instantiate(prefab, parent, false);
        go.transform.localRotation = rot;
        go.transform.localScale    = Vector3.Scale(prefab.transform.localScale, k);
        go.transform.localPosition = center - rot * Vector3.Scale(b.center, k);
        foreach (var c in go.GetComponentsInChildren<Collider>(true)) Destroy(c);
    }

    /// <summary>프리팹을 원점 · 단위 배율로 놓았을 때의 렌더 범위(한 번 재고 기억).</summary>
    private Bounds LocalBounds(GameObject prefab)
    {
        if (_localBounds.TryGetValue(prefab, out var b)) return b;
        var probe = Instantiate(prefab, new Vector3(0f, -5000f, 0f), Quaternion.identity);
        var rends = probe.GetComponentsInChildren<Renderer>();
        b = rends.Length > 0 ? rends[0].bounds : new Bounds(probe.transform.position, Vector3.one);
        foreach (var r in rends) b.Encapsulate(r.bounds);
        b.center -= probe.transform.position;
        Destroy(probe);
        _localBounds[prefab] = b;
        return b;
    }

    /// <summary>꼭대기 돌 아치 — 문이 계단 쪽을 향하게 세우고(원본은 X축이 문 방향) 디졸브로 나타난다. 반환 = 높이(m).</summary>
    private float PlaceArch(StairwaySetSO set, Vector3 basePos)
    {
        if (set.Arch == null) return 3f;
        var b   = LocalBounds(set.Arch);
        var rot = Quaternion.LookRotation(_rise, Vector3.up) * Quaternion.Euler(0f, -90f, 0f);
        float sc = set.ArchScale;
        var go = Instantiate(set.Arch, transform, false);
        go.transform.rotation   = rot;
        go.transform.localScale = set.Arch.transform.localScale * sc;
        var bottom = new Vector3(b.center.x, b.min.y, b.center.z) * sc;
        go.transform.position = basePos - rot * bottom;
        foreach (var c in go.GetComponentsInChildren<Collider>(true)) Destroy(c);   // 장식 — 기둥이 문 앞을 막지 않게
        DissolveEffect.PlayAppear(go, ArchAppear);
        return b.size.y * sc;
    }

    /// <summary>다음 챕터 — 다음 씬 이름(GameScene_ChN)의 끝 숫자.</summary>
    private ChapterId NextChapter()
    {
        int n = !string.IsNullOrEmpty(_nextScene) && char.IsDigit(_nextScene[^1]) ? _nextScene[^1] - '0' : 2;
        return (ChapterId)Mathf.Clamp(n, 1, 4);
    }

    /// <summary>다음 층 맵 곡 — Ch4는 맵 곡이 없어 지금 곡 그대로(null).</summary>
    private string NextMapBgm() => NextChapter() switch
    {
        ChapterId.Chapter2 => "Ch2_Map",
        ChapterId.Chapter3 => "Ch3_Map",
        _ => null,
    };

    /// <summary>바닥 아래에서 제자리(로컬 y 0)로 솟는다 — 감속.</summary>
    private static async UniTask RiseAsync(Transform t, float seconds, CancellationToken ct)
    {
        float y0 = t.localPosition.y, t0 = Time.unscaledTime;
        while (true)
        {
            float k = Mathf.Clamp01((Time.unscaledTime - t0) / seconds);
            float e = 1f - (1f - k) * (1f - k) * (1f - k);
            var p = t.localPosition;
            p.y = Mathf.Lerp(y0, 0f, e);
            t.localPosition = p;
            if (k >= 1f) return;
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
        }
    }

    /// <summary>포탈이 작게 열려 제 크기로 — 감속.</summary>
    private static async UniTask GrowAsync(Transform t, float seconds, CancellationToken ct)
    {
        var full = t.localScale;
        float t0 = Time.unscaledTime;
        while (t != null)
        {
            float k = Mathf.Clamp01((Time.unscaledTime - t0) / seconds);
            t.localScale = full * Mathf.Lerp(0.05f, 1f, 1f - (1f - k) * (1f - k));
            if (k >= 1f) return;
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
        }
    }

    /// <summary>
    /// 계단 · 회랑 방향을 방 격자 축(±X · ±Z)에 맞춘다. 블록은 늘 격자 축으로 놓이는데 방향이 대각이면
    /// 첫 계단이 들쭉날쭉한 마름모로 깔렸다(10-02 3차 실측 — 숲 아레나 가운데 마커).
    /// </summary>
    private static Vector3 SnapToAxis(Vector3 v)
    {
        v.y = 0f;
        if (v.sqrMagnitude < 0.001f) return Vector3.forward;
        return Mathf.Abs(v.x) >= Mathf.Abs(v.z) ? new Vector3(Mathf.Sign(v.x), 0f, 0f) : new Vector3(0f, 0f, Mathf.Sign(v.z));
    }

    /// <summary>
    /// 계단이 오르는 방향 — 카메라가 보는 쪽(화면 위)으로 오르게 고른다. 그래야 디딤판이 층계참 앞(카메라 쪽)에 놓여 보인다.
    /// 출구 마커 쪽으로만 세웠더니 그 쪽이 카메라 쪽인 아레나(Ch1)에서 계단이 층계참 뒤에 가려 「돌벽 위 아치」로만 보였다(10-10 실측).
    /// 디딤판 자리에 바닥이 없거나 벽이 있으면 옆(바깥쪽에 가까운 것 먼저) → 마지막에 카메라 쪽.
    /// </summary>
    private static Vector3 ChooseRise(Vector3 ground, Vector3 outward)
    {
        var cam = Camera.main;
        var d0 = SnapToAxis(cam != null ? cam.transform.forward : outward);
        var side = new Vector3(d0.z, 0f, -d0.x);
        if (Vector3.Dot(side, outward) < 0f) side = -side;
        foreach (var d in new[] { d0, side, -side, -d0 })
            if (FootprintClear(ground, d))
            {
                Debug.Log($"[ChapterStairway] 오름 방향 {d} (바깥 {outward})");
                return d;
            }
        Debug.LogWarning($"[ChapterStairway] 디딤판 자리가 다 막혔다 — 카메라 쪽 {d0}으로 세운다");
        return d0;
    }

    /// <summary>층계참 앞 디딤판 자리(바닥 높이 ±0.6 m)에 바닥이 이어지고 벽이 없는가.</summary>
    private static bool FootprintClear(Vector3 ground, Vector3 rise)
    {
        float run  = StepCount * StepRun;
        int   mask = LayerMask.GetMask("Ground");
        for (float t = LandingDepth * 0.5f + 0.4f; t <= LandingDepth * 0.5f + run; t += 1.4f)
            foreach (float x in new[] { -StairWidth * 0.4f, StairWidth * 0.4f })
            {
                var p = ground - rise * t + new Vector3(rise.z, 0f, -rise.x) * x;
                if (!Physics.Raycast(p + Vector3.up * 3f, Vector3.down, out var hit, 6f, mask, QueryTriggerInteraction.Ignore)) return false;
                if (Mathf.Abs(hit.point.y - ground.y) > 0.6f) return false;
            }
        var center = ground - rise * (LandingDepth * 0.5f + run * 0.5f) + Vector3.up * 1.2f;
        return !Physics.CheckBox(center, new Vector3(StairWidth * 0.5f, 1f, run * 0.5f), Quaternion.LookRotation(rise, Vector3.up),
                                 LayerMask.GetMask("Wall"), QueryTriggerInteraction.Ignore);
    }

    /// <summary>
    /// 광장 가운데 자리(10-18 ④) — 층계참 = 아레나 바닥 가운데에서 카메라 정면 축(오르는 쪽 = 포탈 쪽)으로 <see cref="CenterAhead"/>.
    /// 출구 자리(Next_Ch 마커 앞)는 아레나마다 달라 숲은 광장 오른쪽(대각 마커), 화룡 · 기사는 먼 벽 앞에 섰다(사용자 사진 04).
    /// 디딤판 · 층계참 자리에 바닥이 이어지고, 아치 높이까지 장식 · 벽이 없고, 발치가 플레이어와 걸어서 이어질 때만 true — 아니면 출구 자리.
    /// </summary>
    private static bool TryArenaCenterSpot(Transform arena, Vector3 exitGround, out Vector3 pos, out Vector3 rise)
    {
        pos = exitGround;
        var cam = Camera.main;
        rise = SnapToAxis(cam != null ? cam.transform.forward : Vector3.forward);
        if (arena == null || !TryArenaFloorBounds(arena, out var floor)) return false;

        int ground = LayerMask.GetMask("Ground");
        Vector3 mid = new Vector3(floor.center.x, exitGround.y, floor.center.z) + rise * CenterAhead;
        if (!Physics.Raycast(mid + Vector3.up * 3f, Vector3.down, out var hit, 6f, ground, QueryTriggerInteraction.Ignore))
            return Reject(mid, "바닥 없음");
        mid.y = hit.point.y;

        if (!FootprintClear(mid, rise)) return Reject(mid, "디딤판 자리 바닥이 끊기거나 벽");
        // 층계참 자리 바닥 — FootprintClear는 디딤판 쪽만 본다
        var side = new Vector3(rise.z, 0f, -rise.x);
        foreach (float t in new[] { -LandingDepth * 0.4f, LandingDepth * 0.4f })
            foreach (float x in new[] { -StairWidth * 0.4f, StairWidth * 0.4f })
            {
                var p = mid + rise * t + side * x;
                if (!Physics.Raycast(p + Vector3.up * 3f, Vector3.down, out var h, 6f, ground, QueryTriggerInteraction.Ignore)
                    || Mathf.Abs(h.point.y - mid.y) > 0.6f)
                    return Reject(mid, "층계참 자리 바닥이 끊김");
            }

        // 디딤판 발치 ~ 아치 뒤까지 · 바닥 위 ClearLift ~ 아치 높이 — 장식(기본 레이어) · 벽이 있으면 안 된다.
        // 보스 · 플레이어 몸, 가로 · 세로 모두 넓은 판(장식 레이어로 깐 바닥 메시 — 화룡 아치 바닥 92 m)은 치지 않는다.
        float back = LandingDepth * 0.5f + StepCount * StepRun, front = LandingDepth * 0.5f + 0.6f;
        var center = mid + rise * ((front - back) * 0.5f) + Vector3.up * ((ClearLift + ClearHeight) * 0.5f);
        var half   = new Vector3(StairWidth * 0.5f + ClearSide, (ClearHeight - ClearLift) * 0.5f, (front + back) * 0.5f);
        var hits   = Physics.OverlapBox(center, half, Quaternion.LookRotation(rise, Vector3.up),
                                        LayerMask.GetMask("Default", "Wall"), QueryTriggerInteraction.Ignore);
        foreach (var c in hits)
        {
            if (c.GetComponentInParent<RelicFairy.Monster.MonsterBase>() != null || c.GetComponentInParent<PlayerController>() != null) continue;
            var size = c.bounds.size;
            if (size.x > FloorSlabMin && size.z > FloorSlabMin) continue;
            return Reject(mid, $"막힘 {c.name}");
        }

        // 발치가 플레이어와 걸어서 이어지는가(기사 단상처럼 끊긴 곳이면 오를 수 없다)
        var player = GameRunBootstrapper.Instance?.Run?.Player;
        Vector3 foot = mid - rise * (back + 0.6f);
        if (player != null && RelicFairy.Monster.BossArenaGuard.TryPullInside(foot, mid.y, player.transform, player.transform.position, null, out _))
            return Reject(mid, "발치가 플레이어와 이어지지 않음");

        pos = mid;
        Debug.Log($"[ChapterStairway] 광장 가운데 {pos} · 바닥 가운데 {floor.center} · 오름 {rise}");
        return true;

        bool Reject(Vector3 at, string why)
        {
            Debug.LogWarning($"[ChapterStairway] 광장 가운데 {at} 못 씀({why}) — 출구 자리 {exitGround}");
            return false;
        }
    }

    /// <summary>아레나 바닥 범위 — Ground 콜라이더(트리거 제외)를 합친다(미니맵이 재는 바닥과 같은 기준).</summary>
    private static bool TryArenaFloorBounds(Transform arena, out Bounds bounds)
    {
        int ground = LayerMask.NameToLayer("Ground");
        bounds = default;
        bool has = false;
        foreach (var col in arena.GetComponentsInChildren<Collider>(false))
        {
            if (col.isTrigger || col.gameObject.layer != ground) continue;
            if (!has) { bounds = col.bounds; has = true; }
            else bounds.Encapsulate(col.bounds);
        }
        return has && bounds.size.x > 1f && bounds.size.z > 1f;
    }

    /// <summary>
    /// 소용돌이(포탈 이펙트)는 정면 축이 「터널 축」이라, 걷는 방향으로 돌리면 위에서 보는 카메라엔 옆으로 누운 원통으로 보였다(10-02 실측).
    /// <see cref="ChapterGate"/>처럼 카메라를 수평으로 바라보게 한다 — 카메라가 없으면 받은 방향.
    /// </summary>
    private static Quaternion FaceCamera(Vector3 at, Vector3 fallback)
    {
        // 수평만 돌리면 카메라가 38° 내려다봐서 여전히 원통 옆면이 보였다(10-02 2차 실측).
        // 카메라 시선(-forward)에 맞추면 화면 가장자리 · 가까운 문에서 터널이 옆으로 누웠다(3차 실측) — 카메라 자리를 바로 본다.
        var cam = Camera.main;
        Vector3 toCam = cam != null ? cam.transform.position - at : fallback;
        if (toCam.sqrMagnitude < 0.01f) toCam = fallback;
        return Quaternion.LookRotation(toCam.normalized, Vector3.up);
    }

    /// <summary>문 소용돌이 — 카메라가 따라 움직이므로 매 프레임 카메라 자리를 바로 본다(터널 축이 카메라를 향해 동심원으로 보인다).</summary>
    private sealed class FaceCameraYaw : MonoBehaviour
    {
        private void LateUpdate()
        {
            var cam = Camera.main;
            if (cam == null) return;
            Vector3 toCam = cam.transform.position - transform.position;
            if (toCam.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(toCam.normalized, Vector3.up);
        }
    }

    /// <summary>출구 앞 벽 블록(벽 레이어)의 렌더러를 디졸브로 걷는다 — 「벽이 열리며」.</summary>
    private void OpenWall()
    {
        var center = transform.position + _outward * 2f + Vector3.up * 2f;
        var hits = Physics.OverlapSphere(center, WallOpenRadius, LayerMask.GetMask("Wall"), QueryTriggerInteraction.Ignore);
        var seen = new HashSet<GameObject>();
        foreach (var h in hits)
        {
            var r = h.GetComponentInParent<Renderer>();
            if (r == null) continue;
            var size = r.bounds.size;
            if (size.x > WallPieceMax || size.y > WallPieceMax * 2f || size.z > WallPieceMax) continue;   // 통짜 벽은 그대로
            var go = r.gameObject;
            if (!seen.Add(go)) continue;
            DissolveEffect.PlayDisappear(go, AppearDuration, () => { if (go != null) foreach (var rr in go.GetComponentsInChildren<Renderer>()) rr.enabled = false; });
        }
    }

    private void CreateLabel(Vector3 pos)
    {
        var go = new GameObject("Label");
        go.transform.SetParent(transform, false);
        go.transform.position = pos;
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        if (Camera.main != null) go.transform.rotation = Camera.main.transform.rotation;
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta  = new Vector2(320f, 90f);
        rt.localScale = Vector3.one * LabelScale;
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text      = "다음 층으로";
        tmp.fontSize  = 48f;
        tmp.fontStyle = FontStyles.Normal;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color     = new Color(0.85f, 0.80f, 0.62f, 1f);
        // 월드 글자는 얇은 본문체(NotoSansKR) — 기본 폰트(DNF Bold)는 3D 인게임 표기엔 무겁다(10-02 사용자 · RelicFloatText 기준)
        var thin = ThinFont();
        if (thin != null) tmp.font = thin;
        TMPOutlineHelper.ApplySoftShadow(tmp);
        if (thin != null && tmp.fontMaterial.HasProperty(ShaderUtilities.ID_FaceDilate))
            tmp.fontMaterial.SetFloat(ShaderUtilities.ID_FaceDilate, LabelDilate);
    }

    /// <summary>얇은 본문체 — 기본 폰트의 폴백 목록에 들어 있다(DnfFontAssetGenerator가 NotoSansKR를 폴백으로 건다).</summary>
    private static TMP_FontAsset ThinFont()
    {
        var def = TMP_Settings.defaultFontAsset;
        if (def == null) return null;
        if (def.name.Contains("NotoSansKR")) return def;
        var table = def.fallbackFontAssetTable;
        if (table == null) return null;
        for (int i = 0; i < table.Count; i++)
            if (table[i] != null && table[i].name.Contains("NotoSansKR")) return table[i];
        return null;
    }

    /// <summary>계단 꼭대기 — 보스방을 떠나 회랑으로.</summary>
    private async UniTaskVoid EnterCorridorAsync()
    {
        if (_entered || _corridor == null) return;
        _entered = true;
        var ct = this.GetCancellationTokenOnDestroy();
        var player = GameRunBootstrapper.Instance?.Run?.Player;
        try
        {
            RelicFairy.Monster.BossStoryScenes.FlushSealed();   // 8b 요청 — 보스방을 떠나는 순간 봉인된 몸을 치운다
            Managers.Sound?.PlayEvent(SoundEvent.PortalEnter);
            // 들어설 때 분출은 뺐다 — 0.35초 와이프가 바로 덮어 화면에 남지 않았다(10-11 톤 점검)
            Vector2 dir = new Vector2(_rise.x, _rise.z);   // 걸어 들어가는 쪽으로 덮는다
            await ScreenFade.CoverAsync(dir, WipeColor, WipeDuration, WipeCurve, ct);
            // 보스 곡이 회랑까지 이어졌다 — 다음 층 맵 곡으로(10-10). Ch4로 가는 길은 맵 곡이 없어 그대로.
            var bgm = NextMapBgm();
            if (bgm != null) Managers.Sound?.PlayBgmAsync(bgm).Forget();
            if (player != null)
            {
                var start = _corridor.position + Vector3.up * 0.1f;
                player.transform.SetPositionAndRotation(start, Quaternion.LookRotation(Vector3.forward, Vector3.up));
                player.RequestFacing(Quaternion.LookRotation(Vector3.forward, Vector3.up));   // 방향 목표도 — 안 하면 다음 물리 프레임에 옛 방향으로 돌아갔다(10-09 출시 점검)
                if (player.TryGetComponent<Rigidbody>(out var rb)) { rb.position = start; rb.linearVelocity = Vector3.zero; }
            }
            Vector3 doorFxPos = _doorCenter + Vector3.up * 1.8f;
            var doorFx = RunFx.PlayLoop(RunFxSlot.Portal, doorFxPos, FaceCamera(doorFxPos, -_doorFwd), 0.9f, Color.clear);
            if (doorFx != null) { doorFx.transform.SetParent(_corridor, true); doorFx.AddComponent<FaceCameraYaw>(); }
            ScenePreloader.Begin(_nextScene);
            _corridorEnteredAt = Time.unscaledTime;
            await UniTask.Delay(TimeSpan.FromSeconds(0.15f), DelayType.UnscaledDeltaTime, cancellationToken: ct);   // 카메라가 따라붙을 틈
            await ScreenFade.RevealAsync(Vector2.up, WipeDuration, WipeCurve, ct);
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>회랑 끝 문 — 덮고 다음 챕터로(미리 불러 둔 씬).</summary>
    private async UniTaskVoid LeaveAsync()
    {
        if (_leaving) return;
        _leaving = true;
        try
        {
            await ScreenFade.CoverAsync(Vector2.right, WipeColor, WipeDuration, WipeCurve, this.GetCancellationTokenOnDestroy());
        }
        catch (OperationCanceledException) { return; }
        s_arrivedByStairs = true;
        GameRunBootstrapper.Instance?.AdvanceChapter();
    }

    private void FallbackGate()
    {
        if (GameObject.Find("@ChapterGate") == null) ChapterGate.Spawn(transform.position);
    }

    /// <summary>계단 꼭대기 · 회랑 끝 문 트리거(플레이어만).</summary>
    private sealed class StairTrigger : MonoBehaviour
    {
        private ChapterStairway _owner;
        private bool _isDoor;
        public void Init(ChapterStairway owner, bool isDoor) { _owner = owner; _isDoor = isDoor; }
        private void OnTriggerEnter(Collider other)
        {
            if (_owner == null || other.GetComponentInParent<PlayerController>() == null) return;
            if (_isDoor) _owner.LeaveAsync().Forget();
            else         _owner.EnterCorridorAsync().Forget();
        }
    }
}
