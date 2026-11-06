using UnityEngine;

/// <summary>
/// The cat's sound effects. Put this on the cat's root object (the one with CatAI).
/// The public Play... methods are wired to CatAI's events in the Inspector, so the sounds fire
/// at exactly the moment the code event happens. Footsteps play by themselves while the cat moves.
/// </summary>
public class CatAudio : MonoBehaviour
{
    [Header("Sounds")]
    [SerializeField] private SoundSet footstep;
    [Tooltip("Plays when the wind-up starts (the warning).")]
    [SerializeField] private SoundSet windup;
    [Tooltip("Plays at the moment the swipe happens, hit or miss.")]
    [SerializeField] private SoundSet attack;
    [Tooltip("Plays in addition to Attack when the swipe hits nothing.")]
    [SerializeField] private SoundSet miss;
    [Tooltip("Plays when the swipe lands on the mouse and does damage.")]
    [SerializeField] private SoundSet hit;

    [Header("Footsteps")]
    [Tooltip("The cat takes a step sound every this many metres it travels. Smaller = faster steps.")]
    [SerializeField] private float strideLength = 0.9f;
    [Tooltip("The cat must be moving at least this fast (metres/second) to make footsteps.")]
    [SerializeField] private float minSpeed = 0.3f;

    [Header("3D sound")]
    [Range(0f, 1f)]
    [SerializeField] private float spatialBlend = 1f;
    [Tooltip("The cat can't be heard beyond this distance.")]
    [SerializeField] private float maxDistance = 25f;

    private Vector3 lastPosition;
    private float distanceSinceStep;

    private void Awake()
    {
        footstep.Init(gameObject, spatialBlend, maxDistance);
        windup.Init(gameObject, spatialBlend, maxDistance);
        attack.Init(gameObject, spatialBlend, maxDistance);
        miss.Init(gameObject, spatialBlend, maxDistance);
        hit.Init(gameObject, spatialBlend, maxDistance);

        lastPosition = transform.position;
    }

    private void Update()
    {
        Vector3 move = transform.position - lastPosition;
        move.y = 0f;
        lastPosition = transform.position;

        float distance = move.magnitude;
        float speed = distance / Mathf.Max(Time.deltaTime, 0.0001f);

        if (speed < minSpeed || distance > 2f)   // standing still, or teleported
        {
            distanceSinceStep = 0f;
            return;
        }

        distanceSinceStep += distance;
        if (distanceSinceStep >= strideLength)
        {
            distanceSinceStep = 0f;
            footstep.Play();
        }
    }

    // Wire these to CatAI's events in the Inspector.
    public void PlayWindup() { windup.Play(); }
    public void PlayAttack() { attack.Play(); }
    public void PlayMiss()   { miss.Play(); }
    public void PlayHit()    { hit.Play(); }
}
