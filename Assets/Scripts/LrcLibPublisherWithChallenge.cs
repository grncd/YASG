using UnityEngine;
using UnityEngine.Networking;
using System;
using System.Text;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.IO;
using System.Linq;
using Unity.Jobs;
using Unity.Collections;
using System.Collections.Generic; // Added for List support
using Debug = UnityEngine.Debug;

/// <summary>
/// A component to publish lyrics to lrclib.net, including solving the required proof-of-work challenge.
/// This version is optimized for simplicity and performance by using the Unity Job System without progress reporting.
/// </summary>
public class LrcLibPublisherWithChallenge : MonoBehaviour
{
    // === PRIVATE JOB-RELATED STATE ===
    private bool isSolving = false;
    private JobHandle challengeJobHandle;
    private LyricsData lyricsToPublish;
    private string currentChallengePrefix;

    // Native Collections used by the job. Must be managed and disposed.
    private NativeArray<long> resultNonce;
    private NativeArray<byte> jobPrefixBytes;
    private NativeArray<byte> jobTargetBytes;

    // === DATA STRUCTURES ===
    [Serializable] private class ChallengeResponse { public string prefix; public string target; }
    [Serializable] private class LyricsData { public string trackName; public string artistName; public string albumName; public float duration; public string plainLyrics; public string syncedLyrics; }

    // --- NEW: Helper classes for parsing corr.json ---
    [Serializable]
    private class CorrespondenceEntry { public string key; public string value; }

    [Serializable]
    private class CorrespondenceList { public List<CorrespondenceEntry> correspondences; }

    // === JOB DEFINITION ===
    // This job is designed to be lean. It takes inputs, finds a nonce, and sets the result.
    private struct SolveChallengeJob : IJob
    {
        [ReadOnly] public NativeArray<byte> PrefixBytes;
        [ReadOnly] public NativeArray<byte> TargetBytes;
        public NativeArray<long> ResultNonce;

        public void Execute()
        {
            ResultNonce[0] = -1; // Default to a failure state
            using (var sha256 = SHA256.Create())
            {
                string prefix = Encoding.UTF8.GetString(PrefixBytes.ToArray());
                for (long nonce = 0; nonce < long.MaxValue; ++nonce)
                {
                    string input = prefix + nonce;
                    byte[] inputBytes = Encoding.UTF8.GetBytes(input);
                    byte[] hashBytes = sha256.ComputeHash(inputBytes);

                    if (VerifyNonce(hashBytes, TargetBytes))
                    {
                        ResultNonce[0] = nonce; // Success!
                        break; // Exit the loop immediately
                    }
                }
            }
        }

        private bool VerifyNonce(byte[] hash, NativeArray<byte> target)
        {
            for (int i = 0; i < hash.Length; i++)
            {
                if (hash[i] > target[i]) return false;
                if (hash[i] < target[i]) return true;
            }
            return true;
        }
    }

