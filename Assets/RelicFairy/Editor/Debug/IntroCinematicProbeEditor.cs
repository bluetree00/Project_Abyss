#if UNITY_EDITOR
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 10-10 인트로 연출 실측 — 테스트 허브에서 Game_Intro를 열어 오프닝 · 서임 · 강림 · 타락 · 전투 시작을 쇼트마다 찍고 잰다.
/// 쇼트마다: 인물(플레이어 · 모르드레드 · 리치)이 화면 안인지 · 벽에 가렸는지 · 화면 높이 비율, 레터박스 · 입력 차단 상태.
/// 끝에: 검을 집고 조작이 돌아오기까지 걸린 시간(설계 상한 45초 + 보스 등장).
/// 결과: Temp/intro_cine_probe.txt · Temp/ui_shots/Intro_*.png · 로그 「[IntroCine] 끝」. 실측 뒤 플레이를 멈출 것(전투에서 죽으면 인트로 완료가 기록된다).
/// </summary>
public static class IntroCinematicProbeEditor
{
    private const BindingFlags Any = BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const int EnvMask = 1 << 0 | 1 << 3 | 1 << 8;
    private const float LetterboxBand = 0.128f;   // 2.39:1 레터박스가 16:9 화면 위아래에서 가리는 비율

    [MenuItem("RelicFairy/Debug/10-10 인트로 연출 실측 (테스트 허브, 플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[IntroCine] 플레이 모드(테스트 허브)에서"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("인트로 연출 실측\n");
        try
        {
            AppBootstrapper.Instance?.RequestLoad(Define.Scene.Game_Intro);
            if (!await WaitAsync(() => SceneManager.GetActiveScene().name == "Game_Intro", 60f)) { sb.AppendLine("Game_Intro 진입 실패"); return; }

            // ① 오프닝 — 첫 화면(하얀 화면 점검)은 촘촘히, 그 뒤는 2.5초마다. 프롤로그 대사는 넘긴다.
            float t0 = Time.realtimeSinceStartup;
            int n = 0;
            foreach (float at in new[] { 0.3f, 0.8f, 1.5f, 2.0f, 2.5f, 3.0f, 3.6f })
            {
                while (Time.realtimeSinceStartup - t0 < at) await UniTask.Yield();
                await Shot($"Intro_O{n++:00}_t{at:0.0}");
                sb.AppendLine($"  오프닝 {at:0.0}초 · 암전 {FadeAlpha():0.00}");
                if (at >= 2.5f) DumpOverlays(sb);
            }
            PlayerController player = null;
            float lastShot = 0f, lastSkip = 0f;
            while (Time.realtimeSinceStartup - t0 < 150f)
            {
                float t = Time.realtimeSinceStartup - t0;
                player = IntroBootstrapper.Instance?.Player;
                bool opened = player != null && !InputLocked(player) && t > 8f;
                if (opened) break;
                if (t - lastSkip > 1.6f) { lastSkip = t; TestHubDebugMenu.AdvanceDialogue(); }
                if (t - lastShot > 2.5f) { lastShot = t; await Shot($"Intro_O{n++:00}_t{t:000}"); sb.AppendLine($"  오프닝 {t:0}초 · {Subject("플레이어", player != null ? player.gameObject : null)}"); }
                await UniTask.Delay(200, DelayType.Realtime);
            }
            if (player == null) { sb.AppendLine("플레이어 없음"); return; }
            sb.AppendLine($"오프닝 끝(조작 인계) {Time.realtimeSinceStartup - t0:0.0}초");
            await UniTask.Delay(1200, DelayType.Realtime);
            await Shot($"Intro_O{n++:00}_walk");

            // ② 검을 집는다 — 회랑을 걷는 대신 검 앞으로 옮겨 F 입력과 같은 사건을 낸다.
            var pickup = Object.FindFirstObjectByType<IntroSwordPickup>();
            if (pickup == null) { sb.AppendLine("검 없음"); return; }
            var to = pickup.transform.position + Vector3.back * 1.2f;
            to.y = player.transform.position.y;
            player.transform.position = to;
            if (player.TryGetComponent<Rigidbody>(out var rb)) { rb.position = to; rb.linearVelocity = Vector3.zero; }
            await UniTask.Delay(400, DelayType.Realtime);
            typeof(IntroSwordPickup).GetField("_consumed", Any)?.SetValue(pickup, true);
            (typeof(IntroSwordPickup).GetField("OnPickup", Any)?.GetValue(pickup) as System.Delegate)?.DynamicInvoke(player);
            float p0 = Time.realtimeSinceStartup;

            // ③ 쇼트가 바뀔 때마다 찍고 잰다. 조작이 돌아오면 끝.
            var director = Object.FindFirstObjectByType<IntroMordredDirector>();
            var mordred  = typeof(IntroMordredDirector).GetField("mordredActor", Any)?.GetValue(director) as GameObject;
            var lich     = typeof(IntroMordredDirector).GetField("lichActor", Any)?.GetValue(director) as GameObject;
            await UniTask.Delay(300, DelayType.Realtime);
            DumpLayout(sb, player, mordred, pickup.transform);
            string last = null;
            int s = 0, lockedSamples = 0, mismatch = 0;
            float unlockedAt = -1f;
            while (Time.realtimeSinceStartup - p0 < 120f)
            {
                string shot = IntroMordredDirector.CurrentShot;
                bool locked = InputLocked(player);
                bool bars = LetterboxShown() > 0.5f;
                if (shot != null) { lockedSamples++; if (bars != locked) mismatch++; }
                if (shot != null && shot != last)
                {
                    last = shot;
                    await UniTask.Delay(450, DelayType.Realtime);
                    await Shot($"Intro_S{s++:00}_{Safe(shot)}");
                    sb.AppendLine($"  {Time.realtimeSinceStartup - p0,5:0.0}초 {shot,-14} | {Subject("플레이어", player.gameObject)} | {Subject("모르드레드", mordred)} | {Subject("리치", lich)} | 레터박스 {(bars ? "있음" : "없음")} · 입력 {(locked ? "막힘" : "열림")}");
                    continue;
                }
                if (shot == null && last != null && !locked) { unlockedAt = Time.realtimeSinceStartup - p0; break; }
                await UniTask.Delay(100, DelayType.Realtime);
            }
            sb.AppendLine($"검을 집고 조작이 돌아오기까지 {unlockedAt:0.0}초(보스 등장 포함) · 쇼트 {s}개");
            sb.AppendLine($"CHECK 쇼트 동안 레터박스 = 입력 차단: {mismatch == 0} (어긋난 표본 {mismatch}/{lockedSamples})");

            // ④ 전투 시작 — 노출(하얀 안개) 점검
            foreach (int ms in new[] { 500, 2000, 4000 })
            {
                await UniTask.Delay(ms == 500 ? 500 : 1500, DelayType.Realtime);
                await Shot($"Intro_F_{ms}");
                sb.AppendLine($"  전투 +{ms / 1000f:0.0}초 · 화면 위쪽 밝기 {TopBrightness():0.00}");
                if (ms == 500) { DumpHaze(sb); await GlassTestAsync(sb); }
            }
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            File.WriteAllText("Temp/intro_cine_probe.txt", sb.ToString());
            Debug.Log("[IntroCine] 끝\n" + sb);
        }
    }

    // ── 판정 ─────────────────────────────────────────────────────────
    /// <summary>화면 안(레터박스 띠 제외) · 벽 가림 · 화면 높이 비율.</summary>
    private static string Subject(string name, GameObject go)
    {
        var cam = Camera.main;
        if (go == null || !go.activeInHierarchy || cam == null) return $"{name} -";
        var rends = go.GetComponentsInChildren<SkinnedMeshRenderer>().Where(r => r.enabled).ToArray();
        if (rends.Length == 0) return $"{name} -";
        var b = rends[0].bounds;
        foreach (var r in rends) b.Encapsulate(r.bounds);
        Vector3 vc = cam.WorldToViewportPoint(b.center);
        Vector3 vt = cam.WorldToViewportPoint(b.center + Vector3.up * b.extents.y);
        Vector3 vb = cam.WorldToViewportPoint(b.center - Vector3.up * b.extents.y);
        bool inView = vc.z > 0f && vc.x > 0.03f && vc.x < 0.97f && vc.y > LetterboxBand && vc.y < 1f - LetterboxBand;
        // 가림 — 대상 자신의 콜라이더는 빼고 카메라 → 몸 가운데 사이의 벽 · 바닥 · 기둥만
        Vector3 from = cam.transform.position, d = b.center - from;
        bool hidden = Physics.RaycastAll(from, d.normalized, d.magnitude, EnvMask, QueryTriggerInteraction.Ignore)
                             .Any(h => !h.collider.transform.IsChildOf(go.transform));
        float frac = Mathf.Abs(vt.y - vb.y) / (1f - 2f * LetterboxBand);   // 레터박스 안쪽 화면 높이 대비
        return $"{name} {(inView ? "안" : "밖")}{(hidden ? "·가림" : "")} {frac:P0}";
    }

    private static bool InputLocked(PlayerController p)
        => p != null && (bool)(typeof(PlayerController).GetField("_inputDisabledExternally", Any)?.GetValue(p) ?? false);

    private static float LetterboxShown()
        => (float)(typeof(CinematicFrame).GetField("_shown", Any)?.GetValue(null) ?? 0f);

    private static float FadeAlpha()
        => (typeof(ScreenFade).GetField("_cg", Any)?.GetValue(null) as CanvasGroup)?.alpha ?? -1f;

    /// <summary>화면 위쪽 1/4의 평균 밝기(0~1) — 전투 시작 화면이 하얗게 날아가는지.</summary>
    private static float TopBrightness()
    {
        var cam = Camera.main;
        if (cam == null) return -1f;
        var rt = RenderTexture.GetTemporary(192, 108, 24);
        var prev = cam.targetTexture;
        cam.targetTexture = rt;
        cam.Render();
        cam.targetTexture = prev;
        var tex = new Texture2D(192, 27, TextureFormat.RGB24, false);
        var active = RenderTexture.active;
        RenderTexture.active = rt;
        tex.ReadPixels(new Rect(0, 81, 192, 27), 0, 0);
        RenderTexture.active = active;
        RenderTexture.ReleaseTemporary(rt);
        float sum = 0f;
        var px = tex.GetPixels();
        foreach (var c in px) sum += c.grayscale;
        Object.DestroyImmediate(tex);
        return sum / px.Length;
    }

    /// <summary>오프닝 첫 화면 — 켜진 오버레이 캔버스(정렬 순서 · 알파 · 화면을 덮는 밝은 이미지)를 적는다.</summary>
    private static void DumpOverlays(StringBuilder sb)
    {
        foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None)
                                .Where(c => c.isRootCanvas && c.isActiveAndEnabled && c.renderMode == RenderMode.ScreenSpaceOverlay)
                                .OrderByDescending(c => c.sortingOrder))
        {
            var cg = c.GetComponent<CanvasGroup>();
            float a = cg != null ? cg.alpha : 1f;
            if (a <= 0.01f) continue;
            var bright = c.GetComponentsInChildren<UnityEngine.UI.Graphic>()
                          .Where(g => g.isActiveAndEnabled && g.color.a > 0.05f && g.rectTransform.rect.width > 1200f
                                      && g.color.grayscale > 0.5f)
                          .Select(g => $"{g.name}(a{g.color.a:0.00} v{g.color.grayscale:0.00} {g.GetType().Name})").Take(4);
            sb.AppendLine($"     캔버스 {c.name} 순서 {c.sortingOrder} 알파 {a:0.00} 밝은 판: {string.Join(", ", bright)}");
        }
    }

    /// <summary>쇼트 계산의 근거 — 인물 몸 범위 · 검 · 통로 양옆 의자(콜라이더 여부).</summary>
    private static void DumpLayout(StringBuilder sb, PlayerController player, GameObject mordred, Transform sword)
    {
        sb.AppendLine($"  배치: 플레이어 {Body(player.gameObject)} · 모르드레드 {Body(mordred)} · 검 {sword.position}");
        Vector3 p = player.transform.position, m = mordred != null ? mordred.transform.position : p;
        Vector3 f = m - p; f.y = 0f; f.Normalize();
        Vector3 r = Vector3.Cross(Vector3.up, f);
        var near = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)
            .Where(x => x.enabled && x.bounds.size.y > 0.6f && x.bounds.size.y < 4f && x.bounds.max.y < p.y + 4f)
            .Select(x => (x, side: Vector3.Dot(x.bounds.center - p, r), along: Vector3.Dot(x.bounds.center - p, f)))
            .Where(t => t.along > -8f && t.along < 12f && Mathf.Abs(t.side) < 7f)
            .OrderBy(t => t.along).Take(30);
        foreach (var (x, side, along) in near)
        {
            var col = x.GetComponentInParent<Collider>();
            sb.AppendLine($"     곁 {x.name,-28} 앞{along,6:0.0} 옆{side,6:0.0} 높이 {x.bounds.max.y - p.y:0.0} 콜라이더 {(col != null ? LayerMask.LayerToName(col.gameObject.layer) : "없음")}");
        }
    }

    private static string Body(GameObject go)
    {
        if (go == null) return "-";
        var rs = go.GetComponentsInChildren<SkinnedMeshRenderer>();
        if (rs.Length == 0) return go.transform.position.ToString();
        var b = rs[0].bounds;
        foreach (var x in rs) b.Encapsulate(x.bounds);
        return $"발 {b.min.y:0.00} 머리 {b.max.y:0.00} 가운데 {b.center}";
    }

    /// <summary>전투 시작 화면 위쪽을 덮는 반투명 판 — 시야 안의 큰 투명 렌더러를 적는다.</summary>
    private static void DumpHaze(StringBuilder sb)
    {
        var cam = Camera.main;
        if (cam == null) return;
        var planes = GeometryUtility.CalculateFrustumPlanes(cam);
        foreach (var x in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None)
                     .Where(x => x.enabled && x.isVisible && !(x is ParticleSystemRenderer) && x.sharedMaterial != null
                                 && x.sharedMaterial.renderQueue >= 2900 && x.bounds.size.magnitude > 8f
                                 && GeometryUtility.TestPlanesAABB(planes, x.bounds))
                     .OrderByDescending(x => x.bounds.size.magnitude).Take(8))
            sb.AppendLine($"     투명 판 {x.name} ({x.sharedMaterial.name} q{x.sharedMaterial.renderQueue}) 크기 {x.bounds.size} 가운데 {x.bounds.center} 거리 {Vector3.Distance(cam.transform.position, x.bounds.center):0.0}");
    }

    /// <summary>원인 확인 — 시야 안 큰 유리 판을 잠깐 끄고 찍어 위쪽 밝기를 비교한다(실측 뒤 다시 켠다).</summary>
    private static async UniTask GlassTestAsync(StringBuilder sb)
    {
        var cam = Camera.main;
        if (cam == null) return;
        var planes = GeometryUtility.CalculateFrustumPlanes(cam);
        var glass = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None)
            .Where(x => x.enabled && x.name.Contains("GlassWindow") && GeometryUtility.TestPlanesAABB(planes, x.bounds)).ToArray();
        var room = Object.FindFirstObjectByType<BossRoomController>();
        var barrier = room != null ? typeof(BossRoomController).GetField("barrier", Any)?.GetValue(room) as GameObject : null;
        sb.AppendLine($"     유리 {glass.Length}개 · 보스룸 결계 {(barrier != null ? barrier.name + (barrier.activeInHierarchy ? " 켜짐" : " 꺼짐") : "없음")}");
        foreach (var g in glass.Where(g => g.bounds.size.magnitude > 10f))
            sb.AppendLine($"     큰 유리 {Path(g.transform)} 크기 {g.bounds.size} 가운데 {g.bounds.center} 배율 {g.transform.lossyScale} 정적 {g.isPartOfStaticBatch}");
        foreach (var g in glass) g.enabled = false;
        await UniTask.Delay(200, DelayType.Realtime);
        await Shot("Intro_F_noglass");
        sb.AppendLine($"     유리 끔 → 위쪽 밝기 {TopBrightness():0.00}");
        foreach (var g in glass) g.enabled = true;
        if (barrier != null && barrier.activeSelf)
        {
            barrier.SetActive(false);
            await UniTask.Delay(200, DelayType.Realtime);
            await Shot("Intro_F_nobarrier");
            sb.AppendLine($"     결계 끔 → 위쪽 밝기 {TopBrightness():0.00}");
            barrier.SetActive(true);
        }
    }

    private static string Path(Transform t)
    {
        var sb = new StringBuilder(t.name);
        for (var p = t.parent; p != null; p = p.parent) sb.Insert(0, p.name + "/");
        return sb.ToString();
    }

    private static string Safe(string s) => new string(s.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());

    private static UniTask Shot(string name)
    {
        var m = typeof(UILayoutRuntimeProbeEditor).GetMethod("ShotAsync", BindingFlags.Static | BindingFlags.NonPublic);
        return m != null ? (UniTask)m.Invoke(null, new object[] { name, 0 }) : UniTask.CompletedTask;
    }

    private static async UniTask<bool> WaitAsync(System.Func<bool> cond, float seconds)
    {
        float t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < seconds)
        {
            try { if (cond()) return true; } catch { }
            await UniTask.Delay(250, DelayType.Realtime);
        }
        return false;
    }
}
#endif
