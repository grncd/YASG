using UnityEngine;
using System.Diagnostics;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine.UI;
using System;

public class SetupManager : MonoBehaviour
{
    [Header("Retrieved Credentials")]
    public string spdc;
    public string apikey;

    [Header("UI Elements")]
    public TextMeshProUGUI statusTextLogin;
    public Slider loginProgress;
    public SetupPage loginPage;
    public TextMeshProUGUI statusTextPreinstall;
    public Slider preinstallProgress;
    public SetupPage preinstallPage;

    // --- Private members for handling the process ---
    private Process activeProcess;
    private bool processIsRunning = false;
    private ActiveProcessType currentProcessType = ActiveProcessType.None;
    private enum ActiveProcessType { None, Login, Preinstall }

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
        StartCoroutine(RunProcessCoroutine());
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
    }

    private void ParsePreinstallOutputLine(string line)
    {
        UnityEngine.Debug.Log($"[Preinstall] {line}");
        Match match = Regex.Match(line, @"\[(\d{1,3})%\]\s*(.*)");

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
            if (message.Contains("Script finished. Closing browser."))
            {
                loginPage.NextPage();
            }
        }
    }

    private void ParseLoginOutputLine(string line)
    {
        UnityEngine.Debug.Log($"[Login] {line}");
        
        // Robustness Check: Ensure UI elements are valid before updating
        if(statusTextLogin == null || loginProgress == null) return;

        if (line.Contains("Please log in")) { statusTextLogin.text = "Waiting for you to log into Spotify..."; }
        else if (line.Contains("Redirected to open.spotify.com")) { statusTextLogin.text = "Login successful! Retrieving cookie..."; }
        else if (line.StartsWith("sp_dc cookie:")) { spdc = line.Split(new[] { ':' }, 2)[1].Trim(); statusTextLogin.text = "Cookie found! Generating API Key..."; }
        else if (line.StartsWith("Client Secret:")) { apikey = line.Split(new[] { ':' }, 2)[1].Trim(); statusTextLogin.text = "API Key retrieved!"; }
        else if (line.Contains("Attempting to create a new app...")) { loginProgress.value = 0.25f; }
        else if (line.Contains("Filling out app creation form...")) { loginProgress.value = 0.5f; }
        else if (line.Contains("Submitting form...")) { loginProgress.value = 0.75f; }
        else if (line.Contains("Copying client secret to clipboard...")) { loginProgress.value = 1f; }
    }

    private void ProcessErrorLine(string line)
    {
        // Filter for the multi-line pip warning.
        if (line.Contains("WARNING: You are using pip version") || line.Contains("install --upgrade pip"))
        {
            UnityEngine.Debug.Log($"[Ignored Warning] {line}");
            return; // Exit without showing error on UI
        }

        // For any other real error, log it and update the UI.
        UnityEngine.Debug.LogError($"[Process Error] {line}");
        if (currentProcessType == ActiveProcessType.Preinstall && statusTextPreinstall != null)
        {
            statusTextPreinstall.text = "An error occurred. Check console.";
        }
        else if (currentProcessType == ActiveProcessType.Login && statusTextLogin != null)
        {
             statusTextLogin.text = "An error occurred. Check console.";
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
}