    // === PUBLIC ORCHESTRATION METHOD ===
    public async void PublishWithChallenge(string trackName, string artistName, string albumName, float duration, string trackUrl)
    {
        if (isSolving)
        {
            Debug.LogWarning("Already solving a challenge. Please wait.");
            return;
        }



        // Load Lyrics
        string dataPath = PlayerPrefs.GetString("dataPath");
        string workingLyricsPath = Path.Combine(dataPath, "workingLyrics");
        string trackId = trackUrl.Split('/').Last();
        string plainLyricsPath = Path.Combine(workingLyricsPath, $"{trackId}_{trackName}_False_plain.txt");
        string syncedLyricsPath = Path.Combine(workingLyricsPath, $"{trackId}_{trackName}_False_synced.txt");

        if (!File.Exists(plainLyricsPath) || !File.Exists(syncedLyricsPath))
        {
            Debug.LogError("Lyric files not found!");
            return;
        }

        string[] fullPlainLines = File.ReadAllLines(plainLyricsPath);
        List<string> lyricLines = fullPlainLines.Skip(3).ToList();
        
        // Trim trailing empty lines from the list to avoid counting them as unsynced
        while (lyricLines.Count > 0 && string.IsNullOrWhiteSpace(lyricLines[lyricLines.Count - 1]))
        {
            lyricLines.RemoveAt(lyricLines.Count - 1);
        }

        string plainLyrics = string.Join("\n", lyricLines);

        // Validate plain lyrics format (on non-empty lines)
        foreach (string line in lyricLines)
        {
            if (!string.IsNullOrWhiteSpace(line) && !(char.IsUpper(line[0]) || char.IsDigit(line[0]) || line[0] == '\''))
            {
                AlertManager.Instance.ShowWarning(LocalizationManager.L("alert.invalid_lyric_format.title", "Invalid lyric format."), LocalizationManager.L("alert.invalid_lyric_format.info", "Each lyric line must start with a capitalized letter, or an apostrophe or a number."), LocalizationManager.L("alert.dismiss", "Dismiss"));
                return;
            }
        }

        string[] syncedLines = File.ReadAllLines(syncedLyricsPath);

        // Filter out empty lines from synced lyrics to match the filtered plain lyrics
        List<string> filteredSyncedLines = new List<string>();
        foreach (string line in syncedLines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            string processedLine = line;
            // Ensure there is a space after the timestamp bracket if missing
            if (processedLine.Contains("]") && !processedLine.Contains("] "))
            {
                processedLine = processedLine.Replace("]", "] ");
            }

            // Pad single digit minutes with a zero (e.g., [5:15.00] -> [05:15.00])
            if (processedLine.StartsWith("[") && processedLine.Length > 2 && !char.IsDigit(processedLine[2]) && processedLine[2] == ':')
            {
                processedLine = processedLine.Insert(1, "0");
            }
            filteredSyncedLines.Add(processedLine);
        }
        string formattedSyncedLyrics = string.Join("\n", filteredSyncedLines);

        int meaningfulPlainCount = lyricLines.Count(l => !string.IsNullOrWhiteSpace(l));
        if (filteredSyncedLines.Count < meaningfulPlainCount)
        {
            AlertManager.Instance.ShowWarning(LocalizationManager.L("alert.lyrics_not_synced.title", "Not all lyrics are synced!"), LocalizationManager.L("alert.lyrics_not_synced.info", "Please make sure to sync all the lyrics before publishing."), LocalizationManager.L("alert.dismiss", "Dismiss"));
            return;
        }

        if (meaningfulPlainCount < 4)
        {
            AlertManager.Instance.ShowError(LocalizationManager.L("alert.lyrics_too_short.title", "The lyrics are too short."), LocalizationManager.L("alert.lyrics_too_short.info", "You need to have at least 4 verses (4 lines) to be able to submit."), LocalizationManager.L("alert.dismiss", "Dismiss"));
            return;
        }

        Debug.Log("Starting lyrics publication process...");
        LevelResourcesCompiler.Instance.ChallengeBegin();

        lyricsToPublish = new LyricsData { trackName = trackName, artistName = artistName, albumName = albumName, duration = duration, plainLyrics = plainLyrics, syncedLyrics = formattedSyncedLyrics };

        // Step 1: Get Challenge
        ChallengeResponse challenge = await GetChallengeAsync();
        if (challenge == null)
        {
            Debug.LogError("Failed to get a valid challenge from the server.");
            LevelResourcesCompiler.Instance.ChallengeEnd(); // End loading on failure
            return;
        }


        // Step 2: Schedule Job
        Debug.Log("Solving challenge with Unity Job System... The game will not freeze.");
        isSolving = true;
        currentChallengePrefix = challenge.prefix;

        resultNonce = new NativeArray<long>(1, Allocator.Persistent);
        jobPrefixBytes = new NativeArray<byte>(Encoding.UTF8.GetBytes(challenge.prefix), Allocator.Persistent);
        jobTargetBytes = new NativeArray<byte>(HexStringToByteArray(challenge.target), Allocator.Persistent);

        var job = new SolveChallengeJob
        {
            PrefixBytes = jobPrefixBytes,
            TargetBytes = jobTargetBytes,
            ResultNonce = resultNonce,
        };

        challengeJobHandle = job.Schedule();
    }

    // === UNITY LIFECYCLE METHODS ===
    private void Update()
    {
        if (!isSolving) return;

        // Simply check if the job is done.
        if (challengeJobHandle.IsCompleted)
        {
            // The job is done, so we can safely complete it and get the results.
            challengeJobHandle.Complete();
            long solvedNonce = resultNonce[0];

            // Clean up all NativeArrays to prevent memory leaks
            resultNonce.Dispose();
            jobPrefixBytes.Dispose();
            jobTargetBytes.Dispose();

            isSolving = false; // Mark as no longer solving

            if (solvedNonce >= 0)
            {
                Debug.Log($"Challenge solved. Nonce: {solvedNonce}");
                // Step 3: Publish the lyrics using the solved token
                _ = PublishLyricsAsync(lyricsToPublish, currentChallengePrefix, solvedNonce);
            }
            else
            {
                LevelResourcesCompiler.Instance.ChallengeEnd(); // End loading on failure
                Debug.LogError("Challenge solving failed. Could not find a valid nonce.");
                AlertManager.Instance.ShowError(LocalizationManager.L("alert.challenge_failed.title", "Challenge Failed."), LocalizationManager.L("alert.challenge_failed.info", "Could not solve the proof-of-work challenge."), LocalizationManager.L("alert.dismiss", "Dismiss"));
            }
        }
    }

