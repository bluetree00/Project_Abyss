// using UnityEngine;
// using Game.CharacterStates.PlayerControllerStates;

// namespace Game.CharacterStates.VagabondStates
// {
//     // ──────────────────────────────
//     // ▶ IDLE / MOVE 상태
//     // ──────────────────────────────

//     // 기본 대기 상태
//     public class VagabondIdleState : IdleState<Vagabond>
//     {
//         public override void Enter(Vagabond owner)
//         {
//             base.Enter(owner);
//             owner.Anim.CrossFade("MoveBlend", 0.2f); // 이동 애니메이션 시작
//         }

//         public override void Execute(Vagabond owner) { }

//         public override void Exit(Vagabond owner)
//         {
//             base.Exit(owner);
//         }
//     }

//     // 이동 상태 (Blend Tree 사용)
//     public class VagabondMoveBlendState : State<Vagabond>
//     {
//         public override void Enter(Vagabond owner)
//         {
//             owner.Anim.CrossFade("MoveBlend", 0.1f); // 이동 애니메이션 시작
//         }

//         public override void Execute(Vagabond owner)
//         {
//             if (!owner.CanProcessInput()) return;

//             float moveAmount = owner.MoveDirection.magnitude;
//             float targetSpeed = moveAmount > 0 ? (Input.GetKey(KeyCode.LeftShift) ? 1f : 0.5f) : 0f;

//             // 애니메이션 Blend 파라미터 조절
//             owner.Anim.SetFloat("MoveSpeed", targetSpeed, 0.1f, Time.deltaTime);
//         }

//         public override void Exit(Vagabond owner)
//         {
//             owner.Anim.SetFloat("MoveSpeed", 0f); // 이동 종료 시 속도 초기화
//         }
//     }

//     // ──────────────────────────────
//     // ▶ 회피 상태
//     // ──────────────────────────────

//     public class VagabondDodgeState : AnimationState<Vagabond>
//     {
//         public override void Enter(Vagabond owner)
//         {
//             InitAnimation("Dodge", 0.6f); // 회피 애니메이션 초기화
//             base.Enter(owner);
//         }

//         protected override void OnAnimationEnd(Vagabond owner)
//         {
//             owner.StateMachine.ChangeState(owner.GetState<VagabondIdleState>());
//         }

//     }

//     // ──────────────────────────────
//     // ▶ 콤보 공격 상태
//     // ──────────────────────────────

//     public class VagabondComboAttackState : AnimationState<Vagabond>
//     {
//         private int comboIndex;
//         private bool blocksInput = true;
//         public override bool BlocksInput => blocksInput;

//         public void SetComboIndex(int index)
//         {
//             comboIndex = index;
//         }

//         public override void Enter(Vagabond owner)
//         {
//             blocksInput = true;

//             if (owner.weaponManagerSO.CurrentWeapon == null)
//             {
//                 Debug.LogError("무기가 없습니다. 콤보 공격 불가.");
//                 owner.StateMachine.ChangeState(owner.GetState<VagabondIdleState>());
//                 return;
//             }

//             var weapon = owner.weaponManagerSO.CurrentWeapon;
//             int index = Mathf.Clamp(comboIndex, 0, weapon.lightAttackAnimationSetSO.normalAttackAnimations.Length - 1);

//             // 콤보 애니메이션 및 종료 시간 설정
//             string animName = weapon.lightAttackAnimationSetSO.normalAttackAnimations[index];
//             float endTime = weapon.lightAttackAnimationSetSO.comboEndTimes[index];

            

//             InitAnimation(animName, endTime);

//             owner.RotateTowardsMousePosition();      // 캐릭터 방향 회전
//             owner.OnAttackAnimationStart();          // 공격 시작 처리
//             base.Enter(owner);
//         }

//         public override void Exit(Vagabond owner)
//         {
//             Debug.Log("콤보 공격 상태 종료");
//             owner.OnAttackAnimationEnd(); // 공격 종료 처리
//         }

//         protected override void OnAnimationEnd(Vagabond owner)
//         {
//             blocksInput = false;
//             owner.OnAttackAnimationEnd();
//             owner.StateMachine.ChangeState(owner.GetState<VagabondIdleState>());
//         }
//     }

//     // ──────────────────────────────
//     // ▶ 무기 변경 상태
//     // ──────────────────────────────

//     public class VagabondChangeWeaponState : AnimationState<Vagabond>
//     {
//         public override void Enter(Vagabond owner)
//         {
//             string anim = owner.weaponManagerSO.CurrentWeapon?.weapon_ChangeWeapon_AnimationName;

