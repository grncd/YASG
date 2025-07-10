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

        Debug.Log("Starting lyrics publication process...");
        LevelResourcesCompiler.Instance.ChallengeBegin();

        // Load Lyrics
        string dataPath = PlayerPrefs.GetString("dataPath");
        string workingLyricsPath = Path.Combine(dataPath, "workingLyrics");
        string trackId = trackUrl.Split('/').Last();
        string plainLyricsPath = Path.Combine(workingLyricsPath, $"{trackId}_{trackName}_plain.txt");
        string syncedLyricsPath = Path.Combine(workingLyricsPath, $"{trackId}_{trackName}_synced.txt");

        if (!File.Exists(plainLyricsPath) || !File.Exists(syncedLyricsPath))
        {
            Debug.LogError("Lyric files not found!");
            return;
        }

        string[] plainLines = File.ReadAllLines(plainLyricsPath);
        string plainLyrics = string.Join("\n", plainLines.Skip(3));

        string[] syncedLines = File.ReadAllLines(syncedLyricsPath);
        for (int i = 0; i < syncedLines.Length; i++)
        {
            if (syncedLines[i].Contains("]"))
            {
                syncedLines[i] = syncedLines[i].Replace("]", "] ");
            }
        }
        string formattedSyncedLyrics = string.Join("\n", syncedLines);

        lyricsToPublish = new LyricsData { trackName = trackName, artistName = artistName, albumName = albumName, duration = duration, plainLyrics = plainLyrics, syncedLyrics = formattedSyncedLyrics };

        // Step 1: Get Challenge
        ChallengeResponse challenge = await GetChallengeAsync();
        if (challenge == null)
        {
            Debug.LogError("Failed to get a valid challenge from the server.");
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
                StartCoroutine(PublishLyrics(lyricsToPublish, currentChallengePrefix, solvedNonce));
            }
            else
            {
                Debug.LogError("Challenge solving failed. Could not find a valid nonce.");
                // You might want an alert for this failure case as well
                AlertManager.Instance.ShowError("Challenge Failed.", "Could not solve the proof-of-work challenge.", "Dismiss");
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
    private async Task<ChallengeResponse> GetChallengeAsync()
    {
        using (var request = UnityWebRequest.Post("https://lrclib.net/api/request-challenge", ""))
        {
            request.SetRequestHeader("User-Agent", "YASG-Challenge-Solver");
            var asyncOp = request.SendWebRequest();
            while (!asyncOp.isDone) await Task.Yield();
            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"Failed to get challenge: {request.error}");
                return null;
            }
            Debug.Log($"Challenge received. Payload: {request.downloadHandler.text}");
            return JsonUtility.FromJson<ChallengeResponse>(request.downloadHandler.text);
        }
    }

    private System.Collections.IEnumerator PublishLyrics(LyricsData lyricsPayload, string prefix, long solvedNonce)
    {
        string publishToken = $"{prefix}:{solvedNonce}";
        Debug.Log($"Using publish token: {publishToken}");
        string url = "https://lrclib.net/api/publish";
        string jsonPayload = JsonUtility.ToJson(lyricsPayload);

        using (var request = new UnityWebRequest(url, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonPayload);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("User-Agent", "YASG (in-game lyric editor for my WIP karaoke game. will put link here when released!)");
            request.SetRequestHeader("X-Publish-Token", publishToken);

            yield return request.SendWebRequest();
            LevelResourcesCompiler.Instance.ChallengeEnd();

            if (request.result != UnityWebRequest.Result.Success)
            {
                AlertManager.Instance.ShowError("Error publishing lyrics.", request.downloadHandler.text, "Dismiss");
                Debug.LogError($"Error publishing lyrics: {request.error}\nServer Response: {request.downloadHandler.text}");
            }
            else
            {
                AlertManager.Instance.ShowSuccess("Your lyrics have been sent!", "By contributing lyrics, you don't just help YASG, but also every other project that uses LRCLib. Thank you so much!\n(You and all YASG players are now able to play this song.)", "Dismiss");
                Debug.Log($"Lyrics published successfully!\nServer Response: {request.downloadHandler.text}");
            }
        }
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