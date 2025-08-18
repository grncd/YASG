using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using System.Diagnostics;
using UnityEngine.UI;
using System.Text.RegularExpressions;

public class WebServerManager : MonoBehaviour
{
    [Header("Web Server Settings")]
    public int port = 8080;
    public string ngrokPath = "";
    public string ngrokAuthToken = "2J91uLLuuBT4RcZnFwkJlwEFhCv_5o6Pd1XgjfxK1TsANahei";
    
    [Header("UI References")]
    public Image qrCodeImage;
    
    private HttpListener httpListener;
    private bool isServerRunning = false;
    private Process ngrokProcess;
    private string publicUrl = "";
    private LevelResourcesCompiler levelCompiler;
    
    [System.Serializable]
    public class SongRequest
    {
        public string url;
        public string name;
        public string artist;
        public string length;
        public string cover;
        public List<string> players;
        public List<int> difficulties;
        public List<bool> micToggles;
    }
    
    [System.Serializable]
    public class QueueResponse
    {
        public List<QueueItem> queue;
        public string currentSong;
    }
    
    [System.Serializable]
    public class QueueItem
    {
        public string name;
        public string artist;
        public string length;
        public string cover;
        public List<string> players;
        public bool processed;
    }
    
    [System.Serializable]
    public class NgrokTunnel
    {
        public string public_url;
        public string proto;
        public NgrokConfig config;
    }
    
    [System.Serializable]
    public class NgrokConfig
    {
        public string addr;
    }
    
    [System.Serializable]
    public class NgrokResponse
    {
        public List<NgrokTunnel> tunnels;
    }

    private void Start()
    {
        levelCompiler = LevelResourcesCompiler.Instance;
        
        // Set default ngrok path if not specified
        if (string.IsNullOrEmpty(ngrokPath))
        {
            ngrokPath = Path.Combine(PlayerPrefs.GetString("dataPath"), "ngrok.exe");
        }
    }

    public async void StartWebServer()
    {
        if (isServerRunning) return;
        
        try
        {
            // Start HTTP server
            httpListener = new HttpListener();
            httpListener.Prefixes.Add($"http://localhost:{port}/");
            httpListener.Prefixes.Add($"http://127.0.0.1:{port}/");
            httpListener.Start();
            isServerRunning = true;
            
            UnityEngine.Debug.Log($"Web server started on port {port}");
            
            // Start ngrok tunnel
            await StartNgrokTunnel();
            
            // Start handling requests
            StartCoroutine(HandleRequests());
            
            // Generate QR code
            await GenerateQRCode();
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogError($"Failed to start web server: {e.Message}");
        }
    }

    public void StopWebServer()
    {
        if (!isServerRunning) return;
        
        isServerRunning = false;
        
        // Stop HTTP server
        if (httpListener != null)
        {
            httpListener.Stop();
            httpListener.Close();
            httpListener = null;
        }
        
        // Stop ngrok
        if (ngrokProcess != null && !ngrokProcess.HasExited)
        {
            ngrokProcess.Kill();
            ngrokProcess = null;
        }
        
        publicUrl = "";
        UnityEngine.Debug.Log("Web server stopped");
    }

