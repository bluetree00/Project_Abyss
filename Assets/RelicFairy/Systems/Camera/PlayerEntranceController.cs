using System;
using UnityEngine;
using Cysharp.Threading.Tasks;
using System.Threading;

/// <summary>
/// 씬 진입 시 플레이어 등장 연출 오케스트레이터.
/// 카메라 인트로 완료를 기다렸다가, 캐릭터별 <see cref="PlayerEntranceBehaviourSO"/>에 연출을 위임한다.
/// 플레이어 상태(inputReady, 물리, 렌더러 가시성)는 이 컨트롤러가 관리한다.
/// </summary>
public sealed class PlayerEntranceController : MonoBehaviour
{
    // ── Private ──
    private PlayerController _player;
    private PlayerEntranceBehaviourSO _behaviour;
    private CancellationTokenSource _cts;
    private bool _initialized;
    private bool _entrancePlayed;

    // ── Events ──
    /// <summary>등장 연출이 완전히 끝난 직후 발생 (inputReady 활성화 후)</summary>
    public event Action OnEntranceComplete;

    // ── Public Methods ──

    /// <summary>
    /// GameRunBootstrapper에서 호출. 플레이어를 숨기고 카메라 인트로 대기 상태로 준비.
    /// </summary>
    /// <param name="player">등장할 플레이어</param>
    /// <param name="behaviour">연출 SO. null이면 연출 없이 즉시 활성화</param>
    public void Initialize(PlayerController player, PlayerEntranceBehaviourSO behaviour)
    {
        if (_initialized) return;
        _initialized = true;

        _player = player;
        _behaviour = behaviour;

        // SO 없으면 즉시 활성화 상태 유지 (연출 스킵)
        if (_behaviour == null)
        {
            Debug.Log("[PlayerEntranceController] 등장 연출 SO 없음 — 즉시 활성화");
            return;
        }

        // 플레이어 비활성 (입력 차단 + 렌더러 숨김)
        _player.inputReady = false;
        SetPlayerVisible(false);

        // 물리 비활성 (연출 중 중력 간섭 방지)
        if (_player.Rigid != null)
            _player.Rigid.isKinematic = true;

        // 카메라 인트로 완료 구독
        var cam = GameCameraController.Instance;
        if (cam != null)
            cam.OnIntroComplete += OnCameraIntroComplete;
    }

    // ── Lifecycle ──

    private void OnDestroy()
    {
        _cts?.Cancel();
        _cts?.Dispose();

        var cam = GameCameraController.Instance;
        if (cam != null)
            cam.OnIntroComplete -= OnCameraIntroComplete;
    }

    // ── Event Handlers ──

    private void OnCameraIntroComplete()
    {
        var cam = GameCameraController.Instance;
        if (cam != null)
            cam.OnIntroComplete -= OnCameraIntroComplete;

        if (_entrancePlayed) return;
        _entrancePlayed = true;

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);

        PlayEntranceAsync(_cts.Token).Forget();
    }

    // ── Private Methods ──

    private async UniTaskVoid PlayEntranceAsync(CancellationToken ct)
    {
        try
        {
            if (_player == null || _behaviour == null) return;

            Vector3 spawnPos = _player.transform.position;

            // 템플릿 메서드에 연출 위임 (플레이어 노출 타이밍은 컨트롤러가 콜백으로 제공)
            await _behaviour.ExecuteAsync(_player, spawnPos, SetPlayerVisible, ct);

            // 플레이어 조작 활성화
            if (_player != null)
            {
                if (_player.Rigid != null)
                    _player.Rigid.isKinematic = false;
                _player.inputReady = true;
            }

            OnEntranceComplete?.Invoke();
            Debug.Log("[PlayerEntranceController] 등장 연출 완료");
        }
        catch (OperationCanceledException)
        {
            ForceActivatePlayer();
        }
    }

    /// <summary>연출 중단 시 플레이어를 강제 활성화 (안전장치)</summary>
    private void ForceActivatePlayer()
    {
        if (_player == null) return;
        SetPlayerVisible(true);
        if (_player.Rigid != null)
            _player.Rigid.isKinematic = false;
        _player.inputReady = true;
    }

    private void SetPlayerVisible(bool visible)
    {
        if (_player == null) return;
        var renderers = _player.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
            renderers[i].enabled = visible;
    }
}
