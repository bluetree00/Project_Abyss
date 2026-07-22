using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 인트로 조우 컷신 — 구간 진입 즉시 발동(1회).
///
/// 원칙: <b>행동이 먼저, 대사는 나중</b>. 각 대사 앞에 그 대사를 정당화하는 화면 사건을 배치한다.
///
/// 비트 구성
///  ① 즉위식        : 모르드레드가 무형검을 들어 내민다 → Intro_Mordred (차단형 팝업 + 초상화)
///  ② 촛불이 꺼진다 : 부분 감광 → 리치 목소리(화면 밖)
///  ③ 리치 강림     : 디졸브 인 + Appear + 오라 → 리치 대사 2줄
///  ④ 모르드레드 경계: AttackReady로 검을 겨눔 → 저항 대사
///  ⑤ ★광선 연결   : DeathRayStart→Loop + 소울드레인 빔이 가슴에 꽂혀 유지 → 리치 선언
///  ⑥ 저항→붕괴    : GetHit1→2→3 연쇄 + 무형검 변색 → 신음 2줄
///  ⑦ 완성          : DeathRayEnd + 리치 소멸 + 기립 → 웅장한 배경 전환 → 보스전
///
/// 타락 구간 대사는 <b>비차단 UI_BossBark(화자줄 포함)</b>로 출력한다 —
/// 차단형 팝업은 timeScale=0이라 광선·애니메이션이 멈춰 인과가 보이지 않는다.
/// 대사 원본은 DIALOGUE_DATA.csv의 corruptSequenceId 시퀀스(라인 순서 = 비트 순서).
/// </summary>
public sealed class IntroMordredDirector : MonoBehaviour
{
    // ── [SerializeField] ─────────────────────────────────────────────
    [Header("발동 — 무형검 F키 픽업")]
    [Tooltip("플레이어가 무형검을 F로 획득하면 시퀀스가 시작된다. 비우면 Start에서 씬의 IntroSwordPickup을 자동 탐색.")]
    [SerializeField] private IntroSwordPickup swordPickup;

    [Header("대사 시퀀스 (DIALOGUE_DATA.csv)")]
    [SerializeField] private string mordredSequenceId     = "Intro_Mordred";
    [Tooltip("타락 대사 — 라인 순서가 곧 비트 순서다(0:예고 1,2:강림 3:저항 4:선언 5,6:신음).")]
    [SerializeField] private string corruptSequenceId     = "Intro_LichCorrupt";
    [SerializeField] private string combatStartSequenceId = "Mordred_Corrupted";
    [Tooltip("보스전 개시 시 UI_BossBark(BossIntro)로 띄울 보스 이름.")]
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

    [Header("모르드레드 연출 액터")]
    [SerializeField] private Transform  mordredTransform;    // 타락 VFX 부착 지점
    [SerializeField] private GameObject mordredActor;        // 컷신용 액터 — 전투 보스 등장 시 숨김
    [Tooltip("광선이 꽂힐 가슴 위치. 비우면 액터 위치 + 1.4m를 쓴다.")]
    [SerializeField] private Transform  mordredChestPoint;

    [Header("어둠 이펙트")]
    [Tooltip("리치 강림 — 어둠의 포탈이 열리며 등장(Effect_43_DarkPortal 등). 리치 위치에 생성.")]
    [SerializeField] private GameObject lichPortalVfxPrefab;
    [Tooltip("리치 상시 오라 — 등장 후 몸에 두른다(Aura_Dark_LWRP 등). 리치 액터에 부착.")]
    [SerializeField] private GameObject lichAuraVfxPrefab;
    [Tooltip("리치 캐스팅 — 손에 어둠을 모으는 예비 동작(Casting_Dark_3_LWRP 등). 캐스트 지점에 부착.")]
    [SerializeField] private GameObject lichCastVfxPrefab;
    [Tooltip("★타락 1단 — 모르드레드를 덮치는 사슬 공격(Effect_43_DarkChainSwamp 등). 원샷 이펙트.")]
    [SerializeField] private GameObject chainStrikeVfxPrefab;
    [Tooltip("사슬이 덮친 뒤 잠식이 시작되기까지의 간격(초).")]
    [SerializeField, Min(0.1f)] private float chainToCorruptGap = 1.2f;
    [Tooltip("★타락 2단 — 모르드레드를 잠식하는 어둠. 몸에 부착되므로 루프 이펙트여야 한다(Aura_Dark_LWRP 등).")]
    [SerializeField] private GameObject bindChainVfxPrefab;

