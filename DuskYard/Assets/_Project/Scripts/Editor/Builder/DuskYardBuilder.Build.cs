using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace DuskYard.EditorTools
{
    public static partial class DuskYardBuilder
    {
        const string BuildRoot = "../Builds";

        [MenuItem("DuskYard/4. Build Player (macOS + Windows)")]
        public static void BuildPlayers()
        {
            bool mac = BuildFor(BuildTarget.StandaloneOSX, $"{BuildRoot}/macOS/DuskYard.app");
            bool win = BuildFor(BuildTarget.StandaloneWindows64, $"{BuildRoot}/Windows/DuskYard.exe");
            Debug.Log($"[DuskYardBuilder] builds: macOS {(mac ? "OK" : "FAILED")}, Windows {(win ? "OK" : "FAILED")}");
        }

        public static void CliBuildMac() => EditorApplication.Exit(BuildFor(BuildTarget.StandaloneOSX, $"{BuildRoot}/macOS/DuskYard.app") ? 0 : 1);
        /// <summary>Development player: exposes the "GC Allocated In Frame" profiler counter to the benchmark.</summary>
        public static void CliBuildMacDev() => EditorApplication.Exit(BuildFor(BuildTarget.StandaloneOSX, $"{BuildRoot}/macOS_Dev/DuskYard.app", BuildOptions.Development) ? 0 : 1);
        public static void CliBuildWindows() => EditorApplication.Exit(BuildFor(BuildTarget.StandaloneWindows64, $"{BuildRoot}/Windows/DuskYard.exe") ? 0 : 1);

        static bool BuildFor(BuildTarget target, string path, BuildOptions options = BuildOptions.None)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            var opts = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = path,
                target = target,
                targetGroup = BuildTargetGroup.Standalone,
                options = options,
            };
            var report = BuildPipeline.BuildPlayer(opts);
            var s = report.summary;
            Debug.Log($"[DuskYardBuilder] {target}: {s.result}, {s.totalSize / (1024 * 1024)} MB, {s.totalTime.TotalSeconds:0}s, " +
                      $"errors {s.totalErrors}, warnings {s.totalWarnings} -> {Path.GetFullPath(path)}");
            foreach (var step in report.steps)
            foreach (var m in step.messages)
                if (m.type == LogType.Error || m.type == LogType.Warning || m.type == LogType.Exception)
                    Debug.Log($"[Build {m.type}] {m.content}");
            return s.result == BuildResult.Succeeded;
        }
    }
}
