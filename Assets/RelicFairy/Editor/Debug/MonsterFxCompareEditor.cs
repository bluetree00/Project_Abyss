#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

/// <summary>
/// 10-12 몬스터 이펙트 교체 후보 비교 — 몬스터 전수조사 「교체 8칸」(기획/RelicFairy_몬스터전수조사_이펙트·판정예고_20261012.md §2-9).
/// 테스트 허브 → 「10-11 대기방 경계 띠 실측」으로 보스 대기방(적 없음)에 선 뒤 누른다.
/// 칸마다 한 장(7장 — 골렘 Smash · Slam은 같은 이펙트라 한 장): 같은 게임 카메라 · 같은 자리에서 현재 / 후보 a / 후보 b를 하나씩 재생해
/// 각자의 정점에 찍고 가로로 이어 붙인다(왼쪽 = 현재). 넓은 이펙트끼리 겹치지 않고, 셋이 각자 가장 보이는 순간에 찍힌다.
/// 정점 = 0.1초 간격으로 잰 「보이는 양」(살아 있는 입자 알파 × 크기²의 합)이 가장 큰 순간 → 다시 재생해 그 순간에 찍는다(RunFxToneProbeEditor 방식).
/// 띄우는 길은 몬스터 실제 재생 코드와 같다 — 시전(PatternAttackOverrideSO.SpawnPatternVfx) · 특수 상태(SpecialStateDataBase.SpawnVFX) ·
/// 포자(MushroomAngrySporeBlastState) · 직선 빔(SpawnProceduralBeamVfx). 그 배율 · 위치 · 회전 · 수명을 쓰고, 굴절층 · 점광원은 그 코드처럼
/// 건드리지 않는다(켜진 수를 적는다). 입자 크기 모드만 Hierarchy로 강제한다(몬스터 맞힘 이펙트 Local 함정).
/// 바닥에 판정 외곽선(옅은 회색) · 몬스터 자리에 1 m 막대를 둔다 — 세 칸 모두 같아 크기 자가 된다.
/// 결과: Temp/ui_shots/MONFX_*.png · Temp/monfx_compare.txt · 로그 「[MonFxCompare] 끝」. 플레이 중 Assets/에 쓰지 않는다.
/// 10-12 2차(새 후보 메뉴 — 4 · 6 · 7만 4장): 1차 후보가 판정 영역을 못 채워(구름 · 불이 한 점에서 나옴) 「바닥을 고르게 채우는」 구조의
/// 후보를 더했다(Shape 반경 · 각도 · 방출로 미리 고름). 사본 손질(끌 층 · 점광원 · 부채꼴 각 · 면 채움 · 이동 스크립트)은 렌더만 바꾼다.
/// 결과: Temp/ui_shots/MONFX_N*.png · Temp/monfx_compare_new.txt.
/// </summary>
public static class MonsterFxCompareEditor
{
    // ── Constants ─────────────────────────────────────────────────
    private const string Tag        = "[MonFxCompare]";
    private const string ShotDir    = "Temp/ui_shots";
    private const string ReportPath = "Temp/monfx_compare.txt";
    private const string ReportNew  = "Temp/monfx_compare_new.txt";
    private const string ReportBreath = "Temp/monfx_compare_breath.txt";
    private const string ReportBreathCopy = "Temp/monfx_compare_breath_copy.txt";
    private const string ReportPrick = "Temp/monfx_compare_prick.txt";
    private const int    W = 1920, H = 1080;     // 한 번 그리는 크기 — 여기서 같은 자리를 잘라 붙인다
    private const int    Sep        = 6;         // 칸 사이 띠(px)
    private const int    MinPanel   = 360;       // 자른 칸의 최소 가로 · 세로(px)
    private const float  Step       = 0.1f;      // 정점 찾기 간격(초)
    private const float  MaxWindow  = 3f;        // 정점을 찾는 최대 구간(초) — 게임 수명이 더 짧으면 수명까지
    private const float  NearGap    = 1f;        // 플레이어 발밑 → 판정 뒤쪽 가장자리(m)
    private const float  AlphaFloor = 0.2f;      // 이보다 옅은 입자는 액자(범위)에 넣지 않는다
    private const float  EdgeK      = 0.35f;     // 도달 = 수평 거리 + 크기 × 이 값(부드러운 가장자리)
    private const float  ReachPct   = 0.9f;      // 도달 = 보이는 양 90%가 들어가는 수평 반경
    private const float  MaxPull    = 30f;       // 카메라를 뒤로 빼는 한도(m)
    private const float  LineWidth  = 0.03f;     // 판정 외곽선 굵기(m)

    private const string Mon = "Assets/RelicFairy/Characters/Monster/Monster/";
    private const string Fx  = "Assets/RelicFairy/_Imported/EffectSource/";
    private const string Mp  = Fx + "Magic Pig Games (Infinity PBR)/Shared Files/Magic Spells & Particles/";

    private const string Prick2Bee   = Mon + "BattleBee/Effect/Prick 2.prefab";
    private const string Prick2Reduced = Mon + "BattleBee/Effect/Prick 2_Reduced.prefab";   // 10-12 P1 — 층 8개 끈 사본(4b)
    private const string Slash12Skel = Mon + "Skeleton/Effect/Sword Slash 12.prefab";
    private const string Slash7      = Fx + "Hovl Studio 2/Sword slash VFX/Prefabs/Sword Slash 7.prefab";
    private const string SpikesGolem = Mon + "Golem/Effect/Spikes attack.prefab";
    private const string Shattered   = Mp + "Particle Components/PComponent ShatteredGround.prefab";
    private const string LightningBh = Mon + "Beholder/Animator/LightningExplosionHit.prefab";
    private const string FirePlant   = Mon + "MonsterPlant/Effect/SmallFireBulletHit.prefab";
    private const string FireRay     = Mon + "StingRay/Effect/SmallFireBulletHit.prefab";
    private const string FireSala    = Mon + "Salamander/Effect/SmallFireBulletHit.prefab";
    private const string ToxicCloud  = Mp + "Magic Spells/Magic Toxic Cloud.prefab";
    private const string PoisonSmoke = Fx + "SpecialSkillsEffectsPack/AllEffects/EffectsSet_1(NotScriptBased)/Effects/Effect_46_PoisonSmoke/Effect_46_PoisonSmoke.prefab";
    private const string ElecField   = Mp + "Particle Components/PComponent Electric Field Circular.prefab";
    private const string ElecBolts   = Mp + "Particle Components/PComponent Electric Bolts.prefab";
    private const string FireBreath  = Mp + "Magic Spells/Magic Fire Breath.prefab";
    private const string FlameFwd    = Mp + "Particle Components/PComponent Flame Forward.prefab";
    private const string Slime12     = Fx + "Hovl Studio/AAA Projectiles Vol 1/Prefabs/Flash 12 slime.prefab";

    // 2차 새 후보(10-12) — 사용처는 데모 씬 · 변형 사슬뿐(플레이어 · 레전더리 · 보스 0)
    private const string Sp          = Fx + "Spells Pack/LWRP(URP)/Particles_LWRP/Prefabs/";
    private const string Nature4Grn  = Sp + "Variations/Spells/Nature/Spell_Nature_4_Green_LWRP Variant Variant.prefab";
    private const string Nature4Ylw  = Sp + "Variations/Spells/Nature/Spell_Nature_4_Yellow_LWRP Variant Variant.prefab";
    private const string Nature5     = Sp + "Spells/Spell_Nature_5_LWRP.prefab";
    private const string LeavesBuff  = Fx + "Hovl Studio/AOE Magic spells Vol.1/Prefabs/Leaves buff.prefab";
    private const string FlameTsuna  = Fx + "SpecialSkillsEffectsPack/AllEffects/EffectsSet_1(NotScriptBased)/Effects/Effect_51_FlameTsunami/Effect_51_FlameTsunami.prefab";

    // 3차 브레스 후보(10-13) — 화룡이 Style 2 · Effect_19_FlameBreath를 쓰므로 Style 1 · 3만(겹치지 않게)
    private const string Flame1 = Fx + "Luke Peek/Realistic Fire/Prefabs/Style 1/Style 1 - Flamethrower.prefab";
    private const string Flame3 = Fx + "Luke Peek/Realistic Fire/Prefabs/Style 3/Style 3 - Flamethrower.prefab";
    // 4b 사본(10-13 ④) — Style 3 ×5 · 입자 크기 모드 Hierarchy로 구움
    private const string Flame3Copy = Mon + "Salamander/Effect/Style 3 - Flamethrower_Breath.prefab";

