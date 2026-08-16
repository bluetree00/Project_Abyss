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

    /// <summary>제단 비주얼(떠 있는 마도서) Addressable 키. 없으면 콜라이더만 있는 투명 제단이 된다.</summary>
    private const string TomeVisualKey  = "Covenant/CovenantTome";
    private const float  TomeVisualY    = 1.0f;    // 바닥에서 책이 떠 있는 높이
    private const float  TomeSpinSpeed  = 18f;     // 초당 회전(도) — 눈에 띄되 어지럽지 않게
    private const float  TomeBobHeight  = 0.12f;   // 상하 부유 진폭
    private const float  TomeBobSpeed   = 1.6f;

    // ── [SerializeField] ─────────────────────────────────
    [Header("월드 텍스트")]
    [SerializeField] private TMP_FontAsset worldTextFont;
    // 마도서가 바닥에서 1.0 높이에 떠 있어(TomeVisualY) 1.2에 두면 글자가 책에 걸쳐 보였다.
    // 프롬프트(1.8) 위로 올려 [책 → [F] 안내 → 이름] 순으로 읽히게 한다.
    [SerializeField] private float textHeight = 2.15f;
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
    private Transform    _tomeVisual;      // 떠 있는 마도서(있으면 회전·부유)
    private Vector3      _tomeBasePos;

    // ── Lifecycle ────────────────────────────────────────
    private void Start()
    {
        _camTransform = Camera.main != null ? Camera.main.transform : null;
        CreateWorldText();
        CreatePrompt();
        TrySpawnFirstTimeGuide();
        SpawnTomeVisualAsync(this.GetCancellationTokenOnDestroy()).Forget();
    }

    private void Update()
    {
        BillboardTexts();
        AnimateTome();

        if (_opening || _used || !_playerInRange) return;
        if (UIInputGate.Blocked) return;
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

    /// <summary>제단 비주얼(마도서) 로드·부착. 키가 없거나 실패하면 조용히 넘어간다(제단 기능은 그대로).</summary>
    private async UniTaskVoid SpawnTomeVisualAsync(System.Threading.CancellationToken ct)
    {
        var addr = Managers.AddressableManager;
        if (addr == null) return;

        GameObject prefab;
        try { prefab = await addr.TryLoadAssetAsync<GameObject>(TomeVisualKey); }
        catch (OperationCanceledException) { return; }
        if (this == null) return;
        if (prefab == null)
        {
            // 조용히 넘기면 "제단이 안 보인다"로만 드러나 원인 추적이 오래 걸린다.
            Debug.LogWarning($"[WorldCovenantAltar] '{TomeVisualKey}' 로드 실패 — 마도서 없이 투명 제단으로 동작. " +
                             "Addressables 주소가 끊겼는지 확인할 것.");
            return;
        }

        _tomeBasePos = transform.position + Vector3.up * TomeVisualY;
        var go = Instantiate(prefab, _tomeBasePos, Quaternion.identity, transform);
        go.name = "CovenantTome";
        _tomeVisual = go.transform;
    }

    /// <summary>마도서를 천천히 회전·부유시켜 "벼려지길 기다리는 서약"으로 읽히게 한다.</summary>
    private void AnimateTome()
    {
        if (_tomeVisual == null) return;
        _tomeVisual.Rotate(0f, TomeSpinSpeed * Time.deltaTime, 0f, Space.World);
        float bob = Mathf.Sin(Time.time * TomeBobSpeed) * TomeBobHeight;
        _tomeVisual.position = _tomeBasePos + new Vector3(0f, bob, 0f);
    }

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

    /// <summary>
    /// 소진 처리 — "서약 완료"를 잠깐 보여준 뒤 <b>제단을 필드에서 치운다.</b>
    ///
    /// 예전에는 라벨만 "서약 완료"로 바꾸고 오브젝트를 그대로 뒀다. 이미 역할이 끝난 제단이
    /// 방을 나갈 때까지 남아 상호작용할 것처럼 보였다(무기대·각성 제단은 획득 즉시 사라진다 — 그 규칙에 맞춘다).
    /// </summary>
    private void SetSpentVisual()
    {
        ShowPrompt(false);
        if (_worldText != null)
        {
            _worldText.text  = "서약 완료";
            _worldText.color = new Color(0.55f, 0.5f, 0.6f);
        }
        DespawnAsync(this.GetCancellationTokenOnDestroy()).Forget();
    }

    /// <summary>완료 표기를 읽을 시간을 준 뒤 디졸브로 사라진다.</summary>
    private async UniTaskVoid DespawnAsync(System.Threading.CancellationToken ct)
    {
        try { await UniTask.Delay(TimeSpan.FromSeconds(SpentLingerSec), cancellationToken: ct); }
        catch (OperationCanceledException) { return; }

        if (this == null) return;

        // 라벨/안내는 디졸브 대상에서 제외된다(TMP는 디졸브 머티리얼이 씌워지면 깨진다).
        // 제단만 사라지고 "서약 완료" 글자만 공중에 남는 그림을 막기 위해 여기서 먼저 끈다.
        if (_worldText != null) _worldText.gameObject.SetActive(false);
        ShowPrompt(false);

        DissolveEffect.PlayDisappear(gameObject, 0.6f, () => { if (this != null) Destroy(gameObject); });
    }

    /// <summary>"서약 완료" 표기를 유지하는 시간(초). 이후 제단이 사라진다.</summary>
    private const float SpentLingerSec = 1.4f;

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
        _promptText.text = $"<color={UIPalette.GoldHex}>[F]</color> 서약 조립";

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
