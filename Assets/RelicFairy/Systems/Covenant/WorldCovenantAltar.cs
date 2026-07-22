using System;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;

/// <summary>
/// 챕터 시작 대기방에 배치되는 "조립 서약" 제단. 범위에서 F를 누르면 UI_CovenantAssemble 팝업을 연다.
/// 플레이어가 원인×효과를 골라 서약을 벼려내고(첫 서약=실버 고정), 성공 시 제단은 소진된다(대기방당 1회 획득).
/// (기존 사전제작 3지선다 WorldCovenantPickup/CovenantChoiceUI를 대체.)
/// </summary>
[RequireComponent(typeof(Collider))]
public class WorldCovenantAltar : MonoBehaviour
{
    /// <summary>서약 조립이 성공해 제단이 소진된 순간 발생. 시작방 게이트 열림 연출 등이 구독.</summary>
    public static event Action OnCovenantAssembled;

    // ── Constants ────────────────────────────────────────
    private const float PromptOffsetY = 1.8f;
    private const string GuideShownKey = "covenant_altar_guide_v1";   // 최초 1회 가이드(계정 영속)

    // ── [SerializeField] ─────────────────────────────────
    [Header("월드 텍스트")]
    [SerializeField] private TMP_FontAsset worldTextFont;
    [SerializeField] private float textHeight = 1.2f;
    [SerializeField] private float textSize   = 3f;

    // ── Private ──────────────────────────────────────────
    private bool         _playerInRange;
    private bool         _opening;
    private bool         _used;
    private Transform    _camTransform;
    private TextMeshPro  _worldText;
    private GameObject   _promptGo;
    private TextMeshPro  _promptText;
    private OnboardingGuideArrow _guideArrow;

    // ── Lifecycle ────────────────────────────────────────
    private void Start()
    {
        _camTransform = Camera.main != null ? Camera.main.transform : null;
        CreateWorldText();
        CreatePrompt();
        TrySpawnFirstTimeGuide();
    }

    private void Update()
    {
        BillboardTexts();

        if (_opening || _used || !_playerInRange) return;
        if (Input.GetKeyDown(KeyCode.F))
            OpenAsync(this.GetCancellationTokenOnDestroy()).Forget();
    }

    // ── Public Methods ───────────────────────────────────
    public static WorldCovenantAltar SpawnAt(Vector3 position)
    {
        var go = new GameObject("CovenantAltar");
        go.transform.position = position;

        var col = go.AddComponent<SphereCollider>();
        col.isTrigger = true;
        col.radius = 2f;

        return go.AddComponent<WorldCovenantAltar>();
    }

    // ── Private Methods ──────────────────────────────────
    private async UniTaskVoid OpenAsync(System.Threading.CancellationToken ct)
    {
        var run = GameRunBootstrapper.Instance?.Run;
        if (run?.CovenantHandler == null) return;

        if (run.CovenantHandler.Covenants.Count >= CovenantHandler.MaxCovenants)
        {
            ShowNotice("서약이 가득 찼다");
            return;
        }

        _opening = true;
        ShowPrompt(false);

        try
        {
            bool forceSilver = run.CovenantHandler.Covenants.Count == 0;
            string id = await CovenantAssembleUI.ChooseAsync(forceSilver, null, ct);

            if (id != null && run.CovenantHandler.TryAdd(id))
            {
                _used = true;
                var covenant = run.CovenantHandler.Find(id);
                ShowNotice($"<color=#CC88FF>서약</color> {covenant?.DisplayName ?? id} 새김!");
                SetSpentVisual();
                ClearGuide(markDone: true);   // 첫 서약 완성 → 가이드 종료(영속 기록)
                OnCovenantAssembled?.Invoke();  // 시작방 게이트 열림 연출 트리거(수신측이 딜레이 적용)
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            Debug.LogWarning($"[WorldCovenantAltar] 조립 팝업 실패: {e.Message}");
        }
        finally
        {
            _opening = false;
            if (!_used && _playerInRange) ShowPrompt(true);
        }
    }

    private static void ShowNotice(string msg)
    {
        var hud = UnityEngine.Object.FindFirstObjectByType<HudPresenter>(FindObjectsInactive.Include);
        hud?.ShowBuffNotice(msg);
    }

    private void SetSpentVisual()
    {
        ShowPrompt(false);
        if (_worldText != null)
        {
            _worldText.text  = "서약 완료";
            _worldText.color = new Color(0.55f, 0.5f, 0.6f);
        }
    }

    /// <summary>최초 서약(계정 1회)일 때만 제단으로 유도하는 가이드 화살표+안내를 띄운다.</summary>
    private void TrySpawnFirstTimeGuide()
    {
        if (PlayerPrefs.GetInt(GuideShownKey, 0) == 1) return;
        var run = GameRunBootstrapper.Instance?.Run;
        if (run?.CovenantHandler == null || run.CovenantHandler.Covenants.Count > 0) return;

        var go = new GameObject("CovenantAltarGuide");
        go.transform.SetParent(transform, false);   // 제단 파괴 시 함께 정리
        _guideArrow = go.AddComponent<OnboardingGuideArrow>();
        _guideArrow.SetTarget(transform);
        ShowNotice("<color=#CC88FF>서약 제단</color> — [F]로 나만의 서약을 조립하라");
    }

    private void ClearGuide(bool markDone)
    {
        if (markDone)
        {
            PlayerPrefs.SetInt(GuideShownKey, 1);
            PlayerPrefs.Save();
        }
        if (_guideArrow != null)
        {
            Destroy(_guideArrow.gameObject);
            _guideArrow = null;
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
        _worldText.text = "서약 조립";
        _worldText.fontSize = textSize;
        _worldText.alignment = TextAlignmentOptions.Center;
        _worldText.color = new Color(0.8f, 0.5f, 1f);
        _worldText.textWrappingMode = TextWrappingModes.NoWrap;
        _worldText.sortingOrder = 10;
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
        _promptText.sortingOrder = 11;
        TMPOutlineHelper.ApplyDefault(_promptText);
        _promptText.text = "<color=#FFD700>[F]</color> 서약 조립";

        _promptGo.SetActive(false);
    }

    private void ShowPrompt(bool show) => _promptGo?.SetActive(show && !_used);

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
