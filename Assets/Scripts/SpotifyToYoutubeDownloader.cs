using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using YoutubeExplode;
using YoutubeExplode.Videos.Streams;
using YouTubeMusicAPI.Client;
using YouTubeMusicAPI.Models.Search;
using YouTubeMusicAPI.Pagination;
using System.IO;
using System.Text.RegularExpressions;
using Debug = UnityEngine.Debug;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

public static class SpotifyToYoutubeDownloader
{
    /// <summary>
    /// Progress event reporting overall progress (0.0 to 1.0).
    /// 0.0-0.5 = Searching + Matching
    /// 0.5-1.0 = Downloading + Converting
    /// </summary>
    public static event Action<double> OnProgress;

    // Timeout settings
    private static readonly TimeSpan SearchTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan VideoFetchTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan ManifestFetchTimeout = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Runs an async task with a timeout. Throws TimeoutException if the task doesn't complete in time.
    /// </summary>
    private static async Task<T> WithTimeout<T>(Task<T> task, TimeSpan timeout, string operationName)
    {
        using var cts = new CancellationTokenSource();
        var delayTask = Task.Delay(timeout, cts.Token);
        var completedTask = await Task.WhenAny(task, delayTask);

        if (completedTask == delayTask)
        {
            throw new TimeoutException($"{operationName} timed out after {timeout.TotalSeconds} seconds. This may be due to network issues or YouTube rate limiting. Please try again.");
        }

        cts.Cancel(); // Cancel the delay task
        return await task; // Get the result or propagate any exception
    }

