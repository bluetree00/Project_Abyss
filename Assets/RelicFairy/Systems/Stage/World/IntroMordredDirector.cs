using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 인트로 조우 컷신 — 무형검을 F로 집는 순간 발동(1회).
///
/// 원칙: <b>행동이 먼저, 대사는 나중</b>. 각 대사 앞에 그 대사를 정당화하는 화면 사건을 배치한다.
///
/// 쇼트 구성(설계 문서 §2-2 S2~S15) — 대화는 컷, 강림 · 공개만 카메라 이동
///  서임   : 검에 손을 뻗음(S2) → 투샷 · 어깨 너머(S3) → 검을 든 모르드레드(S5) → 검이 손으로(S4)
///  전조   : 빛이 줄고 음악이 끊김(S6) → 화면 밖 리치 목소리 · 모르드레드 경계(S7)
///  강림   : 세 인물 와이드(S8) → 리치 로우앵글 푸시인(S9)
///  타락   : 사슬 강타 와이드(S10) → 저항하는 모르드레드(S12) → 리치 소멸 · 어둠 · 침묵(S13)
///  배신   : S5와 같은 구도로 검이 내려온다(S14) → 암전 → 아레나 · 보스 등장(S15)
///
/// 쇼트 좌표는 「플레이어 → 모르드레드」 축 기준이고, 카메라는 축 오른쪽에만 선다(180도 규칙).
/// 벽 · 기둥에 박히거나 대상을 가리면 <see cref="Unblock"/>이 자리를 고친다.
/// 대사는 전부 <b>비차단 UI_BossBark(화자줄 포함)</b> — 차단형 팝업은 timeScale=0이라 광선 · 애니메이션이 멈춘다.
/// </summary>
public sealed class IntroMordredDirector : MonoBehaviour
{
    // ── Constants ────────────────────────────────────────────────────
    private const float CamRadius = 0.25f;
    private const float StateBlend = 0.2f;   // 인물 동작 바꿈 섞기(초) — 뚝 바뀌면 동작을 건너뛴 듯 보였다(10-13 실측 12번 모두 섞임 없음)
    private const int   EnvMask   = 1 << 0 | 1 << 3 | 1 << 8;   // Default · Ground · Wall — 인물 레이어는 빼 둔다

    // ── Static ───────────────────────────────────────────────────────
    /// <summary>지금 쇼트 이름(S2~S14). 컷신 밖이면 null — 실측 도구가 쇼트마다 화면을 찍을 때 읽는다.</summary>
    public static string CurrentShot { get; private set; }

    // 고정 컷이 멈춘 그림으로 보이지 않게 — 컷마다 화각을 조금씩 좁혀 천천히 다가가는 느낌을 준다(10-16 사용자 「흐름과 멈춤이 덜 느껴져야」).
    // 다음 컷 · 이동이 오면 멈춘다. 카메라 자리는 옮기지 않는다 — 옮기면 달려드는 모르드레드(S14)와 겹쳐 화면을 덮었다(10-16 실측).
    private const float DriftFovRate = 0.6f;    // 초당 화각 감소(°)
    private const float DriftFovMax  = 3f;      // 화각을 최대 이만큼만 좁힌다
    private static CancellationTokenSource s_drift;

    // ── [SerializeField] ─────────────────────────────────────────────
    [Header("발동 — 무형검 F키 픽업")]
    [Tooltip("플레이어가 무형검을 F로 획득하면 시퀀스가 시작된다. 비우면 Start에서 씬의 IntroSwordPickup을 자동 탐색.")]
    [SerializeField] private IntroSwordPickup swordPickup;

    [Header("대사 시퀀스 (DIALOGUE_DATA.csv)")]
    [SerializeField] private string mordredSequenceId     = "Intro_Mordred";
    [Tooltip("타락 대사 — 라인 순서가 곧 비트 순서다(0:예고 1,2:강림 3:저항 4:선언 5,6:신음).")]
    [SerializeField] private string corruptSequenceId     = "Intro_LichCorrupt";
    [SerializeField] private string combatStartSequenceId = "Mordred_Corrupted";
    [Tooltip("보스 등장 클로즈업에 띄울 보스 이름(BossRoomController.EntranceTitle로 넘긴다).")]
    [SerializeField] private string bossIntroName = "타락한 모르드레드";

    [Header("리치 카메오")]
    [Tooltip("씬에 미리 배치한 연출 전용 리치 액터(비활성 상태로 둘 것). 보스 스크립트 없이 모델·애니메이터만.")]
    [SerializeField] private GameObject lichActor;
    [Tooltip("광선이 뻗어나갈 리치 손 위치. 비우면 리치 액터 위치를 쓴다.")]
    [SerializeField] private Transform  lichCastPoint;
    [Tooltip("리치 애니메이터 컨트롤러의 Addressable 키(LichConfig.animatorControllerAddress와 동일). " +
             "연출용 액터는 LichMonster를 제거해 MonsterBase의 런타임 주입을 못 받으므로 여기서 직접 로드한다.")]
    [SerializeField] private string     lichAnimatorKey = "Lich/LichAnimator";
    [SerializeField] private float      lichLingerAfter = 0.5f;
    [Tooltip("리치가 디졸브로 사라지는 시간(초). 포탈 VFX 길이와 맞추면 자연스럽다.")]
    [SerializeField] private float      lichVanishDuration = 1.0f;

    [Header("모르드레드 연출 액터")]
    [SerializeField] private Transform  mordredTransform;    // 타락 VFX 부착 지점
    [SerializeField] private GameObject mordredActor;        // 컷신용 액터 — 전투 보스 등장 시 숨김

    [Header("어둠 이펙트")]
    [Tooltip("리치 강림 — 어둠의 포탈이 열리며 등장(Effect_43_DarkPortal 등). 리치 위치에 생성.")]
    [SerializeField] private GameObject lichPortalVfxPrefab;
    [Tooltip("리치 상시 오라 — 등장 후 몸에 두른다(Aura_Dark_LWRP 등). 리치 액터에 부착.")]
    [SerializeField] private GameObject lichAuraVfxPrefab;
    [Tooltip("리치 캐스팅 — 손에 어둠을 모으는 예비 동작(Casting_Dark_3_LWRP 등). 캐스트 지점에 부착.")]
    [SerializeField] private GameObject lichCastVfxPrefab;
    [Tooltip("★타락 1단 — 모르드레드를 덮치는 사슬 공격(Effect_43_DarkChainSwamp 등). 원샷 이펙트.")]
    [SerializeField] private GameObject chainStrikeVfxPrefab;
    [Tooltip("★타락 2단 — 모르드레드를 잠식하는 어둠. 몸에 부착되므로 루프 이펙트여야 한다(Aura_Dark_LWRP 등).")]
    [SerializeField] private GameObject bindChainVfxPrefab;

    [Header("무형검")]
    [Tooltip("제단 위 무형검 렌더러 — 받는 순간 안개로 흩어지고 손의 검이 맺힌다.")]
    [SerializeField] private Renderer swordRenderer;

    [Header("분위기 / 전환")]
    [SerializeField] private IntroAtmosphereSwitch atmosphere;
    [Tooltip("리치 강림 예고 단계에서 미리 어두워지는 정도(0~1).")]
    [SerializeField, Range(0f, 1f)] private float preDimAmount = 0.35f;
    [SerializeField] private float fadeDuration    = 1.0f;
    [Tooltip("타락→전투 전환 시 밝은 성당이 어둠으로 뒤집히는 웅장한 배경 전환 시간(초).")]
    [SerializeField] private float grandTransitionDuration = 2.5f;

