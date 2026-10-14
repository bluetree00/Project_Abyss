#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using RelicFairy.Monster;
using UnityEngine;

/// <summary>
/// [실측 도구 · 반응 시뮬이 매 프레임 부른다] 보스 연출 점검(10-06 사용자 「데미지 · 수치보다 판정과 연출 변화 — 페이지 연출 · 지연,
/// 공격 뒤 딜레이 · 연출 자연스러움」). 수치가 아니라 「어떻게 보이고 이어지는가」를 센다:
///   ① 페이지 전환 타임라인 — 무적 · 컷신 HUD · 체력바 페이지가 바뀌는 순간 · 전환 동안 FSM 흐름 · 무적이 풀린 뒤 첫 공격까지(전환 사진 1초 간격)
///   ② 패턴 뒤 공백 — 패턴이 끝나고 다음 패턴까지 걸린 시간 · 그 사이 상태(쉼 없이 잇따르는 것 · 오래 서 있는 것)
///   ③ 블렌드 없는 애니 전환 — 섞임(크로스페이드) 없이 뚝 바뀐 상태 쌍(처음 본 쌍은 사진)
///   ④ 순간 이동 — 한 프레임에 크게 튄 위치(연출된 순간이동 패턴인지 사람이 가른다)
///   ⑤ 굳은 자세 — 루프 아닌 모션이 끝 자세로 1초 넘게 그대로(전환 · 패턴 대기 중 멈춘 듯 보인다)
/// 결과: 시뮬 폴더의 presentation.txt + pres_*.png.
/// </summary>
internal sealed class BossPresentationProbe
{
    private const float SnapDistance   = 1.5f;    // 한 프레임 수평 이동이 이보다 크고
    private const float SnapSpeed      = 30f;     // 속도가 이보다 빠르면 「튐」
    private const float ChainGap       = 0.3f;    // 패턴 사이가 이보다 짧으면 「쉼 없이」
    private const float LongIdle       = 5f;      // 이보다 길면 「오래 서 있음」
    private const int   MaxCutShots    = 6;
    private const float FrozenHold     = 1f;      // 루프 아닌 모션이 끝 자세로 이보다 오래 서 있으면 「굳음」
    private const float HitchDt        = 0.08f;   // 이보다 긴 프레임이 끼면 끊김 · 튐 판정을 보류(시뮬 스크린샷 · 첫 이펙트 생성 순간)
    private static readonly float[] PageShotAt = { 0f, 1f, 2f, 3f, 5f, 8f };
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    private readonly MonsterBase    _boss;
    private readonly IBossHudSource _hud;
    private readonly Animator       _anim;
    private readonly string         _dir;
    private readonly FieldInfo      _fsmField;
    private readonly Dictionary<int, string> _names = new();
    private readonly List<(float t, string kind, string text)> _events = new();

    // ── 이전 프레임
    private Vector3 _lastPos;
    private bool    _hasLast;
    private int     _lastCur, _lastNext;
    private float   _lastLen;
    private bool    _lastTrans;
    private string  _lastFsm = "";
    private bool    _lastSpecial, _lastInvuln, _lastCut;
    private int     _lastPage;

    // ── 패턴 공백
    private float  _patternEndAt = -1f;
    private string _patternEndName = "";
    private string _curPatternName = "-";
    private readonly List<string> _gapStates = new();
    private readonly List<(string from, string to, float gap, string states)> _gaps = new();
    private readonly List<(float t, string name)> _patternStarts = new();
    private float _startAt = -1f;   // 이번 패턴이 시작한 시각 — 시뮬의 패턴 이름은 0.1초 늦게 따라온다
    private string _startSeenName = "";
    private bool   _gapAtStart;     // 이번 시작에서 공백 기록을 하나 남겼는가(이름을 고쳐 쓸 대상)

    // ── 끊김 · 튐
    private readonly Dictionary<string, int> _cutPairs = new();
    private readonly Dictionary<string, string> _cutFirst = new();
    private readonly List<string> _snaps = new();
    private int _cutShots;
    private float _prevDt;
    private readonly Dictionary<string, int> _cutHitch = new();

    // ── 굳은 자세
    private int    _heldHash;
    private float  _heldSince = -1f;
    private string _heldCtx = "";
    private float  _lastT;
    private readonly List<string> _frozen = new();

    // ── 사진 예약(실시간)
    private readonly List<(float at, string file)> _shots = new();

