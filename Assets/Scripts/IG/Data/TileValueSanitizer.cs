namespace IGMain
{
    /// <summary>
    /// 저장 파일에서 읽어 온 타일 값을 정상 범위로 강제한다.
    ///
    /// 세션 파일은 외부 앱전용 저장소에 평문으로 있어 손으로 고칠 수 있다.
    /// 검증 없이 받아들이면 "999999999" 같은 다중 문자가 타일 하나에 들어가는데,
    /// ExpressionEvaluator.Tokenize가 연속 숫자를 하나의 수로 병합하므로
    /// 그 타일 하나가 라인 클리어 한 번에 임의 점수를 만들어낸다.
    ///
    /// 세션 파일에는 HMAC 서명이 붙어 있어(SessionIntegrity) 서명 없는 변조는 이미 막히지만,
    /// 이건 그 서명이 뚫렸을 때를 위한 2차 방어다. 그래서 보드와 블록 **양쪽 모두**에
    /// 적용해야 의미가 있다 — 한쪽만 막으면 다른 쪽으로 같은 값이 들어와
    /// BoardGrid.PlaceBlock이 블록 타일의 TileData를 보드로 그대로 복사한다.
    /// </summary>
    public static class TileValueSanitizer
    {
        /// <summary>
        /// 값이 정상이면 그대로, 아니면 빈 문자열을 돌려준다.
        ///
        /// 정상 값은 **한 글자**다 — 숫자, 연산기호, 또는 공백.
        ///
        /// ⚠ **공백 " " 은 빈 칸이 아니라 정상적으로 생성되는 '채워진 칸'이다.**
        /// TileProbabilityResolver.TileValues의 마지막 원소이고 Phase 0에서 약 19% 비중이며,
        /// TileData.IsValid가 !IsNullOrEmpty라 IsPlaceBlock이 true다. 배치 가능 판정·라인 완성
        /// 판정·보드 점유율이 전부 여기에 달려 있고, ExpressionEvaluator는 두 숫자 사이의
        /// 공백을 "+"로 승격시켜 점수 계산에도 관여한다.
        /// 이걸 화이트리스트에서 빠뜨리면 변조 방어가 아니라 **정상 플레이 데이터를 손상시킨다**
        /// (복원할 때마다 채워진 칸이 사라져 앱 재시작으로 보드를 청소하는 치트가 된다).
        /// </summary>
        public static string Sanitize(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            if (value.Length != 1) return "";

            char c = value[0];
            if (char.IsDigit(c)) return value;

            switch (c)
            {
                // 공백 타일 — 위 주석 참조. 절대 빼지 말 것.
                case ' ':
                // ExpressionEvaluator.IsOperator와 같은 집합.
                // '*'와 '/'는 현재 생성 경로가 만들지 않지만(TileProbabilityResolver.TileValues는
                // '×','÷'만 담는다) 평가기가 인정하는 값이라 함께 허용한다 — 과거 세이브 호환용이다.
                case '+':
                case '-':
                case '*':
                case '/':
                case '×':
                case '÷':
                    return value;
                default:
                    return "";
            }
        }
    }
}
