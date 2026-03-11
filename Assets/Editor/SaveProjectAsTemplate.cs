using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

    public class SaveProjectAsTemplate : EditorWindow
    {
        private static readonly string UnityEditorApplicationProjectTemplatesPath = Path.Combine(
            Path.GetDirectoryName(EditorApplication.applicationPath),
            "Data",
            "Resources",
            "PackageManager",
            "ProjectTemplates"
        );

        private static readonly string[] TemplateFolderNames = new string[]
        {
            "Assets",
            "Packages",
            "ProjectSettings",
        };

        private string folderPath;
        private string packagePath;
        private string zipName;

        private string targetPath;
        private string templateName = "TestTemplate";
        private string templateDisplayName = "Test Template Project";
        private string templateDescription = "Development Template for VRPlayer";
        private string templateDefaultScene;
        private string templateVersion = "0.0.1";
        private SceneAsset templateDefaultSceneAsset;

        [MenuItem("Template/Save Template")]
        private static void ShowWindow()
        {
            var window = EditorWindow.GetWindow<SaveProjectAsTemplate>();
            window.titleContent = new GUIContent("Save Template");
            window.minSize = new Vector2(500, 270);
            window.Show();

        }

        private void InvokeSaveProjectAsTemplate()
        {
            Assembly editorAssembly = Assembly.GetAssembly(typeof(UnityEditor.Editor));
            Type editorUtilityType = editorAssembly.GetType("UnityEditor.EditorUtility");

            MethodInfo methodInfo = editorUtilityType.GetMethod("SaveProjectAsTemplate", BindingFlags.Static | BindingFlags.NonPublic);
            methodInfo.Invoke(editorUtilityType, new object[] { folderPath, templateName, templateDisplayName, templateDescription, templateDefaultScene, templateVersion });
        }

        private void DeleteProjectVersionTxt()
        {
            var projectVersionTxtPath = Path.Combine(folderPath, "ProjectSettings", "ProjectVersion.txt");

            if (File.Exists(projectVersionTxtPath))
            {
                File.Delete(projectVersionTxtPath);
            }
            else
            {
                Debug.LogErrorFormat("File ProjectVersion.txt does not exist at path: {0}", projectVersionTxtPath);
            }
        }

        private void SetTemplateDataFromPackageJson()
        {
            var packageJsonPath = Path.Combine(targetPath, "package.json");
            if (File.Exists(packageJsonPath))
            {
                var packageJson = File.ReadAllText(packageJsonPath);
                var templateData = JsonUtility.FromJson<TemplateData>(packageJson);
                templateName = templateData.Name;
                templateDisplayName = templateData.DisplayName;
                templateDescription = templateData.Description;
                templateDefaultScene = templateData.DefaultScene;
                templateVersion = templateData.Version;
                SetDefaultSceneAssetFromPath();
            }
        }

        private void SetDefaultSceneAssetFromPath()
        {
            if (templateDefaultScene != String.Empty)
            {
                templateDefaultSceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(templateDefaultScene);
                Debug.AssertFormat(templateDefaultSceneAsset != null, "Failed to load scene asset at path from package.json, path: {0}", templateDefaultScene);
            }
            else
            {
                templateDefaultSceneAsset = null;
            }
        }

        private void DefaultSceneGUI()
        {
            templateDefaultSceneAsset = (SceneAsset)EditorGUILayout.ObjectField("Default scene asset:", templateDefaultSceneAsset, typeof(SceneAsset), false);
            if (templateDefaultSceneAsset != null)
            {
                templateDefaultScene = AssetDatabase.GetAssetPath(templateDefaultSceneAsset);
            }
            else
            {
                templateDefaultScene = null;
            }

            using (new EditorGUI.DisabledGroupScope(true))
            {
                EditorGUILayout.TextField("Default scene:", templateDefaultScene);
            }
        }

        private void OnGUI()
        {
            if (GUILayout.Button("Select Target Folder"))
            {
                targetPath = EditorUtility.SaveFolderPanel("Choose target folder", UnityEditorApplicationProjectTemplatesPath, String.Empty);
                SetTemplateDataFromPackageJson();
            }

            using (var check = new EditorGUI.ChangeCheckScope())
            {
                targetPath = EditorGUILayout.TextField("Path:", targetPath);
                if (check.changed)
                {
                    SetTemplateDataFromPackageJson();
                }
            }

            if (GUI.changed)
            {
                zipName = string.Format("com.unity.template.{0}-{1}", templateName, templateVersion);
                packagePath = Path.Combine(targetPath,/* zipName, */"package");
                folderPath = Path.Combine(packagePath, "ProjectData~");
            }

            templateName = EditorGUILayout.TextField("Name:", templateName);
            templateDisplayName = EditorGUILayout.TextField("Display name:", templateDisplayName);
            templateDescription = EditorGUILayout.TextField("Description:", templateDescription);
            DefaultSceneGUI();
            templateVersion = EditorGUILayout.TextField("Version:", templateVersion);

            if (GUILayout.Button("Save"))
            {
                if (string.IsNullOrWhiteSpace(targetPath))
                {
                    return;
                }

                if (Directory.Exists(packagePath))
                {
                    try
                    {
                        Directory.Delete(packagePath, true);
                    }
                    catch
                    {

                    }
                }


                if (!Directory.Exists(folderPath))
                {
                    Directory.CreateDirectory(folderPath);
                }

                AssetDatabase.SaveAssets();
                InvokeSaveProjectAsTemplate();
                DeleteProjectVersionTxt();
                MovePackageFile();
                DeleteEditorFolder();
                
                string tgzPath = Path.Combine(targetPath, zipName + ".tgz");
                MakeTgz(targetPath, zipName);
        }
        }

        private bool DeleteEditorFolder()
        {
            string[] deletePath = new string[4];
            deletePath[3] = Path.Combine(folderPath, "Assets", "Editor");
            deletePath[2] = Path.Combine(folderPath, "Assets", "Editor.meta");
            deletePath[0] = Path.Combine(folderPath, "Assets", "Editor", "SaveProjectAsTemplate.cs");
            deletePath[1] = Path.Combine(folderPath, "Assets", "Editor", "SaveProjectAsTemplate.cs.meta");

            foreach (var path in deletePath)
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
                else if(Directory.Exists(path))
                {
                    Directory.Delete(path);
                }
            }
            return true;
        }

        private bool MovePackageFile()
        {
            string packageJsonName = "package.json";

            var packageJsonPath = Path.Combine(folderPath, packageJsonName);
            var destPath = Path.Combine(packagePath, packageJsonName);

            if (File.Exists(destPath))
            {
                File.Delete(destPath);
            }

            File.Move(packageJsonPath, destPath);

            return true;
        }

    private void MakeTgz(string workingDir, string zipName)
    {
        string sevenZip = @"C:\Program Files\7-Zip\7z.exe";

        if (!File.Exists(sevenZip))
        {
            Debug.LogError("7zip not found: " + sevenZip);
            return;
        }

        string tarPath = Path.Combine(workingDir, zipName + ".tar");
        string tgzPath = Path.Combine(workingDir, zipName + ".tgz");

        if (File.Exists(tarPath)) File.Delete(tarPath);
        if (File.Exists(tgzPath)) File.Delete(tgzPath);

        // tar 생성
        Run7Zip(sevenZip, $"a -ttar \"{tarPath}\" \"package\"", workingDir);

        // tgz 생성
        Run7Zip(sevenZip, $"a -tgzip \"{tgzPath}\" \"{zipName}.tar\"", workingDir);

        if (File.Exists(tarPath))
            File.Delete(tarPath);

        System.Diagnostics.Process.Start(targetPath);
    }

    private void Run7Zip(string exe, string args, string workingDir)
    {
        var process = new System.Diagnostics.Process();

        process.StartInfo.FileName = exe;
        process.StartInfo.Arguments = args;
        process.StartInfo.WorkingDirectory = workingDir;
        process.StartInfo.CreateNoWindow = true;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;

        process.Start();

        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();

        process.WaitForExit();

        Debug.Log(output);

        if (!string.IsNullOrEmpty(error))
            Debug.LogError(error);
    }


    [Serializable]
        private class TemplateData
        {
#pragma warning disable 0649
            [SerializeField]
            private string name;
            [SerializeField]
            private string displayName;
            [SerializeField]
            private string description;
            [SerializeField]
            private string defaultScene;
            [SerializeField]
            private string version;
#pragma warning restore 0649

            public string Name { get { return name; } }
            public string DisplayName { get { return displayName; } }
            public string Description { get { return description; } }
            public string DefaultScene { get { return defaultScene; } }
            public string Version { get { return version; } }
        }
}