    // 줄인 판에서 끌 층(렌더러 · 광원만) — 문서 §2-9 · 「@root」 = 루트 자신의 렌더러
    private const string Slash12Mute = "Distortion(Delete for Mobile),Hit,Flash,Flash1,Flash3,InkSparks,Smoke";
    private const string SpikesMute  = "Fire,Distortion,ShockWave2,SmokeDark";
    private const string Nature4Mute = "@root,Point Light,Glow,Expansive Wave 2,Trails";          // 버섯 메시 · 점광원 · 15 m 빛무리 · 10 m 고리 · 줄기 → 구름 + 알갱이
    private const string Nature4Only = "@root,Point Light,Glow,Expansive Wave 2,Trails,Spore";    // 구름만
    private const string Nature5Mute = "Point Light";
    private const string LeavesMute  = "@root,EndShoot,EndSWave,EndFlash,EndTrail,BTrail,Trails"; // 섬광 · 고리 · 줄기 → 연무 + 잎
    private const string FlameMute2  = "Effect_51_Particle";                                     // 불티 1500/초
    private const string FlameMute1  = "Effect_51_Particle,Effect_51_FlameField";                // 빌보드 불길 1층만

    private static readonly Color OutlineInk = new(0.85f, 0.85f, 0.85f, 0.6f);
    private static readonly Color PoleInk    = new(0.85f, 0.85f, 0.85f, 1f);
    private static readonly Color SepInk     = new(0.16f, 0.16f, 0.16f, 1f);
    private static readonly Color BeamPurple = new(0.70f, 0.40f, 1.00f, 0.95f);   // 문서 §2-9 「보라」(값은 이 도구가 정함)
    private static readonly Color BeamData   = new(1.00f, 0.25f, 0.20f, 0.98f);   // BeholderPatternAttackOverride Line3 proceduralBeamColor

    // ── Static ────────────────────────────────────────────────────
    private static CancellationTokenSource s_cts;
    private static ParticleSystem.Particle[] s_buf = new ParticleSystem.Particle[512];
    private static readonly List<Vector2> s_weights = new();   // (도달 거리, 보이는 양) — 도달 백분위용

    private enum Route { Cast, Special, Spore, Beam }
    private enum Judge { Cells, Circle, Sector }

    /// <summary>시전 경로 값 — 패턴 SO의 castVfx* 그대로(회전 오프셋은 세 칸 모두 0).</summary>
    private sealed class CastSpec
    {
        public float fwd, right, up, life, playerAhead;
        public bool  attach, atPlayer;
    }

    /// <summary>한 칸(현재 또는 후보) — 위는 정의, 아래는 실행 결과.</summary>
    private sealed class Cand
    {
        public string label, path, mute, note;
        public Route  route;
        public float  scale = 1f, fit, beamLen, beamWidth;
        public Color  beamColor;
        public bool   faceRot;
        public float  winFrom, winTo;   // 정점을 찾는 구간(초) — 0이면 처음부터 · 수명까지
        public float  arc;              // >0이면 사본 손질: 모양 호를 이 각으로 줄이되 가운데는 그대로(부채꼴)
        public bool   fill;             // 사본 손질: 원 · 호의 가장자리만 → 면 전체에서 나오게(radiusThickness 1)
        public bool   strip;            // 사본 손질: 스크립트 끔(이동 · 소리 등)

        public bool   loaded;
        public float  used, life, window, peakAt, peakVisible, reachMax, fitReach1;
        public Bounds peakBounds;
        public bool   hasBounds;
    }

    private sealed class Shot
    {
        public string       name, title, judgeNote;
        public CastSpec     cast;
        public Judge        judge;
        public Vector2Int[] cells;
        public float        radius, angle;
        public Cand[]       cands;
    }

    /// <summary>찍는 동안 고정하는 카메라 자세 · 플레이어 자리.</summary>
    private sealed class Ctx
    {
        public Camera     cam;
        public Vector3    camPos, feet, face;
        public Quaternion camRot;
        public float      floorY;
    }

    /// <summary>한 번 띄운 이펙트 — 게임 수명 · 원점(도달을 재는 곳) · 이 도구가 만든 재질.</summary>
    private sealed class Spawned
    {
        public GameObject go;
        public Vector3    origin;
        public float      life;
        public Material   mat;
    }

    private struct Pass
    {
        public float  peakAt, peakVisible, reachMax, life, window;
        public Bounds peakBounds;
        public bool   hasBounds;
    }

    private struct Meas
    {
        public float  visible, reach, maxLight;
        public int    particles, systems, lights, refraction;
        public Bounds bounds;
        public bool   any;
    }

    // ── Public Methods ────────────────────────────────────────────
    [MenuItem("RelicFairy/Debug/10-12 몬스터 이펙트 교체 후보 비교 (테스트 허브, 플레이 중)")]
    private static void Run() => Launch(BuildShots(), ReportPath, "몬스터 이펙트 교체 후보 비교");

    [MenuItem("RelicFairy/Debug/10-12 몬스터 이펙트 새 후보 — 독 분사 · 브레스 · 포자 (테스트 허브, 플레이 중)")]
    private static void RunNew() => Launch(BuildShotsNew(), ReportNew, "몬스터 이펙트 새 후보(4 · 6 · 7) 비교");

    [MenuItem("RelicFairy/Debug/10-13 살라만더 브레스 후보 — 화염방사 Style 1 · 3 (테스트 허브, 플레이 중)")]
    private static void RunBreath() => Launch(BuildShotsBreath(), ReportBreath, "살라만더 브레스 3차 후보(화염방사 Style 1 · 3)");

    /// <summary>
    /// 3차 브레스 후보 2장(10-13) — 2차(불길 해일 · Magic Pig)는 검게 뭉개지거나 옅었다. Luke Peek 화염방사 Style 1 · 3을
    /// 판정(앞 60° · 4 m)에 맞춰 몬스터 정면으로 띄운다. 둘째 장은 정점을 0.5~1.0초 안에서만 찾은 판(브레스는 이른 정점이 실제 피해 순간).
    /// </summary>
    private static List<Shot> BuildShotsBreath()
    {
        Shot Make(string name, string title, float from, float to) => new Shot
        {
            name = name, judge = Judge.Sector, radius = 4f, angle = 60f, judgeNote = "앞 60° · 4 m", title = title,
            cands = new[]
            {
                new Cand { label = "왼쪽 현재", path = FireSala, route = Route.Special, scale = 0.5f, winFrom = from, winTo = to, note = "불꽃 탄 맞힘 한 번(월드 고정 회전)" },
                new Cand { label = "Style 1", path = Flame1, route = Route.Special, fit = 4f, faceRot = true, winFrom = from, winTo = to,
                           note = "Luke Peek 화염방사 Style 1 — 판정 4 m에 맞춤 · 몬스터 정면" },
                new Cand { label = "Style 3", path = Flame3, route = Route.Special, fit = 4f, faceRot = true, winFrom = from, winTo = to,
                           note = "Luke Peek 화염방사 Style 3 — 판정 4 m에 맞춤 · 몬스터 정면" },
            },
        };
        return new List<Shot>
        {
            Make("MONFX_N6B_breath_flamethrower", "살라만더 브레스 3차 — 화염방사 Style 1 · 3(정점 = 수명 전체에서)", 0f, 0f),
            Make("MONFX_N6BE_breath_flamethrower_early", "살라만더 브레스 3차 — 정점을 0.5~1.0초 안에서만", 0.5f, 1f),
        };
    }

    [MenuItem("RelicFairy/Debug/10-13 살라만더 브레스 사본 확인 — 4b 구움 (테스트 허브, 플레이 중)")]
    private static void RunBreathCopy() => Launch(BuildShotsBreathCopy(), ReportBreathCopy, "살라만더 브레스 사본 확인(4b ④ 구움)");

    /// <summary>
    /// 4b 사본 확인 1장(10-13 4b 요청) — 왼쪽 = 83 사진 기준(원본 Style 3 · 판정 4 m에 맞춤), 가운데 = 사본을 배율 1로(게임이 띄우는 그대로),
    /// 오른쪽 = 사본을 4 m에 맞춤(보고서 「맞춤 배율」이 사본에 더 곱해야 할 값). 정점 0.5~1.0초(브레스 피해 순간).
    /// </summary>
    private static List<Shot> BuildShotsBreathCopy() => new List<Shot>
    {
        new Shot
        {
            name = "MONFX_N6C_breath_copy", judge = Judge.Sector, radius = 4f, angle = 60f, judgeNote = "앞 60° · 4 m",
            title = "살라만더 브레스 — 4b 사본(Style 3 ×5 · Hierarchy 구움) 확인 · 정점 0.5~1.0초",
            cands = new[]
            {
                new Cand { label = "83 기준", path = Flame3, route = Route.Special, fit = 4f, faceRot = true, winFrom = 0.5f, winTo = 1f,
                           note = "원본 Style 3 — 판정 4 m에 맞춤(83 10-13 사진 기준)" },
                new Cand { label = "사본 ×1", path = Flame3Copy, route = Route.Special, scale = 1f, faceRot = true, winFrom = 0.5f, winTo = 1f,
                           note = "4b 사본 그대로(게임 배율 1)" },
                new Cand { label = "사본 맞춤", path = Flame3Copy, route = Route.Special, fit = 4f, faceRot = true, winFrom = 0.5f, winTo = 1f,
                           note = "4b 사본 — 판정 4 m에 맞춤(맞춤 배율 = 사본에 더 곱할 값)" },
            },
        },
    };

