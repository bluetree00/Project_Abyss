#if UNITY_EDITOR
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>
/// 10-26 진화 카드 그림 실측(사용자 「무형검 진화 때 무기 선택 · 형 고르는 카드 리소스가 너무 안 좋다 — 힉스필드로 실제 오브젝트 모양 살려서」).
/// 런 안에서 근접 칸을 무형검 +9로 두고 재련소를 열어 진화 창을 띄운다 — 배경 · 카드 머리 그림(카타나 · 대검) · 형 문장이 들어갔나,
/// 이름 줄 · 형 줄이 칸 안에 드나, 형 문장과 형 머리가 겹치지 않나, 수치 칸 · 바닥 글이 카드 안에 드나 본다.
/// 결과 Temp/evo1026.txt · Temp/ui_shots/EVO26_*.png · 로그 「[Evo26] 끝」.
/// </summary>
public static class Evo1026ProbeEditor
{
    private const string Tag = "[Evo26]";
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const string Nameless = "Assets/RelicFairy/Weapon/Nameless/Data/T0_Nameless.asset";

    [MenuItem("RelicFairy/Debug/10-26 진화 카드 그림 실측 — 배경 · 무기 머리 그림 · 형 문장 (런 중)")]
    private static void Run()
    {
        var p = GameRunBootstrapper.Instance?.Run?.Player;
        if (!EditorApplication.isPlaying || p == null) { Debug.LogWarning($"{Tag} 런 안에서"); return; }
        RunAsync(p, p.GetCancellationTokenOnDestroy()).Forget();
    }

