using UnityEngine;
using UnityEngine.UI;

public class OscilloscopeUI : MonoBehaviour
{
    public AudioSource source;
    public RawImage display;

    [Header("Display Settings")]
    public int barCount = 64;
    public float barWidth = 4f;
    public float barSpacing = 2f;
    public float heightMultiplier = 500f;
    public Color barColor = Color.white;
    public bool glow = true;
    public float glowIntensity = 0.5f;
    public int glowSpread = 2;

    [Header("Texture Settings")]
    public int texWidth = 1024;
    public int texHeight = 512;

    private Texture2D texture;
    private float[] spectrumData;
    private Color[] clearPixels;

    void Start()
    {
        texture = new Texture2D(texWidth, texHeight, TextureFormat.RGBA32, false);
        texture.filterMode = FilterMode.Bilinear;
        display.texture = texture;

        spectrumData = new float[512];
        clearPixels = new Color[texWidth * texHeight];
        for (int i = 0; i < clearPixels.Length; i++)
            clearPixels[i] = new Color(0, 0, 0, 0);
    }

    void Update()
    {
        source.GetSpectrumData(spectrumData, 0, FFTWindow.Blackman);
        texture.SetPixels(clearPixels); // Clear frame

        float step = texWidth / (float)barCount;

        for (int i = 0; i < barCount; i++)
        {
            // Logarithmic index mapping
            float logIndex = Mathf.Pow(i / (float)barCount, 2.2f); // 2.2f = adjust curve, higher means more high freq bars
            int dataIndex = Mathf.Clamp((int)(logIndex * spectrumData.Length), 0, spectrumData.Length - 1);

            // Apply scaling to balance volume (manual curve)
            float gain = Mathf.Lerp(5f, 40f, i / (float)barCount); // Boost highs more than lows
            float value = spectrumData[dataIndex] * heightMultiplier * gain;
            int barHeight = Mathf.Clamp((int)value, 1, texHeight - 1);

            int xStart = Mathf.RoundToInt(i * step);
            int xEnd = Mathf.Clamp(xStart + Mathf.RoundToInt(barWidth), 0, texWidth - 1);

            for (int x = xStart; x < xEnd; x++)
            {
                for (int y = 0; y < barHeight; y++)
                {
                    texture.SetPixel(x, y, barColor);

                    if (glow)
                    {
                        for (int g = 1; g <= glowSpread; g++)
                        {
                            float glowAlpha = glowIntensity / g;
                            Color glowColor = new Color(barColor.r, barColor.g, barColor.b, glowAlpha);
                            if (y + g < texHeight)
                                texture.SetPixel(x, y + g, glowColor);
                        }
                    }
                }
            }
        }

        texture.Apply();
    }

}
