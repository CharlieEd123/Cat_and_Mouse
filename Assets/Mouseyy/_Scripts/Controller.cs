using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// LAB 3 - GOAL 2 & GOAL 3
/// Reads WASD / arrow keys (and the gamepad left stick via the Input Actions asset),
/// feeds the result to the Animator blend tree, and moves the character.
/// </summary>
[RequireComponent(typeof(Animator))]
[RequireComponent(typeof(CharacterController))]
public class PlayerMovementInput : MonoBehaviour
{
    [Header("Input")]
    [Tooltip("Drag PlayerControls.inputactions here.")]
    [SerializeField] private InputActionAsset inputActions;
    [SerializeField] private string actionMapName = "Player";
    [SerializeField] private string moveActionName = "Move";

    [Header("Animator Parameters")]
    [SerializeField] private string moveXParam = "MoveX";
    [SerializeField] private string moveYParam = "MoveY";

    [Header("Smoothing")]
    [SerializeField] private float dampTime = 0.1f;

    [Header("Movement")]
    [Tooltip("Only used when the Animator's 'Apply Root Motion' is OFF. " +
             "If your clips already move the character, leave Apply Root Motion on.")]
    [SerializeField] private float moveSpeed = 3f;

    [Tooltip("Speed while holding Left Shift (or pressing the left stick on a gamepad).")]
    [SerializeField] private float runSpeed = 6f;
    [Tooltip("How far out the blend tree is pushed when walking. Set this to match your Walk ring (e.g. 0.5). Running uses 1.")]
    [SerializeField] private float walkBlendScale = 0.5f;

    [Header("Facing")]
    [Tooltip("ON: the character turns to face the direction it moves (A = left, D = right, S = back). OFF: strafes without turning.")]
    [SerializeField] private bool faceMoveDirection = true;
    [Tooltip("How fast the character turns, in degrees per second. Lower = heavier, higher = snappier.")]
    [SerializeField] private float turnSpeed = 720f;

    [Header("Jump")]
    [Tooltip("Name of the Trigger parameter in the Animator that starts the jump animation.")]
    [SerializeField] private string jumpParam = "Jump";
    [Tooltip("Name of the Bool parameter in the Animator that is true while on the ground.")]
    [SerializeField] private string groundedParam = "Grounded";
    [Tooltip("How high the jump goes, in metres.")]
    [SerializeField] private float jumpHeight = 1.2f;
    [Tooltip("Gravity strength (negative). More negative = falls faster, less floaty.")]
    [SerializeField] private float gravity = -20f;

    private InputAction moveAction;
    private Animator animator;
    private CharacterController controller;
    private float verticalVelocity;

    [Header("Dodge Roll")]
    [Tooltip("Name of the Trigger parameter in the Animator that starts the roll animation.")]
    [SerializeField] private string rollParam = "Roll";
    [Tooltip("Speed during the roll. Higher than run speed so it feels like a burst.")]
    [SerializeField] private float rollSpeed = 9f;
    [Tooltip("How long the roll lasts, in seconds. Match this to the length of your roll clip.")]
    [SerializeField] private float rollDuration = 0.6f;
    [Tooltip("Extra wait after a roll before you can roll again.")]
    [SerializeField] private float rollCooldown = 0.4f;

    private bool isRolling;
    private float rollTimer;
    private float rollCooldownTimer;
    private Vector3 rollDirection;

    [Header("Dust Particles")]
    [Tooltip("Drag your dust particle PREFAB here (from the Project window, not the Hierarchy).")]
    [SerializeField] private GameObject dustPrefab;
    [Tooltip("An empty child object placed at the character's feet.")]
    [SerializeField] private Transform feet;
    [Tooltip("Dust appears only when the character moves faster than this (walk is ~3, run is ~6).")]
    [SerializeField] private float speedThreshold = 4f;
    [Tooltip("Seconds between dust puffs while running. Lower = more dust.")]
    [SerializeField] private float dustInterval = 0.25f;
    [SerializeField] private float dustLifetime = 1.5f;

    private float dustTimer;

    [Header("Footstep Sound")]
    [Tooltip("Drag your footstep audio clip here. It loops while you move.")]
    [SerializeField] private AudioClip footstepClip;
    [Tooltip("Volume while walking (0 to 1).")]
    [SerializeField, Range(0f, 1f)] private float walkVolume = 0.3f;
    [Tooltip("Volume while running (0 to 1). Higher than walk volume.")]
    [SerializeField, Range(0f, 1f)] private float runVolume = 0.8f;

