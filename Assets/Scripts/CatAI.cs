using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;
using UnityEngine.Serialization;

/// <summary>
/// The cat. A finite state machine with distinct rules per state:
///
///   Patrol      : walks a loop of patrol points (the cushion is one of the stops).
///   Resting     : stands (or SITS, if the stop is the cushion) for a while at each stop, then walks on.
///                 While patrolling or resting it keeps looking for the mouse.
///   Alert       : the cat just SPOTTED the mouse. It freezes and turns to face it for a short moment
///                 (the "!" telegraph), then either chases (still sees it) or investigates (lost it).
///   Investigate : walks (calmly) to the spot where it last saw the mouse, then looks left and right
///                 for a few seconds ("?" telegraph). Spots the mouse again -> Alert. Finds nothing -> Patrol.
///   Chasing     : runs after the mouse. When close it ATTACKS (see below). If the mouse stays out of sight,
///                 or somewhere unreachable (the hiding spot), for loseInterestTime seconds, it investigates.
///   Windup / Recovering : the attack: a telegraphed wind-up, the strike, then a short recovery.
///   Stunned     : a PERFECT block freezes the cat for a moment.
///
/// DETECTION: distance + field-of-view cone + a line-of-sight check (Physics.Linecast) so walls block sight.
/// TELEGRAPHING: optional "!" / "?" icons above the cat's head and a colour-coded vision cone gizmo.
/// NAVIGATION: uses the baked NavMesh, and jumps across NavMeshLinks (off-mesh links) with an arc.
///
/// All attack timings live in the CatAttackData asset, so designers can tweak them in the Editor.
/// </summary>
public class CatAI : MonoBehaviour
{
    public enum State { Patrol, Resting, Alert, Investigate, Chasing, Windup, Recovering, Stunned }

    [Header("Data")]
    [SerializeField] private CatAttackData data;

    [Header("References")]
    [Tooltip("The mouse (its root object).")]
    [SerializeField] private Transform target;
    [Tooltip("Only colliders on these layers can be hit by the cat. Put the mouse on a 'Player' layer and tick it here.")]
    [SerializeField] private LayerMask playerLayers;
    [Tooltip("Optional. The HealthSO asset from Lab 5. If set, the cat's hits subtract from it.")]
    [SerializeField] private HealthSO mouseHealth;

    [Header("Patrol")]
    [Tooltip("Empty objects placed around the house, in the order the cat visits them. " +
             "Each object's blue arrow (Z) is the direction the cat faces while it waits there. " +
             "The cushion is added automatically if it is not in this list.")]
    [SerializeField] private Transform[] patrolPoints;
    [Tooltip("An empty object placed on the cushion. The cat SITS here (sit animation) instead of just standing.")]
    [SerializeField] private Transform homePoint;
    [Tooltip("How long (seconds) the cat waits at a normal patrol point before walking on.")]
    [SerializeField] private float waitAtPoint = 1.5f;
    [Tooltip("How long (seconds) the cat sits on the cushion before walking on.")]
    [SerializeField] private float sitTimeAtHome = 5f;
    [Tooltip("Patrolling is this fraction of the normal chase speed (a calm walk).")]
    [FormerlySerializedAs("returnSpeedMultiplier")]
    [SerializeField, Range(0.2f, 1f)] private float patrolSpeedMultiplier = 0.6f;
    [Tooltip("If the cat can't get any closer to a stop than this (the NavMesh is blocked there, like at the cushion) it still counts as arrived.")]
    [SerializeField] private float homeAcceptRadius = 3f;

    [Header("Awareness (what the cat can see)")]
    [Tooltip("The cat notices the mouse only inside this distance (metres).")]
    [SerializeField] private float sightRange = 10f;
    [Tooltip("The cat notices the mouse only inside this cone in front of it (degrees, total width).")]
    [SerializeField, Range(30f, 360f)] private float fieldOfView = 140f;
    [Tooltip("Once chasing, the cat keeps tracking the mouse out to this multiple of Sight Range, in every direction.")]
    [SerializeField] private float trackingRangeMultiplier = 1.5f;
    [Tooltip("The cat gives up chasing after the mouse has been out of sight, or somewhere unreachable (the hiding spot), this long (seconds).")]
    [SerializeField] private float loseInterestTime = 4f;
    [Tooltip("The cat only checks for walls between it and the mouse with a line at this height above the ground.")]
    [SerializeField] private float sightHeight = 0.3f;

    [Header("Alert and Investigate")]
    [Tooltip("How long (seconds) the cat freezes and stares when it first spots the mouse, before reacting. This is the warning the player gets.")]
    [SerializeField] private float alertTime = 0.7f;
    [Tooltip("Walking to the last place the mouse was seen is this fraction of the normal chase speed.")]
    [SerializeField, Range(0.2f, 1f)] private float investigateSpeedMultiplier = 0.85f;
    [Tooltip("How long (seconds) the cat looks around once it reaches the last-seen spot.")]
    [SerializeField] private float investigateLookTime = 3f;
    [Tooltip("How far (degrees) the cat swings its head to each side while looking around.")]
    [SerializeField] private float lookAroundAngle = 70f;

    [Header("Chasing")]
    [Tooltip("How often (seconds) the cat re-plans where to walk. Lower = more responsive, slightly more work.")]
    [SerializeField] private float repathInterval = 0.2f;
    [Tooltip("When the mouse is somewhere the cat can't reach (behind a wall), the cat stops this many metres " +
             "BEFORE the closest reachable spot. Raise it if the cat's head pokes through the wall.")]
    [SerializeField] private float wallStandOff = 0.8f;

    [Header("Jumping (NavMeshLinks / off-mesh links)")]
    [Tooltip("How fast (metres/second) the cat travels across a link.")]
    [SerializeField] private float jumpSpeed = 6f;
    [Tooltip("How high (metres) the arc of the jump goes.")]
    [SerializeField] private float jumpHeight = 1f;
    [Tooltip("In YOUR CLIP (at normal speed), how many seconds in does the cat leave the ground? " +
             "This is the length of the crouch / get-ready part at the start of the clip.")]
    [SerializeField] private float jumpClipTakeoffTime = 0.3f;
    [Tooltip("Length of your jump animation clip in seconds at normal speed. Used with Jump Speed Float " +
             "to stretch or squash the clip so it exactly fits the real jump.")]
    [SerializeField] private float jumpClipLength = 1f;
    [Tooltip("In YOUR CLIP (at normal speed), how many seconds in do the paws touch down? " +
             "Leave at 0 to use the end of the clip. Set it if the clip keeps stretching or settling after landing.")]
    [SerializeField] private float jumpClipLandTime = 0f;

