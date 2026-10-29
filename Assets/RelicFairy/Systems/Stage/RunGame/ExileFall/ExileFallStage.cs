using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using OccaSoftware.Buto.Runtime;
using RelicFairy.Monster;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 리치 추방 — 추락 무대(10-11). 같은 씬 아주 아래(y −3000)에 런타임으로 짓는 수직 허공이다. 챕터 씬 · 아레나 프리팹은 불러오지 않는다.
/// 층 넷(Ch4 → Ch3 → Ch2 → Ch1)이 아래에서 위로 스쳐 간다(층당 약 2.2초). 이펙트 절제(10-10 톤) — 층마다 파편 한 종류 · 봉인진 하나.
///   · Ch4 천공의 대제단 — 보라 · 차가운 빛 · 무너진 제단 돌(층계 세트 디딤판). 봉인진 없음(리치는 위에 남았다 · 칸 60 보라 문양과 겹치지 않게)
///   · Ch3 성채 — 붉은 금 · 스테인드글라스 빛 조각 · 기사 봉인진이 깨진다 → 붉은 섬광 · 포효
///   · Ch2 동굴 — 주황 · 불씨 · 화룡 봉인진이 깨진다 → 포효
///   · Ch1 숲 — 초록 안개 · 나뭇잎 · 숲의 수호자 봉인진이 깨진다 → 포효 → 바닥이 보인다
/// 봉인진 · 섬광은 보스 봉인 칸(80 · 81)을 그 보스의 봉인 색으로 곱한다(<see cref="BossStoryScenes.TryGetLook"/>).
/// <para>10-13 사용자 「그 보스의 봉인이 깨진다는 걸 느낄 수 있게」 — 봉인진 층마다 그 보스가 봉인된 모습(주저앉아 사슬 넷)으로 층 앞쪽 봉인진 위에 있고,
/// 몸이 다가오면 버둥 → 사슬이 하나씩 끊김 → 일어섬 → 봉인진이 깨지며 포효(<see cref="ExileFallBossCast"/>). 봉인진은 몸이 지나는 한가운데가 아니라 보스 발밑으로 옮겼다.
/// 모델이 늦게 읽히면 그 층은 봉인진만.</para>
/// 카메라 배경 · 안개 · 탑 원통은 무대가 도는 동안만 바꾸고 <see cref="RestoreView"/>가 되돌린다(착지 와이프가 화면을 덮은 뒤).
/// <para>10-12 83 실측 결함 고침:
/// · 층 넷이 같은 보라였다 — Buto 볼류메트릭 안개는 높이 안개라(<c>exp(-max(y − 기준 높이, 0) / 경계)</c>, 기준 −2) y −3000에선 최대 밀도로
///   화면 전체를 Ch4 빛깔(연보라)로 덮었다. 무대 동안 우선순위 100 볼륨으로 Buto를 끈다 → 층 색 = 카메라 배경 + 씬 안개 색.
/// · 층 색 = 83 챕터 톤 색표(Ch1 #142B25 · Ch2 #422414 · Ch3 #212938 · Ch4 #261F42)에 허공 밝기만 올린 것(<see cref="VoidLift"/>).
/// · 플레이어 · 바닥이 청록으로 그려졌다 — 안개 모드를 Linear로 바꿔 새 셰이더 변형이 생겼고, 에디터가 컴파일하는 동안 청록 대체 셰이더로 그렸다.
///   안개 모드는 씬 그대로 두고(꺼져 있으면 켜지 않는다) 색 · 밀도만 바꾼다.
/// · 파편이 안 보였다 — 탑 먼지 재질의 텍스처가 고리(Circle17)라 0.1~0.3 m 고리로 그려졌다. 같은 재질을 복제해 코드로 만든 부드러운 점 · 조각 · 잎 모양을 입히고
///   크기 · 개수를 화면에서 읽히게 올렸다(층마다 파편 한 종류는 그대로).</para>
/// </summary>
public sealed class ExileFallStage : MonoBehaviour
{
    // ── Constants ─────────────────────────────────────────────────
    public const float Depth         = -3000f;   // 무대 꼭대기 높이
    public const float FallSpeed     = 24f;      // m/s — 아레나 낙하 끝 속도(약 22)와 이어진다
    public const float LandAt        = 9.35f;    // 착지 섬광 — 바닥이 약 10 m 앞
    public const float SpinDegPerSec = 28f;      // 몸이 천천히 돈다

