using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

public enum BlockResult { None, Blocked, Perfect }

/// <summary>
/// The mouse's block. Press Q / right mouse / gamepad left shoulder to raise the block.
/// If the cat's hit lands within the parry window of the press, it's a PERFECT block (parry).
/// The cat asks this script what happened by calling ResolveHit().
/// Put this on the mouse's root object (the one with the Character Controller).
/// </summary>
public class MouseBlock : MonoBehaviour
{
    [SerializeField] private BlockData data;

    [Header("Optional animation")]
    [SerializeField] private Animator animator;
    [SerializeField] private string blockingBool = "Blocking";

    [Header("Events (wire sounds, particles, UI in the Inspector)")]
    public UnityEvent onBlockStart;
    public UnityEvent onBlockEnd;
    public UnityEvent onBlockedHit;   // a normal (late) block absorbed a hit
    public UnityEvent onParry;        // PERFECT block

    public bool IsBlocking { get; private set; }
    public float BlockedDamageMultiplier => data != null ? data.blockedDamageMultiplier : 0f;

    // 0 to 1: how much of the block cooldown is left (handy for a UI meter later).
    public float CooldownRemaining01 =>
        (data == null || data.blockCooldown <= 0f) ? 0f : Mathf.Clamp01((nextBlockTime - Time.time) / data.blockCooldown);

    private float blockStartTime;
    private float nextBlockTime;

    private void Update()
    {
        if (data == null) return;

        Keyboard kb = Keyboard.current;
        Mouse mouse = Mouse.current;
        Gamepad pad = Gamepad.current;

        bool pressed = (kb != null && kb.qKey.wasPressedThisFrame)
                    || (mouse != null && mouse.rightButton.wasPressedThisFrame)
                    || (pad != null && pad.leftShoulder.wasPressedThisFrame);

        bool held = (kb != null && kb.qKey.isPressed)
                 || (mouse != null && mouse.rightButton.isPressed)
                 || (pad != null && pad.leftShoulder.isPressed);

        if (!IsBlocking)
        {
            if (pressed && Time.time >= nextBlockTime) StartBlock();
        }
        else if (!held || Time.time - blockStartTime >= data.maxBlockHold)
        {
            EndBlock();
        }
    }

    private void StartBlock()
    {
        IsBlocking = true;
        blockStartTime = Time.time;
        if (animator != null) animator.SetBool(blockingBool, true);
        onBlockStart.Invoke();
    }

    private void EndBlock()
    {
        IsBlocking = false;
        nextBlockTime = Time.time + data.blockCooldown;
        if (animator != null) animator.SetBool(blockingBool, false);
        onBlockEnd.Invoke();
    }

    /// <summary>
    /// Called by the cat the moment its hit lands. Decides what the block did.
    /// </summary>
    public BlockResult ResolveHit()
    {
        if (data == null || !IsBlocking) return BlockResult.None;

        if (Time.time - blockStartTime <= data.parryWindow)
        {
            onParry.Invoke();
            return BlockResult.Perfect;
        }

        onBlockedHit.Invoke();
        return BlockResult.Blocked;
    }
}
