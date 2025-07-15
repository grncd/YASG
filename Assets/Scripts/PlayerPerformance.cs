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
    private int remoteScore = 0;

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
        if (!local)
        {
            if (string.IsNullOrEmpty(transform.GetChild(4).GetComponent<TextMeshProUGUI>().text))
            {
                remoteScore = 0;
            }else
            {
                string temp = transform.GetChild(4).GetComponent<TextMeshProUGUI>().text.Replace(",", "");
                temp = temp.Replace(".", "");
                remoteScore = int.Parse(temp);
            }
            //Debug.Log(remoteScore);
        }
        if (!LyricsHandler.Instance.songOver)
        {
            UpdatePerformanceBar();
        }
        else
        {
            // wrapping up
            if(PlayerPrefs.GetInt("multiplayer") == 0)
            {
                PlayerPrefs.SetInt(gameObject.name + "Stars", stars);
                PlayerPrefs.SetInt(gameObject.name + "Meh", mehCount);
                PlayerPrefs.SetInt(gameObject.name + "Great", greatCount);
                PlayerPrefs.SetInt(gameObject.name + "Perfect", perfectCount);
            }
        }
        if (LyricsHandler.Instance != null && LyricsHandler.Instance.songOver && local)
        {
            // If the song is over and we haven't sent our final stats yet...
            // (We can use a simple bool flag to ensure this only runs once).
            if (!_finalStatsSent)
            {
                SendFinalStatsToServer();
                _finalStatsSent = true;
            }
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
            if (local)
            {
                scoreAtLineStart = scoreManager._localScore;
            }
            else
            {
                scoreAtLineStart = remoteScore;
            }
        }
        totalPossibleScoreForLine = CalculatePerfectScoreForLine(lineIndex);

        performanceBar.fillAmount = 0f; // Reset instantly
        displayedFillAmount = 0f;
        previousJudge = judge;
    }

    private void CheckStars()
    {
        // 1. Determine the correct current score to use for this check
        float currentScore = 0;
        if (PlayerPrefs.GetInt("multiplayer") == 0)
        {
            // Single Player Mode
            currentScore = scoreManager.score;
        }
        else
        {
            // Multiplayer Mode
            currentScore = local ? scoreManager._localScore : remoteScore;
        }

        // 2. Use the unified 'currentScore' variable for the check
        if (stars < 6)
        {
            // Use a constant for the score-per-star to make it easier to change
            const int SCORE_PER_STAR = 159167;
            if (currentScore > SCORE_PER_STAR * (stars + 1))
            {
                stars++;
                GameObject star = Instantiate(starPrefab, starsLocation);
                fxControl.clip = starFX;
                fxControl.Play();
                star.SetActive(true);
            }
        }
    }

    private bool _finalStatsSent = false;
    private void SendFinalStatsToServer()
    {
        Debug.Log("Song over. Sending final performance stats to server.");
        if (PlayerData.LocalPlayerInstance != null)
        {
            PlayerData.LocalPlayerInstance.RequestUpdatePerformanceStats_ServerRpc(perfectCount, greatCount, mehCount, stars);
        }
    }

    private void UpdatePerformanceBar()
    {
        if (!judgeInt) return; // No need to update if we are not judging

        // 1. Determine the correct current score source for this frame
        float currentScore = 0;
        bool isMultiplayer = PlayerPrefs.GetInt("multiplayer") == 1;

        if (isMultiplayer)
        {
            currentScore = local ? scoreManager._localScore : remoteScore;
        }
        else // Single Player
        {
            currentScore = scoreManager.score;
        }

        // 2. Use 'currentScore' for all logic below
        if (currentScore >= 1000000f && !perfect)
        {
            perfect = true;
            transform.GetChild(4).GetComponent<Animator>().Play("1milli");
            transform.GetChild(4).GetComponent<TextMeshProUGUI>().color = new Color(1f, 0.7905534f, 0f);
            transform.GetChild(4).GetChild(0).GetComponent<MPImage>().color = new Color(1f, 0.7905534f, 0f);
            transform.GetChild(4).GetChild(1).gameObject.SetActive(true);
            foreach (Transform child in transform.GetChild(5))
            {
                child.GetComponent<MPImage>().color = new Color(1f, 0.7905534f, 0f);
                child.GetChild(0).GetComponent<MPImage>().color = new Color(1f, 0.7905534f, 0f);
            }

            fxControl.clip = perfectFX;
            fxControl.Play();
        }

        if (totalPossibleScoreForLine > 0)
        {
            CheckStars(); // CheckStars will now work correctly

            // <<< THE MAIN FIX IS HERE >>>
            // Calculate gained score using the correct source
            float gainedScore = currentScore - scoreAtLineStart;

            // Ensure gainedScore isn't negative, which can happen with network fluctuations
            gainedScore = Mathf.Max(0, gainedScore);

            float performanceRatio = gainedScore / totalPossibleScoreForLine;
            currentRatio = performanceRatio;

            // Smooth transition
            displayedFillAmount = Mathf.Lerp(displayedFillAmount, performanceRatio, Time.deltaTime * smoothingSpeed);
            performanceBar.fillAmount = displayedFillAmount;
        }
    }

    public void Judge()
    {
        if (currentRatio < 0.45f)
        {
            Color temp = new Color(1f, 0.6941f, 0.2784f);
            judgmentGlow.color = temp;
            judgmentText.text = "Meh...";
            judgmentText.color = temp;
            judgmentGlow.GetComponent<Animator>().Play("JudgmentGlow", -1, 0f);
            judgmentText.GetComponent<Animator>().Play("Judgment", -1, 0f);
            mehParticles.Play();
            mehCount++;
        }
        else if (currentRatio < 0.915f)
        {
            Color temp = new Color(91f / 255f, 1f, 71f / 255f);
            judgmentGlow.color = temp;
            judgmentText.text = "Great!";
            judgmentText.color = temp;
            judgmentGlow.GetComponent<Animator>().Play("JudgmentGlow", -1, 0f);
            judgmentText.GetComponent<Animator>().Play("Judgment", -1, 0f);
            greatParticles.Play();
            greatCount++;
        }
        else
        {
            Color temp = new Color(59f / 255f, 1f, 1f);
            judgmentGlow.color = temp;
            judgmentText.text = "Perfect!";
            judgmentText.color = temp;
            judgmentGlow.GetComponent<Animator>().Play("JudgmentGlow", -1, 0f);
            judgmentText.GetComponent<Animator>().Play("Judgment", -1, 0f);
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

        // Assume AudioClipPitchProcessor.Instance exposes the processed audio�s length (the one used to build pitchOverTime)
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
                return (validNoteCount * AudioClipPitchProcessor.Instance.scoreIncrement) * 0.366f;
            }
            else
            {
                return (validNoteCount * AudioClipPitchProcessor.Instance.scoreIncrement) * 0.366f * 0.85f;
            }
        }
    }

}