    private const float FloorAt     = 9.75f;
    private const float ShadowAt    = 0.35f;     // 그림자 「그 봉인이 버틸 줄 알았어?」(붕괴 컷신 끝에서 옮겨 왔다)
    private const float Line1At     = 2.5f;      // 멀린 「봉인이… 하나씩 끊어지고 있어.」(Ch3 무렵)
    private const float Line2At     = 6.9f;      // 멀린 「붙잡아! 바닥이야!」(Ch1 무렵)
    private const float HoldAround  = 0.6f;      // 층 한가운데 앞뒤로 그 층 빛깔을 붙든다(그 사이만 섞는다)
    private const float BandHeight  = 48f;       // 층 하나의 파편 높이(m) ≈ 층당 2.2초 × 낙하 속도
    private const float BandWidth   = 22f;
    private const float SigilScale  = 0.6f;      // 봉인진 칸 배율 1 = 반경 10 m → 반경 6 m(몸이 한가운데로 지나간다)
    private const float BurstScale  = 0.9f;
    private const float CastGap     = 2f;        // 보스 발밑 = 몸이 지나는 길에서 옆으로 (사슬 반경 × 크기) + 이만큼(몸과 겹치지 않게 — 10-13 ②f 사진)
    private const float CastAhead   = 2f;        //   카메라가 보는 쪽으로 조금 앞
    private const float CastDrop    = 3f;        //   층 한가운데보다 이만큼 아래 — 다가오는 동안 화면에 먼저 든다
    private const float CamBack     = 4f;        // 카메라 — 플레이어 위 비스듬히(리치 반대편)
    private const float CamHeight   = 5.5f;
    private const float LookDrop    = 0.8f;      // 몸보다 조금 아래를 본다 — 다가오는 층이 화면에 먼저 든다
    private const float CamOrbitDegPerSec = 6f;
    private const float CamSettle   = 5f;        // 넘겨받은 카메라 오차가 줄어드는 빠르기(1/초)
    private const float CamJitter   = 0.035f;    // 흔들림은 「살짝」이 상한(10-01 메모)
    private const float BreakShake  = 0.08f;
    private const float ShakeDecay  = 0.3f;      // 1초에 줄어드는 양
    private const float LightRange  = 20f;
    private const float LightIntensity = 3f;
    private const float FogDensityExp2 = 0.02f;  // 지수²: 50 m 63% · 80 m 92% — 다음 층은 희미하게, 그 너머는 층 색 허공
    private const float FogDensityExp  = 0.03f;
    private const float FogStart    = 12f;       // 씬이 Linear일 때만
    private const float FogEnd      = 80f;
    private const float VoidLift    = 1.5f;      // 챕터 안개 색(휘도 0.13~0.17)을 허공 배경으로 쓸 때 올리는 밝기 — 색조는 그대로
    private const float ButoOffPriority = 100f;  // 챕터 볼륨(1) · 기본 톤(0) 위
    private const int   ShapeSize   = 64;        // 파편 모양 텍스처(픽셀)
    private const int   StoneCount  = 6;

    private static readonly int   BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly Color FloorColor  = new(0.07f, 0.11f, 0.06f, 1f);
    private static readonly Color RedFlash    = new(0.95f, 0.3f, 0.22f, 1f);
    // 앞쪽 반원에만 돌을 놓는다(카메라는 뒤 · 위) — 리치 → 플레이어 방향 기준 각(도)
    private static readonly float[] StoneAngles = { -95f, -55f, -15f, 25f, 65f, 105f };

    private enum Debris { Stone, Glass, Ember, Leaf }

    private readonly struct Layer
    {
        public readonly string Name;
        public readonly string BossId;    // null = 봉인진 없음
        public readonly Color  Fog;
        public readonly Color  Light;
        public readonly Debris Debris;
        public readonly float  At;        // 몸이 층 한가운데(봉인진)를 지나는 시각(초)
        public readonly int    Roar;      // 포효 칸(@GameRun) — -1 = 없음
        public readonly bool   RedFlash;

        public Layer(string name, string bossId, Color fog, Color light, Debris debris, float at, int roar, bool redFlash)
        {
            Name = name; BossId = bossId; Fog = fog; Light = light; Debris = debris; At = at; Roar = roar; RedFlash = redFlash;
        }
    }

