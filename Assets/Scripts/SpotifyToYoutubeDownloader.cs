using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using YoutubeExplode;
using YoutubeExplode.Common;
using YoutubeExplode.Videos.Streams;
using Debug = UnityEngine.Debug;

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
        Debug.Log($"[SpotifyToYoutube] Searching for: '{query}'");

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
        // Sort candidates by duration match, then try them in order, skipping unavailable videos
        var candidates = searchResults
            .OrderBy(v => Math.Abs((v.Duration ?? TimeSpan.Zero).TotalSeconds - spotifyDuration.TotalSeconds))
            .ToList();

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

        // Progress reporter - maps download progress (0-1) to overall progress (0.5-0.9)
        var progress = new Progress<double>(p =>
        {
            double overallProgress = 0.5 + (p * 0.4); // 50% -> 90%
            OnProgress?.Invoke(overallProgress);
            Debug.Log($"[SpotifyToYoutube] Download Progress: {p * 100:F1}%");
        });

        await youtube.Videos.Streams.DownloadAsync(audioStreamInfo, tempFilePath, progress);

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

    private static Task ConvertToMp3(string inputPath, string outputPath)
    {
        return Task.Run(() =>
        {
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
        });
    }
}
