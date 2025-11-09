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
    public SetupPage loginPage;
    public SetupPage manualLoginPage;
    public Button manualLoginPageButton;
    public TextMeshProUGUI statusTextPreinstall;
    public Slider preinstallProgress;
    public SetupPage preinstallPage;
    public TextMeshProUGUI selectedDataPath;
    public Button selectDataPathButton;
    public Button selectMethodButton;
    public MPImage demucsButton;
    public MPImage VRButton;
    public TextMeshProUGUI statusTextFinalInstall;
    public Slider finalInstallProgress;
    public SetupPage finalInstallPage;
    public AudioSource audioSource;
    public AudioSource completeFX;


    // --- Private members for handling the process ---
    private Process activeProcess;
    private bool processIsRunning = false;
    private ActiveProcessType currentProcessType = ActiveProcessType.None;
    private enum ActiveProcessType { None, Login, Preinstall, FinalInstall }

    // --- Main Thread Dispatcher ---
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

    // --- Public Methods for UI Buttons ---

    public void Quit()
    {
        OnApplicationQuit(); // Ensure process is killed
        Application.Quit();
    }

    private void Awake()
    {
        if (PlayerPrefs.GetInt("setupDone") == 1)
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

        // Initial UI state
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

        string batUrl = "https://raw.githubusercontent.com/grncd/YASGsetuputilities/refs/heads/main/pyinstall.bat";
        string pyUrl = "https://raw.githubusercontent.com/grncd/YASGsetuputilities/refs/heads/main/spotifydc.py";
        string py2Url = "https://raw.githubusercontent.com/grncd/YASGsetuputilities/refs/heads/main/fullinstall.py";
        string py3Url = "https://raw.githubusercontent.com/grncd/YASGsetuputilities/refs/heads/main/updatechecker.py";
        string batPath = Path.Combine(setupUtilitiesPath, "pyinstall.bat");
        string pyPath = Path.Combine(setupUtilitiesPath, "spotifydc.py");
        string py2Path = Path.Combine(setupUtilitiesPath, "fullinstall.py");
        string py3Path = Path.Combine(setupUtilitiesPath, "updatechecker.py");
        statusTextPreinstall.text = "Downloading setup files...";
        yield return StartCoroutine(DownloadFile(batUrl, batPath));
        yield return StartCoroutine(DownloadFile(pyUrl, pyPath));
        yield return StartCoroutine(DownloadFile(py2Url, py2Path));
        yield return StartCoroutine(DownloadFile(py3Url, py3Path));
        yield return StartCoroutine(DownloadFile("https://raw.githubusercontent.com/grncd/YASGsetuputilities/refs/heads/main/getlyrics.bat", Path.Combine(dataPath, "getlyrics.bat")));
        yield return StartCoroutine(DownloadFile("https://raw.githubusercontent.com/grncd/YASGsetuputilities/refs/heads/main/downloadsong.bat", Path.Combine(dataPath, "downloadsong.bat")));
        Directory.CreateDirectory(Path.Combine(dataPath, "vocalremover", "input"));
        yield return StartCoroutine(DownloadFile("https://raw.githubusercontent.com/grncd/YASGsetuputilities/refs/heads/main/main.py", Path.Combine(dataPath, "vocalremover", "main.py")));
        yield return StartCoroutine(DownloadFile("https://raw.githubusercontent.com/grncd/YASGsetuputilities/refs/heads/main/vr.py", Path.Combine(dataPath, "vocalremover", "vr.py")));

        StartCoroutine(RunProcessCoroutine());
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
        // Ensure there's a sensible default (Unity game's data folder) if none set
        string defaultPath = PlayerPrefs.GetString("dataPath");
        if (string.IsNullOrEmpty(defaultPath))
        {
            // Use the user's Roaming AppData folder so the path works regardless of username.
            string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrEmpty(roaming))
            {
                // Fallback: construct a plausible Roaming path from the user profile.
                string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                roaming = Path.Combine(userProfile ?? @"C:\Users\Default", "AppData", "Roaming");
            }
            defaultPath = Path.Combine(roaming, "YASG", "YASG");

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
            ClearFolder(defaultPath);
        }

        // Update UI to show current/default path before opening selector
        if (selectedDataPath != null) selectedDataPath.text = defaultPath;
        selectDataPathButton.interactable = true;
    }

    public void StartLogin()
    {
        if (processIsRunning)
        {
            UnityEngine.Debug.LogWarning("A process is already running.");
            return;
        }

        // Initial UI state
        if (statusTextLogin != null) statusTextLogin.text = "Starting...";
        if (loginProgress != null) loginProgress.value = 0;

        currentProcessType = ActiveProcessType.Login;
        StartCoroutine(RunProcessCoroutine());
    }

    // --- Core Process Handling Coroutine ---

    private IEnumerator RunProcessCoroutine()
    {
        processIsRunning = true;

        activeProcess = new Process();
        string dataPath = PlayerPrefs.GetString("dataPath");

        // Configure the process based on which button was pressed
        if (currentProcessType == ActiveProcessType.Preinstall)
        {
            string scriptPath = Path.Combine(dataPath, "setuputilities", "pyinstall.bat");
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
            activeProcess.StartInfo.FileName = Path.Combine(dataPath, "venv", "Scripts", "python.exe");
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
            activeProcess.StartInfo.FileName = Path.Combine(dataPath, "venv", "Scripts", "python.exe");
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

        // Common process settings
        activeProcess.StartInfo.UseShellExecute = false;
        activeProcess.StartInfo.CreateNoWindow = true;
        activeProcess.StartInfo.RedirectStandardOutput = true;
        activeProcess.StartInfo.RedirectStandardError = true;
        activeProcess.EnableRaisingEvents = true;

        // --- Event Handlers that use the Main Thread Dispatcher ---
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

        // Queue a final action to handle completion status
        //QueueForMainThread(() => HandleProcessCompletion(activeProcess.ExitCode));

        CleanUpProcess();

    }

    // --- Parsers and Handlers (now executed by the main thread) ---

    private void ParseOutputLine(string line)
    {
        // Route to the correct parser based on the active process
        if (currentProcessType == ActiveProcessType.Preinstall)
        {
            ParsePreinstallOutputLine(line);
        }
        else if (currentProcessType == ActiveProcessType.Login)
        {
            ParseLoginOutputLine(line);
        }
        else if (currentProcessType == ActiveProcessType.FinalInstall)
        {
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
                preinstallPage.NextPage();
            }

        }
    }

    private void ParseLoginOutputLine(string line)
    {
        UnityEngine.Debug.Log($"[Login] {line}");

        // Robustness Check: Ensure UI elements are valid before updating
        if (statusTextLogin == null || loginProgress == null) return;
        if (line.Contains("Script finished. Closing browser."))
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

            statusTextLogin.text = "Cookie found! Generating API Key...";
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
        else if (line.Contains("Attempting to create a new app...")) { loginProgress.value = 0.25f; }
        else if (line.Contains("Filling out app creation form...")) { loginProgress.value = 0.5f; }
        else if (line.Contains("Submitting form...")) { loginProgress.value = 0.75f; }
        else if (line.Contains("Copying client secret to clipboard...")) { loginProgress.value = 1f; }
        else if (line.Contains("Interstitial button found, clicking it...")) { statusTextLogin.text = "Please check your email and click the link to verify!"; }
        else if (line.Contains("Interstitial button is gone, proceeding...")) { statusTextLogin.text = "Email verified!"; }
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
            UnityEngine.Debug.LogWarning($"[FinalInstall] No match for line: {line}");
        }
        if (line.Contains("Setup Complete!"))
        {
            finalInstallPage.NextPage();
            completeFX.Play();
            UnityEngine.Debug.Log("Final installation completed successfully.");
        }
    }

    private void ProcessErrorLine(string line)
    {
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

    // --- Cleanup ---

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
        transitionAnim.Play("Outro");
        if (audioSource != null && audioSource.isPlaying)
        {
            float startVolume = audioSource.volume;
            float duration = 2.5f;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                audioSource.volume = Mathf.Lerp(startVolume, 0f, elapsed / duration);
                await Task.Yield();
            }
            audioSource.volume = 0f;
        }
        await Task.Delay(TimeSpan.FromSeconds(4f));
        UnityEngine.SceneManagement.SceneManager.LoadScene("Menu");
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

}