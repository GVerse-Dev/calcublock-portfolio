namespace IGMain
{
    public static class ScoreFormatterUtility
    {
        /// <summary>
        /// 음수의 절댓값을 표시 가능한 범위로 돌려준다.
        ///
        /// C#은 기본 unchecked 컨텍스트라 -long.MinValue가 자기 자신이다.
        /// 그래서 아래 포맷터들이 "-" + Format(-score) 로 부호를 벗겨내는 순간
        /// score == long.MinValue이면 같은 인자로 자신을 무한 호출한다.
        /// StackOverflowException은 catch할 수 없고 IL2CPP에서는 네이티브 스택
        /// 오버플로(SIGSEGV)로 프로세스가 즉사한다 — 조작된 세션 파일 하나로
        /// 앱이 켤 때마다 죽는 상태를 만들 수 있었다.
        ///
        /// long.MaxValue로 잘라내면 FormatCompact/FormatCompactK는 정수 나눗셈이 ±1을 흡수해
        /// 출력이 완전히 동일하고, FormatFull만 마지막 자리가 1 차이 난다.
        /// 어느 쪽이든 SaveManager의 MAX_SCORE(1조) 클램프 이후로는 도달하지 않는 값이라
        /// 표시 정확도는 문제가 되지 않는다.
        /// </summary>
        private static long Magnitude(long negative) =>
            negative == long.MinValue ? long.MaxValue : -negative;

        /// <summary>
        /// 현재 점수용. 콤마 구분 전체 숫자로 반환한다.
        /// UI의 AutoSize가 영역 초과를 처리하므로 포맷터가 축약하지 않는다.
        /// 예: 1234567 → "1,234,567"
        /// </summary>
        public static string FormatFull(long score)
        {
            if (score < 0)
                return "-" + FormatFull(Magnitude(score));

            return score.ToString("N0");
        }

        /// <summary>
        /// 베스트 점수용. 6자리 고정 영역에 맞게 1M 이상이면 K/M/B로 축약한다.
        /// 예: 999999 → "999,999" / 1500000 → "1.5M" / 2100000000 → "2.1B"
        /// </summary>
        public static string FormatCompact(long score)
        {
            if (score < 0)
                return "-" + FormatCompact(Magnitude(score));

            if (score >= 1_000_000_000L)
                return (score / 100_000_000L / 10.0).ToString("0.#") + "B";

            if (score >= 1_000_000L)
                return (score / 100_000L / 10.0).ToString("0.#") + "M";

            return score.ToString("N0");
        }

        /// <summary>
        /// 타이틀 화면 등 아주 좁은 영역용. 1000 이상부터 K 단위로도 축약한다.
        /// 예: 950 → "950" / 1500 → "1.5K" / 1500000 → "1.5M"
        /// </summary>
        public static string FormatCompactK(long score)
        {
            if (score < 0)
                return "-" + FormatCompactK(Magnitude(score));

            if (score >= 1_000_000_000L)
                return (score / 100_000_000L / 10.0).ToString("0.#") + "B";

            if (score >= 1_000_000L)
                return (score / 100_000L / 10.0).ToString("0.#") + "M";

            if (score >= 1_000L)
                return (score / 100L / 10.0).ToString("0.#") + "K";

            return score.ToString();
        }
    }
}
