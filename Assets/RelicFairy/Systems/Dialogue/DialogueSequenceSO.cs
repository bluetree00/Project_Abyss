using UnityEngine;

[CreateAssetMenu(menuName = "RelicFairy/Dialogue/Sequence", fileName = "Dialogue_")]
public class DialogueSequenceSO : ScriptableObject
{
    [SerializeField] private DialogueLine[] _lines;
    public DialogueLine[] Lines => _lines;
}
