using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class HealthBarFeedback : MonoBehaviour
{
    [SerializeField] RectTransform bar;   // the HealthBar itself
    [SerializeField] Image outline;       // the Outline child

    [SerializeField] Color normalOutline = new Color32(0x1F, 0x2A, 0x44, 255);
    [SerializeField] Color parryOutline  = new Color32(0xFF, 0xD1, 0x66, 255);
    [SerializeField] Color blockOutline  = new Color32(0x60, 0xA5, 0xFA, 255);

    [SerializeField] float duration = 0.2f;

    Coroutine running;

    public void Play(HitResult result)
    {
        if (running != null) StopCoroutine(running);
        running = StartCoroutine(Punch(result));
    }

    IEnumerator Punch(HitResult result)
    {
        float strength = result == HitResult.Hit ? 0.15f
                       : result == HitResult.Blocked ? 0.06f
                       : 0.10f;

        outline.color = result == HitResult.Parried ? parryOutline
                      : result == HitResult.Blocked ? blockOutline
                      : normalOutline;

        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float k = 1f - t / duration;
            bar.localScale = Vector3.one * (1f + strength * k * k);
            yield return null;
        }

        bar.localScale = Vector3.one;
        outline.color = normalOutline;
        running = null;
    }

    void OnEnable()  => CombatEvents.OnAttackResolved += OnAttack;
    void OnDisable() => CombatEvents.OnAttackResolved -= OnAttack;
    void OnAttack(HitResult result, float damage) => Play(result);
}