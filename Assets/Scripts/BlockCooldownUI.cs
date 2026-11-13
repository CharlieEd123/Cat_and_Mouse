using UnityEngine;
using UnityEngine.UI;

public class BlockCooldownUI : MonoBehaviour
{
    [SerializeField] private MouseBlock mouseBlock;
    [SerializeField] private Image icon;
    [SerializeField] private Image cooldownOverlay;
    [SerializeField] private Color readyColor = Color.white;
    [SerializeField] private Color cooldownColor = new Color(0.5f, 0.5f, 0.5f, 1f);

    private void Awake()
    {
        if (icon == null) icon = GetComponent<Image>();
    }

    private void Update()
    {
        if (mouseBlock == null || cooldownOverlay == null) return;

        float remaining = mouseBlock.CooldownRemaining01;   // 1 = just went on cooldown, 0 = ready
        cooldownOverlay.fillAmount = remaining;
        icon.color = remaining > 0f ? cooldownColor : readyColor;
    }
}