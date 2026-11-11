using UnityEditor;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.XR.Management;

// One-click: turn on OpenXR for the Windows standalone build.  MindMaze > Enable OpenXR (PC VR)
public static class MMVRSetup
{
    [MenuItem("MindMaze/Enable OpenXR (PC VR)")]
    public static void Enable()
    {
        XRGeneralSettingsPerBuildTarget perTarget;
        if (!EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out perTarget) || perTarget == null)
        {
            if (!AssetDatabase.IsValidFolder("Assets/XR")) AssetDatabase.CreateFolder("Assets", "XR");
            perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
            AssetDatabase.CreateAsset(perTarget, "Assets/XR/XRGeneralSettings.asset");
            EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, perTarget, true);
        }
        if (!perTarget.HasSettingsForBuildTarget(BuildTargetGroup.Standalone)) perTarget.CreateDefaultSettingsForBuildTarget(BuildTargetGroup.Standalone);
        if (!perTarget.HasManagerSettingsForBuildTarget(BuildTargetGroup.Standalone)) perTarget.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Standalone);

        var general = perTarget.SettingsForBuildTarget(BuildTargetGroup.Standalone);
        general.InitManagerOnStart = true;
        var manager = perTarget.ManagerSettingsForBuildTarget(BuildTargetGroup.Standalone);
        bool ok = XRPackageMetadataStore.AssignLoader(manager, "UnityEngine.XR.OpenXR.OpenXRLoader", BuildTargetGroup.Standalone);
        EditorUtility.SetDirty(perTarget); EditorUtility.SetDirty(general); EditorUtility.SetDirty(manager);
        AssetDatabase.SaveAssets();
        Debug.Log("MindMaze: OpenXR loader assigned for Standalone = " + ok);
        EnableProfiles();
    }


static void EnableProfiles()
    {
        var s = UnityEngine.XR.OpenXR.OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Standalone);
        if (s == null) { Debug.LogWarning("MindMaze: OpenXR settings not ready, run the menu item again."); return; }
        int n = 0;
        foreach (var f in s.GetFeatures<UnityEngine.XR.OpenXR.Features.OpenXRInteractionFeature>())
        {
            f.enabled = true; EditorUtility.SetDirty(f); n++;
        }
        EditorUtility.SetDirty(s);
        AssetDatabase.SaveAssets();
        Debug.Log("MindMaze: enabled " + n + " OpenXR controller profiles");
    }
}
