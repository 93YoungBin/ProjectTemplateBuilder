using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 현재 Unity 프로젝트를 Unity Template(.tgz) 형태로 Export하는 Editor Tool
/// 
/// 주요 기능
/// - Unity 내부 API SaveProjectAsTemplate 호출
/// - Template Package 구조 자동 생성
/// - 불필요 파일 제거
/// - 7zip을 이용한 tgz 압축 생성
/// - Unity Template 폴더로 이동
/// </summary>

public class SaveProjectAsTemplate : EditorWindow
{
    // Unity Editor 내부 Template 저장 경로
    private static readonly string UnityTemplatePath = Path.Combine(
        Path.GetDirectoryName(EditorApplication.applicationPath),
        "Data",
        "Resources",
        "PackageManager",
        "ProjectTemplates"
    );

    private const string SevenZipPath = @"C:\Program Files\7-Zip\7z.exe";

    // 작업 경로
    private string targetPath;
    private string packagePath;
    private string folderPath;
    private string zipName;

    // Template Metadata
    private string templateName = "mytemplate";
    private string templateDisplayName = "My Template";
    private string templateDescription = "Unity Template";
    private string templateVersion = "1.0.0";

    private string templateDefaultScene;
    private SceneAsset templateDefaultSceneAsset;

    #region Window

    [MenuItem("Tools/Template/Save Project As Template")]
    private static void ShowWindow()
    {
        var window = GetWindow<SaveProjectAsTemplate>();
        window.titleContent = new GUIContent("Template Export");
        window.minSize = new Vector2(500, 300);
    }

    #endregion

    #region GUI

    private void OnGUI()
    {
        DrawPathSection();
        DrawTemplateInfo();
        DrawButtons();
    }

    /// <summary>
    /// 경로 선택 GUI
    /// </summary>
    private void DrawPathSection()
    {
        GUILayout.Label("Target Folder", EditorStyles.boldLabel);

        if (GUILayout.Button("Select Target Folder"))
        {
            targetPath = EditorUtility.SaveFolderPanel(
                "Choose target folder",
                UnityTemplatePath,
                "");

            LoadTemplateData();
        }

        EditorGUI.BeginChangeCheck();

        targetPath = EditorGUILayout.TextField("Path", targetPath);

        if (EditorGUI.EndChangeCheck())
        {
            LoadTemplateData();
        }

        if (!string.IsNullOrEmpty(targetPath))
        {
            zipName = $"com.unity.template.{templateName}-{templateVersion}";
            packagePath = Path.Combine(targetPath, "package");
            folderPath = Path.Combine(packagePath, "ProjectData~");
        }
    }

    /// <summary>
    /// Template Metadata GUI
    /// </summary>
    private void DrawTemplateInfo()
    {
        GUILayout.Space(10);
        GUILayout.Label("Template Info", EditorStyles.boldLabel);

        templateName = EditorGUILayout.TextField("Name", templateName);
        templateDisplayName = EditorGUILayout.TextField("Display Name", templateDisplayName);
        templateDescription = EditorGUILayout.TextField("Description", templateDescription);
        templateVersion = EditorGUILayout.TextField("Version", templateVersion);

        DrawDefaultSceneGUI();
    }