    [Header("Telegraph icons (optional). Children of the cat above its head; they are shown/hidden automatically.")]
    [Tooltip("Shown during Alert. For example a yellow \"!\".")]
    [SerializeField] private GameObject alertIcon;
    [Tooltip("Shown during Investigate. For example an orange \"?\".")]
    [SerializeField] private GameObject investigateIcon;
    [Tooltip("Shown while chasing and attacking. For example a red \"!!\".")]
    [SerializeField] private GameObject chaseIcon;
    [Tooltip("Tick to place the icons automatically above the top of the cat's visible model every frame. " +
             "This keeps them over the cat's head no matter where they sit in the Hierarchy.")]
    [SerializeField] private bool placeIconsAboveModel = true;
    [Tooltip("Icon height (metres) above the cat's root position, used when Use Model Bounds is OFF. " +
             "This is the simple, reliable way: raise it until the icons sit over the cat's head.")]
    [SerializeField] private float iconHeight = 0.9f;
    [Tooltip("Tick to measure the top of the cat's meshes instead of using Icon Height. " +
             "Leave OFF unless the model's bounds are accurate (animated models often report a bounds that is too tall).")]
    [SerializeField] private bool useModelBounds = false;
    [Tooltip("Only used when Use Model Bounds is ON: how far (metres) above the top of the model the icons float.")]
    [SerializeField] private float iconHeightAboveCat = 0.4f;
    [Tooltip("TEST ONLY: keeps the Alert icon visible all the time so you can see where it sits. Untick when finished.")]
    [SerializeField] private bool testShowAlertIcon = false;

    [Header("Animation (leave Animator empty to skip). Names must match the Animator Controller parameters.")]
    [SerializeField] private Animator animator;
    [Tooltip("Bool: true while the cat is sitting on its cushion.")]
    [SerializeField] private string sittingBool = "Sitting";
    [Tooltip("Float: how fast the cat is moving (metres/second). Drives idle / walk / run.")]
    [SerializeField] private string speedFloat = "Speed";
    [Tooltip("Trigger: fires when the attack starts (the wind-up begins).")]
    [SerializeField] private string windupTrigger = "Attack";
    [Tooltip("OPTIONAL trigger fired at the moment the hit lands. Leave EMPTY if your attack is one single clip.")]
    [SerializeField] private string strikeTrigger = "";
    [Tooltip("OPTIONAL float used as the attack state's Speed Multiplier, so the clip's hit frame lands exactly when the strike happens. Leave empty to skip.")]
    [SerializeField] private string attackSpeedFloat = "AttackSpeed";
    [Tooltip("In your attack clip (at normal speed), how many seconds in does the hit actually land? Used with Attack Speed Float.")]
    [SerializeField] private float attackClipHitTime = 0.6f;
    [Tooltip("Bool: true while the cat is stunned (the sleep animation).")]
    [SerializeField] private string stunnedBool = "Stunned";
    [Tooltip("OPTIONAL trigger fired when the cat jumps across a link. Leave EMPTY to skip.")]
    [SerializeField] private string jumpTrigger = "";
    [Tooltip("OPTIONAL float used as the jump state's Speed Multiplier, so the clip lasts exactly as long as the jump. Leave EMPTY to skip.")]
    [SerializeField] private string jumpSpeedFloat = "";

    [Tooltip("Keeps the cat's model (the object with the Animator) fixed on the cat, so an animation clip can never slide it " +
             "forward. Untick only if you WANT the animation to move the model.")]
    [SerializeField] private bool pinModelToRoot = true;
    [Tooltip("While the jump animation plays, the model is slid this many metres BACKWARD, to cancel the forward stretch " +
             "of the clip so the cat looks like it lands on the landing point. 0 = off. Try 0.3 and adjust.")]
    [SerializeField] private float jumpModelPullback = 0f;
    [Tooltip("The exact name of the jump state in the Animator window (for example 23Jump). Used for Jump Model Pullback.")]
    [SerializeField] private string jumpStateName = "23Jump";

    [Header("Debug")]
    [Tooltip("Tick to print the cat's state changes in the Console.")]
    [SerializeField] private bool debugLogs = false;

    [Header("Events (wire sounds, particles, shader flashes, UI in the Inspector)")]
    [Tooltip("Fires when the cat first spots the mouse (the start of Alert). Good for an alert sound.")]
    public UnityEvent onNoticed;
    [Tooltip("Fires when the cat gives up chasing and goes to investigate.")]
    public UnityEvent onLoseInterest;
    [Tooltip("Fires when the cat starts investigating the last-seen spot.")]
    public UnityEvent onInvestigate;
    [Tooltip("Fires when the cat reaches its cushion and sits down.")]
    public UnityEvent onReturnedHome;
    [Tooltip("Fires when the cat jumps across a link.")]
    public UnityEvent onJump;
    [Tooltip("Fires when the cat lands after a jump.")]
    public UnityEvent onLand;
    [Tooltip("Fires when the wind-up starts: the TELEGRAPH. Good for a warning sound or a colour flash.")]
    public UnityEvent onWindup;
    [Tooltip("Fires at the moment the hit lands.")]
    public UnityEvent onStrike;
    [Tooltip("Fires when the cat actually damages the mouse.")]
    public UnityEvent onMouseHit;
    [Tooltip("Fires only when the mouse takes FULL damage (not blocked at all). Good for hit and hurt sounds.")]
    public UnityEvent onMouseHurt;
    [Tooltip("Fires when the swipe hits nothing. Good for a miss sound.")]
    public UnityEvent onMiss;
    [Tooltip("Fires when the cat gets stunned by a perfect block.")]
    public UnityEvent onStunned;
    [Tooltip("Fires when the stun ends.")]
    public UnityEvent onStunEnd;

