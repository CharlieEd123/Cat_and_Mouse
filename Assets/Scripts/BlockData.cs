using UnityEngine;

/// <summary>
/// Designer-editable settings for the mouse's block / parry.
/// Create an instance with: Assets > Create > Game > Block Data.
/// </summary>
[CreateAssetMenu(fileName = "BlockData", menuName = "Game/Block Data")]
public class BlockData : ScriptableObject
{
    [Tooltip("PERFECT BLOCK: the hit must land within this many seconds after you pressed block. " +
             "Smaller = harder / more skillful. Larger = more forgiving.")]
    public float parryWindow = 0.25f;

    [Tooltip("A normal (late) block still reduces damage. 0 = no damage, 1 = full damage.")]
    [Range(0f, 1f)] public float blockedDamageMultiplier = 0.25f;

    [Tooltip("The mouse can't hold block longer than this (seconds).")]
    public float maxBlockHold = 1.5f;

    [Tooltip("Wait after lowering your block before you can block again. Stops button-mashing.")]
    public float blockCooldown = 0.6f;
}
