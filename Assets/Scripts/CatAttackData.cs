using UnityEngine;

/// <summary>
/// Designer-editable settings for the cat's attack. Create an instance with:
/// Assets > Create > Game > Cat Attack Data. No C# changes needed to tweak the feel.
/// </summary>
[CreateAssetMenu(fileName = "CatAttackData", menuName = "Game/Cat Attack Data")]
public class CatAttackData : ScriptableObject
{
    [Header("Movement (used by the cat's simple chase)")]
    public float moveSpeed = 3.5f;
    [Tooltip("Degrees per second the cat turns toward the mouse.")]
    public float turnSpeed = 360f;

    [Header("When the cat starts an attack")]
    [Tooltip("The cat begins its wind-up when the mouse is closer than this (metres).")]
    public float attackStartRange = 1.8f;

    [Header("Timing (seconds)")]
    [Tooltip("The telegraph: time between the cat starting the attack and the hit landing. The mouse's chance to react.")]
    public float windupTime = 0.6f;
    [Tooltip("Time the cat is stuck recovering after a normal attack.")]
    public float recoveryTime = 0.5f;
    [Tooltip("Minimum time between the end of one attack and the start of the next.")]
    public float attackCooldown = 1f;

    [Header("Hitbox (checked at the moment the hit lands)")]
    public float hitRadius = 1.2f;
    [Tooltip("How far in front of the cat the hitbox sphere is centred.")]
    public float hitForwardOffset = 1f;

    [Header("Damage and stun")]
    public float damage = 15f;
    [Tooltip("How long the cat is stunned after a perfectly timed block.")]
    public float stunDuration = 1f;
}
