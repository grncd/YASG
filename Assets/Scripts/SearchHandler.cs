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

public class SearchHandler : MonoBehaviour
{
    private string clientId;
    private string clientSecret;
    private string accessToken;
    public GameObject trackGUI;
    public Transform trackParent;
    public LevelResourcesCompiler LRC;
    public AlertManager alertManager;
    public GameObject selectorMenu;

    void Start()
    {
        clientId = PlayerPrefs.GetString("CLIENTID");
        clientSecret = PlayerPrefs.GetString("APIKEY");
        StartCoroutine(TokenAndRequestRoutine());
    }

    public void Search(string search)
    {
        foreach (Transform child in trackParent.transform)
        {
            Destroy(child.gameObject);
        }
        if (!string.IsNullOrEmpty(search))
        {
            StartCoroutine(SearchRoutine(search));
        }
    }

    IEnumerator TokenAndRequestRoutine()
    {
        while (true)
        {
            // Get a new access token.
            yield return StartCoroutine(GetAccessToken());

            // Use the token to perform the search request

            // Wait for 1 hour (3600 seconds) before repeating.
            yield return new WaitForSeconds(3600);
        }
    }

    IEnumerator SearchRoutine(string search)
    {
        yield return StartCoroutine(GetSpotifyAlbum(search));
    }

    string GenerateSpotifySearchUrl(string query, string artist = "", string album = "", string type = "track")
    {
        string baseUrl = "https://api.spotify.com/v1/search?q=";
        string formattedQuery = HttpUtility.UrlEncode(query); // Encode the main query

        if (!string.IsNullOrEmpty(artist))
        {
            formattedQuery += "%20artist%3A" + HttpUtility.UrlEncode(artist); // Append artist filter
        }

        if (!string.IsNullOrEmpty(album))
        {
            formattedQuery += "%20album%3A" + HttpUtility.UrlEncode(album); // Append album filter
        }

        string finalUrl = baseUrl + formattedQuery + "&type=" + HttpUtility.UrlEncode(type);
        return finalUrl;
    }

