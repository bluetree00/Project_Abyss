using System;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;

/// <summary>
/// BaseCamp 배치용 유물 각성 제단.
/// 범위에 들어와 F 키를 누르면 UI_AwakeningPanel 팝업을 연다.
/// SpawnAt()으로 코드 스폰하거나 씬에 직접 배치 가능.
/// </summary>
[RequireComponent(typeof(Collider))]
public class WorldAwakeningAltar : MonoBehaviour
{
    // ── 직렬화 필드 ──────────────────────────────────────────────────────
    [Header("월드 텍스트")]
    [SerializeField] private TMP_FontAsset worldTextFont;
    [SerializeField] private float textHeight = 1.2f;
    [SerializeField] private float textSize   = 3f;

    // ── 상수 ─────────────────────────────────────────────────────────────
    private const float PromptOffsetY = 1.8f;

    // ── 비공개 필드 ──────────────────────────────────────────────────────
    private bool         _playerInRange;
    private bool         _opening;
    private Transform    _camTransform;
    private TextMeshPro  _worldText;
    private GameObject   _promptGo;
    private TextMeshPro  _promptText;

    // ── Lifecycle ─────────────────────────────────────────────────────────

    private void Start()
    {
        _camTransform = Camera.main != null ? Camera.main.transform : null;
        CreateWorldText();
        CreatePrompt();
    }

    private void Update()
    {
        BillboardTexts();

        if (_opening || !_playerInRange) return;
        if (Input.GetKeyDown(KeyCode.F))
            OpenAsync(this.GetCancellationTokenOnDestroy()).Forget();
    }

    // ── Public 팩토리 ─────────────────────────────────────────────────────

    public static WorldAwakeningAltar SpawnAt(Vector3 position)
    {
        var go = new GameObject("AwakeningAltar");
        go.transform.position = position;

        var col = go.AddComponent<SphereCollider>();
        col.isTrigger = true;
        col.radius = 2f;

        return go.AddComponent<WorldAwakeningAltar>();
    }

    // ── 내부 ──────────────────────────────────────────────────────────────

    private async UniTaskVoid OpenAsync(System.Threading.CancellationToken ct)
    {
        _opening = true;
        ShowPrompt(false);

        try
        {
            await Managers.UI.ShowPopupUIAndGetAsync<UI_AwakeningPanel>();
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            Debug.LogWarning($"[WorldAwakeningAltar] 팝업 로드 실패: {e.Message}");
        }
        finally
        {
            _opening = false;
            if (_playerInRange) ShowPrompt(true);
        }
    }

    private void BillboardTexts()
    {
        if (_camTransform == null) return;
        if (_worldText != null)
            _worldText.transform.rotation = _camTransform.rotation;
        if (_promptGo != null && _promptGo.activeSelf)
            _promptGo.transform.rotation = _camTransform.rotation;
    }

    private void CreateWorldText()
    {
        var go = new GameObject("AltarLabel");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.up * textHeight;

        _worldText = go.AddComponent<TextMeshPro>();
        if (worldTextFont != null) _worldText.font = worldTextFont;
        _worldText.text = "유물 각성";
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
        _promptText.text = "<color=#FFD700>[F]</color> 각성 관리";

        _promptGo.SetActive(false);
    }

    private void ShowPrompt(bool show) => _promptGo?.SetActive(show);

    private void OnTriggerEnter(Collider other)
    {
        if (!IsPlayer(other)) return;
        _playerInRange = true;
        ShowPrompt(true);
    }

    private void OnTriggerExit(Collider other)
    {
        if (!IsPlayer(other)) return;
        _playerInRange = false;
        ShowPrompt(false);
    }

    private static bool IsPlayer(Collider col)
        => col.GetComponentInParent<PlayerController>() != null;
}