    [Header("무형검 변색")]
    [Tooltip("타락과 함께 흰빛→검은빛으로 물드는 무형검 렌더러.")]
    [SerializeField] private Renderer swordRenderer;
    [SerializeField] private Color    swordCorruptedColor = new Color(0.18f, 0.06f, 0.24f);
    [SerializeField, Min(0.1f)] private float swordCorruptDuration = 2.0f;

    [Header("분위기 / 전환")]
    [SerializeField] private IntroAtmosphereSwitch atmosphere;
    [Tooltip("리치 강림 예고 단계에서 미리 어두워지는 정도(0~1).")]
    [SerializeField, Range(0f, 1f)] private float preDimAmount = 0.35f;
    [SerializeField] private float fadeDuration    = 1.0f;
    [Tooltip("타락→전투 전환 시 밝은 성당이 어둠으로 뒤집히는 웅장한 배경 전환 시간(초).")]
    [SerializeField] private float grandTransitionDuration = 2.5f;
    [Tooltip("컷신 카메라 샷 전환 시간(초).")]
    [SerializeField] private float camMoveDuration = 1.6f;
    [Tooltip("붕괴 구간에서 기본 플레이어 카메라로 되돌아가는 블렌드를 기다리는 시간(초).")]
    [SerializeField] private float cameraReturnBlend = 1.2f;

    [Header("시네마틱 레터박스")]
    [Tooltip("카메라 연출 구간(컷신 샷)에만 위아래 검은 바를 넣는다. 기본 카메라 복귀 시 걷힌다.")]
    [SerializeField] private bool  useLetterbox = true;
    [Tooltip("레터박스 슬라이드 인/아웃 시간(초).")]
    [SerializeField] private float letterboxDuration = 0.5f;
    [Tooltip("목표 종횡비. 2.39=시네마스코프.")]
    [SerializeField] private float letterboxAspect = 2.39f;
    [Tooltip("레터박스 캔버스 정렬 순서. UI 팝업(200~)보다 낮아야 대사창을 가리지 않는다.")]
    [SerializeField] private int   letterboxSortingOrder = 150;
    [Tooltip("전투 진입 시 HUD 페이드 인 시간(초).")]
    [SerializeField] private float hudFadeDuration = 0.8f;
    [Tooltip("보스 등장 연출(카메라 팬)이 끝나 조작이 돌아온 뒤 HUD를 띄우기까지의 대기(초).")]
    [SerializeField] private float hudFadeDelay = 6f;

    [Header("무형검 하사")]
    [Tooltip("검이 모르드레드에게서 플레이어에게 건너가는 시간(초).")]
    [SerializeField, Min(0.2f)] private float swordHandOverDuration = 1.4f;

    [Header("무기 지급 (Addressable 무기 SO 키)")]
    [Tooltip("무형검 획득 시 슬롯0(근접)에 장착할 무기 SO 키.")]
    [SerializeField] private string namelessWeaponKey = "T0_Nameless";
    [Tooltip("보스전 진입 시 슬롯1(원거리)에 장착할 무기 SO 키.")]
    [SerializeField] private string rangedWeaponKey = "T1_Bow";

    [Header("원거리 무기 튜토리얼")]
    [Tooltip("전투 진입 시 순서대로 표시할 튜토리얼 안내(비차단 자막). 실제 키 매핑에 맞춰 문구 수정.")]
    [SerializeField, TextArea] private string[] rangedTutorialLines =
    {
        "타락한 기사는 검이 닿지 않는다. — 원거리 무기로 맞서라.",
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
    }

    private void OnDestroy()
    {
        if (swordPickup != null) swordPickup.OnPickup -= HandleSwordPickup;
    }

    /// <summary>무형검을 F로 획득한 순간 시퀀스를 시작한다. 1회만.</summary>
    private void HandleSwordPickup(PlayerController player)
    {
        if (_started || player == null) return;
        _player = player;
        RunAsync(this.GetCancellationTokenOnDestroy()).Forget();
    }

