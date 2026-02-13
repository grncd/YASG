#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine;
using System;
using System.Threading.Tasks;

/// <summary>
/// Wrapper for youtubedl-android library (io.github.junkfood02.youtubedl-android)
/// Provides Unity-friendly interface to yt-dlp functionality on Android
/// </summary>
public static class YtDlpAndroidWrapper
{
    private const string YOUTUBEDL_CLASS = "com.yausername.youtubedl_android.YoutubeDL";
    private const string REQUEST_CLASS = "com.yausername.youtubedl_android.YoutubeDLRequest";
    private const string CALLBACK_CLASS = "com.yausername.youtubedl_android.DownloadProgressCallback";
    private static bool _initialized = false;
    private static bool _updated = false;

    /// <summary>
    /// AndroidJavaProxy that implements DownloadProgressCallback to receive progress from yt-dlp.
    /// </summary>
    private class ProgressCallback : AndroidJavaProxy
    {
        private readonly Action<double> _onProgress;

        public ProgressCallback(Action<double> onProgress)
            : base(CALLBACK_CLASS)
        {
            _onProgress = onProgress;
        }

        // Called by the Java library: void onProgressUpdate(float progress, long etaInSeconds, String line)
        void onProgressUpdate(float progress, long etaInSeconds, string line)
        {
            _onProgress?.Invoke(progress / 100.0);
        }
    }

    /// <summary>
    /// Initialize the youtubedl-android library. Must be called before any download operations.
    /// </summary>
    public static void Initialize()
    {
        if (_initialized) return;

        try
        {
            Debug.Log("[YtDlpAndroid] Initializing youtubedl-android library...");

            // Get the current activity (context)
            using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            using var context = activity.Call<AndroidJavaObject>("getApplicationContext");

            // Initialize YoutubeDL singleton
            using var youtubeDLClass = new AndroidJavaClass(YOUTUBEDL_CLASS);
            using var instance = youtubeDLClass.GetStatic<AndroidJavaObject>("INSTANCE");
            instance.Call("init", context);

            // Initialize FFmpeg module so yt-dlp can find ffmpeg/ffprobe for post-processing
            // In v0.18.1 the class moved to com.yausername.ffmpeg.FFmpeg
            using var ffmpegClass = new AndroidJavaClass("com.yausername.ffmpeg.FFmpeg");
            using var ffmpegInstance = ffmpegClass.GetStatic<AndroidJavaObject>("INSTANCE");
            ffmpegInstance.Call("init", context);

            _initialized = true;
            Debug.Log("[YtDlpAndroid] Library and FFmpeg initialized successfully");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[YtDlpAndroid] Initialization failed: {ex.Message}\n{ex.StackTrace}");
            throw;
        }
    }

    /// <summary>
    /// Update yt-dlp to the latest version. Should be called after Initialize().
    /// Only updates once per session.
    /// </summary>
    public static async Task UpdateYtDlpAsync()
    {
        if (_updated) return;

        await Task.Run(() =>
        {
            AndroidJNI.AttachCurrentThread();
            try
            {
                Debug.Log("[YtDlpAndroid] Updating yt-dlp...");

                using var youtubeDLClass = new AndroidJavaClass(YOUTUBEDL_CLASS);
                using var instance = youtubeDLClass.GetStatic<AndroidJavaObject>("INSTANCE");

                using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                using var context = activity.Call<AndroidJavaObject>("getApplicationContext");

                // UpdateChannel is a Kotlin sealed class — STABLE is a nested object class with INSTANCE field
                using var stableChannelClass = new AndroidJavaClass(
                    "com.yausername.youtubedl_android.YoutubeDL$UpdateChannel$STABLE");
                using var stableChannel = stableChannelClass.GetStatic<AndroidJavaObject>("INSTANCE");

                var updateStatus = instance.Call<AndroidJavaObject>("updateYoutubeDL", context, stableChannel);
                if (updateStatus != null)
                {
                    string statusName = updateStatus.Call<string>("name");
                    Debug.Log($"[YtDlpAndroid] yt-dlp update status: {statusName}");
                }

                _updated = true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[YtDlpAndroid] yt-dlp update failed (non-fatal): {ex.Message}");
                _updated = true;
            }
        });
    }