    private static readonly Layer[] Layers =
    {
        // 안개 = 83 챕터 톤 색표(구현설계 「챕터별_톤·포그·배경_점검_20261012」 §4) · 빛 = 층 악센트(설계 표 — 차가운 보라 · 촛불 · 불씨 · 숲빛)
        new("Ch4 천공의 대제단", null,                          new Color32(0x26, 0x1F, 0x42, 0xFF), new Color(0.62f, 0.5f, 1f),   Debris.Stone, 0.9f, -1, false),
        new("Ch3 성채",         StoryProgress.DeathKnight,     new Color32(0x21, 0x29, 0x38, 0xFF), new Color(1f, 0.66f, 0.38f),  Debris.Glass, 3.1f,  0, false),   // 10-13 붉은 번쩍 끔 — 네 층 중 홀로 번쩍였고 봉인진 터짐 · 포효 · 흔들림이 이미 깨짐을 알린다(84 검토)
        new("Ch2 동굴",         StoryProgress.Dragon,          new Color32(0x42, 0x24, 0x14, 0xFF), new Color(1f, 0.52f, 0.22f),  Debris.Ember, 5.3f,  1, false),
        new("Ch1 숲",           StoryProgress.ForestGuardian,  new Color32(0x14, 0x2B, 0x25, 0xFF), new Color(0.6f, 0.95f, 0.6f), Debris.Leaf,  7.5f,  2, false),
    };

    // ── Private ───────────────────────────────────────────────────
    private readonly GameObject[] _sigils     = new GameObject[4];
    private readonly Color[]      _sealColors = new Color[4];
    private readonly Vector3[]    _sigilAt    = new Vector3[4];
    private readonly ExileFallBossCast[] _casts = new ExileFallBossCast[4];   // 층의 봉인된 보스(봉인진 층만)
    private Vector3     _top;
    private Vector3     _away;            // 리치 → 플레이어(수평)
    private Transform[] _stones;
    private Vector3[]   _stoneAxes;
    private float[]     _stoneSpeeds;
    private Material    _particleMat;     // 탑 먼지 재질(원본 — 건드리지 않는다)
    private readonly List<Object>         _owned  = new();   // 무대가 만든 재질 · 텍스처 · 프로파일 — 지워질 때 함께 지운다
    private readonly List<ParticleSystem> _clouds = new();
    private Volume      _butoOff;
    private Light       _light;
    private Transform   _cam;
    private Camera      _camera;
    private GameObject  _shell;           // 탑 원통(DungeonSpaceDirector) — 몸을 늦게 따라와 화면을 가로지르지 않게 무대 동안 끈다
    private float       _shake;
    private bool        _viewTaken;
    private CameraClearFlags _clearBefore;
    private Color       _bgBefore;
    private bool        _fogBefore;
    private FogMode     _fogModeBefore;
    private Color       _fogColorBefore;
    private float       _fogStartBefore;
    private float       _fogEndBefore;
    private float       _fogDensityBefore;

    // ── Properties ────────────────────────────────────────────────
    public Vector3 Top      => _top;
    /// <summary>추락이 시작된 뒤 흐른 실시간(초). 시작 전이면 -1.</summary>
    public float   FallTime { get; private set; } = -1f;
    /// <summary>층 파편 입자(실측 — 화면 안 개수 · 크기). 돌은 <see cref="Stones"/>.</summary>
    public IReadOnlyList<ParticleSystem> Clouds => _clouds;
    public IReadOnlyList<Transform>      Stones => _stones;
    /// <summary>Buto를 끄는 볼륨이 켜져 있다(실측).</summary>
    public bool    ButoSuppressed => _butoOff != null && _butoOff.isActiveAndEnabled;

    /// <summary>지금 지나는 층 이름(실측 · 로그용).</summary>
    public string LayerName
    {
        get
        {
            if (FallTime < 0f) return "-";
            for (int i = Layers.Length - 1; i >= 0; i--)
                if (FallTime >= Layers[i].At - 1.1f) return Layers[i].Name;
            return Layers[0].Name;
        }
    }

    // ── Lifecycle ─────────────────────────────────────────────────
    private void Update()
    {
        if (_stones == null) return;
        float dt = Time.unscaledDeltaTime;
        for (int i = 0; i < _stones.Length; i++)
            if (_stones[i] != null) _stones[i].Rotate(_stoneAxes[i], _stoneSpeeds[i] * dt, Space.World);
    }

    private void OnDestroy()
    {
        StopSigils(0f);
        foreach (var cast in _casts) cast?.Dispose();
        foreach (var o in _owned)
            if (o != null) Destroy(o);
        _owned.Clear();
    }

    // ── Public Methods ────────────────────────────────────────────
    public static ExileFallStage Create(Vector3 around, Vector3 away)
    {
        var go    = new GameObject("@ExileFall");
        var stage = go.AddComponent<ExileFallStage>();
        stage._top  = new Vector3(around.x, Depth, around.z);
        stage._away = away;
        go.transform.position = stage._top;
        return stage;
    }

    /// <summary>카메라 자리 — 몸 위 비스듬히, 리치 반대편(아레나 낙하 · 추락이 같은 값을 쓴다).</summary>
    public static Vector3 CamTarget(Vector3 body, Vector3 away, float yaw)
        => body + Quaternion.Euler(0f, yaw, 0f) * (-away * CamBack) + Vector3.up * CamHeight;