    /// <summary>
    /// Default Scene 설정
    /// </summary>
    private void DrawDefaultSceneGUI()
    {
        templateDefaultSceneAsset = (SceneAsset)EditorGUILayout.ObjectField(
            "Default Scene",
            templateDefaultSceneAsset,
            typeof(SceneAsset),
            false);

        templateDefaultScene = templateDefaultSceneAsset
            ? AssetDatabase.GetAssetPath(templateDefaultSceneAsset)
            : null;

        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.TextField("Scene Path", templateDefaultScene);
        }
    }

    private void DrawButtons()
    {
        GUILayout.Space(10);

        if (GUILayout.Button("Save Template"))
            SaveTemplate(false);

        if (GUILayout.Button("Save And Install Template"))
            SaveTemplate(true);
    }

    #endregion

    #region Template Save

    /// <summary>
    /// Template 저장 메인 함수
    /// </summary>
    private void SaveTemplate(bool installToUnity)
    {
        if (string.IsNullOrEmpty(targetPath))
        {
            Debug.LogError("Target path is empty");
            return;
        }

        PrepareDirectory();

        AssetDatabase.SaveAssets();

        InvokeSaveProjectAsTemplate();

        DeleteProjectVersion();

        MovePackageJson();

        DeleteEditorFolder();

        MakeTgz(targetPath, zipName);

        if (installToUnity)
        {
            MoveToUnityTemplateFolder(
                Path.Combine(targetPath, zipName + ".tgz"));
        }
    }

    /// <summary>
    /// 작업 폴더 준비
    /// </summary>
    private void PrepareDirectory()
    {
        if (Directory.Exists(packagePath))
            Directory.Delete(packagePath, true);

        Directory.CreateDirectory(folderPath);
    }

    #endregion

    #region Unity Internal API

    /// <summary>
    /// Unity 내부 API 호출
    /// EditorUtility.SaveProjectAsTemplate
    /// </summary>
    private void InvokeSaveProjectAsTemplate()
    {
        Assembly editorAssembly = Assembly.GetAssembly(typeof(Editor));

        Type editorUtilityType =
            editorAssembly.GetType("UnityEditor.EditorUtility");

        MethodInfo method =
            editorUtilityType.GetMethod(
                "SaveProjectAsTemplate",
                BindingFlags.Static | BindingFlags.NonPublic);

        method.Invoke(editorUtilityType, new object[]
        {
            folderPath,
            templateName,
            templateDisplayName,
            templateDescription,
            templateDefaultScene,
            templateVersion
        });
    }

    #endregion

    #region Template File Processing

    /// <summary>
    /// ProjectVersion.txt 제거
    /// </summary>
    private void DeleteProjectVersion()
    {
        string path = Path.Combine(
            folderPath,
            "ProjectSettings/ProjectVersion.txt");

        if (File.Exists(path))
            File.Delete(path);
    }

    /// <summary>
    /// package.json 이동
    /// </summary>
    private void MovePackageJson()
    {
        string source = Path.Combine(folderPath, "package.json");
        string dest = Path.Combine(packagePath, "package.json");

        if (!File.Exists(source))
            return;

        if (File.Exists(dest))
            File.Delete(dest);

        File.Move(source, dest);
    }

    /// <summary>
    /// Editor 폴더 제거
    /// </summary>
    private void DeleteEditorFolder()
    {
        string editorPath = Path.Combine(folderPath, "Assets/Editor");

        if (Directory.Exists(editorPath))
            Directory.Delete(editorPath, true);
    }

    #endregion

    #region Compression

    /// <summary>
    /// tgz 압축 생성
    /// </summary>
    private void MakeTgz(string workingDir, string zipName)
    {
        if (!File.Exists(SevenZipPath))
        {
            Debug.LogError("7zip not installed");
            return;
        }

        string tarPath = Path.Combine(workingDir, zipName + ".tar");
        string tgzPath = Path.Combine(workingDir, zipName + ".tgz");

        DeleteIfExists(tarPath);
        DeleteIfExists(tgzPath);

        Run7Zip($"a -ttar \"{tarPath}\" \"package\"", workingDir);

        Run7Zip($"a -tgzip \"{tgzPath}\" \"{zipName}.tar\"", workingDir);

        DeleteIfExists(tarPath);

        System.Diagnostics.Process.Start(workingDir);
    }

    private void Run7Zip(string args, string workingDir)
    {
        var process = new System.Diagnostics.Process();

        process.StartInfo.FileName = SevenZipPath;
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

        if (!string.IsNullOrEmpty(error))
            Debug.LogError(error);
    }

    private void DeleteIfExists(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }

    #endregion

    #region Template Install

    /// <summary>
    /// Unity Template 폴더 이동
    /// </summary>
    private void MoveToUnityTemplateFolder(string tgzPath)
    {
        if (!File.Exists(tgzPath))
        {
            Debug.LogError("tgz not found");
            return;
        }

        string dest = Path.Combine(
            UnityTemplatePath,
            Path.GetFileName(tgzPath));

        if (File.Exists(dest))
            File.Delete(dest);

        File.Move(tgzPath, dest);

        Debug.Log("Template installed");

        System.Diagnostics.Process.Start(UnityTemplatePath);
    }

    #endregion

    #region Template Data

    /// <summary>
    /// package.json에서 metadata 로드
    /// </summary>
    private void LoadTemplateData()
    {
        if (string.IsNullOrEmpty(targetPath))
            return;

        string packageJsonPath =
            Path.Combine(targetPath, "package.json");

        if (!File.Exists(packageJsonPath))
            return;

        try
        {
            var json = File.ReadAllText(packageJsonPath);

            var data = JsonUtility.FromJson<TemplateData>(json);

            if (data == null)
                return;

            templateName = data.Name;
            templateDisplayName = data.DisplayName;
            templateDescription = data.Description;
            templateDefaultScene = data.DefaultScene;
            templateVersion = data.Version;

            SetDefaultSceneAsset();
        }
        catch (Exception e)
        {
            Debug.LogError(e);
        }
    }

    private void SetDefaultSceneAsset()
    {
        if (string.IsNullOrEmpty(templateDefaultScene))
            return;

        templateDefaultSceneAsset =
            AssetDatabase.LoadAssetAtPath<SceneAsset>(
                templateDefaultScene);
    }

    [Serializable]
    private class TemplateData
    {
        [SerializeField] private string name;
        [SerializeField] private string displayName;
        [SerializeField] private string description;
        [SerializeField] private string defaultScene;
        [SerializeField] private string version;

        public string Name => name;
        public string DisplayName => displayName;
        public string Description => description;
        public string DefaultScene => defaultScene;
        public string Version => version;
    }

    #endregion
}