using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

//============================================================
// CharacterBase — 캐릭터 컴포넌트의 기반 (직접 부착하지 않는다)
//
// Rigidbody·Animator 캐싱과 "비동기 초기화" 골격(Template Method)을 제공한다.
// 파생 클래스는 InitAsync를 재정의해 base.InitAsync 뒤에 자기 초기화를 잇는다.
//============================================================
public abstract class CharacterBase : MonoBehaviour
{
    // ── SerializeField ────────────────────────────────────────────
    // 리지드바디 컴포넌트 (물리 연산용). 초기화 시 같은 오브젝트의 컴포넌트로 다시 잡는다.
    [SerializeField] private Rigidbody rb;

    // ── Private ───────────────────────────────────────────────────
    // 애니메이터 컴포넌트 (캐릭터 애니메이션 제어용)
    protected Animator anim;

    // ── Properties ────────────────────────────────────────────────
    public Animator  Anim  => anim;
    public Rigidbody Rigid => rb;

    // ── Lifecycle ─────────────────────────────────────────────────
    // Awake에서 비동기 초기화를 시작한다. 첫 await 전까지는 Awake 안에서 동기로 실행된다.
    private void Awake()
    {
        // enabled=false는 전시(Display) 목적으로 비활성화된 인스턴스 — 초기화 불필요
        if (!enabled) return;
        RunInitAsync(destroyCancellationToken).Forget();
    }

    // ── Protected Methods ─────────────────────────────────────────
    /// <summary>비동기 초기화 훅. 파생 클래스는 base 호출 뒤에 자기 초기화를 잇는다.</summary>
    protected virtual UniTask InitAsync(CancellationToken ct)
    {
        CacheCoreComponents();
        return UniTask.CompletedTask;
    }

    /// <summary>리지드바디 회전 고정을 위해 각속도를 0으로 리셋한다.</summary>
    protected void FreezeRotation()
    {
        if (Rigid == null || Rigid.isKinematic) return;
        Rigid.angularVelocity = Vector3.zero;
    }

    // ── Private Methods ───────────────────────────────────────────
    private async UniTaskVoid RunInitAsync(CancellationToken ct)
    {
        try
        {
            await InitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            // 초기화 도중 파괴 — 정상 종료
        }
    }

    // 핵심 컴포넌트(Animator, Rigidbody) 캐싱
    private void CacheCoreComponents()
    {
        rb   = GetComponent<Rigidbody>();
        anim = GetComponent<Animator>();
    }
}
