using UnityEditor;
using System.IO;
using System.IO.Compression;
using UnityEngine;
using System.Collections.Generic;

public class MultiPlatformBuilder
{
    [MenuItem("Build/Build Windows and Linux + Zip")]
    public static void BuildAndZipAll()
    {
        string buildRoot = "Builds";
        string projectName = Application.productName;

        // 1. Setup Paths
        string winDir = Path.Combine(buildRoot, "Windows");
        string linuxDir = Path.Combine(buildRoot, "Linux");

        // Clean old builds
        if (Directory.Exists(buildRoot)) Directory.Delete(buildRoot, true);
        Directory.CreateDirectory(winDir);
        Directory.CreateDirectory(linuxDir);

        // 2. Perform Builds
        BuildForPlatform(BuildTarget.StandaloneWindows64, Path.Combine(winDir, projectName + ".exe"));
        BuildForPlatform(BuildTarget.StandaloneLinux64, Path.Combine(linuxDir, projectName + ".x86_64"));

        // 3. Zip the results
        ZipBuild(winDir, Path.Combine(buildRoot, projectName + "_Windows.zip"));
        ZipBuild(linuxDir, Path.Combine(buildRoot, projectName + "_Linux.zip"));

        Debug.Log("Build and Zipping process complete!");
    }

    private static void BuildForPlatform(BuildTarget target, string path)
    {
        Debug.Log($"Building for {target}...");

        BuildPlayerOptions options = new BuildPlayerOptions();
        options.scenes = GetEnabledScenes();
        options.locationPathName = path;
        options.target = target;
        options.options = BuildOptions.None;

        var report = BuildPipeline.BuildPlayer(options);

        if (report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
            Debug.Log($"{target} Build Succeeded!");
        else
            Debug.LogError($"{target} Build Failed!");
    }

    private static void ZipBuild(string sourceDir, string zipPath)
    {
        if (File.Exists(zipPath)) File.Delete(zipPath);

        Debug.Log($"Zipping {sourceDir} to {zipPath}...");
        ZipFile.CreateFromDirectory(sourceDir, zipPath);
    }

    private static string[] GetEnabledScenes()
    {
        List<string> scenes = new List<string>();
        foreach (var scene in EditorBuildSettings.scenes)
        {
            if (scene.enabled) scenes.Add(scene.path);
        }
        return scenes.ToArray();
    }
}