using UnityEngine;
using UnityEngine.Networking;
using System;
using System.Text;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Diagnostics;
using Debug = UnityEngine.Debug; // Explicitly use Unity's Debug

/// <summary>
/// A component to publish lyrics to lrclib.net, including solving the required proof-of-work challenge.
/// </summary>
public class LrcLibPublisherWithChallenge : MonoBehaviour
{
    // === DATA STRUCTURES FOR JSON PARSING ===

    [Serializable]
    private class ChallengeResponse
    {
        public string prefix;
        public string target;
    }

    [Serializable]
    private class LyricsData
    {
        public string trackName;
        public string artistName;
        public string albumName;
        public float duration;
        public string plainLyrics;
        public string syncedLyrics;
    }

    // === PUBLIC ORCHESTRATION METHOD ===

    /// <summary>
    /// The main entry point. Fetches a challenge, solves it, and then publishes the lyrics.
    /// This method is async so it can await the background task without blocking the main thread.
    /// </summary>
    public async void PublishWithChallenge(
        string trackName, string artistName, string albumName, float duration,
        string plainLyrics, string syncedLyrics)
    {
        Debug.Log("Starting lyrics publication process...");

        // --- Step 1: Request the challenge from the server ---
        ChallengeResponse challenge = null;
        using (var request = UnityWebRequest.Post("https://lrclib.net/api/request-challenge", ""))
        {
            request.SetRequestHeader("User-Agent", "YASG-Challenge-Solver");
            var asyncOp = request.SendWebRequest();

            // Await the web request completion on the main thread
            while (!asyncOp.isDone)
            {
                await Task.Yield(); // Yield control back, allows the game to continue running
            }

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"Failed to get challenge: {request.error}");
                return;
            }

            challenge = JsonUtility.FromJson<ChallengeResponse>(request.downloadHandler.text);
            Debug.Log($"Challenge received. Prefix: {challenge.prefix}, Target: {challenge.target}");
        }

        if (challenge == null || string.IsNullOrEmpty(challenge.prefix))
        {
            Debug.LogError("Challenge response was invalid.");
            return;
        }

        // --- Step 2: Solve the challenge on a background thread to avoid freezing ---
        Debug.Log("Solving challenge... This may take a moment and use significant CPU.");
        var stopwatch = Stopwatch.StartNew();

        // Task.Run offloads the CPU-intensive work to a thread pool thread.
        string solvedNonce = await Task.Run(() => SolveChallengeCPU(challenge.prefix, challenge.target));

        stopwatch.Stop();
        Debug.Log($"Challenge solved in {stopwatch.ElapsedMilliseconds} ms. Nonce: {solvedNonce}");

        // --- Step 3: Publish the lyrics using the solved nonce as the token ---
        // We are back on the main thread here, so we can start a Coroutine.
        StartCoroutine(PublishLyrics(
            trackName, artistName, albumName, duration,
            plainLyrics, syncedLyrics, solvedNonce
        ));
    }


    // === PRIVATE HELPER METHODS ===

    /// <summary>
    /// Coroutine to send the final POST request with the lyrics data and the solved token.
    /// </summary>
    private System.Collections.IEnumerator PublishLyrics(
        string trackName, string artistName, string albumName, float duration,
        string plainLyrics, string syncedLyrics, string publishToken)
    {
        string url = $"https://lrclib.net/api/publish?X-Publish-Token={publishToken}";
        var lyricsPayload = new LyricsData
        {
            trackName = trackName,
            artistName = artistName,
            albumName = albumName,
            duration = duration,
            plainLyrics = plainLyrics,
            syncedLyrics = syncedLyrics
        };
        string jsonPayload = JsonUtility.ToJson(lyricsPayload);

        using (var request = new UnityWebRequest(url, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonPayload);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("User-Agent", "YASG");

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"Error publishing lyrics: {request.error}");
                Debug.LogError($"Server Response: {request.downloadHandler.text}");
            }
            else
            {
                Debug.Log("Lyrics published successfully!");
                Debug.Log($"Server Response: {request.downloadHandler.text}");
            }
        }
    }

    /// <summary>
    /// The CPU-intensive challenge solver.
    /// MUST be run on a background thread.
    /// </summary>
    private static string SolveChallengeCPU(string prefix, string targetHex)
    {
        byte[] targetBytes = HexStringToByteArray(targetHex);
        using (var sha256 = SHA256.Create())
        {
            for (long nonce = 0; nonce < long.MaxValue; ++nonce)
            {
                string input = prefix + nonce;
                byte[] inputBytes = Encoding.UTF8.GetBytes(input);
                byte[] hashBytes = sha256.ComputeHash(inputBytes);

                if (VerifyNonce(hashBytes, targetBytes))
                {
                    return nonce.ToString();
                }
            }
        }
        // Should realistically never be reached unless the challenge is impossible
        throw new Exception("Could not solve the challenge within reasonable limits.");
    }

    /// <summary>
    /// Verifies if the hash is "less than or equal to" the target.
    /// </summary>
    private static bool VerifyNonce(byte[] hash, byte[] target)
    {
        // Assumes hash and target are the same length (32 bytes for SHA256)
        for (int i = 0; i < hash.Length; i++)
        {
            if (hash[i] > target[i]) return false; // If any byte is greater, it fails
            if (hash[i] < target[i]) return true;  // If a byte is smaller, it's a valid solution
        }
        return true; // If all bytes are equal, it's also a valid solution
    }

    /// <summary>
    /// Converts a hexadecimal string to a byte array.
    /// </summary>
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


    // === INSPECTOR TEST REGION ===
    #region Example Usage
    [Header("Test Data (for Inspector)")]
    [SerializeField] private string _testTrackName = "The Best Song";
    [SerializeField] private string _testArtistName = "The Best Artist";
    [SerializeField] private string _testAlbumName = "The Best Album";
    [SerializeField] private float _testDuration = 245.5f;
    [TextArea(3, 8)]
    [SerializeField] private string _testPlainLyrics = "This is line one\nThis is line two";
    [TextArea(3, 8)]
    [SerializeField] private string _testSyncedLyrics = "[00:10.50]This is line one\n[00:15.75]This is line two";

    [ContextMenu("Publish Lyrics (with Challenge)")]
    private void TestPublishFromInspector()
    {
        PublishWithChallenge(
            _testTrackName, _testArtistName, _testAlbumName, _testDuration,
            _testPlainLyrics, _testSyncedLyrics
        );
    }
    #endregion
}