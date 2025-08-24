using System;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;

public class PartyModePlayer : MonoBehaviour
{
    [Header("UI References")]
    public RectMask2D rectMask;
    public GameObject bg;
    public GameObject fill;
    public GameObject ready;

    [Header("Mic Settings")]
    public float volumeThreshold = 0.65f;
    public float holdDuration = 2.2f;

    private AudioClip micClip;
    private int micDeviceIndex;
    private string micDeviceName;
    private bool isCharging = false;
    private float screamTimer = 0f;

    private const int sampleWindow = 128;
    private int startPaddingTop = 340;
    private int endPaddingTop = 12;

    void Start()
    {
        micDeviceIndex = PlayerPrefs.GetInt($"{gameObject.name}Mic", 0);

        Debug.Log($"Available microphones: {Microphone.devices.Length}");
        for (int i = 0; i < Microphone.devices.Length; i++)
        {
            Debug.Log($"Mic {i}: {Microphone.devices[i]}");
        }

        if (Microphone.devices.Length > micDeviceIndex)
        {
            micDeviceName = Microphone.devices[micDeviceIndex];
            Debug.Log($"Starting microphone: {micDeviceName}");
            micClip = Microphone.Start(micDeviceName, true, 10, 44100);

            // Wait for mic to initialize
            while (!(Microphone.GetPosition(micDeviceName) > 0)) { }
            Debug.Log("Microphone started successfully");
        }
        else
        {
            Debug.LogError($"No microphone found at index {micDeviceIndex}!");
        }

        // Reset UI
        SetPadding(startPaddingTop);
        ready.SetActive(false);
        fill.SetActive(true);
        bg.SetActive(true);
    }

    void Update()
    {
        float volume = GetMicVolume();
        Debug.Log(volume);

        if (volume > volumeThreshold)
        {
            screamTimer += Time.deltaTime;

            // Progress fill
            float progress = Mathf.Clamp01(screamTimer / holdDuration);
            int currentTop = Mathf.RoundToInt(Mathf.Lerp(startPaddingTop, endPaddingTop, progress));
            SetPadding(currentTop);

            if (screamTimer >= holdDuration && !isCharging)
            {
                isCharging = true;
                OnChargeComplete();
            }
        }
        else
        {
            // Reset if user stops screaming before finishing
            screamTimer = 0f;
            isCharging = false;
            SetPadding(startPaddingTop);
        }
    }

    float GetMicVolume()
    {
        if (micClip == null) return 0f;

        int micPos = Microphone.GetPosition(micDeviceName) - sampleWindow + 1;
        if (micPos < 0) return 0f;

        float[] waveData = new float[sampleWindow];
        micClip.GetData(waveData, micPos);

        float total = 0f;
        for (int i = 0; i < sampleWindow; i++)
        {
            total += Mathf.Abs(waveData[i]);
        }

        return total / sampleWindow;
    }

    void SetPadding(int topPadding)
    {
        if (rectMask != null)
        {
            rectMask.padding = new Vector4(rectMask.padding.x, rectMask.padding.y, rectMask.padding.z, topPadding);
        }
    }

    void OnChargeComplete()
    {
        fill.SetActive(false);
        bg.SetActive(false);
        ready.SetActive(true);
        Match match = Regex.Match(gameObject.name, @"\d+");
        if (match.Success)
        {
            int playerNumber = int.Parse(match.Value);
            LevelResourcesCompiler.Instance.playersReady[playerNumber-1] = true;
        }
    }

    public void StopRecording()
    {
        if (!string.IsNullOrEmpty(micDeviceName) && Microphone.IsRecording(micDeviceName))
        {
            Microphone.End(micDeviceName);
            micClip = null;
        }
    }

    void OnDestroy()
    {
        // Auto-clean if object is destroyed
        StopRecording();
    }
}
