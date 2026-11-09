using UnityEngine;

/// <summary>
/// The mouse's sound effects. Put this on the mouse's root object.
/// The public Play... methods are wired to events in the Inspector:
///   CatAI.onMouseHurt   -> PlayHurt
///   MouseBlock.onBlockStart -> PlayBlock
///   MouseBlock.onBlockedHit -> PlayBlockedHit   (optional)
///   MouseBlock.onParry      -> PlayParry        (optional)
/// Any slot you leave without a clip is simply silent.
/// </summary>
public class MouseAudio : MonoBehaviour
{
    [Header("Sounds")]
    [Tooltip("Plays when the cat hits the mouse and it takes full damage.")]
    [SerializeField] private SoundSet hurt;
    [Tooltip("Plays when the mouse raises its block.")]
    [SerializeField] private SoundSet block;
    [Tooltip("Optional: the cat's hit lands on a late block (reduced damage).")]
    [SerializeField] private SoundSet blockedHit;
    [Tooltip("Optional: a PERFECT block (parry).")]
    [SerializeField] private SoundSet parry;

    [Header("3D sound")]
    [Tooltip("0 = the same loudness everywhere (good for the player's own sounds). 1 = fully 3D.")]
    [Range(0f, 1f)]
    [SerializeField] private float spatialBlend = 0f;
    [SerializeField] private float maxDistance = 25f;

    private void Awake()
    {
        hurt.Init(gameObject, spatialBlend, maxDistance);
        block.Init(gameObject, spatialBlend, maxDistance);
        blockedHit.Init(gameObject, spatialBlend, maxDistance);
        parry.Init(gameObject, spatialBlend, maxDistance);
    }

    public void PlayHurt()       { hurt.Play(); }
    public void PlayBlock()      { block.Play(); }
    public void PlayBlockedHit() { blockedHit.Play(); }
    public void PlayParry()      { parry.Play(); }
}
