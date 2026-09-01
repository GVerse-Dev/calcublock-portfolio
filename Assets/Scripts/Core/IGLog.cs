using System.Diagnostics;

namespace IGMain
{
    /// <summary>
    /// IG_VERBOSE_LOG 심볼이 정의되지 않으면 호출 자체가 컴파일 단계에서 삭제된다.
    /// 문자열 보간 인자도 평가되지 않아 GC 할당이 완전히 0이 된다.
    /// 활성화: Player Settings > Scripting Define Symbols에 IG_VERBOSE_LOG 추가.
    /// </summary>
    public static class IGLog
    {
        [Conditional("IG_VERBOSE_LOG")]
        public static void Verbose(string message) => UnityEngine.Debug.Log(message);

        [Conditional("IG_VERBOSE_LOG")]
        public static void VerboseFormat(string format, params object[] args)
            => UnityEngine.Debug.LogFormat(format, args);
    }
}
