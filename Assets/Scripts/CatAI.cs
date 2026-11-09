using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;

/// <summary>
/// The cat.
///
///   Resting   : sits at its cushion. Notices the mouse only if it can SEE it (in range, in front, no wall in the way)
///               and can actually reach it.
///   Chasing   : runs after the mouse. Does a telegraphed attack when close (wind-up -> strike -> recovery).
///               A PERFECT block stuns the cat. If the mouse stays out of sight, or stays somewhere the cat
///               can't reach (the hiding spot), for loseInterestTime seconds, the cat gives up.
///   Returning : walks back to its cushion (homePoint), then rests again.
///
/// All attack timings live in the CatAttackData asset, so designers can tweak them in the Editor.
/// </summary>
public class CatAI : MonoBehaviour
{
    public enum State { Resting, Chasing, Windup, Recovering, Stunned, Returning }

    [Header("Data")]
    [SerializeField] private CatAttackData data;

    [Header("References")]
    [Tooltip("The mouse (its root object).")]
    [SerializeField] private Transform target;
    [Tooltip("Only colliders on these layers can be hit by the cat. Put the mouse on a 'Player' layer and tick it here.")]
    [SerializeField] private LayerMask playerLayers;
    [Tooltip("Optional. The HealthSO asset from Lab 5. If set, the cat's hits subtract from it.")]
    [SerializeField] private HealthSO mouseHealth;

    [Header("Home (the cushion)")]
    [Tooltip("An empty object placed on the cushion. Its blue arrow (Z) is the direction the cat faces while resting. " +
             "If left empty, the cat's starting position is used.")]
    [SerializeField] private Transform homePoint;
    [Tooltip("Walking back to the cushion is this fraction of the normal chase speed (calm walk).")]
    [SerializeField, Range(0.2f, 1f)] private float returnSpeedMultiplier = 0.6f;
    [Tooltip("If the cat can't get any closer to the cushion than this (the NavMesh is blocked there) it still counts as home and sits down.")]
    [SerializeField] private float homeAcceptRadius = 3f;

    [Header("Awareness (when the cat notices and gives up)")]
    [Tooltip("The cat notices the mouse only inside this distance (metres).")]
    [SerializeField] private float sightRange = 10f;
    [Tooltip("The cat notices the mouse only inside this cone in front of it (degrees, total width).")]
    [SerializeField, Range(30f, 360f)] private float fieldOfView = 140f;
    [Tooltip("Once chasing, the cat keeps tracking the mouse out to this multiple of Sight Range, in every direction.")]
    [SerializeField] private float trackingRangeMultiplier = 1.5f;
    [Tooltip("The cat gives up after the mouse has been out of sight, or somewhere unreachable (the hiding spot), this long (seconds).")]
    [SerializeField] private float loseInterestTime = 4f;
    [Tooltip("The cat only checks for walls between it and the mouse with a line at this height above the ground.")]
    [SerializeField] private float sightHeight = 0.3f;

    [Header("Chasing")]
    [Tooltip("How often (seconds) the cat re-plans where to walk. Lower = more responsive, slightly more work.")]
    [SerializeField] private float repathInterval = 0.2f;
    [Tooltip("When the mouse is somewhere the cat can't reach (behind a wall), the cat stops this many metres " +
             "BEFORE the closest reachable spot. Raise it if the cat's head pokes through the wall.")]
    [SerializeField] private float wallStandOff = 0.8f;

    [Header("Animation (leave Animator empty to skip). Names must match the Animator Controller parameters.")]
    [SerializeField] private Animator animator;
    [Tooltip("Bool: true while the cat is resting on its cushion.")]
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

    [Header("Debug")]
    [Tooltip("Tick to print the cat's state changes and, while returning, its NavMeshAgent details in the Console.")]
    [SerializeField] private bool debugLogs = false;