    public State CurrentState { get; private set; } = State.Resting;

    private const float StopArriveDistance = 0.3f;

    private NavMeshAgent agent;
    private NavMeshPath pathBuffer;
    private Vector3 chaseGoal;
    private float nextRepathTime;
    private float nextReachCheck;
    private bool targetReachable = true;
    private float unseenTimer;
    private float unreachableTimer;
    private Vector3 lastAgentDestination = Vector3.positiveInfinity;
    private float lastAgentRequestTime;
    private Vector3 stunPosition;
    private Quaternion stunRotation;
    private float blockedTimer;
    private State lastLoggedState = State.Resting;
    private Coroutine routine;
    private float nextAttackTime;

    // Patrol stops (built in Awake from patrolPoints + the cushion).
    private Vector3[] stopWalk;       // where to walk to (snapped onto the NavMesh)
    private Quaternion[] stopRot;     // which way to face when waiting there
    private bool[] stopSits;          // true for the cushion
    private int currentStop;
    private float restTimer;
    private bool restSits;

    // Alert / Investigate.
    private float alertTimer;
    private Vector3 lastSeenPosition;
    private Vector3 investigateGoal;
    private bool lookingAround;
    private float lookTimer;
    private float lookBaseYaw;

    private bool isJumping;
    private Vector3 modelStartLocalPosition;   // where the model sits on the cat (used to stop it drifting)
    private float pullbackWeight;              // 0..1, how much of the jump pullback is applied right now
    private float nextJumpPoseLogTime;
    private Renderer[] bodyRenderers;   // the cat's visible meshes (used to find the top of its head)
    private float nextIconDebugTime;
    private float nextIconWarnTime;

    private void Awake()
    {
        pathBuffer = new NavMeshPath();
        agent = GetComponent<NavMeshAgent>(); // optional: used if the cat has one
        if (agent == null)
            Debug.LogWarning(name + ": no NavMeshAgent, using straight-line chase (walks through walls).");
        else
            agent.autoTraverseOffMeshLink = false;   // we jump across links ourselves (see JumpRoutine)

        if (animator != null)
        {
            // The NavMeshAgent and this script move the cat. Never let an animation clip move it as well.
            animator.applyRootMotion = false;
            modelStartLocalPosition = animator.transform.localPosition;
        }

        BuildPatrolStops();
        CacheBodyRenderers();
        SetIcons(null);
    }

    // Remembers the cat's own meshes (not the icons' meshes) so the icons can float above the real top of its head.
    private void CacheBodyRenderers()
    {
        List<Renderer> list = new List<Renderer>();
        foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
        {
            if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;
            if (IsUnderIcon(r.transform)) continue;
            list.Add(r);
        }
        bodyRenderers = list.ToArray();
    }

    private bool IsUnderIcon(Transform t)
    {
        return (alertIcon != null && t.IsChildOf(alertIcon.transform))
            || (investigateIcon != null && t.IsChildOf(investigateIcon.transform))
            || (chaseIcon != null && t.IsChildOf(chaseIcon.transform));
    }

    private void BuildPatrolStops()
    {
        List<Transform> points = new List<Transform>();
        if (patrolPoints != null)
            foreach (Transform p in patrolPoints)
                if (p != null) points.Add(p);

        if (homePoint != null && !points.Contains(homePoint)) points.Add(homePoint);

        // No points at all: the cat just stays where it starts and sits (the old resting behaviour).
        if (points.Count == 0)
        {
            stopWalk = new[] { transform.position };
            stopRot = new[] { transform.rotation };
            stopSits = new[] { true };
            currentStop = 0;
            restTimer = sitTimeAtHome;
            restSits = true;
            CurrentState = State.Resting;
            return;
        }

        stopWalk = new Vector3[points.Count];
        stopRot = new Quaternion[points.Count];
        stopSits = new bool[points.Count];
        int homeIndex = -1;

        for (int i = 0; i < points.Count; i++)
        {
            Vector3 pos = points[i].position;
            if (NavMesh.SamplePosition(pos, out NavMeshHit hit, 3f, NavMesh.AllAreas))
                pos = hit.position;   // the cat can only walk to a spot on the NavMesh

            stopWalk[i] = pos;
            stopRot[i] = points[i].rotation;
            stopSits[i] = points[i] == homePoint;
            if (stopSits[i]) homeIndex = i;
        }

        // Start the game resting at the cushion if there is one, otherwise at the first stop.
        currentStop = homeIndex >= 0 ? homeIndex : 0;
        restSits = stopSits[currentStop];
        restTimer = restSits ? sitTimeAtHome : waitAtPoint;
        CurrentState = State.Resting;
    }

    private void Update()
    {
        if (data == null || target == null) return;

        // Cross a NavMeshLink (off-mesh link) with a jump, unless the cat is busy attacking or stunned.
        if (!isJumping && CanJump() && agent.isOnOffMeshLink)
            StartCoroutine(JumpRoutine());

        if (isJumping)
        {
            UpdateAnimator();
            return;
        }

        switch (CurrentState)
        {
            case State.Patrol:      UpdatePatrol();      break;
            case State.Resting:     UpdateResting();     break;
            case State.Alert:       UpdateAlert();       break;
            case State.Investigate: UpdateInvestigate(); break;
            case State.Chasing:     UpdateChasing();     break;
            case State.Stunned:     StopMoving();        break;   // frozen (see LateUpdate)
            // Windup / Recovering are handled by coroutines.
        }

        UpdateAnimator();
        UpdateIcons();
        if (debugLogs) DebugReport();
    }

    // While stunned, hold the cat exactly where and how it was when the stun began, whatever else
    // (the agent, animation root motion, a leftover turn) tries to move or rotate it.
    private void LateUpdate()
    {
        if (CurrentState == State.Stunned)
            transform.SetPositionAndRotation(stunPosition, stunRotation);

        // If the model (the object with the Animator) is a child of the cat, hold it in place on the cat,
        // so no animation clip can slide it forward during a jump.
        PinModel();
        DebugJumpPose();

        BillboardIcons();
    }