    private static async UniTaskVoid RunAsync(PlayerController p, CancellationToken ct)
    {
        var sb = new StringBuilder("10-26 진화 카드 그림 실측\n");
        int pass = 0, total = 0;
        void Check(string what, bool ok, string detail = "") { total++; if (ok) pass++; sb.AppendLine($"CHECK {what}: {ok}{(detail.Length > 0 ? "  — " + detail : "")}"); }
        var go = new GameObject("~Evo1026Probe");
        UI_CruciblePanel panel = null;
        try
        {
            typeof(PlayerController).GetField("debugInvincible", Inst)?.SetValue(p, true);
            foreach (var mo in Object.FindObjectsByType<RelicFairy.Monster.MonsterBase>(FindObjectsSortMode.None))
                if (mo != null && !mo.IsDead) mo.gameObject.SetActive(false);
            var wm = p.WeaponManager;
            if (wm.Weapon0Data == null || !wm.Weapon0Data.weaponSOKey.Contains("Nameless"))
            {
                var equip = typeof(CrucibleV3ProbeEditor).GetMethod("EquipAsync", BindingFlags.Static | BindingFlags.NonPublic);
                bool ok = equip != null && await (UniTask<bool>)equip.Invoke(null, new object[] { p, Nameless, ct });
                if (!ok) { sb.AppendLine("  무형검 장착 실패"); return; }
            }
            if (wm.CurrentSlotIndex != PlayerWeaponManager.Slot0) await wm.SwitchToSlotAsync(PlayerWeaponManager.Slot0);
            var w = wm.Weapon0Data;
            w.enhanceLevel = 9;
            w.highestEnhanceReached = Mathf.Max(w.highestEnhanceReached, 30);   // 증강 고르기 창이 끼지 않게
            w.RecomputeEnhancedStats();

            var run  = GameRunBootstrapper.Instance.Run;
            var ctrl = go.AddComponent<CrucibleRoomController>();
            ctrl.Initialize(run, await WeaponEnhanceService.EnsureLoadedAsync(), new System.Random(7), null);
            panel = await Managers.UI.ShowPopupUIAndGetAsync<UI_CruciblePanel>();
            panel.Bind(ctrl);
            await UniTask.Delay(900, ignoreTimeScale: true, cancellationToken: ct);
            (F<Button[]>(panel, "_chooseCard"))?[0]?.onClick.Invoke();
            Call(panel, "RefreshAll");
            await UniTask.Delay(500, ignoreTimeScale: true, cancellationToken: ct);

            Call(panel, "ShowEvolvePanel");
            await UniTask.Delay(700, ignoreTimeScale: true, cancellationToken: ct);
            var evo = F<GameObject>(panel, "_evolvePanel");
            Check("진화 창이 열렸다", evo != null && evo.activeInHierarchy);
            if (evo == null) return;

            var backdrop = Find(evo.transform, "Backdrop")?.GetComponent<RawImage>();
            Check("창 배경 = 모루 위 무형검(찌그러짐 없이 위아래만 자름)", backdrop != null && backdrop.texture != null && backdrop.uvRect.height < 1f,
                  backdrop != null ? $"uv {backdrop.uvRect} · 알파 {backdrop.color.a:0.00}" : "없음");
            if (backdrop != null)
            {
                var rb = WorldRect(backdrop.rectTransform); var rp = WorldRect((RectTransform)evo.transform);
                Check("창 배경이 판 가로를 꽉 채움(양옆 어두운 띠 없음)", Mathf.Abs(rb.width - rp.width) <= 2f && Mathf.Abs(rb.xMin - rp.xMin) <= 2f,
                      $"배경 {rb.xMin:0}~{rb.xMax:0} · 판 {rp.xMin:0}~{rp.xMax:0}");
            }
            await Shot("EVO26_1_Open");

            foreach (var cardName in new[] { "EvolveA", "EvolveB" })
            {
                var card = Find(evo.transform, cardName) as RectTransform;
                if (card == null || !card.gameObject.activeInHierarchy) { sb.AppendLine($"  {cardName} 꺼짐"); continue; }
                var banner = Find(card, "Banner")?.GetComponent<Image>();
                var emblem = Find(card, "FormEmblem")?.GetComponent<Image>();
                var name   = Find(card, "Name")?.GetComponent<TMP_Text>();
                var skills = Find(card, "Skills")?.GetComponent<TMP_Text>();
                var head   = Find(card, "FormHead")?.GetComponent<TMP_Text>();
                var line   = Find(card, "FormLine")?.GetComponent<TMP_Text>();
                var foot   = Find(card, "Foot") as RectTransform;
                var stat3  = Find(card, "Stat3") as RectTransform;
                string nm = name != null ? name.text : "?";
                sb.AppendLine($"  [{cardName} · {nm}] 머리 그림 {banner?.sprite?.name} · 형 문장 {emblem?.sprite?.name} · 형 「{Plain(head)}」");
                Check($"{nm} — 머리 그림이 켜짐", banner != null && banner.enabled && banner.sprite != null);
                Check($"{nm} — 형 문장이 켜짐", emblem != null && emblem.enabled && emblem.sprite != null);
                Check($"{nm} — 이름 · 스킬 줄이 칸 안", Fits(name) && Fits(skills), $"이름 {name?.preferredWidth:0}/{name?.rectTransform.rect.width:0} · 스킬 {skills?.preferredWidth:0}/{skills?.rectTransform.rect.width:0}");
                Check($"{nm} — 형 머리 · 한 줄이 칸 안", Fits(head) && Fits(line, autosize: true), $"머리 {head?.preferredWidth:0}/{head?.rectTransform.rect.width:0}");
                Check($"{nm} — 형 문장과 형 글이 안 겹침", emblem != null && head != null && !Overlap(emblem.rectTransform, head.rectTransform) && !Overlap(emblem.rectTransform, line.rectTransform));
                Check($"{nm} — 수치 칸 · 바닥 글이 카드 안", Inside(stat3, card) && (foot == null || Inside(foot, card)));
            }

            Call(panel, "PickEvolveCard", 0);
            await UniTask.Delay(400, ignoreTimeScale: true, cancellationToken: ct);
            var cardB  = Find(evo.transform, "EvolveB");
            var underB = Find(evo.transform, "EvolveBUnder")?.GetComponent<Image>();
            Check("안 고른 카드가 옅어져도 배경이 비치지 않음(불투명 받침)",
                  underB != null && underB.gameObject.activeInHierarchy && underB.color.a >= 0.99f
                  && underB.transform.GetSiblingIndex() < cardB.GetSiblingIndex()
                  && Mathf.Abs(WorldRect((RectTransform)underB.transform).center.x - WorldRect((RectTransform)cardB).center.x) <= 1f,
                  $"카드 B 알파 {cardB?.GetComponent<CanvasGroup>()?.alpha:0.00}");
            await Shot("EVO26_2_Pick");
            Call(panel, "HideEvolvePanel");
            await UniTask.Delay(300, ignoreTimeScale: true, cancellationToken: ct);
        }
        catch (System.OperationCanceledException) { sb.AppendLine("취소"); }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            if (panel != null) Managers.UI.ClosePopupUI(panel);
            if (go != null) Object.Destroy(go);
            sb.AppendLine($"CHECK 합계 {pass}/{total}");
            File.WriteAllText("Temp/evo1026.txt", sb.ToString());
            Debug.Log($"{Tag} 끝\n" + sb);
        }
    }

    private static string Plain(TMP_Text t) => t == null ? "" : System.Text.RegularExpressions.Regex.Replace(t.text, "<[^>]+>", "");

    private static bool Fits(TMP_Text t, bool autosize = false)
    {
        if (t == null) return false;
        t.ForceMeshUpdate();
        return autosize ? !t.isTextOverflowing && t.textInfo.lineCount <= 1 : t.preferredWidth <= t.rectTransform.rect.width + 1f;
    }

    private static Rect WorldRect(RectTransform r)
    {
        var c = new Vector3[4]; r.GetWorldCorners(c);
        return Rect.MinMaxRect(c[0].x, c[0].y, c[2].x, c[2].y);
    }

    private static bool Overlap(RectTransform a, RectTransform b)
    {
        var ra = WorldRect(a); var rb = WorldRect(b);
        ra.xMin += 1f; ra.xMax -= 1f; ra.yMin += 1f; ra.yMax -= 1f;
        return ra.Overlaps(rb);
    }

    private static bool Inside(RectTransform a, RectTransform card)
    {
        if (a == null) return false;
        var ra = WorldRect(a); var rc = WorldRect(card);
        return ra.xMin >= rc.xMin - 1f && ra.xMax <= rc.xMax + 1f && ra.yMin >= rc.yMin - 1f && ra.yMax <= rc.yMax + 1f;
    }

    private static Transform Find(Transform root, string name)
    {
        if (root == null) return null;
        if (root.name == name) return root;
        foreach (Transform c in root) { var f = Find(c, name); if (f != null) return f; }
        return null;
    }

    private static T F<T>(UI_CruciblePanel panel, string name) where T : class
        => typeof(UI_CruciblePanel).GetField(name, Inst)?.GetValue(panel) as T;

    private static void Call(UI_CruciblePanel panel, string method, params object[] args)
        => typeof(UI_CruciblePanel).GetMethod(method, Inst)?.Invoke(panel, args.Length == 0 ? null : args);

    private static UniTask Shot(string name)
    {
        var m = typeof(UILayoutRuntimeProbeEditor).GetMethod("ShotAsync", BindingFlags.Static | BindingFlags.NonPublic);
        return m != null ? (UniTask)m.Invoke(null, new object[] { name, 0 }) : UniTask.CompletedTask;
    }
}
#endif
