using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;

/// <summary>
/// 랭킹방(10-29 설계 §5-3 · 2a) — 원형 홀 하나에 테마 둘(해방기 / 악몽). 거점 씬 먼 자리에 그때 불러 두고 문으로 오간다(새 씬 없음).
///   · 해방기 · 악몽 리치를 깨고 돌아오면 <see cref="BaseCampBootstrapper"/>가 귀환 대사 전에 <see cref="VisitAsync"/> — 도착 · 비석 창 · 돌아가는 문 · 성문 앞.
///   · 거점 「기록의 문」에서도 같은 길로 들어온다.
///   · 비석 [F] = <see cref="RankingBoardOverlay.ShowAsync"/>(창 · 랭킹 · 이름은 4c). 전투 · 다른 상호작용은 없다.
/// 홀 프리팹(바닥 · 기둥 · 보스 석상 넷 · 비석 · 돌아가는 문 · 빛)은 MCP로 지었다 — 이 컴포넌트는 루트에 붙어 테마와 드나듦만 맡는다.
/// </summary>
public sealed class RankingHall : MonoBehaviour
{
    // ── Constants ─────────────────────────────────────────────
    public const string PrefabKey = "RankingHall/RankingHall";
    private static readonly Vector3 HallOrigin = new(0f, 600f, 0f);   // 거점 섬 · 탑과 겹치지 않는 하늘(천장 없음 · 안개 그대로)
    private const float FadeOutSec   = 0.4f;
    private const float FadeInSec    = 0.6f;
    private const float SteleRange   = 2.6f;
    private const float VistaSeconds = 3.8f;   // 아래 풍경을 훑는 시간
    private const float VistaReturn  = 1.4f;   // 플레이어 뒤로 돌아오는 시간

    // ── Static ────────────────────────────────────────────────
    private static RankingHall s_open;
    private static readonly int BaseColorId     = Shader.PropertyToID("_BaseColor");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    // ── Serialized ────────────────────────────────────────────
    [Header("자리")]
    [SerializeField] private Transform _spawnPoint;
    [SerializeField] private Transform _stele;

    [Header("테마 — 해방기 / 악몽")]
    [SerializeField] private Light[]    _themeLights;
    [SerializeField] private Renderer[] _tintRenderers;   // 석상 · 비석 빛 — MaterialPropertyBlock으로 물든다
    [SerializeField] private TextMeshPro _title;
    [SerializeField] private Color _liberationColor = new(1.00f, 0.82f, 0.45f, 1f);   // 금빛 — 사슬 끊긴 석상
    [SerializeField] private Color _nightmareColor  = new(0.70f, 0.30f, 0.60f, 1f);   // 악몽 포탈 물듦과 같은 색

    [Header("도착 둘러보기 — 아래 던전 풍경(10-29)")]
    [SerializeField] private Transform _vistaFrom;   // 난간 밖 · 숲 구역을 내려다보는 자리
    [SerializeField] private Transform _vistaTo;     // 뒤 위 — 꼭대기 전체 + 너머 구역
    [SerializeField] private Transform _vistaLook;

    // ── Private ───────────────────────────────────────────────
    private StoryEra _era;
    private PlayerController _player;
    private TextMeshPro _prompt;
    private Transform _cam;
    private bool _returnRequested;
    private TextMeshPro[] _labels;   // 석상 이름 · 비석 제목 · 문 이름 — 카메라를 본다(거울 글자 막기)

    // ── Properties ────────────────────────────────────────────
    /// <summary>랭킹방에 들어와 있다 — 거점 다른 연출(가림 · 이름표)이 쉬어도 된다.</summary>
    public static bool IsVisiting => s_open != null;

    // ── Lifecycle ─────────────────────────────────────────────
    private void Update()
    {
        if (_player == null || _stele == null) return;
        bool near = Flat(_player.transform.position - _stele.position).sqrMagnitude <= SteleRange * SteleRange;
        ShowPrompt(near && !RankingBoardOverlay.IsOpen);
        if (near && !RankingBoardOverlay.IsOpen && !UIInputGate.Blocked && Input.GetKeyDown(KeyCode.F))
            RankingBoardOverlay.ShowAsync(_era).Forget();
    }