    [Header("시네마틱 레터박스")]
    [Tooltip("입력 차단 구간과 같다 — 검을 받는 순간 걸리고, 전투 직전 암전 속에서 걷힌다.")]
    [SerializeField] private bool  useLetterbox = true;
    [Tooltip("레터박스 슬라이드 인/아웃 시간(초).")]
    [SerializeField] private float letterboxDuration = 0.5f;
    [Tooltip("목표 종횡비. 2.39=시네마스코프.")]
    [SerializeField] private float letterboxAspect = 2.39f;
    [Tooltip("레터박스 캔버스 정렬 순서. UI 팝업(200~)보다 낮아야 대사창을 가리지 않는다.")]
    [SerializeField] private int   letterboxSortingOrder = UISortingOrder.Letterbox;
    [Tooltip("전투 진입 시 HUD 페이드 인 시간(초).")]
    [SerializeField] private float hudFadeDuration = 0.8f;

    [Header("무형검 하사")]
    [Tooltip("전시된 검이 디졸브로 사라지는 시간(초).")]
    [SerializeField, Min(0.2f)] private float swordDissolveDuration = 1.0f;
    [Tooltip("손의 검이 같은 디졸브로 맺히는 시간(초) — 전시 검이 사라지는 후반부와 겹친다.")]
    [SerializeField, Min(0.1f)] private float swordCondenseDuration = 0.8f;
    [Tooltip("전시 검이 이만큼(0~1) 사라졌을 때 손의 검이 맺히기 시작한다.")]
    [SerializeField, Range(0f, 1f)] private float swordHandOverlap = 0.45f;
    [Tooltip("검이 손에 맺히는 순간의 빛 고리 색(무형검 안개 빛).")]
    [SerializeField] private Color swordArriveTint = new Color(0.72f, 0.9f, 1f, 1f);

    [Header("무기 지급 (Addressable 무기 SO 키)")]
    [Tooltip("무형검 획득 시 슬롯0(근접)에 장착할 무기 SO 키.")]
    [SerializeField] private string namelessWeaponKey = "T0_Nameless";
    [Tooltip("보스전 진입 시 슬롯1(원거리)에 장착할 무기 SO 키.")]
    [SerializeField] private string rangedWeaponKey = "T1_Bow";

    [Header("원거리 무기 튜토리얼")]
    [Tooltip("전투 진입 시 순서대로 표시할 튜토리얼 안내(비차단 자막). 실제 키 매핑에 맞춰 문구 수정.")]
    [SerializeField, TextArea] private string[] rangedTutorialLines =
    {
        "타락한 모르드레드에겐 검이 닿지 않는다. — 원거리 무기로 맞서라.",
        "[2] 키로 원거리 무기를 들고, 조준해 공격하라.",
    };
    [Tooltip("튜토리얼 각 줄 사이 간격(초).")]
    [SerializeField] private float rangedTutorialGap = 3.5f;

    [Header("전투 조명 점등")]
    [Tooltip("암전 후 보스전이 시작되면 하나씩 켜질 횃불. 인스펙터의 Intensity가 목표 밝기가 된다.")]
    [SerializeField] private Light[] combatTorches;
    [Tooltip("보스전 개시 후 첫 횃불이 켜지기까지 대기(초).")]
    [SerializeField] private float torchIgniteDelay = 2f;
    [Tooltip("횃불이 하나씩 켜지는 간격(초).")]
    [SerializeField] private float torchStepGap = 0.35f;
    [Tooltip("각 횃불이 밝아지는 시간(초).")]
    [SerializeField] private float torchFadeIn = 0.6f;

    [Header("보스 개시")]
    [SerializeField] private BossRoomController bossRoom;
    [SerializeField] private bool               bossUnbeatable = true;
    [Tooltip("전투 개시 시 플레이어를 옮길 아레나 진입 지점. 보스는 아레나 원위치에 있으므로 암전 중 이동시킨다.")]
    [SerializeField] private Transform arenaEntryPoint;

    // ── Private ──────────────────────────────────────────────────────
    private bool             _started;
    private PlayerController _player;
    private Animator         _mordredAnim;
    private Animator         _lichAnim;
    // 쇼트 축(SetupAxis) — 플레이어 발 · 모르드레드 발 · 앞 · 오른쪽, 그리고 몸 범위로 잰 머리 · 가슴
    private Vector3          _p, _m, _f, _r;
    private Vector3          _pHead, _pChest, _mHead, _mChest;
    private float            _baseFov;
    private Transform        _displayAura;   // 전시 이펙트(FogAura) — 검에서 떼어 바닥에 둔다(PlaceDisplayAura)

    // ── Lifecycle ────────────────────────────────────────────────────
    private void Awake()
    {
        if (mordredActor != null) _mordredAnim = mordredActor.GetComponentInChildren<Animator>(true);
        if (lichActor    != null) _lichAnim    = lichActor.GetComponentInChildren<Animator>(true);
    }

    private void Start()
    {
        if (swordPickup == null)
            swordPickup = FindFirstObjectByType<IntroSwordPickup>(FindObjectsInactive.Include);

        if (swordPickup != null) swordPickup.OnPickup += HandleSwordPickup;
        PlaceDisplayAura();
    }

    private void OnDestroy()
    {
        if (swordPickup != null) swordPickup.OnPickup -= HandleSwordPickup;
        RelicFairy.UI.UI_BossBark.CinematicLayout = false;
        StopDrift();
    }

    // ── Public Methods ───────────────────────────────────────────────
    /// <summary>아레나가 런타임 생성이라 보스룸은 IntroArenaLoader가 로드 후 주입한다.</summary>
    public void SetBossRoom(BossRoomController room) => bossRoom = room;

    /// <summary>무형검을 F로 획득한 순간 시퀀스를 시작한다. 1회만.</summary>
    private void HandleSwordPickup(PlayerController player)
    {
        if (_started || player == null) return;
        _player = player;
        RunAsync(this.GetCancellationTokenOnDestroy()).Forget();
    }