    private async Task AuthenticateNgrok()
    {
        try
        {
            if (string.IsNullOrEmpty(ngrokAuthToken))
            {
                UnityEngine.Debug.LogError("Ngrok auth token is not set!");
                return;
            }

            UnityEngine.Debug.Log("Authenticating ngrok...");
            
            ProcessStartInfo authPsi = new ProcessStartInfo
            {
                FileName = ngrokPath,
                Arguments = $"authtoken {ngrokAuthToken}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (Process authProcess = new Process { StartInfo = authPsi })
            {
                authProcess.Start();
                
                string output = await authProcess.StandardOutput.ReadToEndAsync();
                string error = await authProcess.StandardError.ReadToEndAsync();
                
                authProcess.WaitForExit();
                
                if (authProcess.ExitCode == 0)
                {
                    UnityEngine.Debug.Log("Ngrok authentication successful");
                }
                else
                {
                    UnityEngine.Debug.LogError($"Ngrok authentication failed: {error}");
                    if (!string.IsNullOrEmpty(output))
                    {
                        UnityEngine.Debug.LogError($"Ngrok auth output: {output}");
                    }
                }
            }
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogError($"Failed to authenticate ngrok: {e.Message}");
        }
    }

    private async Task StartNgrokTunnel()
    {
        try
        {
            // Check if ngrok exists
            if (!File.Exists(ngrokPath))
            {
                UnityEngine.Debug.LogError($"Ngrok not found at: {ngrokPath}");
                UnityEngine.Debug.LogError("Please download ngrok from https://ngrok.com/download and place it in your data folder");
                return;
            }

            // Authenticate ngrok first
            await AuthenticateNgrok();

            // Create ngrok config file to handle host header properly
            string configPath = Path.Combine(PlayerPrefs.GetString("dataPath"), "ngrok.yml");
            string configContent = $@"version: ""2""
authtoken: {ngrokAuthToken}
tunnels:
  yasg:
    proto: http
    addr: {port}
    host_header: rewrite";
            
            File.WriteAllText(configPath, configContent);
            UnityEngine.Debug.Log($"Created ngrok config at: {configPath}");
            
            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = ngrokPath,
                Arguments = $"start yasg --config \"{configPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            UnityEngine.Debug.Log($"Starting ngrok with command: {psi.FileName} {psi.Arguments}");

            ngrokProcess = new Process { StartInfo = psi };
            
            // Add event handlers to capture ngrok output
            ngrokProcess.OutputDataReceived += (sender, args) =>
            {
                if (!string.IsNullOrEmpty(args.Data))
                {
                    UnityEngine.Debug.Log($"[Ngrok Output] {args.Data}");
                }
            };
            
            ngrokProcess.ErrorDataReceived += (sender, args) =>
            {
                if (!string.IsNullOrEmpty(args.Data))
                {
                    UnityEngine.Debug.LogError($"[Ngrok Error] {args.Data}");
                }
            };
            
            ngrokProcess.Start();
            ngrokProcess.BeginOutputReadLine();
            ngrokProcess.BeginErrorReadLine();
            
            UnityEngine.Debug.Log("Ngrok process started, waiting for tunnel establishment...");
            
            // Wait longer for ngrok to establish tunnel
            await Task.Delay(5000);
            
            // Check if process is still running
            if (ngrokProcess.HasExited)
            {
                UnityEngine.Debug.LogError($"Ngrok process exited with code: {ngrokProcess.ExitCode}");
                return;
            }
            
            // Get the public URL from ngrok API
            await GetNgrokUrl();
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogError($"Failed to start ngrok: {e.Message}");
            UnityEngine.Debug.LogError($"Stack trace: {e.StackTrace}");
        }
    }

    private async Task GetNgrokUrl()
    {
        try
        {
            UnityEngine.Debug.Log("Attempting to get ngrok URL from API...");
            
            using (WebClient client = new WebClient())
            {
                for (int attempt = 1; attempt <= 3; attempt++)
                {
                    try
                    {
                        UnityEngine.Debug.Log($"Attempt {attempt}: Connecting to ngrok API at http://localhost:4040/api/tunnels");
                        string response = await client.DownloadStringTaskAsync("http://localhost:4040/api/tunnels");
                        
                        UnityEngine.Debug.Log($"Ngrok API response: {response}");
                        
                        // Manual JSON parsing for ngrok response since Unity's JsonUtility has limitations
                        if (response.Contains("\"public_url\""))
                        {
                            var match = Regex.Match(response, "\"public_url\"\\s*:\\s*\"([^\"]+)\"");
                            if (match.Success)
                            {
                                publicUrl = match.Groups[1].Value;
                                UnityEngine.Debug.Log($"Ngrok tunnel established: {publicUrl}");
                                return;
                            }
                        }
                        else if (response.Contains("\"tunnels\":[]"))
                        {
                            UnityEngine.Debug.LogWarning("Ngrok API returned empty tunnels array");
                        }
                        
                        if (attempt < 3)
                        {
                            UnityEngine.Debug.Log($"Attempt {attempt} failed, retrying in 2 seconds...");
                            await Task.Delay(2000);
                        }
                    }
                    catch (WebException webEx)
                    {
                        UnityEngine.Debug.LogError($"Attempt {attempt} - Web exception: {webEx.Message}");
                        if (attempt < 3)
                        {
                            await Task.Delay(2000);
                        }
                    }
                }
                
                UnityEngine.Debug.LogError("Failed to get ngrok URL after 3 attempts");
            }
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogError($"Failed to get ngrok URL: {e.Message}");
            UnityEngine.Debug.LogError($"Stack trace: {e.StackTrace}");
        }
    }

    private IEnumerator HandleRequests()
    {
        while (isServerRunning)
        {
            if (httpListener.IsListening)
            {
                var contextTask = Task.Run(() => httpListener.GetContext());
                
                while (!contextTask.IsCompleted && isServerRunning)
                {
                    yield return null;
                }
                
                if (contextTask.IsCompleted && !contextTask.IsFaulted)
                {
                    HttpListenerContext context = contextTask.Result;
                    ProcessRequest(context);
                }
            }
            yield return null;
        }
    }

    private void ProcessRequest(HttpListenerContext context)
    {
        HttpListenerRequest request = context.Request;
        HttpListenerResponse response = context.Response;
        
        try
        {
            string responseString = "";
            
            if (request.Url.AbsolutePath == "/" && request.HttpMethod == "GET")
            {
                responseString = GetWebPageHTML();
                response.ContentType = "text/html";
            }
            else if (request.Url.AbsolutePath == "/api/queue" && request.HttpMethod == "GET")
            {
                responseString = GetQueueJSON();
                response.ContentType = "application/json";
            }
            else if (request.Url.AbsolutePath == "/api/add-song" && request.HttpMethod == "POST")
            {
                string postData = "";
                using (StreamReader reader = new StreamReader(request.InputStream, request.ContentEncoding))
                {
                    postData = reader.ReadToEnd();
                }
                
                responseString = ProcessSongRequest(postData);
                response.ContentType = "application/json";
            }
            else if (request.Url.AbsolutePath.StartsWith("/static/"))
            {
                // Handle static files (CSS, JS, etc.)
                string filePath = request.Url.AbsolutePath.Substring(8); // Remove "/static/"
                responseString = GetStaticFile(filePath);
                
                if (filePath.EndsWith(".css"))
                    response.ContentType = "text/css";
                else if (filePath.EndsWith(".js"))
                    response.ContentType = "application/javascript";
            }
            else
            {
                response.StatusCode = 404;
                responseString = "404 - Not Found";
            }
            
            // Enable CORS
            response.Headers.Add("Access-Control-Allow-Origin", "*");
            response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");
            
            byte[] buffer = Encoding.UTF8.GetBytes(responseString);
            response.ContentLength64 = buffer.Length;
            response.OutputStream.Write(buffer, 0, buffer.Length);
            response.OutputStream.Close();
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogError($"Error processing request: {e.Message}");
            response.StatusCode = 500;
            response.Close();
        }
    }

    private string GetQueueJSON()
    {
        QueueResponse queueResponse = new QueueResponse
        {
            queue = new List<QueueItem>(),
            currentSong = "None"
        };
        
        if (levelCompiler != null && levelCompiler.mainQueue != null)
        {
            foreach (var queueObj in levelCompiler.mainQueue)
            {
                queueResponse.queue.Add(new QueueItem
                {
                    name = queueObj.track.name,
                    artist = queueObj.track.artist,
                    length = queueObj.track.length,
                    cover = queueObj.track.cover,
                    players = queueObj.players,
                    processed = queueObj.processed
                });
            }
            
            if (queueResponse.queue.Count > 0)
            {
                queueResponse.currentSong = $"{queueResponse.queue[0].name} by {queueResponse.queue[0].artist}";
            }
        }
        
        return JsonUtility.ToJson(queueResponse);
    }

    private string ProcessSongRequest(string postData)
    {
        try
        {
            SongRequest songRequest = JsonUtility.FromJson<SongRequest>(postData);
            
            if (levelCompiler != null && songRequest.players != null && songRequest.players.Count > 0)
            {
                // Validate that all arrays have the same length
                if (songRequest.difficulties == null || songRequest.micToggles == null ||
                    songRequest.players.Count != songRequest.difficulties.Count ||
                    songRequest.players.Count != songRequest.micToggles.Count)
                {
                    return "{\"success\":false,\"message\":\"Invalid player data - mismatched array lengths\"}";
                }
                
                // Limit to 4 players maximum
                int playerCount = Mathf.Min(songRequest.players.Count, 4);
                
                // Create new queue object with multiple players
                var newQueueObject = new LevelResourcesCompiler.QueueObject
                {
                    players = songRequest.players.GetRange(0, playerCount),
                    playerDifficulties = songRequest.difficulties.GetRange(0, playerCount),
                    playerMicToggle = songRequest.micToggles.GetRange(0, playerCount),
                    track = new LevelResourcesCompiler.BackgroundTrack
                    {
                        url = songRequest.url,
                        name = songRequest.name,
                        artist = songRequest.artist,
                        length = songRequest.length,
                        cover = songRequest.cover
                    }
                };
                
                // Add to queue
                levelCompiler.mainQueue.Add(newQueueObject);
                
                // Update party mode UI
                levelCompiler.UpdatePartyModeUI();
                
                string playerNames = string.Join(", ", newQueueObject.players);
                UnityEngine.Debug.Log($"Song added to queue: {songRequest.name} by {songRequest.artist} with players: {playerNames}");
            }
            else
            {
                return "{\"success\":false,\"message\":\"No players specified or LevelResourcesCompiler not found\"}";
            }
            
            return "{\"success\":true,\"message\":\"Song added to queue!\"}";
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogError($"Error processing song request: {e.Message}");
            return "{\"success\":false,\"message\":\"Failed to add song to queue.\"}";
        }
    }

    private async Task GenerateQRCode()
    {
        if (string.IsNullOrEmpty(publicUrl) || qrCodeImage == null) return;
        
        try
        {
            // Use a QR code API service
            string qrApiUrl = $"https://api.qrserver.com/v1/create-qr-code/?size=300x300&data={Uri.EscapeDataString(publicUrl)}";
            
            using (WebClient client = new WebClient())
            {
                byte[] imageData = await client.DownloadDataTaskAsync(qrApiUrl);
                
                // Create texture from downloaded image
                Texture2D texture = new Texture2D(2, 2);
                texture.LoadImage(imageData);
                
                // Create sprite
                Sprite qrSprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
                
                // Set sprite on main thread
                if (qrCodeImage != null)
                {
                    qrCodeImage.sprite = qrSprite;
                }
                
                UnityEngine.Debug.Log("QR code generated successfully");
            }
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogError($"Failed to generate QR code: {e.Message}");
        }
    }

    private string GetWebPageHTML()
    {
        return @"<!DOCTYPE html>
<html lang='en'>
<head>
    <meta charset='UTF-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>YASG Party Mode - Add Songs</title>
    <link rel='stylesheet' href='/static/style.css'>
</head>
<body>
    <div class='container'>
        <header>
            <h1>🎤 YASG Party Mode</h1>
            <p>Add songs to the karaoke queue!</p>
        </header>
        
        <div class='add-song-form'>
            <h2>Add a Song</h2>
            <form id='songForm'>
                <div class='song-info-section'>
                    <h3>Song Information</h3>
                    <div class='form-group'>
                        <label for='songUrl'>Spotify Song URL:</label>
                        <input type='url' id='songUrl' name='songUrl' placeholder='https://open.spotify.com/track/...' required>
                    </div>
                    
                    <div class='form-group'>
                        <label for='songName'>Song Name:</label>
                        <input type='text' id='songName' name='songName' required>
                    </div>
                    
                    <div class='form-group'>
                        <label for='artistName'>Artist:</label>
                        <input type='text' id='artistName' name='artistName' required>
                    </div>
                    
                    <div class='form-group'>
                        <label for='songLength'>Duration (mm:ss):</label>
                        <input type='text' id='songLength' name='songLength' placeholder='3:45' required>
                    </div>
                    
                    <div class='form-group'>
                        <label for='albumCover'>Album Cover URL (optional):</label>
                        <input type='url' id='albumCover' name='albumCover' placeholder='https://...'>
                    </div>
                </div>
                
                <div class='players-section'>
                    <h3>Players (1-4)</h3>
                    <div id='playersContainer'>
                        <div class='player-entry' data-player='1'>
                            <h4>Player 1</h4>
                            <div class='form-group'>
                                <label>Name:</label>
                                <input type='text' name='playerName1' required>
                            </div>
                            <div class='form-group'>
                                <label>Difficulty:</label>
                                <select name='difficulty1'>
                                    <option value='0'>Easy</option>
                                    <option value='1' selected>Medium</option>
                                    <option value='2'>Hard</option>
                                </select>
                            </div>
                            <div class='form-group'>
                                <label>
                                    <input type='checkbox' name='micToggle1' checked> Use Microphone
                                </label>
                            </div>
                        </div>
                    </div>
                    
                    <div class='player-controls'>
                        <button type='button' id='addPlayer' class='add-player-btn'>+ Add Player</button>
                        <button type='button' id='removePlayer' class='remove-player-btn' style='display: none;'>- Remove Player</button>
                    </div>
                </div>
                
                <button type='submit'>Add to Queue</button>
            </form>
        </div>
        
        <div class='queue-section'>
            <h2>Current Queue</h2>
            <div id='currentSong' class='current-song'></div>
            <div id='queueList' class='queue-list'></div>
        </div>
    </div>
    
    <script src='/static/script.js'></script>
</body>
</html>";
    }

    private string GetStaticFile(string fileName)
    {
        switch (fileName)
        {
            case "style.css":
                return GetCSS();
            case "script.js":
                return GetJavaScript();
            default:
                return "";
        }
    }

    private string GetCSS()
    {
        return @"
* {
    margin: 0;
    padding: 0;
    box-sizing: border-box;
}

body {
    font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
    background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
    color: #fff;
    min-height: 100vh;
}

.container {
    max-width: 800px;
    margin: 0 auto;
    padding: 20px;
}

header {
    text-align: center;
    margin-bottom: 40px;
}

header h1 {
    font-size: 3em;
    margin-bottom: 10px;
    text-shadow: 2px 2px 4px rgba(0,0,0,0.3);
}

header p {
    font-size: 1.2em;
    opacity: 0.9;
}

.add-song-form {
    background: rgba(255,255,255,0.1);
    backdrop-filter: blur(10px);
    border-radius: 20px;
    padding: 30px;
    margin-bottom: 40px;
    box-shadow: 0 8px 32px rgba(0,0,0,0.1);
}

.add-song-form h2 {
    margin-bottom: 25px;
    text-align: center;
}

.song-info-section,
.players-section {
    margin-bottom: 30px;
    padding: 20px;
    background: rgba(255,255,255,0.05);
    border-radius: 15px;
}

.song-info-section h3,
.players-section h3 {
    margin-bottom: 20px;
    text-align: center;
    color: #feca57;
}

.player-entry {
    background: rgba(255,255,255,0.1);
    padding: 15px;
    border-radius: 10px;
    margin-bottom: 15px;
}

.player-entry h4 {
    margin-bottom: 15px;
    color: #ff6b6b;
    text-align: center;
}

.player-controls {
    text-align: center;
    margin-top: 20px;
}

.add-player-btn,
.remove-player-btn {
    width: auto;
    padding: 10px 20px;
    margin: 0 10px;
    font-size: 14px;
    background: rgba(255,255,255,0.2);
}

.add-player-btn:hover,
.remove-player-btn:hover {
    background: rgba(255,255,255,0.3);
}

.form-group {
    margin-bottom: 20px;
}

.form-group label {
    display: block;
    margin-bottom: 8px;
    font-weight: 600;
}

.form-group input,
.form-group select {
    width: 100%;
    padding: 12px;
    border: none;
    border-radius: 10px;
    background: rgba(255,255,255,0.2);
    color: #fff;
    font-size: 16px;
}

.form-group input::placeholder {
    color: rgba(255,255,255,0.7);
}

.form-group input[type='checkbox'] {
    width: auto;
    margin-right: 10px;
}

button {
    width: 100%;
    padding: 15px;
    border: none;
    border-radius: 10px;
    background: linear-gradient(45deg, #ff6b6b, #feca57);
    color: #fff;
    font-size: 18px;
    font-weight: 600;
    cursor: pointer;
    transition: transform 0.2s;
}

button:hover {
    transform: translateY(-2px);
}

.queue-section {
    background: rgba(255,255,255,0.1);
    backdrop-filter: blur(10px);
    border-radius: 20px;
    padding: 30px;
    box-shadow: 0 8px 32px rgba(0,0,0,0.1);
}

.queue-section h2 {
    text-align: center;
    margin-bottom: 25px;
}

.current-song {
    background: rgba(255,255,255,0.2);
    padding: 15px;
    border-radius: 10px;
    margin-bottom: 20px;
    text-align: center;
    font-weight: 600;
}

.queue-item {
    background: rgba(255,255,255,0.1);
    padding: 15px;
    border-radius: 10px;
    margin-bottom: 10px;
    display: flex;
    justify-content: space-between;
    align-items: center;
}

.queue-item.processed {
    opacity: 0.6;
}

.song-info {
    flex: 1;
}

.song-title {
    font-weight: 600;
    margin-bottom: 5px;
}

.song-details {
    opacity: 0.8;
    font-size: 0.9em;
}

.players {
    text-align: right;
    opacity: 0.9;
}

@media (max-width: 600px) {
    .container {
        padding: 10px;
    }
    
    header h1 {
        font-size: 2em;
    }
    
    .add-song-form,
    .queue-section {
        padding: 20px;
    }
}";
    }

    private string GetJavaScript()
    {
        return @"
document.addEventListener('DOMContentLoaded', function() {
    const form = document.getElementById('songForm');
    const queueList = document.getElementById('queueList');
    const currentSong = document.getElementById('currentSong');
    const addPlayerBtn = document.getElementById('addPlayer');
    const removePlayerBtn = document.getElementById('removePlayer');
    const playersContainer = document.getElementById('playersContainer');
    
    let playerCount = 1;
    
    // Load queue on page load
    loadQueue();
    
    // Refresh queue every 5 seconds
    setInterval(loadQueue, 5000);
    
    // Add player functionality
    addPlayerBtn.addEventListener('click', function() {
        if (playerCount < 4) {
            playerCount++;
            addPlayerEntry(playerCount);
            updatePlayerButtons();
        }
    });
    
    // Remove player functionality
    removePlayerBtn.addEventListener('click', function() {
        if (playerCount > 1) {
            const lastPlayer = document.querySelector(`[data-player='${playerCount}']`);
            if (lastPlayer) {
                lastPlayer.remove();
            }
            playerCount--;
            updatePlayerButtons();
        }
    });
    
    function addPlayerEntry(playerNum) {
        const playerEntry = document.createElement('div');
        playerEntry.className = 'player-entry';
        playerEntry.setAttribute('data-player', playerNum);
        
        playerEntry.innerHTML = `
            <h4>Player ${playerNum}</h4>
            <div class='form-group'>
                <label>Name:</label>
                <input type='text' name='playerName${playerNum}' required>
            </div>
            <div class='form-group'>
                <label>Difficulty:</label>
                <select name='difficulty${playerNum}'>
                    <option value='0'>Easy</option>
                    <option value='1' selected>Medium</option>
                    <option value='2'>Hard</option>
                </select>
            </div>
            <div class='form-group'>
                <label>
                    <input type='checkbox' name='micToggle${playerNum}' checked> Use Microphone
                </label>
            </div>
        `;
        
        playersContainer.appendChild(playerEntry);
    }
    
    function updatePlayerButtons() {
        addPlayerBtn.style.display = playerCount >= 4 ? 'none' : 'inline-block';
        removePlayerBtn.style.display = playerCount <= 1 ? 'none' : 'inline-block';
    }
    
    form.addEventListener('submit', async function(e) {
        e.preventDefault();
        
        const formData = new FormData(form);
        
        // Collect player data
        const players = [];
        const difficulties = [];
        const micToggles = [];
        
        for (let i = 1; i <= playerCount; i++) {
            const playerName = formData.get(`playerName${i}`);
            const difficulty = parseInt(formData.get(`difficulty${i}`));
            const micToggle = formData.get(`micToggle${i}`) === 'on';
            
            if (playerName) {
                players.push(playerName);
                difficulties.push(difficulty);
                micToggles.push(micToggle);
            }
        }
        
        const songData = {
            url: formData.get('songUrl'),
            name: formData.get('songName'),
            artist: formData.get('artistName'),
            length: formData.get('songLength'),
            cover: formData.get('albumCover') || '',
            players: players,
            difficulties: difficulties,
            micToggles: micToggles
        };
        
        try {
            const response = await fetch('/api/add-song', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                },
                body: JSON.stringify(songData)
            });
            
            const result = await response.json();
            
            if (result.success) {
                alert('Song added to queue successfully!');
                form.reset();
                // Reset to 1 player
                while (playerCount > 1) {
                    const lastPlayer = document.querySelector(`[data-player='${playerCount}']`);
                    if (lastPlayer) {
                        lastPlayer.remove();
                    }
                    playerCount--;
                }
                updatePlayerButtons();
                loadQueue();
            } else {
                alert('Failed to add song: ' + result.message);
            }
        } catch (error) {
            alert('Error adding song: ' + error.message);
        }
    });
    
    async function loadQueue() {
        try {
            const response = await fetch('/api/queue');
            const data = await response.json();
            
            // Update current song
            currentSong.textContent = 'Now Playing: ' + data.currentSong;
            
            // Update queue list
            queueList.innerHTML = '';
            
            if (data.queue.length === 0) {
                queueList.innerHTML = '<p style=""text-align: center; opacity: 0.7;"">Queue is empty</p>';
                return;
            }
            
            data.queue.forEach((item, index) => {
                const queueItem = document.createElement('div');
                queueItem.className = 'queue-item' + (item.processed ? ' processed' : '');
                
                queueItem.innerHTML = `
                    <div class=""song-info"">
                        <div class=""song-title"">${item.name}</div>
                        <div class=""song-details"">by ${item.artist} • ${item.length}</div>
                    </div>
                    <div class=""players"">
                        ${item.players.join(', ')}
                        ${item.processed ? '<br><small>✓ Ready</small>' : '<br><small>⏳ Processing</small>'}
                    </div>
                `;
                
                queueList.appendChild(queueItem);
            });
        } catch (error) {
            console.error('Error loading queue:', error);
        }
    }
});";
    }

    private void OnDestroy()
    {
        StopWebServer();
    }

    private void OnApplicationQuit()
    {
        StopWebServer();
    }
    
    // Temporary test method - call this to test the web server
    [ContextMenu("Test Start Web Server")]
    public void TestStartWebServer()
    {
        StartWebServer();
    }
}