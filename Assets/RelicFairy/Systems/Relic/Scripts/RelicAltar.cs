using System;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;

/// <summary>
/// 시작방 유물 제단. CombatGirl 플레이어가 범위에 들어와 F를 누르면 해당 유물을 선택한다.
/// 유물 단일 선택(스왑형) — 선택 시 로드아웃에 기록하고 그 제단만 숨긴다. 다른 제단을 선택하면
/// 이전 선택 제단은 다시 등장(재선택 가능). 실제 유물 효과 적용은 던전 진입 시(지연 적용).
/// 캐릭터 전시(스폰)와 분리된 별도 오브젝트. (WorldAwakeningAltar 상호작용 패턴 기반)
/// </summary>
[RequireComponent(typeof(Collider))]
public class RelicAltar : MonoBehaviour
{
    private const float PromptOffsetY = 1.8f;

    // 현재 선택된 유물 제단(단 하나). 다른 제단 선택 시 이전 것을 복귀시키기 위한 공유 상태.
    private static RelicAltar s_selected;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => s_selected = null;

    [Header("유물")]
    [SerializeField] private RelicClassSO relicClass;

    [Header("표현 이펙트")]
    [Tooltip("유물을 표현하는 VFX(형제 오브젝트). 큐브 메시 대체 — 제단 선택 시 함께 숨기고 복귀 시 다시 표시.")]
    [SerializeField] private GameObject relicVfx;

    [Header("월드 텍스트")]
    [SerializeField] private TMP_FontAsset worldTextFont;
    [SerializeField] private float textHeight = 1.4f;
    [SerializeField] private float textSize = 3f;

    private PlayerController _player;
    private bool _claimed;
    private Transform _camTransform;
    private TextMeshPro _worldText;
    private GameObject _promptGo;
    private TextMeshPro _promptText;

    public RelicClassSO RelicClass => relicClass;

    private void Start()
    {
        _camTransform = Camera.main != null ? Camera.main.transform : null;
        CreateWorldText();
        CreatePrompt();
    }

    private void Update()
    {
        BillboardTexts();
        if (_claimed || _player == null) return;
        if (UIInputGate.Blocked) return;
        if (Input.GetKeyDown(KeyCode.F)) OpenInfoPopup();
    }

    private void OnTriggerEnter(Collider other)
    {
        var p = other.GetComponentInParent<PlayerController>();
        if (p == null || _claimed) return;
        _player = p;
        ShowPrompt(true);
    }

    private void OnTriggerExit(Collider other)
    {
        var p = other.GetComponentInParent<PlayerController>();
        if (p != null && p == _player)
        {
            _player = null;
            ShowPrompt(false);
        }
    }

    /// <summary>
    /// F — 곧바로 선택하지 않고 <b>유물 정보 팝업</b>을 먼저 띄운다.
    /// 무엇을 고르는지 모르고 확정하던 흐름이었다(제단 위엔 이름만 떠 있었다).
    /// 팝업에서 '선택'을 눌러야 Claim이 실제로 실행된다.
    /// </summary>
    // 중복 오픈 가드 — 팝업은 BlocksGameplay라 timeScale=0이 되지만 Input.GetKeyDown(F)는 raw라 계속 먹힌다.
    // F를 두 번 누르면 두 번째 유물 팝업이 스택 밑에 좀비로 남아, 위 팝업을 닫아도 시간정지가 영구 고착됐다.
    private bool _opening;

    private void OpenInfoPopup()
    {
        if (_player == null || relicClass == null) return;
        if (s_selected == this) return;   // 이미 선택된 제단 — 무동작
        if (_opening || Managers.UI.HasPopup<UI_RelicInfoPopup>()) return;   // 이미 열림/여는 중 — 중복 금지

        OpenInfoPopupAsync().Forget();
    }

    private async UniTaskVoid OpenInfoPopupAsync()
    {
        _opening = true;
        try
        {
            var popup = await Managers.UI.ShowPopupUIAndGetAsync<UI_RelicInfoPopup>();
            if (popup == null) { Claim(); return; }   // 팝업 로드 실패 시 기존 동작으로 폴백
            popup.Setup(relicClass, Claim);
        }
        catch (OperationCanceledException) { }
        finally { _opening = false; }
    }

