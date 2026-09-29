using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;

/// <summary>
/// 베이스캠프 구역 표지 — 그 구역에 <b>처음</b> 들어갔을 때 한 번 화면 위에 구역 이름을 띄우는 신호(<see cref="TitleRequested"/>)와,
/// 첫 판 온보딩의 <b>지금 목표</b>를 멀리서 가리키는 떠 있는 이름판(월드 글자 + ▼, 카메라를 향함).
///
/// 09-27 사용자: 「각 구역이 어떤 내용인지 알 수 있게 — 이름판 + 다가가면 구역 이름」.
/// 09-29 사용자: 「가이드가 과하고 부족하다」 → 2차 개편 「조용히 · 하나만」: 멀리서 늘 보이던 이름판 8장을 걷고,
///   이름판은 <b>온보딩 목표 하나만</b> 목표 위에 뜬다(▼ 화살표와 한 장으로). 가까이서는 스테이션 이름표가 곳의 이름을 맡는다(<see cref="BaseCampLabelRule"/>).
/// 매 판 반복되면 거슬리므로 화면 표시는 구역마다 세이브 슬롯당 한 번(ae 기준). 온보딩 동안은 목표 구역만 — 나머지는 온보딩 뒤 처음 들어갈 때.
/// 화면 쪽 표시는 UI 레인이 <see cref="TitleRequested"/>를 구독해 그린다(계약 09-27).
/// </summary>
[RequireComponent(typeof(SphereCollider))]
public sealed class ZoneSign : MonoBehaviour
{
    private const string SeenKeyPrefix = "basecamp_zone_seen_v1_slot";
    // 화면 배너(UI 레인)가 떠 있는 동안 — 이름판을 숨긴다. 같은 구역 배너와 이름판이 화면 위 가운데에 두 벌로 겹쳤다(ae 09-28).
    private const float BannerHoldSeconds = 3.0f;   // 98 배너: 나타남 0.35 + 유지 2.0 + 사라짐 0.6 = 2.95초(실시간)
    private const float BannerReturnFade = 0.4f;
    private const float ObjectiveHeight = 3.0f;     // 목표 위 이름판 높이(m) — 발 +4.8 m 위는 게임 카메라가 못 잡는다(09-28 실측)
    private const float ObjectiveBob = 0.2f;        // ▼ 바운스(m)
    private const float ObjectiveBobSpeed = 3f;
    private const float ObjectivePickRange = 16f;   // 목표에서 이 안의 가장 가까운 구역이 목표 구역
    private const float ArrivalCooldown = 20f;      // 같은 구역 도착 표시를 이 안에 다시 띄우지 않는다(경계를 오가도 겹치지 않게)

    /// <summary>베이스캠프 구역 id 전량 — 슬롯 초기화·디버그 메뉴가 「처음 한 번」 기록을 지울 때 쓴다(PlayerPrefs는 키를 나열할 수 없다).
    /// 씬의 ZoneSign.zoneId를 늘리면 여기도 늘릴 것.</summary>
    public static readonly string[] KnownZoneIds = { "summon", "forge", "parts", "relic", "memory", "seal", "trial", "abyss" };

    private static float s_bannerUntil = -1f;
    private static readonly List<ZoneSign> s_signs = new();
    private static ZoneSign  s_objective;
    private static Transform s_objectiveTarget;

    /// <summary>(제목, 한 줄 설명) — 구역에 처음 들어갔을 때 한 번.</summary>
    public static event Action<string, string> TitleRequested;

    /// <summary>(제목) — 처음이 아닐 때 구역에 들어설 때마다, 작고 옅게(09-29 사용자 「도착하면 위쪽에 은은한 페이드로 어떤 구역인지」).
    /// 배너(처음)가 떠 있으면 쏘지 않는다. 화면 쪽은 UI 레인 ZoneTitleBanner.ShowSubtle.</summary>
    public static event Action<string> ArrivalRequested;

    /// <summary>구역 이름 배너가 지금 떠 있는가 — 다른 월드 글자(무형검 받침 등)가 배너 자리와 겹치지 않게 숨을 때 본다.</summary>
    public static bool BannerShowing => Time.unscaledTime < s_bannerUntil;

    /// <summary>전경 연출(멀린의 공간 생성) 동안 모든 이름판을 숨긴다 — 전경 카메라 바로 앞 이름판이 화면을 덮었다(ae 09-28).</summary>
    public static bool LabelsHidden { get; set; }

    /// <summary>목표 이름판(▼ 포함)이 지금 보이는가 — 온보딩 ▼ 화살표가 같은 자리에 두 벌로 겹치지 않게 본다.</summary>
    public static bool ObjectivePlateVisible { get; private set; }