    // ── 연출 시퀀스 ──────────────────────────────────────────────────
    private async UniTaskVoid RunAsync(CancellationToken ct)
    {
        if (_started) return;
        _started = true;
        var player = _player;

        try
        {
            // 플레이어 통제 — 컷신 동안 입력 차단 + 남은 관성 제거 + 카메라 수동 제어 인계.
            player?.SetInputEnabled(false);
            FreezePlayerGrounded(player);
            PrepareTorches();   // 목표 밝기를 기억하고 전부 소등(점등은 보스전 개시 후)
            // HUD 억제 재확인 — 부팅 타이밍으로 억제가 누락됐어도 컷신 중엔 확실히 숨긴다.
            // (전투 진입 시 FadeInHudAsync가 억제를 풀고 페이드로 띄운다)
            UIRootBootstrapper.Instance?.SetHudStartRoomSuppressed(true);
            var cam = GameCameraController.Instance;
            cam?.TakeManualControl();

            // 카메라 연출 구간 진입 — 시네마틱 레터박스 인(기본 카메라 복귀 시 걷힌다).
            if (useLetterbox)
                CinematicFrame.ShowAsync(letterboxDuration, null, ct, letterboxAspect, 0f,
                                         title: null, sortingOrder: letterboxSortingOrder).Forget();

            var lines = Managers.DialogueData?.GetLines(corruptSequenceId);

            Vector3 mordredPos = mordredTransform != null ? mordredTransform.position : new Vector3(0f, 1f, 2.26f);
            Vector3 lichPos    = lichActor != null ? lichActor.transform.position : mordredPos + new Vector3(3f, 2f, 5f);

            // ── ① 무형검 획득 — 검과 플레이어 손을 한 화면에 담고, 그 샷이 끝난 뒤 획득 연출.
            //     (샷을 Forget으로 던지면 다음 즉위식 샷과 겹쳐 카메라가 뒤엉킨다 → 반드시 await)
            {
                Vector3 swordPos = swordRenderer != null ? swordRenderer.transform.position : mordredPos;
                Vector3 handPos  = player != null ? player.transform.position + Vector3.up * 1.2f : swordPos;
                Vector3 mid      = Vector3.Lerp(swordPos, handPos, 0.5f);
                await FrameCameraAsync(mid + new Vector3(2.2f, 1.0f, -2.6f), mid, camMoveDuration * 0.8f, ct);
            }
            await AbsorbSwordAsync(player, ct);

            // ── ①-b 즉위식 대사 — 검을 쥔 직후, 모르드레드가 이를 인정한다.
            await FrameCameraAsync(mordredPos + new Vector3(3.5f, 2.2f, -6f), mordredPos + new Vector3(0f, 1.6f, 0f), camMoveDuration, ct);
            PlayMordred("AttackReady");
            await PlaySequenceAsync(mordredSequenceId);

            // ── ② 촛불이 꺼진다 — 부분 감광 후 화면 밖 목소리
            if (atmosphere != null)
                atmosphere.DimStepAsync(preDimAmount, 1.4f, ct).Forget();
            await FrameCameraAsync(mordredPos + new Vector3(1.2f, 2.6f, -5.2f), mordredPos + new Vector3(0f, 3.5f, 2f), camMoveDuration, ct);
            await BarkAsync(lines, 0, 2.0f, ct);

            // ── ③ 리치 강림 — 디졸브 인 + Appear + 로우앵글 푸시인
            await AwakenLichAsync(ct);

            // 강림 샷 ①: 바닥 가까이서 올려다보며 리치를 크게 잡는다(위압).
            await FrameCameraAsync(lichPos + new Vector3(-3.2f, -1.4f, -5.2f), lichPos + new Vector3(0f, 0.6f, 0f), camMoveDuration, ct);
            await BarkAsync(lines, 1, 2.6f, ct);
            // 강림 샷 ②: 리치 주위를 돌며 천천히 다가간다(궤도 이동으로 존재감 유지).
            await FrameCameraAsync(lichPos + new Vector3(2.6f, 0.4f, -3.6f), lichPos + new Vector3(0f, 0.2f, 0f), camMoveDuration * 1.2f, ct);
            await BarkAsync(lines, 2, 2.4f, ct);

            // ── ④ 카메라 연출은 여기까지 — 리치를 보여줬으니 <b>기본 플레이어 카메라</b>로 되돌린다.
            //     이후 저항·타락·붕괴는 전부 평소 조작 시점에서 진행된다. 레터박스도 함께 걷는다.
            if (useLetterbox)
                CinematicFrame.HideAsync(letterboxDuration, null, ct).Forget();
            if (player != null) cam?.HandToGameplayCamera(player.transform);
            await DelaySafe(cameraReturnBlend, ct);

            // 모르드레드 경계 — 검을 겨눈 뒤 저항 대사
            PlayMordred("AttackReady");
            await BarkAsync(lines, 3, 2.2f, ct);

            // ── ⑤ ★타락 — 리치가 손에 어둠을 모으고, 사슬이 모르드레드를 휘감아 속박한다.
            PlayLich("DeathRayStart");
            var castVfx = SpawnAttached(lichCastVfxPrefab, lichCastPoint != null ? lichCastPoint : lichActor?.transform);
            await DelaySafe(0.5f, ct);

            // 1단 — 사슬이 모르드레드를 덮친다(원샷). 리치 선언 대사가 이 순간에 얹힌다.
            PlayLich("DeathRayLoop");
            SpawnChainStrike();
            await BarkAsync(lines, 4, chainToCorruptGap, ct);

            // 2단 — 사슬이 걷힌 자리에서 어둠이 몸을 잠식하기 시작한다(루프). 이펙트는 하나만.
            var bindVfx = SpawnCorruptionAura();
            await DelaySafe(0.5f, ct);

            // ── ⑥ 저항 → 붕괴 (카메라는 이미 기본 시점으로 복귀한 상태)
            PlayMordred("GetHit1");
            await DelaySafe(0.9f, ct);
            PlayMordred("GetHit2");
            CorruptSwordAsync(ct).Forget();
            await BarkAsync(lines, 5, 1.4f, ct);
            PlayMordred("GetHit3");
            await BarkAsync(lines, 6, 1.6f, ct);

            // ── ⑦ 완성 — 속박 해제 + 리치 소멸 + 기립
            PlayLich("DeathRayEnd");
            if (castVfx != null) Destroy(castVfx);
            if (bindVfx != null) Destroy(bindVfx);
            await DelaySafe(lichLingerAfter, ct);
            if (lichActor != null) lichActor.SetActive(false);
            PlayMordred("Idle2");
            await DelaySafe(0.8f, ct);

            // ── ⑧ 웅장한 배경 전환 — 밝은 성당이 어둠 + 핵심 라이팅으로 뒤집힘
            if (atmosphere != null)
                await atmosphere.SwitchToDarkAsync(grandTransitionDuration, ct);
            else
            {
                await ScreenFade.Out(fadeDuration, ct);
                await ScreenFade.In(fadeDuration, ct);
            }

            // ── ⑨ 보스전 개시 — 암전 중 플레이어를 아레나로 옮긴다(보스는 아레나 원위치).
            await ScreenFade.Out(fadeDuration, ct);

            if (mordredActor != null) mordredActor.SetActive(false);
            if (player != null && arenaEntryPoint != null)
                player.transform.SetPositionAndRotation(arenaEntryPoint.position, arenaEntryPoint.rotation);

            // 컷신 종료 — 물리 복구(접지 후 kinematic 해제). 전투 중엔 정상 중력이 필요하다.
            UnfreezePlayer(player);

            if (player != null) cam?.HandToGameplayCamera(player.transform);

            await ScreenFade.In(fadeDuration, ct);

            if (bossRoom != null && player != null)
                bossRoom.BeginBossFightExternally(player, bossUnbeatable);

            // ── ⑩ 전투 개시 대사 — 보스 등장 연출과 함께 흐른다(비차단 자막).
            ShowCombatBarks();
            // 어둠 속에서 횃불이 하나씩 켜지며 전장이 드러난다(공격 판정 가시성 확보).
            IgniteTorchesAsync(ct).Forget();

            // ── ⑪ 보스 등장 연출이 끝나 조작이 돌아온 뒤에야 HUD를 페이드로 띄운다.
            //     (등장 카메라 팬 중에 HUD가 떠 있으면 연출을 깬다)
            await DelaySafe(hudFadeDelay, ct);
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
            UnfreezePlayer(player); // 컷신이 끊겨도 플레이어가 kinematic으로 굳지 않게
            if (useLetterbox) CinematicFrame.HideAsync(0f, null).Forget(); // 레터박스가 화면에 남지 않게
        }
    }

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
    /// 무형검 획득 — 손에 무기가 디졸브로 나타나고, 제단의 검은 알파가 빠지며 사라진다.
    /// 검이 날아오지 않으므로 플레이어를 건드리지 않는다(부유·이동 문제 없음).
    /// 카메라는 호출측이 잡아두므로 여기서는 건드리지 않는다.
    /// </summary>
    private async UniTask AbsorbSwordAsync(PlayerController player, CancellationToken ct)
    {
        if (swordRenderer == null || player == null) return;

        var sword = swordRenderer.gameObject;

        // 부유 모션을 멈춰 사라지는 동안 흔들리지 않게 한다.
        var drifter = sword.GetComponentInParent<VoidDrifter>();
        if (drifter != null) drifter.enabled = false;

        // 검에 붙은 이펙트(FogAura)를 먼저 끈다 — 검이 사라져도 파티클만 공중에 남는 것 방지.
        foreach (var ps in sword.GetComponentsInChildren<ParticleSystem>(true))
            ps.gameObject.SetActive(false);

        // 손의 검이 디졸브로 나타난다(장착 시 PlayerWeaponManager가 PlayAppear 재생).
        await EquipToSlotAsync(player, namelessWeaponKey, MeleeSlot, setActive: true);

        // 제단의 검은 <b>머티리얼을 교체하지 않고</b> 알파만 낮추며 사라진다.
        // DissolveEffect는 머티리얼을 통째로 갈아끼우는 방식이라, 반투명 안개 머티리얼
        // (M_NamelessFog, alpha 0.32)에 씌우면 셰이더가 맞지 않아 핑크 잔상이 남는다.
        await FadeOutSwordAsync(swordRenderer, swordHandOverDuration, ct);

        sword.SetActive(false);
        await DelaySafe(0.3f, ct);
    }

