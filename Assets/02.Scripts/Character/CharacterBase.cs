//============================================================
// 📦 네임스페이스 및 의존성
//============================================================
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;
using Game.CharacterStates;
using Game.CharacterStates.PlayerCharacterStates;

public class CharacterBase : MonoBehaviour
{
 
    protected Animator anim;
    public Animator Anim => anim;


    [SerializeField] private Rigidbody rb;
    public Rigidbody Rigid => rb;

    public Transform playerTransform;


    private async void Awake()
    {
        await InitAsync();
    }

    protected virtual async Task InitAsync()
    {
        InitCoreComponents();
    }

    private void InitCoreComponents()
    {
        rb = GetComponent<Rigidbody>();
        anim = GetComponent<Animator>();
        playerTransform = transform;

    }
}