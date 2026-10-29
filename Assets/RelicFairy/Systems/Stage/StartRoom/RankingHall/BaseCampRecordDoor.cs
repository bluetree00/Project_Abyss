using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;

/// <summary>
/// 거점 「기록의 문」(10-29 설계 §5-3 · 2a) — 해방기를 끝낸 뒤 성문 곁(옛 악몽 갈림길 받침 자리)에 서는 상호작용 하나.
///   · [F] → 랭킹방(<see cref="RankingHall.VisitAsync"/>) — 도착 · 비석 창 · 돌아가는 문 · 이 자리.
///   · 악몽까지 깼으면(<see cref="RankingFlow.HasClearedNightmare"/>) 먼저 「해방기 기록 / 악몽 기록」 두 장 창(<see cref="RankingRoomChoice"/>) — 닫으면 그대로.
/// 받침 · 수정 · 빛은 <see cref="BaseCampFxDirector"/>가 세워 넘긴다(엔딩 전엔 아예 만들지 않는다).
/// </summary>
[RequireComponent(typeof(SphereCollider))]
public sealed class BaseCampRecordDoor : MonoBehaviour
{
    // ── Constants ─────────────────────────────────────────────
    private const float LabelAboveCrystal  = 1.05f;   // 금빛 수정 끝에 이름표가 걸렸다(10-29 사진) — 옛 갈림길 0.75보다 위
    private const float PromptAboveCrystal = 1.5f;
    private const float SpinDegPerSec      = 16f;
    private const float BobAmplitude       = 0.08f;
    private const float BobSpeed           = 1.3f;

    // ── Private ───────────────────────────────────────────────
    private Transform   _crystal;
    private Vector3     _crystalBase;
    private Color       _accent;
    private PlayerController _player;
    private Transform   _cam;
    private TextMeshPro _label;
    private GameObject  _prompt;
    private bool        _busy;
    private bool        _ready;   // 드러남 연출이 끝났다 — 그 전엔 [F]를 받지 않는다

    // ── Lifecycle ─────────────────────────────────────────────
    private void Awake() => GetComponent<SphereCollider>().isTrigger = true;

    private void Update()
    {
        if (_crystal != null)
        {
            _crystal.Rotate(0f, SpinDegPerSec * Time.deltaTime, 0f, Space.World);
            _crystal.localPosition = _crystalBase + Vector3.up * (Mathf.Sin(Time.time * BobSpeed) * BobAmplitude);
        }
        Billboard();
        if (!_ready || _busy || _player == null || UIInputGate.Blocked) return;
        if (Input.GetKeyDown(KeyCode.F)) VisitAsync(_player, this.GetCancellationTokenOnDestroy()).Forget();
    }

    private void OnDestroy() => BaseCampLabelRule.Unregister(transform);

    private void OnTriggerEnter(Collider other)
    {
        var p = other.GetComponentInParent<PlayerController>();
        if (p == null) return;
        _player = p;
        ShowPrompt(_ready && !_busy);
    }

    private void OnTriggerExit(Collider other)
    {
        var p = other.GetComponentInParent<PlayerController>();
        if (p != null && p == _player) { _player = null; ShowPrompt(false); }
    }

    // ── Public Methods ────────────────────────────────────────

    /// <param name="crystalHeight">받침 위 수정 높이(로컬 m) — 이름표·[F] 안내를 그 위에 띄운다.</param>
    public void Setup(Transform crystal, Color accent, float crystalHeight)
    {
        _crystal     = crystal;
        _crystalBase = crystal != null ? crystal.localPosition : Vector3.zero;
        _accent      = accent;
        CreateTexts(crystalHeight);
        // 「가까운 곳 하나만」 이름표 규칙 — 곁에 오면 옆 유물 성소 이름표 대신 이 이름표가 뜬다(옛 갈림길과 같은 자리 · 10-01 f5)
        BaseCampLabelRule.Register(transform);
    }

    /// <summary>드러남 연출이 끝났다 — 이제 [F]를 받는다.</summary>
    public void MarkReady()
    {
        _ready = true;
        ShowPrompt(_player != null && !_busy);
    }

    // ── Private Methods ───────────────────────────────────────

    private async UniTaskVoid VisitAsync(PlayerController player, CancellationToken ct)
    {
        _busy = true;
        ShowPrompt(false);
        try
        {
            var era = StoryEra.Liberated;
            if (RankingFlow.HasClearedNightmare)
            {
                var pick = new UniTaskCompletionSource<StoryEra?>();
                RankingRoomChoice.Show(e => pick.TrySetResult(e), () => pick.TrySetResult(null));
                var picked = await pick.Task.AttachExternalCancellation(ct);
                if (!picked.HasValue) return;
                era = picked.Value;
            }
            await RankingHall.VisitAsync(era, player, ct);
        }
        catch (OperationCanceledException) { }
        finally
        {
            _busy = false;
            ShowPrompt(_ready && _player != null);
        }
    }

    // ── 월드 이름표 · [F] 안내(옛 악몽 갈림길과 같은 결) ──

    private void CreateTexts(float crystalHeight)
    {
        var labelGo = new GameObject("RecordLabel");
        labelGo.transform.SetParent(transform, false);
        labelGo.transform.localPosition = Vector3.up * (crystalHeight + LabelAboveCrystal);
        _label = labelGo.AddComponent<TextMeshPro>();
        _label.text             = "기록의 문";
        _label.fontSize         = 3f;
        _label.alignment        = TextAlignmentOptions.Center;
        _label.color            = Color.Lerp(_accent, Color.white, 0.35f);
        _label.textWrappingMode = TextWrappingModes.NoWrap;
        _label.sortingOrder     = UISortingOrder.WorldLabel;
        TMPOutlineHelper.ApplySoftShadow(_label);

        _prompt = new GameObject("InteractPrompt");
        _prompt.transform.SetParent(transform, false);
        _prompt.transform.localPosition = Vector3.up * (crystalHeight + PromptAboveCrystal);
        var tmp = _prompt.AddComponent<TextMeshPro>();
        tmp.text             = $"<color={UIPalette.GoldHex}>[F]</color> 기록 보기";
        tmp.fontSize         = 4f;
        tmp.alignment        = TextAlignmentOptions.Center;
        tmp.color            = Color.white;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.sortingOrder     = UISortingOrder.WorldPrompt;
        TMPOutlineHelper.ApplySoftShadow(tmp);
        _prompt.SetActive(false);
    }

    private void Billboard()
    {
        if (_cam == null)
        {
            var cam = Camera.main;
            if (cam == null) return;
            _cam = cam.transform;
        }
        if (_label != null)
        {
            _label.transform.rotation = _cam.rotation;
            bool show = BaseCampLabelRule.IsShown(transform);
            if (_label.enabled != show) _label.enabled = show;
        }
        if (_prompt != null && _prompt.activeSelf) _prompt.transform.rotation = _cam.rotation;
    }

    private void ShowPrompt(bool on)
    {
        if (_prompt != null && _prompt.activeSelf != on) _prompt.SetActive(on);
    }
}
