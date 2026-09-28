using System.Text;

/// <summary>
/// 한국어를 <b>어절 단위로</b> 줄바꿈시키는 공용 도구.
///
/// <para><b>무엇이 문제였나</b> — TMP 설정(<c>TMP Settings.m_UseModernHangulLineBreakingRules = 0</c>)이
/// 한글을 중국어·일본어처럼 <b>글자 단위</b>로 끊는다. 그래서 「감전이 옮겨붙 / 는다」,
/// 「주변을 멈춰 세 / 운다」처럼 낱말 한가운데서 줄이 갈렸다(서약 HUD 칸 실측).</para>
///
/// <para><b>어떻게 고치나</b> — 어절마다 <c>&lt;nobr&gt;</c>로 묶는다. 그러면 띄어쓰기와
/// 여는 괄호 앞에서만 끊긴다(괄호 묶음은 줄머리로 넘어가는 편이 읽기 좋다).
/// 전역 설정을 바꾸면 게임 전체의 줄배치가 한꺼번에 바뀌므로, 검증한 화면에만 적용한다.</para>
///
/// <para>리치 텍스트가 켜진 TMP에만 쓴다. 이미 태그가 든 문자열은 그대로 돌려준다(태그를 깨뜨리지 않는다).</para>
/// </summary>
public static class UIKoreanWrap
{
    public static string Words(string text)
    {
        if (string.IsNullOrEmpty(text) || text.IndexOf('<') >= 0) return text;

        var sb = new StringBuilder(text.Length + 32);
        int start = 0;
        for (int i = 0; i <= text.Length; i++)
        {
            bool end = i == text.Length;
            char c = end ? '\0' : text[i];
            bool split = end || c == ' ' || c == '\n' || (c == '(' && i > start);
            if (!split) continue;

            if (i > start) sb.Append("<nobr>").Append(text, start, i - start).Append("</nobr>");
            if (end) break;

            // 여는 괄호는 다음 어절의 머리로 넘긴다. 공백 없이 맞붙은 두 nobr 사이에선 TMP가 끊지 않으므로
            // 폭 0 공백(<zwsp>, TMP가 글리프를 합성한다)을 끼워 끊을 자리를 준다 — 없으면 「줄어든다(절인」이
            // 한 덩어리가 되어 결계 설명이 14.3px로 줄었다.
            if (c == '(') { sb.Append("<zwsp>"); start = i; continue; }
            sb.Append(c);
            start = i + 1;
        }
        return sb.ToString();
    }
}