    // Holds the model on the cat, and (optionally) slides it backward while the jump pose plays,
    // to cancel the forward stretch of the jump animation.
    private void PinModel()
    {
        if (animator == null || animator.transform == transform || animator.transform.parent == null) return;
        if (!pinModelToRoot && jumpModelPullback <= 0f) return;

        Vector3 localPos = modelStartLocalPosition;

        if (jumpModelPullback > 0f)
        {
            float target = IsJumpPoseActive() ? 1f : 0f;
            pullbackWeight = Mathf.MoveTowards(pullbackWeight, target, Time.deltaTime * 8f);   // eases in and out

            Vector3 world = -transform.forward * jumpModelPullback * pullbackWeight;
            localPos += animator.transform.parent.InverseTransformVector(world);
        }

        animator.transform.localPosition = localPos;
    }

    // Debug only: while the jump pose plays, print which clip is really playing and how far the visible
    // cat is from the cat's real position. Helps find what is pushing the model forward.
    private void DebugJumpPose()
    {
        if (!debugLogs || animator == null || bodyRenderers == null) return;
        if (Time.time < nextJumpPoseLogTime || !IsJumpPoseActive()) return;
        nextJumpPoseLogTime = Time.time + 0.25f;

        string clipName = "(none)";
        AnimatorClipInfo[] clips = animator.GetCurrentAnimatorClipInfo(0);
        if (clips.Length > 0 && clips[0].clip != null) clipName = clips[0].clip.name;

        string text = "JUMP POSE, clip '" + clipName + "' | ";
        foreach (Renderer r in bodyRenderers)
        {
            if (r == null) continue;

            text += "'" + r.name + "' centre is " + FlatDistanceTo(r.bounds.center).ToString("F2") + " m from the cat root";

            SkinnedMeshRenderer skinned = r as SkinnedMeshRenderer;
            if (skinned != null && skinned.rootBone != null)
                text += " (its root bone '" + skinned.rootBone.name + "' is " + FlatDistanceTo(skinned.rootBone.position).ToString("F2") + " m away)";

            text += "; ";
        }
        Debug.Log(text, this);
    }

    // True while the Animator is playing (or just blending into) the jump state.
    private bool IsJumpPoseActive()
    {
        if (string.IsNullOrEmpty(jumpStateName)) return isJumping;

        if (animator.GetCurrentAnimatorStateInfo(0).IsName(jumpStateName)) return true;

        return animator.IsInTransition(0) && animator.GetNextAnimatorStateInfo(0).IsName(jumpStateName);
    }

    private bool CanJump()
    {
        return agent != null && agent.enabled && agent.isOnNavMesh
            && CurrentState != State.Windup && CurrentState != State.Recovering && CurrentState != State.Stunned;
    }

    // Prints state changes in the Console.
    private void DebugReport()
    {
        if (CurrentState == lastLoggedState) return;
        Debug.Log(name + ": state -> " + CurrentState, this);
        lastLoggedState = CurrentState;
    }

    // Keeps the Animator's Sitting and Speed parameters in sync with what the cat is doing.
    private void UpdateAnimator()
    {
        if (animator == null) return;

        if (!string.IsNullOrEmpty(sittingBool))
            animator.SetBool(sittingBool, CurrentState == State.Resting && restSits);

        if (!string.IsNullOrEmpty(speedFloat))
        {
            float speed = (agent != null && agent.enabled && agent.isOnNavMesh) ? agent.velocity.magnitude : 0f;
            animator.SetFloat(speedFloat, speed, 0.1f, Time.deltaTime); // small damping = smooth blending
        }
    }

    // ---------------- Telegraph icons ----------------

    private void UpdateIcons()
    {
        // Test switch: always show the alert icon so you can see where it floats.
        if (testShowAlertIcon)
        {
            SetIcons(alertIcon);
            return;
        }

        switch (CurrentState)
        {
            case State.Alert:       SetIcons(alertIcon);       break;
            case State.Investigate: SetIcons(investigateIcon); break;
            case State.Chasing:
            case State.Windup:
            case State.Recovering:  SetIcons(chaseIcon);       break;
            default:                SetIcons(null);            break;
        }
    }

    // Shows only the icon passed in (or none).
    private void SetIcons(GameObject show)
    {
        if (alertIcon != null) alertIcon.SetActive(alertIcon == show);
        if (investigateIcon != null) investigateIcon.SetActive(investigateIcon == show);
        if (chaseIcon != null) chaseIcon.SetActive(chaseIcon == show);
    }

    // Makes the visible icon always face the camera.
    private void BillboardIcons()
    {
        Vector3 above = Vector3.zero;
        bool havePosition = placeIconsAboveModel && TryGetPointAboveModel(out above);

        Camera cam = Camera.main;

        PlaceIcon(alertIcon, cam, havePosition, above);
        PlaceIcon(investigateIcon, cam, havePosition, above);
        PlaceIcon(chaseIcon, cam, havePosition, above);

        if (debugLogs && Time.time >= nextIconDebugTime && IconIsShowing())
        {
            nextIconDebugTime = Time.time + 1f;
            Debug.Log(name + ": cat root " + transform.position + ", wanted icon point " + above + " | "
                      + DescribeIcon(alertIcon) + DescribeIcon(investigateIcon) + DescribeIcon(chaseIcon), this);
        }
    }

    // Debug text: where the icon's empty object is, and where each mesh/text under it is drawn.
    private static string DescribeIcon(GameObject icon)
    {
        if (icon == null || !icon.activeSelf) return "";

        string s = "[" + icon.name + " object at " + icon.transform.position;
        foreach (Renderer r in icon.GetComponentsInChildren<Renderer>())
            s += "; '" + r.name + "' drawn at " + r.bounds.center + " (local pos " + r.transform.localPosition + ")";
        return s + "] ";
    }

