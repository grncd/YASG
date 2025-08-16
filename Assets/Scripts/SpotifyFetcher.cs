using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking; // for UnityWebRequest
using Newtonsoft.Json.Linq;
using System.Collections;   // install Newtonsoft.Json via NuGet/Unity package manager

public class SpotifyFetcher : MonoBehaviour
{
    public static SpotifyFetcher Instance { get; private set; }
    private void Awake() {
        Instance = this;
    }

    public IEnumerator GetPreviewUrl(string trackId, Action<string> onResult)
    {
        string embedUrl = $"https://open.spotify.com/embed/track/{trackId}";
        using (UnityWebRequest www = UnityWebRequest.Get(embedUrl))
        {
            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("Error fetching embed: " + www.error);
                onResult?.Invoke(null);
                yield break;
            }

            string html = www.downloadHandler.text;

            // Find the __NEXT_DATA__ JSON with regex
            var match = Regex.Match(html, @"<script id=""__NEXT_DATA__"" type=""application/json"">(.*?)</script>", RegexOptions.Singleline);
            if (!match.Success)
            {
                Debug.LogError("Could not find __NEXT_DATA__ script in HTML.");
                onResult?.Invoke(null);
                yield break;
            }

            string json = match.Groups[1].Value;

            try
            {
                var root = JObject.Parse(json);
                var previewUrl = root["props"]?["pageProps"]?["state"]?["data"]?["entity"]?["audioPreview"]?["url"]?.ToString();

                if (string.IsNullOrEmpty(previewUrl))
                {
                    Debug.LogWarning("No preview URL found for this track.");
                    onResult?.Invoke(null);
                }
                else
                {
                    onResult?.Invoke(previewUrl);
                }
            }
            catch (Exception e)
            {
                Debug.LogError("JSON parse error: " + e);
                onResult?.Invoke(null);
            }
        }
    }
}
