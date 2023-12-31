using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using InfiniteRealms.Data;
using Newtonsoft.Json;
using UnityEditorInternal;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace InfiniteRealms.Editor
{
    public static class BuildScene
    {
        [MenuItem("Infinite Realms/Complete Scene Build")]
        private static void CleanBuild()
        {
            if (!CheckBuildSupport())
                return;
            
            ClearAllAssetBundleTags();
            AssignAssetBundleTags();
            BuildAssetBundles();
        }
        
        [MenuItem("Infinite Realms/Build Steps/1 - Check Build Modules")]
        private static void CheckBuildSupportAlone()
        {
            if (!CheckBuildSupport())
                return;
            
            EditorUtility.DisplayDialog("Build Support Check", "Everything is installed, ready to go", "OK");
        }

        [MenuItem("Infinite Realms/Build Steps/2 - Clear All Asset Bundle Tags")]
        private static void ClearAllAssetBundleTags()
        {
            // Get all Asset paths in projekt
            var allAssetPaths = AssetDatabase.GetAllAssetPaths();

            foreach (var assetPath in allAssetPaths)
            {
                AssignAssetBundleTagToAsset(assetPath, string.Empty);
            }

            Debug.Log("All AssetBundle tags have been cleared.");
        }
        
        [MenuItem("Infinite Realms/Build Steps/3 - Tag Assets in Scene")]
        private static void AssignAssetBundleTags()
        {
            var activeScene = SceneManager.GetActiveScene();
            var assetBundleName = activeScene.name;
            var rootObjects = activeScene.GetRootGameObjects();

            // Tag the active scene as an asset
            var scenePath = activeScene.path;
            AssignAssetBundleTagToAsset(scenePath, assetBundleName + "_scene");

            // Iterate through all root GameObjects and their children
            foreach (var rootObject in rootObjects)
            {
                AssignAssetBundleTagRecursively(rootObject, assetBundleName + "_assets");
            }
            
            // Sanitize unwanted Objects
            SanitizeAssetBundleTags<Camera>(activeScene);
            SanitizeAssetBundleTags<AudioListener>(activeScene);
            SanitizeAssetBundleTags<WindZone>(activeScene);
            SanitizeAssetBundleTags<Volume>(activeScene);
            SanitizeAssetBundleTags<Light>(activeScene, light => light.type == LightType.Directional);

            Debug.Log("Assets in the scene and the scene itself have been tagged for the AssetBundle: " + assetBundleName);
        }

        [MenuItem("Infinite Realms/Build Steps/4 - Build AssetBundles")]
        private static void BuildAssetBundles()
        {
            var tempPath = Path.GetTempPath();
            var outputDirectory = Path.Combine(tempPath, "irExport/bundle");
            var windowsDirectory = Path.Combine(tempPath, "irExport/windows");
            var macosDirectory = Path.Combine(tempPath, "irExport/macos");

            Debug.Log(Path.Combine(tempPath, "irExport"));

            try
            {
                // Clean up temporary directories
                if (Directory.Exists(windowsDirectory)) Directory.Delete(windowsDirectory, true);
                if (Directory.Exists(macosDirectory)) Directory.Delete(macosDirectory, true);
                if (Directory.Exists(outputDirectory)) Directory.Delete(outputDirectory, true);

                // Start
                Directory.CreateDirectory(macosDirectory);
                Directory.CreateDirectory(windowsDirectory);
                Directory.CreateDirectory(outputDirectory);
            }
            catch (Exception e)
            {
                Debug.LogError("Failed allocating temporary directories for AssetsBundles: " + e.Message);
            }


            var activeScene = SceneManager.GetActiveScene();
            var assetBundleName = activeScene.name;
            
            var windowsAssets = assetBundleName + "_assets_windows.unity3d";
            var windowsScene = assetBundleName + "_scene_windows.unity3d";
            var macosAssets = assetBundleName + "_assets_macos.unity3d";
            var macosScene = assetBundleName + "_scene_macos.unity3d";

            try
            {
                // Build AssetBundles for Windows
                BuildPipeline.BuildAssetBundles(windowsDirectory, BuildAssetBundleOptions.None,
                    BuildTarget.StandaloneWindows);
            }
            catch
            {
                throw new Exception("Failed building Windows AssetsBundles. Is 'Windows Build Support' installed for this Unity version?");
            }
            
            try
            {
                // Assemble Windows AssetBundles
                var windowsAssetsPath = GetAssetsBundlePath(windowsDirectory);

                if (windowsAssetsPath != null)
                {
                    File.Move(windowsAssetsPath,
                        Path.Combine(outputDirectory, windowsAssets));
                }
                else
                {
                    windowsAssets = "";
                }
                
                File.Move(Path.Combine(windowsDirectory, assetBundleName + "_scene"),
                    Path.Combine(outputDirectory, windowsScene));
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return;
            }

            try
            {
                // Build AssetBundles for macOS
                BuildPipeline.BuildAssetBundles(macosDirectory, BuildAssetBundleOptions.None,
                    BuildTarget.StandaloneOSX);
            }
            catch
            {
                throw new Exception("Failed building macOS AssetsBundles. Is 'Mac Build Support' installed for this Unity version?");
            }
            
            try
            {
                // Assemble macOS AssetBundles
                var macosAssetsPath = GetAssetsBundlePath(macosDirectory);

                if (macosAssetsPath != null)
                {
                    File.Move(macosAssetsPath,
                        Path.Combine(outputDirectory, macosAssets));
                }
                else
                {
                    macosAssets = "";
                }

                File.Move(Path.Combine(macosDirectory, assetBundleName + "_scene"),
                    Path.Combine(outputDirectory, macosScene));
            }            
            catch (Exception e)
            {
                Debug.LogException(e);
                return;
            }

            // Create the manifest object
            var manifestData = new ManifestData
            {
                ScenarioName = assetBundleName,
                CreationTime = DateTime.Now,
                UnityVersion = InternalEditorUtility.GetFullUnityVersion(),
                Files = new Dictionary<string, string>
                {
                    { "windowsAssets", windowsAssets },
                    { "windowsScene", windowsScene },
                    { "macosAssets", macosAssets },
                    { "macosScene", macosScene }
                }
            };

            // Serialize the manifest object to JSON
            var manifestContent = JsonConvert.SerializeObject(manifestData, Formatting.Indented);
            
            // Write the JSON content to the manifest file
            File.WriteAllText(outputDirectory + "/manifest.json", manifestContent);

            // Compress the output folder
            var zipFilePath = "Assets/Output/" + assetBundleName + ".irscene";
            if (File.Exists(zipFilePath)) File.Delete(zipFilePath);
            ZipFile.CreateFromDirectory(outputDirectory, zipFilePath);

            Debug.Log("Successfully built package: '" + zipFilePath + "'.");
        }

        private static bool CheckBuildSupport()
        {
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows))
            {
                EditorUtility.DisplayDialog("Build error", "Windows Build Support is not installed", "OK");
                return false;
            }
            
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneOSX))
            {
                EditorUtility.DisplayDialog("Build error", "macOS Build Support is not installed", "OK");
                return false;
            }

            return true;
        }
        
        private static void AssignAssetBundleTagRecursively(GameObject obj, string assetBundleName)
        {
            // Assign the AssetBundle name to the object's prefab (if it has one)
            if (PrefabUtility.GetPrefabInstanceStatus(obj) == PrefabInstanceStatus.Connected)
            {
                var prefab = PrefabUtility.GetCorrespondingObjectFromOriginalSource(obj);
                AssignAssetBundleTagToAsset(prefab, assetBundleName);
            }

            // Assign the AssetBundle name to all assets attached to the object
            var components = obj.GetComponents<Component>();
            foreach (var component in components)
            {
                var asset = GetAssetFromComponent(component);
                if (asset != null)
                {
                    AssignAssetBundleTagToAsset(asset, assetBundleName);
                }
            }

            // Recurse through the object's children
            foreach (Transform child in obj.transform)
            {
                AssignAssetBundleTagRecursively(child.gameObject, assetBundleName);
            }
        }

        private static Object GetAssetFromComponent(Component component)
        {
            return component switch
            {
                MeshFilter meshFilter => meshFilter.sharedMesh,
                MeshRenderer meshRenderer => meshRenderer.sharedMaterial,
                SkinnedMeshRenderer skinnedMeshRenderer => skinnedMeshRenderer.sharedMaterial,
                _ => null
            };
        }

        private static void AssignAssetBundleTagToAsset(Object asset, string assetBundleName)
        {
            var assetPath = AssetDatabase.GetAssetPath(asset);
            AssignAssetBundleTagToAsset(assetPath, assetBundleName);
        }

        private static void AssignAssetBundleTagToAsset(string assetPath, string assetBundleName)
        {
            var importer = AssetImporter.GetAtPath(assetPath);

            if (Path.GetExtension(assetPath).Equals(".cs") || Path.GetExtension(assetPath).Equals(".js"))
                return;
            
            if (importer != null && string.IsNullOrEmpty(importer.assetBundleName))
            {
                importer.assetBundleName = assetBundleName;
            }
        }
        
        private static void SanitizeAssetBundleTags<T>(Scene activeScene, Func<T, bool> predicate = null) where T : Component
        {
            var components = activeScene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<T>()).ToArray();
            foreach (var component in components)
            {
                if (predicate != null && !predicate(component)) continue;

                var assetPath = AssetDatabase.GetAssetPath(component.gameObject);
                AssignAssetBundleTagToAsset(assetPath, string.Empty);
            }
        }
        
        private static string GetAssetsBundlePath(string directory)
        {
            var assetBundlePaths = Directory.GetFiles(directory, "*_assets");
    
            return assetBundlePaths.Length == 0 ? null : assetBundlePaths[0];
        }
    }
}
