using MPUIKIT;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class PlayerPerformance : MonoBehaviour
{
    public Image performanceBar;
    public MPImage judgmentGlow;
    public RealTimePitchDetector scoreManager;
    public ParticleSystem mehParticles;
    public ParticleSystem greatParticles;
    public ParticleSystem perfectParticles;
    public TextMeshProUGUI judgmentText;
    public GameObject starPrefab;
    public Transform starsLocation;
    //[HideInInspector]
    public bool judgeInt = false;
    private AudioSource fxControl;
    public AudioClip starFX;
    public AudioClip perfectFX;
    private bool perfect = false;
    private bool previousJudge = false;

    private float scoreAtLineStart;
    private float totalPossibleScoreForLine;
    private float displayedFillAmount = 0f;
    private float currentRatio = 0f;
    private int stars = 0;
    private int mehCount = 0;
    private int greatCount = 0;
    private int perfectCount = 0;
    private int diffIndex = 1;
    private bool paused = false;
    private bool local;

    // Added to store the start time of the current lyric line
    private float currentLineStartTime = 0f;

    public float smoothingSpeed = 5f;

    void Start()
    {
        scoreManager = GetComponent<RealTimePitchDetector>();
        if(PlayerPrefs.GetInt("multiplayer") == 0)
        {
            diffIndex = PlayerPrefs.GetInt(gameObject.name + "Difficulty");
        }
        else
        {
            diffIndex = PlayerPrefs.GetInt("Player1Difficulty");
        }
        fxControl = this.gameObject.GetComponent<AudioSource>();
        if (LyricsHandler.Instance != null)
        {
            LyricsHandler.Instance.OnNewLyricLine += OnNewLyricLine;
        }
        if (scoreManager.enabled)
        {
            local = true;
        }
        else
        {
            local = false;
        }

    }

    public void Pause()
    {
        paused = !paused;
        if (paused)
        {
            judgeInt = false;
        }
        else
        {
            judgeInt = true;
        }
    }

    void Update()
    {
        if (!LyricsHandler.Instance.songOver)
        {
            UpdatePerformanceBar();
        }
        else
        {
            // wrapping up
            PlayerPrefs.SetInt(gameObject.name + "Stars", stars);
            PlayerPrefs.SetInt(gameObject.name + "Meh", mehCount);
            PlayerPrefs.SetInt(gameObject.name + "Great", greatCount);
            PlayerPrefs.SetInt(gameObject.name + "Perfect", perfectCount);
        }
    }

    private void OnNewLyricLine(int lineIndex, float startTime, bool judge)
    {
        // Save the start time for later use in scoring
        currentLineStartTime = startTime;
        judgeInt = judge;
        Debug.Log(judge);
        if (judge)
        {
            if ((judge && (lineIndex != 0)) && (previousJudge && (lineIndex != 0)))
            {
                Judge();
            }
        }
        else
        {
            if ((judge && (lineIndex != 0)) || (previousJudge && (lineIndex != 0)))
            {
                Judge();
            }
        }
        if(PlayerPrefs.GetInt("multiplayer") == 0)
        {
            scoreAtLineStart = scoreManager.score;
        }
        else
        {
            scoreAtLineStart = scoreManager._localScore;
        }
        totalPossibleScoreForLine = CalculatePerfectScoreForLine(lineIndex);

        performanceBar.fillAmount = 0f; // Reset instantly
        displayedFillAmount = 0f;
        previousJudge = judge;
    }

    private void CheckStars()
    {
        if (PlayerPrefs.GetInt("multiplayer") == 0)
        {
            if (local)
            {
                if (stars < 6)
                {
                    if (scoreManager.score > 159167 * (stars + 1))
                    {
                        stars++;
                        GameObject star = Instantiate(starPrefab, starsLocation);
                        fxControl.clip = starFX;
                        fxControl.Play();
                        star.SetActive(true);
                    }
                }
            }
            else
            {
                if (stars < 6)
                {
                    string scoreText = transform.GetChild(4).GetComponent<TextMeshProUGUI>().text;
                    if (int.Parse(scoreText.Replace(",","")) > 159167 * (stars + 1))
                    {
                        stars++;
                        GameObject star = Instantiate(starPrefab, starsLocation);
                        fxControl.clip = starFX;
                        fxControl.Play();
                        star.SetActive(true);
                    }
                }
            }
        }
        else
        {
            if (stars < 6)
            {
                if (scoreManager._localScore > 159167 * (stars + 1))
                {
                    stars++;
                    GameObject star = Instantiate(starPrefab, starsLocation);
                    fxControl.clip = starFX;
                    fxControl.Play();
                    star.SetActive(true);
                }
            }
        }
        
    }

    private void UpdatePerformanceBar()
    {
        if (PlayerPrefs.GetInt("multiplayer") == 0)
        {
            if (local)
            {
                if (judgeInt)
                {
                    if (scoreManager.score >= 1000000f && !perfect)
                    {
                        perfect = true;
                        transform.GetChild(4).GetComponent<Animator>().Play("1milli");
                        transform.GetChild(4).GetComponent<TextMeshProUGUI>().color = new Color(1f, 0.7100834f, 0f);
                        foreach (Transform child in transform.GetChild(5))
                        {
                            child.GetComponent<MPImage>().color = new Color(1f, 0.7100834f, 0f);
                            child.GetChild(0).GetComponent<MPImage>().color = new Color(1f, 0.7100834f, 0f);
                        }

                        fxControl.clip = perfectFX;
                        fxControl.Play();
                    }
                    if (totalPossibleScoreForLine > 0)
                    {
                        CheckStars();
                        float gainedScore = scoreManager.score - scoreAtLineStart;
                        float performanceRatio = gainedScore / totalPossibleScoreForLine;
                        currentRatio = performanceRatio;
                        // Smooth transition
                        displayedFillAmount = Mathf.Lerp(displayedFillAmount, performanceRatio, Time.deltaTime * smoothingSpeed);
                        performanceBar.fillAmount = displayedFillAmount;
                    }
                }
            }
            else
            {
                string scoreText = transform.GetChild(4).GetComponent<TextMeshProUGUI>().text;
                if (judgeInt)
                {
                    if (int.Parse(scoreText.Replace(",", "")) >= 1000000f && !perfect)
                    {
                        perfect = true;
                        transform.GetChild(4).GetComponent<Animator>().Play("1milli");
                        transform.GetChild(4).GetComponent<TextMeshProUGUI>().color = new Color(1f, 0.7100834f, 0f);
                        foreach (Transform child in transform.GetChild(5))
                        {
                            child.GetComponent<MPImage>().color = new Color(1f, 0.7100834f, 0f);
                            child.GetChild(0).GetComponent<MPImage>().color = new Color(1f, 0.7100834f, 0f);
                        }

                        fxControl.clip = perfectFX;
                        fxControl.Play();
                    }
                    if (totalPossibleScoreForLine > 0)
                    {
                        CheckStars();
                        float gainedScore = int.Parse(scoreText.Replace(",", "")) - scoreAtLineStart;
                        float performanceRatio = gainedScore / totalPossibleScoreForLine;
                        currentRatio = performanceRatio;
                        // Smooth transition
                        displayedFillAmount = Mathf.Lerp(displayedFillAmount, performanceRatio, Time.deltaTime * smoothingSpeed);
                        performanceBar.fillAmount = displayedFillAmount;
                    }
                }
            }
        }
        else
        {
            if (judgeInt)
            {
                if (scoreManager._localScore >= 1000000f && !perfect)
                {
                    perfect = true;
                    transform.GetChild(4).GetComponent<Animator>().Play("1milli");
                    transform.GetChild(4).GetComponent<TextMeshProUGUI>().color = new Color(1f, 0.7100834f, 0f);
                    foreach (Transform child in transform.GetChild(5))
                    {
                        child.GetComponent<MPImage>().color = new Color(1f, 0.7100834f, 0f);
                        child.GetChild(0).GetComponent<MPImage>().color = new Color(1f, 0.7100834f, 0f);
                    }

                    fxControl.clip = perfectFX;
                    fxControl.Play();
                }
                if (totalPossibleScoreForLine > 0)
                {
                    CheckStars();
                    float gainedScore = scoreManager._localScore - scoreAtLineStart;
                    float performanceRatio = gainedScore / totalPossibleScoreForLine;
                    currentRatio = performanceRatio;
                    // Smooth transition
                    displayedFillAmount = Mathf.Lerp(displayedFillAmount, performanceRatio, Time.deltaTime * smoothingSpeed);
                    performanceBar.fillAmount = displayedFillAmount;
                }
            }
        }
        
    }

    public void Judge()
    {
        judgmentGlow.GetComponent<Animator>().StopPlayback();
        judgmentText.GetComponent<Animator>().StopPlayback();
        if (currentRatio < 0.425f)
        {
            Color temp = new Color(1f, 0.6941f, 0.2784f);
            judgmentGlow.color = temp;
            judgmentText.text = "Meh...";
            judgmentText.color = temp;
            judgmentGlow.GetComponent<Animator>().Play("JudgmentGlow");
            judgmentText.GetComponent<Animator>().Play("Judgment");
            mehParticles.Play();
            mehCount++;
        }
        else if (currentRatio < 0.75f)
        {
            Color temp = new Color(91f / 255f, 1f, 71f / 255f);
            judgmentGlow.color = temp;
            judgmentText.text = "Great!";
            judgmentText.color = temp;
            judgmentGlow.GetComponent<Animator>().Play("JudgmentGlow");
            judgmentText.GetComponent<Animator>().Play("Judgment");
            greatParticles.Play();
            greatCount++;
        }
        else
        {
            Color temp = new Color(59f / 255f, 1f, 1f);
            judgmentGlow.color = temp;
            judgmentText.text = "Perfect!";
            judgmentText.color = temp;
            judgmentGlow.GetComponent<Animator>().Play("JudgmentGlow");
            judgmentText.GetComponent<Animator>().Play("Judgment");
            perfectParticles.Play();
            perfectCount++;
        }
    }

    // New version of CalculatePerfectScoreForLine:
    private float CalculatePerfectScoreForLine(int lineIndex)
    {
        // Assume LyricsHandler.Instance.GetLineDuration() returns the duration of this lyric line.
        float lineDuration = LyricsHandler.Instance.GetLineDuration(lineIndex);
        float lineEndTime = currentLineStartTime + lineDuration;

        // Assume AudioClipPitchProcessor.Instance exposes the processed audio’s length (the one used to build pitchOverTime)
        // For example, in AudioClipPitchProcessor you could add:
        // public float ProcessedAudioLength { get { return audioClip.length; } }
        float processedAudioLength = AudioClipPitchProcessor.Instance.ProcessedAudioLength;

        // Also assume AudioClipPitchProcessor.Instance exposes its pitchOverTime list (or provide a getter for it)
        var pitchList = AudioClipPitchProcessor.Instance.pitchOverTime;
        int totalFrames = pitchList.Count;

        // Convert the start and end times into indices within pitchList.
        int startIndex = Mathf.FloorToInt((currentLineStartTime / processedAudioLength) * totalFrames);
        int endIndex = Mathf.FloorToInt((lineEndTime / processedAudioLength) * totalFrames);

        // Clamp indices to valid range.
        startIndex = Mathf.Clamp(startIndex, 0, totalFrames - 1);
        endIndex = Mathf.Clamp(endIndex, 0, totalFrames - 1);

        int validNoteCount = 0;
        for (int i = startIndex; i <= endIndex; i++)
        {
            if (pitchList[i] > 32f && pitchList[i] < 498f)
            {
                validNoteCount++;
            }
        }

        // The perfect score for this line is based on how many frames contained a detected note.
        // Multiply by AudioClipPitchProcessor.Instance.scoreIncrement (or adjust the multiplier as needed).
        if(diffIndex == 0)
        {
            if (!scoreManager.enableAdvancedAntiMonotony)
            {
                return (validNoteCount * AudioClipPitchProcessor.Instance.scoreIncrement) * 0.27f;
            }
            else
            {
                return (validNoteCount * AudioClipPitchProcessor.Instance.scoreIncrement) * 0.27f * 0.85f;
            }
        }else if (diffIndex == 1)
        {
            if (!scoreManager.enableAdvancedAntiMonotony)
            {
                return (validNoteCount * AudioClipPitchProcessor.Instance.scoreIncrement) * 0.34f;
            }
            else
            {
                return (validNoteCount * AudioClipPitchProcessor.Instance.scoreIncrement) * 0.34f * 0.85f;
            }
        }
        else
        {
            if (!scoreManager.enableAdvancedAntiMonotony)
            {
                return (validNoteCount * AudioClipPitchProcessor.Instance.scoreIncrement) * 0.38f;
            }
            else
            {
                return (validNoteCount * AudioClipPitchProcessor.Instance.scoreIncrement) * 0.38f * 0.85f;
            }
        }
    }

}
