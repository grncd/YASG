using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TMPro;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using MPUIKIT;

public class ResultsScreen : MonoBehaviour
{
    private int playerId;
    public GameObject starPrefab;
    private AudioSource starFX;

    private string profileName;
    private int achievedScore;
    private int formerScore;
    private int newScore;
    private int formerLevel;
    private int newLevel;
    private bool animPlaying = false;
    private MPImage progressBar;
    private TextMeshProUGUI levelText;
    private ParticleSystem particles;
    public AudioSource levelFX;
    public AudioSource levelUpFX;
    // scoreDisplay.text = Mathf.RoundToInt(score).ToString("#,#")
    private async void Start()
    {
        Application.targetFrameRate = -1;

        playerId = int.Parse(Regex.Replace(gameObject.name, "[^0-9]", ""));

        if (PlayerPrefs.GetInt("Player" + playerId.ToString()) == 1)
        {
            starFX = gameObject.GetComponent<AudioSource>();
            DisplaySequence();
        }
        else
        {
            gameObject.SetActive(false);
            return;
        }

        AudioSource musicSource = GameObject.Find("Music")?.GetComponent<AudioSource>();
        Destroy(musicSource.gameObject);

        particles = transform.GetChild(5).GetChild(1).GetComponent<ParticleSystem>();
        profileName = PlayerPrefs.GetString("Player" + playerId.ToString() + "Name");
        achievedScore = PlayerPrefs.GetInt("Player" + playerId.ToString() + "Score");
        formerScore = ProfileManager.Instance.GetProfileByName(profileName).totalScore;
        newScore = formerScore + achievedScore;
        formerLevel = ProfileManager.Instance.GetProfileByName(profileName).level;

        ProfileManager.Instance.AddProfileTotalScore(profileName, achievedScore);

        newLevel = ProfileManager.Instance.GetProfileByName(profileName).level;
        LevelUpResult result = AnalyzeScoreChange(formerScore, newScore);
        progressBar = transform.GetChild(5).GetChild(0).gameObject.GetComponent<MPImage>();
        levelText = transform.GetChild(5).GetChild(0).GetChild(0).GetChild(0).gameObject.GetComponent<TextMeshProUGUI>();
        levelText.text = formerLevel.ToString();
        progressBar.fillAmount = result.previousLevelProgressPercent / 100f;  // Set initial fill

        await Task.Delay(TimeSpan.FromSeconds(3.5f));
        if (result.leveledUp)
        {
            levelUpFX.Play();
            ChangeLevel();
        }
        else
        {
            levelFX.Play();
        }
        await AnimateProgressBar(result.previousLevelProgressPercent, result.newLevelProgressPercent, 1.5f);
    }

    public async void ChangeLevel()
    {
        await Task.Delay(TimeSpan.FromSeconds(0.75f));
        particles.Play();
        levelText.text = newLevel.ToString();
    }

