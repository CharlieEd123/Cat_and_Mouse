using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Updates a UI Slider or Image fill amount based on the HealthSO asset.
/// </summary>
public class HealthUI : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The HealthSO asset that stores the player's health.")]
    [SerializeField] private HealthSO healthData;

    [Tooltip("Optional: Drag your UI Slider here.")]
    [SerializeField] private Slider healthSlider;

    [Tooltip("Optional: Drag your UI Image (Filled) here if you aren't using a Slider.")]
    [SerializeField] private Image healthFillImage;

    [Header("Settings")]
    [Tooltip("The maximum health value used to calculate percentages (e.g., 100).")]
    [SerializeField] private float maxHealth = 100f;

    private void Awake()
    {
        // Force reset the health asset to full the second the game starts
        if (healthData != null)
        {
            healthData.maxHealth = maxHealth;
            healthData.currentHealth = maxHealth;
        }
    }

    private void Update()
    {
        if (healthData == null) return;

        // Calculate health percentage (0 to 1)
        float healthPercent = Mathf.Clamp01(healthData.currentHealth / maxHealth);

        // Update Slider if assigned
        if (healthSlider != null)
        {
            healthSlider.value = healthPercent;
        }

        // Update Image Fill Amount if assigned (ensure Image Type is set to Filled)
        if (healthFillImage != null)
        {
            healthFillImage.fillAmount = healthPercent;
        }
    }
}