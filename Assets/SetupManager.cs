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
    [Header("Retrieved Credentials")]
    public string spdc;
    private string apikey;
    private string clientID;
    private string method;
    public Animator transitionAnim;


    [Header("UI Elements")]
    public TextMeshProUGUI statusTextLogin;
    public Slider loginProgress;
    public SetupPage preLoginPage;
    public SetupPage loginPage;
    public ConsoleLogHandler loginConsole;
    public SetupPage manualLoginPage;
    public Button manualLoginPageButton;
    public TextMeshProUGUI statusTextPreinstall;
    public Slider preinstallProgress;
    public SetupPage preinstallPage;
    public ConsoleLogHandler preinstallConsole;
    public TextMeshProUGUI selectedDataPath;
    public Button selectDataPathButton;
    public Button selectMethodButton;
    public MPImage demucsButton;
    public MPImage VRButton;
    public TextMeshProUGUI statusTextFinalInstall;
    public Slider finalInstallProgress;
    public SetupPage finalInstallPage;
    public ConsoleLogHandler finalInstallConsole;
    public AudioSource audioSource;
    public AudioSource completeFX;

    [Header("Skip Login Option")]
    [Tooltip("If true, login pages will be skipped entirely (uses anonymous Spotify API)")]
    public bool skipLogin = true;
    public SetupPage postLoginPage;
    public SetupPage additionalLoginPage;

    [Header("Auto-Setup UI References (for testing builds)")]
    public SetupPage welcomePage;
    public SetupPage tosPage;
    public Button pathButton;
    public Button welcomeNextButton;
    public Button tosAgreeButton;
    public Button useSharedApiButton;
    public Button goToMenuButton;
    public Button demucsNextButton;


    private Process activeProcess;
    private bool processIsRunning = false;
    private ActiveProcessType currentProcessType = ActiveProcessType.None;
    private enum ActiveProcessType { None, Login, Preinstall, FinalInstall }

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

        // Shortcut: Ctrl+L on PreLoginPage to skip to Manual Login
        if (preLoginPage != null && preLoginPage.gameObject.activeInHierarchy)
        {
            if ((Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) && Input.GetKeyDown(KeyCode.L))
            {
                UnityEngine.Debug.Log("Shortcut detected: Skipping to Manual Login.");
                preLoginPage.gameObject.SetActive(false);
                if (manualLoginPage != null) manualLoginPage.gameObject.SetActive(true);
                // Ensure other pages are off just in case
                if (loginPage != null) loginPage.gameObject.SetActive(false);
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

        string batUrl = $"https://raw.githubusercontent.com/grncd/YASGsetuputilities/refs/heads/main/pyinstall{scriptExt}";
        string pyUrl = "https://raw.githubusercontent.com/grncd/YASGsetuputilities/refs/heads/main/spotifydc.py";
        string py2Url = "https://raw.githubusercontent.com/grncd/YASGsetuputilities/refs/heads/main/fullinstall.py";
        string py3Url = "https://raw.githubusercontent.com/grncd/YASGsetuputilities/refs/heads/main/updatechecker.py";

        string batPath = Path.Combine(setupUtilitiesPath, $"pyinstall{scriptExt}");
        string pyPath = Path.Combine(setupUtilitiesPath, "spotifydc.py");
        string py2Path = Path.Combine(setupUtilitiesPath, "fullinstall.py");
        string py3Path = Path.Combine(setupUtilitiesPath, "updatechecker.py");

        statusTextPreinstall.text = "Downloading setup files...";
        yield return StartCoroutine(DownloadFile(batUrl, batPath));
        yield return StartCoroutine(DownloadFile(pyUrl, pyPath));
        yield return StartCoroutine(DownloadFile(py2Url, py2Path));
        yield return StartCoroutine(DownloadFile(py3Url, py3Path));

        string lyricsScript = $"getlyrics{scriptExt}";
        string songScript = $"downloadsong{scriptExt}";

        string lyricsPath = Path.Combine(dataPath, lyricsScript);
        string songPath = Path.Combine(dataPath, songScript);

        yield return StartCoroutine(DownloadFile($"https://raw.githubusercontent.com/grncd/YASGsetuputilities/refs/heads/main/{lyricsScript}", lyricsPath));
        yield return StartCoroutine(DownloadFile($"https://raw.githubusercontent.com/grncd/YASGsetuputilities/refs/heads/main/{songScript}", songPath));

        Directory.CreateDirectory(Path.Combine(dataPath, "vocalremover", "input"));
        yield return StartCoroutine(DownloadFile("https://raw.githubusercontent.com/grncd/YASGsetuputilities/refs/heads/main/main.py", Path.Combine(dataPath, "vocalremover", "main.py")));
        yield return StartCoroutine(DownloadFile("https://raw.githubusercontent.com/grncd/YASGsetuputilities/refs/heads/main/vr.py", Path.Combine(dataPath, "vocalremover", "vr.py")));

        if (isLinux)
        {
            GrantExecutePermission(batPath);
            GrantExecutePermission(lyricsPath);
            GrantExecutePermission(songPath);
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
        if (/*!Application.isEditor*/ true)
        {
            // Ensure there's a sensible default (Unity game's data folder) if none set
            string defaultPath = PlayerPrefs.GetString("dataPath");

            // Debugging Linux path issues
            UnityEngine.Debug.Log($"Platform: {Application.platform}, Current DataPath: {defaultPath}");

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
                if (Application.platform == RuntimePlatform.LinuxPlayer || Application.platform == RuntimePlatform.LinuxEditor)
                {
                    // Explicitly force ~/.local/share/YASG/YASG to prevent ambiguity or switching to .config
                    string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    baseFolder = Path.Combine(userProfile, ".local", "share", "YASG");
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
                }

                defaultPath = Path.Combine(baseFolder, "YASG");

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
            string baseFolder;
            if (isLinux)
            {
                string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                baseFolder = Path.Combine(userProfile, ".local", "share", "YASG");
            }
            else
            {
                string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                if (string.IsNullOrEmpty(roaming))
                {
                    string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    roaming = Path.Combine(userProfile ?? @"C:\Users\Default", "AppData", "Roaming");
                }
                baseFolder = Path.Combine(roaming, "YASG");
            }
            string defaultPath = Path.Combine(baseFolder, "YASG");

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

    public void StartLogin()
    {
        if (processIsRunning)
        {
            UnityEngine.Debug.LogWarning("A process is already running.");
            return;
        }

        if (statusTextLogin != null) statusTextLogin.text = "Starting...";
        if (loginProgress != null) loginProgress.value = 0;

        apikey = "";
        clientID = "";
        spdc = "";

        currentProcessType = ActiveProcessType.Login;
        StartCoroutine(RunProcessCoroutine());
    }

    private IEnumerator RunProcessCoroutine()
    {
        processIsRunning = true;

        activeProcess = new Process();
        string dataPath = PlayerPrefs.GetString("dataPath");
        bool isLinux = Application.platform == RuntimePlatform.LinuxPlayer || Application.platform == RuntimePlatform.LinuxEditor;

        string pythonExe = isLinux ? Path.Combine(dataPath, "venv", "bin", "python3") : Path.Combine(dataPath, "venv", "Scripts", "python.exe");

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
        }
        else if (currentProcessType == ActiveProcessType.Login)
        {
            string scriptPath = Path.Combine(dataPath, "setuputilities", "spotifydc.py");
            if (!File.Exists(scriptPath))
            {
                UnityEngine.Debug.LogError($"Script not found at: {scriptPath}");
                QueueForMainThread(() => statusTextLogin.text = "Error: Script not found.");
                processIsRunning = false;
                yield break;
            }
            activeProcess.StartInfo.FileName = pythonExe;
            activeProcess.StartInfo.Arguments = $"-u \"{scriptPath}\"";
        }
        else if (currentProcessType == ActiveProcessType.FinalInstall)
        {
            string scriptPath = Path.Combine(dataPath, "setuputilities", "fullinstall.py");
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
                if (currentProcessType == ActiveProcessType.Login) statusTextLogin.text = "Error: Failed to start.";
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

        if (currentProcessType == ActiveProcessType.Login)
        {
            if (string.IsNullOrEmpty(apikey) || string.IsNullOrEmpty(clientID))
            {
                UnityEngine.Debug.Log("Login process finished without retrieving valid credentials. Switching to Manual Login.");
                QueueForMainThread(() => SwitchToManualLogin());
            }
        }


        CleanUpProcess();

    }

    private void ParseOutputLine(string line)
    {
        // Route to the correct parser based on the active process
        if (currentProcessType == ActiveProcessType.Preinstall)
        {
            if (preinstallConsole != null) preinstallConsole.AddLog(line);
            ParsePreinstallOutputLine(line);
        }
        else if (currentProcessType == ActiveProcessType.Login)
        {
            if (loginConsole != null) loginConsole.AddLog(line);
            ParseLoginOutputLine(line);
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
                if (skipLogin)
                {
                    preinstallPage.NextPage();
                    preLoginPage.NextPage();
                    loginPage.NextPage();
                    manualLoginPage.NextPage();
                    preinstallProgress.value = 1f;
                }
                else
                {
                    preinstallPage.NextPage();
                }
            }

        }
    }

    private void ParseLoginOutputLine(string line)
    {
        UnityEngine.Debug.Log($"[Login] {line}");

        if (line.Contains("Stopping process."))
        {
            UnityEngine.Debug.Log("['Stopping process.' detected] Killing process to trigger manual login fallback.");
            try
            {
                if (activeProcess != null && !activeProcess.HasExited)
                {
                    activeProcess.Kill();
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError($"Failed to kill process: {e.Message}");
            }
            return;
        }

        // Robustness Check: Ensure UI elements are valid before updating
        if (statusTextLogin == null || loginProgress == null) return;
        if (line.Contains("Script finished. Closing browser.") || line.Contains("Script finished successfully!"))
        {
            loginPage.NextPage();
            manualLoginPage.NextPage();
        }
        else if (line.Contains("Still on create page after app creation attempt."))
        {
            loginPage.NextPage();
        }
        if (line.Contains("Please log in")) { statusTextLogin.text = "Waiting for you to log into Spotify..."; }
        else if (line.Contains("Redirected to open.spotify.com")) { statusTextLogin.text = "Login successful! Retrieving cookie..."; }
        else if (line.StartsWith("sp_dc cookie:"))
        {
            spdc = line.Split(new[] { ':' }, 2)[1].Trim();
            // Create syrics folder and config.json inside it
            string syricsFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "syrics");
            Directory.CreateDirectory(syricsFolder);

            // Write config.json with the required content
            string configPath = Path.Combine(syricsFolder, "config.json");
            string configJson = "{\n" +
                $"    \"sp_dc\": \"{spdc}\",\n" +
                "    \"download_path\": \"downloads\",\n" +
                "    \"create_folder\": true,\n" +
                "    \"album_folder_name\": \"{name} - {artists}\",\n" +
                "    \"play_folder_name\": \"{name} - {owner}\",\n" +
                "    \"file_name\": \"{name}\",\n" +
                "    \"synced_lyrics\": true,\n" +
                "    \"force_download\": false\n" +
                "}";
            try
            {
                File.WriteAllText(configPath, configJson);
                UnityEngine.Debug.Log($"Created config.json at {configPath}");
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError($"Failed to write config.json: {e.Message}");
            }

            statusTextLogin.text = "Cookie found! Generating API Keys...";
            // Save spdc to key.txt in dataPath
            string dataPath = PlayerPrefs.GetString("dataPath");
            string keyFilePath = Path.Combine(dataPath, "key.txt");
            try
            {
                File.WriteAllText(keyFilePath, spdc + Environment.NewLine);
                UnityEngine.Debug.Log($"Saved spdc to {keyFilePath}");
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError($"Failed to save spdc to key.txt: {e.Message}");
            }
        }
        else if (line.StartsWith("Client Secret:")) { apikey = line.Split(new[] { ':' }, 2)[1].Trim(); statusTextLogin.text = "API Key retrieved!"; PlayerPrefs.SetString("APIKEY", apikey); }
        else if (line.StartsWith("Client ID:")) { clientID = line.Split(new[] { ':' }, 2)[1].Trim(); statusTextLogin.text = "Client ID retrieved!"; PlayerPrefs.SetString("CLIENTID", clientID); }
        else if (line.Contains("Extracting bearer token from network traffic...")) { loginProgress.value = 0.25f; }
        else if (line.Contains("Checking TOS acceptance status...")) { loginProgress.value = 0.5f; }
        else if (line.Contains("Creating Spotify developer application...")) { loginProgress.value = 0.75f; }
        else if (line.Contains("Application created successfully!")) { loginProgress.value = 1f; }
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
        if (line.Contains("Setup Complete!"))
        {
            finalInstallPage.NextPage();
            completeFX.Play();
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
        else if (currentProcessType == ActiveProcessType.Login && loginConsole != null)
        {
            loginConsole.AddLog(line);
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
        else if (currentProcessType == ActiveProcessType.Login && statusTextLogin != null)
        {
            statusTextLogin.text = "An error occurred. You might need to use another account.";
            loginPage.NextPage();
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
        else if (currentProcessType == ActiveProcessType.Login && statusTextLogin != null)
        {
            if (exitCode == 0 && !string.IsNullOrEmpty(spdc) && !string.IsNullOrEmpty(apikey))
            {
                statusTextLogin.text = "Success! Credentials have been saved.";
            }
            else
            {
                statusTextLogin.text = "Process finished, but failed to get credentials.";
            }
        }
    }

    private void SwitchToManualLogin()
    {
        if (loginPage != null) loginPage.gameObject.SetActive(false);
        if (manualLoginPage != null) manualLoginPage.gameObject.SetActive(true);
    }


    public void SkipLogin()
    {
        preinstallProgress.value = 1f;
        preLoginPage.NextPage();
        loginPage.NextPage();
        manualLoginPage.NextPage();
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

    public void StartFinalInstall()
    {
        if (processIsRunning)
        {
            UnityEngine.Debug.LogWarning("A process is already running.");
            return;
        }

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

    public void ManualAPIKey(string key)
    {
        PlayerPrefs.SetString("APIKEY", key);
        if ((PlayerPrefs.GetString("APIKEY").Length == 32 || PlayerPrefs.GetString("APIKEY").Length == 16) && (PlayerPrefs.GetString("CLIENTID").Length == 32 || PlayerPrefs.GetString("CLIENTID").Length == 16))
        {
            manualLoginPageButton.interactable = true;
        }
    }

    public void ManualClientID(string id)
    {
        PlayerPrefs.SetString("CLIENTID", id);
        if ((PlayerPrefs.GetString("APIKEY").Length == 32 || PlayerPrefs.GetString("APIKEY").Length == 16) && (PlayerPrefs.GetString("CLIENTID").Length == 32 || PlayerPrefs.GetString("CLIENTID").Length == 16))
        {
            manualLoginPageButton.interactable = true;
        }
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

        // Step 2.5: Click path button to confirm data path
        UnityEngine.Debug.Log("[AutoSetup] Step 2.5: Clicking path button");
        if (pathButton != null)
        {
            pathButton.onClick.Invoke();
        }

        // Step 3: Wait for preinstall to finish
        UnityEngine.Debug.Log("[AutoSetup] Step 3: Waiting for preinstall to complete");
        yield return new WaitUntil(() => preinstallProgress != null && preinstallProgress.value >= 0.99f);
        yield return new WaitForSeconds(1f); // Extra buffer for page transition

        // Step 4: Skip login entirely (using anonymous API)
        UnityEngine.Debug.Log("[AutoSetup] Step 4: Skipping login (using anonymous Spotify API)");
        SkipLogin();

        yield return new WaitForSeconds(0.5f);

        // Step 6: Select Demucs and click Next
        UnityEngine.Debug.Log("[AutoSetup] Step 6: Selecting Demucs and starting final install");
        ToggleDemucs();
        demucsNextButton.onClick.Invoke();
        yield return new WaitForSeconds(0.3f);

        // Step 7: Wait for final install to complete
        UnityEngine.Debug.Log("[AutoSetup] Step 7: Waiting for final install to complete");
        yield return new WaitUntil(() => finalInstallProgress != null && finalInstallProgress.value >= 0.99f);
        yield return new WaitForSeconds(1f); // Extra buffer for completion animation

        // Step 8: Click "Go to menu"
        UnityEngine.Debug.Log("[AutoSetup] Step 8: Clicking 'Go to menu'");
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