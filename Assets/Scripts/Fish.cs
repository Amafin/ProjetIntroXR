using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Haptics;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// Poisson simple pour chasse au harpon.
/// - Reste dans une zone (maison) autour de son point de spawn (rayon max 20 m)
/// - Prend peur si le joueur est trop proche, fuit, puis revient doucement
/// - Quand il est tué : se met sur le côté et flotte/stagne
/// - Une fois mort, il peut être saisi à la main (XR Grab Interactable ajouté automatiquement).
///   Le saisir le retire du harpon ; l'amener à la sacoche sur la hanche (voir FishBag) le range : +1 poisson.
/// - Si le prefab n'a pas de collider, une boîte englobante est créée au lancement
///   (sans collider, le harpon ne peut pas le toucher).
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
    [Tooltip("Vitesse de nage du joueur (m/s) en dessous de laquelle le poisson ne prend PAS peur : " +
             "en approchant lentement, on peut venir tout près. 0 = il fuit toujours.")]
    public float calmApproachSpeed = 0.9f;
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

    [Header("Ramassage")]
    [Tooltip("Rayon (m) dans lequel la main peut saisir le poisson MORT. Plus grand que le poisson, " +
             "pour ne pas avoir à viser précisément son corps.")]
    public float grabRadius = 0.38f;

    [Header("Obstacles (terrain, rochers...)")]
    [Tooltip("Layers considérés comme obstacles. Exclure le layer de l'eau si elle a un collider !")]
    public LayerMask obstacleMask = ~0;
    [Tooltip("Rayon de détection (un peu plus grand que le poisson)")]
    public float obstacleRadius = 0.5f;
    [Tooltip("Distance de détection devant le poisson (augmente avec la vitesse)")]
    public float obstacleCheckDistance = 3f;
    [Tooltip("Vitesse de rotation minimale quand il évite un obstacle")]
    public float avoidTurnSpeed = 5f;
    [Tooltip("Vérifie toutes les X secondes si le poisson est bloqué")]
    public float stuckCheckInterval = 1.5f;
    [Tooltip("Distance minimale parcourue entre deux vérifications, sinon il est considéré bloqué")]
    public float stuckMinDistance = 0.3f;
    [Tooltip("Durée de la manoeuvre de dégagement quand il est bloqué")]
    public float escapeDuration = 1.5f;
    [Tooltip("Hauteur minimale gardée au-dessus du sol (le poisson ne peut pas descendre plus bas)")]
    public float groundMargin = 0.4f;

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

    // Obstacles / déblocage
    private readonly RaycastHit[] hitBuffer = new RaycastHit[8];
    private float stuckTimer;
    private Vector3 lastCheckPosition;
    private float escapeTimer;
    private Vector3 escapeDir;
    private Vector3 lastHitNormal = Vector3.up;
    private float lastHitTime = -10f;

    // Collider / saisie / harpon
    private XRGrabInteractable grab;
    private Harpoon impaledBy;
    private bool carried;      // porté par le harpon tenu en main
    private bool needsRebase;  // vient d'être lâché : repart de sa position actuelle
    private bool collected;

    // Tous les poissons présents dans la scène (pour la visée du scan, sans rechercher à chaque image)
    private static readonly System.Collections.Generic.List<Fish> all = new System.Collections.Generic.List<Fish>();
    public static System.Collections.Generic.IReadOnlyList<Fish> All => all;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        all.Clear();
    }

    void OnEnable()
    {
        if (!all.Contains(this)) all.Add(this);
    }

    void OnDisable()
    {
        all.Remove(this);
    }

    private Collider bodyCollider;

    /// <summary>Volume du poisson dans le monde (son collider).</summary>
    public Bounds WorldBounds
    {
        get
        {
            if (bodyCollider == null) bodyCollider = GetComponentInChildren<Collider>();
            return bodyCollider != null ? bodyCollider.bounds : new Bounds(transform.position, Vector3.one);
        }
    }

    /// <summary>Espèce du poisson (nom, statut pêchable / protégé, teinte).</summary>
    public FishSpecies Species { get; private set; } = FishSpecies.Default;

    public bool IsDead => state == State.Dead;
    /// <summary>Déplacement du poisson (m/s), pour viser là où il sera.</summary>
    public Vector3 Velocity => rb != null && !rb.isKinematic ? rb.linearVelocity : Vector3.zero;
    /// <summary>Vitesse de nage du poisson (m/s).</summary>
    public float Speed => rb != null && !rb.isKinematic ? rb.linearVelocity.magnitude : 0f;
    /// <summary>Tenu dans une main.</summary>
    public bool IsHeld => grab != null && grab.isSelected;
    /// <summary>Tenu en main ou embroché sur le harpon : le spawner ne doit pas le supprimer.</summary>
    public bool IsBusy => IsHeld || impaledBy != null;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.useGravity = false;
        rb.linearDamping = 1.5f;
        rb.angularDamping = 3f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic; // pas de traversée à haute vitesse
        rb.constraints = RigidbodyConstraints.FreezeRotation;

        EnsureCollider();
    }

    /// <summary>
    /// Le prefab du poisson n'a aucun collider : sans ça, ni le harpon ni les mains ne peuvent
    /// le toucher. On crée une boîte à partir du volume du modèle.
    /// </summary>
    void EnsureCollider()
    {
        foreach (Collider c in GetComponentsInChildren<Collider>())
            if (!c.isTrigger) return;

        Bounds local = new Bounds(Vector3.zero, new Vector3(0.1f, 0.3f, 0.4f));
        bool has = false;

        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            Bounds b = r.localBounds;
            Transform space = r.transform;
            if (r is SkinnedMeshRenderer smr && smr.rootBone != null)
                space = smr.rootBone;

            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = b.center + Vector3.Scale(b.extents,
                    new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                Vector3 p = transform.InverseTransformPoint(space.TransformPoint(corner));
                if (!has) { local = new Bounds(p, Vector3.zero); has = true; }
                else local.Encapsulate(p);
            }
        }

        BoxCollider box = gameObject.AddComponent<BoxCollider>();
        box.center = local.center;

        // Un peu plus petit que le volume du modèle, qui inclut le bout des nageoires.
        // L'axe le plus fin (l'épaisseur) est réduit bien davantage : ce volume est élargi par
        // les nageoires pectorales, alors que le corps d'un poisson est plat.
        Vector3 size = local.size * 0.8f;
        if (size.x <= size.y && size.x <= size.z) size.x *= 0.45f;
        else if (size.y <= size.x && size.y <= size.z) size.y *= 0.45f;
        else size.z *= 0.45f;

        // ...mais jamais trop fin : il doit rester touchable.
        Vector3 scale = transform.lossyScale;
        size.x = Mathf.Max(size.x, 0.12f / Mathf.Max(0.0001f, Mathf.Abs(scale.x)));
        size.y = Mathf.Max(size.y, 0.12f / Mathf.Max(0.0001f, Mathf.Abs(scale.y)));
        size.z = Mathf.Max(size.z, 0.12f / Mathf.Max(0.0001f, Mathf.Abs(scale.z)));
        box.size = size;
    }

    void Update()
    {
        // Poisson mort tenu en main : rangé dès que la main arrive sur la sacoche (à la hanche)
        if (state != State.Dead || collected || !IsHeld) return;

        IXRSelectInteractor hand = grab.interactorsSelecting[0];
        Vector3 handPos = hand.GetAttachTransform(grab).position;

        FishBag bag = FishBag.Instance;
        if (bag != null && bag.IsInBagZone(handPos))
            Collect(bag, hand);
    }

    void Start()
    {
        spawnPoint = transform.position;

        // En VR, on utilise la tête du joueur (Main Camera de l'XR Origin) : la racine de
        // l'XR Origin reste au sol et ne suit pas toujours les déplacements réels du casque.
        if (player == null)
        {
            if (Camera.main != null)
            {
                player = Camera.main.transform;
            }
            else
            {
                GameObject p = GameObject.FindGameObjectWithTag("Player");
                if (p != null) player = p.transform;
                else Debug.LogWarning("Fish: aucun joueur trouvé (ni Main Camera, ni tag 'Player'). Le poisson n'aura jamais peur.", this);
            }
        }

        PickNewTarget();
    }

    void FixedUpdate()
    {
        if (state != State.Dead)
        {
            CheckStuck();
            KeepAboveGround();
        }

        switch (state)
        {
            case State.Wandering: UpdateWandering(); break;
            case State.Fleeing: UpdateFleeing(); break;
            case State.Returning: UpdateReturning(); break;
            case State.Dead: UpdateDead(); break;
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
        // Point aléatoire dans la sphère de la maison, en évitant les destinations
        // situées dans le terrain ou derrière un obstacle
        for (int i = 0; i < 15; i++)
        {
            Vector3 candidate = spawnPoint + Random.insideUnitSphere * homeRadius;
            candidate = ClampToWater(candidate);

            if (Vector3.Distance(candidate, transform.position) < minWanderDistance) continue;
            if (!IsPathClear(candidate)) continue;

            targetPoint = candidate;
            return;
        }

        // Aucun point valide trouvé : il retourne à son point de spawn (zone libre)
        targetPoint = spawnPoint;
    }

    // ---------------------------------------------------------------- Peur

    bool PlayerTooClose()
    {
        if (player == null) return false;
        if (PlayerIsCalm()) return false; // approche lente : le poisson reste tranquille
        return Vector3.Distance(transform.position, player.position) < fearDistance;
    }

    /// <summary>Le joueur nage assez lentement pour ne pas effrayer le poisson.</summary>
    bool PlayerIsCalm()
    {
        return calmApproachSpeed > 0f && SwimLocomotion.PlayerSpeed < calmApproachSpeed;
    }

    /// <summary>Donne son espèce au poisson et applique sa teinte au modèle.</summary>
    public void SetSpecies(FishSpecies species)
    {
        Species = species ?? FishSpecies.Default;

        var block = new MaterialPropertyBlock();
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            r.GetPropertyBlock(block);
            block.SetColor("_BaseColor", Species.tint);
            block.SetColor("_Color", Species.tint);
            r.SetPropertyBlock(block);
        }
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

        // En sécurité s'il est assez loin, ou si le joueur s'est calmé
        bool safe = player == null || PlayerIsCalm() ||
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
        // Prévenu avant tout le reste : c'est ici que tuer une espèce protégée est compté
        if (DiveGame.Instance != null) DiveGame.Instance.NotifyFishKilled(this);
        deathBaseY = transform.position.y;
        bobTimer = Random.value * 10f;

        // Bascule sur le côté (roulis de 90° autour de l'axe avant), droite ou gauche au hasard
        float side = Random.value > 0.5f ? 90f : -90f;
        deadRotation = transform.rotation * Quaternion.Euler(0f, 0f, side);

        rb.constraints = RigidbodyConstraints.None;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.isKinematic = true; // on pilote le mouvement à la main

        // Fige l'animation de nage
        Animator anim = GetComponent<Animator>();
        if (anim != null) anim.speed = 0f;

        // Le collider est toujours conservé : il sert à saisir le poisson mort
        MakeGrabbable();
    }

    // ---------------------------------------------------------------- Saisie / harpon / sac

    void MakeGrabbable()
    {
        if (grab != null) return;

        // Zone de saisie élargie autour du poisson mort (ajoutée AVANT le Grab Interactable, qui
        // relève les colliders à sa création). Le poisson mort est immobile : aucun effet physique.
        if (grabRadius > 0f)
        {
            Vector3 bodyCenter = transform.InverseTransformPoint(WorldBounds.center); // relevé avant d'ajouter la zone
            SphereCollider zone = gameObject.AddComponent<SphereCollider>();
            Vector3 scale = transform.lossyScale;
            float s = Mathf.Max(0.0001f, Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
            zone.radius = grabRadius / s;
            zone.center = bodyCenter;
        }

        grab = GetComponent<XRGrabInteractable>();
        if (grab == null) grab = gameObject.AddComponent<XRGrabInteractable>();

        grab.movementType = XRBaseInteractable.MovementType.Instantaneous;
        grab.throwOnDetach = false;      // le corps est kinématique : pas de lancer
        grab.useDynamicAttach = true;    // saisi là où la main le touche
        // Pendant le changement de bonbonne, on ne peut rien saisir de nouveau
        grab.selectFilters.Add(new XRSelectFilterDelegate((interactor, interactable) =>
            !PlayerLock.Active || interactable.interactorsSelecting.Contains(interactor)));
        grab.selectEntered.AddListener(OnGrabbed);
    }

    void OnGrabbed(SelectEnterEventArgs args)
    {
        // Prendre le poisson en main le retire du harpon
        if (impaledBy != null)
        {
            Harpoon h = impaledBy;
            impaledBy = null;
            carried = false;
            h.ReleaseFish(this);
        }
        Pulse(args.interactorObject, 0.3f, 0.08f);
    }

    /// <summary>Appelé par le harpon quand il se plante dans ce poisson (null = il s'en retire).</summary>
    public void SetImpaledBy(Harpoon harpoon)
    {
        impaledBy = harpoon;
        if (harpoon == null) SetCarried(false);
    }

    /// <summary>Appelé par le harpon : vrai tant que le harpon tenu en main transporte le poisson.</summary>
    public void SetCarried(bool value)
    {
        if (carried && !value) needsRebase = true;
        carried = value;
    }

    void Collect(FishBag bag, IXRSelectInteractor hand)
    {
        collected = true;
        Pulse(hand, 0.8f, 0.2f);

        if (grab.interactionManager != null)
            grab.interactionManager.CancelInteractableSelection((IXRSelectInteractable)grab);

        bag.Add(this);
        Destroy(gameObject);
    }

    static void Pulse(IXRInteractor interactor, float amplitude, float duration)
    {
        if (interactor == null || interactor.transform == null) return;
        HapticImpulsePlayer haptics = interactor.transform.GetComponentInParent<HapticImpulsePlayer>();
        if (haptics != null) haptics.SendHapticImpulse(amplitude, duration);
    }

    void UpdateDead()
    {
        // Tenu en main ou transporté par le harpon : quelqu'un d'autre le déplace
        if (carried || IsHeld)
        {
            needsRebase = true;
            return;
        }

        // Vient d'être lâché : il flotte là où il est, dans l'orientation où on l'a laissé
        if (needsRebase)
        {
            needsRebase = false;
            deathBaseY = transform.position.y;
            deadRotation = transform.rotation;
        }

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
        // Manoeuvre de dégagement : on ignore la cible et on s'éloigne de l'obstacle
        if (escapeTimer > 0f)
        {
            escapeTimer -= Time.fixedDeltaTime;
            target = transform.position + escapeDir * 5f;
        }

        Vector3 dir = target - transform.position;
        if (dir.sqrMagnitude < 0.0001f) return;
        dir.Normalize();

        // Évitement : si un obstacle est devant, on longe sa surface en s'en écartant
        float checkDist = Mathf.Max(obstacleCheckDistance, speed * 0.6f);
        if (ObstacleAhead(transform.position, transform.forward, checkDist, out RaycastHit hit))
        {
            Vector3 n = hit.normal;
            float proximity = 1f - Mathf.Clamp01(hit.distance / checkDist);

            Vector3 slide = Vector3.ProjectOnPlane(dir, n);
            if (slide.sqrMagnitude < 0.05f) // face à l'obstacle : on choisit un côté
                slide = Vector3.ProjectOnPlane(transform.right, n);

            dir = (slide.normalized + n * (0.5f + proximity)).normalized;
            turn = Mathf.Max(turn, avoidTurnSpeed);
            speed *= Mathf.Lerp(1f, 0.4f, proximity); // ralentit près de l'obstacle
        }

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

    // ---------------------------------------------------------------- Obstacles

    /// <summary>Sphere-cast qui ignore le poisson lui-même et les autres poissons.</summary>
    bool ObstacleAhead(Vector3 from, Vector3 dir, float distance, out RaycastHit best)
    {
        best = default;
        int count = Physics.SphereCastNonAlloc(from, obstacleRadius, dir, hitBuffer,
                                               distance, obstacleMask,
                                               QueryTriggerInteraction.Ignore);

        float bestDist = float.MaxValue;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            RaycastHit h = hitBuffer[i];
            if (h.collider.transform.IsChildOf(transform)) continue;
            if (h.collider.GetComponentInParent<Fish>() != null) continue;

            if (h.distance < bestDist)
            {
                bestDist = h.distance;
                best = h;
                found = true;
            }
        }

        // La sphère démarre déjà dans l'obstacle : pas de normale fournie
        if (found && best.normal == Vector3.zero) best.normal = -dir;
        return found;
    }

    bool IsPathClear(Vector3 to)
    {
        if (Physics.CheckSphere(to, obstacleRadius, obstacleMask, QueryTriggerInteraction.Ignore))
            return false;

        Vector3 d = to - transform.position;
        return !ObstacleAhead(transform.position, d.normalized, d.magnitude, out _);
    }

    void CheckStuck()
    {
        stuckTimer += Time.fixedDeltaTime;
        if (stuckTimer < stuckCheckInterval) return;
        stuckTimer = 0f;

        bool shouldBeMoving = !(state == State.Wandering && pauseTimer > 0f);
        float moved = Vector3.Distance(transform.position, lastCheckPosition);
        lastCheckPosition = transform.position;

        if (shouldBeMoving && escapeTimer <= 0f && moved < stuckMinDistance)
            Unstick();
    }

    void Unstick()
    {
        Vector3 away = (Time.time - lastHitTime < 2f) ? lastHitNormal : -transform.forward;
        escapeDir = (away + Random.insideUnitSphere * 0.5f).normalized;
        escapeTimer = escapeDuration;

        if (state == State.Wandering) PickNewTarget();
    }

    /// <summary>
    /// Filet de sécurité : si le poisson passe sous la surface du sol (collision ratée,
    /// terrain sans collider...), il est remonté au-dessus du sol.
    /// </summary>
    void KeepAboveGround()
    {
        const float checkUp = 1f; // le rayon part 1 m au-dessus du poisson
        Vector3 p = rb.position;

        int count = Physics.RaycastNonAlloc(p + Vector3.up * checkUp, Vector3.down, hitBuffer,
                                            checkUp + groundMargin, obstacleMask,
                                            QueryTriggerInteraction.Ignore);

        float bestDist = float.MaxValue;
        float groundY = 0f;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            RaycastHit h = hitBuffer[i];
            if (h.collider.transform.IsChildOf(transform)) continue;
            if (h.collider.GetComponentInParent<Fish>() != null) continue;

            if (h.distance < bestDist)
            {
                bestDist = h.distance;
                groundY = h.point.y;
                found = true;
            }
        }

        if (!found) return;

        float minY = groundY + groundMargin;
        if (p.y < minY)
        {
            p.y = minY;
            rb.position = p;

            Vector3 v = rb.linearVelocity;
            if (v.y < 0f) { v.y = 0f; rb.linearVelocity = v; }
        }
    }

    void OnCollisionEnter(Collision c) { HandleCollision(c, true); }
    void OnCollisionStay(Collision c) { HandleCollision(c, false); }

    void HandleCollision(Collision c, bool enter)
    {
        if (state == State.Dead) return;
        if (c.collider.GetComponentInParent<Fish>() != null) return;

        lastHitNormal = c.GetContact(0).normal;
        lastHitTime = Time.time;

        // Cogné pendant la balade : on change de destination tout de suite
        if (enter && state == State.Wandering) PickNewTarget();
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
