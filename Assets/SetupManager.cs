using UnityEngine;
using System.Diagnostics;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine.UI;
using System;
using SFB;
using UnityEngine.Networking;
using MPUIKIT;
using FishNet.Managing.Scened;
using System.Threading.Tasks;

public class SetupManager : MonoBehaviour
{
    private string method;
    public Animator transitionAnim;

    [Header("UI Elements")]
    public TextMeshProUGUI statusTextPreinstall;
    public Slider preinstallProgress;
    public SetupPage preinstallPage;
    public ConsoleLogHandler preinstallConsole;
    public TextMeshProUGUI selectedDataPath;
    public Button selectDataPathButton;
    public Button selectMethodButton;
    public Button androidDisableButton;
    public MPImage demucsButton;
    public MPImage VRButton;
    public TextMeshProUGUI statusTextFinalInstall;
    public Slider finalInstallProgress;
    public SetupPage finalInstallPage;
    public ConsoleLogHandler finalInstallConsole;
    public AudioSource audioSource;
    public AudioSource completeFX;


    [Header("Conditional Setup Flow")]
    [Tooltip("The page where the user selects Demucs or VocalRemover.org")]
    public SetupPage methodSelectionPage;
    [Tooltip("The final completion page")]
    public SetupPage completionPage;

    [Header("Auto-Setup UI References (for testing builds)")]
    public SetupPage welcomePage;
    public SetupPage tosPage;
    public Button pathButton;
    public Button welcomeNextButton;
    public Button tosAgreeButton;
    public Button goToMenuButton;


    private Process activeProcess;
    private bool processIsRunning = false;
    private ActiveProcessType currentProcessType = ActiveProcessType.None;
    private bool shouldStartFinalInstall = false;
    private enum ActiveProcessType { None, Preinstall, FinalInstall }

    // This queue holds Actions (methods) that are sent from background threads
    // and need to be executed safely on Unity's main thread.
    private readonly static Queue<Action> executionQueue = new Queue<Action>();

    void Update()
    {

        // This runs on the main thread every frame.
        // It checks if there are any tasks in the queue and executes them.
        // This is the key to making UI updates from background processes work reliably.
        lock (executionQueue)
        {
            while (executionQueue.Count > 0)
            {
                // Dequeue the action and invoke it.
                executionQueue.Dequeue().Invoke();
            }
        }

    }

    /// <summary>
    /// Queues a method (Action) to be executed on the main thread.
    /// </summary>
    private void QueueForMainThread(Action action)
    {
        if (action == null) return;
        lock (executionQueue)
        {
            executionQueue.Enqueue(action);
        }
    }
    public void Quit()
    {
        OnApplicationQuit(); // Ensure process is killed
        Application.Quit();
    }