    private void Claim()
    {
        if (relicClass == null) return;
        if (s_selected == this) return; // 이미 선택된 제단 — 무동작

        // 이전 선택 유물 복귀(다시 선택 가능)
        if (s_selected != null) s_selected.Deselect();

        // 로드아웃에 기록 후, 허브 플레이어를 재스폰해 유물을 클린하게 적용(재스폰-온-스왑).
        // 허브에서 바로 유물 스킬을 테스트할 수 있고, 다른 유물로 바꾸면 중첩 없이 교체된다.
        s_selected = this;
        AppBootstrapper.Instance?.Loadout?.SetRelic(relicClass);

        // 퀘스트: 유물 획득 보고 (CombatGirl 제단 경로)
        QuestEvents.Report("Relic", relicClass.DisplayName);

        Debug.Log($"[RelicAltar] 유물 선택: {relicClass.DisplayName} ({relicClass.Id})");

        BaseCampBootstrapper.Instance?.RespawnWithLoadout();

        Select();
    }

    /// <summary>이 유물을 선택 상태로 — 제단을 숨긴다(GO 비활성, 머티리얼 보존).</summary>
    private void Select()
    {
        _claimed = true;
        _player = null;
        ShowPrompt(false);
        if (relicVfx != null) relicVfx.SetActive(false);   // 유물 표현 이펙트도 함께 숨김
        gameObject.SetActive(false);
    }

    /// <summary>선택 해제 — 제단을 디졸브로 다시 등장시켜 재선택 가능하게 한다.</summary>
    private void Deselect()
    {
        _claimed = false;
        _player = null;
        ShowPrompt(false);
        if (relicVfx != null) relicVfx.SetActive(true);    // 복귀 시 유물 표현 이펙트 재등장
        DissolveEffect.PlayAppear(gameObject, 0.5f);
    }

    // ── 월드 텍스트 / 프롬프트 (WorldAwakeningAltar 패턴) ──────────────────────

    private void BillboardTexts()
    {
        if (_camTransform == null) return;
        if (_worldText != null) _worldText.transform.rotation = _camTransform.rotation;
        if (_promptGo != null && _promptGo.activeSelf) _promptGo.transform.rotation = _camTransform.rotation;
    }

    private void CreateWorldText()
    {
        var go = new GameObject("RelicLabel");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.up * textHeight;

        _worldText = go.AddComponent<TextMeshPro>();
        if (worldTextFont != null) _worldText.font = worldTextFont;
        _worldText.text = relicClass != null ? relicClass.DisplayName : "유물";
        _worldText.fontSize = textSize;
        _worldText.alignment = TextAlignmentOptions.Center;
        _worldText.color = new Color(0.9f, 0.7f, 0.2f);
        _worldText.textWrappingMode = TextWrappingModes.NoWrap;
        _worldText.sortingOrder = UISortingOrder.WorldLabel;
        TMPOutlineHelper.ApplyDefault(_worldText);
    }

    private void CreatePrompt()
    {
        _promptGo = new GameObject("InteractPrompt");
        _promptGo.transform.SetParent(transform, false);
        _promptGo.transform.localPosition = Vector3.up * PromptOffsetY;

        _promptText = _promptGo.AddComponent<TextMeshPro>();
        if (worldTextFont != null) _promptText.font = worldTextFont;
        _promptText.fontSize = 4f;
        _promptText.alignment = TextAlignmentOptions.Center;
        _promptText.color = Color.white;
        _promptText.textWrappingMode = TextWrappingModes.NoWrap;
        _promptText.sortingOrder = UISortingOrder.WorldPrompt;
        TMPOutlineHelper.ApplyDefault(_promptText);
        _promptText.text = "<color=#FFD700>[F]</color> 유물 선택";

        _promptGo.SetActive(false);
    }

    private void ShowPrompt(bool on)
    {
        if (_promptGo != null) _promptGo.SetActive(on);
    }
}
