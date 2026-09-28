using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;

/// <summary>
/// 베이스캠프 <b>파츠 공방</b> — [F] → 시작 원거리 파츠 선택(<see cref="UI_StartPartPopup"/>). 고른 파츠는
/// 로드아웃(<see cref="PlayerLoadout.StartPartId"/>)에 적히고 런 시작에 Lv1로 켜진다(GameRunSession).
///
/// 규칙(베이스캠프 재설계 §2, 09-27 사용자 결정): 원거리 무기를 먼저 골라야 열린다 · 분열의 시위만 처음부터 열려 있고
/// 나머지 넷은 기억의 제단 「원거리」 갈래에서 연다 · 허브에 있는 동안 몇 번이든 바꿀 수 있다(모루와 달리 한 번에 사라지지 않는다).
/// (WeaponForgeAltar 상호작용 패턴 — 트리거 + F + 월드 글자)
/// </summary>
[RequireComponent(typeof(Collider))]
public sealed class StartPartStation : MonoBehaviour
{
    private const float PromptOffsetY = 1.8f;
    private const float TextHeight    = 1.4f;
    private const string DefaultPartId = "part_split";

    // 파츠 id → 해금 노드(기억의 제단 원거리 갈래). 기본 파츠는 노드가 없다.
    private static readonly Dictionary<string, string> UnlockNode = new()
    {
        { "part_pierce",  MemoryAltarCatalog.PartPierce },
        { "part_power",   MemoryAltarCatalog.PartPower },
        { "part_homing",  MemoryAltarCatalog.PartHoming },
        { "part_explode", MemoryAltarCatalog.PartExplode },
    };

    [Header("월드 텍스트")]
    [SerializeField] private TMP_FontAsset worldTextFont;
    [SerializeField] private float textSize = 3f;

    [Header("안내")]
    [SerializeField, TextArea] private string needRangedHint = "원거리 무기가 없다 — 먼저 <b>장비 공방</b>에서 활이나 석궁을 골라라.";

    private PlayerController _player;
    private bool _busy;
    private Transform _camTransform;
    private TextMeshPro _worldText;
    private GameObject _promptGo;
    private HudPresenter _hud;

    // ── Lifecycle ─────────────────────────────────────────────

    private void Awake() => GetComponent<Collider>().isTrigger = true;

    private void Start()
    {
        _camTransform = Camera.main != null ? Camera.main.transform : null;
        CreateWorldText();
        CreatePrompt();
    }

    private void Update()
    {
        BillboardTexts();
        if (_busy || _player == null) return;
        if (UIInputGate.Blocked) return;
        if (Input.GetKeyDown(KeyCode.F))
            OpenAsync(this.GetCancellationTokenOnDestroy()).Forget();
    }

    private void OnTriggerEnter(Collider other)
    {
        var p = other.GetComponentInParent<PlayerController>();
        if (p == null) return;
        _player = p;
        ShowPrompt(true);
    }

    private void OnTriggerExit(Collider other)
    {
        var p = other.GetComponentInParent<PlayerController>();
        if (p != null && p == _player) { _player = null; ShowPrompt(false); }
    }

    // ── Public Methods ────────────────────────────────────────

    /// <summary>파츠의 해금 여부 — 기본 파츠는 항상, 나머지는 기억의 제단 노드.</summary>
    public static bool IsUnlocked(string partId)
        => partId == DefaultPartId || (UnlockNode.TryGetValue(partId ?? "", out var node) && MemoryAltarService.IsUnlocked(node));

    // ── Private Methods ───────────────────────────────────────

