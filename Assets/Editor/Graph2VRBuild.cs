using System;
using System.IO;
using System.Linq;
using System.Xml;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Uses the Android toolchain configured in Unity Preferences > External Tools.
// Optional output override: Library/Graph2VRBuildOutput.txt (absolute directory).
public static class Graph2VRBuild
{
    static bool building;
    static string Output {
        get {
            const string config = "Library/Graph2VRBuildOutput.txt";
            return File.Exists(config) ? File.ReadAllText(config).Trim() : Path.GetFullPath("Builds/" + PlayerSettings.bundleVersion);
        }
    }

    [InitializeOnLoadMethod]
    static void WatchRequests() { EditorApplication.update += RunRequestedBuild; }

    static void RunRequestedBuild()
    {
        string request = Path.Combine(Output, "build-request.txt");
        if (building || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(request)) return;
        string platform = File.ReadAllText(request).Trim();
        if (platform != "Android" && platform != "Windows") { File.Delete(request); return; }
        var target = platform == "Android" ? BuildTarget.Android : BuildTarget.StandaloneWindows64;
        var group = platform == "Android" ? BuildTargetGroup.Android : BuildTargetGroup.Standalone;
        if (EditorUserBuildSettings.activeBuildTarget != target)
        {
            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(group, target))
            {
                File.Delete(request);
                File.WriteAllText(Path.Combine(Output, platform + "-result.txt"), "Failed to switch target");
            }
            return;
        }
        File.Delete(request);
        building = true;
        try
        {
            if (platform == "Android") Android(); else Windows();
            File.WriteAllText(Path.Combine(Output, platform + "-result.txt"), "Succeeded " + PlayerSettings.bundleVersion);
        }
        catch (Exception error) { File.WriteAllText(Path.Combine(Output, platform + "-result.txt"), error.ToString()); Debug.LogException(error); }
        finally { building = false; }
    }

    [MenuItem("Graph2VR/Build release/Android APK")]
    public static void Android()
    {
        PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        EditorUserBuildSettings.buildAppBundle = false;
        Build(BuildTarget.Android, Path.Combine(Output, "Android/Graph2VR-" + PlayerSettings.bundleVersion + "-Android-arm64.apk"));
    }

    [MenuItem("Graph2VR/Build release/Windows x64")]
    public static void Windows()
    {
        Build(BuildTarget.StandaloneWindows64, Path.Combine(Output, "Windows/Graph2VR.exe"));
    }

    static void Build(BuildTarget target, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
            scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
            locationPathName = path, target = target, options = BuildOptions.None
        });
        Debug.Log("GRAPH2VR_BUILD_RESULT " + report.summary.result + " version=" + PlayerSettings.bundleVersion + " errors=" + report.summary.totalErrors);
        if (report.summary.result != BuildResult.Succeeded) throw new Exception("Graph2VR build failed: " + report.summary.result);
    }
}

// Graph2VR supports user-selected HTTP SPARQL servers and media on local networks.
public sealed class Graph2VRAndroidManifest : IPostGenerateGradleAndroidProject
{
    public int callbackOrder { get { return 1000; } }
    public void OnPostGenerateGradleAndroidProject(string path)
    {
        string manifest = Path.Combine(path, "src/main/AndroidManifest.xml");
        var xml = new XmlDocument();
        xml.Load(manifest);
        var application = (XmlElement)xml.SelectSingleNode("/manifest/application");
        application.SetAttribute("usesCleartextTraffic", "http://schemas.android.com/apk/res/android", "true");
        xml.Save(manifest);
    }
}