    private AudioSource footstepSource;

    [Header("Action Sounds")]
    [SerializeField] private AudioClip jumpClip;
    [SerializeField] private AudioClip landClip;
    [SerializeField] private AudioClip rollClip;
    [Tooltip("Volume of jump, land and roll sounds (0 to 1).")]
    [SerializeField, Range(0f, 1f)] private float sfxVolume = 0.7f;
    [Tooltip("Random pitch change so repeated sounds don't feel robotic. 0.1 = plus or minus 10%.")]
    [SerializeField, Range(0f, 0.3f)] private float pitchVariation = 0.1f;
    [Tooltip("How fast you must be falling for the landing sound to play. Stops tiny bumps from making noise.")]
    [SerializeField] private float landSoundMinFallSpeed = 3f;

    private AudioSource sfxSource;
    private bool wasGrounded = true;

    public Vector2 MoveInput { get; private set; }

    private void Awake()
    {
        animator = GetComponent<Animator>();
        controller = GetComponent<CharacterController>();

        // Footstep audio: use an AudioSource on this object, or add one if missing
        footstepSource = GetComponent<AudioSource>();
        if (footstepSource == null) footstepSource = gameObject.AddComponent<AudioSource>();
        footstepSource.clip = footstepClip;
        footstepSource.loop = true;
        footstepSource.playOnAwake = false;
        footstepSource.spatialBlend = 0f; // 2D sound so volume doesn't depend on camera distance

        // A second AudioSource for one-shot effects (jump, land, roll)
        sfxSource = gameObject.AddComponent<AudioSource>();
        sfxSource.playOnAwake = false;
        sfxSource.loop = false;
        sfxSource.spatialBlend = 0f;

        if (inputActions == null)
        {
            Debug.LogWarning($"{name}: No InputActionAsset assigned. Keyboard (WASD / arrows) will still work.");
            return;
        }

        var map = inputActions.FindActionMap(actionMapName, throwIfNotFound: false);
        if (map == null)
        {
            Debug.LogError($"{name}: Action map \"{actionMapName}\" not found in {inputActions.name}.");
            return;
        }

        moveAction = map.FindAction(moveActionName, throwIfNotFound: false);
        if (moveAction == null)
        {
            Debug.LogError($"{name}: Action \"{moveActionName}\" not found in map \"{actionMapName}\".");
        }
    }

    private void OnEnable()
    {
        moveAction?.Enable();
    }

    private void OnDisable()
    {
        moveAction?.Disable();
    }