    private void Awake()
    {
        if (PlayerPrefs.GetInt("setupDone") == 1 && !Application.isEditor)
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene("Menu");
        }
    }

    public void StartPreinstall()
    {
        if (processIsRunning)
        {
            UnityEngine.Debug.LogWarning("A process is already running.");
            return;
        }

        if (statusTextPreinstall != null) statusTextPreinstall.text = "Starting...";
        if (preinstallProgress != null) preinstallProgress.value = 0;

        currentProcessType = ActiveProcessType.Preinstall;
        StartCoroutine(DownloadSetupFilesAndRun());
    }

    private IEnumerator DownloadSetupFilesAndRun()
    {
        string dataPath = PlayerPrefs.GetString("dataPath");
        if (string.IsNullOrEmpty(dataPath))
        {
            UnityEngine.Debug.LogError("Data path is not set.");
            if (statusTextPreinstall != null) statusTextPreinstall.text = "Error: Data path not set.";
            yield break;
        }

        string setupUtilitiesPath = Path.Combine(dataPath, "setuputilities");
        try
        {
            if (!Directory.Exists(setupUtilitiesPath))
            {
                Directory.CreateDirectory(setupUtilitiesPath);
                UnityEngine.Debug.Log($"Created directory: {setupUtilitiesPath}");
            }
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogError($"Failed to create directory: {e.Message}");
            if (statusTextPreinstall != null) statusTextPreinstall.text = "Error: Failed to create directory.";
            yield break;
        }

        bool isLinux = Application.platform == RuntimePlatform.LinuxPlayer || Application.platform == RuntimePlatform.LinuxEditor;
        string scriptExt = isLinux ? ".sh" : ".bat";

        // Only download files needed for Demucs setup
        string batUrl = $"https://raw.githubusercontent.com/grncd/YASGsetuputilities/refs/heads/main/pyinstall{scriptExt}";
        string pyUrl = "https://raw.githubusercontent.com/grncd/YASGsetuputilities/refs/heads/main/fullinstall.py";
        string py2Url = "https://raw.githubusercontent.com/grncd/YASGsetuputilities/refs/heads/main/updatechecker.py";
        string mainPyUrl = "https://raw.githubusercontent.com/grncd/YASGsetuputilities/refs/heads/main/main.py";

        string batPath = Path.Combine(setupUtilitiesPath, $"pyinstall{scriptExt}");
        string pyPath = Path.Combine(setupUtilitiesPath, "fullinstall.py");
        string py2Path = Path.Combine(setupUtilitiesPath, "updatechecker.py");
        string vocalRemoverPath = Path.Combine(dataPath, "vocalremover");

        statusTextPreinstall.text = "Downloading setup files...";
        yield return StartCoroutine(DownloadFile(batUrl, batPath));
        yield return StartCoroutine(DownloadFile(pyUrl, pyPath));
        yield return StartCoroutine(DownloadFile(py2Url, py2Path));

        // Create vocalremover folder and download main.py (needed for both methods)
        Directory.CreateDirectory(vocalRemoverPath);
        Directory.CreateDirectory(Path.Combine(vocalRemoverPath, "input"));
        yield return StartCoroutine(DownloadFile(mainPyUrl, Path.Combine(vocalRemoverPath, "main.py")));

        if (isLinux)
        {
            GrantExecutePermission(batPath);
        }

        StartCoroutine(RunProcessCoroutine());
    }

    private void GrantExecutePermission(string path)
    {
        try
        {
            Process chmod = new Process();
            chmod.StartInfo.FileName = "chmod";
            chmod.StartInfo.Arguments = $"+x \"{path}\"";
            chmod.StartInfo.UseShellExecute = false;
            chmod.StartInfo.CreateNoWindow = true;
            chmod.Start();
            chmod.WaitForExit();
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogError($"Failed to chmod {path}: {e.Message}");
        }
    }

    private IEnumerator DownloadFile(string url, string path)
    {
        using (UnityWebRequest www = UnityWebRequest.Get(url))
        {
            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                UnityEngine.Debug.LogError($"Failed to download {url}: {www.error}");
                if (statusTextPreinstall != null) statusTextPreinstall.text = $"Error downloading {Path.GetFileName(path)}.";
            }
            else
            {
                try
                {
                    File.WriteAllBytes(path, www.downloadHandler.data);
                    UnityEngine.Debug.Log($"Successfully downloaded and saved {Path.GetFileName(path)} to {path}");
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogError($"Failed to save file {path}: {e.Message}");
                    if (statusTextPreinstall != null) statusTextPreinstall.text = $"Error saving {Path.GetFileName(path)}.";
                }
            }
        }
    }

    public static void ClearFolder(string path)
    {
        if (!Directory.Exists(path)) return; // Prevent crash if folder doesn't exist

        foreach (string file in Directory.GetFiles(path))
        {
            File.Delete(file);
        }
        foreach (string dir in Directory.GetDirectories(path))
        {
            Directory.Delete(dir, true);
        }
    }

    private void Start()
    {

        StartCoroutine(SetFrameRate());

        IEnumerator SetFrameRate()
        {
            yield return new WaitForSeconds(1f);

            // Apply FPS limit setting on all platforms
            if (SettingsManager.Instance != null)
            {
                bool limitFPS = SettingsManager.Instance.GetSetting<bool>("LimitFPS", false);

                if (limitFPS)
                {
                    Application.targetFrameRate = 60;
                    UnityEngine.Debug.Log("[Exit] FPS limited to 60");
                }
                else
                {
                    // On Android, use native refresh rate
                    if (Application.platform == RuntimePlatform.Android)
                    {
                        Application.targetFrameRate = (int)Screen.currentResolution.refreshRateRatio.value;
                        UnityEngine.Debug.Log($"[Exit] FPS set to native refresh rate: {Application.targetFrameRate}");
                    }
                    else
                    {
                        // On other platforms, use -1 (unlimited)
                        Application.targetFrameRate = -1;
                        UnityEngine.Debug.Log("[Exit] FPS set to unlimited (-1)");
                    }
                }
            }
            else if (Application.platform == RuntimePlatform.Android)
            {
                Application.targetFrameRate = 60;
            }
        }

        if (/*!Application.isEditor*/ true)
        {
            // Ensure there's a sensible default (Unity game's data folder) if none set
            string defaultPath = PlayerPrefs.GetString("dataPath");

            // Debugging Linux path issues
            UnityEngine.Debug.Log($"Platform: {Application.platform}, Current DataPath: {defaultPath}");

            // On Android, ALWAYS use persistentDataPath - never use saved values
            if (Application.platform == RuntimePlatform.Android)
            {
                if (androidDisableButton != null)
                {
                    androidDisableButton.interactable = false;
                    UnityEngine.Debug.Log("[Setup] Android detected, disabled requested button.");
                }

                defaultPath = Application.persistentDataPath;
                PlayerPrefs.SetString("dataPath", defaultPath);
                PlayerPrefs.Save();
                UnityEngine.Debug.Log($"[Setup] Android detected, forced path to: {defaultPath}");

                if (selectedDataPath != null) selectedDataPath.text = defaultPath;
                selectDataPathButton.interactable = true;
                return; // Skip the rest of the setup logic
            }

            bool isLinux = Application.platform == RuntimePlatform.LinuxPlayer || Application.platform == RuntimePlatform.LinuxEditor;

            // Fix for Linux users stuck with .config path in PlayerPrefs
            if (isLinux && !string.IsNullOrEmpty(defaultPath) && defaultPath.Contains(".config"))
            {
                UnityEngine.Debug.Log("Detected incorrect .config path on Linux. Forcing migration to .local/share.");
                defaultPath = ""; // Force recalculation
            }

            if (string.IsNullOrEmpty(defaultPath))
            {
                string baseFolder;
                if (Application.platform == RuntimePlatform.Android)
                {
                    // On Android, use Unity's persistentDataPath which points to writable app storage
                    defaultPath = Application.persistentDataPath;
                    UnityEngine.Debug.Log($"[Setup] Android detected, using persistentDataPath: {defaultPath}");
                }
                else if (Application.platform == RuntimePlatform.LinuxPlayer || Application.platform == RuntimePlatform.LinuxEditor)
                {
                    // Explicitly force ~/.local/share/YASG/YASG to prevent ambiguity or switching to .config
                    string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    baseFolder = Path.Combine(userProfile, ".local", "share", "YASG");
                    defaultPath = Path.Combine(baseFolder, "YASG");
                }
                else
                {
                    // Use the user's Roaming AppData folder so the path works regardless of username.
                    string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                    if (string.IsNullOrEmpty(roaming))
                    {
                        // Fallback: construct a plausible Roaming path from the user profile.
                        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                        roaming = Path.Combine(userProfile ?? @"C:\Users\Default", "AppData", "Roaming");
                    }
                    baseFolder = Path.Combine(roaming, "YASG");
                    defaultPath = Path.Combine(baseFolder, "YASG");
                }

                // Ensure the folder exists and persist it to PlayerPrefs.
                try
                {
                    Directory.CreateDirectory(defaultPath);
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogWarning($"Could not create default data path '{defaultPath}': {e.Message}");
                }

                PlayerPrefs.SetString("dataPath", defaultPath);
            }
            else
            {
                try
                {
                    ClearFolder(defaultPath);
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogWarning($"Failed to clear folder {defaultPath}: {e.Message}");
                }
            }

            // Update UI to show current/default path before opening selector
            if (selectedDataPath != null) selectedDataPath.text = defaultPath;
            selectDataPathButton.interactable = true;
        }

        // Start auto-setup if this is a testing build (version starts with 'T')
        if (Application.version.StartsWith("T"))
        {
            // Delete all PlayerPrefs for a fresh start
            PlayerPrefs.DeleteAll();
            PlayerPrefs.Save();
            UnityEngine.Debug.Log("[AutoSetup] Deleted all PlayerPrefs for testing.");

            // Re-set the default data path since we just deleted it
            bool isLinux = Application.platform == RuntimePlatform.LinuxPlayer || Application.platform == RuntimePlatform.LinuxEditor;
            string defaultPath;

            if (Application.platform == RuntimePlatform.Android)
            {
                // On Android, use Unity's persistentDataPath which points to writable app storage
                defaultPath = Application.persistentDataPath;
                UnityEngine.Debug.Log($"[AutoSetup] Android detected, using persistentDataPath: {defaultPath}");
            }
            else if (isLinux)
            {
                string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string baseFolder = Path.Combine(userProfile, ".local", "share", "YASG");
                defaultPath = Path.Combine(baseFolder, "YASG");
            }
            else
            {
                string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                if (string.IsNullOrEmpty(roaming))
                {
                    string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    roaming = Path.Combine(userProfile ?? @"C:\Users\Default", "AppData", "Roaming");
                }
                string baseFolder = Path.Combine(roaming, "YASG");
                defaultPath = Path.Combine(baseFolder, "YASG");
            }

            // Delete everything inside dataPath to ensure a clean state
            if (Directory.Exists(defaultPath))
            {
                try
                {
                    // Delete all files
                    foreach (string file in Directory.GetFiles(defaultPath, "*", SearchOption.AllDirectories))
                    {
                        try { File.Delete(file); } catch { }
                    }
                    // Delete all subdirectories
                    foreach (string dir in Directory.GetDirectories(defaultPath))
                    {
                        try { Directory.Delete(dir, true); } catch { }
                    }
                    UnityEngine.Debug.Log($"[AutoSetup] Cleared all contents of dataPath: {defaultPath}");
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogWarning($"[AutoSetup] Failed to fully clear dataPath: {e.Message}");
                }
            }

            try { Directory.CreateDirectory(defaultPath); } catch { }
            PlayerPrefs.SetString("dataPath", defaultPath);
            PlayerPrefs.Save();
            if (selectedDataPath != null) selectedDataPath.text = defaultPath;
            UnityEngine.Debug.Log($"[AutoSetup] Re-set dataPath to: {defaultPath}");

            // Set up file logging
            string logPath = Path.Combine(Path.GetDirectoryName(Application.dataPath), "log.txt");
            try
            {
                // Clear existing log file
                File.WriteAllText(logPath, $"=== YASG Test Build Log - {DateTime.Now} ===\n\n");
                Application.logMessageReceived += (logString, stackTrace, type) =>
                {
                    try
                    {
                        string logEntry = $"[{DateTime.Now:HH:mm:ss}] [{type}] {logString}\n";
                        if (type == LogType.Exception || type == LogType.Error)
                        {
                            logEntry += $"  Stack: {stackTrace}\n";
                        }
                        File.AppendAllText(logPath, logEntry);
                    }
                    catch { } // Silently ignore logging errors
                };
                UnityEngine.Debug.Log($"[AutoSetup] File logging enabled at: {logPath}");
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning($"[AutoSetup] Failed to set up file logging: {e.Message}");
            }

            UnityEngine.Debug.Log("[AutoSetup] Testing build detected. Starting automatic setup.");
            StartCoroutine(AutoSetupCoroutine());
        }
    }

    private IEnumerator RunProcessCoroutine()
    {
        processIsRunning = true;
        UnityEngine.Debug.Log($"[RunProcessCoroutine] Starting process of type: {currentProcessType}");

        activeProcess = new Process();
        string dataPath = PlayerPrefs.GetString("dataPath");
        bool isLinux = Application.platform == RuntimePlatform.LinuxPlayer || Application.platform == RuntimePlatform.LinuxEditor;

        string pythonExe = isLinux ? Path.Combine(dataPath, "venv", "bin", "python3") : Path.Combine(dataPath, "venv", "Scripts", "python.exe");
        UnityEngine.Debug.Log($"[RunProcessCoroutine] Python path: {pythonExe}, Exists: {File.Exists(pythonExe)}");

        if (currentProcessType == ActiveProcessType.Preinstall)
        {
            string scriptName = isLinux ? "pyinstall.sh" : "pyinstall.bat";
            string scriptPath = Path.Combine(dataPath, "setuputilities", scriptName);
            if (!File.Exists(scriptPath))
            {
                UnityEngine.Debug.LogError($"Script not found at: {scriptPath}");
                QueueForMainThread(() => statusTextPreinstall.text = "Error: Script not found.");
                processIsRunning = false;
                yield break;
            }
            activeProcess.StartInfo.FileName = scriptPath;
            UnityEngine.Debug.Log($"[RunProcessCoroutine] Starting preinstall with script: {scriptPath}");
        }
        else if (currentProcessType == ActiveProcessType.FinalInstall)
        {
            string scriptPath = Path.Combine(dataPath, "setuputilities", "fullinstall.py");
            UnityEngine.Debug.Log($"[RunProcessCoroutine] FinalInstall script path: {scriptPath}, Exists: {File.Exists(scriptPath)}");
            UnityEngine.Debug.Log($"[RunProcessCoroutine] Method selected: {method}");

            if (!File.Exists(scriptPath))
            {
                UnityEngine.Debug.LogError($"Script not found at: {scriptPath}");
                QueueForMainThread(() => statusTextFinalInstall.text = "Error: Script not found.");
                processIsRunning = false;
                yield break;
            }
            activeProcess.StartInfo.FileName = pythonExe;
            activeProcess.StartInfo.Arguments = $" -u \"{scriptPath}\" {(method == "demucs" ? "true" : "false")}";
            if (method == "demucs")
            {
                PlayerPrefs.SetInt("demucsInstalled", 1);
                SettingsManager.Instance.SetSetting("VocalProcessingMethod", 1);
            }
            else
            {
                SettingsManager.Instance.SetSetting("VocalProcessingMethod", 0);
            }
            UnityEngine.Debug.Log($"[RunProcessCoroutine] Starting final install with command: {pythonExe} {activeProcess.StartInfo.Arguments}");
        }

        activeProcess.StartInfo.WorkingDirectory = dataPath;
        activeProcess.StartInfo.UseShellExecute = false;
        activeProcess.StartInfo.CreateNoWindow = true;
        activeProcess.StartInfo.RedirectStandardOutput = true;
        activeProcess.StartInfo.RedirectStandardError = true;
        activeProcess.EnableRaisingEvents = true;

        activeProcess.OutputDataReceived += (sender, args) =>
        {
            if (!string.IsNullOrEmpty(args.Data))
            {
                // We are on a background thread here. Queue the work.
                QueueForMainThread(() => ParseOutputLine(args.Data));
            }
        };

        activeProcess.ErrorDataReceived += (sender, args) =>
        {
            if (!string.IsNullOrEmpty(args.Data))
            {
                QueueForMainThread(() => ProcessErrorLine(args.Data));
            }
        };

        try
        {
            activeProcess.Start();
            activeProcess.BeginOutputReadLine();
            activeProcess.BeginErrorReadLine();
            UnityEngine.Debug.Log($"Process '{Path.GetFileName(activeProcess.StartInfo.FileName)}' started successfully.");
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogError($"Failed to start process: {e.Message}");
            QueueForMainThread(() =>
            {
                if (currentProcessType == ActiveProcessType.Preinstall) statusTextPreinstall.text = "Error: Failed to start.";
                if (currentProcessType == ActiveProcessType.FinalInstall) statusTextFinalInstall.text = "Error: Failed to start.";
            });
            processIsRunning = false;
            yield break;
        }

        // Wait here in the coroutine until the process exits. UI updates are handled by Update().
        while (!activeProcess.HasExited)
        {
            yield return null;
        }

        UnityEngine.Debug.Log($"Process finished with exit code: {activeProcess.ExitCode}.");

        // Save the process type before cleanup (cleanup resets it to None)
        ActiveProcessType completedProcessType = currentProcessType;
        CleanUpProcess();

        // If preinstall just completed and we need to start final install
        if (shouldStartFinalInstall && completedProcessType == ActiveProcessType.Preinstall)
        {
            shouldStartFinalInstall = false;
            UnityEngine.Debug.Log("[RunProcessCoroutine] Preinstall completed, starting final install.");
            yield return new WaitForSeconds(0.5f); // Small delay for page transition
            StartFinalInstall();
        }
    }

    private void ParseOutputLine(string line)
    {
        // Route to the correct parser based on the active process
        if (currentProcessType == ActiveProcessType.Preinstall)
        {
            if (preinstallConsole != null) preinstallConsole.AddLog(line);
            ParsePreinstallOutputLine(line);
        }
        else if (currentProcessType == ActiveProcessType.FinalInstall)
        {
            if (finalInstallConsole != null) finalInstallConsole.AddLog(line);
            ParseFinalInstallOutputLine(line);
        }
    }

    private void ParsePreinstallOutputLine(string line)
    {
        UnityEngine.Debug.Log($"[Preinstall] {line}");
        Match match = Regex.Match(line, @"\[\s*(\d{1,3})%\s*\]\s*(.*)");

        if (match.Success)
        {
            string message = match.Groups[2].Value.Trim();
            string percentageStr = match.Groups[1].Value;

            // Robustness Check: Only update UI if the references are not null.
            if (statusTextPreinstall != null)
            {
                statusTextPreinstall.text = message;
            }
            if (preinstallProgress != null && int.TryParse(percentageStr, out int percentage))
            {
                preinstallProgress.value = percentage / 100.0f;
            }
            if (message.Contains("Setup completed"))
            {
                preinstallProgress.value = 1f;
                preinstallPage.NextPage(); // Go to Full Install page
                // Set flag to start final install after this process exits
                shouldStartFinalInstall = true;
            }

        }
    }

    private void ParseFinalInstallOutputLine(string line)
    {
        UnityEngine.Debug.Log($"[FinalInstall] {line}");
        Match match = Regex.Match(line, @"^\s*\[\s*(\d{1,3})%\s*\]\s*(.*)");

        if (match.Success)
        {
            string percentageStr = match.Groups[1].Value;
            string message = match.Groups[2].Value.Trim();

            UnityEngine.Debug.Log($"[FinalInstall] Matched! Percentage: {percentageStr}, Message: {message}");

            if (statusTextFinalInstall != null && !string.IsNullOrEmpty(message))
            {
                statusTextFinalInstall.text = Regex.Replace(message, "-+", "").Trim();
            }
            if (finalInstallProgress != null && int.TryParse(percentageStr, out int percentage))
            {
                finalInstallProgress.value = percentage / 100.0f;
            }
        }
        else
        {
            if (line.StartsWith("SETUP_FFMPEG_PATH:"))
            {
                string newPath = line.Split(new[] { ':' }, 2)[1].Trim();
                UpdateProcessPath(newPath);
            }
            else
            {
                UnityEngine.Debug.LogWarning($"[FinalInstall] No match for line: {line}");
            }
        }
        if (line.Contains("Setup Complete!") || line.Contains("Setup Complete") || line.Contains("Installation complete"))
        {
            finalInstallPage.NextPage();
            if (completeFX != null) completeFX.Play();
            UnityEngine.Debug.Log("Final installation completed successfully.");
        }
    }

    private void UpdateProcessPath(string newPath)
    {
        try
        {
            string currentPath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Process);
            if (string.IsNullOrEmpty(currentPath))
            {
                currentPath = "";
            }

            // Determine if we should use case-insensitive comparison (Windows)
            bool isWindows = Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.WindowsEditor;
            StringComparison comparison = isWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

            string[] paths = currentPath.Split(Path.PathSeparator);
            foreach (string p in paths)
            {
                if (string.Equals(p.Trim(), newPath.Trim(), comparison))
                {
                    UnityEngine.Debug.Log($"[EnvUpdate] Path already contained: {newPath}");
                    return;
                }
            }

            string separator = Path.PathSeparator.ToString();
            // Ensure we don't start with a separator if current path is empty
            string updatedPath = string.IsNullOrEmpty(currentPath) ? newPath : currentPath + separator + newPath;

            Environment.SetEnvironmentVariable("PATH", updatedPath, EnvironmentVariableTarget.Process);
            UnityEngine.Debug.Log($"[EnvUpdate] Successfully added to PATH: {newPath}");
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogError($"[EnvUpdate] Failed to update PATH: {e.Message}");
        }
    }

    private void ProcessErrorLine(string line)
    {
        // Add all error lines to the console
        if (currentProcessType == ActiveProcessType.Preinstall && preinstallConsole != null)
        {
            preinstallConsole.AddLog(line);
        }
        else if (currentProcessType == ActiveProcessType.FinalInstall && finalInstallConsole != null)
        {
            finalInstallConsole.AddLog(line);
        }

        // Filter for the multi-line pip warning.
        if (line.Contains("WARNING: You are using pip version") || line.Contains("install --upgrade pip") || line.Contains("A new release of pip is available"))
        {
            UnityEngine.Debug.Log($"[Ignored Warning] {line}");
            return; // Exit without showing error on UI
        }

        // For any other real error, log it and update the UI.
        UnityEngine.Debug.LogError($"[Process Error] {line}");
        if (currentProcessType == ActiveProcessType.Preinstall && statusTextPreinstall != null)
        {
            statusTextPreinstall.text = "An error occurred. Check logs for details.";
        }
        else if (currentProcessType == ActiveProcessType.FinalInstall && statusTextFinalInstall != null)
        {
            statusTextFinalInstall.text = "An error occurred. Check logs for details.";
        }
    }

    private void HandleProcessCompletion(int exitCode)
    {
        if (currentProcessType == ActiveProcessType.Preinstall && statusTextPreinstall != null)
        {
            // Only show success if the last message wasn't an error.
            if (exitCode == 0 && !statusTextPreinstall.text.ToLower().Contains("error"))
            {
                statusTextPreinstall.text = "Setup completed successfully!";
                if (preinstallProgress != null) preinstallProgress.value = 1f;
            }
            else if (exitCode != 0)
            {
                statusTextPreinstall.text = "Setup failed. Check console for errors.";
            }
        }
    }




    private void CleanUpProcess()
    {
        if (activeProcess != null)
        {
            activeProcess.Close();
            activeProcess = null;
        }
        processIsRunning = false;
        currentProcessType = ActiveProcessType.None;
    }

    private void OnApplicationQuit()
    {
        if (activeProcess != null && !activeProcess.HasExited)
        {
            UnityEngine.Debug.Log("Application quitting, killing active process...");
            activeProcess.Kill();
        }
    }

    public void OpenFolderSelector()
    {
        var paths = StandaloneFileBrowser.OpenFolderPanel("Select Folder", "", false);
        if (paths.Length > 0 && !string.IsNullOrEmpty(paths[0]))
        {
            PlayerPrefs.SetString("dataPath", paths[0]);
            if (selectedDataPath != null) selectedDataPath.text = paths[0];
        }
    }

    public void ToggleDemucs()
    {
        method = "demucs";
        selectMethodButton.interactable = true; // 0.1686275f
        demucsButton.color = new Color(1f, 1f, 1f, 0.2980392f);
        demucsButton.OutlineColor = new Color(1f, 1f, 1f, 1f); // 0.772549f
        VRButton.color = new Color(1f, 1f, 1f, 0.1686275f);
        VRButton.OutlineColor = new Color(0f, 0f, 0f, 0.772549f);
    }

    public void ToggleVR()
    {
        method = "vr";
        selectMethodButton.interactable = true; // 0.1686275f
        VRButton.color = new Color(1f, 1f, 1f, 0.2980392f);
        VRButton.OutlineColor = new Color(1f, 1f, 1f, 1f); // 0.772549f
        demucsButton.color = new Color(1f, 1f, 1f, 0.1686275f);
        demucsButton.OutlineColor = new Color(0f, 0f, 0f, 0.772549f);
    }

    /// <summary>
    /// Called when the user confirms their vocal processing method selection.
    /// Routes to the appropriate setup path based on selection.
    /// </summary>
    public void ConfirmMethodSelection()
    {
        if (string.IsNullOrEmpty(method))
        {
            UnityEngine.Debug.LogWarning("No method selected!");
            return;
        }

        if (method == "vr")
        {
            // VocalRemover.org path: No Python needed, just create folders and complete
            UnityEngine.Debug.Log("[Setup] VocalRemover.org selected - skipping Python installation.");
            StartVocalRemoverSetup();
        }
        else if (method == "demucs")
        {
            // Demucs path: Need Python, preinstall, and final install
            UnityEngine.Debug.Log("[Setup] Demucs selected - starting full installation.");
            StartPreinstall();
            // Navigate to preinstall page so user can see progress
            if (methodSelectionPage != null && preinstallPage != null)
            {
                methodSelectionPage.GoToPage(preinstallPage);
            }
        }
    }

    /// <summary>
    /// Lightweight setup for VocalRemover.org users - no Python needed.
    /// Just creates necessary folders and goes to completion.
    /// </summary>
    private void StartVocalRemoverSetup()
    {
        string dataPath = PlayerPrefs.GetString("dataPath");
        if (string.IsNullOrEmpty(dataPath))
        {
            UnityEngine.Debug.LogError("Data path is not set!");
            return;
        }

        try
        {
            // Create only the essential folders
            string downloadsPath = Path.Combine(dataPath, "downloads");
            string outputPath = Path.Combine(dataPath, "output", "htdemucs");

            Directory.CreateDirectory(downloadsPath);
            Directory.CreateDirectory(outputPath);

            UnityEngine.Debug.Log($"[Setup] Created folders: {downloadsPath}, {outputPath}");

            // Set the vocal processing method to VocalRemover.org (0)
            SettingsManager.Instance.SetSetting("VocalProcessingMethod", 0);

            // Mark setup as NOT having demucs installed
            PlayerPrefs.SetInt("demucsInstalled", 0);
            PlayerPrefs.Save();

            // Navigate directly to the completion page
            if (methodSelectionPage != null && completionPage != null)
            {
                methodSelectionPage.GoToPage(completionPage);
            }
            else
            {
                UnityEngine.Debug.LogError("[Setup] methodSelectionPage or completionPage is not assigned in the inspector!");
            }
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogError($"[Setup] Failed to create folders: {e.Message}");
        }
    }

    public void StartFinalInstall()
    {
        if (processIsRunning)
        {
            UnityEngine.Debug.LogWarning("[StartFinalInstall] A process is already running.");
            return;
        }

        UnityEngine.Debug.Log("[StartFinalInstall] Starting final install process...");

        // Initial UI state
        if (statusTextFinalInstall != null) statusTextFinalInstall.text = "Starting...";
        if (finalInstallProgress != null) finalInstallProgress.value = 0;

        currentProcessType = ActiveProcessType.FinalInstall;
        StartCoroutine(RunProcessCoroutine());
    }

    public async void CompleteSetup()
    {
        PlayerPrefs.SetInt("setupDone", 1);
        PlayerPrefs.Save();

        UnityEngine.Debug.Log("Setup complete. Quitting application.");

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    /// <summary>
    /// Runs through the entire setup automatically for testing builds.
    /// Only called when Application.version starts with 'T'.
    /// </summary>
    private IEnumerator AutoSetupCoroutine()
    {
        UnityEngine.Debug.Log("[AutoSetup] Step 1: Clicking Next on welcome page");
        yield return new WaitForSeconds(0.5f);

        // Step 1: Click Next on first page
        if (welcomeNextButton != null)
        {
            welcomeNextButton.onClick.Invoke();
        }
        else if (welcomePage != null)
        {
            welcomePage.NextPage();
        }

        yield return new WaitForSeconds(0.5f);

        // Step 2: Click Agree on TOS page
        UnityEngine.Debug.Log("[AutoSetup] Step 2: Clicking Agree on TOS page");
        if (tosAgreeButton != null)
        {
            tosAgreeButton.onClick.Invoke();
        }
        else if (tosPage != null)
        {
            tosPage.NextPage();
        }

        yield return new WaitForSeconds(0.5f);

        // Step 3: Click path button to confirm data path (goes to method selection)
        UnityEngine.Debug.Log("[AutoSetup] Step 3: Clicking path button");
        if (pathButton != null)
        {
            pathButton.onClick.Invoke();
        }

        yield return new WaitForSeconds(0.5f);

        // Step 4: Select Demucs and confirm (this triggers preinstall)
        UnityEngine.Debug.Log("[AutoSetup] Step 4: Selecting Demucs and confirming method");
        ToggleDemucs();
        ConfirmMethodSelection();

        yield return new WaitForSeconds(0.5f);

        // Step 5: Wait for preinstall to finish
        UnityEngine.Debug.Log("[AutoSetup] Step 5: Waiting for preinstall to complete");
        yield return new WaitUntil(() => preinstallProgress != null && preinstallProgress.value >= 0.99f);
        yield return new WaitForSeconds(1f); // Extra buffer for page transition

        // Step 6: Wait for final install to complete
        UnityEngine.Debug.Log("[AutoSetup] Step 6: Waiting for final install to complete");
        yield return new WaitUntil(() => finalInstallProgress != null && finalInstallProgress.value >= 0.99f);
        yield return new WaitForSeconds(1f); // Extra buffer for completion animation

        // Step 7: Click "Go to menu"
        UnityEngine.Debug.Log("[AutoSetup] Step 7: Clicking 'Go to menu'");
        if (goToMenuButton != null)
        {
            goToMenuButton.onClick.Invoke();
        }
        else
        {
            CompleteSetup();
        }

        UnityEngine.Debug.Log("[AutoSetup] Automatic setup completed!");
    }
}