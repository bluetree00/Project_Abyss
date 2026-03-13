//============================================================
// 네임스페이스 및 의존성
//============================================================
using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;
using Game.CharacterStates;
using Game.CharacterStates.PlayerControllerStates;

//============================================================
// CharacterBase 클래스
// 기본 플레이어 및 캐릭터 동작의 기반이 되는 컴포넌트 관리 클래스
// Rigidbody와 Animator 컴포넌트 접근과 초기화, 기본 업데이트 처리를 담당
//============================================================
public class CharacterBase : MonoBehaviour
{
    // 애니메이터 컴포넌트 (캐릭터 애니메이션 제어용)
    protected Animator anim;
    public Animator Anim => anim;  // 외부에서 접근 가능하도록 프로퍼티 제공

    // 리지드바디 컴포넌트 (물리 연산용)
    [SerializeField] private Rigidbody rb;
    public Rigidbody Rigid => rb;  // 외부에서 접근 가능하도록 프로퍼티 제공

    // 캐릭터 Transform 컴포넌트 (위치, 회전, 크기 제어용)
    public Transform playerTransform;

    // 리지드바디 회전 고정을 위해 각속도를 0으로 리셋하는 함수
    protected void FreezeRotation() => Rigid.angularVelocity = Vector3.zero;

    //============================================================
    // 초기화 (비동기)
    // Awake() 시점에서 비동기로 컴포넌트 초기화 수행
    //============================================================
    private async void Awake()
    {
        await InitAsync();
    }

    // 비동기 초기화 함수 (파생 클래스에서 재정의 가능)
    protected virtual async UniTask InitAsync()
    {
        InitCoreComponents();
        await UniTask.CompletedTask;  // 혹시 비동기 작업 추가시 대비
    }

    // 핵심 컴포넌트(Animator, Rigidbody, Transform) 초기화
    private void InitCoreComponents()
    {
        rb = GetComponent<Rigidbody>();
        anim = GetComponent<Animator>();
        playerTransform = transform;
    }

    //============================================================
    // 매 프레임 기본 업데이트 처리
    // 리지드바디의 회전이 물리적 영향으로 변경되는 것을 방지하기 위한 회전 고정 처리
    //============================================================
    protected virtual void Update()
    {
        FreezeRotation();
    }
}
