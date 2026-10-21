using UnityEngine;

/// <summary>
/// Poisson simple pour chasse au harpon.
/// - Reste dans une zone (maison) autour de son point de spawn (rayon max 20 m)
/// - Prend peur si le joueur est trop proche, fuit, puis revient doucement
/// - Quand il est tué : se met sur le côté et flotte/stagne
///
/// Installation :
///  1. Ajouter ce script sur le prefab du poisson (avec un Rigidbody + Collider).
///  2. Taguer le joueur "Player" (ou assigner la variable player).
///  3. Pour tuer le poisson depuis le harpon : fish.Die();
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class Fish : MonoBehaviour
{
    private enum State { Wandering, Fleeing, Returning, Dead }

    [Header("Référence joueur")]
    [Tooltip("Laisser vide pour chercher automatiquement l'objet taggé 'Player'")]
    public Transform player;

    [Header("Maison (zone de vie)")]
    [Tooltip("Distance max au point de spawn")]
    public float homeRadius = 20f;

    [Header("Nage tranquille")]
    public float wanderSpeed = 1.5f;
    public float turnSpeed = 2f;
    [Tooltip("Distance minimale entre deux destinations aléatoires")]
    public float minWanderDistance = 3f;
    [Tooltip("Pause aléatoire à chaque destination atteinte (min/max en secondes)")]
    public Vector2 pauseRange = new Vector2(0f, 2f);
    public float arriveDistance = 1f;

    [Header("Peur")]
    [Tooltip("Distance à laquelle le joueur fait peur au poisson")]
    public float fearDistance = 6f;
    public float fleeSpeed = 6f;
    public float fleeTurnSpeed = 6f;
    [Tooltip("Durée minimale de la fuite (secondes)")]
    public float fleeDuration = 2.5f;
    [Tooltip("Distance de sécurité à atteindre avant de calmer la fuite")]
    public float safeDistance = 10f;

    [Header("Retour à la maison")]
    public float returnSpeed = 1.8f;
    [Tooltip("En dessous de cette distance au spawn, le poisson reprend sa balade")]
    public float returnUntilDistance = 0.7f; // fraction de homeRadius

    [Header("Mort")]
    [Tooltip("Vitesse de bascule sur le côté")]
    public float deathRollSpeed = 3f;
    [Tooltip("Hauteur de flottaison relative au niveau où il meurt (légère remontée)")]
    public float deathFloatUpSpeed = 0.15f;
    [Tooltip("Amplitude du léger balancement sur place")]
    public float bobAmplitude = 0.05f;
    public float bobSpeed = 1f;
    [Tooltip("Désactiver le collider à la mort ? (sinon il reste harponnable)")]
    public bool keepColliderWhenDead = true;

    [Header("Eau (optionnel)")]
    [Tooltip("Si > 0, le poisson ne dépassera jamais cette hauteur Y (surface de l'eau)")]
    public float waterSurfaceY = 0f;
    public bool useWaterSurface = false;
    [Tooltip("Marge sous la surface")]
    public float surfaceMargin = 0.5f;

    private Rigidbody rb;
    private Vector3 spawnPoint;
    private Vector3 targetPoint;
    private State state = State.Wandering;
    private float fleeTimer;
    private float pauseTimer;
    private float deathBaseY;
    private float bobTimer;
    private Quaternion deadRotation;

    public bool IsDead => state == State.Dead;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.useGravity = false;
        rb.linearDamping = 1.5f;
        rb.angularDamping = 3f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.constraints = RigidbodyConstraints.FreezeRotation;
    }

    void Start()
    {
        spawnPoint = transform.position;

        if (player == null)
        {
            GameObject p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) player = p.transform;
            else Debug.LogWarning("Fish: aucun joueur trouvé (tag 'Player'). Le poisson n'aura jamais peur.", this);
        }

        PickNewTarget();
    }

    void FixedUpdate()
    {
        switch (state)
        {
            case State.Wandering: UpdateWandering(); break;
            case State.Fleeing:   UpdateFleeing();   break;
            case State.Returning: UpdateReturning(); break;
            case State.Dead:      UpdateDead();      break;
        }
    }

    // ---------------------------------------------------------------- Balade

    void UpdateWandering()
    {
        if (PlayerTooClose())
        {
            StartFleeing();
            return;
        }

        if (pauseTimer > 0f)
        {
            pauseTimer -= Time.fixedDeltaTime;
            SlowDown();
            return;
        }

        MoveToward(targetPoint, wanderSpeed, turnSpeed);

        if (Vector3.Distance(transform.position, targetPoint) < arriveDistance)
        {
            pauseTimer = Random.Range(pauseRange.x, pauseRange.y);
            PickNewTarget();
        }
    }

    void PickNewTarget()
    {
        // Point aléatoire dans la sphère de la maison
        for (int i = 0; i < 10; i++)
        {
            Vector3 candidate = spawnPoint + Random.insideUnitSphere * homeRadius;
            candidate = ClampToWater(candidate);
            if (Vector3.Distance(candidate, transform.position) >= minWanderDistance)
            {
                targetPoint = candidate;
                return;
            }
        }
        targetPoint = ClampToWater(spawnPoint + Random.insideUnitSphere * homeRadius);
    }

    // ---------------------------------------------------------------- Peur

    bool PlayerTooClose()
    {
        if (player == null) return false;
        return Vector3.Distance(transform.position, player.position) < fearDistance;
    }

    void StartFleeing()
    {
        state = State.Fleeing;
        fleeTimer = fleeDuration;
    }

    void UpdateFleeing()
    {
        fleeTimer -= Time.fixedDeltaTime;

        Vector3 fleeDir;
        if (player != null)
        {
            fleeDir = transform.position - player.position;
            // Un peu de variation pour que ça ne soit pas une ligne droite parfaite
            fleeDir += Random.insideUnitSphere * 0.3f;
        }
        else
        {
            fleeDir = transform.forward;
        }

        fleeDir.Normalize();
        Vector3 fleeTarget = ClampToWater(transform.position + fleeDir * 10f);
        MoveToward(fleeTarget, fleeSpeed, fleeTurnSpeed);

        bool safe = player == null ||
                    Vector3.Distance(transform.position, player.position) > safeDistance;

        if (fleeTimer <= 0f && safe)
        {
            state = State.Returning;
        }
    }

    // ---------------------------------------------------------------- Retour

    void UpdateReturning()
    {
        // S'il se refait surprendre en revenant, il refuit
        if (PlayerTooClose())
        {
            StartFleeing();
            return;
        }

        MoveToward(spawnPoint, returnSpeed, turnSpeed);

        float dist = Vector3.Distance(transform.position, spawnPoint);
        if (dist < homeRadius * returnUntilDistance)
        {
            state = State.Wandering;
            pauseTimer = Random.Range(pauseRange.x, pauseRange.y);
            PickNewTarget();
        }
    }

    // ---------------------------------------------------------------- Mort

    /// <summary>À appeler quand le harpon touche le poisson.</summary>
    public void Die()
    {
        if (state == State.Dead) return;

        state = State.Dead;
        deathBaseY = transform.position.y;
        bobTimer = Random.value * 10f;

        // Bascule sur le côté (roulis de 90° autour de l'axe avant), droite ou gauche au hasard
        float side = Random.value > 0.5f ? 90f : -90f;
        deadRotation = transform.rotation * Quaternion.Euler(0f, 0f, side);

        rb.constraints = RigidbodyConstraints.None;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.isKinematic = true; // on pilote le mouvement à la main

        if (!keepColliderWhenDead)
        {
            foreach (Collider c in GetComponentsInChildren<Collider>())
                c.enabled = false;
        }
    }

    void UpdateDead()
    {
        // Roulis progressif sur le côté
        transform.rotation = Quaternion.Slerp(
            transform.rotation, deadRotation, deathRollSpeed * Time.fixedDeltaTime);

        // Remonte très doucement puis stagne avec un léger balancement
        bobTimer += Time.fixedDeltaTime * bobSpeed;
        deathBaseY += deathFloatUpSpeed * Time.fixedDeltaTime;

        float maxY = float.MaxValue;
        if (useWaterSurface) maxY = waterSurfaceY - 0.05f; // flotte juste sous/à la surface
        deathBaseY = Mathf.Min(deathBaseY, maxY);

        Vector3 pos = transform.position;
        pos.y = deathBaseY + Mathf.Sin(bobTimer) * bobAmplitude;
        transform.position = pos;
    }

    // ---------------------------------------------------------------- Utilitaires

    void MoveToward(Vector3 target, float speed, float turn)
    {
        Vector3 dir = target - transform.position;
        if (dir.sqrMagnitude < 0.0001f) return;
        dir.Normalize();

        Quaternion targetRot = Quaternion.LookRotation(dir, Vector3.up);
        rb.MoveRotation(Quaternion.Slerp(rb.rotation, targetRot, turn * Time.fixedDeltaTime));

        // Avance dans la direction où il regarde (nage naturelle)
        Vector3 desiredVel = rb.rotation * Vector3.forward * speed;
        rb.linearVelocity = Vector3.Lerp(rb.linearVelocity, desiredVel, 5f * Time.fixedDeltaTime);
    }

    void SlowDown()
    {
        rb.linearVelocity = Vector3.Lerp(rb.linearVelocity, Vector3.zero, 3f * Time.fixedDeltaTime);
    }

    Vector3 ClampToWater(Vector3 p)
    {
        // Reste dans la sphère de la maison
        Vector3 offset = p - spawnPoint;
        if (offset.magnitude > homeRadius)
            p = spawnPoint + offset.normalized * homeRadius;

        // Reste sous la surface si définie
        if (useWaterSurface)
            p.y = Mathf.Min(p.y, waterSurfaceY - surfaceMargin);

        return p;
    }

    // ---------------------------------------------------------------- Debug

    void OnDrawGizmosSelected()
    {
        Vector3 center = Application.isPlaying ? spawnPoint : transform.position;

        Gizmos.color = new Color(0f, 0.8f, 1f, 0.35f);
        Gizmos.DrawWireSphere(center, homeRadius);

        Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, fearDistance);

        if (Application.isPlaying && state != State.Dead)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(transform.position, targetPoint);
            Gizmos.DrawSphere(targetPoint, 0.2f);
        }
    }
}
