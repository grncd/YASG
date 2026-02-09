using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using UnityEngine;

/// <summary>
/// Self-contained SOFA (Singing-Oriented Forced Aligner) implementation.
/// Provides phoneme-level alignment from audio + transcript using an ONNX model.
/// </summary>
public static class SOFAAligner
{
    #region Data Structures

    [Serializable]
    public struct PhonemeAlignment
    {
        public string phoneme;
        public float startTime;
        public float endTime;
        public float confidence;
    }

    [Serializable]
    public struct WordAlignment
    {
        public string word;
        public float startTime;
        public float endTime;
        public PhonemeAlignment[] phonemes;
    }

    [Serializable]
    public class AlignmentResult
    {
        public WordAlignment[] words;
        public PhonemeAlignment[] phonemes;
        public float totalConfidence;
    }

    #endregion

    #region Config

    // Mel spectrogram parameters (baked into ONNX model, used for frame count calculation)
    private static int _sampleRate = 44100;
    private static int _hopLength = 512;
    private static int _scaleFactor = 4;
    private static int _vocabSize = 51;

    // Post-processing constants
    private const float MinSPLength = 0.1f;

    #endregion

    #region State

    private static InferenceSession _session;
    private static Dictionary<string, string[]> _dictionary;
    private static Dictionary<string, int> _phonemeToId;
    private static Dictionary<int, string> _idToPhoneme;
    private static bool _initialized;

    #endregion

    #region Public API

    /// <summary>
    /// Initialize the SOFA aligner. Loads the ONNX model, dictionary, and config.
    /// Must be called before Align().
    /// </summary>
    public static void Initialize()
    {
        if (_initialized) return;

        LoadConfig();
        LoadDictionary();
        LoadModel();
        _initialized = true;

        Debug.Log($"[SOFAAligner] Initialized. Vocab size: {_vocabSize}, Dictionary entries: {_dictionary.Count}");
    }

    /// <summary>
    /// Align an AudioClip with a transcript to produce phoneme-level timestamps.
    /// </summary>
    public static AlignmentResult Align(AudioClip clip, string transcript)
    {
        if (!_initialized) Initialize();

        float[] samples = new float[clip.samples * clip.channels];
        clip.GetData(samples, 0);

        // Convert to mono if stereo
        if (clip.channels > 1)
        {
            float[] mono = new float[clip.samples];
            for (int i = 0; i < clip.samples; i++)
            {
                float sum = 0f;
                for (int c = 0; c < clip.channels; c++)
                    sum += samples[i * clip.channels + c];
                mono[i] = sum / clip.channels;
            }
            samples = mono;
        }

        return Align(samples, clip.frequency, transcript);
    }

    /// <summary>
    /// Align raw audio samples with a transcript to produce phoneme-level timestamps.
    /// </summary>
    public static AlignmentResult Align(float[] audioSamples, int sampleRate, string transcript)
    {
        if (!_initialized) Initialize();

        // Resample to target sample rate if needed
        float[] waveform = sampleRate != _sampleRate
            ? Resample(audioSamples, sampleRate, _sampleRate)
            : audioSamples;

        // G2P: transcript -> phoneme sequence + IDs
        var (phonemeSeq, phSeqId, wordBoundaryIndices, words, wordPhonemeCounts) = TranscriptToPhonemes(transcript);

        if (phonemeSeq.Length == 0)
        {
            Debug.LogWarning("[SOFAAligner] No phonemes generated from transcript.");
            return new AlignmentResult { words = Array.Empty<WordAlignment>(), phonemes = Array.Empty<PhonemeAlignment>(), totalConfidence = 0f };
        }

        // Calculate number of output frames
        int numFrames = (int)((float)waveform.Length * _scaleFactor / _hopLength + 0.5f);

        // Run ONNX inference
        var (edgeDiff, edgeProb, phProbLog) = RunInference(waveform, numFrames, phSeqId);

        // Viterbi decode
        var (phIdxSeq, phTimeInt, frameConfidence) = Decode(phSeqId, phProbLog, edgeProb);

        // Compute total confidence
        float totalConfidence = ComputeTotalConfidence(frameConfidence);

        // Convert to timestamps
        float frameLength = (float)_hopLength / (_sampleRate * _scaleFactor);
        var phonemeAlignments = ConvertToTimestamps(phIdxSeq, phTimeInt, frameConfidence, edgeDiff, phonemeSeq, frameLength, numFrames);

        // Post-process
        phonemeAlignments = PostProcess(phonemeAlignments, waveform.Length / (float)_sampleRate);

        // Group into words using actual phoneme counts from G2P
        var wordAlignments = GroupIntoWords(phonemeAlignments, wordPhonemeCounts, words);

        // Enforce minimum word durations (redistributes from overly-long neighbors)
        EnforceMinimumWordDurations(wordAlignments);

        // Diagnostic logging - flag words with suspicious durations
        Debug.Log($"[SOFAAligner] === Word Alignment Summary ({wordAlignments.Length} words, {phonemeAlignments.Length} phonemes) ===");
        for (int i = 0; i < wordAlignments.Length; i++)
        {
            var w = wordAlignments[i];
            float dur = w.endTime - w.startTime;
            string flag = "";
            if (dur < 0.05f) flag = " *** TOO SHORT";
            else if (dur > 3.0f) flag = " *** TOO LONG";
            Debug.Log($"  [{i}] \"{w.word}\" {w.startTime:F3}s - {w.endTime:F3}s (dur={dur:F3}s, {w.phonemes.Length}ph){flag}");
        }

        return new AlignmentResult
        {
            words = wordAlignments,
            phonemes = phonemeAlignments,
            totalConfidence = totalConfidence
        };
    }

