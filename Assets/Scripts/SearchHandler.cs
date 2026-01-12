using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System.Collections.Generic;
using System.Web;
using TMPro;
using MPUIKIT;
using UnityEngine.UI;
using static LevelResourcesCompiler;
using UnityEngine.UIElements;
using System.Linq;
using System;
using System.Text;
using System.Security.Cryptography;

public class SearchHandler : MonoBehaviour
{
    public GameObject trackGUI;
    public Transform trackParent;
    public LevelResourcesCompiler LRC;
    public AlertManager alertManager;
    public GameObject selectorMenu;
    [Tooltip("Loading indicator to show during search")]
    public GameObject loadingIndicator;

    public enum SearchType { None, Search, Favorites, Downloads }
    private SearchType currentSearchType = SearchType.None;
    private string currentSearchQuery = "";

    // Anonymous API support
    private string anonymousAccessToken;
    private string clientToken;
    private long anonymousTokenExpiry = 0;
    private long clientTokenExpiry = 0;
    private string deviceId;

    // Spotify web player constants
    private const string WEB_CLIENT_ID = "d8a5ed958d274c2e8ee717e6a4b0971d";
    private const string CLIENT_VERSION = "1.2.82.42.g4c44af04";
    private const string SEARCH_HASH = "fcad5a3e0d5af727fb76966f06971c19cfa2275e6ff7671196753e008611873c";

    void Start()
    {
        // Generate a persistent device ID for anonymous API
        deviceId = PlayerPrefs.GetString("DEVICE_ID", "");
        if (string.IsNullOrEmpty(deviceId))
        {
            deviceId = Guid.NewGuid().ToString();
            PlayerPrefs.SetString("DEVICE_ID", deviceId);
            PlayerPrefs.Save();
        }

        StartCoroutine(TokenAndRequestRoutine());
    }

    public void Refresh()
    {
        switch (currentSearchType)
        {
            case SearchType.Search:
                if (!string.IsNullOrEmpty(currentSearchQuery)) Search(currentSearchQuery);
                break;
            case SearchType.Favorites:
                DisplayFavoriteSongs();
                break;
            case SearchType.Downloads:
                DisplayDownloadedSongs();
                break;
        }
    }

    public void Search(string search)
    {
        currentSearchType = SearchType.Search;
        currentSearchQuery = search;
        foreach (Transform child in trackParent.transform)
        {
            Destroy(child.gameObject);
        }
        if (!string.IsNullOrEmpty(search))
        {
            if (loadingIndicator != null)
            {
                loadingIndicator.SetActive(true);
            }
            StartCoroutine(SearchRoutine(search));
        }
    }

    IEnumerator TokenAndRequestRoutine()
    {
        while (true)
        {
            // Get anonymous tokens
            yield return StartCoroutine(GetAnonymousTokens());

            // Wait for 1 hour (3600 seconds) before repeating.
            yield return new WaitForSeconds(3600);
        }
    }

    #region Anonymous API Methods

    IEnumerator GetAnonymousTokens()
    {
        // First get the client token
        yield return StartCoroutine(GetClientToken());

        // Then get the anonymous access token
        yield return StartCoroutine(GetAnonymousAccessToken());
    }