    /// <summary>렌더러의 색 알파를 0까지 낮추며 서서히 지운다(머티리얼 교체 없음 → 셰이더 깨짐 없음).</summary>
    private static async UniTask FadeOutSwordAsync(Renderer renderer, float dur, CancellationToken ct)
    {
        if (renderer == null) return;

        var mats = renderer.materials;              // 접근 시점에 인스턴스 복제 — 원본 에셋 무오염
        if (mats == null || mats.Length == 0) return;

        var startColors = new Color[mats.Length];
        for (int i = 0; i < mats.Length; i++)
        {
            if (mats[i] == null) continue;
            startColors[i] = mats[i].HasProperty(BaseColorId) ? mats[i].GetColor(BaseColorId)
                           : mats[i].HasProperty(ColorId)     ? mats[i].GetColor(ColorId)
                           : Color.white;
        }

        float t = 0f;
        try
        {
            while (t < dur)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / dur);
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    var c = startColors[i];
                    c.a = Mathf.Lerp(startColors[i].a, 0f, k);
                    if (mats[i].HasProperty(BaseColorId)) mats[i].SetColor(BaseColorId, c);
                    if (mats[i].HasProperty(ColorId))     mats[i].SetColor(ColorId, c);
                }
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
        }
        catch (OperationCanceledException) { }
    }

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId     = Shader.PropertyToID("_Color");

    // 무기 슬롯 규약 — 무형검(근접)=0, 원거리=1. PlayerWeaponManager 주석의 스테이션 규약과 동일.
    private const int MeleeSlot  = 0;
    private const int RangedSlot = 1;

    /// <summary>지정 Transform에 이펙트를 부착 생성. 프리팹/부모가 없으면 아무것도 하지 않는다.</summary>
    private GameObject SpawnAttached(GameObject prefab, Transform parent)
    {
        if (prefab == null || parent == null) return null;
        // [임시 추적] 초록 이펙트 출처 확인용 — 확인 후 제거할 것.
        Debug.Log($"[VFX추적] IntroMordredDirector.SpawnAttached: {prefab.name} → {parent.name}");
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

    /// <summary>무형검이 흰빛에서 검은빛으로 물든다. 머티리얼은 인스턴스로 복제해 원본을 오염시키지 않는다.</summary>
    private async UniTaskVoid CorruptSwordAsync(CancellationToken ct)
    {
        if (swordRenderer == null) return;

        var mat = swordRenderer.material; // 접근 시점에 인스턴스 복제됨
        if (mat == null) return;

        Color from = mat.HasProperty(BaseColorId) ? mat.GetColor(BaseColorId) : Color.white;

        float t = 0f;
        try
        {
            while (t < swordCorruptDuration)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / swordCorruptDuration);
                var c = Color.Lerp(from, swordCorruptedColor, k);
                if (mat.HasProperty(BaseColorId)) mat.SetColor(BaseColorId, c);
                if (mat.HasProperty(ColorId))     mat.SetColor(ColorId, c);
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
        }
        catch (OperationCanceledException) { }
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
        }
        anim.Play(state, 0, 0f);
    }

    /// <summary>비차단 자막 1줄 — 화자줄 포함. 표시 직후 hold만큼 기다려 다음 비트로 넘어간다.</summary>
    private async UniTask BarkAsync(DialogueLine[] lines, int index, float hold, CancellationToken ct)
    {
        if (lines != null && index >= 0 && index < lines.Length)
        {
            var l = lines[index];
            RelicFairy.UI.UI_BossBark.Show(l.text, RelicFairy.UI.BossBarkType.Bark, l.speaker);
        }
        await DelaySafe(hold, ct);
    }

    /// <summary>
    /// 무기 SO 키를 지정 슬롯에 장착한다. 프로젝트 정본 경로
    /// (<see cref="GameRunBootstrapper.EquipWeaponToPlayerAsync"/> — 애니 클립 프리로드 + 고정 슬롯)를 그대로 쓴다.
    /// AcquireWeaponAsync(key, autoEquip:false)는 소유 목록에만 넣고 슬롯 장착을 하지 않아 실제로 들리지 않는다.
    /// </summary>
    private static async UniTask EquipToSlotAsync(PlayerController player, string weaponSOKey, int slotIndex, bool setActive)
    {
        if (player == null || string.IsNullOrEmpty(weaponSOKey)) return;

        var so = await Managers.AddressableManager.TryLoadAssetAsync<WeaponSO>(weaponSOKey);
        if (so == null)
        {
            Debug.LogWarning($"[IntroMordredDirector] 무기 SO 로드 실패: {weaponSOKey}");
            return;
        }

        await GameRunBootstrapper.EquipWeaponToPlayerAsync(so, player, slotIndex, setActive);
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
        }
    }

    /// <summary>전투 개시 후 보스 이름 + 전투 대사를 화자줄 포함 비차단 자막으로 표시.</summary>
    private void ShowCombatBarks()
    {
        if (!string.IsNullOrEmpty(bossIntroName))
            RelicFairy.UI.UI_BossBark.Show(bossIntroName, RelicFairy.UI.BossBarkType.BossIntro);

        if (string.IsNullOrEmpty(combatStartSequenceId)) return;
        var lines = Managers.DialogueData?.GetLines(combatStartSequenceId);
        if (lines == null) return;
        foreach (var l in lines)
            RelicFairy.UI.UI_BossBark.Show(l.text, RelicFairy.UI.BossBarkType.Bark, l.speaker);
    }

    /// <summary>컷신 카메라를 지정 위치/시선으로 부드럽게 이동(TakeManualControl 전제).</summary>
    private static async UniTask FrameCameraAsync(Vector3 pos, Vector3 lookAt, float dur, CancellationToken ct)
    {
        var cam = Camera.main;
        if (cam == null) return;
        var t = cam.transform;
        Vector3 startPos = t.position;
        Quaternion startRot = t.rotation;
        Vector3 dir = lookAt - pos;
        Quaternion endRot = dir.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(dir.normalized, Vector3.up) : t.rotation;
        if (dur <= 0f) { t.SetPositionAndRotation(pos, endRot); return; }

        float e = 0f;
        while (e < dur)
        {
            e += Time.deltaTime;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(e / dur));
            t.SetPositionAndRotation(Vector3.Lerp(startPos, pos, k), Quaternion.Slerp(startRot, endRot, k));
            try { await UniTask.Yield(PlayerLoopTiming.Update, ct); }
            catch (OperationCanceledException) { return; }
        }
        t.SetPositionAndRotation(pos, endRot);
    }

    private static async UniTask PlaySequenceAsync(string seqId)
    {
        if (string.IsNullOrEmpty(seqId)) return;

        var dlg = Managers.DialogueData;
        if (dlg == null) return;
        if (!dlg.IsInitialized) await dlg.InitializeAsync();

        var lines = dlg.GetLines(seqId);
        if (lines == null || lines.Length == 0) return;

        await Managers.UI.WaitUntilNoBlockingPopupAsync();
        var popup = await Managers.UI.ShowPopupUIAndGetAsync<UI_DialoguePopup>();
        if (popup != null) await popup.ShowAsync(lines);
    }

    private static async UniTask DelaySafe(float seconds, CancellationToken ct)
    {
        if (seconds <= 0f) return;
        try { await UniTask.Delay(TimeSpan.FromSeconds(seconds), cancellationToken: ct); }
        catch (OperationCanceledException) { }
    }
}