    // The point just above the top of the cat's visible model (in world space).
    private bool TryGetPointAboveModel(out Vector3 point)
    {
        point = Vector3.zero;

        // Simple mode (default): a fixed height straight above the cat's root.
        if (!useModelBounds)
        {
            point = transform.position + Vector3.up * iconHeight;
            return true;
        }

        if (bodyRenderers == null) return false;

        bool found = false;
        Bounds bounds = new Bounds();
        foreach (Renderer r in bodyRenderers)
        {
            if (r == null) continue;

            // Ignore any mesh far from the cat (a stray marker, shadow or sphere parented under it)
            // so it can't drag the icons away from the cat's head.
            float away = Vector3.Distance(r.bounds.center, transform.position);
            if (away > 3f)
            {
                if (debugLogs && Time.time >= nextIconWarnTime)
                {
                    nextIconWarnTime = Time.time + 5f;
                    Debug.LogWarning(name + ": ignoring mesh '" + r.name + "' for icon placement, it is "
                                     + away.ToString("F1") + " m from the cat root.", r);
                }
                continue;
            }

            if (!found) { bounds = r.bounds; found = true; }
            else bounds.Encapsulate(r.bounds);
        }

        if (!found)
        {
            // No usable mesh: float above the cat's root object instead.
            point = transform.position + Vector3.up * (1f + iconHeightAboveCat);
            return true;
        }

        point = new Vector3(bounds.center.x, bounds.max.y + iconHeightAboveCat, bounds.center.z);

        if (debugLogs && Time.time >= nextIconDebugTime && IconIsShowing())
        {
            nextIconDebugTime = Time.time + 1f;
            Debug.Log(name + ": cat root at " + transform.position + ", model top at " + bounds.max.y.ToString("F2")
                      + ", icon placed at " + point + " (" + bodyRenderers.Length + " body meshes)", this);
        }
        return true;
    }

    private bool IconIsShowing()
    {
        return (alertIcon != null && alertIcon.activeSelf)
            || (investigateIcon != null && investigateIcon.activeSelf)
            || (chaseIcon != null && chaseIcon.activeSelf);
    }

    private static void PlaceIcon(GameObject icon, Camera cam, bool setPosition, Vector3 position)
    {
        if (icon == null || !icon.activeSelf) return;

        if (setPosition) icon.transform.position = position;
        if (cam != null) icon.transform.rotation = cam.transform.rotation;   // face the camera
    }

    // ---------------- Patrol / Resting ----------------

    private void UpdatePatrol()
    {
        if (NoticedTarget()) { StartAlert(); return; }

        Vector3 destination = stopWalk[currentStop];

        // The agent stops 'stoppingDistance' short of its destination, so count that as "arrived".
        float arriveDistance = StopArriveDistance;
        if (agent != null) arriveDistance = Mathf.Max(arriveDistance, agent.stoppingDistance + 0.25f);

        float distance = FlatDistanceTo(destination);
        bool arrived = distance <= arriveDistance;

        // Something (like the cushion itself) may block the NavMesh, so the cat can't get any closer.
        // If it has stopped moving and is close enough, count that as arrived.
        if (!arrived && HasStalled(0.75f) && distance <= homeAcceptRadius) arrived = true;

        if (arrived)
        {
            StartResting();
            return;
        }

        MoveTo(destination, data.moveSpeed * patrolSpeedMultiplier);
    }

    private void StartResting()
    {
        StopMoving();
        CurrentState = State.Resting;
        restSits = stopSits[currentStop];
        restTimer = restSits ? sitTimeAtHome : waitAtPoint;
        if (restSits) onReturnedHome.Invoke();
    }

    private void UpdateResting()
    {
        StopMoving();

        // Turn to face the way this stop's marker points.
        transform.rotation = Quaternion.RotateTowards(transform.rotation, stopRot[currentStop], data.turnSpeed * 0.5f * Time.deltaTime);

        if (NoticedTarget()) { StartAlert(); return; }

        restTimer -= Time.deltaTime;
        if (restTimer > 0f) return;

        if (stopWalk.Length <= 1)
        {
            restTimer = restSits ? sitTimeAtHome : waitAtPoint;   // only one stop: just keep resting here
            return;
        }

        GoPatrol((currentStop + 1) % stopWalk.Length);
    }

    private void GoPatrol(int stopIndex)
    {
        currentStop = stopIndex;
        CurrentState = State.Patrol;
        blockedTimer = 0f;
        lastAgentDestination = Vector3.positiveInfinity;   // plan a fresh route
    }

    private int NearestStopIndex()
    {
        int best = 0;
        float bestDistance = float.MaxValue;
        for (int i = 0; i < stopWalk.Length; i++)
        {
            float d = FlatDistanceTo(stopWalk[i]);
            if (d < bestDistance) { bestDistance = d; best = i; }
        }
        return best;
    }

    // ---------------- Alert ----------------

    // True if the cat can see the mouse (in range, inside the cone, no wall in the way) AND can reach it.
    // Remembers where it saw it.
    private bool NoticedTarget()
    {
        RefreshReachability();
        if (targetReachable && CanSeeTarget(false))
        {
            lastSeenPosition = target.position;
            return true;
        }
        return false;
    }

    private void StartAlert()
    {
        StopMoving();
        CurrentState = State.Alert;
        alertTimer = 0f;
        onNoticed.Invoke();
    }

    private void UpdateAlert()
    {
        StopMoving();
        FaceTarget();   // stare at the mouse while the "!" shows

        if (CanSeeTarget(true)) lastSeenPosition = target.position;

        alertTimer += Time.deltaTime;
        if (alertTimer < alertTime) return;

        // Time is up: does the cat still see the mouse?
        RefreshReachability();
        if (targetReachable && CanSeeTarget(true))
            StartChasing();
        else
            StartInvestigate(lastSeenPosition);
    }

    // ---------------- Investigate ----------------

    private void StartInvestigate(Vector3 point)
    {
        investigateGoal = point;
        if (NavMesh.SamplePosition(point, out NavMeshHit hit, 2f, NavMesh.AllAreas))
            investigateGoal = hit.position;
        else
            investigateGoal = transform.position;

        CurrentState = State.Investigate;
        lookingAround = false;
        blockedTimer = 0f;
        lastAgentDestination = Vector3.positiveInfinity;
        onInvestigate.Invoke();
    }

