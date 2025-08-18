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
using UnityEngine.Networking;

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
    
    // Spotify API
    private string spotifyClientId;
    private string spotifyClientSecret;
    private string spotifyAccessToken;
    private System.DateTime tokenExpiryTime;
    
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
    
    // Spotify API Classes
    [System.Serializable]
    public class SpotifyTokenResponse
    {
        public string access_token;
        public string token_type;
        public int expires_in;
    }
    
    [System.Serializable]
    public class SpotifySearchResponse
    {
        public SpotifyTracksPage tracks;
    }
    
    [System.Serializable]
    public class SpotifyTracksPage
    {
        public List<SpotifyTrack> items;
        public int total;
    }
    
    [System.Serializable]
    public class SpotifyTrack
    {
        public string id;
        public string name;
        public List<SpotifyArtist> artists;
        public SpotifyAlbum album;
        public int duration_ms;
        public SpotifyExternalUrls external_urls;
    }
    
    [System.Serializable]
    public class SpotifyArtist
    {
        public string name;
        public string id;
    }
    
    [System.Serializable]
    public class SpotifyAlbum
    {
        public string name;
        public List<SpotifyImage> images;
    }
    
    [System.Serializable]
    public class SpotifyImage
    {
        public string url;
        public int width;
        public int height;
    }
    
    [System.Serializable]
    public class SpotifyExternalUrls
    {
        public string spotify;
    }
    
    // User tracking
    [System.Serializable]
    public class UserSession
    {
        public string userId;
        public string queuedSongId;
        public int queuePosition;
        public bool hasPendingRequest;
        public System.DateTime requestTime;
    }
    
    private Dictionary<string, UserSession> userSessions = new Dictionary<string, UserSession>();

    private void Start()
    {
        levelCompiler = LevelResourcesCompiler.Instance;
        
        // Set default ngrok path if not specified
        if (string.IsNullOrEmpty(ngrokPath))
        {
            ngrokPath = Path.Combine(PlayerPrefs.GetString("dataPath"), "ngrok.exe");
        }
        
        // Initialize Spotify credentials
        spotifyClientId = PlayerPrefs.GetString("CLIENTID");
        spotifyClientSecret = PlayerPrefs.GetString("APIKEY");
        
        // Start Spotify token refresh routine
        StartCoroutine(SpotifyTokenRefreshRoutine());
    }
    
    private IEnumerator SpotifyTokenRefreshRoutine()
    {
        while (true)
        {
            yield return StartCoroutine(GetSpotifyAccessToken());
            yield return new WaitForSeconds(3300); // Refresh every 55 minutes (tokens expire in 1 hour)
        }
    }
    
    private IEnumerator GetSpotifyAccessToken()
    {
        if (string.IsNullOrEmpty(spotifyClientId) || string.IsNullOrEmpty(spotifyClientSecret))
        {
            UnityEngine.Debug.LogError("Spotify credentials not found in PlayerPrefs");
            yield break;
        }
        
        string url = "https://accounts.spotify.com/api/token";
        
        WWWForm form = new WWWForm();
        form.AddField("grant_type", "client_credentials");
        form.AddField("client_id", spotifyClientId);
        form.AddField("client_secret", spotifyClientSecret);
        
        using (UnityWebRequest request = UnityWebRequest.Post(url, form))
        {
            request.SetRequestHeader("Content-Type", "application/x-www-form-urlencoded");
            yield return request.SendWebRequest();
            
            if (request.result == UnityWebRequest.Result.Success)
            {
                SpotifyTokenResponse tokenResponse = JsonUtility.FromJson<SpotifyTokenResponse>(request.downloadHandler.text);
                spotifyAccessToken = tokenResponse.access_token;
                tokenExpiryTime = System.DateTime.Now.AddSeconds(tokenResponse.expires_in);
                UnityEngine.Debug.Log("Spotify access token obtained successfully");
            }
            else
            {
                UnityEngine.Debug.LogError($"Failed to get Spotify access token: {request.error}");
            }
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
                    StartCoroutine(ProcessRequestCoroutine(context));
                }
            }
            yield return null;
        }
    }

    private IEnumerator ProcessRequestCoroutine(HttpListenerContext context)
    {
        yield return StartCoroutine(ProcessRequestAsync(context));
    }
    
    private IEnumerator ProcessRequestAsync(HttpListenerContext context)
    {
        HttpListenerRequest request = context.Request;
        HttpListenerResponse response = context.Response;
        
        string responseString = "";
        bool isError = false;
        
        // Handle search endpoint separately due to async nature
        if (request.Url.AbsolutePath == "/api/search" && request.HttpMethod == "GET")
        {
            string query = request.QueryString["q"];
            yield return StartCoroutine(ProcessSpotifySearchCoroutine(query, result => responseString = result));
            response.ContentType = "application/json";
        }
        else
        {
            try
            {
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
                    
                    string userId = GetOrCreateUserId(request);
                    responseString = ProcessSongRequest(postData, userId);
                    response.ContentType = "application/json";
                }
                else if (request.Url.AbsolutePath == "/api/user-status" && request.HttpMethod == "GET")
                {
                    string userId = GetOrCreateUserId(request);
                    responseString = GetUserStatus(userId);
                    response.ContentType = "application/json";
                }
                else if (request.Url.AbsolutePath == "/api/queue-position" && request.HttpMethod == "GET")
                {
                    string userId = GetOrCreateUserId(request);
                    responseString = GetQueuePosition(userId);
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
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError($"Error processing request: {e.Message}");
                response.StatusCode = 500;
                responseString = "Internal Server Error";
                isError = true;
            }
        }
        
        // Send response
        try
        {
            if (!isError)
            {
                // Enable CORS
                response.Headers.Add("Access-Control-Allow-Origin", "*");
                response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
                response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");
                
                // Set user ID cookie if needed
                if (request.Url.AbsolutePath.StartsWith("/api/"))
                {
                    string userId = GetOrCreateUserId(request);
                    response.Headers.Add("Set-Cookie", $"yasg_user_id={userId}; Path=/; Max-Age=86400"); // 24 hours
                }
            }
            
            byte[] buffer = Encoding.UTF8.GetBytes(responseString);
            response.ContentLength64 = buffer.Length;
            response.OutputStream.Write(buffer, 0, buffer.Length);
            response.OutputStream.Close();
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogError($"Error sending response: {e.Message}");
            try
            {
                response.Close();
            }
            catch { }
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

    private string ProcessSongRequest(string postData, string userId)
    {
        try
        {
            // Check if user already has a pending request
            if (userSessions.ContainsKey(userId) && userSessions[userId].hasPendingRequest)
            {
                return "{\"success\":false,\"message\":\"You already have a song in the queue. Please wait until it's played before adding another.\"}";
            }
            
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
                
                // Update user session
                if (userSessions.ContainsKey(userId))
                {
                    userSessions[userId].hasPendingRequest = true;
                    userSessions[userId].queuedSongId = songRequest.url;
                    userSessions[userId].requestTime = System.DateTime.Now;
                }
                
                // Store the user ID in the queue object for position tracking
                newQueueObject.requestedByUserId = userId;
                
                // Update party mode UI
                levelCompiler.UpdatePartyModeUI();
                
                string playerNames = string.Join(", ", newQueueObject.players);
                UnityEngine.Debug.Log($"Song added to queue by user {userId}: {songRequest.name} by {songRequest.artist} with players: {playerNames}");
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
            <p>Search and add songs to the karaoke queue!</p>
        </header>
        
        <!-- Progress indicator -->
        <div class='progress-container'>
            <div class='progress-step active' data-step='1'>
                <div class='step-number'>1</div>
                <div class='step-label'>Search Song</div>
            </div>
            <div class='progress-step' data-step='2'>
                <div class='step-number'>2</div>
                <div class='step-label'>Select Players</div>
            </div>
            <div class='progress-step' data-step='3'>
                <div class='step-number'>3</div>
                <div class='step-label'>Complete</div>
            </div>
        </div>
        
        <!-- Step 1: Search -->
        <div id='step1' class='step-container active'>
            <div class='search-section'>
                <h2>Search for a Song</h2>
                <div class='search-box'>
                    <input type='text' id='searchInput' placeholder='Search for a song or artist...' />
                    <button id='searchBtn' class='search-button'>🔍</button>
                </div>
                <div id='searchResults' class='search-results'></div>
                <div id='searchLoading' class='loading hidden'>Searching...</div>
            </div>
        </div>
        
        <!-- Step 2: Players -->
        <div id='step2' class='step-container'>
            <div class='players-section'>
                <h2>Configure Players</h2>
                <div class='selected-song-info'>
                    <div id='selectedSongCard' class='song-card'></div>
                </div>
                
                <div class='players-config'>
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
                
                <div class='step-navigation'>
                    <button id='backToSearch' class='nav-button secondary'>← Back to Search</button>
                    <button id='proceedToComplete' class='nav-button primary'>Add to Queue →</button>
                </div>
            </div>
        </div>
        
        <!-- Step 3: Complete -->
        <div id='step3' class='step-container'>
            <div class='completion-section'>
                <h2>Song Added Successfully!</h2>
                <div id='completionSongCard' class='song-card'></div>
                
                <div class='queue-status'>
                    <h3>Your Queue Status</h3>
                    <div id='queuePosition' class='position-indicator'>
                        <div class='position-circle'>
                            <span id='positionNumber'>-</span>
                        </div>
                        <div class='position-text'>
                            <div>Position in Queue</div>
                            <div id='queueTotal' class='total'>of 0 songs</div>
                        </div>
                    </div>
                    
                    <div id='waitMessage' class='wait-message'>
                        Please wait for your turn. Your song will be played soon!
                    </div>
                </div>
                
                <div class='completion-actions'>
                    <button id='viewQueue' class='nav-button secondary'>View Full Queue</button>
                    <button id='startOver' class='nav-button primary'>Add Another Song</button>
                </div>
            </div>
        </div>
        
        <!-- Queue viewer -->
        <div id='queueModal' class='modal hidden'>
            <div class='modal-content'>
                <div class='modal-header'>
                    <h3>Current Queue</h3>
                    <button id='closeModal' class='close-button'>×</button>
                </div>
                <div class='modal-body'>
                    <div id='currentSong' class='current-song'></div>
                    <div id='queueList' class='queue-list'></div>
                </div>
            </div>
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
    background: linear-gradient(135deg, #1a1a2e 0%, #16213e 50%, #0f3460 100%);
    color: #e8eaed;
    min-height: 100vh;
    overflow-x: hidden;
}

.container {
    max-width: 900px;
    margin: 0 auto;
    padding: 20px;
}

header {
    text-align: center;
    margin-bottom: 30px;
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

/* Progress Indicator */
.progress-container {
    display: flex;
    justify-content: center;
    align-items: center;
    margin-bottom: 40px;
    gap: 40px;
}

.progress-step {
    display: flex;
    flex-direction: column;
    align-items: center;
    position: relative;
    opacity: 0.5;
    transition: all 0.3s ease;
}

.progress-step.active {
    opacity: 1;
}

.progress-step.completed {
    opacity: 1;
}

.step-number {
    width: 50px;
    height: 50px;
    border-radius: 50%;
    background: rgba(56, 189, 248, 0.2);
    display: flex;
    align-items: center;
    justify-content: center;
    font-weight: 600;
    font-size: 1.2em;
    margin-bottom: 10px;
    transition: all 0.3s ease;
    border: 2px solid rgba(56, 189, 248, 0.3);
}

.progress-step.active .step-number {
    background: linear-gradient(45deg, #3b82f6, #06b6d4);
    transform: scale(1.1);
    border-color: #3b82f6;
    box-shadow: 0 0 20px rgba(59, 130, 246, 0.4);
}

.progress-step.completed .step-number {
    background: linear-gradient(45deg, #10b981, #059669);
    border-color: #10b981;
}

.progress-step.completed .step-number::after {
    content: '✓';
    font-size: 1.5em;
}

.step-label {
    font-size: 0.9em;
    font-weight: 500;
}

/* Step Containers */
.step-container {
    display: none;
    animation: fadeIn 0.5s ease-in;
}

.step-container.active {
    display: block;
}

@keyframes fadeIn {
    from { opacity: 0; transform: translateY(20px); }
    to { opacity: 1; transform: translateY(0); }
}

/* Search Section */
.search-section {
    background: rgba(30, 41, 59, 0.4);
    backdrop-filter: blur(20px);
    border-radius: 24px;
    padding: 32px;
    box-shadow: 0 8px 32px rgba(0,0,0,0.3);
    border: 1px solid rgba(148, 163, 184, 0.1);
}

.search-section h2 {
    text-align: center;
    margin-bottom: 30px;
    color: #06b6d4;
    text-shadow: 0 0 10px rgba(6, 182, 212, 0.3);
}

.search-box {
    display: flex;
    gap: 10px;
    margin-bottom: 30px;
}

.search-box input {
    flex: 1;
    padding: 16px 20px;
    border: 2px solid rgba(71, 85, 105, 0.4);
    border-radius: 16px;
    background: rgba(15, 23, 42, 0.6);
    color: #e2e8f0;
    font-size: 16px;
    backdrop-filter: blur(10px);
    transition: all 0.3s ease;
}

.search-box input:focus {
    outline: none;
    border-color: #3b82f6;
    background: rgba(15, 23, 42, 0.8);
    box-shadow: 0 0 0 3px rgba(59, 130, 246, 0.1);
}

.search-box input::placeholder {
    color: rgba(148, 163, 184, 0.7);
}

.search-button {
    width: 60px;
    padding: 15px;
    border: none;
    border-radius: 16px;
    background: linear-gradient(45deg, #3b82f6, #06b6d4);
    color: #fff;
    font-size: 20px;
    cursor: pointer;
    transition: all 0.3s ease;
    box-shadow: 0 4px 14px rgba(59, 130, 246, 0.3);
}

.search-button:hover {
    transform: scale(1.05);
    background: linear-gradient(45deg, #2563eb, #0891b2);
    box-shadow: 0 6px 20px rgba(59, 130, 246, 0.4);
}

.search-results {
    max-height: 500px;
    overflow-y: auto;
    padding-right: 10px;
}

.search-results::-webkit-scrollbar {
    width: 8px;
}

.search-results::-webkit-scrollbar-track {
    background: rgba(255,255,255,0.1);
    border-radius: 10px;
}

.search-results::-webkit-scrollbar-thumb {
    background: rgba(255,255,255,0.3);
    border-radius: 10px;
}

/* Song Cards */
.song-card {
    background: rgba(30, 41, 59, 0.6);
    border-radius: 16px;
    padding: 20px;
    margin-bottom: 15px;
    display: flex;
    align-items: center;
    gap: 20px;
    cursor: pointer;
    transition: all 0.3s ease;
    backdrop-filter: blur(20px);
    border: 1px solid rgba(71, 85, 105, 0.3);
}

.song-card:hover {
    background: rgba(51, 65, 85, 0.7);
    transform: translateY(-4px);
    box-shadow: 0 8px 25px rgba(0, 0, 0, 0.4);
    border-color: rgba(59, 130, 246, 0.5);
}

.song-card.selected {
    background: linear-gradient(45deg, rgba(59, 130, 246, 0.2), rgba(6, 182, 212, 0.2));
    border: 2px solid #06b6d4;
    box-shadow: 0 0 20px rgba(6, 182, 212, 0.3);
}

.song-artwork {
    width: 80px;
    height: 80px;
    border-radius: 12px;
    background: rgba(15, 23, 42, 0.8);
    display: flex;
    align-items: center;
    justify-content: center;
    overflow: hidden;
    flex-shrink: 0;
    border: 2px solid rgba(71, 85, 105, 0.4);
}

.song-artwork img {
    width: 100%;
    height: 100%;
    object-fit: cover;
    border-radius: 10px;
}

.song-artwork .placeholder {
    font-size: 2em;
    opacity: 0.5;
}

.song-details {
    flex: 1;
}

.song-title {
    font-size: 1.3em;
    font-weight: 600;
    margin-bottom: 5px;
    color: #fff;
}

.song-artist {
    font-size: 1.1em;
    color: rgba(255,255,255,0.8);
    margin-bottom: 8px;
}

.song-album {
    font-size: 0.9em;
    color: rgba(255,255,255,0.6);
    margin-bottom: 5px;
}

.song-duration {
    font-size: 0.9em;
    color: #06b6d4;
    font-weight: 500;
}

/* Players Section */
.players-section {
    background: rgba(30, 41, 59, 0.4);
    backdrop-filter: blur(20px);
    border-radius: 24px;
    padding: 32px;
    box-shadow: 0 8px 32px rgba(0,0,0,0.3);
    border: 1px solid rgba(148, 163, 184, 0.1);
}

.players-section h2 {
    text-align: center;
    margin-bottom: 30px;
    color: #06b6d4;
    text-shadow: 0 0 10px rgba(6, 182, 212, 0.3);
}

.selected-song-info {
    margin-bottom: 30px;
}

.players-config h3 {
    margin-bottom: 20px;
    text-align: center;
    color: #8b5cf6;
}

.player-entry {
    background: rgba(15, 23, 42, 0.6);
    padding: 20px;
    border-radius: 16px;
    margin-bottom: 15px;
    backdrop-filter: blur(10px);
    border: 1px solid rgba(71, 85, 105, 0.3);
}

.player-entry h4 {
    margin-bottom: 15px;
    color: #06b6d4;
    text-align: center;
    font-size: 1.1em;
}

.form-group {
    margin-bottom: 15px;
}

.form-group label {
    display: block;
    margin-bottom: 8px;
    font-weight: 600;
    color: rgba(255,255,255,0.9);
}

.form-group input,
.form-group select {
    width: 100%;
    padding: 12px 15px;
    border: 2px solid rgba(71, 85, 105, 0.4);
    border-radius: 12px;
    background: rgba(15, 23, 42, 0.6);
    color: #e2e8f0;
    font-size: 14px;
    backdrop-filter: blur(10px);
    transition: all 0.3s ease;
}

.form-group input:focus,
.form-group select:focus {
    outline: none;
    border-color: #3b82f6;
    background: rgba(15, 23, 42, 0.8);
    box-shadow: 0 0 0 3px rgba(59, 130, 246, 0.1);
}

.form-group input::placeholder {
    color: rgba(255,255,255,0.7);
}

.form-group input[type='checkbox'] {
    width: auto;
    margin-right: 10px;
    transform: scale(1.2);
}

.player-controls {
    text-align: center;
    margin: 20px 0;
    display: flex;
    gap: 15px;
    justify-content: center;
}

.add-player-btn {
    width: auto;
    padding: 12px 24px;
    font-size: 16px;
    font-weight: 600;
    background: linear-gradient(45deg, #10b981, #06b6d4);
    color: #ffffff;
    border: 2px solid rgba(16, 185, 129, 0.4);
    backdrop-filter: blur(10px);
    border-radius: 12px;
    transition: all 0.3s ease;
    box-shadow: 0 4px 16px rgba(16, 185, 129, 0.3);
    position: relative;
    overflow: hidden;
}

.add-player-btn::before {
    content: '';
    position: absolute;
    top: 0;
    left: -100%;
    width: 100%;
    height: 100%;
    background: linear-gradient(90deg, transparent, rgba(255,255,255,0.3), transparent);
    transition: left 0.5s;
}

.add-player-btn:hover::before {
    left: 100%;
}

.add-player-btn:hover {
    background: linear-gradient(45deg, #059669, #0891b2);
    transform: translateY(-3px) scale(1.05);
    box-shadow: 0 6px 24px rgba(16, 185, 129, 0.4);
}

.remove-player-btn {
    width: auto;
    padding: 10px 20px;
    font-size: 14px;
    background: rgba(239, 68, 68, 0.2);
    color: #fca5a5;
    border: 1px solid rgba(239, 68, 68, 0.3);
    backdrop-filter: blur(10px);
    border-radius: 10px;
    transition: all 0.3s ease;
}

.remove-player-btn:hover {
    background: rgba(239, 68, 68, 0.3);
    transform: translateY(-2px);
    color: #ffffff;
}

/* Navigation */
.step-navigation {
    display: flex;
    gap: 20px;
    justify-content: space-between;
    margin-top: 30px;
}

.nav-button {
    padding: 15px 30px;
    border: none;
    border-radius: 15px;
    font-size: 16px;
    font-weight: 600;
    cursor: pointer;
    transition: all 0.3s ease;
    min-width: 150px;
}

.nav-button.primary {
    background: linear-gradient(45deg, #3b82f6, #06b6d4);
    color: #fff;
    box-shadow: 0 4px 14px rgba(59, 130, 246, 0.3);
}

.nav-button.secondary {
    background: rgba(71, 85, 105, 0.4);
    color: #e2e8f0;
    backdrop-filter: blur(10px);
    border: 1px solid rgba(148, 163, 184, 0.3);
}

.nav-button:hover {
    transform: translateY(-2px);
}

.nav-button.primary:hover {
    background: linear-gradient(45deg, #1e40af, #0891b2);
    box-shadow: 0 6px 20px rgba(59, 130, 246, 0.4);
}

.nav-button.secondary:hover {
    background: rgba(100, 116, 139, 0.6);
    border-color: rgba(148, 163, 184, 0.5);
}

/* Completion Section */
.completion-section {
    background: rgba(26, 26, 46, 0.9);
    border: 1px solid rgba(59, 130, 246, 0.3);
    backdrop-filter: blur(10px);
    border-radius: 16px;
    padding: 30px;
    box-shadow: 0 8px 32px rgba(0, 0, 0, 0.4);
    text-align: center;
}

.completion-section h2 {
    margin-bottom: 30px;
    color: #3b82f6;
    font-size: 2.5em;
    font-weight: 600;
}

.queue-status {
    margin: 30px 0;
}

.queue-status h3 {
    margin-bottom: 20px;
    color: #06b6d4;
    font-weight: 500;
}

.position-indicator {
    display: flex;
    align-items: center;
    justify-content: center;
    gap: 20px;
    margin-bottom: 20px;
}

.position-circle {
    width: 100px;
    height: 100px;
    border-radius: 50%;
    background: linear-gradient(45deg, #3b82f6, #06b6d4);
    display: flex;
    align-items: center;
    justify-content: center;
    font-size: 2em;
    font-weight: 600;
    color: #ffffff;
    animation: pulse 2s infinite;
    box-shadow: 0 4px 20px rgba(59, 130, 246, 0.3);
}

@keyframes pulse {
    0% { transform: scale(1); }
    50% { transform: scale(1.05); }
    100% { transform: scale(1); }
}

.position-text {
    text-align: left;
}

.position-text > div:first-child {
    font-size: 1.2em;
    font-weight: 600;
    margin-bottom: 5px;
}

.total {
    color: rgba(226, 232, 240, 0.7);
}

.wait-message {
    background: rgba(59, 130, 246, 0.2);
    border: 1px solid rgba(59, 130, 246, 0.3);
    padding: 15px;
    border-radius: 12px;
    color: rgba(226, 232, 240, 0.9);
    font-style: italic;
}

.completion-actions {
    display: flex;
    gap: 20px;
    justify-content: center;
    margin-top: 30px;
}

/* Modal */
.modal {
    position: fixed;
    top: 0;
    left: 0;
    width: 100%;
    height: 100%;
    background: rgba(0, 0, 0, 0.8);
    display: flex;
    align-items: center;
    justify-content: center;
    z-index: 1000;
    backdrop-filter: blur(8px);
}

.modal.hidden {
    display: none;
}

.modal-content {
    background: rgba(26, 26, 46, 0.95);
    border: 1px solid rgba(59, 130, 246, 0.3);
    backdrop-filter: blur(20px);
    border-radius: 16px;
    width: 90%;
    max-width: 600px;
    max-height: 80vh;
    overflow: hidden;
    box-shadow: 0 20px 40px rgba(0, 0, 0, 0.5);
}

.modal-header {
    padding: 20px;
    border-bottom: 1px solid rgba(59, 130, 246, 0.2);
    display: flex;
    justify-content: space-between;
    align-items: center;
    background: rgba(22, 33, 62, 0.5);
}

.modal-header h3 {
    color: #3b82f6;
    font-size: 1.5em;
    font-weight: 600;
}

.close-button {
    width: 40px;
    height: 40px;
    border: none;
    border-radius: 50%;
    background: rgba(59, 130, 246, 0.2);
    color: #e2e8f0;
    font-size: 1.5em;
    cursor: pointer;
    transition: all 0.3s ease;
}

.close-button:hover {
    background: rgba(59, 130, 246, 0.4);
    transform: scale(1.1);
    color: #ffffff;
}

.modal-body {
    padding: 20px;
    max-height: 60vh;
    overflow-y: auto;
}

/* Queue Items */
.current-song {
    background: rgba(59, 130, 246, 0.2);
    border: 1px solid rgba(59, 130, 246, 0.3);
    padding: 15px;
    border-radius: 12px;
    margin-bottom: 20px;
    text-align: center;
    font-weight: 600;
    color: #3b82f6;
}

.queue-item {
    background: rgba(30, 41, 59, 0.6);
    border: 1px solid rgba(71, 85, 105, 0.3);
    padding: 15px;
    border-radius: 12px;
    margin-bottom: 10px;
    display: flex;
    justify-content: space-between;
    align-items: center;
    transition: all 0.3s ease;
}

.queue-item:hover {
    background: rgba(30, 41, 59, 0.8);
    border-color: rgba(71, 85, 105, 0.5);
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

/* Loading States */
.loading {
    text-align: center;
    padding: 20px;
    color: rgba(255,255,255,0.7);
    font-style: italic;
}

.hidden {
    display: none;
}

/* Responsive Design */
@media (max-width: 768px) {
    .container {
        padding: 10px;
    }
    
    header h1 {
        font-size: 2.5em;
    }
    
    .progress-container {
        gap: 20px;
    }
    
    .progress-step {
        font-size: 0.9em;
    }
    
    .step-number {
        width: 40px;
        height: 40px;
        font-size: 1em;
    }
    
    .song-card {
        flex-direction: column;
        text-align: center;
    }
    
    .song-artwork {
        margin-bottom: 15px;
    }
    
    .step-navigation {
        flex-direction: column;
    }
    
    .position-indicator {
        flex-direction: column;
    }
    
    .completion-actions {
        flex-direction: column;
    }
    
    .modal-content {
        width: 95%;
        margin: 20px;
    }
}

@media (max-width: 480px) {
    header h1 {
        font-size: 2em;
    }
    
    .search-section,
    .players-section,
    .completion-section {
        padding: 20px;
    }
    
    .progress-container {
        gap: 15px;
    }
    
    .step-label {
        font-size: 0.8em;
    }
}";
    }

    private string GetJavaScript()
    {
        return @"
document.addEventListener('DOMContentLoaded', function() {
    // Global state
    let currentStep = 1;
    let selectedSong = null;
    let playerCount = 1;
    let searchTimeout = null;
    let userCanRequest = true;
    
    // Element references
    const progressSteps = document.querySelectorAll('.progress-step');
    const stepContainers = document.querySelectorAll('.step-container');
    const searchInput = document.getElementById('searchInput');
    const searchBtn = document.getElementById('searchBtn');
    const searchResults = document.getElementById('searchResults');
    const searchLoading = document.getElementById('searchLoading');
    const selectedSongCard = document.getElementById('selectedSongCard');
    const playersContainer = document.getElementById('playersContainer');
    const addPlayerBtn = document.getElementById('addPlayer');
    const removePlayerBtn = document.getElementById('removePlayer');
    const backToSearchBtn = document.getElementById('backToSearch');
    const proceedToCompleteBtn = document.getElementById('proceedToComplete');
    const completionSongCard = document.getElementById('completionSongCard');
    const positionNumber = document.getElementById('positionNumber');
    const queueTotal = document.getElementById('queueTotal');
    const viewQueueBtn = document.getElementById('viewQueue');
    const startOverBtn = document.getElementById('startOver');
    const queueModal = document.getElementById('queueModal');
    const closeModalBtn = document.getElementById('closeModal');
    const currentSong = document.getElementById('currentSong');
    const queueList = document.getElementById('queueList');
    
    // Initialize
    init();
    
    function init() {
        checkUserStatus();
        setupEventListeners();
        updateStepDisplay();
    }
    
    async function checkUserStatus() {
        try {
            const response = await fetch('/api/user-status');
            const status = await response.json();
            userCanRequest = status.canRequest;
            
            if (!userCanRequest) {
                // User already has a pending request, jump to step 3
                currentStep = 3;
                updateStepDisplay();
                updateQueuePosition();
                startQueuePositionPolling();
            }
        } catch (error) {
            console.error('Error checking user status:', error);
        }
    }
    
    function setupEventListeners() {
        // Search functionality
        searchInput.addEventListener('input', handleSearchInput);
        searchInput.addEventListener('keypress', function(e) {
            if (e.key === 'Enter') {
                performSearch();
            }
        });
        searchBtn.addEventListener('click', performSearch);
        
        // Player management
        addPlayerBtn.addEventListener('click', addPlayer);
        removePlayerBtn.addEventListener('click', removePlayer);
        
        // Navigation
        backToSearchBtn.addEventListener('click', () => goToStep(1));
        proceedToCompleteBtn.addEventListener('click', submitSong);
        startOverBtn.addEventListener('click', startOver);
        
        // Modal
        viewQueueBtn.addEventListener('click', openQueueModal);
        closeModalBtn.addEventListener('click', closeQueueModal);
        queueModal.addEventListener('click', function(e) {
            if (e.target === queueModal) {
                closeQueueModal();
            }
        });
    }
    
    function handleSearchInput() {
        const query = searchInput.value.trim();
        
        // Only auto-search on desktop, not on mobile
        if (isMobile()) {
            // On mobile, just clear results if query is too short
            if (query.length < 2) {
                searchResults.innerHTML = '';
            }
            return;
        }
        
        // Desktop auto-search behavior
        if (searchTimeout) {
            clearTimeout(searchTimeout);
        }
        
        if (query.length >= 2) {
            searchTimeout = setTimeout(() => {
                performSearch(query);
            }, 500); // Debounce search
        } else {
            searchResults.innerHTML = '';
        }
    }
    
    function isMobile() {
        return /Android|webOS|iPhone|iPad|iPod|BlackBerry|IEMobile|Opera Mini/i.test(navigator.userAgent) || 
               (window.matchMedia && window.matchMedia('(max-width: 768px)').matches);
    }
    
    async function performSearch(query = null) {
        const searchQuery = query || searchInput.value.trim();
        
        if (searchQuery.length < 2) {
            return;
        }
        
        searchLoading.classList.remove('hidden');
        searchResults.innerHTML = '';
        
        try {
            const response = await fetch(`/api/search?q=${encodeURIComponent(searchQuery)}`);
            const data = await response.json();
            
            searchLoading.classList.add('hidden');
            
            if (data.error) {
                searchResults.innerHTML = `<div class='error'>Error: ${data.error}</div>`;
                return;
            }
            
            displaySearchResults(data);
        } catch (error) {
            searchLoading.classList.add('hidden');
            searchResults.innerHTML = `<div class='error'>Search failed: ${error.message}</div>`;
        }
    }
    
    function displaySearchResults(data) {
        if (!data.tracks || !data.tracks.items || data.tracks.items.length === 0) {
            searchResults.innerHTML = '<div class=""no-results"">No songs found. Try a different search term.</div>';
            return;
        }
        
        searchResults.innerHTML = '';
        
        data.tracks.items.forEach(track => {
            const songCard = createSongCard(track);
            searchResults.appendChild(songCard);
        });
    }
    
    function createSongCard(track) {
        const card = document.createElement('div');
        card.className = 'song-card';
        card.onclick = () => selectSong(track, card);
        
        // Get album art
        const artworkUrl = track.album.images && track.album.images.length > 0 
            ? track.album.images[0].url 
            : '';
        
        // Convert duration
        const duration = convertDuration(track.duration_ms);
        
        // Get first artist
        const artist = track.artists && track.artists.length > 0 
            ? track.artists[0].name 
            : 'Unknown Artist';
        
        card.innerHTML = `
            <div class='song-artwork'>
                ${artworkUrl ? `<img src='${artworkUrl}' alt='Album Art' />` : '<div class=""placeholder"">🎵</div>'}
            </div>
            <div class='song-details'>
                <div class='song-title'>${escapeHtml(track.name)}</div>
                <div class='song-artist'>${escapeHtml(artist)}</div>
                <div class='song-album'>${escapeHtml(track.album.name)}</div>
                <div class='song-duration'>${duration}</div>
            </div>
        `;
        
        return card;
    }
    
    function selectSong(track, cardElement) {
        // Remove previous selection
        document.querySelectorAll('.song-card.selected').forEach(card => {
            card.classList.remove('selected');
        });
        
        // Select new song
        cardElement.classList.add('selected');
        selectedSong = track;
        
        // Move to step 2
        setTimeout(() => {
            goToStep(2);
        }, 300);
    }
    
    function goToStep(step) {
        if (step === 2 && !selectedSong) {
            alert('Please select a song first.');
            return;
        }
        
        if (step === 3 && !userCanRequest) {
            // User already has a pending request
            updateQueuePosition();
            startQueuePositionPolling();
        }
        
        currentStep = step;
        updateStepDisplay();
        
        if (step === 2) {
            displaySelectedSong();
        } else if (step === 3) {
            displayCompletionSong();
        }
    }
    
    function updateStepDisplay() {
        // Update progress indicator
        progressSteps.forEach((step, index) => {
            const stepNum = index + 1;
            step.classList.remove('active', 'completed');
            
            if (stepNum < currentStep) {
                step.classList.add('completed');
            } else if (stepNum === currentStep) {
                step.classList.add('active');
            }
        });
        
        // Update step containers
        stepContainers.forEach((container, index) => {
            const stepNum = index + 1;
            container.classList.toggle('active', stepNum === currentStep);
        });
    }
    
    function displaySelectedSong() {
        if (!selectedSong) return;
        
        const artworkUrl = selectedSong.album.images && selectedSong.album.images.length > 0 
            ? selectedSong.album.images[0].url 
            : '';
        
        const duration = convertDuration(selectedSong.duration_ms);
        const artist = selectedSong.artists && selectedSong.artists.length > 0 
            ? selectedSong.artists[0].name 
            : 'Unknown Artist';
        
        selectedSongCard.innerHTML = `
            <div class='song-artwork'>
                ${artworkUrl ? `<img src='${artworkUrl}' alt='Album Art' />` : '<div class=""placeholder"">🎵</div>'}
            </div>
            <div class='song-details'>
                <div class='song-title'>${escapeHtml(selectedSong.name)}</div>
                <div class='song-artist'>${escapeHtml(artist)}</div>
                <div class='song-album'>${escapeHtml(selectedSong.album.name)}</div>
                <div class='song-duration'>${duration}</div>
            </div>
        `;
    }
    
    function displayCompletionSong() {
        if (!selectedSong) return;
        
        const artworkUrl = selectedSong.album.images && selectedSong.album.images.length > 0 
            ? selectedSong.album.images[0].url 
            : '';
        
        const duration = convertDuration(selectedSong.duration_ms);
        const artist = selectedSong.artists && selectedSong.artists.length > 0 
            ? selectedSong.artists[0].name 
            : 'Unknown Artist';
        
        completionSongCard.innerHTML = `
            <div class='song-artwork'>
                ${artworkUrl ? `<img src='${artworkUrl}' alt='Album Art' />` : '<div class=""placeholder"">🎵</div>'}
            </div>
            <div class='song-details'>
                <div class='song-title'>${escapeHtml(selectedSong.name)}</div>
                <div class='song-artist'>${escapeHtml(artist)}</div>
                <div class='song-album'>${escapeHtml(selectedSong.album.name)}</div>
                <div class='song-duration'>${duration}</div>
            </div>
        `;
    }
    
    function addPlayer() {
        if (playerCount < 4) {
            playerCount++;
            addPlayerEntry(playerCount);
            updatePlayerButtons();
        }
    }
    
    function removePlayer() {
        if (playerCount > 1) {
            const lastPlayer = document.querySelector(`[data-player='${playerCount}']`);
            if (lastPlayer) {
                lastPlayer.remove();
            }
            playerCount--;
            updatePlayerButtons();
        }
    }
    
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
    
    async function submitSong() {
        if (!selectedSong) {
            alert('No song selected');
            return;
        }
        
        // Collect player data
        const players = [];
        const difficulties = [];
        const micToggles = [];
        
        for (let i = 1; i <= playerCount; i++) {
            const nameInput = document.querySelector(`input[name='playerName${i}']`);
            const difficultySelect = document.querySelector(`select[name='difficulty${i}']`);
            const micToggleInput = document.querySelector(`input[name='micToggle${i}']`);
            
            if (nameInput && nameInput.value.trim()) {
                players.push(nameInput.value.trim());
                difficulties.push(parseInt(difficultySelect.value));
                micToggles.push(micToggleInput.checked);
            }
        }
        
        if (players.length === 0) {
            alert('Please add at least one player name');
            return;
        }
        
        const duration = convertDuration(selectedSong.duration_ms);
        const artist = selectedSong.artists && selectedSong.artists.length > 0 
            ? selectedSong.artists[0].name 
            : 'Unknown Artist';
        const artworkUrl = selectedSong.album.images && selectedSong.album.images.length > 0 
            ? selectedSong.album.images[0].url 
            : '';
        
        const songData = {
            url: selectedSong.external_urls.spotify,
            name: selectedSong.name,
            artist: artist,
            length: duration,
            cover: artworkUrl,
            players: players,
            difficulties: difficulties,
            micToggles: micToggles
        };
        
        try {
            proceedToCompleteBtn.disabled = true;
            proceedToCompleteBtn.textContent = 'Adding to Queue...';
            
            const response = await fetch('/api/add-song', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                },
                body: JSON.stringify(songData)
            });
            
            const result = await response.json();
            
            if (result.success) {
                userCanRequest = false;
                goToStep(3);
                updateQueuePosition();
                startQueuePositionPolling();
            } else {
                alert('Failed to add song: ' + result.message);
                proceedToCompleteBtn.disabled = false;
                proceedToCompleteBtn.textContent = 'Add to Queue →';
            }
        } catch (error) {
            alert('Error adding song: ' + error.message);
            proceedToCompleteBtn.disabled = false;
            proceedToCompleteBtn.textContent = 'Add to Queue →';
        }
    }
    
    async function updateQueuePosition() {
        try {
            const response = await fetch('/api/queue-position');
            const data = await response.json();
            
            positionNumber.textContent = data.position > 0 ? data.position : '-';
            queueTotal.textContent = `of ${data.total} songs`;
            
            if (data.position === -1) {
                // Song not found in queue, user can request again
                userCanRequest = true;
                startOverBtn.style.display = 'inline-block';
            }
        } catch (error) {
            console.error('Error updating queue position:', error);
        }
    }
    
    function startQueuePositionPolling() {
        const interval = setInterval(async () => {
            await updateQueuePosition();
            
            if (userCanRequest) {
                clearInterval(interval);
            }
        }, 3000); // Update every 3 seconds
    }
    
    function startOver() {
        selectedSong = null;
        userCanRequest = true;
        currentStep = 1;
        playerCount = 1;
        
        // Reset player entries
        const playerEntries = document.querySelectorAll('.player-entry');
        playerEntries.forEach((entry, index) => {
            if (index > 0) {
                entry.remove();
            } else {
                // Reset first player
                entry.querySelector('input[type=""text""]').value = '';
                entry.querySelector('select').selectedIndex = 1;
                entry.querySelector('input[type=""checkbox""]').checked = true;
            }
        });
        
        updatePlayerButtons();
        updateStepDisplay();
        
        // Clear search
        searchInput.value = '';
        searchResults.innerHTML = '';
        
        // Reset buttons
        proceedToCompleteBtn.disabled = false;
        proceedToCompleteBtn.textContent = 'Add to Queue →';
    }
    
    async function openQueueModal() {
        queueModal.classList.remove('hidden');
        await loadQueue();
    }
    
    function closeQueueModal() {
        queueModal.classList.add('hidden');
    }
    
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
                        <div class=""song-title"">${escapeHtml(item.name)}</div>
                        <div class=""song-details"">by ${escapeHtml(item.artist)} • ${item.length}</div>
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
    
    // Utility functions
    function convertDuration(durationMs) {
        const totalSeconds = Math.floor(durationMs / 1000);
        const minutes = Math.floor(totalSeconds / 60);
        const seconds = totalSeconds % 60;
        return `${minutes}:${seconds.toString().padStart(2, '0')}`;
    }
    
    function escapeHtml(text) {
        const div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    }
    
    // Auto-refresh queue in modal every 5 seconds
    setInterval(() => {
        if (!queueModal.classList.contains('hidden')) {
            loadQueue();
        }
    }, 5000);
});";
    }
    
    public void OnSongCompleted(string userId)
    {
        if (!string.IsNullOrEmpty(userId) && userSessions.ContainsKey(userId))
        {
            userSessions[userId].hasPendingRequest = false;
            userSessions[userId].queuedSongId = "";
            UnityEngine.Debug.Log($"User session reset for completed song: {userId}");
        }
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
    
    // New API Methods
    private IEnumerator ProcessSpotifySearchCoroutine(string query, System.Action<string> callback)
    {
        if (string.IsNullOrEmpty(query))
        {
            callback("{\"error\":\"No search query provided\"}");
            yield break;
        }
        
        if (string.IsNullOrEmpty(spotifyAccessToken))
        {
            callback("{\"error\":\"Spotify access token not available\"}");
            yield break;
        }
        
        string encodedQuery = UnityWebRequest.EscapeURL(query);
        string searchUrl = $"https://api.spotify.com/v1/search?q={encodedQuery}&type=track&limit=20";
        
        using (UnityWebRequest request = UnityWebRequest.Get(searchUrl))
        {
            request.SetRequestHeader("Authorization", "Bearer " + spotifyAccessToken);
            yield return request.SendWebRequest();
            
            if (request.result == UnityWebRequest.Result.Success)
            {
                callback(request.downloadHandler.text);
            }
            else
            {
                callback($"{{\"error\":\"Spotify search failed: {request.error}\"}}");
            }
        }
    }
    
    private string GetOrCreateUserId(HttpListenerRequest request)
    {
        // Check for existing user ID in cookies
        string userId = null;
        string cookieHeader = request.Headers["Cookie"];
        
        if (!string.IsNullOrEmpty(cookieHeader))
        {
            var match = Regex.Match(cookieHeader, @"yasg_user_id=([^;]+)");
            if (match.Success)
            {
                userId = match.Groups[1].Value;
            }
        }
        
        // Generate new user ID if not found
        if (string.IsNullOrEmpty(userId))
        {
            userId = System.Guid.NewGuid().ToString();
        }
        
        // Initialize user session if not exists
        if (!userSessions.ContainsKey(userId))
        {
            userSessions[userId] = new UserSession
            {
                userId = userId,
                hasPendingRequest = false,
                queuePosition = -1
            };
        }
        
        return userId;
    }
    
    private string GetUserStatus(string userId)
    {
        if (!userSessions.ContainsKey(userId))
        {
            return "{\"hasPendingRequest\":false,\"canRequest\":true}";
        }
        
        var session = userSessions[userId];
        bool canRequest = !session.hasPendingRequest;
        
        return $"{{\"userId\":\"{userId}\",\"hasPendingRequest\":{session.hasPendingRequest.ToString().ToLower()},\"canRequest\":{canRequest.ToString().ToLower()}}}";
    }
    
    private string GetQueuePosition(string userId)
    {
        if (!userSessions.ContainsKey(userId))
        {
            return "{\"position\":-1,\"total\":0}";
        }
        
        var session = userSessions[userId];
        int queueTotal = levelCompiler?.mainQueue?.Count ?? 0;
        
        // Find user's position in queue
        int position = -1;
        if (session.hasPendingRequest && levelCompiler?.mainQueue != null)
        {
            for (int i = 0; i < levelCompiler.mainQueue.Count; i++)
            {
                var queueItem = levelCompiler.mainQueue[i];
                // Check if this queue item was requested by this user
                if (!string.IsNullOrEmpty(queueItem.requestedByUserId) && queueItem.requestedByUserId == userId)
                {
                    position = i + 1;
                    break;
                }
            }
        }
        
        return $"{{\"position\":{position},\"total\":{queueTotal}}}";
    }
    
    private string ConvertDuration(int durationMs)
    {
        int totalSeconds = durationMs / 1000;
        int minutes = totalSeconds / 60;
        int seconds = totalSeconds % 60;
        return $"{minutes}:{seconds:D2}";
    }
}