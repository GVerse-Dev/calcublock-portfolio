using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// 디버그 전용 정의 심볼이 켜진 채로 스토어 빌드가 만들어지는 것을 차단한다.
///
/// BuildEnvironmentWindow.Apply()는 PlayerSettings.SetScriptingDefineSymbols로
/// DEBUG_ADS / IG_GAMELOOP_BUILD를 ProjectSettings.asset(= 커밋되는 파일)에 영구 기록한다.
/// Dev로 전환한 뒤 Release로 되돌리는 것을 잊으면 그대로 스토어 AAB가 나간다.
///
/// 그 결과가 특히 나쁜 이유:
///   - DEBUG_ADS  : AdUnitIds가 테스트 광고 단위로 바뀌어 전 사용자 광고 수익이 0이 된다.
///                  Play(AdMob)와 앱인토스(AIT 광고 그룹) **양쪽 모두** 해당한다.
///   - DEBUG_CONSENT : EEA 지역이 시뮬레이션되어 동의 상태가 매 실행 초기화된다.
///   - IG_GAMELOOP_BUILD : QA 자동플레이 봇 어셈블리가 릴리스에 실린다.
///
/// 안전장치를 사람의 기억에 맡기면 언젠가 반드시 뚫리므로 빌드를 **실패**시킨다.
/// (같은 방식의 선례: Assets/Scripts/Capture/Editor/CaptureBuildGuard.cs)
/// </summary>
public sealed class StoreBuildGuard : IPreprocessBuildWithReport
{
    // CaptureBuildGuard(-10000) 바로 다음. 무거운 작업이 시작되기 전에 실패시킨다.
    public int callbackOrder => -9000;

    /// <summary>릴리스 산출물에 절대 들어가면 안 되는 심볼.</summary>
    private static readonly string[] ForbiddenInRelease =
    {
        "DEBUG_ADS",
        "DEBUG_CONSENT",
        "IG_GAMELOOP_BUILD",
    };

    /// <summary>
    /// 이 프로젝트가 실제로 출시하는 플랫폼. 나머지는 검사하지 않는다.
    ///
    /// WebGL은 앱인토스(<c>.ait</c>) 경로다. SDK의 프로덕션 프로필은 Development 플래그를
    /// 켜지 않으므로(AITWebGLBuilder), OnPreprocessBuild의 isDevelopment 예외에 걸리지 않고 여기까지 온다.
    /// Dev Server / 개발 빌드는 Development가 켜져 있어 자연히 면제된다.
    /// </summary>
    private static readonly BuildTarget[] ReleaseTargets =
    {
        BuildTarget.Android,
        BuildTarget.WebGL,
    };

    public void OnPreprocessBuild(BuildReport report)
    {
        // Development 빌드는 검사 대상이 아니다 — 이 심볼들을 쓰라고 있는 빌드다.
        bool isDevelopment = (report.summary.options & BuildOptions.Development) != 0;
        if (isDevelopment) return;

        // 배포 대상이 아닌 플랫폼은 검사하지 않는다.
        if (System.Array.IndexOf(ReleaseTargets, report.summary.platform) < 0) return;

        // 심볼은 플랫폼마다 따로 저장된다. 검사 대상도 지금 빌드하는 플랫폼의 것이어야 한다.
        NamedBuildTarget named = NamedBuildTarget.FromBuildTargetGroup(
            BuildPipeline.GetBuildTargetGroup(report.summary.platform));

        var offenders = FindForbiddenSymbols(named);
        if (offenders.Count == 0) return;

        Fail(offenders, named, report.summary.options);
    }

    /// <summary>
    /// PlayerSettings에 영구 기록된 심볼 중 금지 목록에 걸리는 것을 찾는다.
    ///
    /// BuildPlayerOptions.extraScriptingDefines(= BuildScript가 쓰는 경로)는 여기에 안 잡히지만,
    /// 그쪽은 빌드 1회에만 적용되고 설정을 오염시키지 않으므로 검사 대상이 아니다.
    /// 문제가 되는 것은 ProjectSettings.asset에 남아 다음 빌드까지 따라오는 값이다.
    /// </summary>
    private static List<string> FindForbiddenSymbols(NamedBuildTarget named)
    {
        string raw = PlayerSettings.GetScriptingDefineSymbols(named);
        if (string.IsNullOrEmpty(raw)) return new List<string>();

        var defined = new HashSet<string>(
            raw.Split(new[] { ';', ',' }, System.StringSplitOptions.RemoveEmptyEntries)
               .Select(s => s.Trim())
               .Where(s => s.Length > 0));

        return ForbiddenInRelease.Where(defined.Contains).ToList();
    }

    private static void Fail(List<string> offenders, NamedBuildTarget named, BuildOptions options)
    {
        string message =
            $"[StoreBuildGuard] 디버그 심볼이 켜진 채로 {named.TargetName} 릴리스 빌드를 만들려 합니다. 빌드를 중단합니다.\n" +
            string.Join("\n", offenders.Select(s => $"  - {s}")) + "\n" +
            "\n" +
            $"       → Player Settings > Other Settings > Scripting Define Symbols ({named.TargetName}) 에서 제거하거나,\n" +
            "         Window > Build > Build Environment 를 Release로 되돌린 뒤 다시 빌드할 것.\n" +
            $"       (의도한 dev 빌드라면 Development Build 옵션을 켤 것)\n" +
            $"       (현재 BuildOptions: {options})";

        Debug.LogError(message);
        throw new BuildFailedException(message);
    }
}