    private void UpdateInvestigate()
    {
        // Spotted the mouse again? Back to Alert.
        if (NoticedTarget()) { StartAlert(); return; }

        if (!lookingAround)
        {
            float distance = FlatDistanceTo(investigateGoal);
            bool arrived = distance <= StopArriveDistance + (agent != null ? agent.stoppingDistance : 0f) + 0.25f;

            // If the cat can't get any closer (a wall, the hiding spot) it still counts as arrived.
            if (!arrived && HasStalled(1.5f)) arrived = true;

            if (arrived)
            {
                StopMoving();
                lookingAround = true;
                lookTimer = 0f;
                lookBaseYaw = transform.eulerAngles.y;
                return;
            }

            MoveTo(investigateGoal, data.moveSpeed * investigateSpeedMultiplier);
            return;
        }

        // Look left and right (one smooth sweep each way), then give up and go back to patrolling.
        StopMoving();
        lookTimer += Time.deltaTime;

        float yaw = lookBaseYaw + Mathf.Sin(lookTimer / Mathf.Max(0.1f, investigateLookTime) * Mathf.PI * 2f) * lookAroundAngle;
        Quaternion look = Quaternion.Euler(0f, yaw, 0f);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, look, data.turnSpeed * Time.deltaTime);

        if (lookTimer >= investigateLookTime)
            GoPatrol(NearestStopIndex());
    }

    // ---------------- Chasing ----------------

    private void UpdateChasing()
    {
        // Keep 'targetReachable' fresh even while the cat is standing still next to the wall
        // (Chase() skips its path planning when the mouse is within attack range).
        RefreshReachability();

        // Count how long the mouse has been out of sight, or somewhere the cat can't reach.
        bool canSee = CanSeeTarget(true);
        if (canSee) lastSeenPosition = target.position;

        unseenTimer = canSee ? 0f : unseenTimer + Time.deltaTime;
        unreachableTimer = targetReachable ? 0f : unreachableTimer + Time.deltaTime;

        if (unseenTimer >= loseInterestTime || unreachableTimer >= loseInterestTime)
        {
            // Give up the chase, but go and check the last place the mouse was.
            // If the mouse is somewhere unreachable (hiding), check the closest spot the cat CAN reach.
            Vector3 goal = unreachableTimer >= loseInterestTime ? chaseGoal : lastSeenPosition;
            onLoseInterest.Invoke();
            StartInvestigate(goal);
            return;
        }

        Chase();

        if (FlatDistanceTo(target.position) <= data.attackStartRange
            && Time.time >= nextAttackTime
            && HasLineOfSight())              // no attacking through walls
        {
            routine = StartCoroutine(AttackRoutine());
        }
    }

    private void StartChasing()
    {
        CurrentState = State.Chasing;
        unseenTimer = 0f;
        unreachableTimer = 0f;
        nextRepathTime = 0f;                   // plan a route right away
        lastAgentDestination = Vector3.positiveInfinity;
        chaseGoal = transform.position;
    }

    // ---------------- Seeing the mouse ----------------

    // noticing (tracking = false): must be in range, inside the field of view, and have a clear line.
    // tracking = true: already chasing, so the cat keeps tracking in every direction, out to a longer range.
    private bool CanSeeTarget(bool tracking)
    {
        Vector3 toTarget = target.position - transform.position;
        toTarget.y = 0f;

        float range = tracking ? sightRange * trackingRangeMultiplier : sightRange;
        if (toTarget.magnitude > range) return false;

        if (!tracking && toTarget.sqrMagnitude > 0.01f
            && Vector3.Angle(transform.forward, toTarget) > fieldOfView * 0.5f)
            return false;

        return HasLineOfSight();
    }

    // True if nothing solid (like a wall) is between the cat and the mouse.
    private bool HasLineOfSight()
    {
        Vector3 from = transform.position + Vector3.up * sightHeight;
        Vector3 to = target.position + Vector3.up * sightHeight;

        if (Physics.Linecast(from, to, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore))
        {
            bool hitMouse = hit.transform == target || hit.transform.IsChildOf(target);
            bool hitSelf = hit.transform == transform || hit.transform.IsChildOf(transform);
            return hitMouse || hitSelf;
        }
        return true;
    }

    // Updates 'targetReachable': can the cat actually walk to where the mouse is?
    // (Used while patrolling / resting / investigating. While chasing, GetChaseGoal() updates it.)
    private void RefreshReachability()
    {
        if (Time.time < nextReachCheck) return;
        nextReachCheck = Time.time + repathInterval;

        if (agent == null || !agent.enabled || !agent.isOnNavMesh)
        {
            targetReachable = true;
            return;
        }

        if (!NavMesh.SamplePosition(target.position, out NavMeshHit snap, 2f, NavMesh.AllAreas))
        {
            targetReachable = false;
            return;
        }

        agent.CalculatePath(snap.position, pathBuffer);
        targetReachable = pathBuffer.status == NavMeshPathStatus.PathComplete;
    }

    // ---------------- Attack ----------------

    private IEnumerator AttackRoutine()
    {
        CurrentState = State.Windup;
        StopMoving();

        if (animator != null)
        {
            // Speed the clip up / slow it down so its hit frame lands exactly when windupTime is over.
            if (!string.IsNullOrEmpty(attackSpeedFloat) && data.windupTime > 0.01f)
                animator.SetFloat(attackSpeedFloat, Mathf.Max(0.1f, attackClipHitTime) / data.windupTime);

            if (!string.IsNullOrEmpty(windupTrigger)) animator.SetTrigger(windupTrigger);
        }
        onWindup.Invoke();                       // the telegraph

        float t = 0f;
        while (t < data.windupTime)
        {
            FaceTarget();                        // keeps tracking until the hit lands
            t += Time.deltaTime;
            yield return null;
        }

        bool parried = Strike();
        if (parried)
        {
            StartStun();
            yield break;
        }

        CurrentState = State.Recovering;
        yield return new WaitForSeconds(data.recoveryTime);
        FinishAndResume();
    }

    // Returns true if the mouse perfectly blocked (parried) the hit.
    private bool Strike()
    {
        if (animator != null && !string.IsNullOrEmpty(strikeTrigger)) animator.SetTrigger(strikeTrigger);
        onStrike.Invoke();

        Collider[] hits = Physics.OverlapSphere(HitCenter(), data.hitRadius, playerLayers);

        foreach (Collider hit in hits)
        {
            MouseBlock block = hit.GetComponentInParent<MouseBlock>();
            BlockResult result = block != null ? block.ResolveHit() : BlockResult.None;

            if (result == BlockResult.Perfect)
            {
                Debug.Log("PERFECT BLOCK! Cat stunned.");
                return true;
            }

            float amount = data.damage;
            if (result == BlockResult.Blocked) amount *= block.BlockedDamageMultiplier;

            if (mouseHealth != null) mouseHealth.Subtract(amount);
            onMouseHit.Invoke();
            if (result == BlockResult.None) onMouseHurt.Invoke();
            Debug.Log("Cat hit " + hit.name + " for " + amount + (result == BlockResult.Blocked ? " (blocked)" : ""));
            return false;                        // only hit once per attack
        }

        onMiss.Invoke();
        Debug.Log("Cat attack missed.");
        return false;
    }

    // ---------------- Stun ----------------

    // Public so other things (a trap, a thrown item) can stun the cat too.
    public void StartStun()
    {
        if (routine != null) StopCoroutine(routine);
        routine = StartCoroutine(StunRoutine());
    }

    private IEnumerator StunRoutine()
    {
        CurrentState = State.Stunned;
        StopMoving();
        if (agent != null && agent.enabled && agent.isOnNavMesh) agent.ResetPath();
        stunPosition = transform.position;
        stunRotation = transform.rotation;

        if (animator != null && !string.IsNullOrEmpty(stunnedBool)) animator.SetBool(stunnedBool, true);
        onStunned.Invoke();

        yield return new WaitForSeconds(data.stunDuration);

        if (animator != null && !string.IsNullOrEmpty(stunnedBool)) animator.SetBool(stunnedBool, false);
        onStunEnd.Invoke();
        FinishAndResume();
    }

    private void FinishAndResume()
    {
        nextAttackTime = Time.time + data.attackCooldown;
        CurrentState = State.Chasing;
        unseenTimer = 0f;
        unreachableTimer = 0f;
        routine = null;
        lastAgentDestination = Vector3.positiveInfinity;   // force a fresh route after a stun / attack
        if (agent != null && agent.enabled && agent.isOnNavMesh) agent.isStopped = false;
    }

    // ---------------- Jumping across NavMeshLinks ----------------

    // When the cat's route crosses a NavMeshLink (an off-mesh link), the agent stops on the link and waits.
    // We move it along a little arc to the other end, then tell the agent the link is finished.
    private IEnumerator JumpRoutine()
    {
        isJumping = true;

        OffMeshLinkData link = agent.currentOffMeshLinkData;
        Vector3 start = transform.position;
        Vector3 end = link.endPos + Vector3.up * agent.baseOffset;

        Vector3 flat = end - start;
        flat.y = 0f;
        Quaternion look = flat.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(flat) : transform.rotation;

        // How long the airborne part lasts, and how long the whole jump lasts (crouch + air).
        float duration = Mathf.Max(0.15f, Vector3.Distance(start, end) / Mathf.Max(0.1f, jumpSpeed));

        // In the clip: the crouch lasts 'crouchClip' seconds, then the cat is in the air for the rest.
        float clipLength = Mathf.Max(0.2f, jumpClipLength);
        float crouchClip = Mathf.Clamp(jumpClipTakeoffTime, 0f, clipLength - 0.1f);
        float landClip = jumpClipLandTime > 0f ? Mathf.Clamp(jumpClipLandTime, crouchClip + 0.1f, clipLength) : clipLength;
        float airClip = landClip - crouchClip;

        // Play the clip fast or slow so the AIRBORNE part of the clip lasts exactly as long as the real arc.
        // The crouch part plays at that same speed, so it stays in step with the clip.
        float clipSpeed = airClip / duration;
        float takeoff = crouchClip / clipSpeed;   // real seconds the cat crouches before leaving the ground

        if (animator != null)
        {
            if (!string.IsNullOrEmpty(jumpSpeedFloat))
                animator.SetFloat(jumpSpeedFloat, clipSpeed);

            if (!string.IsNullOrEmpty(jumpTrigger)) animator.SetTrigger(jumpTrigger);
        }
        if (debugLogs) Debug.Log(name + ": JUMP from " + start + " to " + end, this);
        onJump.Invoke();

        // Crouch on the spot (turning toward the landing) before leaving the ground.
        float wait = 0f;
        while (wait < takeoff)
        {
            wait += Time.deltaTime;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, look, data.turnSpeed * 2f * Time.deltaTime);
            yield return null;
        }

        float t = 0f;

        while (t < duration)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / duration);

            Vector3 pos = Vector3.Lerp(start, end, k);
            pos.y += Mathf.Sin(k * Mathf.PI) * jumpHeight;   // the arc
            transform.position = pos;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, look, data.turnSpeed * 2f * Time.deltaTime);
            yield return null;
        }

        transform.position = end;
        agent.CompleteOffMeshLink();   // tells the agent it has crossed; it carries on with its route
        onLand.Invoke();
        isJumping = false;
    }

    // ---------------- Movement ----------------

    private void Chase()
    {
        if (FlatDistanceTo(target.position) <= data.attackStartRange * 0.8f)
        {
            StopMoving();
            FaceTarget();
            return;
        }

        if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            agent.isStopped = false;
            agent.speed = data.moveSpeed;

            // Work out where the cat should walk. If the mouse is somewhere the cat can't reach
            // (like the hiding spot behind the wall), this is the closest reachable spot: the wall.
            if (Time.time >= nextRepathTime)
            {
                chaseGoal = GetChaseGoal();
                nextRepathTime = Time.time + repathInterval;
            }
            SetAgentDestination(chaseGoal);
            return;
        }

        FaceTarget();
        transform.position += transform.forward * data.moveSpeed * Time.deltaTime;
    }

    // Only asks the agent for a new route when the destination really changed (or it has no route).
    // Calling SetDestination every frame restarts the path calculation each time, so for long routes
    // the path stays "pending" forever and the agent never moves.
    private void SetAgentDestination(Vector3 destination)
    {
        bool moved = (destination - lastAgentDestination).sqrMagnitude > 0.04f;   // more than 0.2 m
        bool retry = !agent.hasPath && !agent.pathPending && Time.time - lastAgentRequestTime > 1f;

        if (moved || retry)
        {
            agent.SetDestination(destination);
            lastAgentDestination = destination;
            lastAgentRequestTime = Time.time;
        }
    }

    // Walks toward a point (used for patrolling and investigating).
    private void MoveTo(Vector3 destination, float speed)
    {
        if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            agent.isStopped = false;
            agent.speed = speed;
            SetAgentDestination(destination);
            return;
        }

        Vector3 flat = destination - transform.position;
        flat.y = 0f;
        if (flat.sqrMagnitude < 0.0001f) return;

        Quaternion look = Quaternion.LookRotation(flat);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, look, data.turnSpeed * Time.deltaTime);
        transform.position += transform.forward * speed * Time.deltaTime;
    }

    // True once the agent has been standing still (with a finished route) for this many seconds.
    private bool HasStalled(float seconds)
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh) return false;

        bool stopped = !agent.pathPending && agent.velocity.magnitude < 0.1f;
        blockedTimer = stopped ? blockedTimer + Time.deltaTime : 0f;
        return blockedTimer >= seconds;
    }

    // Decides where the cat should walk to this moment, and records whether the mouse is reachable.
    //  1. The mouse is reachable      -> walk to the mouse.
    //  2. The mouse is NOT reachable  -> walk to the closest spot the cat CAN reach (e.g. the wall).
    private Vector3 GetChaseGoal()
    {
        Vector3 mouse = target.position;

        // Find the nearest point on the NavMesh to the mouse.
        if (!NavMesh.SamplePosition(mouse, out NavMeshHit snap, 2f, NavMesh.AllAreas))
        {
            targetReachable = false;
            return EdgeTowardsMouse(mouse);       // mouse is nowhere near the NavMesh
        }

        agent.CalculatePath(snap.position, pathBuffer);

        if (pathBuffer.status == NavMeshPathStatus.PathComplete)
        {
            targetReachable = true;
            return snap.position;                 // the cat can walk all the way there
        }

        targetReachable = false;

        // Not fully reachable: the last corner of a partial path is the closest the cat can get.
        if (pathBuffer.status == NavMeshPathStatus.PathPartial && pathBuffer.corners.Length > 0)
            return StandOffFromEdge(pathBuffer.corners[pathBuffer.corners.Length - 1]);

        return EdgeTowardsMouse(mouse);
    }

    // Pulls a goal back toward the cat by 'wallStandOff' metres so the cat's body (head, paws)
    // stays clear of the wall instead of its centre point touching it.
    private Vector3 StandOffFromEdge(Vector3 edgePoint)
    {
        Vector3 back = transform.position - edgePoint;
        back.y = 0f;

        float pull = Mathf.Min(wallStandOff, back.magnitude);
        if (pull < 0.01f) return edgePoint;

        Vector3 goal = edgePoint + back.normalized * pull;
        if (NavMesh.SamplePosition(goal, out NavMeshHit hit, 1f, NavMesh.AllAreas))
            return hit.position;

        return edgePoint;
    }

    // Fallback: walk in a straight line toward the mouse until the NavMesh ends (the wall).
    private Vector3 EdgeTowardsMouse(Vector3 mouse)
    {
        if (NavMesh.Raycast(transform.position, mouse, out NavMeshHit edge, NavMesh.AllAreas))
            return StandOffFromEdge(edge.position);

        return transform.position;                // nothing sensible: stay put
    }

    private void StopMoving()
    {
        if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            agent.isStopped = true;
            agent.velocity = Vector3.zero;
        }
    }

    private void FaceTarget()
    {
        Vector3 flat = target.position - transform.position;
        flat.y = 0f;
        if (flat.sqrMagnitude < 0.0001f) return;

        Quaternion look = Quaternion.LookRotation(flat);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, look, data.turnSpeed * Time.deltaTime);
    }

    private float FlatDistanceTo(Vector3 point)
    {
        Vector3 flat = point - transform.position;
        flat.y = 0f;
        return flat.magnitude;
    }

    private Vector3 HitCenter()
    {
        return transform.position + transform.forward * data.hitForwardOffset;
    }

    // ---------------- Debug gizmos ----------------

    // Always drawn in the Scene view (and the Game view if the Gizmos button is on).
    // The vision cone changes colour with the state, so you can SEE what the cat is thinking.
    private void OnDrawGizmos()
    {
        Gizmos.color = ConeColor();

        Vector3 origin = transform.position + Vector3.up * sightHeight;
        float half = fieldOfView * 0.5f;
        Vector3 previous = origin + Quaternion.Euler(0f, -half, 0f) * transform.forward * sightRange;

        Gizmos.DrawLine(origin, previous);
        const int segments = 16;
        for (int i = 1; i <= segments; i++)
        {
            float angle = -half + fieldOfView * i / segments;
            Vector3 next = origin + Quaternion.Euler(0f, angle, 0f) * transform.forward * sightRange;
            Gizmos.DrawLine(previous, next);
            previous = next;
        }
        Gizmos.DrawLine(origin, previous);

        // Green: the patrol route.
        if (patrolPoints != null)
        {
            Gizmos.color = Color.green;
            for (int i = 0; i < patrolPoints.Length; i++)
            {
                if (patrolPoints[i] == null) continue;
                Gizmos.DrawWireSphere(patrolPoints[i].position, 0.3f);

                Transform next = patrolPoints[(i + 1) % patrolPoints.Length];
                if (next != null) Gizmos.DrawLine(patrolPoints[i].position, next.position);
            }
        }

        if (homePoint != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(homePoint.position, 0.4f);
        }
    }

    private Color ConeColor()
    {
        switch (CurrentState)
        {
            case State.Alert:       return Color.yellow;
            case State.Investigate: return new Color(1f, 0.55f, 0f);   // orange
            case State.Chasing:
            case State.Windup:
            case State.Recovering:  return Color.red;
            case State.Stunned:     return Color.gray;
            default:                return Color.cyan;                 // Patrol / Resting: calm
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (data == null) return;

        Gizmos.color = Color.yellow;                       // cat starts attacking inside this
        Gizmos.DrawWireSphere(transform.position, data.attackStartRange);

        Gizmos.color = Color.red;                          // where the hit actually lands
        Gizmos.DrawWireSphere(HitCenter(), data.hitRadius);
    }
}