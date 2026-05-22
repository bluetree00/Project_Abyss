using UnityEngine;
using TMPro;

/// <summary>
/// 버프 타일 상호작용 컴포넌트.
///
/// ■ BuffBox (상자)
///   플레이어가 트리거 영역 안에서 상호작용 키를 눌러야 발동.
///   근처에 가면 "[F] 열기" 월드 팝업 표시.
///   1회 사용 후 비활성화.
///
/// ■ BuffPedestal (발판)
///   플레이어가 밟으면 즉시 발동.
///   1회 사용 후 비활성화.
///
/// ■ 공통
///   BuffRoller.Roll() → 버프/디버프 결정
///   → RoomBuffHandler.AddBuff() 또는 즉시 피해
/// </summary>
public class BuffTileInteraction : MonoBehaviour
{
    // ── 상수 ────────────────────────────────────────────────
    private const int DefaultRoomDuration = 1;
    private const float PromptOffsetY = 1.2f;

    // ── 직렬화 필드 ─────────────────────────────────────────
    [Header("타일 설정")]
    [SerializeField] private bool isPedestal;
    [SerializeField] private int roomDuration = DefaultRoomDuration;

    // ── 비공개 필드 ─────────────────────────────────────────
    private bool _used;
    private bool _playerInRange;
    private HudPresenter _hud;
    private GameObject _promptGo;
    private TextMeshPro _promptText;

    // ── Properties ──────────────────────────────────────────
    public bool IsPedestal => isPedestal;
    public bool IsUsed => _used;

    // ── Lifecycle ───────────────────────────────────────────

    private void Update()
    {
        // 프롬프트 빌보드 (카메라 방향으로 회전)
        if (_promptGo != null && _promptGo.activeSelf && Camera.main != null)
            _promptGo.transform.rotation = Camera.main.transform.rotation;

        if (_used || isPedestal || !_playerInRange) return;

        if (Input.GetKeyDown(KeyCode.F))
            Activate();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_used) return;
        if (!IsPlayer(other)) return;

        _playerInRange = true;

        if (isPedestal)
        {
            Activate();
        }
        else
        {
            ShowPrompt(true);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (!IsPlayer(other)) return;
        _playerInRange = false;
        ShowPrompt(false);
    }

    private void OnDestroy()
    {
        if (_promptGo != null)
            Destroy(_promptGo);
    }

    // ── Public Methods ──────────────────────────────────────

    /// <summary>런타임에 상자/발판 모드 설정 (MapBuilder에서 호출).</summary>
    public void Setup(bool pedestal, int duration = DefaultRoomDuration)
    {
        isPedestal = pedestal;
        roomDuration = duration;
    }

    // ── Private Methods ─────────────────────────────────────

    private void Activate()
    {
        if (_used) return;
        _used = true;

        ShowPrompt(false);

        var app = AppBootstrapper.Instance;
        if (app == null || app.CurrentRun == null || !app.CurrentRun.IsRunning)
        {
            Debug.LogWarning("[BuffTile] No active run.");
            return;
        }

        var session = app.CurrentRun;
        var result = BuffRoller.Roll(roomDuration);

        string noticeMsg;

        if (result.IsInstant)
        {
            int damage = (int)result.Modifier.Value;
            session.Player?.RuntimeStats?.Damage(damage);
            noticeMsg = $"<color=#FF4444>즉시 피해 {damage}</color>";
            Debug.Log($"[BuffTile] 즉시 피해 {damage} (티어 {result.Tier})");
        }
        else
        {
            session.BuffHandler.AddBuff(
                result.Modifier,
                result.RoomDuration,
                result.IsPercent,
                result.IsDebuff,
                result.BuffType,
                result.Tier
            );

            string typeName = GetStatName(result.Modifier.Type);
            string sign = result.Modifier.Value >= 0 ? "+" : "";
            string valueStr = result.IsPercent
                ? $"{sign}{result.Modifier.Value * 100f:F0}%"
                : $"{sign}{result.Modifier.Value:F0}";
            string color = result.IsDebuff ? "#FF6666" : "#66CCFF";

            noticeMsg = $"<color={color}>{typeName} {valueStr}</color> ({result.RoomDuration}방)";
            Debug.Log($"[BuffTile] {(result.IsDebuff ? "디버프" : "버프")} T{result.Tier} {result.Modifier.Type} {valueStr}");
        }

        ShowNotice(noticeMsg);
        OnUsed();
    }

    private void OnUsed()
    {
        var col = GetComponent<Collider>();
        if (col != null) col.enabled = false;

        var renderers = GetComponentsInChildren<Renderer>();
        foreach (var r in renderers)
        {
            var mat = r.material;
            if (mat.HasProperty("_Color"))
            {
                var c = mat.color;
                c.a = 0.3f;
                mat.color = c;
            }
        }
    }

    // ── 월드 프롬프트 ([F] 열기) ────────────────────────────

    private void ShowPrompt(bool show)
    {
        if (show && _promptGo == null)
            CreatePrompt();

        if (_promptGo != null)
            _promptGo.SetActive(show && !_used);
    }

    private void CreatePrompt()
    {
        _promptGo = new GameObject("InteractPrompt");
        _promptGo.transform.SetParent(transform, false);
        _promptGo.transform.localPosition = Vector3.up * PromptOffsetY;

        _promptText = _promptGo.AddComponent<TextMeshPro>();
        _promptText.text = "<color=#FFD700>[F]</color> 열기";
        _promptText.fontSize = 4f;
        _promptText.alignment = TextAlignmentOptions.Center;
        _promptText.color = Color.white;
        _promptText.enableWordWrapping = false;

        var rect = _promptGo.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(3f, 1f);

        TMPOutlineHelper.ApplyDefault(_promptText);

        _promptGo.SetActive(false);
    }

    // ── HUD 알림 ────────────────────────────────────────────

    private void ShowNotice(string message)
    {
        if (_hud == null)
            _hud = FindObjectOfType<HudPresenter>(true);
        _hud?.ShowBuffNotice(message);
    }

    private static string GetStatName(StatType type) => type switch
    {
        StatType.MoveSpeed    => "이동속도",
        StatType.AttackPower  => "공격력",
        StatType.MeleeAttack  => "근접공격",
        StatType.RangedAttack => "원거리공격",
        StatType.Defense      => "방어력",
        StatType.AttackSpeed  => "공격속도",
        StatType.Projectile   => "투사체",
        _                     => type.ToString(),
    };

    private static bool IsPlayer(Collider col)
    {
        return col.GetComponentInParent<PlayerController>() != null;
    }
}