    public static async Task DownloadClosestMatch(string artist, string trackName, TimeSpan spotifyDuration, string outputPath, string albumName = null)
    {
        var query = $"{artist} - {trackName}";

        // Progress: 0% - Starting search
        OnProgress?.Invoke(0.0);
        Debug.Log($"[SpotifyToYoutube] Searching YouTube Music for: '{query}' (Spotify duration: {spotifyDuration})");

        // 1. Search YouTube Music for songs (0% -> 20%) - with timeout
        IReadOnlyList<SongSearchResult> searchResults;
        try
        {
            var ytMusic = new YouTubeMusicClient();
            var allResults = await WithTimeout(
                ytMusic.SearchAsync(query, SearchCategory.Songs).FetchItemsAsync(0, 10),
                SearchTimeout,
                "YouTube Music search"
            );
            searchResults = allResults.OfType<SongSearchResult>().ToList();
        }
        catch (TimeoutException)
        {
            throw new Exception($"YouTube Music search timed out after {SearchTimeout.TotalSeconds} seconds. Please check your internet connection and try again.");
        }
        OnProgress?.Invoke(0.2);

        if (searchResults.Count == 0)
        {
            throw new Exception("No results found on YouTube Music.");
        }

        // Print all results for debugging
        foreach (var result in searchResults)
        {
            var artists = string.Join(", ", result.Artists.Select(a => a.Name));
            Debug.Log($"[SpotifyToYoutube] Found option: '{result.Name}' by {artists} ({result.Duration}) - ID: {result.Id}");
        }

        // 2. Find and download the best available match (20% -> 50%)
        // Weighted scoring system: combines title relevance and duration matching
        const double titleWeight = 0.55;
        const double durationWeight = 0.45;

        var scoredCandidates = searchResults
            .Select(v =>
            {
                double titleScore = CalculateTitleScore(v.Name, artist, trackName);
                double durationScore = CalculateDurationScore(v.Duration, spotifyDuration);
                double combinedScore = (titleScore * titleWeight) + (durationScore * durationWeight);
                return new { Song = v, TitleScore = titleScore, DurationScore = durationScore, CombinedScore = combinedScore };
            })
            .OrderByDescending(x => x.CombinedScore)
            .ToList();

        // Log top candidates with their scores for debugging
        Debug.Log($"[SpotifyToYoutube] Scored candidates (title weight: {titleWeight:P0}, duration weight: {durationWeight:P0}):");
        foreach (var scored in scoredCandidates.Take(5))
        {
            var durationDiff = Math.Abs(scored.Song.Duration.TotalSeconds - spotifyDuration.TotalSeconds);
            Debug.Log($"  [{scored.CombinedScore:F1}] '{scored.Song.Name}' (T:{scored.TitleScore:F0} D:{scored.DurationScore:F0} diff:{durationDiff:F1}s)");
        }

        var candidates = scoredCandidates
            .Select(x => new VideoCandidate(x.Song.Id, x.Song.Name, x.Song.Duration))
            .ToList();

        int downloadMethod = SettingsManager.Instance.GetSetting<int>("DownloadMethod", 0);
        bool useYtDlp = downloadMethod == 0;

        if (useYtDlp)
        {
            await DownloadWithYtDlp(candidates, outputPath);
        }
        else
        {
            await DownloadWithYoutubeExplode(new YoutubeClient(), candidates, outputPath, artist, trackName, albumName, spotifyDuration);
        }
    }
    private static async Task DownloadWithYtDlp(List<VideoCandidate> candidates, string outputPath)
    {
        var dataPath = PlayerPrefs.GetString("dataPath");
        bool isWindows = Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.WindowsEditor;
        string ytDlpPath;
        if (isWindows)
        {
            ytDlpPath = Path.Combine(dataPath, "yt-dlp.exe");
        }
        else
        {
            ytDlpPath = Path.Combine(dataPath, "yt-dlp");
        }
        string ffmpegLocationArg = isWindows
            ? $"--ffmpeg-location \"{Path.Combine(dataPath, "vocalremover", "ffmpeg_lib")}\" "
            : "";

        Exception lastException = null;
        int attemptCount = 0;
        int maxAttempts = Math.Min(candidates.Count, 7);

        foreach (var candidate in candidates.Take(maxAttempts))
        {
            attemptCount++;
            OnProgress?.Invoke(0.2 + (0.7 * attemptCount / maxAttempts));

            try
            {
                Debug.Log($"[SpotifyToYoutube] yt-dlp attempt {attemptCount}/{maxAttempts}: '{candidate.Title}' ({candidate.Id})");

                var url = $"https://youtube.com/watch?v={candidate.Id}";

                await Task.Run(() =>
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = ytDlpPath,
                        Arguments = $"{ffmpegLocationArg}-x --audio-format mp3 --audio-quality 192k --newline -o \"{outputPath}\" \"{url}\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };

                    using (var process = Process.Start(startInfo))
                    {
                        process.OutputDataReceived += (sender, args) =>
                        {
                            if (string.IsNullOrEmpty(args.Data)) return;
                            Debug.Log($"[yt-dlp] {args.Data}");

                            // Parse progress: [download] XX.X%
                            var match = Regex.Match(args.Data, @"\[download\]\s+([\d.]+)%");
                            if (match.Success && double.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double pct))
                            {
                                double overallProgress = 0.5 + (pct / 100.0 * 0.5);
                                OnProgress?.Invoke(overallProgress);
                            }
                        };
                        process.ErrorDataReceived += (sender, args) =>
                        {
                            if (string.IsNullOrEmpty(args.Data)) return;
                            Debug.Log($"[yt-dlp] {args.Data}");

                            // Also parse progress from stderr in case yt-dlp version outputs it here
                            var match = Regex.Match(args.Data, @"\[download\]\s+([\d.]+)%");
                            if (match.Success && double.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double pct))
                            {
                                double overallProgress = 0.5 + (pct / 100.0 * 0.5);
                                OnProgress?.Invoke(overallProgress);
                            }
                        };

                        process.BeginOutputReadLine();
                        process.BeginErrorReadLine();
                        process.WaitForExit();

                        if (process.ExitCode != 0)
                        {
                            throw new Exception($"yt-dlp exited with code {process.ExitCode}");
                        }
                    }
                });

