#if UNITY_IOS || UNITY_IPHONE
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;

/// <summary>
/// ATT(앱 추적 투명성) 권한 팝업 문구를 언어별로 로컬라이즈.
///
/// 배경:
/// GoogleMobileAdsSettings의 userTrackingUsageDescription은 단일 문자열이라
/// PListProcessor가 NSUserTrackingUsageDescription에 그대로 하나만 박습니다.
/// (참고: 같은 설정의 userLanguage는 인스펙터 UI 언어일 뿐, plist와 무관)
///
/// 그래서 언어별 문구가 필요하면 Xcode 프로젝트에 InfoPlist.strings를
/// 직접 넣어줘야 하며, 이 스크립트가 그 역할을 합니다.
///
/// 동작:
/// - {lang}.lproj/InfoPlist.strings 생성 후 Xcode 빌드에 추가
/// - Info.plist의 CFBundleLocalizations에 지원 언어 등록
/// - Descriptions에 없는 언어는 Info.plist 기본값(영문)으로 폴백
/// </summary>
public static class AttDescriptionLocalizer
{
    private const string ATT_KEY = "NSUserTrackingUsageDescription";

    /// <summary>기본(폴백) 언어. Info.plist 원본 문자열의 언어와 일치해야 함.</summary>
    private const string DEFAULT_LANGUAGE = "en";

    // 언어 코드 → ATT 팝업 문구.
    // 문구 작성 시 주의: 애플 심사 가이드라인상 "왜 추적하는지"가 드러나야 하며,
    // 허용을 강요하거나 보상을 약속하는 표현은 리젝 사유가 됩니다.
    private static readonly Dictionary<string, string> Descriptions =
        new Dictionary<string, string>
        {
            { "en", "This allows us to show you more relevant ads instead of generic ones." },
            { "ko", "회원님께 더 관련성 높은 광고를 제공하기 위해 사용됩니다." },
        };

    // PListProcessor(GoogleMobileAds)가 Info.plist를 먼저 쓰도록 뒤 순서로 실행.
    [PostProcessBuild(100)]
    public static void OnPostProcessBuild(BuildTarget buildTarget, string path)
    {
        if (buildTarget != BuildTarget.iOS)
        {
            return;
        }

        AddLocalizedStringsFiles(path);
        RegisterLocalizations(path);
    }

    /// <summary>{lang}.lproj/InfoPlist.strings 생성 후 Xcode 빌드 리소스로 등록.</summary>
    private static void AddLocalizedStringsFiles(string path)
    {
        string pbxPath = PBXProject.GetPBXProjectPath(path);

        var project = new PBXProject();
        project.ReadFromFile(pbxPath);

        // ATT 문구는 앱 본체(메인 타겟)에 들어가야 함. UnityFramework 아님.
        string targetGuid = project.GetUnityMainTargetGuid();

        foreach (var entry in Descriptions)
        {
            string relativeDir = entry.Key + ".lproj";
            string absoluteDir = Path.Combine(path, relativeDir);
            Directory.CreateDirectory(absoluteDir);

            // .strings 문법: "키" = "값"; — 값 안의 " 와 \ 는 이스케이프 필요.
            string escaped = entry.Value.Replace("\\", "\\\\").Replace("\"", "\\\"");
            string contents = string.Format("\"{0}\" = \"{1}\";\n", ATT_KEY, escaped);

            string relativePath = relativeDir + "/InfoPlist.strings";
            // BOM 없는 UTF-8. BOM이 있으면 Xcode가 .strings 파싱에 실패할 수 있음.
            File.WriteAllText(Path.Combine(path, relativePath), contents,
                              new UTF8Encoding(false));

            string fileGuid = project.AddFile(relativePath, relativePath);
            project.AddFileToBuild(targetGuid, fileGuid);
        }

        project.WriteToFile(pbxPath);
    }

    /// <summary>
    /// CFBundleLocalizations에 등록해야 iOS가 .lproj를 실제로 조회함.
    /// 등록이 없으면 모든 기기가 기본 문구만 보게 됨.
    /// </summary>
    private static void RegisterLocalizations(string path)
    {
        string plistPath = Path.Combine(path, "Info.plist");

        var plist = new PlistDocument();
        plist.ReadFromFile(plistPath);

        plist.root.SetString("CFBundleDevelopmentRegion", DEFAULT_LANGUAGE);

        PlistElementArray localizations = plist.root.CreateArray("CFBundleLocalizations");
        foreach (var entry in Descriptions)
        {
            localizations.AddString(entry.Key);
        }

        plist.WriteToFile(plistPath);
    }
}
#endif