    public BossPresentationProbe(MonsterBase boss, string dir)
    {
        _boss = boss;
        _hud  = boss as IBossHudSource;
        _dir  = dir;
        _fsmField = typeof(MonsterBase).GetField("_fsm", Inst);
        _anim = boss.GetComponentsInChildren<Animator>(true).FirstOrDefault(a => a.runtimeAnimatorController != null);
        BuildNames();
        _lastPage = _hud != null ? _hud.HudPage : 1;
    }

    /// <summary>매 프레임. <paramref name="t"/> = 전투 시각(초), <paramref name="pattern"/> = 시뮬이 본 지금 패턴.</summary>
    public void Tick(float t, string pattern, bool inCutscene)
    {
        if (_boss == null || !_boss.gameObject.activeInHierarchy) return;
        _lastT = t;
        string fsm = (_fsmField?.GetValue(_boss) as MonsterFSM)?.CurrentType?.Name ?? "?";
        bool special = _boss.IsInSpecialState;
        bool invuln  = _hud != null ? _hud.HudInvulnerable : _boss.IsDamageImmuneNow;
        int  page    = _hud != null ? _hud.HudPage : 1;

        // FSM 흐름 · 무적 · 컷신 · 페이지
        if (fsm != _lastFsm) { _events.Add((t, "상태", $"{_lastFsm} → {fsm}")); if (_patternEndAt >= 0f && !special) _gapStates.Add(fsm); _lastFsm = fsm; }
        if (invuln != _lastInvuln) { _events.Add((t, "무적", invuln ? "켜짐" : "꺼짐")); _lastInvuln = invuln; }
        if (inCutscene != _lastCut) { _events.Add((t, "컷신", inCutscene ? "시작" : "끝")); _lastCut = inCutscene; }
        if (page != _lastPage)
        {
            _events.Add((t, "페이지", $"{_lastPage} → {page}"));
            foreach (float d in PageShotAt) _shots.Add((Time.realtimeSinceStartup + d, $"pres_page{page}_{d:0}s.png"));
            _lastPage = page;
        }

        // 시뮬의 패턴 이름은 논리 틱(0.1초)마다 바뀐다 — 시작 0.4초 안에 새 이름이 오면 시작 · 공백 기록을 고쳐 쓴다
        if (special && _startAt >= 0f && t - _startAt < 0.4f && pattern != _startSeenName)
        {
            _startSeenName = pattern;
            _curPatternName = pattern;
            if (_patternStarts.Count > 0) _patternStarts[_patternStarts.Count - 1] = (_patternStarts[^1].t, pattern);
            if (_gapAtStart && _gaps.Count > 0) _gaps[^1] = (_gaps[^1].from, pattern, _gaps[^1].gap, _gaps[^1].states);
        }

        // 패턴 공백 — 특수 상태(패턴) 끝 → 다음 패턴 시작
        if (special && !_lastSpecial)
        {
            _gapAtStart = _patternEndAt >= 0f;
            if (_gapAtStart)
                _gaps.Add((_patternEndName, pattern, t - _patternEndAt, string.Join(">", _gapStates.Distinct())));
            _curPatternName = pattern;
            _patternStarts.Add((t, pattern));
            _startAt = t;
            _startSeenName = pattern;
            _patternEndAt = -1f;
            _gapStates.Clear();
        }
        else if (!special && _lastSpecial)
        {
            _patternEndAt   = t;
            _patternEndName = _curPatternName;
            _gapStates.Clear();
            _gapStates.Add(fsm);
        }
        _lastSpecial = special;

        // 블렌드 없는 애니 전환 — 지난 프레임의 「다음 상태」가 아닌 것으로 바로 바뀌었다
        if (_anim != null && _anim.isActiveAndEnabled)
        {
            var cur = _anim.GetCurrentAnimatorStateInfo(0);
            bool trans = _anim.IsInTransition(0);
            if (_lastCur != 0 && cur.fullPathHash != _lastCur && cur.fullPathHash != _lastNext)
            {
                string pair = $"{Name(_lastCur)} → {Name(cur.fullPathHash)}";
                if (Mathf.Max(Time.deltaTime, _prevDt) > HitchDt)
                {
                    _cutHitch.TryGetValue(pair, out int h);
                    _cutHitch[pair] = h + 1;
                }
                else
                {
                    _cutPairs.TryGetValue(pair, out int n);
                    _cutPairs[pair] = n + 1;
                    if (n == 0)
                    {
                        _cutFirst[pair] = $"{t:0.0}초 · 패턴 {pattern} · FSM {fsm} · 앞 상태 길이 {_lastLen:0.00}초 · 직전 프레임 전환 중 {(_lastTrans ? "예" : "아니오")} · dt {Time.deltaTime:0.000}/{_prevDt:0.000} · 애니 속도 {_anim.speed:0.00}";
                        if (_cutShots < MaxCutShots) { _cutShots++; _shots.Add((Time.realtimeSinceStartup + 0.05f, $"pres_cut{_cutShots}_{Safe(Name(cur.fullPathHash))}.png")); }
                    }
                }
            }
            // 굳은 자세 — 루프 아닌 상태가 끝(normalizedTime ≥ 1)에 머문다(시간 정지 중엔 세지 않는다)
            bool held = !trans && !cur.loop && cur.normalizedTime >= 1f && Time.timeScale > 0.01f;
            if (!(held && _heldSince >= 0f && cur.fullPathHash == _heldHash))
            {
                CloseHold(t);
                if (held) { _heldHash = cur.fullPathHash; _heldSince = t; _heldCtx = $"패턴 {pattern} · FSM {fsm}{(inCutscene ? " · 컷신 중" : "")}"; }
            }

            _lastCur  = cur.fullPathHash;
            _lastNext = trans ? _anim.GetNextAnimatorStateInfo(0).fullPathHash : 0;
            _lastLen  = cur.length;
            _lastTrans = trans;
        }

        // 순간 이동 — 한 프레임 수평으로 크게 튐
        Vector3 p = _boss.transform.position;
        if (_hasLast && Time.deltaTime > 0f)
        {
            Vector3 d = p - _lastPos; d.y = 0f;
            float m = d.magnitude;
            float span = Mathf.Max(Time.deltaTime, _prevDt);   // 이번 이동은 앞 프레임 dt로 일어났다(도구는 Update 앞에서 읽는다)
            if (m > SnapDistance && m / span > SnapSpeed)
                _snaps.Add($"{t:0.0}초 · {m:0.0} m · 패턴 {pattern} · FSM {fsm} · 애니 {Name(_lastCur)} · dt {Time.deltaTime:0.000}/{_prevDt:0.000} · 배속 {Time.timeScale:0.00}");
        }
        _lastPos = p; _hasLast = true;
        _prevDt = Time.deltaTime;

        // 예약 사진
        for (int i = _shots.Count - 1; i >= 0; i--)
        {
            if (Time.realtimeSinceStartup < _shots[i].at) continue;
            ScreenCapture.CaptureScreenshot(Path.Combine(_dir, _shots[i].file));
            _shots.RemoveAt(i);
        }
    }

