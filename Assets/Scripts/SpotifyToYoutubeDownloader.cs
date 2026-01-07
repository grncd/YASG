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

        // 2. Find the best match (20% -> 40%)
        // Primary: duration match. Secondary (tie-breaker): album name in description
        var candidates = searchResults
            .OrderBy(v => Math.Abs((v.Duration ?? TimeSpan.Zero).TotalSeconds - spotifyDuration.TotalSeconds))
            .ToList();

        YoutubeExplode.Videos.Video bestMatch = null;
        var bestDurationDiff = double.MaxValue;

        // Get the best duration match first
        var topCandidate = candidates.First();
        var topDurationDiff = Math.Abs((topCandidate.Duration ?? TimeSpan.Zero).TotalSeconds - spotifyDuration.TotalSeconds);

        // If album name is provided, check if any candidates with similar duration have the album in description
        if (!string.IsNullOrWhiteSpace(albumName))
        {
            Debug.Log($"[SpotifyToYoutube] Looking for album '{albumName}' in candidates with similar duration...");

            // Only check candidates within 5 seconds of the best duration match
            var similarDurationCandidates = candidates
                .Where(v => Math.Abs((v.Duration ?? TimeSpan.Zero).TotalSeconds - spotifyDuration.TotalSeconds) <= topDurationDiff + 5)
                .ToList();

            int checkedCount = 0;
            foreach (var candidate in similarDurationCandidates)
            {
                var video = await youtube.Videos.GetAsync(candidate.Id);
                var durationDiff = Math.Abs((video.Duration ?? TimeSpan.Zero).TotalSeconds - spotifyDuration.TotalSeconds);
                var hasAlbum = video.Description.Contains(albumName, StringComparison.OrdinalIgnoreCase);

                Debug.Log($"[SpotifyToYoutube] '{candidate.Title}' - Duration diff: {durationDiff:F1}s, Has album: {hasAlbum}");

                // Report progress during matching (20% -> 40%)
                checkedCount++;
                OnProgress?.Invoke(0.2 + (0.2 * checkedCount / similarDurationCandidates.Count));

                // Prefer this if: it has a better duration, OR same duration but has album match
                if (bestMatch == null ||
                    durationDiff < bestDurationDiff ||
                    (Math.Abs(durationDiff - bestDurationDiff) < 1 && hasAlbum))
                {
                    if (hasAlbum || bestMatch == null)
                    {
                        bestMatch = video;
                        bestDurationDiff = durationDiff;
                        if (hasAlbum) break; // Found album match with good duration, stop
                    }
                }
            }
        }

        // If no match found yet (no album provided or no album match), use best duration match
        if (bestMatch == null)
        {
            bestMatch = await youtube.Videos.GetAsync(topCandidate.Id);
        }

        OnProgress?.Invoke(0.4);

        if (bestMatch == null)
        {
            throw new Exception("Could not find a suitable match.");
        }

        Debug.Log($"[SpotifyToYoutube] Best match found: '{bestMatch.Title}' ({bestMatch.Duration}) - URL: {bestMatch.Url}");

        // 3. Get stream manifest (40% -> 50%)
        var streamManifest = await youtube.Videos.Streams.GetManifestAsync(bestMatch.Id);
        OnProgress?.Invoke(0.5);

        // 4. Select best audio stream (highest bitrate)
        var audioStreamInfo = streamManifest.GetAudioOnlyStreams().GetWithHighestBitrate();

        if (audioStreamInfo == null)
        {
            throw new Exception("No audio stream found.");
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
