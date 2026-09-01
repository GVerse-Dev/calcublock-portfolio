using System;
using System.IO;
using System.Linq;
// AppsInTossMenu는 네임스페이스가 AppsInToss다. asmdef의 rootNamespace가
// "AppsInToss.Editor"라 헷갈리기 쉬운데, 그 값은 신규 스크립트 생성 시 기본값일 뿐
// 실제 선언과 무관하다. (AITDeployManager 쪽이 AppsInToss.Editor.Menu다)
using AppsInToss;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEngine;

/// <summary>
/// 커맨드라인 배치모드 앱인토스(.ait) 빌드.
///
/// 왜 별도 래퍼가 필요한가 — SDK가 주는 진입점은 에디터 메뉴 <c>AIT > Build &amp; Package</c>
/// 하나이고, 그 실체인 <c>AITDeployManager.RunBuildAndPackage()</c>가 <b>async void</b>다.
/// 그래서 <c>-quit</c>을 붙이면 첫 <c>await</c>에서 메서드가 반환되는 순간 에디터가 종료되어
/// 빌드가 중간에 잘린다. 이 스크립트는 <c>-quit</c> 없이 띄운 뒤 산출물을 감시해 직접 끝낸다.
///
/// 사용 (2패스로 나눈다):
/// <code>
///   1) 프로필 전환 — 재컴파일이 이 프로세스 안에서 끝나게 한다
///      Unity.exe -quit -batchmode -nographics -projectPath . \
///                -executeMethod AitBuildScript.ActivateProfile
///
///   2) 빌드 — -quit 없음. 감시자가 종료 코드를 정한다
///      Unity.exe -batchmode -nographics -projectPath . -buildTarget WebGL \
///                -executeMethod AitBuildScript.BuildAndPackage
/// </code>
///
/// <b>프로필을 빌드 직전에 바꾸지 않는다.</b> 이유는 <see cref="BuildProfileGuard"/> 주석과
/// 같다 — 전환이 스크립팅 심볼 변경을 통해 재컴파일을 유발하고, 빌드 직전에 그러면 stale
/// 어셈블리로 빌드되는 더 나쁜 함정이 생긴다. 그래서 여기서는 <b>검사만 하고 실패시킨다</b>.
/// </summary>
public static class AitBuildScript
{
    private const string ProfilePath = "Assets/Settings/Build Profiles/WebGL-AIT.asset";
    private const string ProfileName = "WebGL-AIT";
    private const string AitDir = "ait-build";

    /// <summary>7/31 실측은 42MB·수 분이었다. 넉넉히 두되 무한 대기는 막는다.</summary>
    private static readonly TimeSpan BuildTimeout = TimeSpan.FromMinutes(60);

    /// <summary>산출물이 다 쓰였다고 볼 최소 정지 시간. 쓰는 중인 파일을 성공으로 읽지 않기 위한 것.</summary>
    private static readonly TimeSpan SettleTime = TimeSpan.FromSeconds(5);

    private static DateTime _startedUtc;
    private static DateTime _deadlineUtc;

    // ── 1패스: 프로필 전환 ────────────────────────────────────────────────────

    /// <summary>WebGL-AIT 프로필을 활성화하고 끝낸다. 재컴파일은 이 프로세스가 물고 간다.</summary>
    public static void ActivateProfile()
    {
        var profile = AssetDatabase.LoadAssetAtPath<BuildProfile>(ProfilePath);
        if (profile == null)
        {
            Debug.LogError($"[AitBuild] 프로필 자산이 없다: {ProfilePath}");
            EditorApplication.Exit(1);
            return;
        }

        BuildProfile.SetActiveBuildProfile(profile);
        AssetDatabase.SaveAssets();
        Debug.Log($"[AitBuild] 활성 프로필 → {profile.name}");
    }

    // ── 2패스: 빌드 + 패키징 ──────────────────────────────────────────────────

