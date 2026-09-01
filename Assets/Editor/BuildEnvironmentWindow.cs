using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

/// <summary>
/// Window > Build Environment 에서 열기.
/// 정의 심볼을 ProjectSettings.asset(= 커밋되는 파일)에 영구 기록한다.
///
/// Release로 되돌리는 것을 잊으면 스토어 빌드가 테스트 광고로 나가므로,
/// 그 사고는 StoreBuildGuard가 빌드를 실패시켜 막는다.
/// </summary>
public class BuildEnvironmentWindow : EditorWindow
{
    // Stage는 Dev와 심볼이 같다. 폐기된 CI의 environment 입력(dev/stage/release)을
    // 그대로 옮겨 온 잔재이며, 지금은 Dev와 구분되지 않는다.
    private enum Env { Dev, Stage, Release }

    private const string SYMBOLS_DEV     = "DOTWEEN;IG_GAMELOOP_BUILD;DEBUG_ADS";
    private const string SYMBOLS_STAGE   = "DOTWEEN;IG_GAMELOOP_BUILD;DEBUG_ADS";
    private const string SYMBOLS_RELEASE = "DOTWEEN";

    [MenuItem("Window/Build Environment")]
    private static void Open() => GetWindow<BuildEnvironmentWindow>("Build Environment");

    private void OnGUI()
    {
        EditorGUILayout.Space(8);
        GUILayout.Label("빌드 환경 전환", EditorStyles.boldLabel);
        EditorGUILayout.Space(4);

        // 현재 심볼 표시
        string current = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Android);
        EditorGUI.BeginDisabledGroup(true);
        EditorGUILayout.TextField("현재 Android 심볼", current);
        EditorGUI.EndDisabledGroup();

        EditorGUILayout.Space(8);

        DrawEnvButton(Env.Dev,     SYMBOLS_DEV,     current);
        DrawEnvButton(Env.Stage,   SYMBOLS_STAGE,   current);
        DrawEnvButton(Env.Release, SYMBOLS_RELEASE, current);

        EditorGUILayout.Space(12);
        EditorGUILayout.HelpBox(
            "Dev / Stage : DEBUG_ADS 포함 → 테스트 광고 ID 사용\n" +
            "Release      : DEBUG_ADS 없음  → 프로덕션 광고 ID 사용",
            MessageType.Info);
    }

    private void DrawEnvButton(Env env, string symbols, string current)
    {
        bool isActive = current == symbols;
        GUI.color = isActive ? Color.green : Color.white;

        string label = isActive ? $"✓ {env} (현재 적용 중)" : $"  {env} 로 전환";
        EditorGUI.BeginDisabledGroup(isActive);

        if (GUILayout.Button(label, GUILayout.Height(32)))
            Apply(env, symbols);

        EditorGUI.EndDisabledGroup();
        GUI.color = Color.white;
    }

    private void Apply(Env env, string symbols)
    {
        PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.Android, symbols);
        Debug.Log($"[BuildEnv] {env} 환경 적용 완료: {symbols}");
        Repaint();
    }
}