    /// <summary>
    /// Export alignment result as a Praat TextGrid file.
    /// Creates two tiers: "words" and "phonemes" with full interval coverage.
    /// Use this to review/correct alignments in Praat for fine-tuning training data.
    /// </summary>
    public static void ExportTextGrid(AlignmentResult result, float totalDuration, string filePath)
    {
        // Use InvariantCulture to ensure decimal points (not commas) in all locales
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        var sb = new System.Text.StringBuilder();

        sb.AppendLine("File type = \"ooTextFile\"");
        sb.AppendLine("Object class = \"TextGrid\"");
        sb.AppendLine();
        sb.AppendLine("xmin = 0");
        sb.AppendLine(string.Format(ci, "xmax = {0:F6}", totalDuration));
        sb.AppendLine("tiers? <exists>");
        sb.AppendLine("size = 2");
        sb.AppendLine("item []:");

        // Tier 1: Words
        var wordIntervals = BuildIntervals(
            result.words.Select(w => (w.startTime, w.endTime, w.word)).ToArray(),
            totalDuration);
        WriteTier(sb, 1, "words", totalDuration, wordIntervals);

        // Tier 2: Phonemes
        var phonemeIntervals = BuildIntervals(
            result.phonemes.Select(p => (p.startTime, p.endTime, p.phoneme)).ToArray(),
            totalDuration);
        WriteTier(sb, 2, "phonemes", totalDuration, phonemeIntervals);

        File.WriteAllText(filePath, sb.ToString());
        Debug.Log($"[SOFAAligner] TextGrid exported to: {filePath}");
    }

    private static List<(float start, float end, string text)> BuildIntervals(
        (float start, float end, string text)[] items, float totalDuration)
    {
        var intervals = new List<(float start, float end, string text)>();
        float cursor = 0f;

        foreach (var (start, end, text) in items)
        {
            // Clamp to valid range
            float s = Mathf.Max(cursor, Mathf.Max(0f, start));
            float e = Mathf.Max(s + 0.001f, Mathf.Min(totalDuration, end));

            // Fill gap before this item with silence
            if (s > cursor + 0.001f)
                intervals.Add((cursor, s, ""));

            intervals.Add((s, e, text));
            cursor = e;
        }

        // Fill trailing silence
        if (cursor < totalDuration - 0.001f)
            intervals.Add((cursor, totalDuration, ""));

        return intervals;
    }

    private static void WriteTier(System.Text.StringBuilder sb, int tierIndex, string name,
        float totalDuration, List<(float start, float end, string text)> intervals)
    {
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        sb.AppendLine($"    item [{tierIndex}]:");
        sb.AppendLine($"        class = \"IntervalTier\"");
        sb.AppendLine($"        name = \"{name}\"");
        sb.AppendLine($"        xmin = 0");
        sb.AppendLine(string.Format(ci, "        xmax = {0:F6}", totalDuration));
        sb.AppendLine($"        intervals: size = {intervals.Count}");

        for (int i = 0; i < intervals.Count; i++)
        {
            var (start, end, text) = intervals[i];
            sb.AppendLine($"        intervals [{i + 1}]:");
            sb.AppendLine(string.Format(ci, "            xmin = {0:F6}", start));
            sb.AppendLine(string.Format(ci, "            xmax = {0:F6}", end));
            sb.AppendLine($"            text = \"{text}\"");
        }
    }

    /// <summary>
    /// Release ONNX session resources.
    /// </summary>
    public static void Dispose()
    {
        _session?.Dispose();
        _session = null;
        _initialized = false;
    }

    #endregion

    #region Config & Dictionary Loading