    private async Task AnimateProgressBar(float fromPercent, float toPercent, float duration)
    {
        float startFill = fromPercent / 100f;
        float endFill = toPercent / 100f;

        if (endFill < startFill)
        {
            float firstPhaseDuration = duration * 0.5f;
            float secondPhaseDuration = duration * 0.5f;

            // --- Phase 1: EaseInSine to 100% ---
            float elapsed = 0f;
            while (elapsed < firstPhaseDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / firstPhaseDuration);
                float easedT = 1f - Mathf.Cos((t * Mathf.PI) / 2f); // EaseInSine
                progressBar.fillAmount = Mathf.Lerp(startFill, 1f, easedT);
                await Task.Yield();
            }
            progressBar.fillAmount = 1f;

            await Task.Delay(TimeSpan.FromMilliseconds(100));

            progressBar.fillAmount = 0f;

            // --- Phase 2: EaseOutSine from 0% to target ---
            elapsed = 0f;
            while (elapsed < secondPhaseDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / secondPhaseDuration);
                float easedT = Mathf.Sin((t * Mathf.PI) / 2f); // EaseOutSine
                progressBar.fillAmount = Mathf.Lerp(0f, endFill, easedT);
                await Task.Yield();
            }
            progressBar.fillAmount = endFill;
        }
        else
        {
            // Simple fill → still use EaseInOutSine
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float easedT = -(Mathf.Cos(Mathf.PI * t) - 1f) / 2f; // EaseInOutSine
                progressBar.fillAmount = Mathf.Lerp(startFill, endFill, easedT);
                await Task.Yield();
            }
            progressBar.fillAmount = endFill;
        }
    }

    private async void DisplaySequence()
    {
        gameObject.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = PlayerPrefs.GetString("Player" + playerId.ToString() + "Name");
        gameObject.transform.GetChild(1).GetComponent<TextMeshProUGUI>().text = PlayerPrefs.GetInt("Player" + playerId.ToString() + "Score").ToString("#,#");
        gameObject.transform.GetChild(3).GetChild(3).GetComponent<TextMeshProUGUI>().text = "x" + PlayerPrefs.GetInt("Player" + playerId.ToString() + "Perfect").ToString();
        gameObject.transform.GetChild(3).GetChild(4).GetComponent<TextMeshProUGUI>().text = "x" + PlayerPrefs.GetInt("Player" + playerId.ToString() + "Great").ToString();
        gameObject.transform.GetChild(3).GetChild(5).GetComponent<TextMeshProUGUI>().text = "x" + PlayerPrefs.GetInt("Player" + playerId.ToString() + "Meh").ToString();
        if (PlayerPrefs.GetInt("Player" + playerId.ToString() + "Placement") == 0)
        {
            gameObject.transform.GetChild(4).GetComponent<TextMeshProUGUI>().colorGradient = new VertexGradient(new Color(1, 0.847f, 0), new Color(1, 0.847f, 0), new Color(1, 0.569f, 0), new Color(1, 0.569f, 0));
            gameObject.transform.GetChild(4).GetComponent<TextMeshProUGUI>().text = "1st";
        }
        else if (PlayerPrefs.GetInt("Player" + playerId.ToString() + "Placement") == 1)
        {
            gameObject.transform.GetChild(4).GetComponent<TextMeshProUGUI>().colorGradient = new VertexGradient(new Color(1, 1, 1), new Color(1, 1, 1), new Color(0.6886792f, 0.6886792f, 0.6886792f), new Color(0.6886792f, 0.6886792f, 0.6886792f));
            gameObject.transform.GetChild(4).GetComponent<TextMeshProUGUI>().text = "2nd";
        }
        else if (PlayerPrefs.GetInt("Player" + playerId.ToString() + "Placement") == 2)
        {
            gameObject.transform.GetChild(4).GetComponent<TextMeshProUGUI>().colorGradient = new VertexGradient(new Color(1, 0.706f, 0.184f), new Color(1, 0.706f, 0.184f), new Color(0.482f, 0.325f, 0.055f), new Color(0.482f, 0.325f, 0.055f));
            gameObject.transform.GetChild(4).GetComponent<TextMeshProUGUI>().text = "3rd";
        }
        else
        {
            gameObject.transform.GetChild(4).GetComponent<TextMeshProUGUI>().colorGradient = new VertexGradient(new Color(0.482f, 0.482f, 0.482f), new Color(0.482f, 0.482f, 0.482f), new Color(0.18f, 0.18f, 0.18f), new Color(0.18f, 0.18f, 0.18f));
            gameObject.transform.GetChild(4).GetComponent<TextMeshProUGUI>().text = "" + (PlayerPrefs.GetInt("Player" + playerId.ToString() + "Placement") + 1) + "th";
        }
        await Task.Delay(TimeSpan.FromSeconds(2.5f));
        for (int i = 0; i < PlayerPrefs.GetInt("Player" + playerId.ToString() + "Stars"); i++)
        {
            Instantiate(starPrefab, gameObject.transform.GetChild(2));
            starFX.pitch += 0.1f;
            starFX.Play();
            await Task.Delay(TimeSpan.FromMilliseconds(300));
        }
    }
    public void BackToMenu()
    {
        SceneManager.LoadScene("Menu", LoadSceneMode.Single);
    }

    public class LevelUpResult
    {
        public bool leveledUp;
        public int levelsGained;
        public float previousLevelProgressPercent; // % before the score gain
        public float newLevelProgressPercent;      // % after the score gain
    }

    public static LevelUpResult AnalyzeScoreChange(int oldTotalScore, int newTotalScore)
    {
        LevelUpResult result = new LevelUpResult();

        // -------- Calculate old level and % progress --------
        int oldLevel = 1;
        int oldXpForPrevLevels = 0;

        while (oldTotalScore >= oldXpForPrevLevels + GetRequiredXPForLevel(oldLevel))
        {
            oldXpForPrevLevels += GetRequiredXPForLevel(oldLevel);
            oldLevel++;
        }

        int xpIntoOldLevel = oldTotalScore - oldXpForPrevLevels;
        int xpForOldLevel = GetRequiredXPForLevel(oldLevel);
        result.previousLevelProgressPercent = ((float)xpIntoOldLevel / xpForOldLevel) * 100f;

        // -------- Calculate new level and % progress --------
        int newLevel = 1;
        int newXpForPrevLevels = 0;

        while (newTotalScore >= newXpForPrevLevels + GetRequiredXPForLevel(newLevel))
        {
            newXpForPrevLevels += GetRequiredXPForLevel(newLevel);
            newLevel++;
        }

        int xpIntoNewLevel = newTotalScore - newXpForPrevLevels;
        int xpForNewLevel = GetRequiredXPForLevel(newLevel);
        result.newLevelProgressPercent = ((float)xpIntoNewLevel / xpForNewLevel) * 100f;

        // -------- Calculate level difference --------
        result.levelsGained = newLevel - oldLevel;
        result.leveledUp = result.levelsGained > 0;

        return result;
    }

    private static int GetRequiredXPForLevel(int level)
    {
        return Mathf.RoundToInt(1350000f + 500000f * (level - 1));
    }

}
