using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using Unity.Android.Types;
using UnityEngine;

[InitializeOnLoad]
public static class DustinyFastBuildSettings
{
    static DustinyFastBuildSettings()
    {
        EditorApplication.delayCall += () => Apply(log: false);
    }

    [MenuItem("Dustiny/Apply Fast Test Build Settings")]
    private static void ApplyFromMenu()
    {
        Apply(log: true);
    }


    [MenuItem("Dustiny/Add Windows Defender Exclusions")]
    private static void AddDefenderExclusions()
    {
        string projectRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, ".."));
        string dustinyRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(projectRoot, ".."));
        string userProfile = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
        string localAppData = System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData);

        string[] paths =
        {
            dustinyRoot,
            projectRoot,
            System.IO.Path.Combine(userProfile, ".gradle"),
            System.IO.Path.Combine(localAppData, "Unity"),
            System.IO.Path.Combine(localAppData, "Android")
        };

        var commands = new System.Text.StringBuilder();
        for (int i = 0; i < paths.Length; i++)
        {
            commands.Append("Add-MpPreference -ExclusionPath '")
                .Append(paths[i].Replace("'", "''"))
                .Append("'; ");
        }

        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = "-NoProfile -ExecutionPolicy Bypass -Command \"" + commands + "\"",
            UseShellExecute = true,
            Verb = "runas"
        };

        try
        {
            System.Diagnostics.Process.Start(startInfo);
            Debug.Log("[Dustiny] Windows Defender 예외 등록 창을 열었습니다. UAC에서 허용하면 프로젝트/Gradle/Unity 캐시 폴더가 실시간 검사에서 빠집니다.");
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning("[Dustiny] Defender 예외 등록을 취소했거나 권한이 없습니다: " + exception.Message);
        }
    }
    public static void Apply(bool log)
    {
        NamedBuildTarget android = NamedBuildTarget.Android;

        PlayerSettings.SetIl2CppCodeGeneration(android, Il2CppCodeGeneration.OptimizeSize);
        PlayerSettings.SetIl2CppCompilerConfiguration(android, Il2CppCompilerConfiguration.Release);
        PlayerSettings.SetIncrementalIl2CppBuild(android, true);
        PlayerSettings.SetManagedStrippingLevel(android, ManagedStrippingLevel.Minimal);
        PlayerSettings.SetIl2CppStacktraceInformation(android, Il2CppStacktraceInformation.MethodOnly);

        UserBuildSettings.DebugSymbols.level = DebugSymbolLevel.None;

        if (PlayerSettings.Android.targetArchitectures != UnityEditor.AndroidArchitecture.ARM64)
        {
            PlayerSettings.Android.targetArchitectures = UnityEditor.AndroidArchitecture.ARM64;
        }

        if (log)
        {
            Debug.Log(
                "[Dustiny] 테스트 빌드 설정을 적용했습니다. " +
                "IL2CPP Code Generation = Faster (smaller) builds, " +
                "Create symbols.zip = None, ARM64 only, incremental IL2CPP on."
            );
        }
    }
}

public sealed class DustinyFastBuildPreprocess : IPreprocessBuildWithReport
{
    public int callbackOrder => -100;

    public void OnPreprocessBuild(BuildReport report)
    {
        if (report.summary.platform != BuildTarget.Android)
        {
            return;
        }

        DustinyFastBuildSettings.Apply(log: false);
        Debug.Log("[Dustiny] Android 빌드 전 심볼 zip 생략 및 Faster (smaller) builds 설정을 강제했습니다.");
    }
}