    private static void LoadConfig()
    {
        var configAsset = Resources.Load<TextAsset>("SOFA/config");
        if (configAsset == null)
        {
            Debug.LogError("[SOFAAligner] config.yaml not found in Resources/SOFA/. Using defaults.");
            SetDefaultConfig();
            return;
        }

        _phonemeToId = new Dictionary<string, int>();
        _idToPhoneme = new Dictionary<int, string>();

        string[] lines = configAsset.text.Split('\n');
        bool inVocab = false;
        bool inMelspec = false;

        foreach (string rawLine in lines)
        {
            string line = rawLine.TrimEnd('\r');

            if (line.StartsWith("vocab:"))
            {
                inVocab = true;
                inMelspec = false;
                continue;
            }
            if (line.StartsWith("melspec_config:"))
            {
                inMelspec = true;
                inVocab = false;
                continue;
            }
            if (line.StartsWith("model_config:"))
            {
                inVocab = false;
                inMelspec = false;
                continue;
            }

            if (inMelspec && line.StartsWith("  "))
            {
                string trimmed = line.Trim();
                string[] parts = trimmed.Split(':');
                if (parts.Length == 2)
                {
                    string key = parts[0].Trim();
                    string val = parts[1].Trim();
                    switch (key)
                    {
                        case "sample_rate": int.TryParse(val, out _sampleRate); break;
                        case "hop_length": int.TryParse(val, out _hopLength); break;
                        case "scale_factor": int.TryParse(val, out _scaleFactor); break;
                    }
                }
            }

            if (inVocab && line.StartsWith("  "))
            {
                string trimmed = line.Trim();

                // Parse "<vocab_size>: N"
                if (trimmed.StartsWith("<vocab_size>"))
                {
                    string[] parts = trimmed.Split(':');
                    if (parts.Length == 2)
                        int.TryParse(parts[1].Trim(), out _vocabSize);
                    continue;
                }

                // Parse "phoneme: id" (forward mapping) or "id: phoneme" (reverse mapping)
                // We only need the forward mapping where key is a phoneme string and value is an int
                int colonIdx = trimmed.IndexOf(':');
                if (colonIdx < 0) continue;

                string left = trimmed.Substring(0, colonIdx).Trim().Trim('\'', '"');
                string right = trimmed.Substring(colonIdx + 1).Trim().Trim('\'', '"');

                // Skip empty keys or YAML complex key syntax (? '')
                if (string.IsNullOrEmpty(left) || left == "?") continue;

                // Try forward mapping: "phoneme: id"
                if (int.TryParse(right, out int id) && !int.TryParse(left, out _))
                {
                    // Skip special fallback entries that map to 0 (like AP, pau, etc.)
                    // unless it's SP which is legitimately 0
                    if (left == "SP" || id != 0 || !_phonemeToId.ContainsKey(left))
                    {
                        _phonemeToId[left] = id;
                        if (!_idToPhoneme.ContainsKey(id) || left == "SP")
                            _idToPhoneme[id] = left;
                    }
                }
            }
        }

        Debug.Log($"[SOFAAligner] Config loaded. Sample rate: {_sampleRate}, Hop length: {_hopLength}, Scale factor: {_scaleFactor}, Vocab size: {_vocabSize}, Phonemes mapped: {_phonemeToId.Count}");
    }

    private static void SetDefaultConfig()
    {
        _sampleRate = 44100;
        _hopLength = 512;
        _scaleFactor = 4;
        _vocabSize = 51;
        _phonemeToId = new Dictionary<string, int>
        {
            {"SP", 0}, {"aa", 1}, {"ae", 2}, {"ah", 3}, {"ao", 4}, {"aw", 5},
            {"ax", 6}, {"ay", 7}, {"b", 8}, {"ch", 9}, {"cl", 10}, {"d", 11},
            {"dh", 12}, {"dr", 13}, {"dx", 14}, {"eh", 15}, {"er", 16}, {"ey", 17},
            {"f", 18}, {"g", 19}, {"hh", 20}, {"ih", 21}, {"iy", 22}, {"j", 23},
            {"jh", 24}, {"k", 25}, {"l", 26}, {"m", 27}, {"n", 28}, {"ng", 29},
            {"ow", 30}, {"oy", 31}, {"p", 32}, {"q", 33}, {"r", 34}, {"s", 35},
            {"sh", 36}, {"t", 37}, {"th", 38}, {"tr", 39}, {"ts", 40}, {"uh", 41},
            {"uw", 42}, {"ux", 43}, {"v", 44}, {"vf", 45}, {"w", 46}, {"x", 47},
            {"y", 48}, {"z", 49}, {"zh", 50}
        };
        _idToPhoneme = _phonemeToId.ToDictionary(kv => kv.Value, kv => kv.Key);
    }

    private static void LoadDictionary()
    {
        _dictionary = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        var dictAsset = Resources.Load<TextAsset>("SOFA/dictionary");
        if (dictAsset == null)
        {
            Debug.LogError("[SOFAAligner] dictionary.txt not found in Resources/SOFA/.");
            return;
        }

        string[] lines = dictAsset.text.Split('\n');
        foreach (string rawLine in lines)
        {
            string line = rawLine.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line)) continue;

