using System.Threading.Tasks;
using UnityEngine;

[RequireComponent(typeof(UnityEngine.UI.RawImage))]
public class MaterialAlphaFade : MonoBehaviour
{
    public float fadeDuration = 1f;

    private Material mat;
    private Coroutine fadeRoutine;

    private void Awake()
    {
        // Get the instantiated material used by the RawImage
        var rawImage = GetComponent<UnityEngine.UI.RawImage>();
        if (rawImage.material != null)
        {
            mat = rawImage.material;
        }
        else
        {
            Debug.LogWarning("MaterialAlphaFade: No material found on RawImage.");
        }
    }

    private void OnEnable()
    {
        if (mat != null)
        {
            if (fadeRoutine != null)
                StopCoroutine(fadeRoutine);

            fadeRoutine = StartCoroutine(FadeAlpha(0f, 1f, fadeDuration));
        }
    }
    private System.Collections.IEnumerator FadeAlpha(float from, float to, float duration)
    {
        float t = 0f;
        mat.SetFloat("_Alpha", from);

        yield return new WaitForSeconds(0.6f);

        while (t < duration)
        {
            t += Time.deltaTime;
            float lerpValue = Mathf.Lerp(from, to, t / duration);
            mat.SetFloat("_Alpha", lerpValue);
            yield return null;
        }

        mat.SetFloat("_Alpha", to);
    }
}
