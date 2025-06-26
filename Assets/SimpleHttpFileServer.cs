using UnityEngine;
using System.Net;
using System.IO;
using System.Threading;
using System.Collections.Generic; // Required for Dictionary

public class SimpleHttpFileServer : MonoBehaviour
{
    private HttpListener _listener;
    private Thread _listenerThread;

    // This dictionary will hold our file data in memory.
    // Key: filename (e.g., "song.mp3"), Value: the file's raw byte data.
    private Dictionary<string, byte[]> _fileCache;

    /// <summary>
    /// Starts the server. Instead of a directory, it now takes a list of file paths to load into memory.
    /// </summary>
    public bool StartServer(List<string> filePathsToServe, int port = 8080)
    {
        if (_listener != null && _listener.IsListening)
        {
            Debug.Log("HTTP Server is already running.");
            return true;
        }

        // --- Cache Files into Memory ---
        _fileCache = new Dictionary<string, byte[]>();
        foreach (string path in filePathsToServe)
        {
            if (File.Exists(path))
            {
                try
                {
                    string fileName = Path.GetFileName(path);
                    byte[] fileData = File.ReadAllBytes(path); // Read the whole file once.
                    _fileCache[fileName] = fileData;
                    Debug.Log($"Cached file '{fileName}' ({fileData.Length} bytes) for serving.");
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"Failed to cache file {path}: {ex.Message}");
                    // If we can't cache a required file, we should not start the server.
                    return false;
                }
            }
            else
            {
                Debug.LogError($"File to be served does not exist at path: {path}");
                return false;
            }
        }

        // --- Start the Listener ---
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://*:{port}/");

        Debug.Log($"Starting HTTP server on port {port}.");
        _listener.Start();

        _listenerThread = new Thread(HandleRequests);
        _listenerThread.Start();
        return true;
    }

    public void StopServer()
    {
        if (_listener != null && _listener.IsListening)
        {
            Debug.Log("Stopping HTTP server.");
            _listener.Stop();
            _listener.Close();
            _listenerThread?.Abort();
            _fileCache?.Clear();
        }
    }

    private void HandleRequests()
    {
        while (_listener != null && _listener.IsListening)
        {
            try
            {
                var context = _listener.GetContext();
                ProcessRequest(context);
            }
            catch (HttpListenerException) { break; }
            catch (System.Exception ex) { Debug.LogError($"HTTP Server Error: {ex.Message}"); }
        }
    }

    private void ProcessRequest(HttpListenerContext context)
    {
        string decodedUrlPath = WebUtility.UrlDecode(context.Request.Url.AbsolutePath);
        string requestedFilename = Path.GetFileName(decodedUrlPath);

        // --- Serve from Memory Cache ---
        // Look for the requested filename in our dictionary.
        if (_fileCache.TryGetValue(requestedFilename, out byte[] fileData))
        {
            // If found, write the byte array from RAM directly to the response.
            context.Response.ContentLength64 = fileData.Length;
            context.Response.OutputStream.Write(fileData, 0, fileData.Length);
            Debug.Log($"HTTP Server: Served '{requestedFilename}' from memory cache.");
        }
        else
        {
            Debug.LogWarning($"HTTP Server: Requested file '{requestedFilename}' not found in cache.");
            context.Response.StatusCode = (int)HttpStatusCode.NotFound;
        }
        context.Response.OutputStream.Close();
    }

    private void OnDestroy()
    {
        StopServer();
    }
}