    public static Vector3 LookPoint(Vector3 body) => body + Vector3.down * LookDrop;

    /// <summary>층 · 봉인진 · 바닥 · 빛을 짓는다. 봉인 칸 목록 · 층계 세트를 읽는다(리치전이면 이미 읽혀 있다).</summary>
    public async UniTask BuildAsync(CancellationToken ct)
    {
        await LichVfx.LoadAsync();
        StairwaySetSO stairs = null;
        var am = Managers.AddressableManager;
        if (am != null) stairs = await am.TryLoadAssetAsync<StairwaySetSO>(StairwaySetSO.Address);
        ct.ThrowIfCancellationRequested();

        // 파편 재질 — 탑 빛줄기 먼지와 같은 가산 입자 재질. 없으면 입자 파편은 건너뛴다(봉인진 · 돌은 그대로).
        // 원본 텍스처는 고리(Circle17)라 파편이 작은 고리로 보였다 → 복제해 모양만 바꾼다(같은 셰이더 · 키워드 = 새 변형 없음).
        if (!DungeonSpaceDirector.TryGetShaftStyle(out _particleMat, out _))
            Debug.LogWarning("[Exile] 탑 먼지 재질 없음 — 입자 파편 없이 봉인진 · 돌만");

        for (int i = 0; i < Layers.Length; i++)
            BuildLayer(i, stairs);
        BuildFloor();
        BuildLight();
    }

    /// <summary>카메라 배경 · 안개를 무대 빛깔로 · 탑 원통을 끈다(몸을 무대로 옮긴 순간).</summary>
    public void TakeView(Transform cam)
    {
        _cam = cam;
        if (_viewTaken) return;
        _viewTaken = true;
        if (cam != null && cam.TryGetComponent(out _camera))
        {
            _clearBefore = _camera.clearFlags;
            _bgBefore    = _camera.backgroundColor;
            _camera.clearFlags = CameraClearFlags.SolidColor;
        }
        _fogBefore        = RenderSettings.fog;
        _fogModeBefore    = RenderSettings.fogMode;
        _fogColorBefore   = RenderSettings.fogColor;
        _fogStartBefore   = RenderSettings.fogStartDistance;
        _fogEndBefore     = RenderSettings.fogEndDistance;
        _fogDensityBefore = RenderSettings.fogDensity;
        // 안개 모드는 씬 그대로 — 바꾸면 새 셰이더 변형이 생겨 에디터가 컴파일하는 동안 청록으로 그린다(10-12 83 실측 · 빌드는 변형이 걸러질 수 있다).
        // 꺼져 있던 씬이면 켜지 않는다(배경색만으로 층을 나눈다).
        if (_fogBefore)
        {
            if (_fogModeBefore == FogMode.Linear)
            {
                RenderSettings.fogStartDistance = FogStart;
                RenderSettings.fogEndDistance   = FogEnd;
            }
            else
            {
                RenderSettings.fogDensity = _fogModeBefore == FogMode.Exponential ? FogDensityExp : FogDensityExp2;
            }
        }
        SuppressButo();

        _shell = GameObject.Find("@DungeonShell");
        if (_shell != null) _shell.SetActive(false);
        ApplyAtmosphere(0f);
    }

    /// <summary>바꾼 화면 설정을 되돌린다 — 착지 와이프가 덮은 뒤 · 전환 없이 끝날 때. 씬이 바뀌는 중엔 부르지 않는다(새 씬 안개를 덮지 않게).</summary>
    public void RestoreView()
    {
        StopSigils(0.2f);
        if (!_viewTaken) return;
        _viewTaken = false;
        if (_camera != null)
        {
            _camera.clearFlags      = _clearBefore;
            _camera.backgroundColor = _bgBefore;
        }
        RenderSettings.fog              = _fogBefore;
        RenderSettings.fogMode          = _fogModeBefore;
        RenderSettings.fogColor         = _fogColorBefore;
        RenderSettings.fogStartDistance = _fogStartBefore;
        RenderSettings.fogEndDistance   = _fogEndBefore;
        RenderSettings.fogDensity       = _fogDensityBefore;
        if (_butoOff != null) Destroy(_butoOff.gameObject);
        _butoOff = null;
        if (_shell != null) _shell.SetActive(true);
    }

