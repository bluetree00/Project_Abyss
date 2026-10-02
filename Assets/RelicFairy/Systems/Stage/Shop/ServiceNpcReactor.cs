using System;
using UnityEngine;

/// <summary>
/// 서비스 NPC(행상 · 대장장이 · 정제사)의 반응 — 다가오면 알아보고 돌아보며, 말을 걸면 응대하고,
/// 거래하고 떠나면 반기고(안 했으면 시큰둥), 멀어지면 제 일로 돌아간다.
///
/// 몸이 애니메이터면 상태 이름으로 넘긴다(비운 칸 · 없는 상태는 몸짓 없이 지나간다). 한 번 하는 몸짓은
/// 끝나면 스스로 대기 상태로 돌아온다 — 컨트롤러에 되돌림 전이를 그릴 필요가 없다.
/// 몸이 애니메이터가 아닌 NPC(수정 정령)는 <see cref="Reacted"/>를 받아 스스로 움직인다.
/// 방 컨트롤러는 패널을 열 때 <see cref="BeginTalk"/>, 닫을 때 <see cref="EndTalk"/>만 부른다.
/// </summary>
[DisallowMultipleComponent]
public sealed class ServiceNpcReactor : MonoBehaviour
{
    public enum Reaction { Notice, Talk, Thanks, Shrug, Farewell, Work }

    // ── Constants ───────────────────────────────────────────
    private const string BodyName        = "Body";   // NPC 프리팹 규약 — 몸(모델)은 루트의 자식 「Body」
    private const float CrossFadeSeconds = 0.18f;
    private const float OneShotEnd       = 0.92f;   // 한 번 하는 몸짓이 이만큼 지나면 대기로 돌린다

    // ── [SerializeField] ────────────────────────────────────
    [Header("거리(m) — 알아보기 · 떠났다고 보기")]
    [SerializeField] private float noticeRange = 6f;
    [SerializeField] private float leaveRange  = 8.5f;

    [Header("돌아보기")]
    [Tooltip("몸(자식 「Body」)만 돌린다 — 대장장이처럼 모루는 제자리여야 할 때. 끄면 NPC 전체가 돈다.")]
    [SerializeField] private bool  turnBodyOnly;
    [SerializeField] private float turnSpeed = 200f;   // °/s
    [SerializeField] private float maxTurn   = 70f;    // 처음 방향(카운터 쪽)에서 이 각도까지만 돌아본다

    [Header("애니메이터 상태 — 비우면 그 반응은 몸짓 없이")]
    [SerializeField] private string idleState;
    [SerializeField] private string noticeState;
    [SerializeField] private string talkState;
    [SerializeField] private string thanksState;
    [SerializeField] private string shrugState;
    [SerializeField] private string farewellState;
    [SerializeField] private string workState;

    [Header("혼자 있을 때 하는 일 — 0이면 없음")]
    [SerializeField] private float workInterval;

    // ── Private ─────────────────────────────────────────────
    private Animator   _anim;
    private Transform  _turnRoot;
    private Transform  _player;
    private Quaternion _home;
    private bool       _near;
    private bool       _talking;
    private float      _workTimer;
    private int        _oneShotHash;   // 지금 재생 중인 한 번 하는 몸짓(없으면 0)

    // ── Properties ──────────────────────────────────────────
    /// <summary>반응이 일어날 때마다 — 애니메이터가 아닌 몸(수정 정령)·불꽃 같은 곁들이 연출이 받는다.</summary>
    public event Action<Reaction> Reacted;
    public bool IsNear    => _near;
    public bool IsTalking => _talking;

    // ── Lifecycle ───────────────────────────────────────────
    private void Awake()
    {
        _anim = GetComponentInChildren<Animator>();
        _turnRoot = turnBodyOnly ? transform.Find(BodyName) : null;
        if (_turnRoot == null) _turnRoot = transform;
    }

    private void Start()
    {
        _home      = _turnRoot.rotation;
        _player    = Managers.Player != null ? Managers.Player.PlayerTransform : null;
        _workTimer = workInterval * 0.5f;
        CrossFade(idleState);
    }

    private void Update()
    {
        if (_player == null)
        {
            _player = Managers.Player != null ? Managers.Player.PlayerTransform : null;
            if (_player == null) return;
        }

        Vector3 to = _player.position - _turnRoot.position;
        to.y = 0f;
        float d2 = to.sqrMagnitude;

        if (!_near && d2 < noticeRange * noticeRange)
        {
            _near = true;
            Fire(Reaction.Notice, noticeState);
        }
        else if (_near && !_talking && d2 > leaveRange * leaveRange)
        {
            _near = false;
            _workTimer = workInterval * 0.6f;
            Fire(Reaction.Farewell, farewellState);
        }

        Turn(_near || _talking ? to : Vector3.zero);
        TickOneShot();

        if (_near || _talking || workInterval <= 0f || _oneShotHash != 0) return;
        _workTimer -= Time.deltaTime;
        if (_workTimer > 0f) return;
        _workTimer = workInterval;
        Fire(Reaction.Work, workState);
    }

    // ── Public Methods ──────────────────────────────────────
    /// <summary>패널이 열릴 때(말을 걸었을 때).</summary>
    public void BeginTalk()
    {
        _talking = true;
        _near    = true;
        Fire(Reaction.Talk, talkState);
    }

    /// <summary>패널이 닫힐 때 — 거래를 했으면 반기고, 안 했으면 시큰둥하게.</summary>
    public void EndTalk(bool transacted)
    {
        _talking = false;
        if (transacted) Fire(Reaction.Thanks, thanksState);
        else            Fire(Reaction.Shrug, shrugState);
    }

    // ── Private Methods ─────────────────────────────────────
    private void Fire(Reaction r, string state)
    {
        Reacted?.Invoke(r);
        if (string.IsNullOrEmpty(state) || state == idleState) return;
        if (CrossFade(state)) _oneShotHash = Animator.StringToHash(state);
    }

    private bool CrossFade(string state)
    {
        if (_anim == null || string.IsNullOrEmpty(state)) return false;
        int hash = Animator.StringToHash(state);
        if (!_anim.HasState(0, hash)) return false;
        _anim.CrossFadeInFixedTime(hash, CrossFadeSeconds, 0);
        return true;
    }

    /// <summary>한 번 하는 몸짓이 끝나 가면 대기로 — 전이 없이 상태만 둔 컨트롤러를 쓰기 위해.</summary>
    private void TickOneShot()
    {
        if (_oneShotHash == 0 || _anim == null || _anim.IsInTransition(0)) return;
        var info = _anim.GetCurrentAnimatorStateInfo(0);
        if (info.shortNameHash != _oneShotHash) return;
        if (info.normalizedTime < OneShotEnd) return;
        _oneShotHash = 0;
        CrossFade(idleState);
    }

    /// <summary>플레이어 쪽으로 돌아본다(처음 방향에서 <see cref="maxTurn"/>까지) — 멀어지면 처음 방향으로.</summary>
    private void Turn(Vector3 toPlayer)
    {
        Quaternion target = _home;
        if (toPlayer.sqrMagnitude > 0.01f)
        {
            Quaternion look = Quaternion.LookRotation(toPlayer.normalized);
            float off = Quaternion.Angle(_home, look);
            target = off <= maxTurn ? look : Quaternion.Slerp(_home, look, maxTurn / off);
        }
        _turnRoot.rotation = Quaternion.RotateTowards(_turnRoot.rotation, target, turnSpeed * Time.deltaTime);
    }
}