//             if (!string.IsNullOrEmpty(anim))
//             {
//                 InitAnimation(anim, 0.95f);
//                 base.Enter(owner);
//                 owner.StateMachine.ChangeState(new VagabondIdleState()); // 임시 처리
//             }
//             else
//             {
//                 Debug.LogWarning("무기 변경 애니메이션 이름이 비어있거나 무기가 없습니다.");
//                 owner.StateMachine.ChangeState(new VagabondIdleState());
//             }
//         }

//         protected override void OnAnimationEnd(Vagabond owner)
//         {
//             owner.StateMachine.ChangeState(new VagabondIdleState());
//         }
//     }

//     // ──────────────────────────────
//     // ▶ 스킬 / 궁극기 상태
//     // ──────────────────────────────

//     public class VagabondSkillState : AnimationState<Vagabond>
//     {
//         public override void Enter(Vagabond owner)
//         {
//             InitAnimation("NormalSkile_01"); // 일반 스킬
//             base.Enter(owner);
//         }

//         protected override void OnAnimationEnd(Vagabond owner)
//         {
//             owner.StateMachine.ChangeState(new VagabondIdleState());
//         }
//     }

//     public class VagabondUltimateState : AnimationState<Vagabond>
//     {
//         public override void Enter(Vagabond owner)
//         {
//             InitAnimation("UltimateSkile_01"); // 궁극기
//             base.Enter(owner);
//         }

//         protected override void OnAnimationEnd(Vagabond owner)
//         {
//             owner.StateMachine.ChangeState(new VagabondIdleState());
//         }
//     }

//     // ──────────────────────────────
//     // ▶ 강공격 (차지 공격) 관련 상태
//     // ──────────────────────────────

//     // 강공격 시작 (버튼 누름)
//     public class VagabondChargeStartState : AnimationState<Vagabond>
//     {
//         public override void Enter(Vagabond owner)
//         {

//             InitAnimation("Heavycharge", 0.3f);
//             base.Enter(owner);
//         }

//         protected override void OnAnimationEnd(Vagabond owner)
//         {
//             owner.StateMachine.ChangeState(owner.GetState<VagabondChargeHoldingState>());
//         }
//     }

//     // 강공격 유지 (버튼 누르고 있음)
//     public class VagabondChargeHoldingState : AnimationState<Vagabond>
//     {
//         private bool blocksInput = true;
//         public override bool BlocksInput => blocksInput;

//         public override void Enter(Vagabond owner)
//         {

//             InitAnimation("Heavycharge", 0.3f);
//             base.Enter(owner);
//         }

//         public override void Execute(Vagabond owner)
//         {
//             if (owner.CharacterData.heavyAttackChargeTime >= owner.CharacterData.heavyAttackChargeThreshold)
//             {
//                 owner.StateMachine.ChangeState(owner.GetState<VagabondChargedAttackState>());
//             }
//         }

//         public override void Exit(Vagabond owner)
//         {
//             blocksInput = false;

//         }

//         protected override void OnAnimationEnd(Vagabond owner)
//         {
//             throw new System.NotImplementedException(); // 애니메이션 끝나는 일이 없음
//         }
//     }

//     // 차지 공격 실행
//     public class VagabondChargedAttackState : AnimationState<Vagabond>
//     {
//         private bool blocksInput = true;
//         public override bool BlocksInput => blocksInput;

//         public override void Enter(Vagabond owner)
//         {
//             blocksInput = true;
//             owner.OnAttackAnimationStart();

//             var weapon = owner.weaponManagerSO.CurrentWeapon;
//             string animName = weapon?.heavyAttackSet?.attackClip?.name ?? "HeavyAttack";

//             InitAnimation(animName, 0.7f);
//             base.Enter(owner);
//         }

//         public override void Execute(Vagabond owner)
//         {
//             base.Execute(owner);

//             if (owner.ShouldCancelAttack())
//             {
//                 owner.CancelHeavyAttack();
//             }
//         }

//         protected override void OnAnimationEnd(Vagabond owner)
//         {
//             blocksInput = false;
//             owner.HeavyAttackAbility.HeavyAttackCancelCharging(owner);

//             if (owner.StateMachine.CurrentState is VagabondChargedAttackState)
//             {
//                 owner.StateMachine.ChangeState(owner.GetState<VagabondIdleState>());
//             }

//             owner.OnAttackAnimationEnd();
//         }
//     }

//     // 강공격 취소 (차지 도중 중단)
//     public class VagabondChargeCancelState : AnimationState<Vagabond>
//     {
//         public override void Enter(Vagabond owner)
//         {
//             owner.HeavyAttackAbility.HeavyAttackCancelCharging(owner);
//             base.Enter(owner);
//         }

//         protected override void OnAnimationEnd(Vagabond owner)
//         {
//             owner.OnAttackAnimationEnd();
//             owner.StateMachine.ChangeState(owner.GetState<VagabondIdleState>());
//         }
//     }
// }
