using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace CoachingPropHunt.Editor
{
    /// <summary>
    /// Headless CLI entry point for building the Android ARM64 IL2CPP APK cleanly via unity build.
    /// </summary>
    public static class AndroidApkBuilder
    {
        public static void BuildApk()
        {
            string outputPath = ParseArg("-buildOutput");
            if (string.IsNullOrWhiteSpace(outputPath))
                outputPath = Path.Combine(Directory.GetCurrentDirectory(), "Builds", "ParishramHunt.apk");

            string dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            EditorUserBuildSettings.buildAppBundle = false;
            PlayerSettings.productName = "Parishram Hunt";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.parishram.hunt");
            PlayerSettings.bundleVersion = "0.4.2";
            PlayerSettings.Android.bundleVersionCode = 6;
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetIl2CppCodeGeneration(NamedBuildTarget.Android, Il2CppCodeGeneration.OptimizeSize);
            PlayerSettings.Android.forceInternetPermission = true;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel25;

            string[] scenes = EditorBuildSettings.scenes
                .Where(s => s.enabled && !string.IsNullOrEmpty(s.path))
                .Select(s => s.path)
                .ToArray();
            if (scenes.Length == 0)
                scenes = new[] { "Assets/Scenes/CoachingCampus.unity" };

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outputPath,
                targetGroup = BuildTargetGroup.Android,
                target = BuildTarget.Android,
                options = BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;
            Debug.Log($"BUILD_APK_RESULT: result={summary.result} sizeBytes={summary.totalSize} errors={summary.totalErrors} warnings={summary.totalWarnings} path={summary.outputPath}");

            if (summary.result != BuildResult.Succeeded)
            {
                EditorApplication.Exit(1);
                return;
            }
            EditorApplication.Exit(0);
        }

        private static string ParseArg(string flag)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            }
            return null;
        }
    }
}
