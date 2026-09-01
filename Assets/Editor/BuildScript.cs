using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// 커맨드라인 배치모드 Android 빌드.
///
/// GitHub Actions CI는 폐기했다. 이 스크립트와 Window/Build 메뉴가 유일한 빌드 경로다.
/// 심볼 세트는 BuildEnvironmentWindow와 동일하게 유지할 것.
///
/// 사용:
///   Unity.exe -quit -batchmode -nographics -projectPath . \
///             -executeMethod BuildScript.BuildAndroidDev
///
/// 주의: keystore가 없으면 Unity 기본 디버그 키로 서명된다.
/// 스토어 업로드 불가. 실기기 설치 테스트 전용.
/// </summary>
public static class BuildScript
{
    private const string OUTPUT_DIR = "build/Android";

    // ProjectSettings의 Android 기준 심볼이 이미 "DOTWEEN"(= Release)이므로
    // Dev는 여기에 더할 심볼만 지정한다.
    private static readonly string[] DEV_EXTRA_DEFINES =
    {
        "IG_GAMELOOP_BUILD",
        "DEBUG_ADS",
    };

    [MenuItem("Window/Build/Android Dev APK")]
    public static void BuildAndroidDev()
    {
        Build("dev", DEV_EXTRA_DEFINES, development: true);
    }

    [MenuItem("Window/Build/Android Release APK")]
    public static void BuildAndroidRelease()
    {
        Build("release", Array.Empty<string>(), development: false);
    }

    // GDPR 동의 폼 검증용. DEBUG_CONSENT로 EEA 지역을 시뮬레이션한다.
    // ConsentManager.TestDeviceHashedIds에 기기 해시가 있어야 실제로 적용된다.
    [MenuItem("Window/Build/Android Consent-Test APK")]
    public static void BuildAndroidConsentTest()
    {
        Build("consent-test", DEV_EXTRA_DEFINES.Append("DEBUG_CONSENT").ToArray(),
              development: true);
    }

    // 스토어 업로드용. 릴리즈 keystore 서명 + AAB.
    // Publishing Settings에 keystore/alias 비밀번호가 입력돼 있어야 한다.
    // (비밀번호는 에디터 세션 동안만 유지되므로 에디터 재시작 후에는 재입력 필요)
    [MenuItem("Window/Build/Android Store AAB")]
    public static void BuildAndroidStoreAab()
    {
        if (!PlayerSettings.Android.useCustomKeystore)
        {
            Fail("Store AAB requires the release keystore. Enable Custom Keystore in Publishing Settings.");
            return;
        }

        if (string.IsNullOrEmpty(PlayerSettings.Android.keystorePass) ||
            string.IsNullOrEmpty(PlayerSettings.Android.keyaliasPass))
        {
            Fail("Keystore/alias password is empty. Enter both in Publishing Settings, then build again.");
            return;
        }

        // Play Console 크래시/ANR 심볼화용 네이티브 디버그 심볼.
        // AAB 옆에 *.symbols.zip이 생성되며, Play Console의 해당 버전
        // (App Bundle 탐색기 → 다운로드 탭)에 함께 업로드할 것.
        var originalLevel = UnityEditor.Android.UserBuildSettings.DebugSymbols.level;
        var originalFormat = UnityEditor.Android.UserBuildSettings.DebugSymbols.format;
        UnityEditor.Android.UserBuildSettings.DebugSymbols.level =
            Unity.Android.Types.DebugSymbolLevel.SymbolTable;
        UnityEditor.Android.UserBuildSettings.DebugSymbols.format =
            Unity.Android.Types.DebugSymbolFormat.Zip;
        try
        {
            Build("store", Array.Empty<string>(), development: false,
                  appBundle: true, keepCustomKeystore: true);
        }
        finally
        {
            UnityEditor.Android.UserBuildSettings.DebugSymbols.level = originalLevel;
            UnityEditor.Android.UserBuildSettings.DebugSymbols.format = originalFormat;
        }
    }

    private static void Build(string env, string[] extraDefines, bool development,
                              bool appBundle = false, bool keepCustomKeystore = false)
    {
        string[] scenes = EditorBuildSettings.scenes
            .Where(scene => scene.enabled)
            .Select(scene => scene.path)
            .ToArray();

        if (scenes.Length == 0)
        {
            Fail("No enabled scenes in Build Settings.");
            return;
        }

        // 테스트 빌드는 실기기에 바로 설치할 수 있는 APK, 스토어 업로드는 AAB.
        EditorUserBuildSettings.buildAppBundle = appBundle;

        var options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = $"{OUTPUT_DIR}/CalcTetris-{env}.{(appBundle ? "aab" : "apk")}",
            target = BuildTarget.Android,
            targetGroup = BuildTargetGroup.Android,
            // 심볼을 PlayerSettings에 영구 반영하지 않고 이 빌드에만 적용한다.
            // (SetScriptingDefineSymbols는 재컴파일 타이밍 문제가 있고, 설정을 오염시킨다)
            extraScriptingDefines = extraDefines,
            options = development
                ? BuildOptions.Development | BuildOptions.AllowDebugging
                : BuildOptions.None,
        };

        Debug.Log($"[Build] env={env} defines=+[{string.Join(";", extraDefines)}] " +
                  $"development={development} scenes={scenes.Length}");

        // 테스트 빌드는 비밀번호 없이도 돌아가도록 디버그 키 서명으로 우회한다.
        // (릴리즈 keystore 비밀번호가 없으면 "Unable to sign..."으로 실패하기 때문)
        // 스토어 빌드(keepCustomKeystore)는 릴리즈 keystore를 그대로 쓴다.
        // ProjectSettings를 더럽히지 않도록 반드시 원복한다.
        bool originalUseCustomKeystore = PlayerSettings.Android.useCustomKeystore;
        if (!keepCustomKeystore)
            PlayerSettings.Android.useCustomKeystore = false;

        BuildReport report;
        try
        {
            report = BuildPipeline.BuildPlayer(options);
        }
        finally
        {
            PlayerSettings.Android.useCustomKeystore = originalUseCustomKeystore;

            // 빌드 도중 Unity가 ProjectSettings.asset을 디스크에 쓰기 때문에,
            // 메모리 값만 되돌리면 파일에는 우회값(false)이 남는다.
            // 커밋되는 파일이라 그대로 두면 CI 릴리즈 서명이 깨진다.
            AssetDatabase.SaveAssets();
        }

        BuildSummary summary = report.summary;

        if (summary.result == BuildResult.Succeeded)
        {
            Debug.Log($"[Build] SUCCESS env={env} " +
                      $"size={summary.totalSize / (1024 * 1024)}MB " +
                      $"time={summary.totalTime} " +
                      $"path={summary.outputPath}");
            return;
        }

        Fail($"env={env} result={summary.result} errors={summary.totalErrors}");
    }

    private static void Fail(string message)
    {
        Debug.LogError($"[Build] FAILED: {message}");

        // 배치모드에서 종료 코드를 0이 아니게 만들어 CI/스크립트가 실패를 감지하게 한다.
        if (Application.isBatchMode)
        {
            EditorApplication.Exit(1);
        }
    }
}