                // yt-dlp succeeded
                if (File.Exists(outputPath))
                {
                    OnProgress?.Invoke(1.0);
                    Debug.Log($"[SpotifyToYoutube] yt-dlp download complete: {outputPath}");
                    return;
                }
                else
                {
                    throw new Exception("yt-dlp reported success but output file not found");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SpotifyToYoutube] yt-dlp failed for '{candidate.Id}': {ex.Message}");
                lastException = ex;
                continue;
            }
        }

        throw new Exception($"yt-dlp: No available video found after trying {attemptCount} candidates. Last error: {lastException?.Message}");
    }

    private static async Task DownloadWithYoutubeExplode(YoutubeClient youtube, List<VideoCandidate> candidates, string outputPath, string artist, string trackName, string albumName, TimeSpan spotifyDuration)
    {
        StreamManifest streamManifest = null;
        IStreamInfo audioStreamInfo = null;
        YoutubeExplode.Videos.Video successfulMatch = null;
        Exception lastException = null;
        int attemptCount = 0;
        int maxAttempts = Math.Min(candidates.Count, 7);

        foreach (var candidate in candidates.Take(maxAttempts))
        {
            attemptCount++;
            OnProgress?.Invoke(0.2 + (0.3 * attemptCount / maxAttempts));

            try
            {
                Debug.Log($"[SpotifyToYoutube] Attempt {attemptCount}/{maxAttempts}: Trying '{candidate.Title}' ({candidate.Id})");

                YoutubeExplode.Videos.Video video;
                try
                {
                    video = await WithTimeout(
                        youtube.Videos.GetAsync(candidate.Id).AsTask(),
                        VideoFetchTimeout,
                        "Video fetch"
                    );
                }
                catch (TimeoutException)
                {
                    throw new Exception($"YouTube video fetch timed out after {VideoFetchTimeout.TotalSeconds} seconds. This may indicate network issues or YouTube rate limiting. Please try again later.");
                }

                if (!string.IsNullOrWhiteSpace(albumName))
                {
                    var hasAlbum = video.Description.Contains(albumName, StringComparison.OrdinalIgnoreCase);
                    var durationDiff = Math.Abs((video.Duration ?? TimeSpan.Zero).TotalSeconds - spotifyDuration.TotalSeconds);
                    Debug.Log($"[SpotifyToYoutube] '{candidate.Title}' - Duration diff: {durationDiff:F1}s, Has album: {hasAlbum}");
                }

                try
                {
                    streamManifest = await WithTimeout(
                        youtube.Videos.Streams.GetManifestAsync(candidate.Id).AsTask(),
                        ManifestFetchTimeout,
                        "Stream manifest fetch"
                    );
                }
                catch (TimeoutException)
                {
                    throw new Exception($"YouTube stream manifest fetch timed out after {ManifestFetchTimeout.TotalSeconds} seconds. This may indicate network issues or YouTube rate limiting. Please try again later.");
                }

                audioStreamInfo = streamManifest.GetAudioOnlyStreams().GetWithHighestBitrate();

                if (audioStreamInfo != null)
                {
                    successfulMatch = video;
                    Debug.Log($"[SpotifyToYoutube] Success! Using: '{video.Title}' ({video.Duration})");
                    break;
                }
            }
            catch (Exception ex) when (ex is not TimeoutException && !ex.Message.Contains("timed out"))
            {
                Debug.LogWarning($"[SpotifyToYoutube] Video '{candidate.Id}' unavailable: {ex.Message}");
                lastException = ex;
                continue;
            }
        }

        OnProgress?.Invoke(0.5);

        if (audioStreamInfo == null || successfulMatch == null)
        {
            throw new Exception($"No available video found after trying {attemptCount} candidates. Last error: {lastException?.Message}");
        }

        // Download with progress (50% -> 90%)
        var tempFileName = $"{artist} - {trackName}_temp.{audioStreamInfo.Container}";
        tempFileName = string.Join("_", tempFileName.Split(Path.GetInvalidFileNameChars()));
        var tempFilePath = Path.Combine(Path.GetDirectoryName(outputPath), tempFileName);

        Debug.Log($"[SpotifyToYoutube] Downloading to: {tempFilePath}");

        var progress = new SimpleProgress<double>(p =>
        {
            double overallProgress = 0.5 + (p * 0.4);
            OnProgress?.Invoke(overallProgress);
        });

        int maxRetries = 5;
        Exception lastDownloadException = null;
        bool downloadSuccess = false;

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                if (attempt > 1)
                {
                    Debug.Log($"[SpotifyToYoutube] Retry attempt {attempt}/{maxRetries} for download...");
                }

                await youtube.Videos.Streams.DownloadAsync(audioStreamInfo, tempFilePath, progress);
                downloadSuccess = true;
                break;
            }
            catch (Exception ex)
            {
                lastDownloadException = ex;
                Debug.LogWarning($"[SpotifyToYoutube] Download attempt {attempt}/{maxRetries} failed: {ex.Message}");

                if (attempt < maxRetries)
                {
                    await Task.Delay(1000 * attempt);
                }
            }
        }

        if (!downloadSuccess)
        {
            if (File.Exists(tempFilePath))
            {
                try { File.Delete(tempFilePath); } catch { }
            }

            throw new Exception($"Download failed after {maxRetries} attempts. Last error: {lastDownloadException?.Message}");
        }

        Debug.Log($"[SpotifyToYoutube] Download Complete! Converting to MP3...");
        OnProgress?.Invoke(0.9);

        await ConvertToMp3(tempFilePath, outputPath);

        if (File.Exists(tempFilePath))
        {
            File.Delete(tempFilePath);
        }

        OnProgress?.Invoke(1.0);
        Debug.Log($"[SpotifyToYoutube] Conversion Complete! Saved to: {outputPath}");
    }

    private static Task ConvertToMp3(string inputPath, string outputPath)
    {
        // Get ffmpeg path on main thread before entering Task.Run (PlayerPrefs requires main thread)
#if !UNITY_ANDROID || UNITY_EDITOR
        string ffmpegPath;
        if (Application.platform == RuntimePlatform.LinuxPlayer || Application.platform == RuntimePlatform.LinuxEditor)
        {
            ffmpegPath = "ffmpeg";
        }
        else
        {
            var dataPath = PlayerPrefs.GetString("dataPath");
            ffmpegPath = Path.Combine(dataPath, "vocalremover", "ffmpeg_lib", "ffmpeg.exe");
        }
#endif

        return Task.Run(() =>
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            // Use ffmpeg-kit on Android - execute() is SYNCHRONOUS and blocks until complete
            try
            {
                // IMPORTANT: We're running in a Task, so we need to attach this thread to the JVM
                AndroidJNI.AttachCurrentThread();
                Debug.Log("[FFmpeg-kit] Thread attached to JVM");

                Debug.Log($"[FFmpeg-kit] Input file exists: {File.Exists(inputPath)}");
                if (!File.Exists(inputPath))
                {
                    throw new Exception($"Input file does not exist: {inputPath}");
                }
                Debug.Log($"[FFmpeg-kit] Input file size: {new FileInfo(inputPath).Length} bytes");
                Debug.Log($"[FFmpeg-kit] Output path: {outputPath}");
                
                string outputDir = Path.GetDirectoryName(outputPath);
                Debug.Log($"[FFmpeg-kit] Output directory exists: {Directory.Exists(outputDir)}");
                if (!Directory.Exists(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                    Debug.Log($"[FFmpeg-kit] Created output directory: {outputDir}");
                }

                // First, let's try to get the version to trigger native library loading
                Debug.Log("[FFmpeg-kit] Attempting to load native libraries via FFmpegKitConfig...");
                try
                {
                    using (AndroidJavaClass ffmpegConfig = new AndroidJavaClass("com.arthenica.ffmpegkit.FFmpegKitConfig"))
                    {
                        // This call should trigger the native library loading
                        string version = ffmpegConfig.CallStatic<string>("getVersion");
                        Debug.Log($"[FFmpeg-kit] FFmpegKit version: {version}");
                        
                        string ffmpegVersion = ffmpegConfig.CallStatic<string>("getFFmpegVersion");
                        Debug.Log($"[FFmpeg-kit] FFmpeg version: {ffmpegVersion}");
                        
                        // Now ignore SIGXCPU signal
                        Debug.Log("[FFmpeg-kit] Ignoring SIGXCPU signal...");
                        using (AndroidJavaClass signalClass = new AndroidJavaClass("com.arthenica.ffmpegkit.Signal"))
                        {
                            AndroidJavaObject sigxcpu = signalClass.CallStatic<AndroidJavaObject>("valueOf", "SIGXCPU");
                            if (sigxcpu != null)
                            {
                                ffmpegConfig.CallStatic("ignoreSignal", sigxcpu);
                                Debug.Log("[FFmpeg-kit] SIGXCPU signal ignored");
                            }
                            else
                            {
                                Debug.LogWarning("[FFmpeg-kit] Could not get SIGXCPU signal enum");
                            }
                        }
                    }
                }
                catch (Exception configEx)
                {
                    Debug.LogError($"[FFmpeg-kit] Failed to access FFmpegKitConfig: {configEx.Message}\n{configEx.StackTrace}");
                    throw;
                }

                using (AndroidJavaClass ffmpeg = new AndroidJavaClass("com.arthenica.ffmpegkit.FFmpegKit"))
                {
                    // Build the conversion command - use single quotes for paths on Android
                    // Escape single quotes in paths by replacing ' with '\''
                    string escapedInput = inputPath.Replace("\"", "\\\"");
                    string escapedOutput = outputPath.Replace("\"", "\\\"");
                    string cmd = $"-y -i \"{escapedInput}\" -vn -ar 44100 -ac 2 -b:a 192k \"{escapedOutput}\"";
                    Debug.Log($"[FFmpeg-kit] Executing conversion command...");
                    Debug.Log($"[FFmpeg-kit] Command: {cmd}");

                    // execute() is SYNCHRONOUS - it will block until FFmpeg completes
                    AndroidJavaObject session = ffmpeg.CallStatic<AndroidJavaObject>("execute", cmd);
                    
                    if (session == null)
                    {
                        // Try to get more info about why it failed
                        Debug.LogError("[FFmpeg-kit] Session is null! Trying to get last session...");
                        try
                        {
                            using (AndroidJavaClass config = new AndroidJavaClass("com.arthenica.ffmpegkit.FFmpegKitConfig"))
                            {
                                AndroidJavaObject lastSession = config.CallStatic<AndroidJavaObject>("getLastSession");
                                if (lastSession != null)
                                {
                                    Debug.Log("[FFmpeg-kit] Found last session via config");
                                    session = lastSession;
                                }
                                else
                                {
                                    Debug.LogError("[FFmpeg-kit] getLastSession also returned null");
                                }
                            }
                        }
                        catch (Exception lastEx)
                        {
                            Debug.LogError($"[FFmpeg-kit] Failed to get last session: {lastEx.Message}");
                        }
                        
                        if (session == null)
                        {
                            throw new Exception("FFmpeg-kit returned null session. Command may have failed to start.");
                        }
                    }

                    // Get the return code
                    AndroidJavaObject rc = session.Call<AndroidJavaObject>("getReturnCode");
                    if (rc == null)
                    {
                        // Session might still be running somehow, check state
                        AndroidJavaObject state = session.Call<AndroidJavaObject>("getState");
                        string stateStr = state?.Call<string>("toString") ?? "UNKNOWN";
                        string logs = session.Call<string>("getAllLogsAsString") ?? "No logs";
                        throw new Exception($"FFmpeg-kit session has no return code. State: {stateStr}. Logs: {logs}");
                    }
                    
                    int returnCode = rc.Call<int>("getValue");
                    Debug.Log($"[FFmpeg-kit] Conversion finished with return code: {returnCode}");

                    // Get logs for debugging
                    string allLogs = session.Call<string>("getAllLogsAsString") ?? "No logs available";
                    Debug.Log($"[FFmpeg-kit] Logs: {allLogs}");

                    if (returnCode == 0)
                    {
                        // Success - verify output file exists
                        if (File.Exists(outputPath))
                        {
                            long fileSize = new FileInfo(outputPath).Length;
                            Debug.Log($"[FFmpeg-kit] Success! Output file created: {fileSize} bytes");
                            session.Dispose();
                            return;
                        }
                        else
                        {
                            session.Dispose();
                            throw new Exception($"FFmpeg reported success but output file not found at: {outputPath}");
                        }
                    }
                    else
                    {
                        session.Dispose();
                        throw new Exception($"FFmpeg conversion failed with return code {returnCode}. Logs: {allLogs}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FFmpeg-kit] Exception: {ex.Message}\n{ex.StackTrace}");
                throw new Exception($"FFmpeg-kit conversion failed: {ex.Message}");
            }
#else
            // Use system ffmpeg on desktop platforms (ffmpegPath was computed on main thread above)
            var startInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = $"-y -i \"{inputPath}\" -vn -ar 44100 -ac 2 -b:a 192k \"{outputPath}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using (var process = Process.Start(startInfo))
            {
                process.WaitForExit();
                if (process.ExitCode != 0)
                {
                    var error = process.StandardError.ReadToEnd();
                    throw new Exception($"FFmpeg conversion failed: {error}");
                }
            }
#endif
        });
    }


    /// <summary>
    /// Calculates a title relevance score (0-100) based on how well the video title matches the artist and track.
    /// </summary>
    private static double CalculateTitleScore(string title, string artist, string trackName)
    {
        if (string.IsNullOrEmpty(title))
            return 0;

        // Check for exact pattern matches (highest confidence)
        string[] exactPatterns = {
            $"{artist} - {trackName}",
            $"{trackName} - {artist}",
            $"{artist}-{trackName}",
            $"{trackName}-{artist}"
        };

        foreach (var pattern in exactPatterns)
        {
            if (title.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                return 100;
        }

        // Check if both artist and track appear as word boundaries
        bool hasArtist = IsWordMatch(title, artist);
        bool hasTrack = IsWordMatch(title, trackName);

        if (hasArtist && hasTrack)
            return 80;

        // Track name only (common for official uploads with simple titles)
        if (hasTrack)
            return 60;

        // Partial/substring matches (less reliable)
        bool containsArtist = title.Contains(artist, StringComparison.OrdinalIgnoreCase);
        bool containsTrack = title.Contains(trackName, StringComparison.OrdinalIgnoreCase);

        if (containsArtist && containsTrack)
            return 50;

        if (containsTrack)
            return 35;

        if (containsArtist)
            return 20;

        // No meaningful match
        return 5;
    }

    /// <summary>
    /// Calculates a duration match score (0-100) based on how close the video duration is to the Spotify track.
    /// Uses exponential decay with a harsh penalty after a threshold.
    /// </summary>
    private static double CalculateDurationScore(TimeSpan videoDuration, TimeSpan spotifyDuration)
    {
        double diffSeconds = Math.Abs(videoDuration.TotalSeconds - spotifyDuration.TotalSeconds);

        // Perfect or near-perfect match (within 1 second)
        if (diffSeconds <= 1)
            return 100;

        // Gentle decay for small differences (1-3 seconds) - likely same version
        if (diffSeconds <= 3)
            return 100 - (diffSeconds * 5); // 95-85

        // Moderate decay for medium differences (3-7 seconds) - possibly different version/intro
        if (diffSeconds <= 7)
            return 85 - ((diffSeconds - 3) * 8); // 85-53

        // Harsh decay for large differences (7-15 seconds) - likely wrong version or has intro/outro
        if (diffSeconds <= 15)
            return 53 - ((diffSeconds - 7) * 5); // 53-13

        // Very large differences (>15 seconds) - probably wrong song or extended mix
        return Math.Max(0, 13 - ((diffSeconds - 15) * 1));
    }

    private static bool IsWordMatch(string text, string word)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(word))
            return false;

        string pattern = $@"(?:^|[\s\-\[\]\(\)\""]){Regex.Escape(word)}(?:$|[\s\-\[\]\(\)\""])";
        return Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase);
    }
}

public class VideoCandidate
{
    public string Id { get; }
    public string Title { get; }
    public TimeSpan Duration { get; }

    public VideoCandidate(string id, string title, TimeSpan duration)
    {
        Id = id;
        Title = title;
        Duration = duration;
    }
}

/// <summary>
/// Simple IProgress implementation that doesn't use SynchronizationContext.
/// Progress<T> captures the sync context and marshals callbacks to it, which can cause
/// blocking/stuttering in Unity builds where the context behaves differently than in the editor.
/// </summary>
public class SimpleProgress<T> : IProgress<T>
{
    private readonly Action<T> _handler;

    public SimpleProgress(Action<T> handler)
    {
        _handler = handler;
    }

    public void Report(T value)
    {
        _handler?.Invoke(value);
    }
}
