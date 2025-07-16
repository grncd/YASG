using UnityEngine;
using System.Diagnostics; // Required for Process
using System.IO;         // Required for Path
using System.Collections;
using System.Collections.Generic; // Required for Queue
using System.Threading;
using TMPro;
using UnityEngine.UI;      // Required for locking

public class SetupManager : MonoBehaviour
{
    // These will be populated by the script
    [Header("Retrieved Credentials")]
    [Tooltip("The sp_dc cookie from Spotify.")]
    public string spdc;
    [Tooltip("The Client Secret from the Spotify Developer App.")]
    public string apikey;

    [Header("UI Elements")]
    public TextMeshProUGUI statusTextLogin;
    public Slider loginProgress;
    public TextMeshProUGUI statusTextPreinstall;
    public Slider preinstallProgress;

    // --- Private members for handling the process ---
    private Process pythonProcess;
    private bool processStarted = false;

    // Thread-safe queues to hold data from the Python process.
    private readonly Queue<string> outputQueue = new Queue<string>();
    private readonly Queue<string> errorQueue = new Queue<string>();

    public void Quit()
    {
        if (pythonProcess != null && !pythonProcess.HasExited)
        {
            pythonProcess.Kill();
        }
        Application.Quit();
    }

    public void PreinstallProgress()
    {
        // HERE
    }

    public void StartLogin()
    {
        if (processStarted)
        {
            UnityEngine.Debug.LogWarning("Login process is already running.");
            return;
        }
        StartCoroutine(RunPythonLoginProcess());
    }

    private IEnumerator RunPythonLoginProcess()
    {
        processStarted = true;
        UnityEngine.Debug.Log("Starting Spotify login process...");

        string dataPath = PlayerPrefs.GetString("dataPath");
        if (string.IsNullOrEmpty(dataPath))
        {
            UnityEngine.Debug.LogError("dataPath not found in PlayerPrefs! Cannot locate the python script.");
            processStarted = false;
            yield break;
        }
        
        string scriptPath = Path.Combine(dataPath, "setuputilities", "spotifydc.py");
        
        if (!File.Exists(scriptPath))
        {
            UnityEngine.Debug.LogError($"Python script not found at path: {scriptPath}");
            processStarted = false;
            yield break;
        }
        
        string pythonExecutable = "python";

        pythonProcess = new Process();
        pythonProcess.StartInfo.FileName = pythonExecutable;

        // --- KEY CHANGE HERE ---
        // The -u flag is critical. It forces Python to run in unbuffered mode,
        // ensuring that print() statements are sent to stdout immediately
        // instead of being buffered until the script exits.
        pythonProcess.StartInfo.Arguments = $"-u \"{scriptPath}\"";

        pythonProcess.StartInfo.UseShellExecute = false;
        pythonProcess.StartInfo.CreateNoWindow = true;
        pythonProcess.StartInfo.RedirectStandardOutput = true;
        pythonProcess.StartInfo.RedirectStandardError = true;

        pythonProcess.OutputDataReceived += (sender, args) =>
        {
            if (args.Data != null)
            {
                lock (outputQueue)
                {
                    outputQueue.Enqueue(args.Data);
                }
            }
        };

        pythonProcess.ErrorDataReceived += (sender, args) =>
        {
            if (args.Data != null)
            {
                lock (errorQueue)
                {
                    errorQueue.Enqueue(args.Data);
                }
            }
        };

        try
        {
            pythonProcess.Start();
            pythonProcess.BeginOutputReadLine();
            pythonProcess.BeginErrorReadLine();
        }
        catch (System.Exception e)
        {
            UnityEngine.Debug.LogError($"Failed to start python process: {e.Message}");
            UnityEngine.Debug.LogError("Is Python installed and in your system's PATH?");
            processStarted = false;
            yield break;
        }

        UnityEngine.Debug.Log("Python script started. Please follow instructions in the Chrome window.");

        while (!pythonProcess.HasExited)
        {
            ProcessQueues();
            yield return null;
        }

        ProcessQueues(); 

        UnityEngine.Debug.Log($"Python process finished with exit code: {pythonProcess.ExitCode}");
        if (string.IsNullOrEmpty(spdc) || string.IsNullOrEmpty(apikey))
        {
            UnityEngine.Debug.LogWarning("Process finished, but one or more credentials were not found. Check error logs.");
        }
        else
        {
            UnityEngine.Debug.Log("SUCCESS: All credentials retrieved!");
            statusTextLogin.text = "Success! Credentials have been saved.";
        }

        pythonProcess.Close();
        pythonProcess = null;
        processStarted = false;
    }

    private void ProcessQueues()
    {
        while (outputQueue.Count > 0)
        {
            string line;
            lock (outputQueue)
            {
                line = outputQueue.Dequeue();
            }
            ParseOutputLine(line);
        }
        
        while (errorQueue.Count > 0)
        {
            string line;
            lock (errorQueue)
            {
                line = errorQueue.Dequeue();
            }
            UnityEngine.Debug.LogError($"[Python Error] {line}");
        }
    }

    private void ParseOutputLine(string line)
    {
        if (string.IsNullOrEmpty(line)) return;

        UnityEngine.Debug.Log($"[Python] {line}");

        // Here we can update the status text based on the python output
        if (line.Contains("Please log in"))
        {
            statusTextLogin.text = "Waiting for you to log into Spotify...";
        }
        else if (line.Contains("Redirected to open.spotify.com"))
        {
            statusTextLogin.text = "Login successful! Retrieving cookie...";
        }
        else if (line.StartsWith("sp_dc cookie:"))
        {
            spdc = line.Split(new[] { ':' }, 2)[1].Trim();
            UnityEngine.Debug.Log($"<color=green>SP_DC Cookie captured!</color>");
            statusTextLogin.text = "Cookie found! Generating your API Key...";
        }
        else if (line.StartsWith("Client Secret:"))
        {
            apikey = line.Split(new[] { ':' }, 2)[1].Trim();
            UnityEngine.Debug.Log($"<color=green>API Key (Client Secret) captured!</color>");
            statusTextLogin.text = "API Key retrieved!";
        }
        else if (line.Contains("Attempting to create a new app..."))
        {
            loginProgress.value = 0.25f;
        }
        else if (line.Contains("Filling out app creation form..."))
        {
            loginProgress.value = 0.5f;
        }
        else if (line.Contains("Submitting form..."))
        {
            loginProgress.value = 0.75f;
        }
        else if (line.Contains("Copying client secret to clipboard..."))
        {
            loginProgress.value = 1f;
        }
    }

    private void OnApplicationQuit()
    {
        if (pythonProcess != null && !pythonProcess.HasExited)
        {
            UnityEngine.Debug.Log("Application quitting, killing python process...");
            pythonProcess.Kill();
        }
    }
}