    // ── 연출 시퀀스 ──────────────────────────────────────────────────
    // 쇼트 번호(S2~S15)는 설계 문서 「인트로연출·대사텍스트_개선설계_20261010」 §2-2를 따른다.
    // 카메라는 서임부터 배신의 일격까지 연출이 계속 쥔다(10-10 — 예전엔 리치 강림 직후 평소 추적 카메라로 넘겨
    // 타락 · 붕괴를 플레이어 뒤 높은 시점으로 보여 줬다). 레터박스는 입력 차단과 같은 구간에만 걸린다.
    private async UniTaskVoid RunAsync(CancellationToken ct)
    {
        if (_started) return;
        _started = true;
        var player = _player;
        var cam    = GameCameraController.Instance;
        var view   = Camera.main;
        _baseFov   = view != null ? view.fieldOfView : 60f;

        try
        {
            // 보스룸은 런타임 생성 아레나에서 주입된다 — 주입 전에 시퀀스가 진행되면 전투가 시작되지 않는다.
            await IntroArenaLoader.ReadyAsync();

            // 플레이어 통제 — 컷신 동안 입력 차단 + 남은 관성 제거 + 카메라 수동 제어 인계.
            player?.SetInputEnabled(false);
            FreezePlayerGrounded(player);
            PrepareTorches();   // 목표 밝기를 기억하고 전부 소등(점등은 보스전 개시 후)
            // HUD 억제 재확인 — 부팅 타이밍으로 억제가 누락됐어도 컷신 중엔 확실히 숨긴다.
            UIRootBootstrapper.Instance?.SetHudStartRoomSuppressed(true);
            cam?.TakeManualControl();
            // 대사는 대사창처럼 — 리치 그림은 왼쪽 아래 고정 · 이름 · 글은 가운데(10-16 사용자 「텍스트마다 리치 이미지가 다르게 나오는 게 좋지 않아」)
            RelicFairy.UI.UI_BossBark.CinematicLayout = true;
            // 무형검을 서임 대사(약 8초) 동안 미리 불러 둔다 — 받는 순간(S4) 클립 · 프리팹 로드로 4~5초 멈췄다(10-16 실측: S4 7.1초)
            PrewarmNamelessAsync().Forget();
            if (useLetterbox)
                CinematicFrame.ShowAsync(letterboxDuration, null, ct, letterboxAspect, 0f,
                                         title: null, sortingOrder: letterboxSortingOrder).Forget();

            var dlg = Managers.DialogueData;
            if (dlg != null && !dlg.IsInitialized) await dlg.InitializeAsync();
            var vows   = dlg?.GetLines(mordredSequenceId);       // 서임 — 고개를 들어라 · 무엇이 될지는 · 받아라
            var lines  = dlg?.GetLines(corruptSequenceId);       // 타락 — 0 예고 · 1,2 강림 · 3 경계 · 4 선언 · 5,6 신음
            var strike = dlg?.GetLines(combatStartSequenceId);   // 배신의 일격

            SetupAxis(player);
            Vector3 chest = _mChest;
            Vector3 sword = swordRenderer != null ? swordRenderer.transform.position : _p + _f * 1.2f + Vector3.up;

            // S2 — 검에 손을 뻗는 순간: 플레이어와 검을 낮게, 옆에서
            Cut("S2 검", At(sword, 0.7f, sword.y + 0.35f, -0.5f), sword + Vector3.up * 0.1f, 30f);
            await Hold(0.6f, ct);

            // S3 — 축 한쪽 눈높이 투샷 → 플레이어 어깨 너머 모르드레드(대화는 컷으로 넘긴다)
            Cut("S3 투샷", Ax(_p, 1.2f, 2.0f, -3.0f), Vector3.Lerp(_pChest, chest, 0.5f), 42f);
            await SayAsync(vows, 0, ct);
            Cut("S3 기사", At(_m, 1.4f, chest.y, -3.6f), _mHead - Vector3.up * 0.25f, 34f);   // ×1.3 — 속 빈 모델이 가까우면 이상(10-22 ③)
            await SayAsync(vows, 1, ct);

            // S5 — 검을 든 모르드레드를 아래에서(모티프). S14에서 같은 구도 · 같은 화각으로 돌아온다.
            PlayMordred("AttackReady");
            BladeShot("S5 검을 든 기사");
            await SayAsync(vows, 2, ct);

            // S4 — 「받아라」에 답해 검이 손으로 날아온다
            // 주인공 정면은 보이지 않는다 — 통로 가운데 등 뒤 높은 곳에서 뻗는 팔과 날아오는 검의 길을 함께 잡는다(10-16 사용자 「캐릭터의 정면은 연출상
            // 보여줄 필요가 없어」 · 「무형검이 플레이어한테 가는 느낌」). 바로 어깨 너머는 머리가 화면을 덮었고, 옆으로 비키면 신도석에 막혀
            // 카메라가 팔 앞까지 당겨졌다(10-16 실측 2회).
            Cut("S4 검을 받다", Ax(_p, 0.6f, 2.3f, -3.0f), Vector3.Lerp(_pChest, sword, 0.5f), 40f);
            await AbsorbSwordAsync(player, ct);

            // S6 — 전조: 빛이 줄고 음악이 끊긴다(소리를 비워 다음 목소리가 들리게)
            PlayMordred("Idle2");
            Cut("S6 전조", Ax(_p, 1.2f, 3.4f, -4.5f), Vector3.Lerp(_p, _m, 0.6f) + Vector3.up * 1.3f, 46f);
            Managers.Sound.FadeOutBgmAsync(1.2f, ct).Forget();
            if (atmosphere != null) atmosphere.DimStepAsync(preDimAmount, 1.4f, ct).Forget();
            await Hold(1.2f, ct);

            // S7 — 소리가 먼저, 형체는 나중: 화면 밖 목소리 → 모르드레드가 검을 겨눈다
            Cut("S7 목소리", At(_p, 0.7f, _pHead.y + 0.25f, -1.3f), chest, 24f);
            await SayAsync(lines, 0, ct);
            PlayMordred("AttackReady");
            await SayAsync(lines, 3, ct);

            // S8 — 강림: 세 인물의 자리가 한 화면에 보이는 높은 와이드(버퍼샷) — 공개는 카메라 이동으로
            Managers.Sound.CrossfadeBgmAsync("Game_Intro_play").Forget();
            Vector3 lichAt = lichActor != null ? lichActor.transform.position : _m + _f * 5f + _r * 3f;
            Vector3 trio   = Vector3.Lerp(chest, lichAt + Vector3.up * 1.5f, 0.5f);
            Cut("S8 강림 와이드", Ax(_p, -1.8f, 4.6f, -3.0f), trio, 52f);
            await AwakenLichAsync(ct);
            await MoveAsync("S8 강림 와이드", Ax(_p, -1.4f, 4.2f, -1.6f), trio, 48f, 2.2f, ct);

            // S9 — 리치 로우앵글 푸시인(이 구간만 카메라가 계속 움직인다)
            Vector3 lichHead = LichHead(lichAt);
            Vector3 lf = lichActor != null ? Flat(lichActor.transform.forward, -_f) : -_f;
            Vector3 lr = Vector3.Cross(Vector3.up, lf);   // 리치의 오른쪽 — 반대(-lr)가 축의 카메라 쪽(+_r)이다
            Vector3 lichGround = new Vector3(lichAt.x, _p.y, lichAt.z);
            Cut("S9 리치", lichGround + lf * 4.6f - lr * 1.4f + Vector3.up * 1.0f, lichHead, 38f);
            var push = MoveAsync("S9 리치", lichGround + lf * 3.4f - lr * 1.0f + Vector3.up * 0.9f, lichHead, 32f, 5.2f, ct);
            await SayAsync(lines, 1, ct);
            await SayAsync(lines, 2, ct);
            await push;

            // S10 — 사슬 강타 와이드: 리치의 손과 모르드레드가 한 화면에
            Cut("S10 사슬", Ax(_m, 2.0f, 1.8f, -4.2f), Vector3.Lerp(chest, lichHead, 0.2f), 56f);
            PlayLich("DeathRayStart");
            var castVfx = SpawnAttached(lichCastVfxPrefab, lichCastPoint != null ? lichCastPoint : lichActor?.transform);
            await WaitStateAsync(_lichAnim, "DeathRayStart", 0.55f, 1.4f, ct);
            PlayLich("DeathRayLoop");
            SpawnChainStrike();
            await SayAsync(lines, 4, ct);

            // S12 — 저항하는 모르드레드(미디엄): 어둠이 몸을 잠식한다
            var bindVfx = SpawnCorruptionAura();
            Cut("S12 저항", At(_m, 1.7f, chest.y, -3.1f), chest, 40f);   // ×1.3(10-22 ③)
            PlayMordred("GetHit1");
            await WaitStateAsync(_mordredAnim, "GetHit1", 0.85f, 1.6f, ct);
            PlayMordred("GetHit2");
            await SayAsync(lines, 5, ct);
            await WaitStateAsync(_mordredAnim, "GetHit2", 0.85f, 1.2f, ct);
            PlayMordred("GetHit3");
            await SayAsync(lines, 6, ct);

            // S13 — 리치가 거둬지고 성당이 어둠으로 뒤집힌다 → 짧은 침묵
            Cut("S13 소멸", Ax(_m, 2.0f, 1.8f, -4.2f), Vector3.Lerp(chest, lichHead, 0.2f), 56f);
            PlayLich("DeathRayEnd");
            if (castVfx != null) Destroy(castVfx);
            await Hold(lichLingerAfter, ct);
            await VanishLichAsync(bindVfx, ct);
            PlayMordred("Idle2");
            Cut("S13 어둠", At(_p, 0.7f, _pHead.y + 0.25f, -1.3f), chest, 30f);
            if (atmosphere != null)
                await atmosphere.SwitchToDarkAsync(grandTransitionDuration, ct);
            await Hold(0.5f, ct);

            // S14 — 배신의 일격: S5와 같은 구도 · 같은 화각. 이번엔 검이 내려온다.
            //     어둠으로 뒤집힌 뒤라 인물이 묻힌다 — 카메라 쪽 낮은 붉은 빛 하나로 윤곽만 세운다(일격과 함께 걷는다).
            PlayMordred("AttackReady");
            BladeShot("S14 배신의 일격", 1.6f);
            var keyLight = SpawnKeyLight(Ax(_m, 0.8f, 0.9f, -1.6f));
            await SayAsync(strike, 0, ct);
            await SayAsync(strike, 1, ct);
            PlayMordred("Attack1");
            await WaitStateAsync(_mordredAnim, "Attack1", 0.5f, 1.5f, ct);
            RelicFairy.UI.UI_BossBark.Dismiss(0f);
            RelicFairy.UI.UI_BossBark.CinematicLayout = false;   // 보스전 대사는 평소 배치
            await ScreenFade.Out(0f, ct);
            if (keyLight != null) Destroy(keyLight);
            Managers.Sound.PlayEffectAsync(SoundKey.Sfx.PlayerHitMedium).Forget();   // 예전 키 sfx_player_hit는 주소가 없어 무음이었다(10-12)

            // S15 — 암전 속에서 아레나로 옮기고, 레터박스를 걷은 채 밝힌다(조작은 보스 등장 연출이 끝나면 돌아온다).
            if (useLetterbox) CinematicFrame.HideAsync(0f, null, ct).Forget();
            if (mordredActor != null) mordredActor.SetActive(false);
            if (player != null && arenaEntryPoint != null)
            {
                player.transform.SetPositionAndRotation(arenaEntryPoint.position, arenaEntryPoint.rotation);
                player.RequestFacing(arenaEntryPoint.rotation);   // 방향 목표도 — 안 하면 다음 물리 프레임에 옛 방향으로 돌아갔다(10-09 출시 점검)
            }

            // 컷신 종료 — 물리 복구(접지 후 kinematic 해제). 전투 중엔 정상 중력이 필요하다.
            StopDrift();
            UnfreezePlayer(player);
            if (view != null) view.fieldOfView = _baseFov;
            CurrentShot = null;
            if (player != null) cam?.HandToGameplayCamera(player.transform);
            await Hold(0.6f, ct);

            await ScreenFade.In(fadeDuration, ct);

            if (bossRoom != null && player != null)
            {
                // 보스 이름은 등장 클로즈업이 띄운다(데스나이트 등장의 이름 자리) — 이름이 두 번 뜨지 않게.
                bossRoom.EntranceTitle = bossIntroName;
                bossRoom.BeginBossFightExternally(player, bossUnbeatable);
            }

            // 어둠 속에서 횃불이 하나씩 켜지며 전장이 드러난다(공격 판정 가시성 확보).
            IgniteTorchesAsync(ct).Forget();

            // 보스 등장 연출(팬 · 클로즈업 · 이름)이 끝나 조작이 돌아온 뒤 HUD를 띄운다.
            // (등장 카메라 중에 HUD가 떠 있으면 연출을 깬다 — 10-09 전 6초 고정이라 클로즈업 위에 떴다)
            float readyBy = Time.unscaledTime + 20f;
            while (bossRoom != null && !bossRoom.IsCombatReady && Time.unscaledTime < readyBy)
                await UniTask.Yield(PlayerLoopTiming.Update, ct);

            UIRootBootstrapper.Instance?.FadeInHudAsync(hudFadeDuration).Forget();
            // 활 지급 — 슬롯1(원거리). 들고 있는 무형검(슬롯0)은 유지하고 소지만 시킨다.
            await EquipToSlotAsync(player, rangedWeaponKey, RangedSlot, setActive: false);
            ShowRangedTutorial();

            // 여기서 시퀀스는 끝난다. 이후 베이스캠프 복귀는 오직 '플레이어 사망'으로만 일어난다
            // (PlayerController가 사망 시 IntroBootstrapper.HandleIntroDeath를 직접 호출).
            // 시간 기반 강제 종료를 두면 아직 싸우는 중에 화면이 넘어가버린다.
        }
        catch (OperationCanceledException)
        {
            RelicFairy.UI.UI_BossBark.CinematicLayout = false;
            StopDrift();
            UnfreezePlayer(player); // 컷신이 끊겨도 플레이어가 kinematic으로 굳지 않게
            if (view != null) view.fieldOfView = _baseFov;
            CurrentShot = null;
            if (useLetterbox) CinematicFrame.HideAsync(0f, null).Forget(); // 레터박스가 화면에 남지 않게
        }
    }

