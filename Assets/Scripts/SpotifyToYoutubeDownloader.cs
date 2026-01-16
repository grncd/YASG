using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using YoutubeExplode;
using YoutubeExplode.Common;
using YoutubeExplode.Videos.Streams;
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

    public static async Task DownloadClosestMatch(string artist, string trackName, TimeSpan spotifyDuration, string outputPath, string albumName = null)
    {
        var youtube = new YoutubeClient();
        var query = $"{artist} - {trackName}";

        // Progress: 0% - Starting search
        OnProgress?.Invoke(0.0);
        Debug.Log($"[SpotifyToYoutube] Searching for: '{query}' (Spotify duration: {spotifyDuration})");

        // 1. Search and get top results (0% -> 20%)
        var searchResults = await youtube.Search.GetVideosAsync(query).CollectAsync(10);
        OnProgress?.Invoke(0.2);

        if (searchResults.Count == 0)
        {
            throw new Exception("No results found on YouTube.");
        }

        // Print all results for debugging
        foreach (var result in searchResults)
        {
            Debug.Log($"[SpotifyToYoutube] Found option: '{result.Title}' ({result.Duration}) - URL: {result.Url}");
        }

        // 2. Find and download the best available match (20% -> 50%)
        // Filter out lyrics videos (usually unofficial with worse quality), unless the track itself has "lyrics" in the name
        bool trackHasLyricsInName = trackName.Contains("lyrics", StringComparison.OrdinalIgnoreCase);
        var filteredResults = searchResults
            .Where(v => trackHasLyricsInName || !v.Title.Contains("lyrics", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // If all results were lyrics videos, fall back to unfiltered list
        if (filteredResults.Count == 0)
        {
            Debug.LogWarning("[SpotifyToYoutube] All results were lyrics videos, using unfiltered list");
            filteredResults = searchResults.ToList();
        }

        // Three-tier candidate selection (prioritized matching):
        // Tier 1: Videos matching "artist - track" or "track - artist" pattern (best match)
        // Tier 2: Videos containing both artist AND track name as separate words
        // Tier 3: All other videos (fallback)
        string pattern1 = $"{artist} - {trackName}";
        string pattern2 = $"{trackName} - {artist}";
        string pattern3 = $"{artist}-{trackName}";
        string pattern4 = $"{trackName}-{artist}";

        var tier1 = filteredResults
            .Where(v => v.Title.Contains(pattern1, StringComparison.OrdinalIgnoreCase) ||
                        v.Title.Contains(pattern2, StringComparison.OrdinalIgnoreCase) ||
                        v.Title.Contains(pattern3, StringComparison.OrdinalIgnoreCase) ||
                        v.Title.Contains(pattern4, StringComparison.OrdinalIgnoreCase))
            .OrderBy(v => Math.Abs((v.Duration ?? TimeSpan.Zero).TotalSeconds - spotifyDuration.TotalSeconds))
            .ToList();

        // For tier 2, check that artist appears as a word boundary (not part of another word)
        var tier2 = filteredResults
            .Where(v => !tier1.Contains(v))
            .Where(v => IsWordMatch(v.Title, artist) && IsWordMatch(v.Title, trackName))
            .OrderBy(v => Math.Abs((v.Duration ?? TimeSpan.Zero).TotalSeconds - spotifyDuration.TotalSeconds))
            .ToList();

        var tier3 = filteredResults
            .Where(v => !tier1.Contains(v) && !tier2.Contains(v))
            .OrderBy(v => Math.Abs((v.Duration ?? TimeSpan.Zero).TotalSeconds - spotifyDuration.TotalSeconds))
            .ToList();

        // Combine: prioritize tier 1, then tier 2, then tier 3
        var candidates = tier1.Concat(tier2).Concat(tier3).ToList();

        if (tier1.Count > 0)
        {
            Debug.Log($"[SpotifyToYoutube] Found {tier1.Count} videos with exact pattern match ('{pattern1}' or similar)");
        }
        else if (tier2.Count > 0)
        {
            Debug.Log($"[SpotifyToYoutube] Found {tier2.Count} videos matching both artist and track name as words");
        }
        else
        {
            Debug.LogWarning($"[SpotifyToYoutube] No good title matches found, using duration-only sorting");
        }

        StreamManifest streamManifest = null;
        IStreamInfo audioStreamInfo = null;
        YoutubeExplode.Videos.Video successfulMatch = null;
        Exception lastException = null;
        int attemptCount = 0;
        int maxAttempts = Math.Min(candidates.Count, 7); // Try up to 7 candidates

        foreach (var candidate in candidates.Take(maxAttempts))
        {
            attemptCount++;
            OnProgress?.Invoke(0.2 + (0.3 * attemptCount / maxAttempts)); // 20% -> 50%

            try
            {
                Debug.Log($"[SpotifyToYoutube] Attempt {attemptCount}/{maxAttempts}: Trying '{candidate.Title}' ({candidate.Id})");

                // Try to get video details (this can fail for unavailable videos)
                var video = await youtube.Videos.GetAsync(candidate.Id);

                // Check album match if album name provided
                if (!string.IsNullOrWhiteSpace(albumName))
                {
                    var hasAlbum = video.Description.Contains(albumName, StringComparison.OrdinalIgnoreCase);
                    var durationDiff = Math.Abs((video.Duration ?? TimeSpan.Zero).TotalSeconds - spotifyDuration.TotalSeconds);
                    Debug.Log($"[SpotifyToYoutube] '{candidate.Title}' - Duration diff: {durationDiff:F1}s, Has album: {hasAlbum}");
                }

                // Try to get stream manifest (this can also fail)
                streamManifest = await youtube.Videos.Streams.GetManifestAsync(candidate.Id);
                audioStreamInfo = streamManifest.GetAudioOnlyStreams().GetWithHighestBitrate();

                if (audioStreamInfo != null)
                {
                    successfulMatch = video;
                    Debug.Log($"[SpotifyToYoutube] Success! Using: '{video.Title}' ({video.Duration})");
                    break;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SpotifyToYoutube] Video '{candidate.Id}' unavailable: {ex.Message}");
                lastException = ex;
                continue; // Try next candidate
            }
        }

        OnProgress?.Invoke(0.5);

        // Check if we found a valid stream
        if (audioStreamInfo == null || successfulMatch == null)
        {
            throw new Exception($"No available video found after trying {attemptCount} candidates. Last error: {lastException?.Message}");
        }

        // 5. Download with progress (50% -> 90%)
        var tempFileName = $"{artist} - {trackName}_temp.{audioStreamInfo.Container}";
        tempFileName = string.Join("_", tempFileName.Split(System.IO.Path.GetInvalidFileNameChars()));
        var tempFilePath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(outputPath), tempFileName);

        Debug.Log($"[SpotifyToYoutube] Downloading to: {tempFilePath}");

        // Progress reporter - uses simple IProgress to avoid SynchronizationContext overhead in builds
        var progress = new SimpleProgress<double>(p =>
        {
            double overallProgress = 0.5 + (p * 0.4); // 50% -> 90%
            OnProgress?.Invoke(overallProgress);
        });

        // Download with retry logic (5 attempts)
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
                    // Wait before retry with exponential backoff
                    await Task.Delay(1000 * attempt);
                }
            }
        }

        if (!downloadSuccess)
        {
            // Clean up temp file if it was partially downloaded
            if (System.IO.File.Exists(tempFilePath))
            {
                try { System.IO.File.Delete(tempFilePath); } catch { }
            }

            throw new Exception($"Download failed after {maxRetries} attempts. Last error: {lastDownloadException?.Message}");
        }

        Debug.Log($"[SpotifyToYoutube] Download Complete! Converting to MP3...");
        OnProgress?.Invoke(0.9);

        // 6. Convert to MP3 using FFmpeg (90% -> 100%)
        await ConvertToMp3(tempFilePath, outputPath);

        // 7. Clean up temp file
        if (System.IO.File.Exists(tempFilePath))
        {
            System.IO.File.Delete(tempFilePath);
        }

        OnProgress?.Invoke(1.0);
        Debug.Log($"[SpotifyToYoutube] Conversion Complete! Saved to: {outputPath}");
    }
    // a

    private static Task ConvertToMp3(string inputPath, string outputPath)
    {
        return Task.Run(() =>
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            // Use ffmpeg-kit on Android - execute() is SYNCHRONOUS and blocks until complete
            try
            {
                // IMPORTANT: We're running in a Task, so we need to attach this thread to the JVM
                AndroidJNI.AttachCurrentThread();
                Debug.Log("[FFmpeg-kit] Thread attached to JVM");

                Debug.Log($"[FFmpeg-kit] Input file exists: {System.IO.File.Exists(inputPath)}");
                if (!System.IO.File.Exists(inputPath))
                {
                    throw new Exception($"Input file does not exist: {inputPath}");
                }
                Debug.Log($"[FFmpeg-kit] Input file size: {new System.IO.FileInfo(inputPath).Length} bytes");
                Debug.Log($"[FFmpeg-kit] Output path: {outputPath}");
                
                string outputDir = System.IO.Path.GetDirectoryName(outputPath);
                Debug.Log($"[FFmpeg-kit] Output directory exists: {System.IO.Directory.Exists(outputDir)}");
                if (!System.IO.Directory.Exists(outputDir))
                {
                    System.IO.Directory.CreateDirectory(outputDir);
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
                    string escapedInput = inputPath.Replace("'", "'\\''");
                    string escapedOutput = outputPath.Replace("'", "'\\''");
                    string cmd = $"-y -i '{escapedInput}' -vn -ar 44100 -ac 2 -b:a 192k '{escapedOutput}'";
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
                        if (System.IO.File.Exists(outputPath))
                        {
                            long fileSize = new System.IO.FileInfo(outputPath).Length;
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
            // Use system ffmpeg on desktop platforms
            string ffmpegPath;
            if (Application.platform == RuntimePlatform.LinuxPlayer || Application.platform == RuntimePlatform.LinuxEditor)
            {
                ffmpegPath = "ffmpeg";
            }
            else
            {
                var dataPath = PlayerPrefs.GetString("dataPath");
                ffmpegPath = System.IO.Path.Combine(dataPath, "vocalremover", "ffmpeg_lib", "ffmpeg.exe");
            }

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


    private static bool IsWordMatch(string text, string word)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(word))
            return false;

        string pattern = $@"(?:^|[\s\-\[\]\(\)\""]){System.Text.RegularExpressions.Regex.Escape(word)}(?:$|[\s\-\[\]\(\)\""])";
        return System.Text.RegularExpressions.Regex.IsMatch(text, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
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
