using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

// ShapeData의 커스텀 에디터 클래스 정의
[CustomEditor(typeof(ShapeData), false)] // ShapeData 대상
[CanEditMultipleObjects]
[System.Serializable]
public class ShapeDataDrawer : Editor
{
    // 현재 인스펙터에서 편집 중인 ShapeData 인스턴스
    private ShapeData ShapeDataInstance => target as ShapeData;

    // 인스펙터 UI를 재정의
    public override void OnInspectorGUI()
    {
        // SerializedObject 데이터 업데이트
        serializedObject.Update();

        // 1. "Clear Board" 버튼 표시
        ClearBoardButton();
        EditorGUILayout.Space();

        // 2. 행(row)과 열(column) 입력 필드 표시
        DrawColumnsInputFields();
        EditorGUILayout.Space();

        // 3. 보드 데이터가 존재하면 테이블 형태로 표시
        if (ShapeDataInstance.board != null && ShapeDataInstance.columns > 0 && ShapeDataInstance.rows > 0)
        {
            DrawBoardTable();
        }

        // SerializedObject 데이터 저장
        serializedObject.ApplyModifiedProperties();

        // 변경된 데이터가 있을 경우 대상 ScriptableObject를 저장 상태로 설정
        if (GUI.changed)
        {
            EditorUtility.SetDirty(ShapeDataInstance);
        }
    }

    // "Clear Board" 버튼 표시
    private void ClearBoardButton()
    {
        if (GUILayout.Button("Clear Board")) // 버튼 생성
        {
            ShapeDataInstance.Clear(); // 보드 초기화
        }
    }

    // 행(row) 및 열(column) 입력 필드 표시
    private void DrawColumnsInputFields()
    {
        // 기존 값 임시 저장
        var columnsTemp = ShapeDataInstance.columns;
        var rowsTemp = ShapeDataInstance.rows;

        // 새로운 행과 열 값 입력 필드
        ShapeDataInstance.columns = EditorGUILayout.IntField("Columns", ShapeDataInstance.columns);
        ShapeDataInstance.rows = EditorGUILayout.IntField("Rows", ShapeDataInstance.rows);

        // 값이 변경되었을 때 새로운 보드 생성
        if ((ShapeDataInstance.columns != columnsTemp || ShapeDataInstance.rows != rowsTemp) &&
            ShapeDataInstance.columns > 0 && ShapeDataInstance.rows > 0)
        {
            ShapeDataInstance.CreateNewBoard();
        }
    }

    // 2D 보드 데이터를 테이블 형태로 표시
    private void DrawBoardTable()
    {
        // 테이블 스타일 정의
        var tableStyle = new GUIStyle("box");
        tableStyle.padding = new RectOffset(10, 10, 10, 10);
        tableStyle.margin.left = 32;

        // 열 헤더 스타일
        var headerColumnStyle = new GUIStyle();
        headerColumnStyle.fixedWidth = 65; // 고정된 열 너비
        headerColumnStyle.alignment = TextAnchor.MiddleCenter;

        // 행 스타일
        var rowStyle = new GUIStyle();
        rowStyle.fixedHeight = 25; // 고정된 행 높이
        rowStyle.alignment = TextAnchor.MiddleCenter;

        // 데이터 필드 스타일
        var dataFieldStyle = new GUIStyle(EditorStyles.miniButtonMid);
        dataFieldStyle.normal.background = Texture2D.grayTexture; // 비활성화 색상
        dataFieldStyle.onNormal.background = Texture2D.whiteTexture; // 활성화 색상

        // 각 행(row)을 순회하며 데이터 표시
        for (var row = 0; row < ShapeDataInstance.rows; row++)
        {
            // 행 시작
            EditorGUILayout.BeginHorizontal(headerColumnStyle);

            // 각 열(column)을 순회하며 데이터 표시
            for (var column = 0; column < ShapeDataInstance.columns; column++)
            {
                EditorGUILayout.BeginHorizontal(rowStyle);

                // 현재 칸의 활성화 상태를 토글 버튼으로 표시
                var data = EditorGUILayout.Toggle(ShapeDataInstance.board[row].column[column], dataFieldStyle);

                // 변경된 데이터 저장
                ShapeDataInstance.board[row].column[column] = data;

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndHorizontal(); // 행 종료
        }
    }
}