            int tabIdx = line.IndexOf('\t');
            if (tabIdx < 0) continue;

            string word = line.Substring(0, tabIdx).Trim().ToLowerInvariant();
            string[] phonemes = line.Substring(tabIdx + 1).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (!_dictionary.ContainsKey(word))
                _dictionary[word] = phonemes;
        }
    }

    #endregion

    #region Model Loading

    private static void LoadModel()
    {
        // Try StreamingAssets first (for large files), then Resources
        string modelPath = Path.Combine(Application.streamingAssetsPath, "SOFA", "model.onnx");

        if (!File.Exists(modelPath))
        {
            // Try loading from Resources (will load into memory)
            var modelAsset = Resources.Load<TextAsset>("SOFA/model");
            if (modelAsset != null)
            {
                // Write to temporary file since OnnxRuntime needs a file path
                modelPath = Path.Combine(Application.temporaryCachePath, "sofa_model.onnx");
                File.WriteAllBytes(modelPath, modelAsset.bytes);
                Resources.UnloadAsset(modelAsset);
            }
            else
            {
                Debug.LogError("[SOFAAligner] model.onnx not found in StreamingAssets/SOFA/ or Resources/SOFA/.");
                return;
            }
        }

        var options = new SessionOptions();
        options.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;

        try
        {
            _session = new InferenceSession(modelPath, options);
            Debug.Log($"[SOFAAligner] Model loaded from: {modelPath}");

            // Log model inputs/outputs
            foreach (var input in _session.InputMetadata)
                Debug.Log($"[SOFAAligner] Input: {input.Key} -> {input.Value.ElementDataType} {string.Join("x", input.Value.Dimensions)}");
            foreach (var output in _session.OutputMetadata)
                Debug.Log($"[SOFAAligner] Output: {output.Key} -> {output.Value.ElementDataType} {string.Join("x", output.Value.Dimensions)}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[SOFAAligner] Failed to load model: {e.Message}");
        }
    }

    #endregion

    #region G2P (Grapheme-to-Phoneme)

    private static (string[] phonemeSeq, int[] phSeqId, int[] wordBoundaryIndices, string[] words, int[] wordPhonemeCounts) TranscriptToPhonemes(string transcript)
    {
        // Clean transcript
        string cleaned = Regex.Replace(transcript.ToLowerInvariant(), @"[^\w\s']", " ");
        string[] words = cleaned.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

        var phonemeList = new List<string>();
        var wordBoundaries = new List<int>(); // Index in phonemeList where each word starts
        var validWords = new List<string>();
        var actualPhonemeCounts = new List<int>(); // Actual phonemes emitted per word

        // Start with SP
        phonemeList.Add("SP");

        for (int w = 0; w < words.Length; w++)
        {
            string word = words[w];
            string[] phonemes;

            if (_dictionary.TryGetValue(word, out phonemes))
            {
                wordBoundaries.Add(phonemeList.Count);
                validWords.Add(word);

                int emittedCount = 0;
                foreach (string ph in phonemes)
                {
                    // Map phoneme to vocab, falling back to SP for unknowns
                    if (_phonemeToId.ContainsKey(ph))
                    {
                        phonemeList.Add(ph);
                        emittedCount++;
                    }
                    else
                        Debug.LogWarning($"[SOFAAligner] Unknown phoneme '{ph}' in word '{word}', skipping.");
                }
                actualPhonemeCounts.Add(emittedCount);

                // Add SP between words
                phonemeList.Add("SP");
            }
            else
            {
                // Unknown word - try simple letter-based fallback
                var fallback = LetterFallback(word);
                if (fallback.Length > 0)
                {
                    wordBoundaries.Add(phonemeList.Count);
                    validWords.Add(word);
                    phonemeList.AddRange(fallback);
                    actualPhonemeCounts.Add(fallback.Length);
                    phonemeList.Add("SP");
                }
                else
                {
                    Debug.LogWarning($"[SOFAAligner] Word '{word}' not in dictionary, skipping.");
                }
            }
        }

        // Convert to IDs
        string[] phonemeSeq = phonemeList.ToArray();
        int[] phSeqId = new int[phonemeSeq.Length];
        for (int i = 0; i < phonemeSeq.Length; i++)
        {
            phSeqId[i] = _phonemeToId.TryGetValue(phonemeSeq[i], out int id) ? id : 0;
        }

        return (phonemeSeq, phSeqId, wordBoundaries.ToArray(), validWords.ToArray(), actualPhonemeCounts.ToArray());
    }

    private static readonly Dictionary<char, string> LetterToPhoneme = new Dictionary<char, string>
    {
        {'a', "ae"}, {'b', "b"}, {'c', "k"}, {'d', "d"}, {'e', "eh"},
        {'f', "f"}, {'g', "g"}, {'h', "hh"}, {'i', "ih"}, {'j', "jh"},
        {'k', "k"}, {'l', "l"}, {'m', "m"}, {'n', "n"}, {'o', "ow"},
        {'p', "p"}, {'q', "k"}, {'r', "r"}, {'s', "s"}, {'t', "t"},
        {'u', "ah"}, {'v', "v"}, {'w', "w"}, {'x', "k"}, {'y', "y"},
        {'z', "z"}
    };

    private static string[] LetterFallback(string word)
    {
        var result = new List<string>();
        foreach (char c in word.ToLowerInvariant())
        {
            if (LetterToPhoneme.TryGetValue(c, out string ph) && _phonemeToId.ContainsKey(ph))
                result.Add(ph);
        }
        return result.ToArray();
    }

    #endregion

    #region ONNX Inference

    private static (float[] edgeDiff, float[] edgeProb, float[,] phProbLog) RunInference(float[] waveform, int numFrames, int[] phSeqId)
    {
        // Prepare input tensors
        var waveformTensor = new DenseTensor<float>(waveform, new[] { 1, waveform.Length });
        // num_frames must be a scalar (0-D tensor) - the ONNX model has an internal Unsqueeze
        // that converts scalar -> 1-D for Slice. Passing 1-D makes it 2-D and breaks Slice.
        var numFramesTensor = new DenseTensor<long>(new long[] { numFrames }, Array.Empty<int>());

        long[] phSeqIdLong = new long[phSeqId.Length];
        for (int i = 0; i < phSeqId.Length; i++)
            phSeqIdLong[i] = phSeqId[i];
        var phSeqIdTensor = new DenseTensor<long>(phSeqIdLong, new[] { 1, phSeqId.Length });

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("waveform", waveformTensor),
            NamedOnnxValue.CreateFromTensor("num_frames", numFramesTensor),
            NamedOnnxValue.CreateFromTensor("ph_seq_id", phSeqIdTensor)
        };

        using var results = _session.Run(inputs);

        // Extract outputs as dense tensors and convert to arrays
        var edgeDiffTensor = results.First(r => r.Name == "edge_diff").AsTensor<float>() as DenseTensor<float>;
        var edgeProbTensor = results.First(r => r.Name == "edge_prob").AsTensor<float>() as DenseTensor<float>;
        var phProbLogTensor = results.First(r => r.Name == "ph_prob_log").AsTensor<float>() as DenseTensor<float>;

        // Get dimensions - last dimension is T for edge tensors, (T, vocab) for phProbLog
        var edgeDims = edgeDiffTensor.Dimensions.ToArray();
        var phDims = phProbLogTensor.Dimensions.ToArray();

        int T = edgeDims[edgeDims.Length - 1];
        int vocabDim = phDims[phDims.Length - 1];
        int phT = phDims[phDims.Length - 2]; // Should equal T

        // Use buffer for fast access
        var edgeDiffBuf = edgeDiffTensor.Buffer;
        var edgeProbBuf = edgeProbTensor.Buffer;
        var phProbLogBuf = phProbLogTensor.Buffer;

        // Flatten edge tensors (skip batch dim if present)
        int edgeOffset = edgeDims.Length > 1 ? 0 : 0; // Batch dim is always at start if present
        float[] edgeDiff = new float[T];
        float[] edgeProb = new float[T];

        int startIdx = edgeDiffBuf.Length - T; // Take last T elements (handles batch dim)
        for (int i = 0; i < T; i++)
        {
            edgeDiff[i] = edgeDiffBuf.Span[startIdx + i];
            edgeProb[i] = edgeProbBuf.Span[startIdx + i];
        }

        // Extract ph_prob_log as 2D array (T, vocabDim)
        float[,] phProbLog = new float[phT, vocabDim];
        int phStartIdx = phProbLogBuf.Length - (phT * vocabDim); // Skip batch dim
        for (int t = 0; t < phT; t++)
            for (int v = 0; v < vocabDim; v++)
                phProbLog[t, v] = phProbLogBuf.Span[phStartIdx + t * vocabDim + v];

        Debug.Log($"[SOFAAligner] Inference complete. T={T}, phT={phT}, vocab={vocabDim}");

        return (edgeDiff, edgeProb, phProbLog);
    }

    #endregion

    #region Viterbi Decoder

    private static (int[] phIdxSeq, int[] phTimeInt, float[] frameConfidence) Decode(int[] phSeqId, float[,] phProbLog, float[] edgeProb)
    {
        int T = phProbLog.GetLength(0);
        int S = phSeqId.Length;

        // Index ph_prob_log by ph_seq_id to get prob_log (T, S)
        float[,] probLog = new float[T, S];
        for (int t = 0; t < T; t++)
            for (int s = 0; s < S; s++)
                probLog[t, s] = phProbLog[t, phSeqId[s]];

        // Compute edge probability logs
        float[] edgeProbLog = new float[T];
        float[] notEdgeProbLog = new float[T];
        for (int t = 0; t < T; t++)
        {
            edgeProbLog[t] = MathF.Log(edgeProb[t] + 1e-6f);
            notEdgeProbLog[t] = MathF.Log(1f - edgeProb[t] + 1e-6f);
        }

        // Initialize DP tables
        float[,] dp = new float[T, S];
        int[,] backtrackS = new int[T, S];
        float[] currPhMaxProbLog = new float[S];

        for (int t = 0; t < T; t++)
            for (int s = 0; s < S; s++)
            {
                dp[t, s] = float.NegativeInfinity;
                backtrackS[t, s] = -1;
            }

        for (int s = 0; s < S; s++)
            currPhMaxProbLog[s] = float.NegativeInfinity;

        // Base case
        dp[0, 0] = probLog[0, 0];
        currPhMaxProbLog[0] = probLog[0, 0];
        backtrackS[0, 0] = 0;

        if (phSeqId[0] == 0 && S > 1) // First phoneme is SP, can start from second
        {
            dp[0, 1] = probLog[0, 1];
            currPhMaxProbLog[1] = probLog[0, 1];
            backtrackS[0, 1] = 0;
        }

        // Forward pass
        int prob3PadLen = S >= 2 ? 2 : 1;
        float tsRatio = (float)T / S;

        ForwardPass(T, S, probLog, notEdgeProbLog, edgeProbLog, currPhMaxProbLog, dp, backtrackS, phSeqId, prob3PadLen, tsRatio);

        // Backward trace
        var phIdxList = new List<int>();
        var phTimeList = new List<int>();
        var confidenceList = new List<float>();

        // Determine end state
        int endS;
        if (S >= 2 && dp[T - 1, S - 2] > dp[T - 1, S - 1] && phSeqId[S - 1] == 0)
            endS = S - 2;
        else
            endS = S - 1;

        int currentS = endS;
        for (int t = T - 1; t >= 0; t--)
        {
            confidenceList.Add(dp[t, currentS]);

            if (backtrackS[t, currentS] != 0) // A transition occurred
            {
                phIdxList.Add(currentS);
                phTimeList.Add(t);
                currentS -= backtrackS[t, currentS];
                if (currentS < 0) currentS = 0;
            }
        }

        phIdxList.Reverse();
        phTimeList.Reverse();
        confidenceList.Reverse();

        // Compute frame confidence from DP values
        float[] frameConf = new float[confidenceList.Count];
        frameConf[0] = MathF.Exp(confidenceList[0]);
        for (int i = 1; i < confidenceList.Count; i++)
            frameConf[i] = MathF.Exp(confidenceList[i] - confidenceList[i - 1]);

        return (phIdxList.ToArray(), phTimeList.ToArray(), frameConf);
    }

    private static void ForwardPass(int T, int S, float[,] probLog, float[] notEdgeProbLog, float[] edgeProbLog,
        float[] currPhMaxProbLog, float[,] dp, int[,] backtrackS, int[] phSeqId, int prob3PadLen, float tsRatio)
    {
        for (int t = 1; t < T; t++)
        {
            for (int s = 0; s < S; s++)
            {
                // Transition 1: STAY [t-1,s] -> [t,s]
                float prob1 = dp[t - 1, s] + probLog[t, s] + notEdgeProbLog[t];

                // Transition 2: ADVANCE-1 [t-1,s-1] -> [t,s]
                float prob2 = float.NegativeInfinity;
                if (s >= 1)
                {
                    prob2 = dp[t - 1, s - 1]
                            + probLog[t, s - 1]
                            + edgeProbLog[t]
                            + currPhMaxProbLog[s - 1] * tsRatio;
                }

                // Transition 3: ADVANCE-2 / SKIP SP [t-1,s-2] -> [t,s]
                float prob3 = float.NegativeInfinity;
                if (s >= prob3PadLen)
                {
                    int skippedIdx = s - prob3PadLen + 1;
                    // Can only skip SP (phoneme ID 0)
                    if (skippedIdx < S - 1 && phSeqId[skippedIdx] != 0)
                    {
                        prob3 = float.NegativeInfinity;
                    }
                    else
                    {
                        prob3 = dp[t - 1, s - prob3PadLen]
                                + probLog[t, s - prob3PadLen]
                                + edgeProbLog[t]
                                + currPhMaxProbLog[s - prob3PadLen] * tsRatio;
                    }
                }

                // Select best
                float bestProb = prob1;
                int bestIdx = 0;

                if (prob2 > bestProb)
                {
                    bestProb = prob2;
                    bestIdx = 1;
                }
                if (prob3 > bestProb)
                {
                    bestProb = prob3;
                    bestIdx = 2;
                }

                dp[t, s] = bestProb;
                backtrackS[t, s] = bestIdx;

                // Update running max probability
                if (bestIdx == 0) // STAY
                    currPhMaxProbLog[s] = MathF.Max(currPhMaxProbLog[s], probLog[t, s]);
                else if (bestIdx > 0) // ADVANCE
                    currPhMaxProbLog[s] = probLog[t, s];

                // Reset for SP phonemes
                if (phSeqId[s] == 0)
                    currPhMaxProbLog[s] = 0f;
            }
        }
    }

    #endregion

    #region Timestamp Conversion

    private static PhonemeAlignment[] ConvertToTimestamps(int[] phIdxSeq, int[] phTimeInt, float[] frameConfidence,
        float[] edgeDiff, string[] phonemeSeq, float frameLength, int totalFrames)
    {
        if (phIdxSeq.Length == 0)
            return Array.Empty<PhonemeAlignment>();

        // Convert frame indices to timestamps with edgeDiff sub-frame refinement
        float[] phTimePred = new float[phIdxSeq.Length + 1];
        for (int i = 0; i < phIdxSeq.Length; i++)
        {
            int frameIdx = phTimeInt[i];
            float fractional = 0f;
            if (frameIdx >= 0 && frameIdx < edgeDiff.Length)
                fractional = Mathf.Clamp(edgeDiff[frameIdx], -0.5f, 0.5f);
            phTimePred[i] = frameLength * (frameIdx + fractional);
        }
        phTimePred[phIdxSeq.Length] = frameLength * totalFrames; // End time

        // Enforce monotonicity (each timestamp must be >= previous)
        for (int i = 1; i < phTimePred.Length; i++)
        {
            if (phTimePred[i] < phTimePred[i - 1])
                phTimePred[i] = phTimePred[i - 1] + frameLength * 0.5f;
        }

        // Build alignment array
        var alignments = new List<PhonemeAlignment>();
        for (int i = 0; i < phIdxSeq.Length; i++)
        {
            int seqIdx = phIdxSeq[i];
            if (seqIdx >= 0 && seqIdx < phonemeSeq.Length)
            {
                string ph = phonemeSeq[seqIdx];
                if (ph == "SP") continue; // Skip silence phonemes in output

                float conf = i < frameConfidence.Length ? Mathf.Clamp01(frameConfidence[i]) : 0f;
                alignments.Add(new PhonemeAlignment
                {
                    phoneme = ph,
                    startTime = phTimePred[i],
                    endTime = phTimePred[i + 1],
                    confidence = conf
                });
            }
        }

        return alignments.ToArray();
    }

    private static float ComputeTotalConfidence(float[] frameConfidence)
    {
        if (frameConfidence.Length == 0) return 0f;

        float logSum = 0f;
        int count = 0;
        for (int i = 0; i < frameConfidence.Length; i++)
        {
            if (frameConfidence[i] > 0f)
            {
                logSum += MathF.Log(frameConfidence[i] + 1e-6f);
                count++;
            }
        }

        return count > 0 ? MathF.Exp(logSum / count / 3f) : 0f;
    }

    #endregion

    #region Post-Processing

    private static PhonemeAlignment[] PostProcess(PhonemeAlignment[] alignments, float totalDuration)
    {
        if (alignments.Length <= 1) return alignments;

        var result = new List<PhonemeAlignment>();

        for (int i = 0; i < alignments.Length; i++)
        {
            var current = alignments[i];

            // Fill small gaps: if gap to next phoneme is < MinSPLength, extend current to meet next
            if (i < alignments.Length - 1)
            {
                float gap = alignments[i + 1].startTime - current.endTime;
                if (gap > 0 && gap < MinSPLength)
                {
                    float midpoint = (current.endTime + alignments[i + 1].startTime) / 2f;
                    current.endTime = midpoint;
                    // We'll adjust the next one's start when we process it
                }
            }

            // Adjust start if previous phoneme was extended
            if (result.Count > 0)
            {
                float gap = current.startTime - result[result.Count - 1].endTime;
                if (gap > 0 && gap < MinSPLength)
                {
                    current.startTime = result[result.Count - 1].endTime;
                }
            }

            // Ensure non-negative duration
            if (current.endTime <= current.startTime)
                current.endTime = current.startTime + 0.01f;

            // Clamp to total duration
            current.startTime = Mathf.Max(0f, current.startTime);
            current.endTime = Mathf.Min(totalDuration, current.endTime);

            result.Add(current);
        }

        return result.ToArray();
    }

    #endregion

    #region Word Grouping

    private static WordAlignment[] GroupIntoWords(PhonemeAlignment[] phonemes, int[] wordPhonemeCounts, string[] words)
    {
        if (words.Length == 0 || phonemes.Length == 0)
            return Array.Empty<WordAlignment>();

        var wordAlignments = new List<WordAlignment>();
        int phonemeIdx = 0;

        for (int w = 0; w < words.Length; w++)
        {
            var wordPhonemes = new List<PhonemeAlignment>();

            // Use the actual emitted phoneme count from G2P (not dictionary re-lookup)
            int count = w < wordPhonemeCounts.Length ? wordPhonemeCounts[w] : 1;

            for (int j = 0; j < count && phonemeIdx < phonemes.Length; j++)
            {
                wordPhonemes.Add(phonemes[phonemeIdx]);
                phonemeIdx++;
            }

            if (wordPhonemes.Count > 0)
            {
                wordAlignments.Add(new WordAlignment
                {
                    word = words[w],
                    startTime = wordPhonemes[0].startTime,
                    endTime = wordPhonemes[wordPhonemes.Count - 1].endTime,
                    phonemes = wordPhonemes.ToArray()
                });
            }
        }

        // Add any remaining phonemes to the last word
        if (phonemeIdx < phonemes.Length && wordAlignments.Count > 0)
        {
            var last = wordAlignments[wordAlignments.Count - 1];
            var remaining = new List<PhonemeAlignment>(last.phonemes);
            while (phonemeIdx < phonemes.Length)
            {
                remaining.Add(phonemes[phonemeIdx]);
                phonemeIdx++;
            }
            last.phonemes = remaining.ToArray();
            last.endTime = remaining[remaining.Count - 1].endTime;
            wordAlignments[wordAlignments.Count - 1] = last;
        }

        return wordAlignments.ToArray();
    }

    /// <summary>
    /// Enforce minimum word durations by redistributing time from overly-long neighbors.
    /// Prevents words from being too short to detect during playback.
    /// </summary>
    private static void EnforceMinimumWordDurations(WordAlignment[] words, float minDuration = 0.12f)
    {
        if (words.Length <= 1) return;

        // Multiple passes to handle cascading adjustments
        for (int pass = 0; pass < 3; pass++)
        {
            bool anyChanged = false;

            for (int i = 0; i < words.Length; i++)
            {
                float dur = words[i].endTime - words[i].startTime;
                if (dur >= minDuration) continue;

                float deficit = minDuration - dur;
                anyChanged = true;

                // Calculate how much we can take from each side
                float expandBefore = 0f;
                float expandAfter = 0f;

                if (i > 0)
                {
                    float prevDur = words[i - 1].endTime - words[i - 1].startTime;
                    float prevAvail = Mathf.Max(0f, prevDur - minDuration);
                    expandBefore = Mathf.Min(deficit / 2f, prevAvail);
                }

                if (i < words.Length - 1)
                {
                    float nextDur = words[i + 1].endTime - words[i + 1].startTime;
                    float nextAvail = Mathf.Max(0f, nextDur - minDuration);
                    expandAfter = Mathf.Min(deficit - expandBefore, nextAvail);
                }

                // If after side didn't have enough, try more from before
                if (expandBefore + expandAfter < deficit && i > 0)
                {
                    float prevDur = words[i - 1].endTime - words[i - 1].startTime;
                    float prevAvail = Mathf.Max(0f, prevDur - minDuration);
                    expandBefore = Mathf.Min(deficit - expandAfter, prevAvail);
                }

                if (expandBefore + expandAfter < 0.001f) continue; // Can't expand

                // Apply: shift this word's boundaries
                var w = words[i];
                w.startTime -= expandBefore;
                w.endTime += expandAfter;
                words[i] = w;

                // Shrink previous word's end
                if (i > 0 && expandBefore > 0f)
                {
                    var prev = words[i - 1];
                    prev.endTime = w.startTime;
                    words[i - 1] = prev;
                }

                // Shrink next word's start
                if (i < words.Length - 1 && expandAfter > 0f)
                {
                    var next = words[i + 1];
                    next.startTime = w.endTime;
                    words[i + 1] = next;
                }
            }

            if (!anyChanged) break;
        }
    }

    #endregion

    #region Audio Utilities

    private static float[] Resample(float[] input, int fromRate, int toRate)
    {
        if (fromRate == toRate) return input;

        double ratio = (double)toRate / fromRate;
        int outputLength = (int)(input.Length * ratio);
        float[] output = new float[outputLength];

        for (int i = 0; i < outputLength; i++)
        {
            double srcIndex = i / ratio;
            int srcIndexInt = (int)srcIndex;
            float frac = (float)(srcIndex - srcIndexInt);

            if (srcIndexInt + 1 < input.Length)
                output[i] = input[srcIndexInt] * (1f - frac) + input[srcIndexInt + 1] * frac;
            else if (srcIndexInt < input.Length)
                output[i] = input[srcIndexInt];
        }

        return output;
    }

    #endregion
}
