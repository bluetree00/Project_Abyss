using System;
using UnityEngine;

public enum DialogueSpeaker { None, God, Shadow, Lich }

[Serializable]
public class DialogueLine
{
    public DialogueSpeaker speaker;
    /// <summary>Addressable 키. 비어 있으면 현재 슬롯의 이전 스프라이트를 유지.</summary>
    public string illustrationKey;
    [TextArea(2, 5)]
    public string text;
}