    // ── 쇼트(주인공 ↔ 모르드레드 축 기준) ─────────────────────────────
    /// <summary>쇼트 축을 잡는다 — 플레이어 → 모르드레드가 앞(+_f), 그 오른쪽(+_r)이 카메라가 머무는 쪽(180도 규칙).</summary>
    private void SetupAxis(PlayerController player)
    {
        // 높이는 피벗이 아니라 몸(스킨드 메시) 범위로 잰다 — 모르드레드 액터 피벗은 바닥 아래(-0.58)라
        // 피벗 기준 오프셋은 카메라가 그의 머리 위 하늘만 잡았다(10-10 첫 실측).
        Vector3 mPos = mordredTransform != null ? mordredTransform.position : new Vector3(0.25f, 0f, 2.26f);
        Vector3 pPos = player != null ? player.transform.position : mPos - Vector3.forward * 7f;
        var mb = BodyBounds(mordredActor, mPos, 2.3f);
        var pb = BodyBounds(player != null ? player.gameObject : null, pPos, 1.6f);
        _m = new Vector3(mPos.x, mb.min.y, mPos.z);
        _p = new Vector3(pPos.x, pb.min.y, pPos.z);
        _f = Flat(_m - _p, Vector3.forward);
        _r = Vector3.Cross(Vector3.up, _f);
        _mHead  = new Vector3(_m.x, mb.max.y - 0.15f, _m.z);
        _mChest = new Vector3(_m.x, mb.min.y + mb.size.y * 0.68f, _m.z);
        _pHead  = new Vector3(_p.x, pb.max.y - 0.1f, _p.z);
        _pChest = new Vector3(_p.x, pb.min.y + pb.size.y * 0.7f, _p.z);
    }

