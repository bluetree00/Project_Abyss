using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;

/// <summary>
/// S5 「층계 회랑」 — 보스 처치 뒤 다음 층으로 걸어 오른다(10-03 설계 · 구현 설계 §2). <see cref="ChapterGate"/>의 자리를 맡는다.
/// <list type="number">
/// <item>출구 자리(Next_Ch 마커 앞)의 벽이 디졸브로 걷히고 오르는 첫 계단이 나타난다.</item>
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
    private const int   EntrySteps      = 4;
    private const int   EntryWidth      = 5;
    private const float WallOpenRadius  = 3.2f;   // 출구 앞에서 걷어낼 벽 반경
    private const float AppearDuration  = 0.9f;
    private const float WipeDuration    = 0.35f;
    private const float CorridorTimeout = 40f;    // 회랑에 들어선 뒤 이만큼 지나도 끝 문을 못 지나면 자동 진행(소프트락 방지 · 걸어서 ~8초)
    private const float WallPieceMax    = 8f;     // 걷어낼 벽 조각 최대 크기 — 커스텀 아레나의 통짜 벽(Ch2 50 m)은 건드리지 않는다
    private const float DoorTriggerSize = 3.2f;
    private const float LabelDilate     = 0.14f;  // Noto는 Thin 마스터 — 0.08은 이 크기(글자 ~1 m)에서 획이 끊겼다(10-02 실측) · 기본 0.18은 굵다

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
    private BlockPalette _lower;
    private BlockPalette _upper;
    private string       _nextScene;
    private bool         _entered;
    private bool         _leaving;
    private Transform    _entry;
    private Transform    _corridor;
    private GameObject   _doorFx;
    private float        _corridorEnteredAt = -1f;
    private Vector3      _doorCenter;

    // ── Public Methods ────────────────────────────────────────────
    /// <param name="groundPos">출구 자리(바닥) — <see cref="BossExitPath"/>가 정한 곳.</param>
    /// <param name="outward">아레나 밖 쪽(마커 방향) — 계단이 그쪽으로 오른다.</param>
    public static ChapterStairway Spawn(Vector3 groundPos, Vector3 outward, BlockPalette lower, BlockPalette upper, string nextScene)
    {
        var go = new GameObject("@ChapterStairway");
        go.transform.position = groundPos;
        var s = go.AddComponent<ChapterStairway>();
        s._outward   = SnapToAxis(outward);
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
            // 1) 출구 앞 벽을 걷는다(렌더만 디졸브 — 콜라이더는 남겨 허공으로 떨어지지 않게)
            OpenWall();

            // 2) 첫 계단 — 아레나 바닥 위에서 바깥쪽으로 오른다
            _entry = MapBuilder.BuildEntrySteps(_lower, transform.position - _outward * (EntrySteps * CellSize), _outward,
                                                EntrySteps, EntryWidth, CellSize);
            _entry.SetParent(transform, true);
            DissolveEffect.PlayAppear(_entry.gameObject, AppearDuration);

            // 꼭대기 트리거 — 마지막 계단 위
            var trig = new GameObject("TopTrigger");
            trig.transform.SetParent(transform, false);
            trig.transform.position = transform.position + Vector3.up * (EntrySteps * MapBuilderRise() + 1f);
            trig.transform.rotation = Quaternion.LookRotation(_outward, Vector3.up);
            var col = trig.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.size = new Vector3(EntryWidth * CellSize, 2.5f, 1.5f);
            trig.AddComponent<StairTrigger>().Init(this, false);

            await RunFx.LoadAsync();
            ct.ThrowIfCancellationRequested();
            // 계단 끝 문 빛 — 「다음 층으로」
            Vector3 topFxPos = transform.position + Vector3.up * (EntrySteps * MapBuilderRise() + 1.8f);
            _doorFx = RunFx.PlayLoop(RunFxSlot.Portal, topFxPos, FaceCamera(topFxPos, -_outward), 0.8f, Color.clear);
            if (_doorFx != null) _doorFx.AddComponent<FaceCameraYaw>();
            CreateLabel(transform.position + Vector3.up * (EntrySteps * MapBuilderRise() + 4.4f));

            // 3) 회랑을 미리 지어 둔다(같은 씬 높은 곳) — 오르는 순간 바로 옮기도록
            _corridor = MapBuilder.BuildStairCorridor(_lower, _upper, transform.position + Vector3.up * CorridorLift, CellSize,
                                                      new System.Random(), out _doorCenter, out var doorFwd);
            var door = new GameObject("DoorTrigger");
            door.transform.SetParent(_corridor, false);
            door.transform.position = _doorCenter + Vector3.up * 1.2f;
            door.transform.rotation = Quaternion.LookRotation(doorFwd, Vector3.up);
            var dcol = door.AddComponent<BoxCollider>();
            dcol.isTrigger = true;
            dcol.size = new Vector3(DoorTriggerSize, 2.5f, 1.5f);
            door.AddComponent<StairTrigger>().Init(this, true);
            Vector3 doorFxPos = _doorCenter + Vector3.up * 1.8f;
            var doorFx = RunFx.PlayLoop(RunFxSlot.Portal, doorFxPos, FaceCamera(doorFxPos, -doorFwd), 0.9f, Color.clear);
            if (doorFx != null) { doorFx.transform.SetParent(_corridor, true); doorFx.AddComponent<FaceCameraYaw>(); }
            Debug.Log($"[ChapterStairway] 계단 · 회랑 준비 — 다음 {_nextScene}");
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { Debug.LogError($"[ChapterStairway] 짓기 예외 — 게이트로 대신한다: {e}"); FallbackGate(); }
    }

    private static float MapBuilderRise() => 0.25f;   // MapBuilder.StairRise와 같다(계단 높이)

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
        rt.localScale = Vector3.one * 0.02f;
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
            Vector2 dir = new Vector2(_outward.x, _outward.z);
            await ScreenFade.CoverAsync(dir, WipeColor, WipeDuration, WipeCurve, ct);
            if (player != null)
            {
                var start = _corridor.position + Vector3.up * 0.1f;
                player.transform.SetPositionAndRotation(start, Quaternion.LookRotation(Vector3.forward, Vector3.up));
                if (player.TryGetComponent<Rigidbody>(out var rb)) { rb.position = start; rb.linearVelocity = Vector3.zero; }
            }
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