    private async UniTaskVoid OpenAsync(CancellationToken ct)
    {
        var loadout = AppBootstrapper.Instance?.Loadout;
        if (loadout == null) return;

        // 파츠는 원거리 무기에 내장되는 계통 — 무기가 없으면 고를 대상이 없다.
        if (loadout.WeaponSlot1 == null)
        {
            if (_hud == null) _hud = FindFirstObjectByType<HudPresenter>(FindObjectsInactive.Include);
            _hud?.ShowBuffNotice(needRangedHint);
            return;
        }

        var data = Managers.WeaponParts;
        if (data == null || data.All == null || data.All.Count == 0)
        {
            Debug.LogWarning("[StartPartStation] 원거리 파츠 데이터가 없다.");
            return;
        }

        _busy = true;
        ShowPrompt(false);
        try
        {
            var cards = new List<UI_StartPartPopup.Card>(data.All.Count);
            foreach (var part in data.All)
            {
                bool unlocked = IsUnlocked(part.part_id);
                cards.Add(new UI_StartPartPopup.Card
                {
                    Part       = part,
                    Unlocked   = unlocked,
                    UnlockHint = unlocked ? null : $"기억의 제단 「{NodeName(part.part_id)}」에서 해금",
                });
            }

            var popup = await Managers.UI.ShowPopupUIAndGetAsync<UI_StartPartPopup>();
            if (popup == null) return;
            var wait = popup.WaitForInteractionAsync(ct);
            popup.Setup(cards, loadout.StartPartId);
            await wait;
            string chosen = popup.ResultPartId;   // 창은 닫힌 뒤 0.14초 후 파괴 — 바로 읽는다
            if (string.IsNullOrEmpty(chosen) || chosen == loadout.StartPartId) return;

            loadout.SetStartPart(chosen);
            QuestEvents.Report("Parts", chosen);
            // 얻기 연출 — 작업대 궤도 구슬이 원거리 무기(등)로 감겨 들어간다
            BaseCampFxDirector.Instance?.PlayAcquire(transform.position + Vector3.up * 1.3f, BaseCampFxDirector.AcquireKind.Part);
            if (_player != null)
                GuidelineVisual.Toast(_player.transform.position + Vector3.up * 2.4f, data.GetById(chosen)?.part_name ?? chosen, GuidelineVisual.ToastKind.Relic);
            Debug.Log($"[StartPartStation] 시작 파츠: {chosen}");
        }
        catch (OperationCanceledException) { }
        finally
        {
            _busy = false;
            if (_player != null) ShowPrompt(true);
        }
    }

    private static string NodeName(string partId)
        => UnlockNode.TryGetValue(partId ?? "", out var id) ? MemoryAltarCatalog.Get(id)?.DisplayName ?? id : partId;

    // ── World Text / Prompt (WeaponForgeAltar 패턴) ───────────

    private void BillboardTexts()
    {
        if (_camTransform == null) return;
        if (_worldText != null) _worldText.transform.rotation = _camTransform.rotation;
        if (_promptGo != null && _promptGo.activeSelf) _promptGo.transform.rotation = _camTransform.rotation;
    }

    private void CreateWorldText()
    {
        var go = new GameObject("PartsLabel");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.up * TextHeight;

        _worldText = go.AddComponent<TextMeshPro>();
        if (worldTextFont != null) _worldText.font = worldTextFont;
        _worldText.text = "파츠 작업대";   // 구역 이름(「파츠 공방」)은 구역 표지가 — 스테이션 라벨은 물건 이름
        _worldText.fontSize = textSize;
        _worldText.alignment = TextAlignmentOptions.Center;
        _worldText.color = new Color(0.72f, 0.66f, 1f);
        _worldText.textWrappingMode = TextWrappingModes.NoWrap;
        _worldText.sortingOrder = UISortingOrder.WorldLabel;
        TMPOutlineHelper.ApplySoftShadow(_worldText);
    }

    private void CreatePrompt()
    {
        _promptGo = new GameObject("InteractPrompt");
        _promptGo.transform.SetParent(transform, false);
        _promptGo.transform.localPosition = Vector3.up * PromptOffsetY;

        var tmp = _promptGo.AddComponent<TextMeshPro>();
        if (worldTextFont != null) tmp.font = worldTextFont;
        tmp.fontSize = 4f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.sortingOrder = UISortingOrder.WorldPrompt;
        TMPOutlineHelper.ApplySoftShadow(tmp);
        tmp.text = $"<color={UIPalette.GoldHex}>[F]</color> 파츠 고르기";

        _promptGo.SetActive(false);
    }

    private void ShowPrompt(bool on)
    {
        if (_promptGo != null) _promptGo.SetActive(on);
    }
}