    public void DisplayFavoriteSongs()
    {
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
                        button.onClick.AddListener(delegate {
                            // Use currentFavSong.length for the formatted duration string,
                            // and currentAdaptedTrack for the full (adapted) Track object.
                            LRC.PreCompile(currentFavSong.url, currentFavSong.name, currentFavSong.artist, currentFavSong.length, currentAdaptedTrack);
                        });
                    }
                    else
                    {
                        button.onClick.AddListener(delegate {
                            // LRC.PreCompile will receive the original source URL (e.g., Spotify URL)
                            // for the 'urlToPlay' parameter.
                            EditorManager.Instance.StartEditing(currentFavSong.name, currentFavSong.artist, currentAdaptedTrack.album.name, currentAdaptedTrack.duration_ms, currentFavSong.url);
                        });
                    }
                }
                else
                {
                    button.onClick.AddListener(delegate {
                        // 1. Create a new SongData struct with the selected song's info.
                        SongData newSong = new SongData(
                            currentFavSong.name,
                            currentFavSong.artist,
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
            temp.transform.GetChild(4).GetComponent<TextMeshProUGUI>().text = "Song Length: " + favSong.length;

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

                if(PlayerPrefs.GetInt("multiplayer") == 0)
                {
                    if(PlayerPrefs.GetInt("editing") == 0)
                    {
                        button.onClick.AddListener(delegate {
                            // LRC.PreCompile will receive the original source URL (e.g., Spotify URL)
                            // for the 'urlToPlay' parameter.
                            LRC.PreCompile(currentSong.url, currentSong.name, currentSong.artist, currentSong.length, currentAdaptedTrack);
                        });
                    }
                    else
                    {
                        button.onClick.AddListener(delegate {
                            // LRC.PreCompile will receive the original source URL (e.g., Spotify URL)
                            // for the 'urlToPlay' parameter.
                            EditorManager.Instance.StartEditing(currentSong.name, currentSong.artist, currentAdaptedTrack.album.name, currentAdaptedTrack.duration_ms, currentSong.url);
                        });
                    }
                }
                else
                {
                    button.onClick.AddListener(delegate {
                        // 1. Create a new SongData struct with the selected song's info.
                        SongData newSong = new SongData(
                            currentSong.name,
                            currentSong.artist,
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
            temp.transform.GetChild(4).GetComponent<TextMeshProUGUI>().text = "Song Length: " + downloadedSong.length;

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

    IEnumerator GetAccessToken()
    {
        string url = "https://accounts.spotify.com/api/token";

        WWWForm form = new WWWForm();
        form.AddField("grant_type", "client_credentials");
        form.AddField("client_id", clientId);
        form.AddField("client_secret", clientSecret);

        using (UnityWebRequest request = UnityWebRequest.Post(url, form))
        {
            request.SetRequestHeader("Content-Type", "application/x-www-form-urlencoded");
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                Debug.Log("Token response: " + request.downloadHandler.text);

                TokenResponse tokenResponse = JsonUtility.FromJson<TokenResponse>(request.downloadHandler.text);
                accessToken = tokenResponse.access_token;
            }
            else
            {
                Debug.LogError("Token Error: " + request.error);
                
            }
        }
    }

    IEnumerator GetSpotifyAlbum(string search)
    {
        if(accessToken == null)
        {
            AlertManager.Instance.ShowError("Failed to retrieve access token.", "This likely happened due to connectivity issues. Please check your connection and try again.", "Dismiss");
            StopCoroutine(GetSpotifyAlbum(search));
        }
        string url = GenerateSpotifySearchUrl(search);

        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            request.SetRequestHeader("Authorization", "Bearer " + accessToken);
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                string json = request.downloadHandler.text;
                Debug.Log("Spotify API response: " + json);

                // Deserialize the JSON into the SearchResponse object.
                SearchResponse searchResponse = JsonUtility.FromJson<SearchResponse>(json);

                // Check if the albums section exists and iterate over each album.
                if (searchResponse.tracks != null && searchResponse.tracks.items != null)
                {
                    foreach (Track track in searchResponse.tracks.items)
                    {
                        GameObject temp = Instantiate(trackGUI, trackParent);
                        if (PlayerPrefs.GetInt("multiplayer") == 0)
                        {
                            if (PlayerPrefs.GetInt("editing") == 0)
                            {
                                temp.GetComponent<UnityEngine.UI.Button>().onClick.AddListener(delegate {
                                    LRC.PreCompile(track.external_urls.spotify, track.name, track.artists[0].name, ConvertDuration(track.duration_ms), track);
                                });
                            }
                            else
                            {
                                temp.GetComponent<UnityEngine.UI.Button>().onClick.AddListener(delegate {
                                    EditorManager.Instance.StartEditing(track.name, track.artists[0].name, track.album.name, track.duration_ms, track.external_urls.spotify);
                                });
                            }
                        }
                        else
                        {
                            temp.GetComponent<UnityEngine.UI.Button>().onClick.AddListener(delegate {
                                // 1. Create a new SongData struct with the selected song's info.
                                SongData newSong = new SongData(
                                    track.name,
                                    track.artists[0].name,
                                    ConvertDuration(track.duration_ms),
                                    track.album.images[0].url, // Example URL
                                    track.external_urls.spotify
                                );

                                // 2. Check if we are the host.
                                if (PlayerData.LocalPlayerInstance != null && PlayerData.LocalPlayerInstance.IsHost.Value)
                                {
                                    // --- NEW: Cache the full track object before sending the RPC ---
                                    LevelResourcesCompiler.lastPrecompiledTrack = track;

                                    // 3. Call the RPC on our own player object to request the change.
                                    PlayerData.LocalPlayerInstance.RequestChangeSong_ServerRpc(newSong);
                                }
                                selectorMenu.SetActive(false);
                            });

                        }
                        temp.transform.GetChild(1).GetComponent<TextMeshProUGUI>().text = track.name;
                        temp.transform.GetChild(2).GetComponent<TextMeshProUGUI>().text = track.artists[0].name;
                        temp.transform.GetChild(5).GetComponent<TextMeshProUGUI>().text = track.external_urls.spotify;
                        temp.transform.GetChild(4).GetComponent<TextMeshProUGUI>().text = "Song Length: " + ConvertDuration(track.duration_ms);
                        SetAlbumCoverFromTrack(track, temp.transform.GetChild(3).GetComponent<MPImage>(), temp.transform.GetChild(0).GetComponent<MPImage>());
                    }
                }
                else
                {
                    Debug.Log("No albums found in the response.");
                }
            }
            else
            {
                Debug.LogError("Spotify API Error: " + request.error);
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
        StartCoroutine(DownloadAlbumCover(highestResImage.url,image,image2));
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

    // Classes to parse the token response
    [System.Serializable]
    public class TokenResponse
    {
        public string access_token;
        public string token_type;
        public int expires_in;
    }

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
}
