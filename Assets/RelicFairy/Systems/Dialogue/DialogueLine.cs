using System;
using UnityEngine;

// 정수 직렬화 순서 고정: 기존 값(Merlin=1, Shadow=2 …)을 보존해야 SO 폴백 에셋이 안 깨진다.
// God은 멀린의 의지였다 — 표시명만 "???"로 감춘다. 신규 화자는 반드시 뒤에 추가할 것.
public enum DialogueSpeaker { None, Merlin, Shadow, Lich, Mordred, Arthur, Knight, ForestGuardian, Dragon, DeathKnight }

[Serializable]
public class DialogueLine
{
    public DialogueSpeaker speaker;
    /// <summary>Addressable 키. 비어 있으면 현재 슬롯의 이전 스프라이트를 유지.</summary>
    public string illustrationKey;
    [TextArea(2, 5)]
    public string text;
}
