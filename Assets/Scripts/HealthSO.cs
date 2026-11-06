using UnityEngine;

/// <summary>
/// LAB 5 - ScriptableObject that stores the player's health as a data asset.
/// Create an instance via: Assets > Create > Game > Health.
/// Any script (the trigger, the UI) can reference the SAME asset, so they all see the same value.
/// </summary>
[CreateAssetMenu(fileName = "PlayerHealth", menuName = "Game/Health")]
public class HealthSO : ScriptableObject
{
    // The one field the lab asks for. Set the starting value in the Inspector (e.g. 100).
    public float currentHealth = 100f;

    // The one method the lab asks for: lowers health and clamps it at zero.
    public void Subtract(float amount)
    {
        currentHealth = Mathf.Max(0f, currentHealth - amount);
    }
}
