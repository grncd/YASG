using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using System.Text.RegularExpressions;
using WebSocketSharp;

public class VocalRemoverAPI : MonoBehaviour
{
    private const string BaseApiUrl = "https://api.vocalremover.org";
    private const string PatronToken = "0fa00340-0b81-11ed-861d-0242ac120003";
    private const string Origin = "https://vocalremover.org";
    private const string Referer = "https://vocalremover.org/";

    private int _serverNumber;
    private string _currentInputFile;
    private string _outputDir;

    [Header("Debug Settings")]
    public string debugCookieString;

    private WebSocketSharp.WebSocket _ws;
    private bool _wsConnected = false;
    private bool _processingComplete = false;
    private string _readyKey = null;
    private int _readyServer = 0;
    private Dictionary<string, string> _cookieStorage = new Dictionary<string, string>();

    public event Action<int> OnProgressChanged;
    public event Action<bool, string> OnProcessingComplete;

    private string GetServerUrl() => $"https://api{_serverNumber}.vocalremover.org";

    private void SetHeaders(UnityWebRequest request)
    {
        request.SetRequestHeader("User-Agent", "Mozilla/5.0 (X11; Linux x86_64; rv:109.0) Gecko/20100101 Firefox/115.0");
        request.SetRequestHeader("Accept", "*/*");
        request.SetRequestHeader("Accept-Language", "en-US,en;q=0.9");
        request.SetRequestHeader("Patron", PatronToken);
        request.SetRequestHeader("Locale", "en");
        try { request.SetRequestHeader("Origin", Origin); } catch { }
        try { request.SetRequestHeader("Referer", Referer); } catch { }
    }

    public void ProcessFile(string inputPath, string outputDir)
    {
        _currentInputFile = inputPath;
        _outputDir = outputDir;
        StartCoroutine(ProcessFileCoroutine());
    }