    [MenuItem("RelicFairy/Debug/10-13 벌 찌르기 Prick 2_Reduced 앞 위치 비교 — 4b (테스트 허브, 플레이 중)")]
    private static void RunPrick() => Launch(BuildShotsPrick(), ReportPrick, "벌 찌르기 Prick 2_Reduced — 앞 위치 0.4 · 1.75 비교(4b 요청)");

    /// <summary>
    /// 4b 요청(10-13) — 배율 훑기(앞 0.4 · ×0.9 → 앞 0.9 m)와 앞 1.75로 옮긴 본 측정(4.4 m)이 어긋났다. 판정 Front2(앞 2.5 m · 옆 ±0.5) 안에 그림이 드는지 본다.
    /// 시전 경로 값은 전투벌 DashSting 그대로(위 0.8 · 몬스터에 붙음 · 수명 0.6 · 회전 0) — 앞 위치만 바꾼다(앞 위치는 장마다라 둘로 나눈다).
    /// </summary>
    private static List<Shot> BuildShotsPrick()
    {
        Shot Make(string name, float fwd, string title, params Cand[] cands) => new Shot
        {
            name = name, cast = new CastSpec { fwd = fwd, up = 0.8f, attach = true, life = 0.6f },
            judge = Judge.Cells, cells = new[] { new Vector2Int(0, 1), new Vector2Int(0, 2) }, judgeNote = "Front2 — 앞 2.5 m · 옆 ±0.5",
            title = title, cands = cands,
        };
        return new List<Shot>
        {
            Make("MONFX_N8A_prick_fwd04", 0.4f, "벌 찌르기 ① 지금 — Prick 2_Reduced ×0.9 · 앞 0.4",
                 new Cand { label = "① 앞 0.4", path = Prick2Reduced, route = Route.Cast, scale = 0.9f, note = "지금 값(DashSting)" }),
            Make("MONFX_N8B_prick_fwd175", 1.75f, "벌 찌르기 ② · ③ — Prick 2_Reduced ×0.9 · 앞 1.75(③ = 정점을 0.3초 안에서만)",
                 new Cand { label = "② 앞 1.75", path = Prick2Reduced, route = Route.Cast, scale = 0.9f, note = "앞 위치만 1.75" },
                 new Cand { label = "③ 앞 1.75 · 0.3초", path = Prick2Reduced, route = Route.Cast, scale = 0.9f, winFrom = 0f, winTo = 0.3f,
                            note = "정점을 0~0.3초 안에서만(찌르기 피해 순간)" }),
        };
    }