    /// <summary>
    /// Download a video using yt-dlp with progress reporting
    /// </summary>
    /// <param name="videoUrl">URL of the video to download</param>
    /// <param name="outputPath">Full path where the file should be saved</param>
    /// <param name="outputTemplate">yt-dlp output template (default: filename)</param>
    /// <param name="extraArgs">Additional command-line arguments for yt-dlp</param>
    /// <param name="onProgress">Callback for progress updates (0.0 to 1.0)</param>
    public static async Task DownloadAsync(
        string videoUrl,
        string outputPath,
        string outputTemplate = null,
        string[] extraArgs = null,
        Action<double> onProgress = null)
    {
        if (string.IsNullOrEmpty(videoUrl))
            throw new ArgumentException("Video URL cannot be empty", nameof(videoUrl));

        if (string.IsNullOrEmpty(outputPath))
            throw new ArgumentException("Output path cannot be empty", nameof(outputPath));

        await Task.Run(() =>
        {
            AndroidJNI.AttachCurrentThread();

            try
            {
                Debug.Log($"[YtDlpAndroid] Starting download: {videoUrl}");
                Debug.Log($"[YtDlpAndroid] Output path: {outputPath}");

                // Get YoutubeDL instance
                AndroidJavaClass youtubeDLClass = new AndroidJavaClass(YOUTUBEDL_CLASS);
                AndroidJavaObject youtubeDL = youtubeDLClass.GetStatic<AndroidJavaObject>("INSTANCE");

                // Create request with video URL
                AndroidJavaObject request = new AndroidJavaObject(REQUEST_CLASS, videoUrl);

                // Add options for audio extraction and conversion
                request.Call<AndroidJavaObject>("addOption", "-x"); // Extract audio
                request.Call<AndroidJavaObject>("addOption", "--audio-format", "mp3");
                request.Call<AndroidJavaObject>("addOption", "--audio-quality", "192K"); // 192 kbps

                // Set output file path
                // Use -o to specify output path
                string outputDir = System.IO.Path.GetDirectoryName(outputPath);
                string outputFile = System.IO.Path.GetFileNameWithoutExtension(outputPath);

                // Ensure output directory exists
                if (!System.IO.Directory.Exists(outputDir))
                {
                    System.IO.Directory.CreateDirectory(outputDir);
                }

                // Build the output template for yt-dlp
                // We need to specify the full path
                request.Call<AndroidJavaObject>("addOption", "-o", outputPath);

                // Add any extra arguments
                if (extraArgs != null)
                {
                    foreach (string arg in extraArgs)
                    {
                        if (!string.IsNullOrEmpty(arg))
                        {
                            request.Call<AndroidJavaObject>("addOption", arg);
                        }
                    }
                }

                // Generate a unique process ID for this download
                string processId = Guid.NewGuid().ToString();

                // Execute the download with progress callback
                Debug.Log("[YtDlpAndroid] Executing download...");
                ProgressCallback callback = onProgress != null ? new ProgressCallback(onProgress) : null;
                AndroidJavaObject response = youtubeDL.Call<AndroidJavaObject>(
                    "execute",
                    request,
                    processId,
                    callback
                );

                // Check exit code
                int exitCode = response.Call<int>("getExitCode");
                string outText = response.Call<string>("getOut");
                string errText = response.Call<string>("getErr");

                Debug.Log($"[YtDlpAndroid] Download finished with exit code: {exitCode}");
                if (!string.IsNullOrEmpty(outText))
                {
                    Debug.Log($"[YtDlpAndroid] stdout: {outText}");
                }
                if (!string.IsNullOrEmpty(errText))
                {
                    Debug.Log($"[YtDlpAndroid] stderr: {errText}");
                }

                if (exitCode != 0)
                {
                    throw new Exception($"yt-dlp failed with exit code {exitCode}. Error: {errText}");
                }

                // Verify output file exists
                if (!System.IO.File.Exists(outputPath))
                {
                    throw new Exception($"yt-dlp reported success but file not found at: {outputPath}");
                }

                Debug.Log($"[YtDlpAndroid] Download complete: {outputPath}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[YtDlpAndroid] Download failed: {ex.Message}\n{ex.StackTrace}");
                throw;
            }
        });
    }

    /// <summary>
    /// Get video information without downloading
    /// </summary>
    /// <param name="videoUrl">URL of the video</param>
    /// <returns>JSON string with video information</returns>
    public static async Task<string> GetVideoInfoAsync(string videoUrl)
    {
        if (string.IsNullOrEmpty(videoUrl))
            throw new ArgumentException("Video URL cannot be empty", nameof(videoUrl));

        return await Task.Run(() =>
        {
            AndroidJNI.AttachCurrentThread();

            try
            {
                Debug.Log($"[YtDlpAndroid] Getting video info: {videoUrl}");

                AndroidJavaClass youtubeDLClass = new AndroidJavaClass(YOUTUBEDL_CLASS);
                AndroidJavaObject youtubeDL = youtubeDLClass.GetStatic<AndroidJavaObject>("INSTANCE");

                AndroidJavaObject request = new AndroidJavaObject(REQUEST_CLASS, videoUrl);
                request.Call<AndroidJavaObject>("addOption", "--dump-json");
                request.Call<AndroidJavaObject>("addOption", "--no-playlist");

                string processId = Guid.NewGuid().ToString();
                AndroidJavaObject response = youtubeDL.Call<AndroidJavaObject>("execute", request, processId);

                int exitCode = response.Call<int>("getExitCode");
                string outText = response.Call<string>("getOut");

                if (exitCode != 0)
                {
                    string errText = response.Call<string>("getErr");
                    throw new Exception($"Failed to get video info. Exit code: {exitCode}, Error: {errText}");
                }

                Debug.Log($"[YtDlpAndroid] Video info retrieved successfully");
                return outText;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[YtDlpAndroid] GetVideoInfo failed: {ex.Message}");
                throw;
            }
        });
    }
}
#endif