    private void LateUpdate()
    {
        if (Cam() == null) return;
        if (_prompt != null && _prompt.gameObject.activeSelf) _prompt.transform.rotation = _cam.rotation;
        if (_labels == null) return;
        // 10-29 사용자 「랭킹 팝업 제목이 넘친다」 — 비석 위 「해방기 기록」이 석판 창 위 끝으로 비쳐 창 제목이 넘친 것처럼 보였다. 창이 떠 있으면 숨긴다.
        bool show = !RankingBoardOverlay.IsOpen;
        foreach (var l in _labels)
        {
            if (l == null) continue;
            if (l.enabled != show) l.enabled = show;
            l.transform.rotation = _cam.rotation;
        }
    }

    private void OnDestroy()
    {
        if (s_open == this) s_open = null;
    }

    // ── Public Methods ────────────────────────────────────────
    /// <summary>
    /// 랭킹방에 들렀다 돌아온다 — 불러 두기 · 어둡게 · 홀 시작 자리 · 밝게 · 비석 창(처음 한 번) · 돌아가는 문 기다림 · 어둡게 · 원래 자리 · 밝게.
    /// 끝나면 홀은 내린다. 같은 때 두 번 부르면 뒤의 것은 그냥 돌아간다.
    /// </summary>
    public static async UniTask VisitAsync(StoryEra era, PlayerController player, CancellationToken ct, bool showBoardOnArrival = true)
    {
        if (player == null || s_open != null) return;
        GameObject go = null;
        try
        {
            go = await Managers.AddressableManager.InstantiateAsync(PrefabKey);
            if (go == null || ct.IsCancellationRequested) return;
            // 거점 카메라는 제 방위를 지킨다 — 홀을 그 방위로 돌려 들어서자마자 비석이 앞에 보이게(10-29 사진: 돌아가는 문만 보였다)
            Vector3 camFwd = Camera.main != null ? Vector3.ProjectOnPlane(Camera.main.transform.forward, Vector3.up) : Vector3.forward;
            go.transform.SetPositionAndRotation(HallOrigin, Quaternion.LookRotation(camFwd.sqrMagnitude > 0.01f ? camFwd.normalized : Vector3.forward));
            var hall = go.GetComponent<RankingHall>();
            if (hall == null) { Debug.LogWarning("[RankingHall] 루트에 RankingHall이 없다"); return; }
            s_open = hall;
            hall.Setup(era, player);

            Vector3 backPos = player.transform.position;
            Quaternion backRot = player.transform.rotation;
            await ScreenFade.Out(FadeOutSec, ct);
            Teleport(player, hall._spawnPoint.position, hall._spawnPoint.rotation);
            GameCameraController.Instance?.HandToGameplayCamera(player.transform, alignHeadingToTarget: true);
            bool vista = hall.BeginVista();   // 10-29 화면이 밝아지기 전에 카메라를 난간 밖(아래 풍경 쪽)에 세운다
            await ScreenFade.In(FadeInSec, ct);
            Debug.Log($"[RankingHall] 도착 — {era}");
            if (vista) await hall.PlayVistaAsync(player, ct);

            if (showBoardOnArrival) await RankingBoardOverlay.ShowAsync(era);
            await UniTask.WaitUntil(() => hall == null || hall._returnRequested, cancellationToken: ct);

            await ScreenFade.Out(FadeOutSec, ct);
            Teleport(player, backPos, backRot);
            GameCameraController.Instance?.HandToGameplayCamera(player.transform, alignHeadingToTarget: true);
            ReleaseHall(ref go);
            await ScreenFade.In(FadeInSec, ct);
            Debug.Log("[RankingHall] 돌아옴");
        }
        catch (OperationCanceledException) { }
        finally
        {
            ReleaseHall(ref go);
            s_open = null;
        }
    }

    /// <summary>돌아가는 문 트리거(<see cref="RankingHallDoor"/>)가 부른다.</summary>
    public void RequestReturn() => _returnRequested = true;

    // ── Private Methods ───────────────────────────────────────
    /// <summary>둘러보기 첫 자리 — 수동 카메라로 난간 밖에 세운다. 자리가 없으면(옛 프리팹) false.</summary>
    private bool BeginVista()
    {
        var cam = GameCameraController.Instance;
        if (cam == null || _vistaFrom == null || _vistaTo == null || _vistaLook == null) return false;
        cam.TakeManualControl();
        cam.transform.SetPositionAndRotation(_vistaFrom.position, _vistaFrom.rotation);
        return true;
    }