    [Header("Events (wire sounds, particles, shader flashes, UI in the Inspector)")]
    [Tooltip("Fires when the cat first notices the mouse and starts chasing. Good for an alert sound.")]
    public UnityEvent onNoticed;
    [Tooltip("Fires when the cat gives up and heads back to its cushion.")]
    public UnityEvent onLoseInterest;
    [Tooltip("Fires when the cat reaches its cushion.")]
    public UnityEvent onReturnedHome;
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

    private const float HomeArriveDistance = 0.3f;

    private NavMeshAgent agent;
    private NavMeshPath pathBuffer;
    private Vector3 chaseGoal;
    private float nextRepathTime;
    private float nextReachCheck;
    private bool targetReachable = true;
    private float unseenTimer;
    private float unreachableTimer;
    private Vector3 homePosition;
    private Vector3 homeWalkPosition;
    private Vector3 lastAgentDestination = Vector3.positiveInfinity;
    private float lastAgentRequestTime;
    private Vector3 stunPosition;
    private Quaternion stunRotation;
    private float blockedTimer;
    private State lastLoggedState = State.Resting;
    private float nextDebugTime;
    private Quaternion homeRotation;
    private Coroutine routine;
    private float nextAttackTime;

    private void Awake()
    {
        pathBuffer = new NavMeshPath();
        agent = GetComponent<NavMeshAgent>(); // optional: used if the cat has one
        if (agent == null) Debug.LogWarning(name + ": no NavMeshAgent, using straight-line chase (walks through walls).");

        homePosition = homePoint != null ? homePoint.position : transform.position;
        homeRotation = homePoint != null ? homePoint.rotation : transform.rotation;

        // The cat can only walk to a spot on the NavMesh, so snap the cushion position onto it.
        homeWalkPosition = homePosition;
        if (NavMesh.SamplePosition(homePosition, out NavMeshHit homeHit, 3f, NavMesh.AllAreas))
            homeWalkPosition = homeHit.position;
    }

    private void Update()
    {
        if (data == null || target == null) return;

        switch (CurrentState)
        {
            case State.Resting:   UpdateResting();   break;
            case State.Chasing:   UpdateChasing();   break;
            case State.Returning: UpdateReturning(); break;
            case State.Stunned:   StopMoving();      break;   // frozen (see LateUpdate)
            // Windup / Recovering are handled by coroutines.
        }

        UpdateAnimator();
        if (debugLogs) DebugReport();
    }

    // While stunned, hold the cat exactly where and how it was when the stun began, whatever else
    // (the agent, animation root motion, a leftover turn) tries to move or rotate it.
    private void LateUpdate()
    {
        if (CurrentState != State.Stunned) return;
        transform.SetPositionAndRotation(stunPosition, stunRotation);
    }

    // Prints state changes, and once a second while returning, what the NavMeshAgent is doing.
    private void DebugReport()
    {
        if (CurrentState != lastLoggedState)
        {
            Debug.Log(name + ": state -> " + CurrentState, this);
            lastLoggedState = CurrentState;
        }

        if (CurrentState == State.Returning && Time.time >= nextDebugTime
            && agent != null && agent.enabled && agent.isOnNavMesh)
        {
            nextDebugTime = Time.time + 1f;
            Debug.Log(name + ": RETURNING. distance to cushion " + FlatDistanceTo(homeWalkPosition).ToString("F2")
                      + ", pathStatus " + agent.pathStatus
                      + ", hasPath " + agent.hasPath
                      + ", pathPending " + agent.pathPending
                      + ", agent speed " + agent.speed.ToString("F1")
                      + ", velocity " + agent.velocity.magnitude.ToString("F2")
                      + ", isStopped " + agent.isStopped, this);
        }
    }

    // Keeps the Animator's Sitting and Speed parameters in sync with what the cat is doing.
    private void UpdateAnimator()
    {
        if (animator == null) return;

        if (!string.IsNullOrEmpty(sittingBool))
            animator.SetBool(sittingBool, CurrentState == State.Resting);

        if (!string.IsNullOrEmpty(speedFloat))
        {
            float speed = (agent != null && agent.enabled && agent.isOnNavMesh) ? agent.velocity.magnitude : 0f;
            animator.SetFloat(speedFloat, speed, 0.1f, Time.deltaTime); // small damping = smooth blending
        }
    }

