using System;
using System.Collections.Generic; // For List<SearchHandler.Image>

[Serializable]
public class Song
{
    public string name;
    public string artist;
    public string length;         // Formatted "M:SS" string
    public int duration_ms;    // Raw duration in milliseconds
    public string coverurl;
    public string url;            // Original source URL (e.g., Spotify URL)

    // Default constructor for JsonUtility
    public Song() { }

    // Constructor from Spotify Track (Assumes SearchHandler.Track and related classes are accessible)
    public Song(SearchHandler.Track track, string formattedLength)
    {
        this.name = track.name;
        this.artist = (track.artists != null && track.artists.Count > 0) ? track.artists[0].name : "Unknown Artist";
        this.length = formattedLength;
        this.duration_ms = track.duration_ms;
        this.coverurl = (track.album?.images != null && track.album.images.Count > 0) ? GetBestImageUrl(track.album.images) : "";
        this.url = track.external_urls?.spotify; // This is the original Spotify URL
        // spotifyTrackId field removed
    }

    // Constructor for manual/other sources
    public Song(string name, string artist, string length, int durationMs, string coverurl, string songSourceUrl)
    {
        this.name = name;
        this.artist = artist;
        this.length = length;
        this.duration_ms = durationMs;
        this.coverurl = coverurl;
        this.url = songSourceUrl; // This is the original source URL
        // spotifyTrackId field removed
    }

    private string GetBestImageUrl(List<SearchHandler.Image> images) // Assumes SearchHandler.Image is accessible
    {
        if (images == null || images.Count == 0) return "";
        SearchHandler.Image bestImage = images[0];
        foreach (var img in images)
        {
            if (img.width > bestImage.width) bestImage = img;
        }
        return bestImage.url;
    }

    public override string ToString()
    {
        // Removed spotifyTrackId from ToString
        return $"Song: {name} by {artist} ({length}), DurationMS: {duration_ms}, URL: {url}";
    }
}

[Serializable]
public class UserMusicData
{
    public List<Song> favorites = new List<Song>();
    public List<Song> downloads = new List<Song>();
}