    /// <summary>
    /// 떨어진다 — 층이 스쳐 가고 봉인진이 깨지며 대사가 흐른다. 착지 시각이 되면 돌아온다(착지 섬광은 부르는 쪽) · 건너뛸 수 없다(10-26 사용자 「리치의 마지막 연출이 스킵되는데 스킵 안 되게」).
    /// <paramref name="snapCamera"/>면 카메라를 바로 제자리에(가림 뒤), 아니면 넘겨받은 자리에서 서서히 맞춘다(아레나 낙하에서 이어질 때).
    /// </summary>
    internal async UniTask PlayAsync(ExileBody body, bool snapCamera, CancellationToken ct)
    {
        Vector3 camError = Vector3.zero;
        if (_cam != null && !snapCamera) camError = _cam.position - CamTarget(body.Position, _away, 0f);
        float yaw = 0f;
        bool  shadow = false, line1 = false, line2 = false;
        int   next = 0;
        FallTime = 0f;

        while (FallTime < LandAt)
        {
            float dt = Time.unscaledDeltaTime;
            FallTime += dt;
            float   t   = FallTime;
            Vector3 pos = _top + Vector3.down * (FallSpeed * t);
            body.MoveTo(pos);
            body.Spin(SpinDegPerSec * dt);
            if (_light != null) _light.transform.position = pos + Vector3.up * 2.5f;

            foreach (var cast in _casts) cast?.Tick(t);
            while (next < Layers.Length && t >= Layers[next].At) BreakLayer(next++, pos);
            if (!shadow && t >= ShadowAt) { shadow = true; ExileFallSequence.SayShadow(); }
            if (!line1  && t >= Line1At)  { line1  = true; ExileFallSequence.Say(1); }
            if (!line2  && t >= Line2At)  { line2  = true; ExileFallSequence.Say(2); }
            ApplyAtmosphere(t);

            if (_cam != null)
            {
                yaw      += CamOrbitDegPerSec * dt;
                camError *= Mathf.Exp(-CamSettle * dt);
                _shake    = Mathf.MoveTowards(_shake, 0f, ShakeDecay * dt);
                float amp = CamJitter + _shake;
                var jitter = new Vector3(Mathf.PerlinNoise(t * 3.1f, 0.3f) - 0.5f,
                                         Mathf.PerlinNoise(0.7f, t * 2.7f) - 0.5f,
                                         Mathf.PerlinNoise(t * 2.3f, 1.9f) - 0.5f) * (2f * amp);
                Vector3 camPos = CamTarget(pos, _away, yaw) + camError + jitter;
                _cam.SetPositionAndRotation(camPos, Quaternion.LookRotation(LookPoint(pos) - camPos, Vector3.up));
            }

            await UniTask.Yield(PlayerLoopTiming.Update, ct);
        }
    }

    // ── Private Methods ───────────────────────────────────────────
    private Vector3 LayerCenter(int i) => _top + Vector3.down * (FallSpeed * Layers[i].At);

