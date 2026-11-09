using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Visual feedback. Put this on any character (the cat, the mouse, a target...).
/// Calling Flash() makes the character flash a colour and then fade back, by smoothly
/// changing the Shader Graph's _FlashAmount float (and optionally its _FlashColor) from C#.
/// FlashSoft() does the same but only flashes part of the way, for a gentler version.
///
/// Works with characters that have several meshes / several materials: every material that
/// uses the flash shader (has the _FlashAmount property) gets flashed.
/// </summary>
public class HitFlash : MonoBehaviour
{
    [Tooltip("Renderers to flash. Leave EMPTY to automatically use every renderer on this object and its children.")]
    [SerializeField] private Renderer[] targetRenderers;

    [Header("Shader Graph property names (the 'Reference' field in the Graph Inspector)")]
    [SerializeField] private string flashProperty = "_FlashAmount";
    [SerializeField] private string colorProperty = "_FlashColor";

    [Header("Look")]
    [Tooltip("Tick to set the shader's colour from here each flash. Untick to use the colour set on the material.")]
    [SerializeField] private bool overrideColor = true;
    [ColorUsage(false)]
    [SerializeField] private Color flashColor = Color.red;
    [Tooltip("Seconds to go from normal to full flash (small = snappy).")]
    [SerializeField] private float flashUpTime = 0.05f;
    [Tooltip("Seconds to fade from full flash back to normal.")]
    [SerializeField] private float flashDownTime = 0.25f;

    [Header("Soft flash (for FlashSoft)")]
    [Tooltip("How far the soft flash goes: 0 = nothing, 1 = same as a full flash. Lower = softer, paler colour.")]
    [Range(0f, 1f)]
    [SerializeField] private float softFlashAmount = 0.35f;

    private readonly List<Material> materials = new List<Material>();
    private int flashId;
    private int colorId;
    private Coroutine running;

    private void Awake()
    {
        flashId = Shader.PropertyToID(flashProperty);
        colorId = Shader.PropertyToID(colorProperty);

        if (targetRenderers == null || targetRenderers.Length == 0)
            targetRenderers = GetComponentsInChildren<Renderer>();

        foreach (Renderer r in targetRenderers)
        {
            if (r == null) continue;

            // .materials gives THIS character its own copies, so flashing one doesn't flash them all.
            foreach (Material m in r.materials)
            {
                if (m.HasProperty(flashId))
                {
                    m.SetFloat(flashId, 0f);
                    materials.Add(m);
                }
            }
        }

        if (materials.Count == 0)
            Debug.LogWarning(name + ": HitFlash found no material with a '" + flashProperty +
                             "' property. Is the flash material assigned, and is the Reference name correct?");
    }

    // A full flash. Wire this to an event in the Inspector.
    public void Flash()
    {
        StartFlash(1f);
    }

    // A gentler flash (for example a blocked hit). Wire this to an event in the Inspector.
    public void FlashSoft()
    {
        StartFlash(softFlashAmount);
    }

    private void StartFlash(float peak)
    {
        if (running != null) StopCoroutine(running); // restart cleanly if hit again mid-flash

        if (overrideColor)
            foreach (Material m in materials)
                if (m.HasProperty(colorId)) m.SetColor(colorId, flashColor);

        running = StartCoroutine(FlashRoutine(peak));
    }

    private IEnumerator FlashRoutine(float peak)
    {
        yield return Fade(0f, peak, flashUpTime);   // quick flash up
        yield return Fade(peak, 0f, flashDownTime); // smooth fade back down
        running = null;
    }

    private IEnumerator Fade(float from, float to, float duration)
    {
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            SetAmount(Mathf.Lerp(from, to, t / duration));
            yield return null;
        }
        SetAmount(to);
    }

    private void SetAmount(float value)
    {
        foreach (Material m in materials) m.SetFloat(flashId, value);
    }
}