    // ── Private Methods ───────────────────────────────────────────
    private static void Launch(List<Shot> shots, string reportPath, string title)
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning(Tag + " 플레이 모드에서(테스트 허브 → 대기방 · 적 없는 방)"); return; }
        s_cts?.Cancel();
        s_cts?.Dispose();
        s_cts = new CancellationTokenSource();
        RunAsync(shots, reportPath, title, s_cts.Token).Forget();
    }

    private static async UniTaskVoid RunAsync(List<Shot> shots, string reportPath, string title, CancellationToken ct)
    {
        Directory.CreateDirectory(ShotDir);
        var sb = new StringBuilder($"{title} {DateTime.Now:MM-dd HH:mm:ss}\n");
        var cam = Camera.main;
        var player = GameRunBootstrapper.Instance?.Run?.Player;
        if (cam == null || player == null)
        {
            sb.AppendLine($"카메라 {cam != null} · 런 플레이어 {player != null} — 테스트 허브 → 「10-11 대기방 경계 띠 실측」으로 대기방에 선 뒤 누를 것");
            Finish(sb, reportPath);
            return;
        }

        var rt = new RenderTexture(W, H, 24);
        try
        {
            player.SetInvincible(300f);
            player.SetInputEnabled(false);
            await UniTask.Delay(TimeSpan.FromSeconds(0.6f), DelayType.Realtime, cancellationToken: ct);   // 멈춘 뒤 카메라가 자리 잡게

            Vector3 feet = player.transform.position;
            Vector3 face = cam.transform.forward; face.y = 0f;
            face = face.sqrMagnitude > 0.01f ? face.normalized : Vector3.forward;
            var ctx = new Ctx { cam = cam, camPos = cam.transform.position, camRot = cam.transform.rotation, feet = feet, face = face, floorY = feet.y };

            sb.AppendLine($"플레이어 {feet} · 카메라 {ctx.camPos}(찍는 동안 이 자세로 고정) · 몬스터 방향 = 카메라 앞(월드 +Z와 {Vector3.Angle(face, Vector3.forward):0}°) · 배속 {Time.timeScale:0.##}");
            sb.AppendLine("한 장 = 같은 카메라 · 같은 자리에서 칸마다 따로 재생해 각자의 정점에 찍고 가로로 붙임(왼쪽부터 아래 [ ] 줄 순서). 바닥 옅은 회색 선 = 판정 · 흰 막대 = 몬스터 자리 1 m");
            sb.AppendLine("띄우는 길 = 몬스터 실제 재생 코드(디스크 판) 그대로 — 굴절층 · 점광원은 그 코드처럼 안 건드림(「굴절층 켜짐」 = 게임에서도 켜진 수). 입자 크기 모드만 Hierarchy 강제");
            sb.AppendLine("보이는 양 = Σ 살아 있는 입자 알파 × (크기 m)² · 도달(90%) = 보이는 양 90%가 들어가는 수평 반경(입자 가장자리 = 크기 × 0.35) · 맞춤 배율 = 판정 크기 ÷ 배율 1에서 잰 도달");
            sb.AppendLine("줄인 판(끈 층)은 렌더러 · 광원만 끔 — 시전 경로 ForcePlayVfx가 SetActive(false)로 꺼 둔 자식을 다시 켜므로, 프리팹에서 줄일 땐 층을 지우거나 ParticleSystemRenderer를 끈다");
            sb.AppendLine("사본 손질(부채꼴 각 · 면 채움 · 스크립트 끔)은 실제로는 프리팹 사본에서 할 일 — 도구는 띄운 직후 같은 값을 넣어 그 모습을 보인다");
            sb.AppendLine();

            foreach (var shot in shots)
            {
                ct.ThrowIfCancellationRequested();
                await ShootAsync(shot, ctx, rt, sb, ct);
            }
        }
        catch (OperationCanceledException) { sb.AppendLine("취소됨"); }
        catch (Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            if (player != null) player.SetInputEnabled(true);
            rt.Release();
            Object.Destroy(rt);
        }
        Finish(sb, reportPath);
    }

    private static void Finish(StringBuilder sb, string reportPath)
    {
        File.WriteAllText(reportPath, sb.ToString());
        Debug.Log(Tag + " 끝\n" + sb);
    }

    /// <summary>찍을 장면 7장 — 문서 §2-1 · §2-4 · §2-9 「교체」 8칸(골렘 Smash · Slam은 한 장).</summary>
    private static List<Shot> BuildShots()
    {
        // 패턴 SO 값 그대로(RatAssassin · Golem · BeholderPatternAttackOverride.asset)
        var rat    = new CastSpec { fwd = 0.4f, right = 0.2f, up = 0.8f, attach = true, life = 0.6f };
        var golem  = new CastSpec { fwd = 0.4f, up = 0f, attach = true, life = 0.8f };
        var behold = new CastSpec { fwd = 0.55f, up = 1.15f, attach = true, atPlayer = true, playerAhead = 2f, life = 0.9f };

        return new List<Shot>
        {
            new Shot
            {
                name = "MONFX_1_rat_slash", cast = rat, judge = Judge.Cells, cells = new[] { new Vector2Int(0, 1) },
                title = "쥐 암살자 Slash(단검 베기 · 교체 1칸) — 시전 경로 · 앞 0.4 · 오른쪽 0.2 · 높이 0.8 · 몬스터에 붙음 · 수명 0.6초",
                judgeNote = "앞 1칸",
                cands = new[]
                {
                    new Cand { label = "왼쪽 현재", path = Prick2Bee, route = Route.Cast, scale = 0.9f, note = "찌르기 이펙트(Prick 2)" },
                    new Cand { label = "가운데 후보 a", path = Slash12Skel, route = Route.Cast, scale = 0.6f, mute = Slash12Mute,
                               note = "문서 a · P1 ×0.6 — 6종이 쓰는 베기의 줄인 판" },
                    new Cand { label = "오른쪽 후보 b", path = Slash7, route = Route.Cast, scale = 0.6f,
                               note = "문서 b 「Sword Slash 1~9 중 얇은 것」 → 층이 가장 적은 7(섬광 · 연기 없음 · 굴절 1층은 그대로) · 배율은 a와 같게 · 사용처 0(데모 씬만)" },
                },
            },
            new Shot
            {
                name = "MONFX_2_golem_smash_slam", cast = golem, judge = Judge.Cells,
                cells = new[] { new Vector2Int(-1, 1), new Vector2Int(0, 1), new Vector2Int(1, 1) },
                title = "골렘 Smash · Slam(주먹 내려찍기 · 교체 2칸 — 같은 이펙트라 한 장) — 시전 경로 · 앞 0.4 · 높이 0 · 몬스터에 붙음 · 수명 0.8초",
                judgeNote = "가운데 칸 = Smash(앞 1칸) · 세 칸 = Slam(가로 3칸)",
                cands = new[]
                {
                    new Cand { label = "왼쪽 현재", path = SpikesGolem, route = Route.Cast, scale = 1.2f, note = "땅 가시 폭발(숲의 수호자 뿌리와 같은 그림)" },
                    new Cand { label = "가운데 후보 a", path = Shattered, route = Route.Cast, fit = 1.5f,
                               note = "문서 a — 판정 가로 3 m에 맞춤 · 원래 반복 3초는 시전 경로가 끔 · 수명은 지금 데이터 0.8초(문서 「1초」는 데이터 몫) · 사용처 0" },
                    new Cand { label = "오른쪽 후보 b", path = SpikesGolem, route = Route.Cast, scale = 0.6f, mute = SpikesMute,
                               note = "문서 b — 불 · 굴절 · 충격파2 · 어두운 연기 끔" },
                },
            },
            new Shot
            {
                name = "MONFX_3_beholder_line3", cast = behold, judge = Judge.Cells,
                cells = new[] { new Vector2Int(0, 1), new Vector2Int(0, 2) },
                title = "비홀더 Laser1_Line3(눈 레이저 직선 · 교체 1칸) — 지금은 플레이어 자리(+높이 1.15)에 터짐 · 후보는 코드 내장 직선(눈 앞 0.55 · 높이 1.15 · 수명 0.9초)",
                judgeNote = "앞 2칸",
                cands = new[]
                {
                    new Cand { label = "왼쪽 현재", path = LightningBh, route = Route.Cast, scale = 0.1f,
                               note = "castVfxAtPlayerPosition — 플레이어 자리는 판정 먼 칸 가운데(앞 2 m)로 둠" },
                    new Cand { label = "가운데 후보 a", route = Route.Beam, beamLen = 2.5f, beamWidth = 0.16f, beamColor = BeamPurple,
                               note = "문서 §2-9 · P1 — useProceduralBeamVfx · 길이 2.5(= 판정 칸) · 굵기 0.16 · 보라(0.70/0.40/1.00 · 값은 도구가 정함)" },
                    new Cand { label = "오른쪽 후보 b", route = Route.Beam, beamLen = 2.5f, beamWidth = 0.2f, beamColor = BeamData,
                               note = "켜기만 했을 때 — 데이터에 남은 굵기 0.2 · 빨강(1/0.25/0.2) · 길이만 2.5" },
                },
            },
            new Shot
            {
                name = "MONFX_4_plant_spray", judge = Judge.Circle, radius = 3.5f,
                title = "식충식물 독 분사(특수 상태 · 교체 1칸) — SpecialStateDataBase.SpawnVFX 경로(발밑 · 회전 월드 고정 · 배율 = one × 값 · 수명 = 길이 + 수명 + 0.3초)",
                judgeNote = "둘레 3.5 m",
                cands = new[]
                {
                    new Cand { label = "왼쪽 현재", path = FirePlant, route = Route.Special, scale = 0.5f, note = "불꽃 탄 맞힘(SmallFireBulletHit)" },
                    new Cand { label = "가운데 후보 a", path = ToxicCloud, route = Route.Special, fit = 3.5f, note = "문서 a — 판정 반경에 맞춤 · 사용처 0(데모 씬만)" },
                    new Cand { label = "오른쪽 후보 b", path = PoisonSmoke, route = Route.Special, fit = 3.5f,
                               note = "문서 b — ⚠ 레전더리 아이템 목록(LegendaryVfxCatalog) · 리치(LichVfxSet)가 같은 프리팹을 씀(10-12 확인) → 문서 원칙(플레이어 쪽 자원 금지)에 걸림" },
                },
            },
            new Shot
            {
                name = "MONFX_5_stingray_discharge", judge = Judge.Circle, radius = 3.5f,
                title = "가오리 방전(특수 상태 · 교체 1칸) — SpawnVFX 경로(위와 같음)",
                judgeNote = "둘레 3.5 m",
                cands = new[]
                {
                    new Cand { label = "왼쪽 현재", path = FireRay, route = Route.Special, scale = 0.5f, note = "불꽃 탄 맞힘(SmallFireBulletHit)" },
                    new Cand { label = "가운데 후보 a", path = ElecField, route = Route.Special, fit = 3.5f, note = "문서 a — 판정 반경에 맞춤 · 사용처 0" },
                    new Cand { label = "오른쪽 후보 b", path = ElecBolts, route = Route.Special, fit = 3.5f,
                               note = "문서 밖 — 이 도구가 더함(같은 팩 · 사용처 0 확인). 문서 b · c는 룬 번개 · 화룡 보스 공유라 뺐다" },
                },
            },
            new Shot
            {
                name = "MONFX_6_salamander_breath", judge = Judge.Sector, radius = 4f, angle = 60f,
                title = "살라만더 화염 브레스(특수 상태 · 교체 1칸) — SpawnVFX 경로 · 판정은 2초 동안 0.4초마다",
                judgeNote = "앞 60° · 4 m",
                cands = new[]
                {
                    new Cand { label = "왼쪽 현재", path = FireSala, route = Route.Special, scale = 0.5f, note = "불꽃 탄 맞힘 한 번(월드 고정 회전)" },
                    new Cand { label = "가운데 후보 a", path = FireBreath, route = Route.Special, fit = 4f, faceRot = true,
                               note = "문서 a — 4 m에 맞춤 · 몬스터 방향으로 돌려 띄움(P3 의도 — 지금 코드는 월드 고정) · 사용처 0(데모 씬만)" },
                    new Cand { label = "오른쪽 후보 b", path = FlameFwd, route = Route.Special, fit = 4f, faceRot = true,
                               note = "문서 b — 4 m에 맞춤 · 몬스터 방향 · 사용처 0" },
                },
            },
            new Shot
            {
                name = "MONFX_7_mushroom_spore", judge = Judge.Circle, radius = 4f,
                title = "성난 버섯 포자 폭발(특수 상태 · 교체 1칸) — MushroomAngrySporeBlastState 경로(발밑 · 회전 월드 고정 · 배율 = one × 값 · 수명 = 길이 + 0.5초)",
                judgeNote = "둘레 4 m",
                cands = new[]
                {
                    new Cand { label = "왼쪽 현재", path = Slime12, route = Route.Spore, scale = 1f, note = "sporeEffectPrefab(Flash 12 slime)" },
                    new Cand { label = "가운데 후보 a", path = ToxicCloud, route = Route.Spore, fit = 4f,
                               note = "문서 a — 판정 반경에 맞춤 · ⚠ 포자 경로는 크기 모드를 안 바꿈 — Magic Pig는 Shape 모드라 게임에선 입자가 안 커진다(코드 한 줄 필요)" },
                    new Cand { label = "오른쪽 후보 c", path = Slime12, route = Route.Spore, fit = 4f, note = "문서 c — 지금 그림을 판정 크기로 키움" },
                },
            },
        };
    }

    /// <summary>
    /// 2차 새 후보 4장 — 1차 4 · 6 · 7칸 후보가 판정 영역을 못 채웠다(Magic Pig는 Shape가 꺼져 한 점에서 나온다).
    /// 고른 기준: 넓은 Shape(원 · 반구 · 호)에서 큰 입자가 오래 머물러 「배율을 맞추면 영역이 찬다」 · URP 셰이더 · 사용처 0(데모 씬만).
    /// </summary>
    private static List<Shot> BuildShotsNew()
    {
        Cand Current(string path, Route route, float scale, string note) => new Cand { label = "왼쪽 현재", path = path, route = route, scale = scale, note = note };

        return new List<Shot>
        {
            new Shot
            {
                name = "MONFX_N4_plant_spray", judge = Judge.Circle, radius = 3.5f, judgeNote = "둘레 3.5 m",
                title = "식충식물 독 분사 — 새 후보(탁하지 않은 녹색 · 바닥 구름) · SpawnVFX 경로",
                cands = new[]
                {
                    Current(FirePlant, Route.Special, 0.5f, "불꽃 탄 맞힘(SmallFireBulletHit)"),
                    new Cand { label = "후보 a", path = Nature4Grn, route = Route.Special, fit = 3.5f, mute = Nature4Mute,
                               note = "Spells Pack 버섯 포자(녹색 판) — 연기 Hemi 반경 1 · 크기 4~6 · 수명 1~3초 · 0.15초부터 15+15+30개 → 배율 1에서 이미 반경 약 3.5 m를 덮는다 · 색 (0.75,1,0)~(0.24,0.91,0.18) · URP Particles/Unlit · 구름 + 알갱이" },
                    new Cand { label = "후보 b", path = Nature4Grn, route = Route.Special, fit = 3.5f, mute = Nature4Only,
                               note = "a에서 알갱이(Spore)까지 끈 구름만 — 더 절제한 판" },
                    new Cand { label = "후보 c", path = LeavesBuff, route = Route.Special, fit = 3.5f, mute = LeavesMute, strip = true,
                               note = "Hovl AOE 잎 버프 — 연무 Hemi 반경 4(납작 0.4) · 크기 2~3 · 녹색 (0.33,1,0.41) 알파 0.1 + 잎 → 반경 4를 옅게 덮는다 · 섬광 · 고리 · 줄기 끔 · 소리 스크립트(없는 스크립트) 끔" },
                },
            },
            new Shot
            {
                name = "MONFX_N6_salamander_breath", judge = Judge.Sector, radius = 4f, angle = 60f, judgeNote = "앞 60° · 4 m",
                title = "살라만더 화염 브레스 — 새 후보(불빛 주황 · 부채꼴 바닥 불길) · SpawnVFX 경로 · 몬스터 방향",
                cands = new[]
                {
                    Current(FireSala, Route.Special, 0.5f, "불꽃 탄 맞힘 한 번(월드 고정 회전)"),
                    new Cand { label = "후보 a", path = FlameTsuna, route = Route.Special, fit = 4f, faceRot = true, arc = 60f, fill = true, strip = true, mute = FlameMute2,
                               note = "SSEP 불길 해일 — 원 반경 12.5 · 호 180°(앞 반원) · 크기 5~10 · 수명 0.5~1초 · 초당 250개(빌보드 + 메시 2층) · 주황 Tint(3.96,0.52,0.05). 사본 손질: 호 60° · 면 채움(원래 가장자리만) · 앞으로 미는 스크립트 끔 · 불티 끔 → 4 m 맞춤" },
                    new Cand { label = "후보 b", path = FlameTsuna, route = Route.Special, fit = 4f, faceRot = true, arc = 60f, fill = true, strip = true, mute = FlameMute1,
                               note = "a에서 메시 불길까지 끈 빌보드 1층 — 더 절제한 판" },
                },
            },
            new Shot
            {
                name = "MONFX_N6T_breath_early", judge = Judge.Sector, radius = 4f, angle = 60f, judgeNote = "앞 60° · 4 m",
                title = "살라만더 브레스 — 정점을 0.5~1.0초 안에서만 찾은 판(1차 후보는 1.8초 끝 무렵에 찍혔다)",
                cands = new[]
                {
                    new Cand { label = "1차 a", path = FireBreath, route = Route.Special, fit = 4f, faceRot = true, winFrom = 0.5f, winTo = 1f,
                               note = "Magic Pig 불 브레스 — Shape 꺼짐(한 점) · 속도 곡선 앞 5 · 옆 ±1 → 좁은 줄기 · 재질은 내장 레거시 셰이더(200 · 203)" },
                    new Cand { label = "1차 b", path = FlameFwd, route = Route.Special, fit = 4f, faceRot = true, winFrom = 0.5f, winTo = 1f,
                               note = "Magic Pig 앞 불길 — Shape 꺼짐 · 1층" },
                    new Cand { label = "새 a", path = FlameTsuna, route = Route.Special, fit = 4f, faceRot = true, arc = 60f, fill = true, strip = true, mute = FlameMute2,
                               winFrom = 0.5f, winTo = 1f, note = "위 장의 후보 a를 같은 구간에서" },
                },
            },
            new Shot
            {
                name = "MONFX_N7_mushroom_spore", judge = Judge.Circle, radius = 4f, judgeNote = "둘레 4 m",
                title = "성난 버섯 포자 폭발 — 새 후보(옅은 노랑 · 연두 가루) · 포자 경로(크기 모드는 실제로 안 바꿈 — Spells Pack은 Local이라 게임에선 자식이 안 커진다)",
                cands = new[]
                {
                    Current(Slime12, Route.Spore, 1f, "sporeEffectPrefab(Flash 12 slime)"),
                    new Cand { label = "후보 a", path = Nature4Ylw, route = Route.Spore, fit = 4f, mute = Nature4Mute,
                               note = "Spells Pack 버섯 포자(노랑 판) — 4a와 같은 구조 · 색 (0.75,0.65,0.27) 계열 옅은 노랑 · 구름 + 알갱이" },
                    new Cand { label = "후보 b", path = Nature4Ylw, route = Route.Spore, fit = 4f, mute = Nature4Only,
                               note = "a의 구름만 — 더 절제한 판" },
                    new Cand { label = "후보 c", path = Nature5, route = Route.Spore, fit = 4f, mute = Nature5Mute,
                               note = "Spells Pack 포자 흩뿌림 — 가운데 포자 빛 + 1.5초 뒤 줄기 15개(초속 10~15)가 퍼져 바닥에 닿으면 포자 터짐 · 색 (0.87,0.74,0.33)/(0.61,0.86,0.16) 옅은 금 · 연두 · ⚠ 터지는 때가 1.5초 넘게 늦다" },
                },
            },
        };
    }

    /// <summary>한 장 — 무대(몬스터 자리 · 막대 · 판정 외곽선) → 칸마다 정점 찾기 → 액자 → 칸마다 정점에 찍어 가로로 붙인다.</summary>
    private static async UniTask ShootAsync(Shot shot, Ctx ctx, RenderTexture rt, StringBuilder sb, CancellationToken ct)
    {
        var props = new List<Object>();
        Texture2D sheet = null;
        try
        {
            float ahead = NearGap + BackExtent(shot);
            Vector3 monPos = ctx.feet + ctx.face * ahead; monPos.y = ctx.floorY;
            var mon = new GameObject("~MonFxCompare_Monster").transform;
            mon.SetPositionAndRotation(monPos, Quaternion.LookRotation(ctx.face, Vector3.up));
            props.Add(mon.gameObject);
            Bounds judge = BuildStage(shot, mon, ctx.floorY, props);

            // 1) 칸마다 정점 찾기(맞춤 배율이면 먼저 배율 1로 도달을 잰다)
            foreach (var c in shot.cands) await SearchAsync(shot, c, mon, ct);

            // 2) 액자 — 판정 · 막대 + 칸들의 정점 범위(판정 크기의 2.5배까지) → 카메라 뒤로 빼기 · 자르기
            Bounds frame = Frame(shot, judge);
            var (pull, crop) = Fit(ctx, rt, frame);

            // 3) 정점에 다시 재생해 찍고 가로로 붙인다
            int n = shot.cands.Length;
            sheet = new Texture2D(n * crop.width + (n - 1) * Sep, crop.height, TextureFormat.RGB24, false);
            Fill(sheet, SepInk);
            sb.AppendLine($"{shot.name} — {shot.title}");
            sb.AppendLine($"    판정 {shot.judgeNote} · 몬스터 자리 = 플레이어 앞 {ahead:0.0} m · 카메라 뒤로 {pull:0.0} m · 한 칸 {crop.width}×{crop.height} px × {n}");
            for (int i = 0; i < n; i++)
                sb.AppendLine(await CaptureAsync(shot, shot.cands[i], mon, ctx, rt, pull, crop, sheet, i * (crop.width + Sep), ct));
            sheet.Apply();
            File.WriteAllBytes($"{ShotDir}/{shot.name}.png", sheet.EncodeToPNG());
        }
        finally
        {
            foreach (var o in props) if (o != null) Object.Destroy(o);
            if (sheet != null) Object.Destroy(sheet);
        }
        sb.AppendLine();
        await UniTask.Delay(TimeSpan.FromSeconds(0.3f), DelayType.Realtime, cancellationToken: ct);
    }

    /// <summary>정점 찾기 — 맞춤이면 배율 1로 한 번 돌려 도달을 재 배율을 정하고, 그 배율로 다시 돌려 보이는 양이 가장 큰 순간을 찾는다.</summary>
    private static async UniTask SearchAsync(Shot shot, Cand c, Transform mon, CancellationToken ct)
    {
        var prefab = c.route == Route.Beam ? null : AssetDatabase.LoadAssetAtPath<GameObject>(c.path);
        c.loaded = c.route == Route.Beam || prefab != null;
        if (!c.loaded) return;

        c.used = c.scale;
        if (c.fit > 0f)
        {
            var probe = await PlayAsync(shot, c, prefab, mon, 1f, 0f, 0f, ct);   // 도달은 수명 전체에서
            c.fitReach1 = probe.reachMax;
            c.used = probe.reachMax > 0.01f ? c.fit / probe.reachMax : 1f;   // 도달이 판정 크기와 같아지는 배율
        }
        var r = await PlayAsync(shot, c, prefab, mon, c.used, c.winFrom, c.winTo, ct);
        c.life        = r.life;
        c.window      = r.window;
        c.peakAt      = r.peakAt;
        c.peakVisible = r.peakVisible;
        c.reachMax    = r.reachMax;
        c.peakBounds  = r.peakBounds;
        c.hasBounds   = r.hasBounds;
    }

    /// <summary>한 번 재생 — 게임 수명(최대 3초 · <paramref name="to"/>가 있으면 거기까지) 동안 0.1초 간격으로 재고 지운다. 정점은 <paramref name="from"/> 뒤에서만 고른다.</summary>
    private static async UniTask<Pass> PlayAsync(Shot shot, Cand c, GameObject prefab, Transform mon, float scale, float from, float to, CancellationToken ct)
    {
        var p = new Pass { peakVisible = -1f };
        var sp = Spawn(shot, c, prefab, mon, scale);
        try
        {
            p.life   = sp.life;
            p.window = Mathf.Min(sp.life, MaxWindow, to > 0f ? to : float.MaxValue);
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < p.window)
            {
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
                float t = Time.realtimeSinceStartup - t0;
                if (t > p.window) break;   // 게임은 수명에 지운다
                var m = Measure(sp.go, sp.origin);
                p.reachMax = Mathf.Max(p.reachMax, m.reach);
                if (t >= from && m.visible > p.peakVisible)
                {
                    p.peakVisible = m.visible;
                    p.peakAt      = t;
                    p.peakBounds  = m.bounds;
                    p.hasBounds   = m.any;
                }
                await UniTask.Delay(TimeSpan.FromSeconds(Step), DelayType.Realtime, cancellationToken: ct);
            }
        }
        finally
        {
            Despawn(sp);
        }
        await UniTask.Delay(TimeSpan.FromSeconds(0.3f), DelayType.Realtime, cancellationToken: ct);
        return p;
    }

    /// <summary>찍기 — 정점까지 기다려 재고, 고정 카메라(+뒤로 빼기)로 그린 뒤 같은 자리를 잘라 붙인다. 보고서 한 줄을 돌려준다.</summary>
    private static async UniTask<string> CaptureAsync(Shot shot, Cand c, Transform mon, Ctx ctx, RenderTexture rt,
                                                      float pull, RectInt crop, Texture2D sheet, int dstX, CancellationToken ct)
    {
        string head = $"    [{c.label}] {Short(c.path)}";
        if (!c.loaded) return head + " — 불러오기 실패(칸 비움)";

        var prefab = c.route == Route.Beam ? null : AssetDatabase.LoadAssetAtPath<GameObject>(c.path);
        var sp = Spawn(shot, c, prefab, mon, c.used);
        Meas m;
        try
        {
            await UniTask.Delay(TimeSpan.FromSeconds(Mathf.Max(0.05f, c.peakAt)), DelayType.Realtime, cancellationToken: ct);
            m = Measure(sp.go, sp.origin);
            RenderPanel(ctx, rt, pull);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            sheet.ReadPixels(new Rect(crop.x, crop.y, crop.width, crop.height), dstX, 0, false);
            RenderTexture.active = prev;
        }
        finally
        {
            Despawn(sp);
        }
        await UniTask.Delay(TimeSpan.FromSeconds(0.3f), DelayType.Realtime, cancellationToken: ct);

        var line = new StringBuilder(head);
        line.Append(c.route == Route.Beam
            ? $" 길이 {c.beamLen} · 굵기 {c.beamWidth} · 색 ({c.beamColor.r:0.##}, {c.beamColor.g:0.##}, {c.beamColor.b:0.##})"
            : $" ×{c.used:0.###}");
        if (c.fit > 0f) line.Append($"(도달 {c.fit} m 맞춤 · 배율 1 도달 {c.fitReach1:0.00} m)");
        if (c.faceRot) line.Append(" · 몬스터 방향");
        if (!string.IsNullOrEmpty(c.mute)) line.Append($" · 끈 층 {c.mute}");
        if (c.arc > 0f) line.Append($" · 사본 손질 호 {c.arc:0}°");
        if (c.fill) line.Append(" · 면 채움");
        if (c.strip) line.Append(" · 스크립트 끔");
        line.Append($"\n        찍은 때 {c.peakAt:0.00}초(찾은 구간 {(c.winFrom > 0f ? $"{c.winFrom:0.0}~" : string.Empty)}{c.window:0.0}초 · 게임 수명 {c.life:0.0}초) · 살아 있는 입자 {m.particles} · 입자계 {m.systems}");
        line.Append($" · 크기 {m.bounds.size.x:0.0}×{m.bounds.size.y:0.0}×{m.bounds.size.z:0.0} m · 도달(90%) {m.reach:0.0} m(구간 최대 {c.reachMax:0.0})");
        line.Append($" · 보이는 양 {m.visible:0.##} · 광원 {m.lights}(최대 세기 {m.maxLight:0.#}) · 굴절층 켜짐 {m.refraction}");
        if (c.route != Route.Beam && c.peakVisible <= 0f) line.Append("\n        ⚠ 보이는 입자 없음 — 재생이 안 됐거나(자동 재생 꺼짐 · 배속 0) 수명 안에 안 나온다");
        if (!string.IsNullOrEmpty(c.note)) line.Append($"\n        {c.note}");
        return line.ToString();
    }

    /// <summary>실제 재생 길로 띄운다 — 띄운 뒤 콜라이더를 끄고(그림과 무관) 끈 층의 렌더러를 끈다.</summary>
    private static Spawned Spawn(Shot shot, Cand c, GameObject prefab, Transform mon, float scale)
    {
        Spawned sp = c.route switch
        {
            Route.Cast    => SpawnCast(shot.cast, prefab, mon, scale),
            Route.Special => SpawnSpecial(prefab, mon, scale, c.faceRot),
            Route.Spore   => SpawnSpore(prefab, mon, scale),
            _             => SpawnBeam(shot.cast, c, mon),
        };
        if (sp.go == null) return sp;
        foreach (var col in sp.go.GetComponentsInChildren<Collider>(true)) col.enabled = false;
        Mute(sp.go, c.mute);
        Reshape(sp.go, c);
        return sp;
    }

    /// <summary>
    /// 사본 손질 — 띄운 같은 프레임(첫 시뮬레이션 전)에 넣는다. 호: 가운데 각은 그대로 두고 폭만 줄인다(모양 회전으로 시작 각을 옮김).
    /// 면 채움: radiusThickness 1(가장자리만 → 원 · 호 전체). 스크립트 끔: 앞으로 미는 이동 · 소리 등(없는 스크립트는 건너뜀).
    /// </summary>
    private static void Reshape(GameObject go, Cand c)
    {
        if (c.arc > 0f || c.fill)
        {
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                var sh = ps.shape;
                if (!sh.enabled) continue;
                if (c.arc > 0f && sh.arc > c.arc)
                {
                    float shift = (sh.arc - c.arc) * 0.5f;
                    sh.rotation += new Vector3(0f, 0f, shift);
                    sh.arc = c.arc;
                }
                if (c.fill) sh.radiusThickness = 1f;
            }
        }
        if (c.strip)
            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
                if (mb != null) mb.enabled = false;
    }

    /// <summary>PatternAttackOverrideSO.SpawnPatternVfx와 같게 — 자리 · 회전(몬스터) · 부모 · 배율(프리팹 배율 × 값) · Hierarchy · 반복 끔 · ForcePlayVfx.</summary>
    private static Spawned SpawnCast(CastSpec s, GameObject prefab, Transform mon, float scale)
    {
        Vector3 origin = s.atPlayer ? mon.position + mon.forward * s.playerAhead
                                    : mon.position + mon.forward * s.fwd + mon.right * s.right;
        Vector3 pos = origin + Vector3.up * s.up;
        Transform parent = s.attach && !s.atPlayer ? mon : null;
        var go = Object.Instantiate(prefab, pos, mon.rotation, parent);
        go.name = "~MonFxCompare_" + prefab.name;
        go.transform.localScale *= scale > 0.001f ? scale : 1f;
        foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
        {
            var main = ps.main;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.loop = false;
        }
        // ForcePlayVfx — 꺼 둔 자식까지 켜고 재생한다
        foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
        {
            if (!ps.gameObject.activeSelf) ps.gameObject.SetActive(true);
            ps.Play(false);
        }
        foreach (var l in go.GetComponentsInChildren<LineRenderer>(true))
        {
            if (!l.gameObject.activeSelf) l.gameObject.SetActive(true);
            l.enabled = true;
        }
        foreach (var tr in go.GetComponentsInChildren<TrailRenderer>(true))
        {
            if (!tr.gameObject.activeSelf) tr.gameObject.SetActive(true);
            tr.enabled = true;
            tr.Clear();
        }
        return new Spawned { go = go, origin = origin, life = s.life };
    }

    /// <summary>SpecialStateDataBase.SpawnVFX와 같게 — 발밑(오프셋 0) · 회전 identity · 배율 = one × 값(프리팹 배율을 덮음) · Hierarchy · 프리팹 자동 재생.</summary>
    private static Spawned SpawnSpecial(GameObject prefab, Transform mon, float scale, bool faceRot)
    {
        var go = Object.Instantiate(prefab, mon.position, faceRot ? mon.rotation : Quaternion.identity);
        go.name = "~MonFxCompare_" + prefab.name;
        go.transform.localScale = Vector3.one * scale;
        Hierarchy(go);
        var root = RootPs(go);
        float life = root != null ? root.main.duration + root.main.startLifetimeMultiplier + 0.3f : 3f;   // AutoDestroyVFX
        return new Spawned { go = go, origin = mon.position, life = life };
    }

    /// <summary>MushroomAngrySporeBlastState.SpawnSporeEffect와 같게 — 발밑 · 회전 identity · 배율 = one × 값 · 수명 = 길이 + 0.5초. 크기 모드는 실제로는 안 바꾼다(지시대로 강제).</summary>
    private static Spawned SpawnSpore(GameObject prefab, Transform mon, float scale)
    {
        var go = Object.Instantiate(prefab, mon.position, Quaternion.identity);
        go.name = "~MonFxCompare_" + prefab.name;
        go.transform.localScale = Vector3.one * scale;
        Hierarchy(go);
        var root = RootPs(go);
        float life = root != null ? root.main.duration + 0.5f : 3f;
        return new Spawned { go = go, origin = mon.position, life = life };
    }

    /// <summary>PatternAttackOverrideSO.SpawnProceduralBeamVfx와 같게 — 선 설정 · Sprites/Default · 눈 앞(fwd) · 높이(up)에서 앞으로 길이만큼.</summary>
    private static Spawned SpawnBeam(CastSpec s, Cand c, Transform mon)
    {
        var go = new GameObject("~MonFxCompare_Beam");
        var line = go.AddComponent<LineRenderer>();
        line.useWorldSpace     = true;
        line.positionCount     = 2;
        line.textureMode       = LineTextureMode.Stretch;
        line.shadowCastingMode = ShadowCastingMode.Off;
        line.receiveShadows    = false;
        line.alignment         = LineAlignment.View;
        line.numCapVertices    = 4;
        line.startWidth        = c.beamWidth;
        line.endWidth          = c.beamWidth;
        line.startColor        = c.beamColor;
        line.endColor          = c.beamColor;
        var mat = new Material(Shader.Find("Sprites/Default"));
        line.sharedMaterial = mat;
        Vector3 start = mon.position + mon.forward * s.fwd + Vector3.up * s.up;
        line.SetPosition(0, start);
        line.SetPosition(1, start + mon.forward * Mathf.Max(0.25f, c.beamLen));
        return new Spawned { go = go, origin = mon.position, life = s.life, mat = mat };
    }

    private static void Despawn(Spawned sp)
    {
        if (sp == null) return;
        if (sp.go != null) Object.Destroy(sp.go);
        if (sp.mat != null) Object.Destroy(sp.mat);
    }

    private static void Hierarchy(GameObject go)
    {
        foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
        {
            var main = ps.main;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        }
    }

    /// <summary>게임 코드의 「루트 PS ?? 자식 첫 PS」(빌드 기준 — 편집기의 가짜 null을 피해 TryGetComponent).</summary>
    private static ParticleSystem RootPs(GameObject go)
        => go.TryGetComponent(out ParticleSystem ps) ? ps : go.GetComponentInChildren<ParticleSystem>();

    /// <summary>끈 층 — 이름이 맞는 노드의 렌더러 · 광원만 끈다(자식은 그대로). 「@root」 = 루트 자신(띄우며 이름이 바뀐다).</summary>
    private static void Mute(GameObject go, string names)
    {
        if (string.IsNullOrEmpty(names)) return;
        var list = names.Split(',');
        bool root = Array.IndexOf(list, "@root") >= 0;
        foreach (var t in go.GetComponentsInChildren<Transform>(true))
        {
            if (!(t == go.transform ? root : Array.IndexOf(list, t.name) >= 0)) continue;
            if (t.TryGetComponent(out Renderer r)) r.enabled = false;
            if (t.TryGetComponent(out Light l)) l.enabled = false;
        }
    }

    /// <summary>무대 — 판정 외곽선(바닥 위 3 cm) · 몬스터 자리 1 m 막대. 둘을 담은 범위를 돌려준다.</summary>
    private static Bounds BuildStage(Shot shot, Transform mon, float floorY, List<Object> props)
    {
        var mat = new Material(Shader.Find("Sprites/Default"));
        props.Add(mat);
        var b = new Bounds(mon.position, Vector3.zero);
        foreach (var pts in JudgeLines(shot, mon, floorY + 0.03f))
        {
            var go = new GameObject("~MonFxCompare_Judge");
            props.Add(go);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace     = true;
            lr.loop              = true;
            lr.positionCount     = pts.Length;
            lr.SetPositions(pts);
            lr.startWidth        = LineWidth;
            lr.endWidth          = LineWidth;
            lr.startColor        = OutlineInk;
            lr.endColor          = OutlineInk;
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows    = false;
            lr.sharedMaterial    = mat;
            foreach (var p in pts) b.Encapsulate(p);
        }

        var pole = GameObject.CreatePrimitive(PrimitiveType.Cube);
        pole.name = "~MonFxCompare_Pole1m";
        props.Add(pole);
        if (pole.TryGetComponent(out Collider col)) col.enabled = false;
        pole.transform.SetPositionAndRotation(mon.position + Vector3.up * 0.5f, mon.rotation);
        pole.transform.localScale = new Vector3(0.05f, 1f, 0.05f);
        if (pole.TryGetComponent(out Renderer pr))
        {
            var pm = pr.material;   // 인스턴스 — 기본 재질 에셋은 건드리지 않는다
            pm.color = PoleInk;
            props.Add(pm);
        }
        b.Encapsulate(mon.position + Vector3.up);
        return b;
    }

    /// <summary>판정 모양 — 칸(1 m · PatternAttackOverrideSO.IsInsideGrid와 같은 칸) · 원 · 부채꼴. 닫힌 선들.</summary>
    private static List<Vector3[]> JudgeLines(Shot s, Transform mon, float y)
    {
        var lines = new List<Vector3[]>();
        Vector3 o = mon.position; o.y = y;
        Vector3 f = mon.forward, r = mon.right;
        switch (s.judge)
        {
            case Judge.Cells:
                foreach (var c in s.cells)
                {
                    Vector3 ctr = o + r * c.x + f * c.y;
                    lines.Add(new[] { ctr + (-r - f) * 0.5f, ctr + (r - f) * 0.5f, ctr + (r + f) * 0.5f, ctr + (-r + f) * 0.5f });
                }
                break;
            case Judge.Circle:
            {
                var pts = new Vector3[64];
                for (int i = 0; i < pts.Length; i++)
                {
                    float a = i * Mathf.PI * 2f / pts.Length;
                    pts[i] = o + (r * Mathf.Cos(a) + f * Mathf.Sin(a)) * s.radius;
                }
                lines.Add(pts);
                break;
            }
            case Judge.Sector:
            {
                const int seg = 24;
                var pts = new Vector3[seg + 2];
                pts[0] = o;
                for (int i = 0; i <= seg; i++)
                {
                    float a = Mathf.Lerp(-s.angle * 0.5f, s.angle * 0.5f, i / (float)seg);
                    pts[i + 1] = o + Quaternion.AngleAxis(a, Vector3.up) * f * s.radius;
                }
                lines.Add(pts);
                break;
            }
        }
        return lines;
    }

    /// <summary>판정이 몬스터 뒤로 뻗는 거리 — 몬스터를 그만큼 더 앞에 세워 플레이어 몸과 겹치지 않게 한다.</summary>
    private static float BackExtent(Shot s)
    {
        switch (s.judge)
        {
            case Judge.Circle: return s.radius;
            case Judge.Cells:
                int minY = int.MaxValue;
                foreach (var c in s.cells) minY = Mathf.Min(minY, c.y);
                return Mathf.Max(0f, 0.5f - minY);
            default: return 0f;
        }
    }

    /// <summary>액자 — 판정 · 막대 범위 + 칸들의 정점 범위(판정 가운데에서 판정 크기의 2.5배 · 최소 2.5 m까지로 자름).</summary>
    private static Bounds Frame(Shot shot, Bounds judge)
    {
        var f = judge;
        Vector3 c = judge.center;
        float cap = Mathf.Max(2.5f, 2.5f * Mathf.Max(judge.extents.x, judge.extents.z));
        Vector3 lo = c - Vector3.one * cap, hi = c + Vector3.one * cap;
        lo.y = judge.min.y - 0.2f;   // 바닥 아래는 볼 것이 없다
        foreach (var k in shot.cands)
        {
            if (!k.hasBounds) continue;
            Vector3 mn = Vector3.Max(k.peakBounds.min, lo), mx = Vector3.Min(k.peakBounds.max, hi);
            if (mn.x > mx.x || mn.y > mx.y || mn.z > mx.z) continue;
            f.Encapsulate(mn);
            f.Encapsulate(mx);
        }
        return f;
    }

    /// <summary>액자가 화면(가장자리 2%) 안에 들 때까지 카메라를 시선 반대로 0.5 m씩 뺀다 → 그 화면에서 액자 자리를 자른다(여백 8% + 16 px · 최소 360 px).</summary>
    private static (float pull, RectInt crop) Fit(Ctx ctx, RenderTexture rt, Bounds frame)
    {
        var cam = ctx.cam;
        var t = cam.transform;
        Vector3 p0 = t.position;
        Quaternion r0 = t.rotation;
        var tt = cam.targetTexture;
        Vector3 back = ctx.camRot * Vector3.back;
        float pull = 0f;
        bool front = false;
        Vector2 vmin = Vector2.zero, vmax = Vector2.one;
        try
        {
            cam.targetTexture = rt;   // 화면 비율을 그릴 그림(16:9)과 맞춘다
            for (; ; pull += 0.5f)
            {
                t.SetPositionAndRotation(ctx.camPos + back * pull, ctx.camRot);
                front = View(cam, frame, out vmin, out vmax);
                bool inside = front && vmin.x >= 0.02f && vmin.y >= 0.02f && vmax.x <= 0.98f && vmax.y <= 0.98f;
                if (inside || pull >= MaxPull) break;
            }
        }
        finally
        {
            cam.targetTexture = tt;
            t.SetPositionAndRotation(p0, r0);
        }
        if (!front) return (pull, new RectInt(0, 0, W, H));

        float x0 = Mathf.Clamp01(vmin.x) * W, x1 = Mathf.Clamp01(vmax.x) * W;
        float y0 = Mathf.Clamp01(vmin.y) * H, y1 = Mathf.Clamp01(vmax.y) * H;
        float padX = (x1 - x0) * 0.08f + 16f, padY = (y1 - y0) * 0.08f + 16f;
        x0 -= padX; x1 += padX; y0 -= padY; y1 += padY;
        int cw = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(x1 - x0, MinPanel)), 1, W);
        int ch = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(y1 - y0, MinPanel)), 1, H);
        int cx = Mathf.Clamp(Mathf.RoundToInt((x0 + x1) * 0.5f - cw * 0.5f), 0, W - cw);
        int cy = Mathf.Clamp(Mathf.RoundToInt((y0 + y1) * 0.5f - ch * 0.5f), 0, H - ch);
        return (pull, new RectInt(cx, cy, cw, ch));
    }

    /// <summary>범위 꼭짓점 8개의 화면 비율 좌표 범위 — 하나라도 카메라 뒤면 false.</summary>
    private static bool View(Camera cam, Bounds b, out Vector2 vmin, out Vector2 vmax)
    {
        vmin = new Vector2(float.MaxValue, float.MaxValue);
        vmax = new Vector2(float.MinValue, float.MinValue);
        bool front = true;
        Vector3 mn = b.min, mx = b.max;
        for (int i = 0; i < 8; i++)
        {
            var p = new Vector3((i & 1) == 0 ? mn.x : mx.x, (i & 2) == 0 ? mn.y : mx.y, (i & 4) == 0 ? mn.z : mx.z);
            Vector3 v = cam.WorldToViewportPoint(p);
            if (v.z <= cam.nearClipPlane) front = false;
            vmin = Vector2.Min(vmin, v);
            vmax = Vector2.Max(vmax, v);
        }
        return front;
    }

    /// <summary>고정한 카메라 자세(+뒤로 빼기)로 한 번 그린다 — 같은 프레임 안에서 되돌려 시네머신과 다투지 않는다(HUD 없음).</summary>
    private static void RenderPanel(Ctx ctx, RenderTexture rt, float pull)
    {
        var cam = ctx.cam;
        var t = cam.transform;
        Vector3 p0 = t.position;
        Quaternion r0 = t.rotation;
        var tt = cam.targetTexture;
        try
        {
            t.SetPositionAndRotation(ctx.camPos + ctx.camRot * Vector3.back * pull, ctx.camRot);
            cam.targetTexture = rt;
            cam.Render();
        }
        finally
        {
            cam.targetTexture = tt;
            t.SetPositionAndRotation(p0, r0);
        }
    }

    /// <summary>
    /// 잰다 — 보이는 양(Σ 알파 × 크기²) · 도달(보이는 양 90% 반경) · 범위(알파 0.2 이상 입자 + 입자 아닌 렌더러) ·
    /// 살아 있는 입자 수 · 켜진 광원 · 켜진 굴절층(Distortion · ShockWave 셰이더 또는 Distortion 재질). 렌더러를 끈 층은 뺀다.
    /// </summary>
    private static Meas Measure(GameObject go, Vector3 origin)
    {
        var m = new Meas();
        if (go == null || !go.activeInHierarchy) return m;
        s_weights.Clear();
        float total = 0f;
        foreach (var ps in go.GetComponentsInChildren<ParticleSystem>())
        {
            m.systems++;
            if (!ps.TryGetComponent(out ParticleSystemRenderer pr) || !pr.enabled) continue;
            int n = ps.particleCount;
            if (n == 0) continue;
            if (s_buf.Length < n) s_buf = new ParticleSystem.Particle[Mathf.NextPowerOfTwo(n)];
            n = ps.GetParticles(s_buf, n);
            m.particles += n;
            bool local = ps.main.simulationSpace == ParticleSystemSimulationSpace.Local;
            float s = Mathf.Abs(ps.transform.lossyScale.x);
            for (int k = 0; k < n; k++)
            {
                Vector3 p = local ? ps.transform.TransformPoint(s_buf[k].position) : s_buf[k].position;
                float size = s_buf[k].GetCurrentSize(ps) * s;
                float a = s_buf[k].GetCurrentColor(ps).a / 255f;
                float w = a * size * size;
                m.visible += w;
                total += w;
                Vector3 d = p - origin; d.y = 0f;
                s_weights.Add(new Vector2(d.magnitude + size * EdgeK, w));
                if (a >= AlphaFloor) Encap(ref m, new Bounds(p, Vector3.one * size));
            }
        }
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled) continue;
            if (IsRefraction(r)) m.refraction++;
            if (r is ParticleSystemRenderer) continue;
            Encap(ref m, r.bounds);
        }
        foreach (var l in go.GetComponentsInChildren<Light>())
        {
            if (!l.enabled) continue;
            m.lights++;
            m.maxLight = Mathf.Max(m.maxLight, l.intensity);
        }
        m.reach = Percentile(total);
        return m;
    }

    private static float Percentile(float total)
    {
        if (total <= 0f || s_weights.Count == 0) return 0f;
        s_weights.Sort((a, b) => a.x.CompareTo(b.x));
        float acc = 0f, goal = total * ReachPct;
        for (int i = 0; i < s_weights.Count; i++)
        {
            acc += s_weights[i].y;
            if (acc >= goal) return s_weights[i].x;
        }
        return s_weights[s_weights.Count - 1].x;
    }

    private static void Encap(ref Meas m, Bounds b)
    {
        if (!m.any) { m.bounds = b; m.any = true; }
        else m.bounds.Encapsulate(b);
    }

    private static bool IsRefraction(Renderer r)
    {
        var mat = r.sharedMaterial;
        if (mat == null) return false;
        string sh = mat.shader != null ? mat.shader.name : string.Empty;
        return sh.IndexOf("Distortion", StringComparison.OrdinalIgnoreCase) >= 0
            || sh.IndexOf("ShockWave", StringComparison.OrdinalIgnoreCase) >= 0
            || mat.name.IndexOf("Distortion", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static void Fill(Texture2D tex, Color c)
    {
        var px = new Color32[tex.width * tex.height];
        Color32 c32 = c;
        for (int i = 0; i < px.Length; i++) px[i] = c32;
        tex.SetPixels32(px);
    }

    private static string Short(string path)
        => string.IsNullOrEmpty(path) ? "(코드 내장 직선)" : path.Replace("Assets/RelicFairy/", string.Empty);
}
#endif