    /// <summary>
    /// 10-29 사용자 「높은 탑에서 아래의 던전 풍경을 웅장하게」 — 게임 카메라는 36° 남짓 내려다봐 아래 풍경이 화면 위 끝에만 걸린다.
    /// 도착할 때 한 번, 숲 구역을 내려다보던 카메라가 뒤 위로 물러나며 꼭대기와 너머 네 구역을 보여 주고 플레이어 뒤로 돌아온다. 그동안 조작은 잠근다.
    /// </summary>
    private async UniTask PlayVistaAsync(PlayerController player, CancellationToken ct)
    {
        var cam = GameCameraController.Instance;
        if (cam == null || player == null) return;
        player.SetInputEnabled(false);
        try
        {
            await cam.MoveManualCameraAsync(_vistaTo.position, _vistaLook.position, VistaSeconds, ct);
            await cam.ReturnToPlayerAsync(player.transform, VistaReturn, ct);
        }
        catch (OperationCanceledException)
        {
            if (cam != null && player != null) cam.HandToGameplayCamera(player.transform, alignHeadingToTarget: true);
            throw;
        }
        finally { if (player != null) player.SetInputEnabled(true); }
    }

    private void Setup(StoryEra era, PlayerController player)
    {
        _era = era;
        _player = player;
        bool nightmare = era == StoryEra.NightmareMode;
        Color c = nightmare ? _nightmareColor : _liberationColor;
        foreach (var l in _themeLights) if (l != null) l.color = c;
        var mpb = new MaterialPropertyBlock();
        foreach (var r in _tintRenderers)
        {
            if (r == null) continue;
            r.GetPropertyBlock(mpb);
            mpb.SetColor(BaseColorId, Color.Lerp(Color.white, c, nightmare ? 0.55f : 0.25f));
            mpb.SetColor(EmissionColorId, c * (nightmare ? 0.35f : 0.2f));
            r.SetPropertyBlock(mpb);
        }
        if (_title != null)
        {
            _title.text  = nightmare ? "악몽 기록" : "해방기 기록";
            _title.color = Color.Lerp(c, Color.white, 0.3f);
        }
        _labels = GetComponentsInChildren<TextMeshPro>(true);
        BuildPrompt();
    }

    private void BuildPrompt()
    {
        if (_stele == null || _prompt != null) return;
        var go = new GameObject("InteractPrompt");
        go.transform.SetParent(_stele, false);
        go.transform.localPosition = Vector3.up * 3.9f;   // 제목(2.9 m) 위 — 겹쳤다(10-29 사진)
        _prompt = go.AddComponent<TextMeshPro>();
        _prompt.text             = $"<color={UIPalette.GoldHex}>[F]</color> 기록 보기";
        _prompt.fontSize         = 4f;
        _prompt.alignment        = TextAlignmentOptions.Center;
        _prompt.color            = Color.white;
        _prompt.textWrappingMode = TextWrappingModes.NoWrap;
        _prompt.sortingOrder     = UISortingOrder.WorldPrompt;
        TMPOutlineHelper.ApplySoftShadow(_prompt);
        go.SetActive(false);
    }

    private void ShowPrompt(bool on)
    {
        if (_prompt != null && _prompt.gameObject.activeSelf != on) _prompt.gameObject.SetActive(on);
    }

    private Transform Cam()
    {
        if (_cam == null && Camera.main != null) _cam = Camera.main.transform;
        return _cam;
    }

    private static void Teleport(PlayerController p, Vector3 pos, Quaternion rot)
    {
        p.transform.SetPositionAndRotation(pos, rot);
        if (p.Rigid != null) { p.Rigid.position = pos; p.Rigid.rotation = rot; p.Rigid.linearVelocity = Vector3.zero; }
    }

    private static void ReleaseHall(ref GameObject go)
    {
        if (go == null) return;
        if (!Managers.AddressableManager.ReleaseInstance(go)) Destroy(go);
        go = null;
    }

    private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
}