    /// <summary>플레이어가 목표 구역 안에 들어섰는가 — 가까이서는 스테이션 이름표·[F] 안내가 이어받으므로 ▼는 걷는다
    /// (도착하면 ▼가 구역 제목 배너와 화면 위에서 겹쳤다, 09-29 첫 판 실측).</summary>
    public static bool ObjectiveReached => s_objective != null && s_objective._playerInside > 0;

    [Header("구역")]
    [SerializeField] private string zoneId = "zone";
    [SerializeField] private string title = "구역";
    [SerializeField, TextArea] private string subtitle = "";

    [Header("이름판")]
    [SerializeField] private TMP_FontAsset font;
    [SerializeField] private float labelHeight = 5.5f;
    [Tooltip("이름판만 옆으로(트리거는 그대로) — 유물 성소는 성문 축 위라 성문으로 걸어가는 내내 입구 한가운데 떠 보였다(ae 09-28)")]
    [SerializeField] private Vector3 labelOffset;
    [SerializeField] private float titleSize = 7f;
    [SerializeField] private Color titleColor = new(0.96f, 0.86f, 0.58f);
    [SerializeField] private Color subtitleColor = new(0.80f, 0.78f, 0.92f);

    [Header("다가가면")]
    [SerializeField, Min(1f)] private float triggerRadius = 8f;
    [Tooltip("비어 있지 않으면 처음 들어갈 때 구역 제목 배너 대신 멀린이 이 한 줄을 말한다(기억의 제단 — 유도설계 B-7)")]
    [SerializeField, TextArea] private string firstEntryMerlinLine = "";

    [Header("카메라가 너무 가까우면 옅게")]
    [Tooltip("이 거리 안에서 옅어지기 시작해 fadeNear에서 사라진다 — 전경 연출에서 이름판이 화면 아래 HUD를 덮지 않게(ae 09-28)")]
    [SerializeField] private float fadeFar = 9f;
    [SerializeField] private float fadeNear = 5f;

    private Transform _camTransform;
    private Transform _label;
    private TextMeshPro _text;
    private MeshRenderer _labelRenderer;
    private bool _labelVisible = true;
    private bool _composedObjective;
    private float _lastArrival = -999f;
    private bool _announced;
    private float _lastAlpha = -1f;
    private int _playerInside;   // 플레이어 콜라이더가 여럿일 수 있어 개수로 센다

    public string Subtitle => subtitle;

    // ── Lifecycle ─────────────────────────────────────────────

