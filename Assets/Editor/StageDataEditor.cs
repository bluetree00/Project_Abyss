using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(StageData))]
public class StageDataEditor : Editor
{
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        StageData stageData = (StageData)target;

        if (stageData.chapters != null)
        {
            foreach (var chapter in stageData.chapters)
            {
                if (chapter.stages != null)
                {
                    for (int i = 0; i < chapter.stages.Count; i++)
                    {
                        var stage = chapter.stages[i];
                        // enum 값 범위 내에서만 자동 할당
                        if (stage != null && i < System.Enum.GetValues(typeof(StageData.StageName)).Length)
                        {
                            var expected = (StageData.StageName)i;
                            if (stage.stageName != expected)
                            {
                                stage.stageName = expected;
                                EditorUtility.SetDirty(stageData);
                            }
                        }
                    }
                }
            }
        }
    }
}