    private void BuildLayer(int i, StairwaySetSO stairs)
    {
        var layer = Layers[i];
        Vector3 c = LayerCenter(i);

        // 봉인진 하나 — 그 보스 봉인 색으로 곱한다(팩 원래 빛깔을 그대로 쓰지 않는다).
        // 10-13 그 보스가 봉인된 채 그 위에 있다 — 몸이 지나는 길 앞쪽(그 층을 지날 때 카메라가 보는 쪽) · 조금 아래. 몸이 다가오면 사슬이 끊기고 깨진다.
        _sigilAt[i] = c;
        if (layer.BossId != null && BossStoryScenes.TryGetLook(layer.BossId, out var look))
        {
            // 몸이 지나는 길 옆(층마다 좌우를 바꾼다) · 조금 앞 · 조금 아래 — 몸 바로 아래면 몸이 보스를 가렸다(②f 사진)
            Vector3 ahead = Quaternion.Euler(0f, CamOrbitDegPerSec * layer.At, 0f) * _away;
            Vector3 side  = Vector3.Cross(Vector3.up, ahead) * (i % 2 == 1 ? 1f : -1f);
            float   scale = ExileFallBossCast.ScaleOf(layer.BossId);
            Vector3 feet  = c + side * (look.ChainRadius * scale + CastGap) + ahead * CastAhead + Vector3.down * CastDrop;
            _sigilAt[i]    = feet;
            _sealColors[i] = look.SealColor;
            _sigils[i] = LichVfx.PlayLoopTinted(LichVfxSlot.BossSealSigil, feet + Vector3.up * 0.05f, BossStoryScenes.SigilFlat, look.SigilScale * scale, look.SealColor);
            _casts[i]  = ExileFallBossCast.Plan(layer.BossId, feet, layer.At);
            if (_casts[i] != null) _casts[i].SpawnAsync(transform, Quaternion.LookRotation(-side, Vector3.up), _owned).Forget();   // 길 쪽을 본다 · 늦으면 그 층은 봉인진만
        }

        // 파편 한 종류
        // 화면에서 읽히는 크기(카메라 5~20 m에서 20~60 px)로 — 10-12 83 실측: 0.06~0.3 m 고리는 거의 안 보였다
        Vector3 band = new(BandWidth, BandHeight, BandWidth);
        switch (layer.Debris)
        {
            case Debris.Stone:
                if (!BuildStones(c, stairs))
                    Cloud("Dust", c, band, 60, ShapeMaterial(Shape.Dot), new Color(0.55f, 0.5f, 0.75f, 0.7f), new Color(0.75f, 0.7f, 0.9f, 0.7f), null,
                          new Vector2(0.2f, 0.35f), new Vector2(-0.2f, 0.2f), 0.3f);
                break;
            case Debris.Glass:   // 스테인드글라스 조각 — 붉은 · 금 · 푸른 빛이 섞여 돈다
                Cloud("Glass", c, band, 140, ShapeMaterial(Shape.Shard), default, default,
                      ThreeColors(new Color(1f, 0.32f, 0.25f, 0.95f), new Color(1f, 0.82f, 0.45f, 0.95f), new Color(0.45f, 0.6f, 1f, 0.95f)),
                      new Vector2(0.22f, 0.45f), new Vector2(-0.3f, 0.3f), 0.3f);
                break;
            case Debris.Ember:   // 불씨 — 위로 오른다
                Cloud("Ember", c, band, 160, ShapeMaterial(Shape.Dot), new Color(1f, 0.45f, 0.15f, 1f), new Color(1f, 0.8f, 0.35f, 1f), null,
                      new Vector2(0.14f, 0.30f), new Vector2(1.5f, 3.5f), 0.6f);
                break;
            case Debris.Leaf:    // 나뭇잎 — 흩날린다
                Cloud("Leaf", c, band, 120, ShapeMaterial(Shape.Leaf), new Color(0.38f, 0.7f, 0.28f, 0.9f), new Color(0.65f, 0.85f, 0.35f, 0.9f), null,
                      new Vector2(0.30f, 0.55f), new Vector2(-0.6f, 0.3f), 0.8f);
                break;
        }
    }