    private void OnDestroy()
    {
        // This is a critical cleanup step to prevent memory leaks if the object is destroyed mid-job.
        if (isSolving)
        {
            challengeJobHandle.Complete();
            resultNonce.Dispose();
            jobPrefixBytes.Dispose();
            jobTargetBytes.Dispose();
            Debug.LogWarning("LrcLibPublisher destroyed while solving. Job was cancelled and memory was cleaned up.");
        }
    }

    // === PRIVATE HELPER METHODS ===
    private static bool IsRetryableError(UnityWebRequest request)
    {
        string error = request.error ?? "";
        string errorLower = error.ToLowerInvariant();
        return errorLower.Contains("curl error 52") ||
               errorLower.Contains("empty reply") ||
               errorLower.Contains("connection reset") ||
               errorLower.Contains("connection refused") ||
               errorLower.Contains("timed out") ||
               errorLower.Contains("timeout") ||
               errorLower.Contains("ssl") ||
               request.result == UnityWebRequest.Result.ConnectionError;
    }

    private async Task<ChallengeResponse> GetChallengeAsync()
    {
        int maxRetries = 25;
        int attempt = 0;

        while (attempt < maxRetries)
        {
            attempt++;
            using (var request = UnityWebRequest.PostWwwForm("https://lrclib.net/api/request-challenge", ""))
            {
                request.SetRequestHeader("User-Agent", "YASG-Challenge-Solver");
                var asyncOp = request.SendWebRequest();
                while (!asyncOp.isDone) await Task.Yield();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    if (IsRetryableError(request) && attempt < maxRetries)
                    {
                        Debug.LogWarning($"LRCLib challenge transient error: {request.error}, retrying... ({attempt}/{maxRetries})");
                        await Task.Delay(1000 * attempt);
                        continue;
                    }

                    Debug.LogError($"Failed to get challenge: {request.error}");
                    return null;
                }

                Debug.Log($"Challenge received. Payload: {request.downloadHandler.text}");
                return JsonUtility.FromJson<ChallengeResponse>(request.downloadHandler.text);
            }
        }

        Debug.LogError($"Failed to get challenge after {maxRetries} retries.");
        return null;
    }

    private async Task PublishLyricsAsync(LyricsData lyricsPayload, string prefix, long solvedNonce)
    {
        string publishToken = $"{prefix}:{solvedNonce}";
        Debug.Log($"Using publish token: {publishToken}");
        string url = "https://lrclib.net/api/publish";
        string jsonPayload = JsonUtility.ToJson(lyricsPayload);

        int maxRetries = 25;
        int attempt = 0;

        while (attempt < maxRetries)
        {
            attempt++;
            using (var request = new UnityWebRequest(url, "POST"))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonPayload);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("User-Agent", "YASG (github.com/grncd/YASG)");
                request.SetRequestHeader("X-Publish-Token", publishToken);

                var asyncOp = request.SendWebRequest();
                while (!asyncOp.isDone) await Task.Yield();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    if (IsRetryableError(request) && attempt < maxRetries)
                    {
                        Debug.LogWarning($"LRCLib publish transient error: {request.error}, retrying... ({attempt}/{maxRetries})");
                        await Task.Delay(1000 * attempt);
                        continue;
                    }

                    LevelResourcesCompiler.Instance.ChallengeEnd();
                    AlertManager.Instance.ShowError(LocalizationManager.L("alert.publish_error.title", "Error publishing lyrics."), request.downloadHandler.text, LocalizationManager.L("alert.dismiss", "Dismiss"));
                    Debug.LogError($"Error publishing lyrics: {request.error}\nServer Response: {request.downloadHandler.text}");
                    return;
                }

                LevelResourcesCompiler.Instance.ChallengeEnd();
                AlertManager.Instance.ShowSuccess(LocalizationManager.L("alert.lyrics_sent.title", "Your lyrics have been sent!"), LocalizationManager.L("alert.lyrics_sent.info", "By contributing lyrics, you don't just help YASG, but also every other project that uses LRCLib. Thank you so much!\n(You and all YASG players are now able to play this song.)"), LocalizationManager.L("alert.dismiss", "Dismiss"));
                Debug.Log($"Lyrics published successfully!\nServer Response: {request.downloadHandler.text}");
                return;
            }
        }

        LevelResourcesCompiler.Instance.ChallengeEnd();
        Debug.LogError($"Failed to publish lyrics after {maxRetries} retries.");
        AlertManager.Instance.ShowError(LocalizationManager.L("alert.publish_error.title", "Error publishing lyrics."), LocalizationManager.L("alert.publish_retry_failed.info", "Failed to publish after multiple retries due to connection issues. Please try again later."), LocalizationManager.L("alert.dismiss", "Dismiss"));
    }

    


    private static byte[] HexStringToByteArray(string hex)
    {
        int numberChars = hex.Length;
        byte[] bytes = new byte[numberChars / 2];
        for (int i = 0; i < numberChars; i += 2)
        {
            bytes[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);
        }
        return bytes;
    }
}