    /// <summary>인물의 몸 범위(스킨드 메시만 — 무기 궤적 · 이펙트는 뺀다). 없으면 피벗에서 키만큼.</summary>
    internal static Bounds BodyBounds(GameObject go, Vector3 pivot, float height)
    {
        if (go != null)
        {
            bool any = false;
            var b = new Bounds();
            foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (!r.enabled) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            if (any && b.size.y > 0.5f) return b;
        }
        return new Bounds(pivot + Vector3.up * height * 0.5f, new Vector3(0.6f, height, 0.6f));
    }

    /// <summary>축 기준 오프셋(높이는 발 기준).</summary>
    private Vector3 Ax(Vector3 origin, float right, float up, float forward)
        => origin + _r * right + Vector3.up * up + _f * forward;

    /// <summary>축 기준 오프셋(높이는 월드 절대값 — 가슴 · 머리 높이에 맞출 때).</summary>
    private Vector3 At(Vector3 origin, float right, float worldY, float forward)
    {
        Vector3 p = origin + _r * right + _f * forward;
        p.y = worldY;
        return p;
    }

    /// <summary>
    /// 검을 든 모르드레드를 낮은 곳에서 올려다보는 구도 — S5(서임)와 S14(배신)가 같은 값을 쓴다.
    /// 머리가 아니라 가슴을 겨눈다 — AttackReady는 몸을 숙인 자세라 머리를 겨누면 그가 화면 아래로 빠졌다(10-10 2차 실측).
    /// </summary>
    // k = 거리 배율 — 속 빈 모델이 가까우면 이상해 1.3배로 물렸다(10-22 ③). S14는 AttackReady로 몸을 앞으로 숙여
    // 같은 자리에서 화면 높이 420 %(S5 144 %)였다 → 1.6배
    private void BladeShot(string label, float k = 1.3f)
        => Cut(label, Ax(_m, 1.0f * k, 1.2f, -2.8f * k), _mChest, 46f);

    /// <summary>배신의 일격 쇼트에만 쓰는 낮은 붉은 키 라이트 — 어둠 속에서 기사의 윤곽을 세운다.</summary>
    private GameObject SpawnKeyLight(Vector3 at)
    {
        var go = new GameObject("@IntroKeyLight");
        go.transform.position = at;
        var light = go.AddComponent<Light>();
        light.type      = LightType.Point;
        light.color     = new Color(0.95f, 0.28f, 0.22f);
        light.intensity = 6f;
        light.range     = 7f;
        light.shadows   = LightShadows.None;
        return go;
    }