    /// <summary>무너진 제단 돌 — 층계 세트 디딤판(Gothic 돌 계단 판)을 앞쪽 반원에 띄워 천천히 굴린다. 세트가 없으면 false.</summary>
    private bool BuildStones(Vector3 c, StairwaySetSO stairs)
    {
        var slabs = stairs != null ? stairs.StepSlabs : null;
        if (slabs == null || slabs.Length == 0) return false;

        var rng   = new System.Random(1011);
        var axis0 = Quaternion.LookRotation(_away, Vector3.up);
        _stones      = new Transform[StoneCount];
        _stoneAxes   = new Vector3[StoneCount];
        _stoneSpeeds = new float[StoneCount];
        for (int k = 0; k < StoneCount; k++)
        {
            var prefab = slabs[k % slabs.Length];
            if (prefab == null) continue;

            float   ang    = StoneAngles[k] + (float)(rng.NextDouble() * 16.0 - 8.0);
            float   radius = 4f + (float)rng.NextDouble() * 4.5f;
            float   h      = ((float)k / (StoneCount - 1) - 0.5f) * BandHeight * 0.8f;
            Vector3 dir    = axis0 * Quaternion.Euler(0f, ang, 0f) * Vector3.forward;
            var rot = Quaternion.Euler((float)rng.NextDouble() * 360f, (float)rng.NextDouble() * 360f, (float)rng.NextDouble() * 360f);
            var go  = Instantiate(prefab, c + dir * radius + Vector3.up * h, rot, transform);
            go.transform.localScale = prefab.transform.localScale * (1.1f + (float)rng.NextDouble() * 0.7f);
            foreach (var col in go.GetComponentsInChildren<Collider>(true)) col.enabled = false;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = ShadowCastingMode.Off;

            _stones[k]      = go.transform;
            _stoneAxes[k]   = new Vector3((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f).normalized;
            _stoneSpeeds[k] = 20f + (float)rng.NextDouble() * 30f;
        }
        return true;
    }

    /// <summary>
    /// 층 파편 구름 — 층 높이만큼의 상자에 한 번 뿌려 두고(월드 공간) 몸이 그 사이로 떨어진다. 입자 크기 모드는 계층(Hierarchy)으로 —
    /// Local이면 맨 위만 줄어드는 함정(10-11 지침). 재질이 없으면 짓지 않는다.
    /// </summary>
    private void Cloud(string name, Vector3 center, Vector3 size, int count, Material mat, Color a, Color b, Gradient palette,
                       Vector2 sizeRange, Vector2 rise, float noise)
    {
        if (mat == null) return;

        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.position = center;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.duration        = 30f;
        main.loop            = false;
        main.playOnAwake     = false;
        main.startLifetime   = 30f;
        main.startSpeed      = 0f;
        main.startSize       = new ParticleSystem.MinMaxCurve(sizeRange.x, sizeRange.y);
        main.startRotation   = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor      = palette != null
            ? new ParticleSystem.MinMaxGradient(palette) { mode = ParticleSystemGradientMode.RandomColor }
            : new ParticleSystem.MinMaxGradient(a, b);
        main.maxParticles    = count;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode     = ParticleSystemScalingMode.Hierarchy;
        main.useUnscaledTime = true;

        var emission = ps.emission;
        emission.enabled = false;   // 한 번에 뿌린다(Emit)

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale     = size;

        var vel = ps.velocityOverLifetime;   // 세 축 모두 같은 모드여야 한다
        vel.enabled = true;
        vel.space   = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(-0.2f, 0.2f);
        vel.y = new ParticleSystem.MinMaxCurve(rise.x, rise.y);
        vel.z = new ParticleSystem.MinMaxCurve(-0.2f, 0.2f);

        var n = ps.noise;
        n.enabled   = noise > 0f;
        n.strength  = noise;
        n.frequency = 0.25f;

        var spin = ps.rotationOverLifetime;
        spin.enabled = true;
        spin.z       = new ParticleSystem.MinMaxCurve(-1.5f, 1.5f);

        var pr = go.GetComponent<ParticleSystemRenderer>();
        pr.sharedMaterial    = mat;
        pr.shadowCastingMode = ShadowCastingMode.Off;
        pr.receiveShadows    = false;

        ps.Play(true);
        ps.Emit(count);
        _clouds.Add(ps);
    }

    private enum Shape { Dot, Shard, Leaf }

    /// <summary>
    /// 파편 모양 재질 — 탑 먼지 재질을 복제해 코드로 만든 모양 텍스처만 바꾼다(같은 셰이더 · 키워드라 새 변형이 생기지 않는다).
    /// 점 = 불씨 · 먼지, 조각 = 마름모(유리), 잎 = 끝이 뾰족한 타원. 무대가 지워질 때 함께 지운다.
    /// </summary>
    private Material ShapeMaterial(Shape shape)
    {
        if (_particleMat == null) return null;
        var tex = new Texture2D(ShapeSize, ShapeSize, TextureFormat.RGBA32, false)
        {
            name = $"ExileShape_{shape}", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear,
        };
        var px   = new Color32[ShapeSize * ShapeSize];
        float h  = (ShapeSize - 1) * 0.5f;
        for (int y = 0; y < ShapeSize; y++)
            for (int x = 0; x < ShapeSize; x++)
            {
                float u = (x - h) / h, v = (y - h) / h;
                float r = shape switch
                {
                    Shape.Shard => Mathf.Abs(u) * 1.6f + Mathf.Abs(v),                   // 길쭉한 마름모
                    Shape.Leaf  => Mathf.Sqrt(u * u * 4f + v * v) + Mathf.Abs(v) * 0.15f, // 끝이 뾰족한 타원
                    _           => Mathf.Sqrt(u * u + v * v),
                };
                float k = Mathf.Clamp01(1f - r);
                k = shape == Shape.Dot ? k * k : Mathf.SmoothStep(0f, 1f, k * 2.2f);   // 점은 부드럽게 · 조각 · 잎은 가장자리를 세운다
                byte c = (byte)Mathf.RoundToInt(k * 255f);
                px[y * ShapeSize + x] = new Color32(c, c, c, c);
            }
        tex.SetPixels32(px);
        tex.Apply(false, true);

        var mat = new Material(_particleMat) { name = $"ExileDebris_{shape}" };
        mat.SetTexture("_BaseMap", tex);
        _owned.Add(tex);
        _owned.Add(mat);
        return mat;
    }

    /// <summary>
    /// Buto 볼류메트릭 안개를 무대 동안 끈다 — 높이 안개라 y −3000에선 최대 밀도로 화면 전체를 챕터 빛깔 한 색으로 덮는다(10-12 83 실측: 네 층이 같은 연보라).
    /// 우선순위 100 전역 볼륨(무대 아래 · 런타임 프로파일). <see cref="RestoreView"/> · 무대 삭제 때 사라진다.
    /// </summary>
    private void SuppressButo()
    {
        if (_butoOff != null) return;
        var go = new GameObject("ButoOff");
        go.transform.SetParent(transform, false);
        _butoOff = go.AddComponent<Volume>();
        _butoOff.isGlobal = true;
        _butoOff.priority = ButoOffPriority;
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        var fog = profile.Add<ButoVolumetricFog>(false);
        fog.mode.overrideState = true;
        fog.mode.value         = VolumetricFogMode.Off;
        _butoOff.sharedProfile = profile;
        _owned.Add(profile);
    }

    private static Gradient ThreeColors(Color a, Color b, Color c)
    {
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(a, 0f), new GradientColorKey(b, 0.5f), new GradientColorKey(c, 1f) },
                  new[] { new GradientAlphaKey(a.a, 0f), new GradientAlphaKey(c.a, 1f) });
        return g;
    }

    /// <summary>숲 바닥 — 맨 아래 어두운 원판(안개 속에서 다가온다). 착지 섬광이 닿기 전에 화면을 덮는다.</summary>
    private void BuildFloor()
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = "Floor";
        if (go.TryGetComponent<Collider>(out var col)) Destroy(col);
        go.transform.SetParent(transform, false);
        go.transform.position   = _top + Vector3.down * (FallSpeed * FloorAt + 0.25f);
        go.transform.localScale = new Vector3(70f, 0.25f, 70f);
        var r = go.GetComponent<MeshRenderer>();
        r.shadowCastingMode = ShadowCastingMode.Off;
        var mpb = new MaterialPropertyBlock();
        mpb.SetColor(BaseColorId, FloorColor);
        r.SetPropertyBlock(mpb);
    }

    /// <summary>몸을 따라가는 빛 하나 — 층 빛깔(차가운 보라 → 촛불 → 불씨 → 숲)로 몸을 물들인다.</summary>
    private void BuildLight()
    {
        var go = new GameObject("FallLight");
        go.transform.SetParent(transform, false);
        go.transform.position = _top + Vector3.up * 2.5f;
        _light = go.AddComponent<Light>();
        _light.type      = LightType.Point;
        _light.range     = LightRange;
        _light.intensity = LightIntensity;
        _light.shadows   = LightShadows.None;
        _light.color     = Layers[0].Light;
    }

    /// <summary>봉인이 깨진다 — 봉인진이 스러지고 그 보스 빛깔 섬광 · 포효(Ch3은 붉은 섬광). 봉인진 없는 층은 이름만 남긴다.</summary>
    private void BreakLayer(int i, Vector3 at)
    {
        var layer = Layers[i];
        ExileFallSequence.Note($"층 {layer.Name}");
        if (layer.BossId == null) return;

        if (_sigils[i] != null)   // 봉인 칸 목록이 없으면 봉인진 없이 소리 · 흔들림만
        {
            LichVfx.Stop(ref _sigils[i], 0.35f);
            LichVfx.PlayTinted(LichVfxSlot.BossSealBurst, _sigilAt[i], Quaternion.identity, BurstScale, _sealColors[i]);   // 보스 발밑에서 깨진다
        }
        ExileFallSequence.Roar(layer.Roar, _casts[i] != null && _casts[i].Ready ? _casts[i].Feet : at);
        if (layer.RedFlash) LichCinematics.Flash(RedFlash, 0.25f, 0.22f);
        _shake = BreakShake;
        ExileFallSequence.Note($"봉인 깨짐 — {layer.BossId}");
    }

    /// <summary>층 빛깔 — 층 한가운데 앞뒤로는 그 층 색을 붙들고, 그 사이에서만 다음 층으로 섞는다.</summary>
    private void ApplyAtmosphere(float t)
    {
        int   i = 0;
        while (i < Layers.Length - 1 && t > Layers[i + 1].At - HoldAround) i++;
        Color fog   = Layers[i].Fog;
        Color light = Layers[i].Light;
        if (i < Layers.Length - 1)
        {
            float from = Layers[i].At + HoldAround;
            float to   = Layers[i + 1].At - HoldAround;
            float k    = to > from ? Mathf.SmoothStep(0f, 1f, (t - from) / (to - from)) : 0f;
            fog   = Color.Lerp(fog,   Layers[i + 1].Fog,   k);
            light = Color.Lerp(light, Layers[i + 1].Light, k);
        }

        // 챕터 안개 색(어둡다)을 허공 배경으로 쓸 때 밝기만 올린다 — 먼 파편 · 봉인진이 같은 색 공기로 녹아든다
        fog = new Color(fog.r * VoidLift, fog.g * VoidLift, fog.b * VoidLift, 1f);
        if (_viewTaken)
        {
            if (_camera != null) _camera.backgroundColor = fog;
            RenderSettings.fogColor = fog;
        }
        if (_light != null) _light.color = light;
    }

    private void StopSigils(float fade)
    {
        for (int i = 0; i < _sigils.Length; i++)
            if (_sigils[i] != null) LichVfx.Stop(ref _sigils[i], fade);
    }
}