    /// <summary>
    /// <c>AIT > Build &amp; Package</c>를 그대로 호출하고, 산출물이 갱신되면 종료한다.
    /// SDK 파이프라인을 재구현하지 않는 이유는 명확하다 — 재구현하면 에디터 메뉴로 만든
    /// 번들과 미묘하게 다른 결과가 나올 수 있고, 그 차이는 검수 단계에서야 드러난다.
    /// </summary>
    public static void BuildAndPackage()
    {
        var active = BuildProfile.GetActiveBuildProfile();
        string activeName = active == null ? "(없음 — classic 플랫폼 모드)" : active.name;

        if (active == null || active.name != ProfileName)
        {
            Debug.LogError(
                $"[AitBuild] 활성 프로필이 {ProfileName}이 아니다: {activeName}\n" +
                $"           먼저 ActivateProfile을 별도 프로세스로 실행할 것.\n" +
                "           (여기서 바꾸면 재컴파일이 빌드 직전에 일어나 stale 어셈블리가 실린다)");
            EditorApplication.Exit(1);
            return;
        }

        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.WebGL)
        {
            Debug.LogError(
                $"[AitBuild] 활성 빌드 타깃이 WebGL이 아니다: {EditorUserBuildSettings.activeBuildTarget}\n" +
                "           실행 인자에 -buildTarget WebGL 을 줄 것.");
            EditorApplication.Exit(1);
            return;
        }

        _startedUtc = DateTime.UtcNow;
        _deadlineUtc = _startedUtc + BuildTimeout;

        Debug.Log($"[AitBuild] 프로필 {activeName} / 타깃 WebGL — Build & Package 시작 " +
                  $"(타임아웃 {BuildTimeout.TotalMinutes:F0}분)");

        // async void라 여기서 즉시 반환된다. 이어지는 진행은 에디터 펌프가 돌린다.
        AppsInTossMenu.BuildAndPackage();

        if (!Application.isBatchMode)
        {
            Debug.LogWarning("[AitBuild] 배치모드가 아니다. 감시자를 붙이지 않는다 — 에디터를 닫지 않기 위함.");
            return;
        }

        EditorApplication.update += Watchdog;
    }

    // ── 감시자 ───────────────────────────────────────────────────────────────

    private static void Watchdog()
    {
        var now = DateTime.UtcNow;

        if (now > _deadlineUtc)
        {
            EditorApplication.update -= Watchdog;
            Debug.LogError($"[AitBuild] 타임아웃 — {BuildTimeout.TotalMinutes:F0}분 안에 .ait가 나오지 않았다.");
            EditorApplication.Exit(1);
            return;
        }

        string produced = FindFreshAit(now);
        if (produced == null) return;

        EditorApplication.update -= Watchdog;

        var info = new FileInfo(produced);
        Debug.Log($"[AitBuild] 완료 — {produced} ({info.Length / 1024.0 / 1024.0:F1} MB, " +
                  $"소요 {(now - _startedUtc).TotalMinutes:F1}분)");
        EditorApplication.Exit(0);
    }

    /// <summary>
    /// 이번 실행에서 새로 쓰인 .ait를 찾는다. 없으면 null.
    /// 빌드 루트와 dist/ 양쪽을 본다 — CLI 버전에 따라 생성 위치가 다르다(AITBuildValidator 동일).
    /// </summary>
    private static string FindFreshAit(DateTime now)
    {
        string root = Path.Combine(Directory.GetCurrentDirectory(), AitDir);
        if (!Directory.Exists(root)) return null;

        foreach (string dir in new[] { root, Path.Combine(root, "dist") })
        {
            if (!Directory.Exists(dir)) continue;

            string hit = Directory.GetFiles(dir, "*.ait")
                .Select(p => new FileInfo(p))
                .Where(f => f.LastWriteTimeUtc > _startedUtc)   // 이번 빌드 산출물인가
                .Where(f => now - f.LastWriteTimeUtc > SettleTime) // 다 쓰였는가
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Select(f => f.FullName)
                .FirstOrDefault();

            if (hit != null) return hit;
        }

        return null;
    }
}