    public void Write()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"연출 점검 — {(_boss != null ? _boss.name : "?")} · 애니 상태 이름 {_names.Count}개 읽음");

        // ① 페이지 전환
        sb.AppendLine("\n── ① 페이지 전환(페이지가 바뀐 순간 앞 10초 ~ 뒤 20초의 흐름 · 사진 pres_page*.png)");
        var pages = _events.Where(e => e.kind == "페이지").ToList();
        if (pages.Count == 0) sb.AppendLine("  페이지 전환 없음");
        foreach (var pg in pages)
        {
            float tp = pg.t;
            sb.AppendLine($"  [{pg.text}] {tp:0.0}초");
            float invOn = _events.Where(e => e.kind == "무적" && e.text == "켜짐" && e.t <= tp + 0.01f && e.t >= tp - 10f).Select(e => e.t).DefaultIfEmpty(-1f).Max();
            float invOff = _events.Where(e => e.kind == "무적" && e.text == "꺼짐" && e.t >= tp).Select(e => e.t).DefaultIfEmpty(-1f).Min();
            float from = invOff >= 0f ? invOff : tp;
            var first = _patternStarts.FirstOrDefault(x => x.t >= from);
            float firstAtk = first.name != null ? first.t : -1f;
            foreach (var e in _events.Where(e => e.t >= tp - 10f && e.t <= tp + 20f && e.kind != "페이지"))
                sb.AppendLine($"     {e.t,6:0.0}  {e.kind,-3} {e.text}");
            sb.AppendLine($"     → 무적 {(invOn >= 0f ? $"{invOn:0.0}초" : "없음")} ~ {(invOff >= 0f ? $"{invOff:0.0}초" : "안 풀림")}" +
                          $" · 무적 시간 {(invOn >= 0f && invOff >= 0f ? $"{invOff - invOn:0.0}초" : "-")}" +
                          $" · 무적이 풀리고 첫 공격까지 {(firstAtk >= 0f && invOff >= 0f ? $"{firstAtk - invOff:0.0}초({first.name})" : "-")}");
        }

        // ② 패턴 뒤 공백
        sb.AppendLine("\n── ② 패턴 뒤 공백(끝난 패턴 → 다음 패턴까지 · 사이 상태)");
        foreach (var g in _gaps.GroupBy(x => x.from).OrderBy(x => x.Key))
        {
            var list = g.ToList();
            int chain = list.Count(x => x.gap < ChainGap), idle = list.Count(x => x.gap > LongIdle);
            string mid = list.GroupBy(x => x.states).OrderByDescending(x => x.Count()).First().Key;
            sb.AppendLine($"  {g.Key,-38} {list.Count,2}회 · 평균 {list.Average(x => x.gap):0.0}초 · 최소 {list.Min(x => x.gap):0.0} · 최대 {list.Max(x => x.gap):0.0}" +
                          $"{(chain > 0 ? $" · ⚠쉼 없이 {chain}" : "")}{(idle > 0 ? $" · ⚠오래 섬 {idle}" : "")} · 사이 {mid}");
        }

        // ③ 블렌드 없는 전환
        sb.AppendLine("\n── ③ 블렌드 없이 바뀐 애니(섞임 없이 뚝 바뀜 · 처음 본 것만 사진 pres_cut*.png)");
        if (_cutPairs.Count == 0) sb.AppendLine("  없음");
        foreach (var kv in _cutPairs.OrderByDescending(x => x.Value))
            sb.AppendLine($"  {kv.Value,3}회  {kv.Key}   (처음 {_cutFirst[kv.Key]})");
        if (_cutHitch.Count > 0)
            sb.AppendLine($"  (프레임 지연 {HitchDt:0.00}초 넘는 순간이라 판정 보류: {string.Join(" · ", _cutHitch.Select(kv => $"{kv.Key} {kv.Value}회"))})");

        // ④ 순간 이동
        sb.AppendLine("\n── ④ 한 프레임에 튄 위치(연출된 순간이동 패턴인지 가린다)");
        if (_snaps.Count == 0) sb.AppendLine("  없음");
        foreach (var s in _snaps.Take(40)) sb.AppendLine("  " + s);

        // ⑤ 굳은 자세
        CloseHold(_lastT);
        sb.AppendLine($"\n── ⑤ 끝 자세로 굳은 애니(루프 아닌 모션이 끝나고 {FrozenHold:0}초 넘게 그대로)");
        if (_frozen.Count == 0) sb.AppendLine("  없음");
        foreach (var s in _frozen.Take(40)) sb.AppendLine("  " + s);

        File.WriteAllText(Path.Combine(_dir, "presentation.txt"), sb.ToString(), Encoding.UTF8);
        Debug.Log("[FightSim] 연출 점검 — presentation.txt");
    }

    private void BuildNames()
    {
        if (_anim == null) return;
        var rac = _anim.runtimeAnimatorController;
        if (rac is AnimatorOverrideController aoc) rac = aoc.runtimeAnimatorController;
        if (rac is not UnityEditor.Animations.AnimatorController ac || ac.layers.Length == 0) return;
        Walk(ac.layers[0].stateMachine, ac.layers[0].name);
    }

    private void Walk(UnityEditor.Animations.AnimatorStateMachine sm, string path)
    {
        foreach (var s in sm.states)
        {
            _names[Animator.StringToHash(path + "." + s.state.name)] = s.state.name;
            _names[s.state.nameHash] = s.state.name;
        }
        foreach (var c in sm.stateMachines) Walk(c.stateMachine, path + "." + c.stateMachine.name);
    }

    private void CloseHold(float t)
    {
        if (_heldSince >= 0f && t - _heldSince >= FrozenHold)
            _frozen.Add($"{_heldSince:0.0}초부터 {t - _heldSince:0.0}초 · {Name(_heldHash)} · {_heldCtx}");
        _heldSince = -1f;
    }

    private string Name(int hash) => _names.TryGetValue(hash, out var n) ? n : hash.ToString();

    private static string Safe(string s)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s.Replace(' ', '_');
    }
}
#endif