    private void UpdateCookies(string header)
    {
        if (string.IsNullOrEmpty(header)) return;

        var parts = header.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            var subParts = part.Trim().Split(';');
            var mainPair = subParts[0].Split(new[] { '=' }, 2);
            if (mainPair.Length == 2)
            {
                var key = mainPair[0].Trim();
                var val = mainPair[1].Trim();

                if (key.Equals("path", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("domain", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("expires", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("max-age", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("samesite", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("secure", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("httponly", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                _cookieStorage[key] = val;
            }
        }
    }

    private string GetCookieHeader()
    {
        if (!string.IsNullOrEmpty(debugCookieString))
            return debugCookieString;

        if (_cookieStorage.Count == 0) return "";
        var sb = new System.Text.StringBuilder();
        foreach (var kv in _cookieStorage)
        {
            if (sb.Length > 0) sb.Append("; ");
            sb.Append($"{kv.Key}={kv.Value}");
        }
        return sb.ToString();
    }

    private IEnumerator ProcessFileCoroutine()
    {
        _readyKey = null;
        _readyServer = 0;
        _processingComplete = false;

        OnProgressChanged?.Invoke(0);
        OnProgressChanged?.Invoke(2);
        yield return StartCoroutine(VisitMainPageCoroutine());

        bool serverSuccess = false;
        yield return StartCoroutine(GetServerCoroutine(success => serverSuccess = success));

        if (!serverSuccess)
        {
            OnProcessingComplete?.Invoke(false, "Failed to get server assignment");
            yield break;
        }

        OnProgressChanged?.Invoke(5);

        long trackId = 0;
        string trackKey = null;

        yield return StartCoroutine(UploadTrackCoroutine(_currentInputFile, (id, key, server) =>
        {
            trackId = id;
            trackKey = key;
            if (server > 0)
                _serverNumber = server;
        }));

        if (trackId == 0 || string.IsNullOrEmpty(trackKey))
        {
            OnProcessingComplete?.Invoke(false, "Failed to upload track");
            yield break;
        }

        OnProgressChanged?.Invoke(25);
        yield return StartCoroutine(ConnectWebSocketCoroutine(trackId, trackKey));

        bool processingSuccess = false;
        yield return StartCoroutine(WaitForProcessingCoroutine(trackId, trackKey, success => processingSuccess = success));

        if (!processingSuccess)
        {
            OnProcessingComplete?.Invoke(false, "Processing timed out");
            DisconnectWebSocket();
            yield break;
        }

        OnProgressChanged?.Invoke(50);

        var baseName = Path.GetFileNameWithoutExtension(_currentInputFile);
        var ext = Path.GetExtension(_currentInputFile);

        var downloadKey = !string.IsNullOrEmpty(_readyKey) ? _readyKey : trackKey;
        var downloadServer = _readyServer > 0 ? _readyServer : _serverNumber;

        var vocalPath = Path.Combine(_outputDir, $"{baseName} [vocals]{ext}");
        var musicPath = Path.Combine(_outputDir, $"{baseName} [no_vocals]{ext}");

        bool vocalExists = File.Exists(vocalPath);
        bool musicExists = File.Exists(musicPath);

        if (vocalExists && musicExists)
        {
            Debug.Log($"Both tracks already exist, skipping download");
            OnProgressChanged?.Invoke(100);
            DisconnectWebSocket();
            OnProcessingComplete?.Invoke(true, "Processing complete (files already exist)");
            yield break;
        }

        bool vocalSuccess = vocalExists;
        if (!vocalExists)
        {
            yield return StartCoroutine(DownloadTrackCoroutine(trackId, downloadKey, downloadServer, "vocal", vocalPath, success => vocalSuccess = success, 50, 75));

            if (!vocalSuccess)
            {
                OnProcessingComplete?.Invoke(false, "Failed to download vocal track");
                DisconnectWebSocket();
                yield break;
            }
        }

        OnProgressChanged?.Invoke(75);

        bool musicSuccess = musicExists;
        if (!musicExists)
        {
            yield return StartCoroutine(DownloadTrackCoroutine(trackId, downloadKey, downloadServer, "music", musicPath, success => musicSuccess = success, 75, 100));

            if (!musicSuccess)
            {
                OnProcessingComplete?.Invoke(false, "Failed to download instrumental track");
                DisconnectWebSocket();
                yield break;
            }
        }

        OnProgressChanged?.Invoke(100);

        DisconnectWebSocket();
        OnProcessingComplete?.Invoke(true, "Processing complete");
    }

    private IEnumerator GetServerCoroutine(Action<bool> callback)
    {
        var url = $"{BaseApiUrl}/split/get_server";

        Task<string> getTask = Task.Run(async () =>
        {
            try
            {
                using (var handler = new HttpClientHandler())
                {
                    handler.UseProxy = true;
                    handler.UseDefaultCredentials = false;
                    using (var client = new HttpClient(handler))
                    {
                        client.Timeout = TimeSpan.FromSeconds(30);
                        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (X11; Linux x86_64; rv:145.0) Gecko/20100101 Firefox/145.0");
                        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "*/*");
                        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.5");
                        client.DefaultRequestHeaders.TryAddWithoutValidation("Referer", Referer);
                        client.DefaultRequestHeaders.TryAddWithoutValidation("Origin", Origin);
                        client.DefaultRequestHeaders.TryAddWithoutValidation("Patron", PatronToken);
                        client.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Fetch-Dest", "empty");
                        client.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Fetch-Mode", "cors");
                        client.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Fetch-Site", "same-site");

                        var response = await client.GetAsync(url);
                        if (!response.IsSuccessStatusCode)
                            Debug.LogError($"GetServer HTTP error: {(int)response.StatusCode}");
                        return await response.Content.ReadAsStringAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"GetServer failed: {ex.Message}");
                return null;
            }
        });

        while (!getTask.IsCompleted) yield return null;
        var json = getTask.Result;

        if (string.IsNullOrEmpty(json))
        {
            callback(false);
            yield break;
        }

        try
        {
            var serverMatch = System.Text.RegularExpressions.Regex.Match(json, "\"server\"\\s*:\\s*(\\d+)");
            if (serverMatch.Success)
            {
                _serverNumber = int.Parse(serverMatch.Groups[1].Value);
                callback(true);
            }
            else
            {
                Debug.LogError("Failed to parse server response");
                callback(false);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error parsing server response: {ex.Message}");
            callback(false);
        }
    }

    private IEnumerator UploadTrackCoroutine(string filePath, Action<long, string, int> callback)
    {
        if (!File.Exists(filePath))
        {
            Debug.LogError($"File not found: {filePath}");
            callback(0, null, 0);
            yield break;
        }

        var url = $"{GetServerUrl()}/split/tracks";
        Task<(long id, string key, int server, string error)> uploadTask = UploadWithHttpClientAsync(filePath, url);

        while (!uploadTask.IsCompleted)
            yield return null;

        bool useUnityFallback = false;
        long resultId = 0;
        string resultKey = null;

        if (uploadTask.IsFaulted)
        {
            useUnityFallback = true;
        }
        else
        {
            var result = uploadTask.Result;
            if (string.IsNullOrEmpty(result.error))
            {
                resultId = result.id;
                resultKey = result.key;
            }
            else
            {
                useUnityFallback = true;
            }
        }

        if (resultId > 0 && !string.IsNullOrEmpty(resultKey))
        {
            callback(resultId, resultKey, uploadTask.Result.server);
            yield break;
        }

        if (useUnityFallback)
            yield return StartCoroutine(UploadWithUnityWebRequestCoroutine(filePath, url, callback));
    }

    private IEnumerator UploadWithUnityWebRequestCoroutine(string filePath, string url, Action<long, string, int> callback, int maxRetries = 3)
    {
        byte[] fileData = File.ReadAllBytes(filePath);
        var fileName = Path.GetFileName(filePath);

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            var form = new WWWForm();
            form.AddBinaryData("file", fileData, fileName, "audio/mpeg");

            using (var request = UnityWebRequest.Post(url, form))
            {
                request.timeout = 300;
                request.useHttpContinue = false;
                SetHeaders(request);

                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    var cookieHeader = request.GetResponseHeader("Set-Cookie");
                    if (!string.IsNullOrEmpty(cookieHeader))
                        UpdateCookies(cookieHeader);

                    var json = request.downloadHandler.text;
                    var idMatch = System.Text.RegularExpressions.Regex.Match(json, "\"id\"\\s*:\\s*(\\d+)");
                    var keyMatch = System.Text.RegularExpressions.Regex.Match(json, "\"key\"\\s*:\\s*\"([^\"]+)\"");
                    var serverMatch = System.Text.RegularExpressions.Regex.Match(json, "\"s\"\\s*:\\s*(\\d+)");

                    if (idMatch.Success && keyMatch.Success)
                    {
                        var id = long.Parse(idMatch.Groups[1].Value);
                        var key = keyMatch.Groups[1].Value;
                        var server = serverMatch.Success ? int.Parse(serverMatch.Groups[1].Value) : 0;
                        callback(id, key, server);
                        yield break;
                    }
                    else
                    {
                        Debug.LogError($"Failed to parse upload response");
                        callback(0, null, 0);
                        yield break;
                    }
                }
                else
                {
                    string error = request.error ?? "Unknown error";
                    bool isRetryable = error.ToLower().Contains("curl error") ||
                                      error.ToLower().Contains("protocol") ||
                                      error.ToLower().Contains("stream") ||
                                      request.result == UnityWebRequest.Result.ConnectionError;

                    if (isRetryable && attempt < maxRetries)
                    {
                        yield return new WaitForSeconds(attempt * 2);
                        continue;
                    }
                    else
                    {
                        Debug.LogError($"Upload failed: {error}");
                        callback(0, null, 0);
                        yield break;
                    }
                }
            }
        }

        callback(0, null, 0);
    }

    private async Task<(long id, string key, int server, string error)> UploadWithHttpClientAsync(string filePath, string url)
    {
        try
        {
            using (var handler = new HttpClientHandler())
            {
                handler.AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate;
                handler.UseCookies = false;
                handler.UseProxy = true;
                handler.UseDefaultCredentials = false;

                using (var client = new HttpClient(handler))
                {
                    client.Timeout = TimeSpan.FromMinutes(10);
                    client.DefaultRequestHeaders.Clear();

                    var manualCookies = GetCookieHeader();
                    if (!string.IsNullOrEmpty(manualCookies))
                        client.DefaultRequestHeaders.Add("Cookie", manualCookies);

                    client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (X11; Linux x86_64; rv:145.0) Gecko/20100101 Firefox/145.0");
                    client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
                    client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.5");
                    client.DefaultRequestHeaders.TryAddWithoutValidation("Origin", Origin);
                    client.DefaultRequestHeaders.TryAddWithoutValidation("Referer", Referer);
                    client.DefaultRequestHeaders.TryAddWithoutValidation("Patron", PatronToken);
                    client.DefaultRequestHeaders.TryAddWithoutValidation("Locale", "en");
                    client.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Fetch-Dest", "empty");
                    client.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Fetch-Mode", "cors");
                    client.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Fetch-Site", "same-site");
                    client.DefaultRequestHeaders.TryAddWithoutValidation("Connection", "keep-alive");

                    byte[] fileData = await Task.Run(() => File.ReadAllBytes(filePath));
                    var fileName = Path.GetFileName(filePath);

                    var boundary = "----geckoformboundary" + Guid.NewGuid().ToString("N").Substring(0, 16);
                    using (var content = new MultipartFormDataContent(boundary))
                    {
                        var fileContent = new ByteArrayContent(fileData);
                        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("audio/mpeg");
                        fileContent.Headers.ContentDisposition = new System.Net.Http.Headers.ContentDispositionHeaderValue("form-data")
                        {
                            Name = "\"file\"",
                            FileName = $"\"{fileName}\""
                        };
                        content.Add(fileContent);

                        var response = await client.PostAsync(url, content);
                        var responseBody = await response.Content.ReadAsStringAsync();

                        if (response.Headers.TryGetValues("Set-Cookie", out var cookies))
                        {
                            foreach (var c in cookies)
                                UpdateCookies(c);
                        }

                        if (!response.IsSuccessStatusCode)
                        {
                            if (responseBody.Contains("Just a moment") || responseBody.Contains("cf-browser-verification"))
                                return (0, null, 0, "Cloudflare challenge detected");
                            return (0, null, 0, $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}");
                        }

                        var idMatch = System.Text.RegularExpressions.Regex.Match(responseBody, "\"id\"\\s*:\\s*(\\d+)");
                        var keyMatch = System.Text.RegularExpressions.Regex.Match(responseBody, "\"key\"\\s*:\\s*\"([^\"]+)\"");
                        var serverMatch = System.Text.RegularExpressions.Regex.Match(responseBody, "\"s\"\\s*:\\s*(\\d+)");

                        if (idMatch.Success && keyMatch.Success)
                        {
                            var id = long.Parse(idMatch.Groups[1].Value);
                            var key = keyMatch.Groups[1].Value;
                            var server = serverMatch.Success ? int.Parse(serverMatch.Groups[1].Value) : 0;
                            return (id, key, server, null);
                        }
                        else
                        {
                            return (0, null, 0, "Failed to parse response");
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            return (0, null, 0, $"Exception: {ex.Message}");
        }
    }

    private IEnumerator WaitForProcessingCoroutine(long id, string key, Action<bool> callback, int maxWaitSeconds = 300, float pollIntervalSeconds = 2f)
    {
        var vocalUrl = $"{GetServerUrl()}/split/listen/vocal/{id}/{key}";
        var startTime = Time.realtimeSinceStartup;
        var lastProgress = 25;
        _processingComplete = false;

        while ((Time.realtimeSinceStartup - startTime) < maxWaitSeconds)
        {
            if (_processingComplete)
            {
                callback(true);
                yield break;
            }

            Task<bool> pollTask = Task.Run(async () =>
            {
                try
                {
                    using (var handler = new HttpClientHandler())
                    {
                        handler.UseProxy = true;
                        handler.UseDefaultCredentials = false;
                        using (var client = new HttpClient(handler))
                        {
                            client.Timeout = TimeSpan.FromSeconds(10);
                            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (X11; Linux x86_64; rv:145.0) Gecko/20100101 Firefox/145.0");
                            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "*/*");
                            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.5");
                            client.DefaultRequestHeaders.TryAddWithoutValidation("Origin", Origin);
                            client.DefaultRequestHeaders.TryAddWithoutValidation("Referer", Referer);
                            client.DefaultRequestHeaders.TryAddWithoutValidation("Patron", PatronToken);
                            client.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Fetch-Dest", "empty");
                            client.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Fetch-Mode", "cors");
                            client.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Fetch-Site", "same-site");

                            var response = await client.GetAsync(vocalUrl);
                            return response.IsSuccessStatusCode;
                        }
                    }
                }
                catch
                {
                    return false;
                }
            });

            while (!pollTask.IsCompleted) yield return null;

            if (pollTask.Result)
            {
                callback(true);
                yield break;
            }

            var elapsed = (int)(Time.realtimeSinceStartup - startTime);
            // Map waiting progress to 25-50% range (upload phase completion)
            var progress = Mathf.Min(25 + (int)(elapsed * 25f / maxWaitSeconds), 49);

            if (progress > lastProgress)
            {
                Debug.Log($"Progress: {progress}%");
                OnProgressChanged?.Invoke(progress);
                lastProgress = progress;
            }

            yield return new WaitForSeconds(pollIntervalSeconds);
        }

        Debug.LogError("Processing timed out");
        callback(false);
    }

    private IEnumerator DownloadTrackCoroutine(long id, string key, int server, string trackType, string outputPath, Action<bool> callback, int progressStart = 50, int progressEnd = 75)
    {
        var url = $"https://api{server}.vocalremover.org/split/listen/{trackType}/{id}/{key}";
        Debug.Log($"-> Downloading {trackType} track from: {url}");

        // Use UnityWebRequest for progress tracking
        using (var request = UnityWebRequest.Get(url))
        {
            request.timeout = 900; // 15 minutes
            SetHeaders(request);

            var operation = request.SendWebRequest();

            // Track download progress
            while (!operation.isDone)
            {
                float downloadProgress = request.downloadProgress;
                int currentProgress = progressStart + (int)((progressEnd - progressStart) * downloadProgress);
                OnProgressChanged?.Invoke(currentProgress);
                yield return null;
            }

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"-> Download failed: {request.error} (code: {request.responseCode})");
                callback(false);
                yield break;
            }

            // Ensure directory exists
            var dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            // Write file
            try
            {
                File.WriteAllBytes(outputPath, request.downloadHandler.data);
                Debug.Log($"-> Downloaded {trackType} track: {request.downloadHandler.data.Length} bytes");
                callback(true);
            }
            catch (Exception ex)
            {
                Debug.LogError($"-> Error saving file: {ex.Message}");
                callback(false);
            }
        }
    }

    private IEnumerator ConnectWebSocketCoroutine(long trackId, string trackKey)
    {
        Debug.Log($"-> WebSocket: Server number is {_serverNumber}");

        var wsUrl = $"wss://api{_serverNumber}.vocalremover.org/cable";
        Debug.Log($"-> Attempting websocket-sharp connection: {wsUrl}");

        _wsConnected = false;

        // websocket-sharp with subprotocol for ActionCable
        _ws = new WebSocketSharp.WebSocket(wsUrl, "actioncable-v1-json");

        // Configure SSL/TLS
        _ws.SslConfiguration.EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12;
        _ws.SslConfiguration.ServerCertificateValidationCallback = (sender, cert, chain, errors) => true;

        // Set Origin header (critical for CORS)
        _ws.Origin = Origin;

        // Note: Most headers are restricted in websocket-sharp
        // Origin is set via property, subprotocol via constructor
        // The library handles User-Agent internally

        Debug.Log($"-> WebSocket configured with Origin: {Origin}");

        _ws.OnOpen += (sender, e) =>
        {
            Debug.Log($"-> WebSocket connected to {wsUrl}!");
            _wsConnected = true;
            // Don't send subscription here - wait for "welcome" message
        };

        _ws.OnMessage += (sender, e) =>
        {
            // Log ALL messages for now to debug
            Debug.Log($"-> WebSocket Msg: {e.Data}");

            // When we receive "welcome", send the subscription
            if (e.Data.Contains("\"type\":\"welcome\""))
            {
                Debug.Log("-> WebSocket: Received welcome, now subscribing...");

                // Correct format: FileSpleeterChannel with just id
                var identifier = $"{{\\\"id\\\":{trackId},\\\"channel\\\":\\\"FileSpleeterChannel\\\"}}";
                var subscribeMsg = $"{{\"command\":\"subscribe\",\"identifier\":\"{identifier}\"}}";
                Debug.Log($"-> Sending subscription: {subscribeMsg}");
                try
                {
                    _ws.Send(subscribeMsg);
                    Debug.Log($"-> Subscription sent successfully");
                }
                catch (Exception ex)
                {
                    Debug.LogError($"-> Subscription send error: {ex.Message}");
                }
            }

            // Check for subscription confirmation
            if (e.Data.Contains("\"type\":\"confirm_subscription\""))
            {
                Debug.Log("-> WebSocket: Subscription confirmed! Waiting for processing...");
            }

            // Check for processing completion - server sends "status":"ready"
            if (e.Data.Contains("\"status\":\"ready\""))
            {
                Debug.Log("-> WebSocket: Processing complete! Status: ready");

                // Parse key and server from the message
                // Format: {"identifier":"...","message":{"status":"ready","key":"abc123","s":26}}
                var keyMatch = Regex.Match(e.Data, "\"key\":\"([^\"]+)\"");
                var serverMatch = Regex.Match(e.Data, "\"s\":(\\d+)");

                if (keyMatch.Success)
                {
                    _readyKey = keyMatch.Groups[1].Value;
                    Debug.Log($"-> Extracted key: {_readyKey}");
                }
                if (serverMatch.Success)
                {
                    _readyServer = int.Parse(serverMatch.Groups[1].Value);
                    Debug.Log($"-> Extracted server: {_readyServer}");
                }

                _processingComplete = true;
            }

            // Log rejection/error messages
            if (e.Data.Contains("\"type\":\"reject\"") || e.Data.Contains("\"error\"") || e.Data.Contains("\"disconnect\""))
            {
                Debug.LogError($"-> WebSocket rejection/error: {e.Data}");
            }
        };

        _ws.OnError += (sender, e) =>
        {
            Debug.LogError($"-> WebSocket Error: {e.Message}");
            if (e.Exception != null)
            {
                Debug.LogError($"-> Exception: {e.Exception.GetType().Name}: {e.Exception.Message}");
            }
        };

        _ws.OnClose += (sender, e) =>
        {
            Debug.Log($"-> WebSocket Closed: {e.Code} - {e.Reason}");
            _wsConnected = false;
        };

        // Connect asynchronously
        Debug.Log($"-> Calling ConnectAsync...");
        _ws.ConnectAsync();

        // Wait for connection with timeout
        float timeout = 15f;
        float elapsed = 0f;
        while (!_wsConnected && elapsed < timeout)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (!_wsConnected)
        {
            Debug.LogWarning($"-> WebSocket connection did not complete in time. IsAlive: {_ws.IsAlive}");
        }

        // Give a moment for subscription to be sent and processed
        yield return new WaitForSeconds(1.0f);
    }

    private IEnumerator VisitMainPageCoroutine()
    {
        var url = "https://vocalremover.org/?patreon=1";
        Debug.Log($"-> Initializing session via HttpClient: {url}");

        Task<bool> visitTask = Task.Run(async () =>
        {
            try
            {
                using (var handler = new HttpClientHandler())
                {
                    handler.UseProxy = true;
                    handler.UseDefaultCredentials = false;
                    handler.UseCookies = false; // Capture manually
                    using (var client = new HttpClient(handler))
                    {
                        client.Timeout = TimeSpan.FromSeconds(30);
                        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (X11; Linux x86_64; rv:145.0) Gecko/20100101 Firefox/145.0");
                        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8");
                        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.5");

                        var response = await client.GetAsync(url);
                        if (response.Headers.TryGetValues("Set-Cookie", out var cookies))
                        {
                            foreach (var c in cookies) UpdateCookies(c);
                        }
                        return response.IsSuccessStatusCode;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"-> Session init failed: {ex.Message}");
                return false;
            }
        });

        while (!visitTask.IsCompleted) yield return null;
        Debug.Log($"-> Session initialized: {visitTask.Result}");
    }

    private void DisconnectWebSocket()
    {
        if (_ws != null)
        {
            try
            {
                if (_ws.IsAlive)
                {
                    _ws.Close();
                }
            }
            catch { }
            _ws = null;
            _wsConnected = false;
            Debug.Log("-> WebSocket Disconnected");
        }
    }

    private async Task<bool> ProbeUrl(string url)
    {
        using (var probe = UnityWebRequest.Get(url))
        {
            SetHeaders(probe);
            var probeOp = probe.SendWebRequest();
            while (!probeOp.isDone) await Task.Delay(50);

            if (probe.responseCode == 404) return false;
            // 403, 400, 426 are all "good" signs that the server exists
            if (probe.result == UnityWebRequest.Result.Success || probe.responseCode > 0) return true;
            return false;
        }
    }



    private void OnDestroy()
    {
        DisconnectWebSocket();
    }

    private void OnApplicationQuit()
    {
        DisconnectWebSocket();
    }
}