    // ---------------- Resting / Chasing / Returning ----------------

    private void UpdateResting()
    {
        StopMoving();

        // Turn to face the way the cushion marker points.
        transform.rotation = Quaternion.RotateTowards(transform.rotation, homeRotation, data.turnSpeed * 0.5f * Time.deltaTime);

        RefreshReachability();
        if (targetReachable && CanSeeTarget(false)) StartChasing();
    }

    private void UpdateChasing()
    {
        // Keep 'targetReachable' fresh even while the cat is standing still next to the wall
        // (Chase() skips its path planning when the mouse is within attack range).
        RefreshReachability();

        // Count how long the mouse has been out of sight, or somewhere the cat can't reach.
        unseenTimer = CanSeeTarget(true) ? 0f : unseenTimer + Time.deltaTime;
        unreachableTimer = targetReachable ? 0f : unreachableTimer + Time.deltaTime;

        if (unseenTimer >= loseInterestTime || unreachableTimer >= loseInterestTime)
        {
            GoHome();
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

    private void UpdateReturning()
    {
        // If the mouse comes back into view AND can be reached, the cat chases again.
        RefreshReachability();
        if (targetReachable && CanSeeTarget(false))
        {
            StartChasing();
            return;
        }

        // The agent stops 'stoppingDistance' short of its destination, so count that as "arrived".
        float arriveDistance = HomeArriveDistance;
        if (agent != null) arriveDistance = Mathf.Max(arriveDistance, agent.stoppingDistance + 0.25f);

        float homeDistance = FlatDistanceTo(homeWalkPosition);
        bool arrived = homeDistance <= arriveDistance;

        // Something (like the cushion itself) may block the NavMesh, so the cat can't get any closer.
        // If it has stopped moving and is close enough, count that as home.
        if (!arrived && agent != null && agent.enabled && agent.isOnNavMesh)
        {
            blockedTimer = agent.velocity.magnitude < 0.1f ? blockedTimer + Time.deltaTime : 0f;
            if (blockedTimer >= 0.75f && homeDistance <= homeAcceptRadius) arrived = true;
        }

        if (arrived)
        {
            StopMoving();
            CurrentState = State.Resting;
            onReturnedHome.Invoke();
            return;
        }

        MoveTo(homeWalkPosition, data.moveSpeed * returnSpeedMultiplier);
    }

    private void StartChasing()
    {
        CurrentState = State.Chasing;
        unseenTimer = 0f;
        unreachableTimer = 0f;
        nextRepathTime = 0f;                   // plan a route right away
        onNoticed.Invoke();
    }

    private void GoHome()
    {
        CurrentState = State.Returning;
        blockedTimer = 0f;
        onLoseInterest.Invoke();
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
    // (Used while resting / returning. While chasing, GetChaseGoal() updates it.)
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

    // Walks toward a point (used to return to the cushion).
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

    // ---------------- Debug gizmos (Scene view, when the cat is selected) ----------------

    private void OnDrawGizmosSelected()
    {
        // Cyan: how far and in which cone the cat can notice the mouse.
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, sightRange);
        Vector3 left = Quaternion.Euler(0f, -fieldOfView * 0.5f, 0f) * transform.forward;
        Vector3 right = Quaternion.Euler(0f, fieldOfView * 0.5f, 0f) * transform.forward;
        Gizmos.DrawRay(transform.position, left * sightRange);
        Gizmos.DrawRay(transform.position, right * sightRange);

        // Green: the cushion.
        if (homePoint != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(homePoint.position, 0.3f);
        }

        if (data == null) return;

        Gizmos.color = Color.yellow;                       // cat starts attacking inside this
        Gizmos.DrawWireSphere(transform.position, data.attackStartRange);

        Gizmos.color = Color.red;                          // where the hit actually lands
        Gizmos.DrawWireSphere(HitCenter(), data.hitRadius);
    }
}