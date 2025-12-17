using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(ShapeData), false)]
[CanEditMultipleObjects]
[System.Serializable]
public class ShapeDataDrawer : Editor
{
    private ShapeData ShapeDataInstance => target as ShapeData;

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        // 1. "Clear Board" 버튼
        if (GUILayout.Button("Clear Board"))
        {
            ShapeDataInstance.Clear();
            Debug.Log("Board cleared.");
        }

        EditorGUILayout.Space();

        // 2. 행(row) 및 열(column) 입력
        DrawColumnsInputFields();

        EditorGUILayout.Space();

        // 3. 보드 테이블 그리기
        if (ShapeDataInstance.board != null && ShapeDataInstance.columns > 0 && ShapeDataInstance.rows > 0)
        {
            DrawBoardTable();
        }

        serializedObject.ApplyModifiedProperties();

        if (GUI.changed)
        {
            EditorUtility.SetDirty(ShapeDataInstance);
        }
    }

    private void DrawColumnsInputFields()
    {
        var columnsTemp = ShapeDataInstance.columns;
        var rowsTemp = ShapeDataInstance.rows;

        ShapeDataInstance.columns = Mathf.Max(0, EditorGUILayout.IntField("Columns", ShapeDataInstance.columns));
        ShapeDataInstance.rows = Mathf.Max(0, EditorGUILayout.IntField("Rows", ShapeDataInstance.rows));

        // 보드 크기가 변경되었으면 새로 생성
        if ((ShapeDataInstance.columns != columnsTemp || ShapeDataInstance.rows != rowsTemp) &&
            ShapeDataInstance.columns > 0 && ShapeDataInstance.rows > 0)
        {
            ShapeDataInstance.CreateNewBoard();
            Debug.Log($"New board created with {ShapeDataInstance.rows} rows and {ShapeDataInstance.columns} columns.");
        }
    }

    private void DrawBoardTable()
    {
        var tableStyle = new GUIStyle("box")
        {
            padding = new RectOffset(10, 10, 10, 10),
            margin = new RectOffset(32, 0, 10, 10)
        };

        // 행(row)을 순회
        for (var row = 0; row < ShapeDataInstance.rows; row++)
        {
            EditorGUILayout.BeginHorizontal(tableStyle);

            // 열(column)을 순회
            for (var column = 0; column < ShapeDataInstance.columns; column++)
            {
                ShapeDataInstance.board[row].column[column] =
                    EditorGUILayout.Toggle(ShapeDataInstance.board[row].column[column]);
            }

            EditorGUILayout.EndHorizontal();
        }
    }
}
