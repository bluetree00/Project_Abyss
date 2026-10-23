using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 저장 슬롯 카드 한 장.
/// 세이브 데이터가 있으면 채워진 상태(filledRoot), 없으면 빈 상태(emptyRoot) 표시.
/// </summary>
public class UI_SaveSlotCard : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────
    // Constants
    // ─────────────────────────────────────────────────────────

    // 카드 글 네 줄(10-16 사용자 「세이브 팝업은 보여줄 정보가 더 필요 · 텍스트 간격도」) — 위계: 어디까지(26 · 500) →
    // 무엇으로(21 · 400) → 상태(21 · 400) → 기록(18 · 300 · 옅게). 세로 그룹이 각 줄을 글자 높이(Noto ≈ 1.45em)로 잡고 그 사이 간격을 더한다.
    private const float HeadSize    = 26f;
    private const float InfoSize    = 21f;
    private const float FootSize    = 18f;
    private const float InfoSpacing = 10f;
    private static readonly Color FootInk = new(0.925f, 0.902f, 0.847f, 0.62f);

    // ─────────────────────────────────────────────────────────
    // SerializeField
    // ─────────────────────────────────────────────────────────

    [Header("상태 루트")]
    [SerializeField] private GameObject filledRoot;
    [SerializeField] private GameObject emptyRoot;

    [Header("채워진 상태 — 텍스트")]
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text progressText;
    [SerializeField] private TMP_Text retryText;
    [SerializeField] private TMP_Text savedAtText;

    [Header("버튼")]
    [SerializeField] private Button   startButton;
    [SerializeField] private TMP_Text startButtonText;
    [SerializeField] private Button   deleteButton;

    // ─────────────────────────────────────────────────────────
    // Private
    // ─────────────────────────────────────────────────────────

    private int  _slotIndex;
    private bool _hasSave;
    private Action<int, bool> _onStart;
    private Action<int>       _onDelete;

    // ─────────────────────────────────────────────────────────
    // Lifecycle
    // ─────────────────────────────────────────────────────────

    private void Awake()
    {
        startButton.onClick.AddListener(OnClickStart);
        if (deleteButton != null)
            deleteButton.onClick.AddListener(OnClickDelete);
        EnsureSavedAtLine();
    }

    // ─────────────────────────────────────────────────────────
    // Public Methods
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// 카드를 초기화한다.
    /// onStart(slotIndex, hasSave) — 게임 시작 클릭 시 호출.
    /// onDelete(slotIndex) — 삭제 클릭 시 호출 (hasSave=true 일 때만 버튼 활성).
    /// </summary>
    public void Setup(RunSaveData data, int slotIndex, Action<int, bool> onStart, Action<int> onDelete = null)
    {
        // 패널이 꺼진 채 먼저 불린다(Awake 전) — 넷째 줄을 여기서 만들어야 이번 글이 그 줄에 들어간다.
        // Awake에서만 만들면 이미 채워진 셋째 줄을 복제해 넷째 줄이 셋째 줄과 같았다(10-16 실측).
        EnsureSavedAtLine();
        _slotIndex = slotIndex;
        _onStart   = onStart;
        _onDelete  = onDelete;
        _hasSave   = data?.hasActiveRun == true;

        filledRoot.SetActive(_hasSave);
        emptyRoot.SetActive(!_hasSave);
        if (deleteButton != null) deleteButton.gameObject.SetActive(_hasSave);
        if (startButtonText != null) startButtonText.text = _hasSave ? "이어하기" : "새 게임";

        // 빈 슬롯 가운데 글이 버튼과 같은 「새 게임」이라 두 번 읽혔다(09-28 UI 전수). DNF + 가짜 굵게는 무거웠다 → Noto 500 · 옅게(10-16).
        if (!_hasSave && emptyRoot != null && emptyRoot.transform.Find("Txt_Empty") is Transform empty
            && empty.TryGetComponent<TMP_Text>(out var emptyText))
        {
            emptyText.text = "빈 슬롯";
            UITheme.StyleThinText(emptyText, UIWeight.Medium, HeadSize, FootInk, shadow: true);
        }
        StyleDeleteBadge();

        if (!_hasSave) return;

        // 슬롯을 고르는 데 필요한 것 — 어디까지 왔나 · 무엇으로 · 지금 상태 · 언제(10-16 — 예전 두 줄 「플레이 시간 · 시도」는 정보가 모자랐다).
        // 네 줄을 넘기지 않는다(09-28 「정보 과밀 제거」 의도 — 아이템 · 시너지 · 골드는 여전히 인게임에서 본다).
        string where = data.isInStartRoom ? "베이스캠프" : $"챕터 {Mathf.Clamp(data.chapter, 1, 4)}";
        if (!data.isInStartRoom && data.progressPercent > 0) where += $"  ·  진행 {data.progressPercent}%";
        SetLine(nameText, where, UIWeight.Medium, HeadSize, UITheme.Thin.Ink);

        string kit, state;
        if (data.isInStartRoom)
        {
            // 10-19 사용자 「세이브 데이터가 딱히 의미 없는 포션이라던지」 — 거점 기록엔 유물 · 무기 · 체력 · 포션이 없다(포션은 새 런에서 받아 늘 0/3).
            // 대신 이 슬롯이 어디까지 왔는지(슬롯 메타): 최고 챕터 · 완주 · 심연의 정수.
            var meta = RunProgressManager.Instance?.PeekMeta(_slotIndex);
            kit   = meta != null ? $"최고 챕터 {Mathf.Clamp(meta.highestChapter, 1, 4)}  ·  완주 {Mathf.Max(0, meta.totalClears)}회" : "";
            state = meta != null ? $"심연의 정수 {Mathf.Max(0, meta.abyssEssence):N0}" : "";
        }
        else
        {
            string relic  = RelicName(data.relicKey);
            string weapon = WeaponName(data.weapon0PrefabKey);
            kit = relic;
            if (!string.IsNullOrEmpty(weapon))
                kit = (string.IsNullOrEmpty(kit) ? "" : kit + "  ·  ") + weapon + (data.weapon0EnhanceLevel > 0 ? $" 재련 {WeaponReforgeService.CountAt(data.weapon0EnhanceLevel)}" : "");

            // 대기방 세이브는 체력이 아직 잡히지 않아 「체력 0 / 1」(SaveSanitizer 하한 1)로 나왔다 — 그땐 체력을 뺀다(10-17).
            state = data.maxHp > 1 ? $"체력 {Mathf.Max(0, data.currentHp)} / {data.maxHp}" : "";
            if (data.potionCapacity > 0) state += (state.Length > 0 ? "  ·  " : "") + $"포션 {data.potionCount}/{data.potionCapacity}";
        }
        SetLine(progressText, kit, UIWeight.Regular, InfoSize, UITheme.Thin.Ink);
        SetLine(retryText, UIKeywordInk.Values(state), UIWeight.Regular, InfoSize, UITheme.Thin.Ink);

        string foot = $"시도 {Mathf.Max(1, data.retryCount)}회  ·  {FormatPlayTime(data.playSeconds)}";
        string saved = FormatSavedAt(data.savedAt);
        if (saved != null) foot += "\n마지막 저장 " + saved;
        SetLine(savedAtText, foot, UIWeight.Light, FootSize, FootInk);

        // 세로 그룹은 위에서 아래로 모으고(가운데 정렬) 줄 사이를 띄운다 — 「늘려 채우기」는 줄을 멀리 떨어뜨렸다(09-28 UI 전수).
        if (filledRoot.TryGetComponent<VerticalLayoutGroup>(out var vlg))
        {
            vlg.childForceExpandHeight = false;
            vlg.childAlignment         = TextAnchor.MiddleCenter;
            vlg.spacing                = InfoSpacing;
        }
    }

    // ─────────────────────────────────────────────────────────
    // Private Methods — Format
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// 넷째 줄(시도 · 시간 · 마지막 저장) — LobbyRoot 프리팹의 SlotCard_1에만 Txt_SavedAt이 없어 1번 슬롯 카드만 세 줄로 나왔다(10-16 로비 점검).
    /// 없으면 셋째 줄을 복제해 바로 아래에 둔다.
    /// </summary>
    private void EnsureSavedAtLine()
    {
        if (savedAtText != null || retryText == null) return;
        savedAtText = Instantiate(retryText, retryText.transform.parent);
        savedAtText.name = "Txt_SavedAt";
        savedAtText.transform.SetSiblingIndex(retryText.transform.GetSiblingIndex() + 1);
    }

    private static void SetLine(TMP_Text t, string text, UIWeight weight, float size, Color color)
    {
        if (t == null) return;
        bool on = !string.IsNullOrEmpty(text);
        t.gameObject.SetActive(on);
        if (!on) return;
        UITheme.StyleThinText(t, weight, size, color, shadow: true);
        t.alignment = TextAlignmentOptions.Center;
        t.lineSpacing = 6f;
        t.text = text;
    }

    /// <summary>삭제 표 — DNF 22 가짜 굵게 58×28은 답답했다 → Noto 500 · 17 · 72×32(10-16).</summary>
    private void StyleDeleteBadge()
    {
        if (deleteButton == null) return;
        if (deleteButton.transform is RectTransform rt) rt.sizeDelta = new Vector2(72f, 32f);
        var label = deleteButton.GetComponentInChildren<TMP_Text>(true);
        if (label != null) UITheme.StyleThinText(label, UIWeight.Medium, 17f, UITheme.Thin.Ink);
    }

    /// <summary>유물 SO 이름(RelicClass_Gawain 등) → 표시 이름. 모르는 키는 비운다.</summary>
    private static string RelicName(string key)
    {
        if (string.IsNullOrEmpty(key)) return "";
        if (key.Contains("Gawain"))   return "가웨인";
        if (key.Contains("Lancelot")) return "랜슬롯";
        return "";
    }

    /// <summary>무기 프리팹 키(T1_Katana 등) → 표시 이름. 모르는 키는 비운다.</summary>
    private static string WeaponName(string key)
    {
        if (string.IsNullOrEmpty(key)) return "";
        if (key.Contains("Nameless"))   return "무형검";
        if (key.Contains("Katana"))     return "카타나";
        if (key.Contains("Greatsword")) return "대검";
        if (key.Contains("Crossbow"))   return "석궁";
        if (key.Contains("Bow"))        return "활";
        return "";
    }

    /// <summary>저장 시각(ISO8601 UTC) → 지역 시각 「10월 16일 14:22」. 읽을 수 없으면 null.</summary>
    private static string FormatSavedAt(string iso)
    {
        if (string.IsNullOrEmpty(iso)) return null;
        if (!DateTime.TryParse(iso, System.Globalization.CultureInfo.InvariantCulture,
                               System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var utc))
            return null;
        var local = utc.ToLocalTime();
        return $"{local.Month}월 {local.Day}일 {local:HH:mm}";
    }

    /// <summary>누적 플레이 시간 — 1시간 미만은 분:초, 넘으면 시간:분.</summary>
    private static string FormatPlayTime(int seconds)
    {
        if (seconds <= 0) return "플레이 시간 --";

        var t = TimeSpan.FromSeconds(seconds);
        return t.TotalHours >= 1d
            ? $"플레이 시간 {(int)t.TotalHours}시간 {t.Minutes}분"
            : $"플레이 시간 {t.Minutes}분 {t.Seconds}초";
    }

    // ─────────────────────────────────────────────────────────
    // Event Handlers
    // ─────────────────────────────────────────────────────────

    private void OnClickStart()  => _onStart?.Invoke(_slotIndex, _hasSave);
    private void OnClickDelete() => _onDelete?.Invoke(_slotIndex);
}