    private void Update()
    {
        // 1) Gamepad / whatever is bound in the Input Actions asset
        Vector2 actionValue = moveAction != null ? moveAction.ReadValue<Vector2>() : Vector2.zero;

        // 2) Direct keyboard read: WASD + arrow keys (works even if the asset has no key bindings)
        Vector2 keyboardValue = Vector2.zero;
        Keyboard kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) keyboardValue.x += 1f;
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed)  keyboardValue.x -= 1f;
            if (kb.wKey.isPressed || kb.upArrowKey.isPressed)    keyboardValue.y += 1f;
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed)  keyboardValue.y -= 1f;

            // Stop diagonals from being faster than straight lines
            keyboardValue = Vector2.ClampMagnitude(keyboardValue, 1f);
        }

        // Use the keyboard if a key is held, otherwise use the action (stick)
        MoveInput = keyboardValue != Vector2.zero ? keyboardValue : actionValue;

        // Run = hold Left/Right Shift, or click the gamepad left stick
        bool running = (kb != null && (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed))
                    || (Gamepad.current != null && Gamepad.current.leftStickButton.isPressed);

        // Walking sits on the middle ring of the blend tree, running on the outer ring
        Vector2 animInput = MoveInput * (running ? 1f : walkBlendScale);

        Vector3 direction = new Vector3(MoveInput.x, 0f, MoveInput.y);

        if (faceMoveDirection)
        {
            // Turn the character toward the direction it is moving (A = left, D = right, S = back)
            if (direction.sqrMagnitude > 0.01f && !isRolling)
            {
                Quaternion targetRotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, turnSpeed * Time.deltaTime);
            }

            // Since the character now faces where it walks, the blend tree only needs "forward" amount
            animInput = new Vector2(0f, animInput.magnitude);
        }

        // Feed the result into the Animator blend tree
        animator.SetFloat(moveXParam, animInput.x, dampTime, Time.deltaTime);
        animator.SetFloat(moveYParam, animInput.y, dampTime, Time.deltaTime);

        // ---- Jump + gravity ----
        bool grounded = controller.isGrounded;
        float fallSpeed = -verticalVelocity; // positive number when falling
        if (grounded && verticalVelocity < 0f)
            verticalVelocity = -2f; // small push keeps the character stuck to the ground

        // Landing sound: just touched the ground after a real fall or jump
        if (grounded && !wasGrounded && fallSpeed > landSoundMinFallSpeed)
            PlaySfx(landClip);

        bool jumpPressed = (kb != null && kb.spaceKey.wasPressedThisFrame)
                        || (Gamepad.current != null && Gamepad.current.buttonSouth.wasPressedThisFrame);

        if (jumpPressed && grounded && !isRolling)
        {
            verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
            animator.SetTrigger(jumpParam);
            PlaySfx(jumpClip);
        }

        verticalVelocity += gravity * Time.deltaTime;
        animator.SetBool(groundedParam, grounded);

        // ---- Dodge roll: Left Ctrl / C, or gamepad B (east button) ----
        rollCooldownTimer -= Time.deltaTime;

        bool rollPressed = (kb != null && (kb.leftCtrlKey.wasPressedThisFrame || kb.cKey.wasPressedThisFrame))
                           || (Gamepad.current != null && Gamepad.current.buttonEast.wasPressedThisFrame);

        if (rollPressed && grounded && !isRolling && rollCooldownTimer <= 0f)
        {
            // Roll the way you are holding; if no direction is held, roll forward
            Vector3 worldDir = faceMoveDirection ? direction : transform.TransformDirection(direction);
            rollDirection = worldDir.sqrMagnitude > 0.01f ? worldDir.normalized : transform.forward;
            rollDirection.y = 0f;
            transform.rotation = Quaternion.LookRotation(rollDirection, Vector3.up);

            isRolling = true;
            rollTimer = rollDuration;
            rollCooldownTimer = rollDuration + rollCooldown;
            animator.SetTrigger(rollParam);
            PlaySfx(rollClip);
        }

        if (isRolling)
        {
            rollTimer -= Time.deltaTime;
            if (rollTimer <= 0f) isRolling = false;
        }

        // ---- Move (horizontal is skipped if root motion is already doing it) ----
        Vector3 horizontal = Vector3.zero;
        if (isRolling)
        {
            horizontal = rollDirection * rollSpeed;
        }
        else if (!animator.applyRootMotion)
        {
            float speed = running ? runSpeed : moveSpeed;
            Vector3 worldDir = faceMoveDirection ? direction : transform.TransformDirection(direction);
            horizontal = worldDir * speed;
        }

        Vector3 motion = horizontal + Vector3.up * verticalVelocity;
        controller.Move(motion * Time.deltaTime);

        // ---- Dust: spawn a puff while moving faster than the threshold on the ground ----
        dustTimer -= Time.deltaTime;
        Vector3 flatVelocity = new Vector3(controller.velocity.x, 0f, controller.velocity.z);
        if (grounded && flatVelocity.magnitude > speedThreshold && dustTimer <= 0f)
        {
            SpawnDust();
            dustTimer = dustInterval;
        }

        // ---- Footsteps: same clip for walk and run, louder when running ----
        bool moving = grounded && !isRolling && flatVelocity.magnitude > 0.1f;
        if (footstepSource != null && footstepClip != null)
        {
            if (moving)
            {
                footstepSource.volume = running ? runVolume : walkVolume;
                if (!footstepSource.isPlaying) footstepSource.Play();
            }
            else if (footstepSource.isPlaying)
            {
                footstepSource.Stop();
            }
        }

        wasGrounded = grounded;
    }

    private void PlaySfx(AudioClip clip)
    {
        if (clip == null || sfxSource == null) return;

        sfxSource.pitch = Random.Range(1f - pitchVariation, 1f + pitchVariation);
        sfxSource.PlayOneShot(clip, sfxVolume);
    }

    private void SpawnDust()
    {
        if (dustPrefab == null)
        {
            Debug.LogWarning($"{name}: Dust Prefab slot is empty.");
            return;
        }

        Vector3 position = feet != null ? feet.position : transform.position;
        GameObject dust = Instantiate(dustPrefab, position, Quaternion.identity);
        Destroy(dust, dustLifetime); // clean up so puffs don't pile up in the scene
    }
}