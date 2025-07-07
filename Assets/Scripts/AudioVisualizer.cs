using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(RectTransform))]
public class AudioVisualizer : MonoBehaviour
{
    [Tooltip("The UI Image prefab to use for each bar in the visualizer.")]
    public GameObject barPrefab;

    [Tooltip("The number of bars to create for the visualization.")]
    [Range(8, 256)]
    public int numberOfBars = 64;

    [Tooltip("The maximum height the bars can reach.")]
    public float maxBarHeight = 200f;

    [Tooltip("How quickly the bars react to changes in audio. Lower values are smoother.")]
    public float smoothingFactor = 8f;

    [Tooltip("The color of the visualizer bars.")]
    public Color barColor = Color.white;

    [Tooltip("Controls the frequency distribution. > 1 gives more bars to low frequencies, < 1 gives more to high frequencies.")]
    public float frequencyDistributionExponent = 2f;

    private RectTransform[] _visualizerBars;
    private float[] _spectrumData;
    private float[] _currentBarHeights;

    void Start()
    {
        if (barPrefab == null)
        {
            Debug.LogError("AudioVisualizer: barPrefab is not assigned!");
            return;
        }

        _spectrumData = new float[1024]; // Must be a power of 2
        _visualizerBars = new RectTransform[numberOfBars];
        _currentBarHeights = new float[numberOfBars];

        // Get the width of this container to properly space the bars
        float containerWidth = GetComponent<RectTransform>().rect.width;
        float barWidth = containerWidth / numberOfBars;

        for (int i = 0; i < numberOfBars; i++)
        {
            GameObject bar = Instantiate(barPrefab, transform);
            bar.name = "Bar_" + i;
            
            Image barImage = bar.GetComponent<Image>();
            if (barImage != null)
            {
                barImage.color = barColor;
            }

            RectTransform rt = bar.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(0, 0);
            rt.pivot = new Vector2(0.5f, 0f);

            // Position the bar
            rt.sizeDelta = new Vector2(barWidth * 0.9f, 0); // 90% width to create small gaps
            rt.anchoredPosition = new Vector2((i * barWidth) + (barWidth / 2), 0);
            
            _visualizerBars[i] = rt;
        }
    }

    void Update()
    {
        if (_visualizerBars == null) return;

        // Get the audio spectrum data
        AudioListener.GetSpectrumData(_spectrumData, 0, FFTWindow.BlackmanHarris);

        for (int i = 0; i < numberOfBars; i++)
        {
            // Use an exponential mapping for a more musical frequency distribution
            float normalizedBarIndex = (float)i / numberOfBars;
            float exponentialIndex = Mathf.Pow(normalizedBarIndex, frequencyDistributionExponent);
            int spectrumIndex = Mathf.Clamp((int)(_spectrumData.Length * exponentialIndex), 0, _spectrumData.Length - 1);
            
            // Amplify the spectrum data and clamp it
            float targetHeight = Mathf.Clamp(_spectrumData[spectrumIndex] * (i + 10) * maxBarHeight, 0, maxBarHeight);

            // Smoothly transition to the target height
            float currentHeight = Mathf.Lerp(_currentBarHeights[i], targetHeight, Time.deltaTime * smoothingFactor);
            
            _visualizerBars[i].sizeDelta = new Vector2(_visualizerBars[i].sizeDelta.x, currentHeight);
            _currentBarHeights[i] = currentHeight;
        }
    }
}
