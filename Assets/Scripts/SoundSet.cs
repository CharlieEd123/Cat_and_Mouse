using UnityEngine;

/// <summary>
/// One sound effect "slot": a few clips to pick from, plus volume and pitch ranges so the sound
/// is slightly different every time. Used as a field inside CatAudio and MouseAudio.
/// It makes its own AudioSource, so changing the pitch for one sound never affects another.
/// </summary>
[System.Serializable]
public class SoundSet
{
    [Tooltip("One or more clips. With several, a different one is picked at random each time (never the same twice in a row).")]
    public AudioClip[] clips;

    [Header("Variation")]
    [Range(0f, 1f)] public float volumeMin = 0.85f;
    [Range(0f, 1f)] public float volumeMax = 1f;
    [Range(0.5f, 2f)] public float pitchMin = 0.95f;
    [Range(0.5f, 2f)] public float pitchMax = 1.05f;

    private AudioSource source;
    private int lastIndex = -1;

    // Called once by the owner script in Awake.
    public void Init(GameObject owner, float spatialBlend, float maxDistance)
    {
        source = owner.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = spatialBlend;            // 0 = flat (UI-like), 1 = fully 3D
        source.rolloffMode = AudioRolloffMode.Linear;
        source.maxDistance = maxDistance;
    }

    public void Play()
    {
        if (source == null || clips == null || clips.Length == 0) return;

        int index = Random.Range(0, clips.Length);
        if (clips.Length > 1 && index == lastIndex) index = (index + 1) % clips.Length;
        lastIndex = index;

        AudioClip clip = clips[index];
        if (clip == null) return;

        source.pitch = Random.Range(Mathf.Min(pitchMin, pitchMax), Mathf.Max(pitchMin, pitchMax));
        source.PlayOneShot(clip, Random.Range(Mathf.Min(volumeMin, volumeMax), Mathf.Max(volumeMin, volumeMax)));
    }
}
