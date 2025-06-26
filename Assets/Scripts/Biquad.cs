using UnityEngine;

namespace MyAudioProcessing
{
    public class Biquad2
    {
        // Coefficients.
        public float b0, b1, b2, a1, a2;
        // Delay buffers.
        public float x1, x2, y1, y2;

        /// <summary>
        /// Sets up a low-shelf filter.
        /// If gainDB is 0, the filter is bypassed.
        /// shelfSlope controls the steepness (try 0.5 for a gentler boost).
        /// </summary>
        public void SetLowShelf(float sampleRate, float cutoff, float gainDB, float Q, float shelfSlope = 0.5f)
        {
            // Bypass if gain is zero.
            if (Mathf.Approximately(gainDB, 0f))
            {
                b0 = 1; b1 = 0; b2 = 0;
                a1 = 0; a2 = 0;
                return;
            }

            float A = Mathf.Pow(10, gainDB / 40f);
            float w0 = 2 * Mathf.PI * cutoff / sampleRate;
            float cosw0 = Mathf.Cos(w0);
            float sinw0 = Mathf.Sin(w0);
            // Use shelfSlope (S) to control the boost's slope.
            float S = shelfSlope;
            float alpha = sinw0 / 2 * Mathf.Sqrt((A + 1 / A) * (1 / S - 1));

            // Standard RBJ low-shelf formulas.
            float a0 = (A + 1) + (A - 1) * cosw0 + 2 * Mathf.Sqrt(A) * alpha;
            b0 = A * ((A + 1) - (A - 1) * cosw0 + 2 * Mathf.Sqrt(A) * alpha);
            b1 = 2 * A * ((A - 1) - (A + 1) * cosw0);
            b2 = A * ((A + 1) - (A - 1) * cosw0 - 2 * Mathf.Sqrt(A) * alpha);
            a1 = -2 * ((A - 1) + (A + 1) * cosw0);
            a2 = (A + 1) + (A - 1) * cosw0 - 2 * Mathf.Sqrt(A) * alpha;

            // Normalize coefficients.
            b0 /= a0;
            b1 /= a0;
            b2 /= a0;
            a1 /= a0;
            a2 /= a0;
        }

        /// <summary>
        /// Process a single sample through the filter.
        /// If the filter is bypassed, returns the input.
        /// </summary>
        public float Process(float input)
        {
            if (Mathf.Approximately(b0, 1f) &&
                Mathf.Approximately(b1, 0f) &&
                Mathf.Approximately(b2, 0f) &&
                Mathf.Approximately(a1, 0f) &&
                Mathf.Approximately(a2, 0f))
            {
                return input;
            }

            float output = b0 * input + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
            x2 = x1;
            x1 = input;
            y2 = y1;
            y1 = output;
            return output;
        }
    }
}