    // 플레이 진입 시 도메인 리로드가 꺼져 있으면 정적 값이 이전 플레이에서 남는다 — 배너 시각은 unscaledTime 기준이라 특히 위험
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        s_bannerUntil = -1f;
        LabelsHidden = false;
        s_signs.Clear();
        s_objective = null;
        s_objectiveTarget = null;
        ObjectivePlateVisible = false;
    }

    private void Awake()
    {
        var col = GetComponent<SphereCollider>();
        col.isTrigger = true;
        col.radius = triggerRadius;
    }

    private void OnEnable()
    {
        if (!s_signs.Contains(this)) s_signs.Add(this);
    }

    private void Start()
    {
        _camTransform = Camera.main != null ? Camera.main.transform : null;
        CreateLabel();
    }

    private void LateUpdate()
    {
        if (_label == null) return;
        if (_camTransform == null)
        {
            // Start에서 잡은 Camera.main이 비어 있을 수 있다(테스트 허브 → 베이스캠프 경로: 카메라가 나중에 서거나 이전 씬 것이 파괴됨).
            // 그래서 빌보드·페이드·숨김이 한 번도 안 돌았다(ae 09-28) → 게임 카메라 정적 인스턴스로 다시 잡는다(검색 아님).
            var gcc = GameCameraController.Instance;
            if (gcc == null) return;
            _camTransform = gcc.transform;
        }

        // 2차 개편(09-29): 이름판은 온보딩 목표 하나만 — 목표 위에 떠서 ▼로 가리킨다. 나머지는 늘 숨는다.
        bool objective = s_objective == this && s_objectiveTarget != null;
        if (objective != _composedObjective)
        {
            _composedObjective = objective;
            _text.text = ComposeLabel();
        }
        if (objective)
        {
            float bob = Mathf.Sin(Time.unscaledTime * ObjectiveBobSpeed) * ObjectiveBob;
            _label.position = s_objectiveTarget.position + Vector3.up * (ObjectiveHeight + bob);
        }
        _label.rotation = _camTransform.rotation;

        // 카메라가 가까우면 옅게 — 값이 바뀔 때만 적용(할당 없음).
        // 숨김(연출 중 · 완전히 옅어짐)은 렌더러를 끈다 — 소프트 그림자 공유 재질은 알파 0으로도 안 사라졌다(ae 09-28 캡처).
        // 구역 안에서는 숨긴다 — 안에 서면 머리 위에 걸려 화면을 가렸다(성문 통로, ae 09-28). 가까이서는 스테이션 이름표가 이어받는다.
        float d = Vector3.Distance(_camTransform.position, _label.position);
        float a = !objective || LabelsHidden || _playerInside > 0 ? 0f : Mathf.Clamp01((d - fadeNear) / Mathf.Max(0.01f, fadeFar - fadeNear));
        a *= BannerDim();
        bool visible = a > 0.02f;
        if (visible != _labelVisible)
        {
            _labelVisible = visible;
            if (_labelRenderer != null) _labelRenderer.enabled = visible;
        }
        if (objective) ObjectivePlateVisible = visible;
        if (visible && Mathf.Abs(a - _lastAlpha) > 0.02f)
        {
            _lastAlpha = a;
            _text.alpha = a;
        }
    }

    private void OnDisable()
    {
        s_signs.Remove(this);
        if (s_objective == this) ObjectivePlateVisible = false;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.GetComponentInParent<PlayerController>() == null) return;
        _playerInside++;
        if (_playerInside > 1) return;   // 플레이어 콜라이더가 여럿 — 들어서는 순간 한 번만
        // 처음 들어가면 제목 + 설명 배너(슬롯당 한 번). 온보딩 동안은 목표 구역만 — 한 순간에 하나만(유도설계 원칙 5),
        // 다른 구역의 처음 배너는 온보딩이 끝난 뒤 처음 들어갈 때로 미룬다.
        if (!_announced && PlayerPrefs.GetInt(SeenKey(zoneId), 0) == 0
            && !(BaseCampOnboardingDirector.InProgress && s_objective != this))
        {
            _announced = true;
            AnnounceWhenReadyAsync(this.GetCancellationTokenOnDestroy()).Forget();
            return;
        }
        ArriveWhenReadyAsync(this.GetCancellationTokenOnDestroy()).Forget();   // 그 밖엔 제목만 은은하게
    }

    private void OnTriggerExit(Collider other)
    {
        if (_playerInside > 0 && other.GetComponentInParent<PlayerController>() != null) _playerInside--;
    }

    // ── Public Methods ────────────────────────────────────────

    /// <summary>
    /// 온보딩의 지금 목표 — 목표에서 가장 가까운 구역의 이름판이 목표 위에 떠 ▼로 가리킨다(null이면 전부 숨김).
    /// 온보딩 ▼ 화살표와 이름판이 따로 떠 겹쳤다(ae 09-28 캡처) → 한 장으로 합쳤다.
    /// </summary>
    public static void SetObjective(Transform target)
    {
        s_objectiveTarget = target;
        s_objective = null;
        ObjectivePlateVisible = false;
        if (target == null) return;

        float best = ObjectivePickRange * ObjectivePickRange;
        foreach (var s in s_signs)
        {
            float sq = (s.transform.position - target.position).sqrMagnitude;
            if (sq < best) { best = sq; s_objective = s; }
        }
    }

    /// <summary>구역 밖에서도 같은 배너로 한 줄을 띄운다(멀린의 공간 생성 대사 등) — 배너는 HUD와 별개 캔버스라 HUD를 숨겨도 보인다.
    /// 배너가 떠 있는 동안 모든 이름판을 숨긴다(화면 위 가운데에서 겹침 방지).</summary>
    public static void RequestTitle(string title, string subtitle)
    {
        s_bannerUntil = Time.unscaledTime + BannerHoldSeconds;
        TitleRequested?.Invoke(title, subtitle);
    }

    /// <summary>부제를 바꾼다 — 심연의 문은 포탈이 열리면 「준비를 마치면 열린다」가 맞지 않다.</summary>
    public void SetSubtitle(string text)
    {
        subtitle = text;
        if (_text != null) _text.text = ComposeLabel();
    }

    /// <summary>슬롯의 구역 이름 기록 전부(<see cref="KnownZoneIds"/>) — 새 게임·슬롯 삭제(RunProgressManager.ResetSlot).</summary>
    public static void ClearAllForSlot(int slot) => ClearForSlot(slot, KnownZoneIds);

    /// <summary>슬롯 삭제 시 구역 기록도 지운다(새 게임이면 다시 처음처럼).</summary>
    public static void ClearForSlot(int slot, params string[] zoneIds)
    {
        foreach (var id in zoneIds) PlayerPrefs.DeleteKey(SeenKeyPrefix + slot + "_" + id);
        PlayerPrefs.Save();
    }

    // ── Private Methods ───────────────────────────────────────

    private static string SeenKey(string id)
        => SeenKeyPrefix + (RunProgressManager.Instance?.ActiveSlotIndex ?? 0) + "_" + id;

    /// <summary>
    /// 처음 들어온 구역의 이름을 띄운다 — 소환 · 시작 대사 · 공간 생성이 끝난 뒤에. 소환의 방은 스폰 자리가 곧 구역 안이라
    /// 소환 빛기둥 위로 배너가 떠 받침 이름표와 겹쳤다(ae 09-28). 기다리는 사이 구역을 벗어났으면 다음 진입 때 다시.
    /// </summary>
    private async UniTaskVoid AnnounceWhenReadyAsync(CancellationToken ct)
    {
        try
        {
            await UniTask.WaitUntil(() => (BaseCampBootstrapper.Instance == null || BaseCampBootstrapper.Instance.IsReady) && !LabelsHidden,
                                    cancellationToken: ct);
            if (_playerInside <= 0) { _announced = false; return; }
            PlayerPrefs.SetInt(SeenKey(zoneId), 1);
            PlayerPrefs.Save();
            if (!string.IsNullOrEmpty(firstEntryMerlinLine))
            {
                // 배너와 멀린이 같이 말하면 두 갈래가 된다(원칙 5) — 이 구역은 멀린 한 줄이 제목을 대신한다
                RelicFairy.UI.UI_BossBark.Show(firstEntryMerlinLine, RelicFairy.UI.BossBarkType.MerlinNarration, DialogueSpeaker.Merlin);
                Debug.Log($"[BaseCampFx] 구역 첫 진입 멀린 한 줄: {title}");
                return;
            }
            RequestTitle(title, subtitle);
            Debug.Log($"[BaseCampFx] 구역 이름 표시: {title}");
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>도착 표시(제목만, 은은하게) — 준비 뒤에, 아직 구역 안이고 배너가 없을 때. 같은 구역은 ArrivalCooldown 안에 다시 안 띄운다.</summary>
    private async UniTaskVoid ArriveWhenReadyAsync(CancellationToken ct)
    {
        try
        {
            await UniTask.WaitUntil(() => (BaseCampBootstrapper.Instance == null || BaseCampBootstrapper.Instance.IsReady) && !LabelsHidden,
                                    cancellationToken: ct);
            if (_playerInside <= 0 || BannerShowing) return;
            if (Time.unscaledTime - _lastArrival < ArrivalCooldown) return;
            _lastArrival = Time.unscaledTime;
            ArrivalRequested?.Invoke(title);
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>배너가 떠 있으면 0, 내려간 뒤 짧게 되돌아온다.</summary>
    private static float BannerDim()
    {
        float r = s_bannerUntil - Time.unscaledTime;
        return r > 0f ? 0f : Mathf.Clamp01(-r / BannerReturnFade);
    }

    /// <summary>목표 이름판 — 제목 + ▼. 부제는 처음 들어갈 때의 배너에서만(멀리서 작은 부제는 읽히지 않았다, 09-29 실측).</summary>
    private string ComposeLabel() => _composedObjective ? $"{title}\n<size=80%>▼</size>" : title;

    private void CreateLabel()
    {
        var root = new GameObject("ZoneLabel");
        root.transform.SetParent(transform, false);
        root.transform.localPosition = Vector3.up * labelHeight + labelOffset;

        var t = root.AddComponent<TextMeshPro>();
        // transform 캐시는 반드시 AddComponent 뒤 — TextMeshPro가 Transform을 RectTransform으로 교체하며
        // 앞서 잡은 참조는 파괴된 가짜 null이 된다(그래서 빌보드·숨김이 한 번도 안 돌았다, ae 09-28).
        _label = t.transform;
        _text = t;
        _labelRenderer = root.GetComponent<MeshRenderer>();
        _labelRenderer.enabled = false;   // 목표가 되기 전엔 숨는다(첫 LateUpdate 전 한 프레임 번쩍임 방지)
        _labelVisible = false;
        if (font != null) t.font = font;
        t.text = ComposeLabel();
        t.fontSize = titleSize;
        t.alignment = TextAlignmentOptions.Center;
        t.color = titleColor;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.sortingOrder = UISortingOrder.WorldLabel;
        TMPOutlineHelper.ApplySoftShadow(t);
    }
}