    /// <summary>리치 머리 높이 — 켜진 뒤라면 렌더러 범위로, 아니면 피벗 + 1.8 m.</summary>
    private Vector3 LichHead(Vector3 lichAt)
    {
        if (lichActor != null)
        {
            var rends = lichActor.GetComponentsInChildren<Renderer>();
            bool any = false;
            var b = new Bounds();
            foreach (var r in rends)
            {
                if (r is ParticleSystemRenderer || !r.enabled) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            if (any && b.size.y > 0.5f) return new Vector3(b.center.x, b.center.y + b.extents.y * 0.6f, b.center.z);
        }
        return lichAt + Vector3.up * 1.8f;
    }

    /// <summary>컷 — 카메라를 그 자리에 바로 세운다. 벽 · 기둥 안이거나 대상을 가리면 대상 쪽으로 당겨 세운다.</summary>
    private static void Cut(string label, Vector3 pos, Vector3 lookAt, float fov)
    {
        var view = Camera.main;
        if (view == null) return;
        CurrentShot = label;
        pos = Unblock(pos, lookAt);
        view.transform.SetPositionAndRotation(pos, LookRot(pos, lookAt, view.transform.rotation));
        view.fieldOfView = fov;
        StartDrift(view);
    }

    private static void StartDrift(Camera view)
    {
        StopDrift();
        s_drift = new CancellationTokenSource();
        DriftAsync(view, view.fieldOfView, s_drift.Token).Forget();
    }

    private static void StopDrift()
    {
        s_drift?.Cancel();
        s_drift?.Dispose();
        s_drift = null;
    }

    /// <summary>컷이 걸린 동안 화각을 천천히 좁힌다 — 다음 컷 · 이동(<see cref="StopDrift"/>)이 끊는다.</summary>
    private static async UniTaskVoid DriftAsync(Camera view, float fov0, CancellationToken ct)
    {
        try
        {
            while (view != null)
            {
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
                view.fieldOfView = Mathf.Max(fov0 - DriftFovMax, view.fieldOfView - DriftFovRate * Time.deltaTime);
            }
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>이동 — 공개 · 강림처럼 카메라가 움직여야 하는 쇼트만 쓴다(대화는 컷).</summary>
    private static async UniTask MoveAsync(string label, Vector3 pos, Vector3 lookAt, float fov, float dur, CancellationToken ct)
    {
        var view = Camera.main;
        if (view == null) return;
        StopDrift();
        CurrentShot = label;
        pos = Unblock(pos, lookAt);
        var t = view.transform;
        Vector3 fromPos = t.position;
        Quaternion fromRot = t.rotation, toRot = LookRot(pos, lookAt, t.rotation);
        float fromFov = view.fieldOfView;
        float e = 0f;
        while (e < dur)
        {
            e += Time.deltaTime;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(e / dur));
            t.SetPositionAndRotation(Vector3.Lerp(fromPos, pos, k), Quaternion.Slerp(fromRot, toRot, k));
            view.fieldOfView = Mathf.Lerp(fromFov, fov, k);
            try { await UniTask.Yield(PlayerLoopTiming.Update, ct); }
            catch (OperationCanceledException) { return; }
        }
        t.SetPositionAndRotation(pos, toRot);
        view.fieldOfView = fov;
    }

    /// <summary>
    /// 카메라 자리 검사 — 벽 · 의자 · 기둥 속이면 위로 올리고, 대상과의 사이를 막으면 막힌 곳 앞으로 당긴다.
    /// 인물(플레이어 · 몬스터 레이어)은 막는 것으로 치지 않는다(어깨 너머 구도는 의도된 가림이다).
    /// </summary>
    internal static Vector3 Unblock(Vector3 pos, Vector3 lookAt)
    {
        for (int i = 0; i < 6 && Physics.CheckSphere(pos, CamRadius, EnvMask, QueryTriggerInteraction.Ignore); i++)
            pos += Vector3.up * 0.4f;
        Vector3 toCam = pos - lookAt;
        float dist = toCam.magnitude;
        if (dist <= 0.5f) return pos;
        // 인물에 붙은 콜라이더(기본 층)도 막는 것으로 치지 않는다 — 대상과 카메라 사이의 플레이어 몸에 걸려 카메라가 대상 0.8 m 앞
        // (플레이어 몸 속)까지 당겨졌다(10-16 실측: S4 플레이어 화면 높이 579~650%). 시작 구에 이미 겹친 것(거리 0)은 SphereCast처럼 뺀다.
        float best = float.MaxValue;
        foreach (var h in Physics.SphereCastAll(lookAt, CamRadius, toCam / dist, dist - 0.3f, EnvMask, QueryTriggerInteraction.Ignore))
            if (h.distance > 0f && h.distance < best && h.collider.GetComponentInParent<CharacterBase>() == null
                                  && h.collider.GetComponentInParent<RelicFairy.Monster.MonsterBase>() == null)
                best = h.distance;
        if (best < float.MaxValue) pos = lookAt + toCam / dist * Mathf.Max(0.8f, best - 0.2f);
        return pos;
    }

    private static Quaternion LookRot(Vector3 pos, Vector3 lookAt, Quaternion fallback)
    {
        Vector3 dir = lookAt - pos;
        return dir.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(dir.normalized, Vector3.up) : fallback;
    }

    private static Vector3 Flat(Vector3 v, Vector3 fallback)
    {
        v.y = 0f;
        return v.sqrMagnitude > 0.0001f ? v.normalized : fallback;
    }

    /// <summary>자막 한 줄 — 쇼트에 맞춰 앞 줄을 걷고 바로 띄운 뒤, 읽을 시간만큼 머문다(초당 13자 + 0.6초 · 10-16 흐름 — 예전 12자 + 0.8초).</summary>
    private static async UniTask SayAsync(DialogueLine[] lines, int index, CancellationToken ct)
    {
        if (lines == null || index < 0 || index >= lines.Length || lines[index] == null) return;
        var l = lines[index];
        RelicFairy.UI.UI_BossBark.ShowLatest(l.text, RelicFairy.UI.BossBarkType.Bark, l.speaker);
        int chars = System.Text.RegularExpressions.Regex.Replace(l.text ?? "", "<[^>]+>", "").Length;
        await Hold(Mathf.Clamp(chars / 13f + 0.6f, 1.6f, 4.2f), ct);
    }

    private static UniTask Hold(float seconds, CancellationToken ct) => DelaySafe(seconds, ct);

    // ── 헬퍼 ─────────────────────────────────────────────────────────
    /// <summary>리치 등장 — 로브 디졸브 인 + 등장 애니메이션. 전체 프리팹을 쓰지 않아 보스 AI가 컷신을 강탈하지 않는다.</summary>
    private async UniTask AwakenLichAsync(CancellationToken ct)
    {
        if (lichActor == null) return;

        // 어둠의 포탈이 먼저 열리고 그 안에서 리치가 나타난다 — 등장 프레임의 공백을 가려준다.
        if (lichPortalVfxPrefab != null)
            Instantiate(lichPortalVfxPrefab, lichActor.transform.position, lichActor.transform.rotation);

        lichActor.SetActive(true);

        // 애니메이터 컨트롤러 주입 — 실제 리치 보스는 MonsterBase.LoadAnimatorControllerAsync가
        // LichConfig의 주소로 런타임 로드해준다. 연출용 액터는 LichMonster를 제거했으므로
        // 그 주입이 일어나지 않아 컨트롤러가 null(=T포즈)이다. 여기서 같은 주소로 직접 채운다.
        await EnsureLichAnimatorAsync();

        var lichForm = lichActor.GetComponentInChildren<RelicFairy.Monster.LichFormController>(true);
        lichForm?.DissolveInFormAsync(RelicFairy.Monster.LichForm.Phase1, ct).Forget();

        PlayLich("Appear");

        // 등장 후 몸에 두르는 암흑 오라
        SpawnAttached(lichAuraVfxPrefab, lichActor.transform);
    }

    /// <summary>
    /// 리치 소멸 — 등장과 대칭. 어둠 포탈이 다시 열리고, 몸이 디졸브로 삼켜지며,
    /// 몸에 두른 오라도 함께 사라진다(연출요소: 포탈 VFX + 디졸브 + 오라 정리).
    /// </summary>
    private async UniTask VanishLichAsync(GameObject bindVfx, CancellationToken ct)
    {
        if (lichActor == null)
        {
            if (bindVfx != null) Destroy(bindVfx);
            return;
        }

        // 어둠 포탈이 다시 열려 리치를 거둬간다 — 등장 때와 같은 VFX로 왕복의 대칭을 만든다.
        if (lichPortalVfxPrefab != null)
            Instantiate(lichPortalVfxPrefab, lichActor.transform.position, lichActor.transform.rotation);

        // 몸을 디졸브로 지운다(리치 머티리얼은 폼 전환에 이미 디졸브를 쓰므로 호환).
        DissolveEffect.PlayDisappear(lichActor, lichVanishDuration);

        // 오라는 포탈이 삼키는 동안 함께 걷힌다.
        if (bindVfx != null) Destroy(bindVfx);

        await DelaySafe(lichVanishDuration, ct);
        if (lichActor != null) lichActor.SetActive(false);
    }

    /// <summary>리치 애니메이터에 컨트롤러가 없으면 Addressable로 로드해 채운다.</summary>
    private async UniTask EnsureLichAnimatorAsync()
    {
        if (_lichAnim == null) _lichAnim = lichActor.GetComponentInChildren<Animator>(true);
        if (_lichAnim == null || _lichAnim.runtimeAnimatorController != null) return;
        if (string.IsNullOrEmpty(lichAnimatorKey)) return;

        var obj = await Managers.AddressableManager.TryLoadAssetAsync<UnityEngine.Object>(lichAnimatorKey);
        if (obj is not RuntimeAnimatorController controller)
        {
            Debug.LogWarning($"[IntroMordredDirector] 리치 애니메이터 컨트롤러 로드 실패: {lichAnimatorKey}");
            return;
        }

        _lichAnim.runtimeAnimatorController = controller;
        _lichAnim.applyRootMotion = false;
        _lichAnim.Rebind();
        _lichAnim.Update(0f);
    }

    /// <summary>
    /// 무형검 획득 — 전시된 검이 그 자리에서 청록 테두리 디졸브로 사라지고, 사라지는 후반부(<see cref="swordHandOverlap"/>)에
    /// 손의 검이 <b>같은 디졸브</b>로 맺힌다 — 같은 검이 자리를 옮기는 것으로 읽힌다(10-18 사용자 「검이 사라지면서 플레이어 손에
    /// 생성되는 연출이 직접 집는 것보다 좋다」). 손을 뻗던 IK(IntroHandReach)는 「집는」 몸짓으로 읽혀 뺐다.
    /// (10-16: 검이 손까지 날아오던 것 → 사라진 뒤 손에 안개로 맺힘.) 전시 이펙트는 새 입자를 멈춰 잦아들게 한다. 카메라는 호출측이 잡아 둔다.
    /// 장착(무기 세트 · 서 있는 자세가 바뀐다)은 손의 검이 맺히기 시작할 때 — 쥐는 자세와 검이 함께 나타난다.
    /// </summary>
    private async UniTask AbsorbSwordAsync(PlayerController player, CancellationToken ct)
    {
        if (swordRenderer == null || player == null) return;

        var sword = swordRenderer.gameObject;

        // 부유 모션을 멈춰 사라지는 동안 흔들리지 않게 한다.
        var drifter = sword.GetComponentInParent<VoidDrifter>();
        if (drifter != null) drifter.enabled = false;

        var anim = player.Anim;
        Transform hand = anim != null && anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.RightHand) : null;

        // 전시 이펙트는 새 입자만 멈춘다 — 남은 빛살 · 알갱이는 제 수명대로 사라진다.
        if (_displayAura != null)
        {
            foreach (var ps in _displayAura.GetComponentsInChildren<ParticleSystem>())
                ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            Destroy(_displayAura.gameObject, 3f);
        }
        var vanish = DissolveEffect.PlayDisappearAsync(sword, swordDissolveDuration, ct);
        await DelaySafe(swordDissolveDuration * swordHandOverlap, ct);

        // 손에 맺힌다 — 작은 빛 고리 + 손의 검이 전시 검과 같은 디졸브로(켜기 전에 디졸브 재질을 씌워 첫 프레임 팝이 없다 —
        // 재질은 방금 전시 검이 불러 캐시에 있다)
        RunFx.Play(RunFxSlot.Ring, hand != null ? hand.position : _pChest, 0.35f, swordArriveTint);
        await EquipToSlotAsync(player, namelessWeaponKey, MeleeSlot, setActive: true,
                               playAppear: false, beforeShow: go => DissolveEffect.PlayAppear(go, swordCondenseDuration));
        await vanish;
        sword.SetActive(false);
        float tail = swordCondenseDuration - swordDissolveDuration * (1f - swordHandOverlap);   // 손의 검이 다 맺히기까지 남은 시간
        await DelaySafe(Mathf.Max(0f, tail) + 0.2f, ct);
    }

    /// <summary>
    /// 전시 이펙트(FogAura = Aura_Light — 바닥 원 · 빛살 · 빛 알갱이)를 검 바로 아래 바닥에 내려놓는다(10-16 사용자 「전시 이펙트도 적절한 포지션이 아닌 것 같아」).
    /// 허리 높이에 떠 있는 검의 자식이라 바닥 원이 공중에 떴고, 입자 배율 방식이 Local이라 씬에서 준 0.3배가 자식 입자엔 안 먹어
    /// 빛살이 원래 반경 2 m로 플레이어 둘레까지 퍼졌다 → 검에서 떼어 바닥에 두고(검이 떠다녀도 그대로) 배율 방식을 Hierarchy로.
    /// </summary>
    private void PlaceDisplayAura()
    {
        if (swordRenderer == null) return;
        _displayAura = swordRenderer.transform.Find("FogAura");
        if (_displayAura == null) return;
        _displayAura.SetParent(swordRenderer.transform.parent, true);
        Vector3 at = swordRenderer.transform.position;
        if (Physics.Raycast(at + Vector3.up * 0.2f, Vector3.down, out var hit, 6f, EnvMask, QueryTriggerInteraction.Ignore))
            _displayAura.position = hit.point + Vector3.up * 0.02f;
        _displayAura.rotation = Quaternion.identity;
        foreach (var ps in _displayAura.GetComponentsInChildren<ParticleSystem>(true))
        {
            var main = ps.main;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            ps.Clear(false);
            ps.Play(false);
        }
    }

    /// <summary>무형검 SO · 클립 · 프리팹을 미리 불러 둔다(장착은 S4에서).</summary>
    private async UniTaskVoid PrewarmNamelessAsync()
    {
        if (string.IsNullOrEmpty(namelessWeaponKey)) return;
        var so = await Managers.AddressableManager.TryLoadAssetAsync<WeaponSO>(namelessWeaponKey);
        if (so != null) await GameRunBootstrapper.PrewarmWeaponAsync(so);
    }

    // 무기 슬롯 규약 — 무형검(근접)=0, 원거리=1. PlayerWeaponManager 주석의 스테이션 규약과 동일.
    private const int MeleeSlot  = 0;
    private const int RangedSlot = 1;

    /// <summary>지정 Transform에 이펙트를 부착 생성. 프리팹/부모가 없으면 아무것도 하지 않는다.</summary>
    private GameObject SpawnAttached(GameObject prefab, Transform parent)
    {
        if (prefab == null || parent == null) return null;
        return Instantiate(prefab, parent.position, parent.rotation, parent);
    }

    /// <summary>컷신 시작 시 호출 — 각 횃불의 인스펙터 밝기를 목표값으로 기억하고 전부 꺼둔다.</summary>
    private void PrepareTorches()
    {
        if (combatTorches == null || combatTorches.Length == 0) return;

        _torchTargets = new float[combatTorches.Length];
        for (int i = 0; i < combatTorches.Length; i++)
        {
            var l = combatTorches[i];
            if (l == null) continue;
            _torchTargets[i] = l.intensity;
            l.intensity = 0f;
            if (!l.gameObject.activeSelf) l.gameObject.SetActive(true);
            l.enabled = true;
        }
    }

    /// <summary>보스전 개시 후 횃불을 하나씩 점등 — 어둠 속에서 전장이 순차로 드러난다.</summary>
    private async UniTaskVoid IgniteTorchesAsync(CancellationToken ct)
    {
        if (combatTorches == null || _torchTargets == null) return;

        await DelaySafe(torchIgniteDelay, ct);

        for (int i = 0; i < combatTorches.Length; i++)
        {
            var l = combatTorches[i];
            if (l == null) continue;
            FadeLightAsync(l, _torchTargets[i], torchFadeIn, ct).Forget();
            await DelaySafe(torchStepGap, ct);
        }
    }

    private static async UniTaskVoid FadeLightAsync(Light light, float target, float dur, CancellationToken ct)
    {
        if (light == null) return;
        if (dur <= 0f) { light.intensity = target; return; }

        float from = light.intensity, t = 0f;
        try
        {
            while (t < dur)
            {
                t += Time.deltaTime;
                if (light == null) return;
                light.intensity = Mathf.Lerp(from, target, Mathf.Clamp01(t / dur));
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
        }
        catch (OperationCanceledException) { return; }

        if (light != null) light.intensity = target;
    }

    private float[] _torchTargets;

    /// <summary>
    /// 타락 1단 — 모르드레드를 덮치는 사슬. 리치 쪽을 바라보게 세워 어디서 뻗어왔는지 읽히게 한다.
    /// 원샷 이펙트라 부모에 붙이지 않고 월드에 두고 스스로 소멸시킨다.
    /// </summary>
    private void SpawnChainStrike()
    {
        if (chainStrikeVfxPrefab == null || mordredTransform == null) return;

        Vector3 origin = mordredTransform.position;

        Transform src = lichCastPoint != null ? lichCastPoint : lichActor != null ? lichActor.transform : null;
        Quaternion rot = mordredTransform.rotation;
        if (src != null)
        {
            Vector3 dir = src.position - origin;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.0001f) rot = Quaternion.LookRotation(dir.normalized, Vector3.up);
        }

        var go = Instantiate(chainStrikeVfxPrefab, origin, rot);
        Destroy(go, 5f); // 원샷(최대 3초) + 여유
    }

    /// <summary>
    /// 타락 2단 — 모르드레드를 잠식하는 어둠. 몸(발밑 기준)에 부착해 그가 쓰러질 때까지 계속 감싼다.
    /// 루프 이펙트를 전제로 하며, 타락 완료 시 호출측이 Destroy한다.
    /// </summary>
    private GameObject SpawnCorruptionAura()
    {
        if (bindChainVfxPrefab == null || mordredTransform == null) return null;
        return Instantiate(bindChainVfxPrefab, mordredTransform.position, mordredTransform.rotation, mordredTransform);
    }

    /// <summary>
    /// 컷신 동안 플레이어를 지면에 고정한다.
    /// 플레이어는 useGravity=false(코드 중력)라 낙하가 FixedUpdate에 의존하는데,
    /// 차단형 대사(timeScale=0)에서는 FixedUpdate가 아예 멈춰 공중에 그대로 얼어붙는다.
    /// → 접지시킨 뒤 Rigidbody를 kinematic으로 돌려 물리에서 분리한다(컷신 종료 시 복구).
    /// </summary>
    private void FreezePlayerGrounded(PlayerController player)
    {
        if (player == null) return;

        SnapToGround(player);

        if (player.TryGetComponent<Rigidbody>(out var rb))
        {
            rb.linearVelocity  = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            _playerRb          = rb;
            _rbWasKinematic    = rb.isKinematic;
            rb.isKinematic     = true;   // timeScale=0 구간에도 위치가 흔들리지 않는다
        }
    }

    /// <summary>컷신 종료 — 물리 복구. 복구 직전 한 번 더 접지시켜 뜬 채로 풀리지 않게 한다.</summary>
    private void UnfreezePlayer(PlayerController player)
    {
        if (_playerRb == null) return;

        SnapToGround(player);
        _playerRb.isKinematic = _rbWasKinematic;
        _playerRb.linearVelocity  = Vector3.zero;
        _playerRb.angularVelocity = Vector3.zero;
        _playerRb = null;
    }

    private Rigidbody _playerRb;
    private bool      _rbWasKinematic;

    /// <summary>
    /// 플레이어를 발밑 지면에 내려놓는다. 플레이어는 useGravity=false(코드 중력)라
    /// 속도를 0으로 만든 순간 공중에 멈춰버리므로, 컷신 시작 시 반드시 접지시킨다.
    /// </summary>
    private static void SnapToGround(PlayerController player)
    {
        const float RayUp = 1.0f, RayDown = 30f;

        Vector3 origin = player.transform.position + Vector3.up * RayUp;
        var hits = Physics.RaycastAll(origin, Vector3.down, RayUp + RayDown, ~0, QueryTriggerInteraction.Ignore);
        if (hits == null || hits.Length == 0) return;

        // 자기 자신(플레이어 콜라이더)을 제외한 가장 가까운 지면
        float best = float.MaxValue;
        Vector3 point = Vector3.zero;
        bool found = false;
        foreach (var h in hits)
        {
            if (h.collider == null) continue;
            if (h.collider.GetComponentInParent<PlayerController>() != null) continue; // 자기 자신
            if (h.distance < best) { best = h.distance; point = h.point; found = true; }
        }

        if (found) player.transform.position = point;
    }

    private void PlayMordred(string state)
    {
        PlayAnimatorState(_mordredAnim, state);
    }

    private void PlayLich(string state)
    {
        PlayAnimatorState(_lichAnim, state);
    }

    /// <summary>
    /// 상태를 안전하게 재생한다. 오브젝트를 켠 <b>같은 프레임</b>에 CrossFade를 부르면 Animator가
    /// 아직 초기화 전이라 무시되고 기본 상태(Idle)만 남는다 — Rebind+Update(0)로 초기화를 강제한 뒤
    /// Play로 확정 재생한다.
    /// </summary>
    private static void PlayAnimatorState(Animator anim, string state)
    {
        if (anim == null || string.IsNullOrEmpty(state)) return;

        if (!anim.isInitialized)
        {
            anim.Rebind();
            anim.Update(0f);
            anim.Play(state, 0, 0f);   // 켠 같은 프레임 — 섞기가 무시되므로 확정 재생
            return;
        }
        anim.CrossFadeInFixedTime(state, StateBlend, 0, 0f);   // 앞 동작에서 섞어 넘긴다
    }

    /// <summary>
    /// 그 상태가 <paramref name="frac"/>(정규화 시간)까지 재생될 때까지 기다린다 — 다음 동작이 앞 동작을 중간에 자르지 않게.
    /// 다른 상태로 넘어갔거나 <paramref name="maxWait"/>초가 지나면 끝. 취소는 삼킨다(다른 대기와 같게 — 끝맺음은 호출부 취소 검사).
    /// </summary>
    private static async UniTask WaitStateAsync(Animator anim, string state, float frac, float maxWait, CancellationToken ct)
    {
        if (anim == null) return;
        int   hash  = Animator.StringToHash(state);
        float start = Time.time, until = start + maxWait;
        bool  seen  = false;
        try
        {
            while (Time.time < until)
            {
                // 섞기 요청은 다음 프레임부터 반영된다 — 같은 프레임에 보면 아직 앞 동작이라 바로 빠졌다(10-13 재측정: 동작 셋이 통째로 건너뜀)
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
                bool blending = anim.IsInTransition(0);
                var  info     = blending ? anim.GetNextAnimatorStateInfo(0) : anim.GetCurrentAnimatorStateInfo(0);
                if (info.shortNameHash == hash)
                {
                    seen = true;
                    if (info.normalizedTime >= frac) return;
                }
                else if (seen || Time.time - start > 0.3f) return;   // 다른 동작으로 넘어갔거나, 시작조차 안 됐다
            }
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>
    /// 무기 SO 키를 지정 슬롯에 장착한다. 프로젝트 정본 경로
    /// (<see cref="GameRunBootstrapper.EquipWeaponToPlayerAsync"/> — 애니 클립 프리로드 + 고정 슬롯)를 그대로 쓴다.
    /// AcquireWeaponAsync(key, autoEquip:false)는 소유 목록에만 넣고 슬롯 장착을 하지 않아 실제로 들리지 않는다.
    /// </summary>
    private static async UniTask EquipToSlotAsync(PlayerController player, string weaponSOKey, int slotIndex, bool setActive,
                                                  bool playAppear = true, Action<GameObject> beforeShow = null)
    {
        if (player == null || string.IsNullOrEmpty(weaponSOKey)) return;

        var so = await Managers.AddressableManager.TryLoadAssetAsync<WeaponSO>(weaponSOKey);
        if (so == null)
        {
            Debug.LogWarning($"[IntroMordredDirector] 무기 SO 로드 실패: {weaponSOKey}");
            return;
        }

        await GameRunBootstrapper.EquipWeaponToPlayerAsync(so, player, slotIndex, setActive, playAppear, beforeShow);
    }

    /// <summary>원거리 무기 튜토리얼 — 멀린 내레이션 톤의 비차단 자막으로 순차 표시.</summary>
    private void ShowRangedTutorial()
    {
        if (rangedTutorialLines == null) return;
        ShowRangedTutorialAsync(this.GetCancellationTokenOnDestroy()).Forget();
    }

    private async UniTaskVoid ShowRangedTutorialAsync(CancellationToken ct)
    {
        foreach (var line in rangedTutorialLines)
        {
            if (string.IsNullOrEmpty(line)) continue;
            RelicFairy.UI.UI_BossBark.Show(line, RelicFairy.UI.BossBarkType.MerlinNarration);
            await DelaySafe(rangedTutorialGap, ct);
            // DelaySafe는 취소를 삼킨다 — 씬이 바뀌어(디렉터 파괴) 취소되면 남은 줄을 다음 씬(거점)에 띄우지 않고 끝낸다(10-13 시뮬 4회차 S0)
            if (ct.IsCancellationRequested) return;
        }
    }

    private static async UniTask DelaySafe(float seconds, CancellationToken ct)
    {
        if (seconds <= 0f) return;
        try { await UniTask.Delay(TimeSpan.FromSeconds(seconds), cancellationToken: ct); }
        catch (OperationCanceledException) { }
    }
}
