using UnityEngine;
using UnityEngine.UI;

public class HealthBarFeedback : MonoBehaviour
{
    [Header("Targets")]
    [Tooltip("What shakes. Leave empty to shake this object (children, like the label, shake with it).")]
    [SerializeField] private RectTransform shakeTarget;
    [Tooltip("The bar's Fill image, which flashes a colour on hits.")]
    [SerializeField] private Image fillImage;

    [Header("Full hit (big shake)")]
    [SerializeField] private float hurtStrength = 20f;     // pixels
    [SerializeField] private float hurtDuration = 0.45f;
    [SerializeField] private Color hurtColor = new Color(1f, 0.15f, 0.15f);

    [Header("Blocked hit (small shake)")]
    [SerializeField] private float blockedStrength = 6f;
    [SerializeField] private float blockedDuration = 0.2f;
    [SerializeField] private Color blockedColor = new Color(1f, 0.85f, 0.2f);

    private Vector2 origin;
    private Color baseColor = Color.white;
    private Color flashColor;
    private float strength, duration, timer;

    private void Awake()
    {
        if (shakeTarget == null) shakeTarget = (RectTransform)transform;
        origin = shakeTarget.anchoredPosition;

        if (fillImage == null)
        {
            Slider s = GetComponent<Slider>();
            if (s != null && s.fillRect != null) fillImage = s.fillRect.GetComponent<Image>();
        }
        if (fillImage != null) baseColor = fillImage.color;
    }

    // Hook these up to UnityEvents in the Inspector.
    public void PlayHurt()    => Play(hurtStrength, hurtDuration, hurtColor);
    public void PlayBlocked() => Play(blockedStrength, blockedDuration, blockedColor);

    private void Play(float s, float d, Color c)
    {
        // Don't let a small shake cut a big one short.
        if (timer > 0f && s < strength * (timer / duration)) return;

        strength = s;
        duration = d;
        timer = d;
        flashColor = c;
    }

    private void Update()
    {
        if (timer <= 0f) return;

        timer -= Time.deltaTime;
        float t = Mathf.Clamp01(timer / duration);          // 1 -> 0

        shakeTarget.anchoredPosition = origin + Random.insideUnitCircle * strength * t;
        if (fillImage != null) fillImage.color = Color.Lerp(baseColor, flashColor, t);

        if (timer <= 0f)
        {
            shakeTarget.anchoredPosition = origin;
            if (fillImage != null) fillImage.color = baseColor;
        }
    }
}