    IEnumerator GetClientToken()
    {
        // Check if we have a valid cached token
        long currentTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (!string.IsNullOrEmpty(clientToken) && currentTime < clientTokenExpiry)
        {
            yield break;
        }

        string url = "https://clienttoken.spotify.com/v1/clienttoken";

        string jsonBody = $@"{{
            ""client_data"": {{
                ""client_version"": ""{CLIENT_VERSION}"",
                ""client_id"": ""{WEB_CLIENT_ID}"",
                ""js_sdk_data"": {{
                    ""device_brand"": ""unknown"",
                    ""device_model"": ""unknown"",
                    ""os"": ""windows"",
                    ""os_version"": ""unknown"",
                    ""device_id"": ""{deviceId}"",
                    ""device_type"": ""computer""
                }}
            }}
        }}";

        using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Accept", "application/json");
            request.SetRequestHeader("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            request.SetRequestHeader("Origin", "https://open.spotify.com");
            request.SetRequestHeader("Referer", "https://open.spotify.com/");

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                Debug.Log("Client token response: " + request.downloadHandler.text);
                var response = JsonUtility.FromJson<ClientTokenResponse>(request.downloadHandler.text);
                if (response.granted_token != null)
                {
                    clientToken = response.granted_token.token;
                    clientTokenExpiry = currentTime + (response.granted_token.expires_after_seconds * 1000);
                    Debug.Log("Client token obtained successfully: " + clientToken.Substring(0, 30) + "...");
                }
                else
                {
                    Debug.LogError("Client token response missing granted_token");
                }
            }
            else
            {
                Debug.LogError("Failed to get client token: " + request.error + "\nResponse: " + request.downloadHandler.text);
            }
        }
    }

    IEnumerator GetAnonymousAccessToken()
    {
        // Check if we have a valid cached token
        long currentTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (!string.IsNullOrEmpty(anonymousAccessToken) && currentTime < anonymousTokenExpiry)
        {
            yield break;
        }

        // Step 1: Fetch the TOTP secret if not already loaded
        yield return StartCoroutine(FetchTotpSecret());

        // Step 2: Fetch the main page to get serverTime and cookies
        long serverTimeMs = 0;
        string spTCookie = "";

        using (UnityWebRequest pageRequest = UnityWebRequest.Get("https://open.spotify.com/"))
        {
            pageRequest.SetRequestHeader("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            pageRequest.SetRequestHeader("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");

            yield return pageRequest.SendWebRequest();

            if (pageRequest.result == UnityWebRequest.Result.Success)
            {
                string html = pageRequest.downloadHandler.text;

                // Extract serverTime from the embedded config
                int configStart = html.IndexOf("id=\"appServerConfig\"");
                if (configStart > 0)
                {
                    int dataStart = html.IndexOf(">", configStart) + 1;
                    int dataEnd = html.IndexOf("<", dataStart);
                    if (dataStart > 0 && dataEnd > dataStart)
                    {
                        string base64Config = html.Substring(dataStart, dataEnd - dataStart);
                        try
                        {
                            string configJson = Encoding.UTF8.GetString(Convert.FromBase64String(base64Config));
                            var config = JsonUtility.FromJson<SpotifyServerConfig>(configJson);
                            // serverTime from config is in seconds, convert to ms
                            serverTimeMs = config.serverTime * 1000;
                            Debug.Log($"Got serverTime: {config.serverTime} (ms: {serverTimeMs})");
                        }
                        catch (Exception e)
                        {
                            Debug.LogError("Failed to parse server config: " + e.Message);
                        }
                    }
                }

                // Get sp_t cookie from response headers
                string setCookie = pageRequest.GetResponseHeader("Set-Cookie");
                if (!string.IsNullOrEmpty(setCookie))
                {
                    var match = System.Text.RegularExpressions.Regex.Match(setCookie, @"sp_t=([^;]+)");
                    if (match.Success)
                    {
                        spTCookie = match.Groups[1].Value;
                        Debug.Log($"Got sp_t cookie: {spTCookie}");
                    }
                }
            }
            else
            {
                Debug.LogError("Failed to fetch Spotify page: " + pageRequest.error);
                yield break;
            }
        }

        if (serverTimeMs == 0)
        {
            serverTimeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        if (string.IsNullOrEmpty(spTCookie))
        {
            spTCookie = deviceId;
        }

        // Step 3: Generate TOTP
        string totp = GenerateSpotifyTotp(serverTimeMs);
        Debug.Log($"Generated TOTP: {totp} (version: {totpVersion})");

        // Step 4: Get access token
        string url = $"https://open.spotify.com/api/token?reason=init&productType=web-player&totp={totp}&totpServer={totp}&totpVer={totpVersion}";

        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            request.SetRequestHeader("Accept", "*/*");
            request.SetRequestHeader("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            request.SetRequestHeader("Referer", "https://open.spotify.com/");
            request.SetRequestHeader("Cookie", $"sp_t={spTCookie}");

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                Debug.Log("Token response: " + request.downloadHandler.text);
                var response = JsonUtility.FromJson<AnonymousTokenResponse>(request.downloadHandler.text);
                if (!string.IsNullOrEmpty(response.accessToken))
                {
                    anonymousAccessToken = response.accessToken;
                    anonymousTokenExpiry = response.accessTokenExpirationTimestampMs;
                    Debug.Log("Anonymous access token obtained successfully");
                }
            }
            else
            {
                Debug.LogError("Failed to get anonymous access token: " + request.error + "\nResponse: " + request.downloadHandler.text);
            }
        }
    }

    // Cached TOTP secret
    private byte[] totpSecret = null;
    private int totpVersion = 0;

    IEnumerator FetchTotpSecret()
    {
        if (totpSecret != null)
        {
            yield break;
        }

        string url = "https://raw.githubusercontent.com/xyloflake/spot-secrets-go/refs/heads/main/secrets/secretDict.json";

        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                try
                {
                    string json = request.downloadHandler.text;
                    // Parse the JSON manually since it has numeric keys
                    // Find the highest version (last key)
                    var secretDict = ParseSecretDict(json);
                    if (secretDict.Count > 0)
                    {
                        // Get the highest version
                        int maxVersion = 0;
                        int[] cipherArray = null;
                        foreach (var kvp in secretDict)
                        {
                            if (kvp.Key > maxVersion)
                            {
                                maxVersion = kvp.Key;
                                cipherArray = kvp.Value;
                            }
                        }

                        if (cipherArray != null)
                        {
                            totpVersion = maxVersion;
                            // Transform: val ^ ((i % 33) + 9)
                            StringBuilder sb = new StringBuilder();
                            for (int i = 0; i < cipherArray.Length; i++)
                            {
                                int transformed = cipherArray[i] ^ ((i % 33) + 9);
                                sb.Append(transformed.ToString());
                            }
                            totpSecret = Encoding.UTF8.GetBytes(sb.ToString());
                            Debug.Log($"TOTP secret loaded, version: {totpVersion}");
                        }
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError("Failed to parse TOTP secret: " + e.Message);
                }
            }
            else
            {
                Debug.LogError("Failed to fetch TOTP secret: " + request.error);
            }
        }

        // Fallback to hardcoded version 61 if fetch failed
        if (totpSecret == null)
        {
            Debug.Log("Using fallback TOTP secret (version 61)");
            int[] fallbackCipher = new int[] { 44, 55, 47, 42, 70, 40, 34, 114, 76, 74, 50, 111, 120, 97, 75, 76, 94, 102, 43, 69, 49, 120, 118, 80, 64, 78 };
            totpVersion = 61;
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < fallbackCipher.Length; i++)
            {
                int transformed = fallbackCipher[i] ^ ((i % 33) + 9);
                sb.Append(transformed.ToString());
            }
            totpSecret = Encoding.UTF8.GetBytes(sb.ToString());
        }
    }

    Dictionary<int, int[]> ParseSecretDict(string json)
    {
        var result = new Dictionary<int, int[]>();
        // Simple JSON parsing for {"version": [array], ...} format
        json = json.Trim();
        if (json.StartsWith("{") && json.EndsWith("}"))
        {
            json = json.Substring(1, json.Length - 2);
            // Split by ], and parse each entry
            int pos = 0;
            while (pos < json.Length)
            {
                // Find key
                int keyStart = json.IndexOf('"', pos);
                if (keyStart < 0) break;
                int keyEnd = json.IndexOf('"', keyStart + 1);
                if (keyEnd < 0) break;
                string key = json.Substring(keyStart + 1, keyEnd - keyStart - 1);

                // Find array
                int arrayStart = json.IndexOf('[', keyEnd);
                if (arrayStart < 0) break;
                int arrayEnd = json.IndexOf(']', arrayStart);
                if (arrayEnd < 0) break;
                string arrayStr = json.Substring(arrayStart + 1, arrayEnd - arrayStart - 1);

                // Parse array
                string[] parts = arrayStr.Split(',');
                int[] arr = new int[parts.Length];
                for (int i = 0; i < parts.Length; i++)
                {
                    arr[i] = int.Parse(parts[i].Trim());
                }

                if (int.TryParse(key, out int version))
                {
                    result[version] = arr;
                }

                pos = arrayEnd + 1;
            }
        }
        return result;
    }

    string GenerateSpotifyTotp(long serverTimeMs)
    {
        if (totpSecret == null)
        {
            Debug.LogError("TOTP secret not loaded!");
            return "000000";
        }

        // Standard HMAC-SHA1 TOTP (RFC 6238)
        long counter = serverTimeMs / 1000 / 30;

        // Convert counter to 8-byte big-endian
        byte[] counterBytes = new byte[8];
        for (int i = 7; i >= 0; i--)
        {
            counterBytes[i] = (byte)(counter & 0xFF);
            counter >>= 8;
        }

        // HMAC-SHA1
        using (var hmac = new System.Security.Cryptography.HMACSHA1(totpSecret))
        {
            byte[] hash = hmac.ComputeHash(counterBytes);

            // Dynamic truncation
            int offset = hash[hash.Length - 1] & 0x0F;
            int binary = ((hash[offset] & 0x7F) << 24)
                       | ((hash[offset + 1] & 0xFF) << 16)
                       | ((hash[offset + 2] & 0xFF) << 8)
                       | (hash[offset + 3] & 0xFF);

            int otp = binary % 1000000;
            return otp.ToString("D6");
        }
    }

    IEnumerator SearchAnonymous(string search)
    {
        // Ensure we have valid tokens
        long currentTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (string.IsNullOrEmpty(anonymousAccessToken) || currentTime >= anonymousTokenExpiry ||
            string.IsNullOrEmpty(clientToken) || currentTime >= clientTokenExpiry)
        {
            yield return StartCoroutine(GetAnonymousTokens());
        }

        if (string.IsNullOrEmpty(anonymousAccessToken))
        {
            AlertManager.Instance.ShowError("Failed to connect to Spotify.", "Could not obtain access token for anonymous search.", "Dismiss");
            yield break;
        }

        if (string.IsNullOrEmpty(clientToken))
        {
            Debug.LogError("Client token is missing!");
            AlertManager.Instance.ShowError("Failed to connect to Spotify.", "Could not obtain client token for search.", "Dismiss");
            yield break;
        }

        Debug.Log($"Searching with access token: {anonymousAccessToken.Substring(0, 20)}... and client token: {clientToken.Substring(0, 20)}...");

        string url = "https://api-partner.spotify.com/pathfinder/v2/query";

        string jsonBody = $@"{{
            ""variables"": {{
                ""searchTerm"": ""{EscapeJsonString(search)}"",
                ""offset"": 0,
                ""limit"": 20,
                ""numberOfTopResults"": 5,
                ""includeAudiobooks"": false,
                ""includeArtistHasConcertsField"": false,
                ""includePreReleases"": false,
                ""includeLocalConcertsField"": false
            }},
            ""operationName"": ""searchDesktop"",
            ""extensions"": {{
                ""persistedQuery"": {{
                    ""version"": 1,
                    ""sha256Hash"": ""{SEARCH_HASH}""
                }}
            }}
        }}";

        using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json;charset=UTF-8");
            request.SetRequestHeader("Accept", "application/json");
            request.SetRequestHeader("Accept-Language", "en");
            request.SetRequestHeader("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            request.SetRequestHeader("Authorization", "Bearer " + anonymousAccessToken);
            request.SetRequestHeader("client-token", clientToken);
            request.SetRequestHeader("app-platform", "WebPlayer");
            request.SetRequestHeader("spotify-app-version", CLIENT_VERSION);
            request.SetRequestHeader("Origin", "https://open.spotify.com");
            request.SetRequestHeader("Referer", "https://open.spotify.com/");

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                string json = request.downloadHandler.text;
                ParseAnonymousSearchResponse(json);
            }
            else
            {
                Debug.LogError("Anonymous search failed: " + request.error + "\nResponse: " + request.downloadHandler.text);
                // Retry with fresh tokens
                anonymousAccessToken = null;
                clientToken = null;
                if (loadingIndicator != null)
                {
                    loadingIndicator.SetActive(false);
                }
            }
        }
    }

    string EscapeJsonString(string str)
    {
        return str.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
    }

    void ParseAnonymousSearchResponse(string json)
    {
        try
        {
            var response = JsonUtility.FromJson<PathfinderSearchResponse>(json);

            if (response.data?.searchV2?.tracksV2?.items == null)
            {
                Debug.Log("No tracks found in anonymous search response.");
                if (loadingIndicator != null)
                {
                    loadingIndicator.SetActive(false);
                }
                return;
            }

            // Cache downloaded URLs for efficient lookup
            HashSet<string> downloadedUrls = new HashSet<string>(FavoritesManager.GetAllDownloads().Select(d => d.url));

            foreach (var item in response.data.searchV2.tracksV2.items)
            {
                var trackData = item.item?.data;
                if (trackData == null) continue;

                // Convert to the existing Track format for compatibility
                Track track = ConvertPathfinderTrackToTrack(trackData);

                GameObject temp = Instantiate(trackGUI, trackParent);
                if (PlayerPrefs.GetInt("multiplayer") == 0)
                {
                    if (PlayerPrefs.GetInt("editing") == 0)
                    {
                        temp.GetComponent<UnityEngine.UI.Button>().onClick.AddListener(delegate
                        {
                            LRC.PreCompile(track.external_urls.spotify, track.name, track.artists[0].name, ConvertDuration(track.duration_ms), track);
                        });
                    }
                    else
                    {
                        temp.GetComponent<UnityEngine.UI.Button>().onClick.AddListener(delegate
                        {
                            EditorManager.Instance.StartEditing(track.name, track.artists[0].name, track.album.name, track.duration_ms, track.external_urls.spotify);
                        });
                    }
                }
                else
                {
                    temp.GetComponent<UnityEngine.UI.Button>().onClick.AddListener(delegate
                    {
                        SongData newSong = new SongData(
                            track.name,
                            track.artists[0].name,
                            track.album.name,
                            ConvertDuration(track.duration_ms),
                            track.album.images.Count > 0 ? track.album.images[0].url : "",
                            track.external_urls.spotify
                        );

                        if (PlayerData.LocalPlayerInstance != null && PlayerData.LocalPlayerInstance.IsHost.Value)
                        {
                            LevelResourcesCompiler.lastPrecompiledTrack = track;
                            PlayerData.LocalPlayerInstance.RequestChangeSong_ServerRpc(newSong);
                        }
                        selectorMenu.SetActive(false);
                    });
                }

                temp.transform.GetChild(1).GetComponent<TextMeshProUGUI>().text = track.name;
                temp.transform.GetChild(2).GetComponent<TextMeshProUGUI>().text = track.artists[0].name;
                temp.transform.GetChild(5).GetComponent<TextMeshProUGUI>().text = track.external_urls.spotify;

                string lengthText = "Song Length: " + ConvertDuration(track.duration_ms);
                if (downloadedUrls.Contains(track.external_urls.spotify))
                {
                    lengthText += " <color=#5dff4d>(Downloaded)</color>";
                }
                temp.transform.GetChild(4).GetComponent<TextMeshProUGUI>().text = lengthText;

                SetAlbumCoverFromTrack(track, temp.transform.GetChild(3).GetComponent<MPImage>(), temp.transform.GetChild(0).GetComponent<MPImage>());
            }

            if (loadingIndicator != null)
            {
                loadingIndicator.SetActive(false);
            }
        }
        catch (Exception e)
        {
            Debug.LogError("Error parsing anonymous search response: " + e.Message);
            if (loadingIndicator != null)
            {
                loadingIndicator.SetActive(false);
            }
        }
    }

    Track ConvertPathfinderTrackToTrack(PathfinderTrackData trackData)
    {
        Track track = new Track();
        track.name = trackData.name;
        track.id = trackData.id;
        track.uri = trackData.uri;
        track.duration_ms = trackData.duration?.totalMilliseconds ?? 0;

        // Convert URI to Spotify URL
        string trackId = trackData.uri?.Replace("spotify:track:", "") ?? trackData.id;
        track.external_urls = new ExternalUrls { spotify = $"https://open.spotify.com/track/{trackId}" };

        // Convert artists
        track.artists = new List<Artist>();
        if (trackData.artists?.items != null)
        {
            foreach (var artistItem in trackData.artists.items)
            {
                string artistName = artistItem.profile?.name ?? "Unknown Artist";
                string artistId = artistItem.uri?.Replace("spotify:artist:", "") ?? "";
                track.artists.Add(new Artist
                {
                    name = artistName,
                    id = artistId,
                    uri = artistItem.uri,
                    external_urls = new ExternalUrls { spotify = $"https://open.spotify.com/artist/{artistId}" }
                });
            }
        }

        if (track.artists.Count == 0)
        {
            track.artists.Add(new Artist { name = "Unknown Artist" });
        }

        // Convert album
        track.album = new Album();
        if (trackData.albumOfTrack != null)
        {
            track.album.name = trackData.albumOfTrack.name ?? "Unknown Album";
            track.album.id = trackData.albumOfTrack.id;
            track.album.uri = trackData.albumOfTrack.uri;
            string albumId = trackData.albumOfTrack.uri?.Replace("spotify:album:", "") ?? trackData.albumOfTrack.id ?? "";
            track.album.external_urls = new ExternalUrls { spotify = $"https://open.spotify.com/album/{albumId}" };

            // Convert images
            track.album.images = new List<Image>();
            if (trackData.albumOfTrack.coverArt?.sources != null)
            {
                foreach (var source in trackData.albumOfTrack.coverArt.sources)
                {
                    track.album.images.Add(new Image
                    {
                        url = source.url,
                        width = source.width,
                        height = source.height
                    });
                }
            }
        }
        else
        {
            track.album.name = "Unknown Album";
            track.album.images = new List<Image>();
        }

        return track;
    }

    #endregion

    IEnumerator SearchRoutine(string search)
    {
        yield return StartCoroutine(SearchAnonymous(search));
    }

    public void DisplayFavoriteSongs()
    {
        currentSearchType = SearchType.Favorites;
        // 1. Clear existing items
        foreach (Transform child in trackParent)
        {
            Destroy(child.gameObject);
        }

        // 2. Get all favorites
        List<Song> favoriteSongs = FavoritesManager.GetAllFavorites();

        if (favoriteSongs == null || favoriteSongs.Count == 0)
        {
            alertManager.ShowInfo("No favorites found.", "You haven't added any favorite song yet! You can do so by clicking the heart icon next to the play button in any song.", "Dismiss");
            return;
        }

        // Create a set of downloaded URLs for efficient lookup
        HashSet<string> downloadedUrls = new HashSet<string>(FavoritesManager.GetAllDownloads().Select(d => d.url));

        // 3. Iterate and populate UI
        foreach (Song favSong in favoriteSongs)
        {
            GameObject temp = Instantiate(trackGUI, trackParent);
            UnityEngine.UI.Button button = temp.GetComponent<UnityEngine.UI.Button>();

            // --- Create an adapter SearchHandler.Track object from favSong data ---
            SearchHandler.Track adaptedTrack = new SearchHandler.Track
            {
                name = favSong.name,
                artists = new List<SearchHandler.Artist> { new SearchHandler.Artist { name = favSong.artist } },
                external_urls = new SearchHandler.ExternalUrls { spotify = favSong.url },
                duration_ms = favSong.duration_ms,
                album = new SearchHandler.Album // Populate album for cover image consistency
                {
                    name = favSong.album, // Populate album name from favorite
                    images = new List<SearchHandler.Image>()
                }
                // Populate other fields if LRC.PreCompile or downstream code relies on them
                // e.g., uri, explicit, popularity, etc., if you store them in FavoritesManager.Song
            };

            if (!string.IsNullOrEmpty(favSong.coverurl))
            {
                // Add a single image to the album. If LRC expects specific sizes or multiple, adjust this.
                adaptedTrack.album.images.Add(new SearchHandler.Image { url = favSong.coverurl, width = 640, height = 640 }); // Example dimensions
            }
            // --- End of adapter Track creation ---

            if (button != null)
            {
                // Capture currentFavSong and adaptedTrack for the delegate
                Song currentFavSong = favSong;
                SearchHandler.Track currentAdaptedTrack = adaptedTrack;

                if (PlayerPrefs.GetInt("multiplayer") == 0)
                {
                    if (PlayerPrefs.GetInt("editing") == 0)
                    {
                        button.onClick.AddListener(delegate
                        {
                            // Use currentFavSong.length for the formatted duration string,
                            // and currentAdaptedTrack for the full (adapted) Track object.
                            LRC.PreCompile(currentFavSong.url, currentFavSong.name, currentFavSong.artist, currentFavSong.length, currentAdaptedTrack);
                        });
                    }
                    else
                    {
                        button.onClick.AddListener(delegate
                        {
                            // LRC.PreCompile will receive the original source URL (e.g., Spotify URL)
                            // for the 'urlToPlay' parameter.
                            EditorManager.Instance.StartEditing(currentFavSong.name, currentFavSong.artist, currentAdaptedTrack.album.name, currentAdaptedTrack.duration_ms, currentFavSong.url);
                        });
                    }
                }
                else
                {
                    button.onClick.AddListener(delegate
                    {
                        // 1. Create a new SongData struct with the selected song's info.
                        SongData newSong = new SongData(
                            currentFavSong.name,
                            currentFavSong.artist,
                            currentFavSong.album, // Use stored album name
                            currentFavSong.length,
                            currentAdaptedTrack.album.images[0].url, // Example URL
                            currentFavSong.url
                        );

                        // 2. Check if we are the host.
                        if (PlayerData.LocalPlayerInstance != null && PlayerData.LocalPlayerInstance.IsHost.Value)
                        {
                            // --- NEW: Cache the adapted track object before sending the RPC ---
                            LevelResourcesCompiler.lastPrecompiledTrack = currentAdaptedTrack;

                            // 3. Call the RPC on our own player object to request the change.
                            PlayerData.LocalPlayerInstance.RequestChangeSong_ServerRpc(newSong);
                        }
                        selectorMenu.SetActive(false);
                    });
                }


            }

            // Populate UI elements (adjust child indices if your prefab layout differs)
            temp.transform.GetChild(1).GetComponent<TextMeshProUGUI>().text = favSong.name;
            temp.transform.GetChild(2).GetComponent<TextMeshProUGUI>().text = favSong.artist;
            temp.transform.GetChild(5).GetComponent<TextMeshProUGUI>().text = favSong.url; // Or spotifyTrackId if more appropriate for display

            string lengthText = "Song Length: " + favSong.length;
            if (downloadedUrls.Contains(favSong.url))
            {
                lengthText += " <color=#5dff4d>(Downloaded)</color>";
            }
            temp.transform.GetChild(4).GetComponent<TextMeshProUGUI>().text = lengthText;

            // Cover art - using your existing LoadImageFromUrl which is good
            MPImage coverImageComponent = temp.transform.GetChild(3).GetComponent<MPImage>();
            MPImage backgroundImageComponent = temp.transform.GetChild(0).GetComponent<MPImage>(); // Assuming background uses same cover

            if (!string.IsNullOrEmpty(favSong.coverurl))
            {
                if (coverImageComponent != null) StartCoroutine(LoadImageFromUrl(favSong.coverurl, coverImageComponent));
                if (backgroundImageComponent != null) StartCoroutine(LoadImageFromUrl(favSong.coverurl, backgroundImageComponent));
            }
            else
            {
                Debug.LogWarning($"No cover URL for favorite: {favSong.name}");
                // Optionally, set a default placeholder image
            }
        }
    }


    public void DisplayDownloadedSongs()
    {
        currentSearchType = SearchType.Downloads;
        // 1. Clear existing items
        foreach (Transform child in trackParent)
        {
            Destroy(child.gameObject);
        }

        // 2. Get all downloaded songs
        List<Song> downloadedSongs = FavoritesManager.GetAllDownloads(); // Changed from GetAllFavorites()

        if (downloadedSongs == null || downloadedSongs.Count == 0)
        {
            // Updated alert message for downloads
            alertManager.ShowInfo("No downloads found.", "You haven't downloaded any songs yet! Songs you download will appear here.", "Dismiss");
            return;
        }

        // 3. Iterate and populate UI
        foreach (Song downloadedSong in downloadedSongs) // Changed variable name
        {
            GameObject temp = Instantiate(trackGUI, trackParent);
            UnityEngine.UI.Button button = temp.GetComponent<UnityEngine.UI.Button>();

            // --- Create an adapter SearchHandler.Track object from downloadedSong data ---
            // This logic remains the same as the Song object structure is consistent
            SearchHandler.Track adaptedTrack = new SearchHandler.Track
            {
                name = downloadedSong.name,
                artists = new List<SearchHandler.Artist> { new SearchHandler.Artist { name = downloadedSong.artist } },
                external_urls = new SearchHandler.ExternalUrls { spotify = downloadedSong.url }, // Original source URL
                duration_ms = downloadedSong.duration_ms,
                album = new SearchHandler.Album
                {
                    name = downloadedSong.album, // Populate album name from download
                    images = new List<SearchHandler.Image>()
                }
            };

            if (!string.IsNullOrEmpty(downloadedSong.coverurl))
            {
                adaptedTrack.album.images.Add(new SearchHandler.Image { url = downloadedSong.coverurl, width = 640, height = 640 });
            }
            // --- End of adapter Track creation ---

            if (button != null)
            {
                Song currentSong = downloadedSong; // Changed variable name
                SearchHandler.Track currentAdaptedTrack = adaptedTrack;

                if (PlayerPrefs.GetInt("multiplayer") == 0)
                {
                    if (PlayerPrefs.GetInt("editing") == 0)
                    {
                        button.onClick.AddListener(delegate
                        {
                            // LRC.PreCompile will receive the original source URL (e.g., Spotify URL)
                            // for the 'urlToPlay' parameter.
                            LRC.PreCompile(currentSong.url, currentSong.name, currentSong.artist, currentSong.length, currentAdaptedTrack);
                        });
                    }
                    else
                    {
                        button.onClick.AddListener(delegate
                        {
                            // LRC.PreCompile will receive the original source URL (e.g., Spotify URL)
                            // for the 'urlToPlay' parameter.
                            EditorManager.Instance.StartEditing(currentSong.name, currentSong.artist, currentAdaptedTrack.album.name, currentAdaptedTrack.duration_ms, currentSong.url);
                        });
                    }
                }
                else
                {
                    button.onClick.AddListener(delegate
                    {
                        // 1. Create a new SongData struct with the selected song's info.
                        SongData newSong = new SongData(
                            currentSong.name,
                            currentSong.artist,
                            currentSong.album, // Use stored album name
                            currentSong.length,
                            currentAdaptedTrack.album.images[0].url, // Example URL
                            currentSong.url
                        );

                        // 2. Check if we are the host.
                        if (PlayerData.LocalPlayerInstance != null && PlayerData.LocalPlayerInstance.IsHost.Value)
                        {
                            // --- NEW: Cache the adapted track object before sending the RPC ---
                            LevelResourcesCompiler.lastPrecompiledTrack = currentAdaptedTrack;

                            // 3. Call the RPC on our own player object to request the change.
                            PlayerData.LocalPlayerInstance.RequestChangeSong_ServerRpc(newSong);
                        }
                        selectorMenu.SetActive(false);
                    });
                }
            }

            // Populate UI elements (adjust child indices if your prefab layout differs)
            temp.transform.GetChild(1).GetComponent<TextMeshProUGUI>().text = downloadedSong.name;
            temp.transform.GetChild(2).GetComponent<TextMeshProUGUI>().text = downloadedSong.artist;
            // Child 5 displays the original source URL. You could also add a " (Downloaded)" suffix if desired.
            temp.transform.GetChild(5).GetComponent<TextMeshProUGUI>().text = downloadedSong.url;
            temp.transform.GetChild(4).GetComponent<TextMeshProUGUI>().text = "Song Length: " + downloadedSong.length + " <color=#5dff4d>(Downloaded)</color>";

            // Cover art
            MPImage coverImageComponent = temp.transform.GetChild(3).GetComponent<MPImage>();
            MPImage backgroundImageComponent = temp.transform.GetChild(0).GetComponent<MPImage>();

            if (!string.IsNullOrEmpty(downloadedSong.coverurl))
            {
                if (coverImageComponent != null) StartCoroutine(LoadImageFromUrl(downloadedSong.coverurl, coverImageComponent));
                if (backgroundImageComponent != null) StartCoroutine(LoadImageFromUrl(downloadedSong.coverurl, backgroundImageComponent));
            }
            else
            {
                Debug.LogWarning($"No cover URL for downloaded song: {downloadedSong.name}");
                // Optionally, set a default placeholder image
            }
        }
    }

    // Ensure LoadImageFromUrl is present in SearchHandler.cs as you provided
    IEnumerator LoadImageFromUrl(string imageUrl, MPImage targetMpImage)
    {
        if (targetMpImage == null || string.IsNullOrEmpty(imageUrl))
        {
            if (targetMpImage == null) Debug.LogError("Target MPImage component is null.");
            if (string.IsNullOrEmpty(imageUrl)) Debug.LogWarning("ImageUrl is null or empty for LoadImageFromUrl.");
            yield break;
        }

        using (UnityWebRequest request = UnityWebRequestTexture.GetTexture(imageUrl))
        {
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                Texture2D texture = DownloadHandlerTexture.GetContent(request);
                if (texture != null)
                {
                    targetMpImage.sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
                }
            }
            else
            {
                Debug.LogError($"Error loading image from {imageUrl}: {request.error}");
            }
        }
    }

    public void SetAlbumCoverFromTrack(Track track, MPImage image, MPImage image2)
    {
        if (track == null || track.album == null || track.album.images == null || track.album.images.Count == 0)
        {
            Debug.LogWarning("No album cover found for this track.");
            return;
        }

        // Find the highest resolution image (largest width)
        var highestResImage = track.album.images[0];
        foreach (var img in track.album.images)
        {
            if (img.width > highestResImage.width)
            {
                highestResImage = img;
            }
        }

        // Start the coroutine to download and apply the image
        StartCoroutine(DownloadAlbumCover(highestResImage.url, image, image2));
    }

    private IEnumerator DownloadAlbumCover(string imageUrl, MPImage image, MPImage image2)
    {
        using (UnityWebRequest request = UnityWebRequestTexture.GetTexture(imageUrl))
        {
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                Texture2D texture = DownloadHandlerTexture.GetContent(request);
                Sprite albumCover = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
                image.sprite = albumCover;
                image2.sprite = albumCover;
            }
            else
            {
                Debug.LogError("Failed to download album cover: " + request.error);
            }
        }
    }

    string ConvertDuration(int durationMs)
    {
        int totalSeconds = durationMs / 1000; // Convert milliseconds to seconds
        int minutes = totalSeconds / 60;
        int seconds = totalSeconds % 60;
        return $"{minutes}:{seconds:D2}"; // Format as "m:ss", ensuring two-digit seconds
    }

    // Classes to parse the search response (kept for compatibility with favorites/downloads)
    [System.Serializable]
    public class SearchResponse
    {
        public PagedTracks tracks;
        public PagedArtists artists;
        public PagedAlbums albums;
        public PagedPlaylists playlists;
        public PagedShows shows;
        public PagedEpisodes episodes;
        public PagedAudiobooks audiobooks;
    }

    #region Paged Response Classes

    [System.Serializable]
    public class PagedTracks
    {
        public string href;
        public int limit;
        public string next;
        public int offset;
        public string previous;
        public int total;
        public List<Track> items;
    }

    [System.Serializable]
    public class PagedArtists
    {
        public string href;
        public int limit;
        public string next;
        public int offset;
        public string previous;
        public int total;
        public List<Artist> items;
    }

    [System.Serializable]
    public class PagedAlbums
    {
        public string href;
        public int limit;
        public string next;
        public int offset;
        public string previous;
        public int total;
        public List<Album> items;
    }

    [System.Serializable]
    public class PagedPlaylists
    {
        public string href;
        public int limit;
        public string next;
        public int offset;
        public string previous;
        public int total;
        public List<Playlist> items;
    }

    [System.Serializable]
    public class PagedShows
    {
        public string href;
        public int limit;
        public string next;
        public int offset;
        public string previous;
        public int total;
        public List<Show> items;
    }

    [System.Serializable]
    public class PagedEpisodes
    {
        public string href;
        public int limit;
        public string next;
        public int offset;
        public string previous;
        public int total;
        public List<Episode> items;
    }

    [System.Serializable]
    public class PagedAudiobooks
    {
        public string href;
        public int limit;
        public string next;
        public int offset;
        public string previous;
        public int total;
        public List<Audiobook> items;
    }

    #endregion

    #region Track and Related Classes

    [System.Serializable]
    public class Track
    {
        public Album album;
        public List<Artist> artists;
        public List<string> available_markets;
        public int disc_number;
        public int duration_ms;
        public bool @explicit;
        public ExternalIds external_ids;
        public ExternalUrls external_urls;
        public string href;
        public string id;
        public bool is_playable;
        public LinkedFrom linked_from;
        public Restrictions restrictions;
        public string name;
        public int popularity;
        public string preview_url;
        public int track_number;
        public string type;
        public string uri;
        public bool is_local;
    }

    [System.Serializable]
    public class ExternalIds
    {
        public string isrc;
        public string ean;
        public string upc;
    }

    [System.Serializable]
    public class ExternalUrls
    {
        public string spotify;
    }

    [System.Serializable]
    public class LinkedFrom
    {
        // This object can be expanded if more fields are needed.
    }

    [System.Serializable]
    public class Restrictions
    {
        public string reason;
    }

    #endregion

    #region Album and Related Classes

    [System.Serializable]
    public class Album
    {
        public string album_type;
        public int total_tracks;
        public List<string> available_markets;
        public ExternalUrls external_urls;
        public string href;
        public string id;
        public List<Image> images;
        public string name;
        public string release_date;
        public string release_date_precision;
        public Restrictions restrictions;
        public string type;
        public string uri;
        public List<Artist> artists;
    }

    [System.Serializable]
    public class Image
    {
        public string url;
        public int height;
        public int width;
    }

    #endregion

    #region Artist Class

    [System.Serializable]
    public class Artist
    {
        public ExternalUrls external_urls;
        public string href;
        public string id;
        public string name;
        public string type;
        public string uri;

        // For detailed artist responses (in the artists section)
        public Followers followers;
        public List<string> genres;
        public List<Image> images;
        public int popularity;
    }

    [System.Serializable]
    public class Followers
    {
        public string href;
        public int total;
    }

    #endregion

    #region Playlist and Related Classes

    [System.Serializable]
    public class Playlist
    {
        public bool collaborative;
        public string description;
        public ExternalUrls external_urls;
        public string href;
        public string id;
        public List<Image> images;
        public string name;
        public Owner owner;
        public bool @public;
        public string snapshot_id;
        public TracksInfo tracks;
        public string type;
        public string uri;
    }

    [System.Serializable]
    public class Owner
    {
        public ExternalUrls external_urls;
        public Followers followers;
        public string href;
        public string id;
        public string type;
        public string uri;
        public string display_name;
    }

    [System.Serializable]
    public class TracksInfo
    {
        public string href;
        public int total;
    }

    #endregion

    #region Show Class

    [System.Serializable]
    public class Show
    {
        public List<string> available_markets;
        public List<Copyright> copyrights;
        public string description;
        public string html_description;
        public bool explicitContent; // renamed from "explicit" because it's a keyword (alternatively use @explicit)
        public ExternalUrls external_urls;
        public string href;
        public string id;
        public List<Image> images;
        public bool is_externally_hosted;
        public List<string> languages;
        public string media_type;
        public string name;
        public string publisher;
        public string type;
        public string uri;
        public int total_episodes;
    }

    [System.Serializable]
    public class Copyright
    {
        public string text;
        public string type;
    }

    #endregion

    #region Episode Class

    [System.Serializable]
    public class Episode
    {
        public string audio_preview_url;
        public string description;
        public string html_description;
        public int duration_ms;
        public bool @explicit;
        public ExternalUrls external_urls;
        public string href;
        public string id;
        public List<Image> images;
        public bool is_externally_hosted;
        public bool is_playable;
        public string language;
        public List<string> languages;
        public string name;
        public string release_date;
        public string release_date_precision;
        public ResumePoint resume_point;
        public string type;
        public string uri;
        public Restrictions restrictions;
    }

    [System.Serializable]
    public class ResumePoint
    {
        public bool fully_played;
        public int resume_position_ms;
    }

    #endregion

    #region Audiobook Class

    [System.Serializable]
    public class Audiobook
    {
        public List<Author> authors;
        public List<string> available_markets;
        public List<Copyright> copyrights;
        public string description;
        public string html_description;
        public string edition;
        public bool @explicit;
        public ExternalUrls external_urls;
        public string href;
        public string id;
        public List<Image> images;
        public List<string> languages;
        public string media_type;
        public string name;
        public List<Narrator> narrators;
        public string publisher;
        public string type;
        public string uri;
        public int total_chapters;
    }

    [System.Serializable]
    public class Author
    {
        public string name;
    }

    [System.Serializable]
    public class Narrator
    {
        public string name;
    }

    #endregion

    #region Anonymous API Response Classes

    [System.Serializable]
    public class SpotifyServerConfig
    {
        public string appName;
        public string market;
        public bool isPremium;
        public string correlationId;
        public bool isAnonymous;
        public long serverTime;
        public string clientVersion;
    }

    [System.Serializable]
    public class ClientTokenResponse
    {
        public string response_type;
        public GrantedToken granted_token;
    }

    [System.Serializable]
    public class GrantedToken
    {
        public string token;
        public int expires_after_seconds;
        public int refresh_after_seconds;
    }

    [System.Serializable]
    public class AnonymousTokenResponse
    {
        public string clientId;
        public string accessToken;
        public long accessTokenExpirationTimestampMs;
        public bool isAnonymous;
    }

    [System.Serializable]
    public class PathfinderSearchResponse
    {
        public PathfinderData data;
    }

    [System.Serializable]
    public class PathfinderData
    {
        public SearchV2 searchV2;
    }

    [System.Serializable]
    public class SearchV2
    {
        public TracksV2 tracksV2;
    }

    [System.Serializable]
    public class TracksV2
    {
        public List<TrackItem> items;
        public int totalCount;
    }

    [System.Serializable]
    public class TrackItem
    {
        public TrackItemWrapper item;
    }

    [System.Serializable]
    public class TrackItemWrapper
    {
        public PathfinderTrackData data;
    }

    [System.Serializable]
    public class PathfinderTrackData
    {
        public string __typename;
        public string id;
        public string name;
        public string uri;
        public PathfinderDuration duration;
        public PathfinderArtists artists;
        public PathfinderAlbum albumOfTrack;
    }

    [System.Serializable]
    public class PathfinderDuration
    {
        public int totalMilliseconds;
    }

    [System.Serializable]
    public class PathfinderArtists
    {
        public List<PathfinderArtistItem> items;
    }

    [System.Serializable]
    public class PathfinderArtistItem
    {
        public string uri;
        public PathfinderProfile profile;
    }

    [System.Serializable]
    public class PathfinderProfile
    {
        public string name;
    }

    [System.Serializable]
    public class PathfinderAlbum
    {
        public string id;
        public string name;
        public string uri;
        public PathfinderCoverArt coverArt;
    }

    [System.Serializable]
    public class PathfinderCoverArt
    {
        public List<PathfinderImageSource> sources;
    }

    [System.Serializable]
    public class PathfinderImageSource
    {
        public string url;
        public int width;
        public int height;
    }

    #endregion
}
