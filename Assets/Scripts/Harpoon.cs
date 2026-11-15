using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Haptics;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// Harpon pour la chasse sous-marine (VR).
///
/// - Se saisit avec la gâchette latérale (grip) des manettes, ou en pinçant avec les mains,
///   grâce au XR Grab Interactable posé sur le même objet (ajouté automatiquement s'il manque).
/// - Se lance en le lâchant pendant le geste : il part dans la direction du lancer, pointe en avant.
/// - Détecte l'impact de la POINTE en balayant son trajet à chaque pas de physique :
///   fonctionne lancé comme en coup d'estoc tenu en main, même à grande vitesse.
/// - Sur un poisson : le tue et reste planté dedans. Si on reprend le harpon, le poisson reste
///   embroché dessus ; il suffit de saisir le poisson avec l'autre main pour le retirer,
///   puis de le passer dans le dos pour le ranger (voir FishBag).
/// - Sur le décor : se plante dans le terrain s'il est lancé.
/// - Sous l'eau : gravité réduite, frottements, et le harpon s'oriente selon sa trajectoire.
///
/// Orientation : la pointe doit être sur l'axe Z (avant) de l'objet.
/// Le modèle 3D est généré par le code (HarpoonModel) ; clic droit sur le composant >
/// "Générer le modèle" pour le voir aussi dans l'éditeur, hors Play.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class Harpoon : MonoBehaviour
{
    [System.Serializable]
    public class FishEvent : UnityEvent<Fish> { }

    [Header("Modèle")]
    [Tooltip("Remplace l'enfant 'Model' par le harpon généré par le code (HarpoonModel) au lancement. Décoche si tu mets ton propre modèle 3D.")]
    public bool generateModel = true;

    [Header("Pointe")]
    [Tooltip("Point situé tout au bout de la pointe (créé par 'Générer le modèle')")]
    public Transform tip;
    public float tipRadius = 0.04f;
    [Tooltip("Aide à la visée : un poisson VIVANT est touché si la pointe passe à moins de cette distance (m). " +
             "Les poissons sont petits : sans cette marge, le lancer est très difficile. 0 = pas d'aide.")]
    public float fishHitRadius = 0.16f;
    [Tooltip("Layers touchés par la pointe. Exclure le joueur, les mains et l'eau (si elle a un collider).")]
    public LayerMask hitMask = ~0;

    [Header("Impact")]
    [Tooltip("Vitesse minimale de la pointe (m/s) pour compter comme un coup")]
    public float minHitSpeed = 2f;
    [Tooltip("La pointe doit avancer dans son axe (1 = parfaitement droit, 0 = n'importe quel sens)")]
    [Range(0f, 1f)] public float minForwardDot = 0.4f;
    [Tooltip("Profondeur d'enfoncement de la pointe dans la cible")]
    public float stickDepth = 0.2f;
    public bool stickInTerrain = true;

    [Header("Tenue en javelot")]
    [Tooltip("Tenu en main, le harpon pointe vers ce que tu regardes, quelle que soit l'orientation de la manette : " +
             "tu peux le tenir au-dessus de l'épaule comme un javelot. Décoché : il suit le poignet.")]
    public bool aimWithGaze = true;
    [Tooltip("Distance du point visé le long du regard (m)")]
    public float aimDistance = 18f;
    [Tooltip("Vitesse à laquelle le harpon se tourne vers le point visé (plus haut = plus nerveux)")]
    public float aimSmoothing = 14f;

    [Header("Aide au lancer")]
    [Tooltip("Vitesse minimale du harpon une fois lancé (m/s) : même un petit geste part vite et droit")]
    public float minLaunchSpeed = 10f;
    [Tooltip("Durée pendant laquelle le harpon vole parfaitement droit, sans retomber (secondes)")]
    public float straightFlightTime = 1.6f;
    [Tooltip("Si un poisson vivant se trouve à moins de cet angle (degrés) de la visée, le lancer est corrigé vers lui. 0 = aucune aide.")]
    [Range(0f, 30f)] public float throwAssistAngle = 9f;
    [Tooltip("Portée de cette aide (m)")]
    public float throwAssistRange = 25f;

    [Header("Étui à la ceinture")]
    [Tooltip("Lâcher le harpon près de l'étui (hanche opposée à la sacoche) l'y range : on peut nager sans le tenir")]
    public bool useHolster = true;
    [Tooltip("Direction du harpon rangé, par rapport au corps (pointe vers l'arrière et le bas)")]
    public Vector3 holsterDirection = new Vector3(0f, -0.35f, -1f);
    [Tooltip("Glissement du harpon dans l'étui (m) : plus haut = la poignée dépasse moins vers l'avant")]
    public float holsterSlide = 0.45f;

    [Header("Reprise dans le dos")]
    [Tooltip("Fermer une main VIDE dans son dos (derrière la tête ou l'épaule) ramène le harpon dans cette main, " +
             "où qu'il soit. Il n'y a jamais qu'un seul harpon.")]
    public bool recallFromBack = true;
    [Tooltip("La main doit être au moins aussi loin derrière la tête (m)")]
    public float backMinBehind = 0.05f;
    [Tooltip("Largeur de la zone de chaque côté de la tête (m)")]
    public float backHalfWidth = 0.5f;
    [Tooltip("La zone descend jusque-là sous la tête (m)")]
    public float backBelow = 0.6f;
    [Tooltip("La zone monte jusque-là au-dessus de la tête (m)")]
    public float backAbove = 0.4f;

    [Header("Lancer")]
    [Tooltip("Multiplie la vitesse du geste au moment du lâcher (un vrai lancer en VR est souvent mou)")]
    public float throwSpeedMultiplier = 1.5f;
    [Tooltip("Vitesse (m/s) à partir de laquelle un lâcher compte comme un lancer")]
    public float minThrowSpeed = 1.5f;
    [Tooltip("Vitesse maximale du harpon après un lancer")]
    public float maxThrowSpeed = 25f;
    [Tooltip("0 = part exactement dans la direction du geste, 1 = part là où pointe le harpon")]
    [Range(0f, 1f)] public float aimAlongHarpoon = 0.5f;

    [Header("Vol sous l'eau")]
    [Tooltip("1 = gravité normale, 0 = flotte. Sous l'eau le harpon coule lentement.")]
    [Range(0f, 1f)] public float gravityScale = 0.35f;
    public float flightDrag = 0.4f;
    public float flightAngularDrag = 1f;
    [Tooltip("Vitesse à laquelle le harpon s'aligne sur sa trajectoire (0 = désactivé)")]
    public float alignToVelocity = 10f;

    [Header("Événements")]
    [Tooltip("Appelé quand un poisson est tué (pour le score, un son, une vibration...)")]
    public FishEvent onFishKilled;

    [Header("Test")]
    [Tooltip("Touche R : remet le harpon à sa position de départ (pratique pour tester)")]
    public bool resetWithKeyR = true;
    [Tooltip("Touche T : lance le harpon droit devant la caméra (utile avec le XR Device Simulator). Lâche-le d'abord s'il est en main.")]
    public bool testThrowWithKeyT = true;
    public float testThrowSpeed = 10f;

    [Header("Départ")]
    [Tooltip("Reste immobile, en suspension, à sa position de départ tant qu'on ne l'a pas saisi (sinon il coule au fond avant d'être attrapé)")]
    public bool hoverUntilGrabbed = true;
    [Tooltip("Si le harpon s'éloigne à plus de cette distance de son point de départ, il y est replacé (0 = jamais)")]
    public float autoResetDistance = 150f;

    private Rigidbody rb;
    private XRGrabInteractable grab;
    private Collider[] ownColliders;
    private float baseDrag;
    private float baseAngularDrag;

    private readonly RaycastHit[] hitBuffer = new RaycastHit[16];
    private Vector3 lastTipPos;
    private bool hasLastTip;

    private bool hovering;
    // Le harpon ne peut tuer ou se planter qu'après avoir été saisi au moins une fois :
    // sinon, au lancement de la scène, il se plantait seul dans le sol en tombant.
    private bool armed;
    private float throwWindow;     // > 0 : on vient de le lâcher, la vitesse du lancer arrive
    private float straightTimer;   // > 0 : vol parfaitement droit juste après le lancer
    private bool aimSnap;          // vient d'être saisi : s'oriente d'un coup, sans lissage
    private Transform head;
    private XRBaseInputInteractor[] hands;   // tout ce qui peut saisir (manettes et mains suivies)
    private Vector3 startPos;
    private Quaternion startRot;

    // Planté dans le décor ou dans un poisson
    private bool stuck;
    private bool holstered;        // rangé dans l'étui à la ceinture
    private Transform followTarget;
    private Vector3 localPos;
    private Quaternion localRot;

    // Poisson embroché (reste sur le harpon tant qu'on ne le retire pas à la main)
    private Fish stuckFish;
    private Vector3 fishLocalPos;
    private Quaternion fishLocalRot;

    public bool IsHeld => grab != null && grab.isSelected;
    public Fish ImpaledFish => stuckFish;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        startPos = transform.position;
        startRot = transform.rotation;
        baseDrag = rb.linearDamping;
        baseAngularDrag = rb.angularDamping;
        rb.isKinematic = false;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        if (generateModel) ApplyModel();

        if (tip == null)
        {
            tip = transform;
            Debug.LogWarning("Harpoon: aucune pointe (tip) assignée, le centre de l'objet est utilisé.", this);
        }

        ownColliders = GetComponentsInChildren<Collider>();
        if (ownColliders.Length == 0)
            Debug.LogError("Harpoon: aucun collider sur le harpon, il ne peut pas être saisi. " +
                           "Clic droit sur le composant > 'Générer le modèle'.", this);

        SetupGrab();

        if (hoverUntilGrabbed)
        {
            hovering = true;
            rb.constraints = RigidbodyConstraints.FreezeAll;
        }
    }

    /// <summary>Branche la saisie : XR Grab Interactable réglé pour être pris au grip et lancé.</summary>
    void SetupGrab()
    {
        grab = GetComponent<XRGrabInteractable>();
        if (grab == null) grab = gameObject.AddComponent<XRGrabInteractable>();

        grab.throwOnDetach = true;            // le lâcher transmet la vitesse de la main
        grab.throwVelocityScale = 1f;         // l'amplification est faite ici (throwSpeedMultiplier)
        grab.throwAngularVelocityScale = 0f;  // pas de toupie : le harpon vole droit
        grab.movementType = XRBaseInteractable.MovementType.Instantaneous;
        grab.selectMode = InteractableSelectMode.Single;
        // Tenue en javelot : la main donne la position, le regard donne la direction (voir AimHeld)
        grab.trackRotation = !aimWithGaze;

        // Harpon en main avec un poisson embroché : l'autre main doit saisir le POISSON,
        // pas voler le harpon (les deux se chevauchent).
        // Pendant le changement de bonbonne, on ne peut rien saisir de nouveau.
        grab.selectFilters.Add(new XRSelectFilterDelegate((interactor, interactable) =>
        {
            if (interactable.interactorsSelecting.Contains(interactor)) return true; // déjà en main : on le garde
            if (PlayerLock.Active) return false;
            return stuckFish == null || !interactable.isSelected;
        }));

        grab.selectEntered.AddListener(OnGrabbed);
        grab.selectExited.AddListener(OnReleased);
    }

    void Start()
    {
        // Crée le sac à poissons (zone dans le dos + compteur) dès le départ
        if (FishBag.Instance == null)
            Debug.LogWarning("Harpoon: impossible de créer le sac à poissons (FishBag).", this);

        hands = FindObjectsByType<XRBaseInputInteractor>(FindObjectsInactive.Include);
    }

    // ---------------------------------------------------------------- Reprise dans le dos

    /// <summary>
    /// Une main vide qui se ferme dans le dos du joueur reprend le harpon : il est téléporté
    /// dans cette main, où qu'il soit (planté au loin, perdu au fond...).
    /// </summary>
    void CheckRecall()
    {
        if (!recallFromBack || hands == null || IsHeld || PlayerLock.Active) return;

        // Un poisson est embroché dessus : pas de retour magique, il faut aller chercher sa prise
        if (stuckFish != null) return;
        if (head == null)
        {
            if (Camera.main == null) return;
            head = Camera.main.transform;
        }

        for (int i = 0; i < hands.Length; i++)
        {
            XRBaseInputInteractor hand = hands[i];
            if (hand == null || !hand.isActiveAndEnabled) continue;
            if (hand.hasSelection) continue;                               // main déjà occupée
            if (!hand.selectInput.ReadWasPerformedThisFrame()) continue;   // elle vient de se fermer
            if (hand is XRRayInteractor) continue;                         // pas le rayon de téléportation

            Vector3 handPos = hand.GetAttachTransform(grab).position;
            if (!IsBehindPlayer(handPos)) continue;

            Recall(hand, handPos);
            return;
        }
    }

    bool IsBehindPlayer(Vector3 point)
    {
        // Repère de la tête « à plat » : baisser ou lever les yeux ne déplace pas le dos
        Vector3 forward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
        if (forward.sqrMagnitude < 0.01f)
            forward = Vector3.ProjectOnPlane(head.forward.y > 0f ? -head.up : head.up, Vector3.up);
        forward.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, forward);

        Vector3 d = point - head.position;
        float z = Vector3.Dot(d, forward);
        return z <= -backMinBehind && z >= -0.9f &&
               Mathf.Abs(Vector3.Dot(d, right)) <= backHalfWidth &&
               d.y >= -backBelow && d.y <= backAbove;
    }

    void Recall(XRBaseInputInteractor hand, Vector3 handPos)
    {
        // Le harpon quitte l'endroit où il était. S'il est planté dans un poisson, le poisson
        // vient avec lui : c'est la façon simple de récupérer une prise lointaine.
        Fish carriedFish = stuckFish;
        holstered = false;
        Unstick();
        hovering = false;
        throwWindow = 0f;
        straightTimer = 0f;
        hasLastTip = false;
        rb.constraints = RigidbodyConstraints.None;
        if (!rb.isKinematic)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        transform.position = handPos;
        rb.position = handPos;

        // Mis directement dans la main : elle le tient tant qu'elle reste fermée
        if (grab.interactionManager != null)
            grab.interactionManager.SelectEnter((IXRSelectInteractor)hand, (IXRSelectInteractable)grab);

        if (carriedFish != null) carriedFish.SetCarried(true); // suit le harpon (voir LateUpdate)
    }

    void OnDestroy()
    {
        if (grab == null) return;
        grab.selectEntered.RemoveListener(OnGrabbed);
        grab.selectExited.RemoveListener(OnReleased);
    }

    // ---------------------------------------------------------------- Saisie / lâcher

    void OnGrabbed(SelectEnterEventArgs args)
    {
        armed = true;
        throwWindow = 0f;
        straightTimer = 0f;
        aimSnap = true;
        if (holstered)
        {
            holstered = false;
            rb.constraints = RigidbodyConstraints.None;
        }
        hasLastTip = false;

        if (hovering)
        {
            hovering = false;
            rb.constraints = RigidbodyConstraints.None;
        }

        if (stuck)
        {
            if (stuckFish != null)
            {
                // On reprend le harpon planté dans un poisson : le poisson vient avec
                stuck = false;
                followTarget = null;
                rb.constraints = RigidbodyConstraints.None;
                stuckFish.SetCarried(true);
            }
            else
            {
                Unstick();
            }
        }

        Pulse(args.interactorObject, 0.4f, 0.1f);
    }

    void OnReleased(SelectExitEventArgs args)
    {
        if (grab.isSelected) return; // passé d'une main à l'autre

        // Lâché près de l'étui : il s'y range au lieu d'être lancé ou de couler
        if (useHolster && stuckFish == null && FishBag.Instance != null &&
            FishBag.Instance.IsInHolsterZone(transform.position))
        {
            holstered = true;
            armed = false;
            throwWindow = 0f;
            hasLastTip = false;
            rb.constraints = RigidbodyConstraints.FreezeAll;
            Pulse(args.interactorObject, 0.5f, 0.1f);
            return;
        }

        if (stuckFish != null)
        {
            // Lâché avec un poisson embroché : l'ensemble flotte, le harpon suit le poisson
            stuckFish.SetCarried(false);
            FollowFish();
            return;
        }

        throwWindow = 0.25f;
        hasLastTip = false;
    }

    void FixedUpdate()
    {
        bool held = IsHeld;

        if (hovering || stuck || holstered) return;

        if (held)
        {
            if (stuckFish != null) return; // il transporte un poisson : la pointe est occupée
            if (PlayerLock.Active)           // changement de bonbonne : pas de coup de harpon
            {
                hasLastTip = false;
                return;
            }
            SweepTip(true);
            return;
        }

        if (rb.isKinematic) return; // le Grab Interactable n'a pas encore rendu la physique

        ApplyThrow();
        ApplyFlight();
        SweepTip(false);
        CheckLost();
    }

    /// <summary>
    /// Harpon en main : il pointe vers ce que le joueur regarde. La main ne donne que la position,
    /// donc peu importe comment la manette est tournée : on peut l'armer au-dessus de l'épaule.
    /// </summary>
    void AimHeld()
    {
        if (!aimWithGaze || stuck || !IsHeld) return;
        if (head == null)
        {
            if (Camera.main == null) return;
            head = Camera.main.transform;
        }

        // Vers le point regardé, vu depuis la main : le harpon converge sur la cible
        Vector3 target = head.position + head.forward * aimDistance;
        Vector3 dir = target - transform.position;
        if (dir.sqrMagnitude < 0.01f) return;

        Quaternion wanted = Quaternion.LookRotation(dir.normalized, Vector3.up);
        if (aimSnap)
        {
            transform.rotation = wanted;
            aimSnap = false;
        }
        else
        {
            transform.rotation = Quaternion.Slerp(transform.rotation, wanted, 1f - Mathf.Exp(-aimSmoothing * Time.deltaTime));
        }
    }

    /// <summary>Harpon rangé : il suit la hanche du joueur, pointe vers l'arrière.</summary>
    bool FollowHolster()
    {
        if (!holstered) return false;

        FishBag bag = FishBag.Instance;
        if (bag == null) return false;

        Vector3 dir = holsterDirection.sqrMagnitude > 0.001f ? holsterDirection.normalized : Vector3.back;
        Quaternion rot = bag.BodyRotation * Quaternion.LookRotation(dir, Vector3.up);
        transform.SetPositionAndRotation(bag.HolsterPosition + rot * Vector3.forward * holsterSlide, rot);
        return true;
    }

    void LateUpdate()
    {
        if (FollowHolster()) return;

        // Harpon en main (sans prise) : l'étui s'illumine quand la main s'en approche
        if (useHolster && stuckFish == null && IsHeld && FishBag.Instance != null)
            FishBag.Instance.IsInHolsterZone(transform.position);

        AimHeld();

        // Le poisson embroché suit le harpon tenu en main
        if (!stuck)
        {
            if (stuckFish != null)
            {
                Quaternion rot = transform.rotation * fishLocalRot;
                stuckFish.transform.SetPositionAndRotation(transform.TransformPoint(fishLocalPos), rot);
            }
            return;
        }

        // La cible a disparu (poisson supprimé par le spawner) : le harpon retombe
        if (followTarget == null)
        {
            stuckFish = null;
            Unstick();
            return;
        }

        // Le harpon planté suit sa cible (poisson qui flotte, objet mobile...)
        transform.SetPositionAndRotation(
            followTarget.TransformPoint(localPos),
            followTarget.rotation * localRot);
    }

    // ---------------------------------------------------------------- Lancer / vol

    /// <summary>
    /// Juste après le lâcher, le Grab Interactable donne au harpon la vitesse de la main.
    /// On la redresse (pointe en avant) et on l'amplifie pour un vrai lancer de harpon.
    /// </summary>
    void ApplyThrow()
    {
        if (throwWindow <= 0f) return;
        throwWindow -= Time.fixedDeltaTime;

        Vector3 v = rb.linearVelocity;
        float speed = v.magnitude;
        if (speed < minThrowSpeed) return; // simple lâcher (ou vitesse pas encore appliquée)

        throwWindow = 0f;

        Vector3 dir = v / speed;
        Vector3 fwd = transform.forward;
        float launch = Mathf.Min(speed * throwSpeedMultiplier, maxThrowSpeed);

        if (aimWithGaze)
        {
            // Geste vers l'arrière ou sur le côté : ce n'est pas un lancer, le harpon est juste lâché
            if (Vector3.Dot(dir, fwd) < 0.15f) return;

            // Le geste donne la force, la visée donne la direction : le harpon part droit où il pointe
            dir = fwd;
            launch = Mathf.Clamp(launch, minLaunchSpeed, Mathf.Max(minLaunchSpeed, maxThrowSpeed));
        }
        else if (Vector3.Dot(dir, fwd) > 0.3f)
        {
            // On ne corrige vers l'axe du harpon que s'il pointe à peu près dans le sens du geste
            dir = Vector3.Slerp(dir, fwd, aimAlongHarpoon).normalized;
        }

        dir = AssistTowardFish(dir, launch);

        Quaternion rot = Quaternion.LookRotation(dir, Vector3.up);
        rb.linearVelocity = dir * launch;
        rb.angularVelocity = Vector3.zero;
        rb.rotation = rot;
        transform.rotation = rot;
        straightTimer = straightFlightTime;
        hasLastTip = false;

        DiveAudio.Burst(1f); // gros mouvement dans l'eau : le son de mouvement se fait entendre
    }

    /// <summary>
    /// Corrige légèrement la direction du lancer vers le poisson vivant le plus proche de la visée,
    /// en visant là où il sera quand le harpon arrivera.
    /// </summary>
    Vector3 AssistTowardFish(Vector3 dir, float speed)
    {
        if (throwAssistAngle <= 0f || speed <= 0.1f) return dir;

        Vector3 origin = tip.position;
        float bestAngle = throwAssistAngle;
        Vector3 bestDir = dir;

        System.Collections.Generic.IReadOnlyList<Fish> all = Fish.All;
        for (int i = 0; i < all.Count; i++)
        {
            Fish f = all[i];
            if (f == null || f.IsDead) continue;

            Vector3 pos = f.WorldBounds.center;
            float dist = Vector3.Distance(origin, pos);
            if (dist > throwAssistRange || dist < 0.3f) continue;

            // Position du poisson à l'arrivée du harpon
            Vector3 lead = pos + f.Velocity * (dist / speed);
            Vector3 to = (lead - origin).normalized;
            float angle = Vector3.Angle(dir, to);
            if (angle < bestAngle)
            {
                bestAngle = angle;
                bestDir = to;
            }
        }
        return bestDir;
    }

    void ApplyFlight()
    {
        rb.linearDamping = flightDrag;
        rb.angularDamping = flightAngularDrag;

        // Juste après le lancer : vol parfaitement droit (gravité annulée). Ensuite, gravité
        // réduite (poussée d'Archimède simplifiée) : le harpon finit par couler doucement.
        float lift = 1f - gravityScale;
        if (straightTimer > 0f)
        {
            straightTimer -= Time.fixedDeltaTime;
            lift = 1f;
        }
        if (rb.useGravity)
            rb.AddForce(-Physics.gravity * lift, ForceMode.Acceleration);

        // S'oriente selon la trajectoire, comme une flèche
        Vector3 v = rb.linearVelocity;
        if (alignToVelocity > 0f && v.sqrMagnitude > 1f)
        {
            Quaternion target = Quaternion.LookRotation(v.normalized, transform.up);
            rb.angularVelocity = Vector3.zero;
            rb.MoveRotation(Quaternion.Slerp(rb.rotation, target, alignToVelocity * Time.fixedDeltaTime));
        }
    }

    void CheckLost()
    {
        if (autoResetDistance <= 0f) return;
        if ((transform.position - startPos).sqrMagnitude > autoResetDistance * autoResetDistance)
            ResetToStart();
    }

    // ---------------------------------------------------------------- Impact

    void SweepTip(bool held)
    {
        Vector3 tipPos = tip.position;

        if (hasLastTip)
        {
            // Trajet parcouru depuis le dernier pas...
            Vector3 delta = tipPos - lastTipPos;
            float speed = delta.magnitude / Time.fixedDeltaTime;

            // ...plus, en vol, le trajet du pas à venir : on touche la cible AVANT que le
            // collider du harpon ne rebondisse dessus.
            if (!held)
            {
                Vector3 v = rb.linearVelocity;
                delta += v * Time.fixedDeltaTime;
                speed = Mathf.Max(speed, v.magnitude);
            }

            float dist = delta.magnitude;
            if (dist > 0.001f && speed >= minHitSpeed)
            {
                Vector3 dir = delta / dist;
                if (Vector3.Dot(dir, transform.forward) >= minForwardDot &&
                    FindHit(lastTipPos, dir, dist, out RaycastHit hit))
                {
                    HandleHit(hit, held);
                }
            }
        }

        lastTipPos = tip.position;
        hasLastTip = true;
    }

    bool FindHit(Vector3 origin, Vector3 dir, float distance, out RaycastHit best)
    {
        best = default;
        float bestDist = float.MaxValue;
        bool found = false;

        // Deux passes : la pointe exacte pour tout, puis une pointe élargie qui ne compte que les poissons
        for (int pass = 0; pass < 2; pass++)
        {
            bool fishOnly = pass == 1;
            if (fishOnly && fishHitRadius <= tipRadius) break;

            int count = Physics.SphereCastNonAlloc(origin, fishOnly ? fishHitRadius : tipRadius, dir, hitBuffer,
                                                   distance, hitMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                RaycastHit h = hitBuffer[i];
                if (h.collider.transform.IsChildOf(transform)) continue; // le harpon lui-même

                Fish f = h.collider.GetComponentInParent<Fish>();
                if (f != null && f.IsDead) continue; // on traverse les poissons déjà morts
                if (fishOnly && f == null) continue;

                // Le joueur (mains, manettes, tête) n'est pas une cible
                if (f == null && h.collider.GetComponentInParent<Unity.XR.CoreUtils.XROrigin>() != null) continue;

                if (h.distance < bestDist)
                {
                    bestDist = h.distance;
                    best = h;
                    found = true;
                }
            }
        }

        // La sphère démarrait déjà dans la cible : pas de point de contact fourni
        if (found && best.distance <= 0f && best.point == Vector3.zero)
            best.point = origin;

        return found;
    }

    void HandleHit(RaycastHit hit, bool held)
    {
        // Jamais saisi : c'est juste un objet qui tombe, il ne tue rien et ne se plante pas
        if (!held && !armed) return;

        Fish fish = hit.collider.GetComponentInParent<Fish>();

        if (fish != null)
        {
            fish.Die();
            onFishKilled?.Invoke(fish);
            Impale(fish, hit, held);
            return;
        }

        if (!held && stickInTerrain)
            StickTo(hit, hit.collider.transform);
    }

    // ---------------------------------------------------------------- Plantage

    /// <summary>Plante le harpon dans un poisson, qui reste embroché jusqu'à ce qu'on le retire à la main.</summary>
    void Impale(Fish fish, RaycastHit hit, bool held)
    {
        stuckFish = fish;
        fish.SetImpaledBy(this);

        foreach (Collider mine in ownColliders)
            foreach (Collider theirs in fish.GetComponentsInChildren<Collider>())
                if (mine != null && theirs != null) Physics.IgnoreCollision(mine, theirs, true);

        if (held)
        {
            // Coup d'estoc : le harpon reste en main, on amène le poisson sur la pointe
            Vector3 offset = tip.position - transform.forward * stickDepth - hit.point;
            fish.transform.position += offset;
            RememberFishPose();
            fish.SetCarried(true);

            if (grab.interactorsSelecting.Count > 0)
                Pulse(grab.interactorsSelecting[0], 0.7f, 0.15f);
        }
        else
        {
            StickTo(hit, fish.transform);
            RememberFishPose();
        }
    }

    void RememberFishPose()
    {
        fishLocalPos = transform.InverseTransformPoint(stuckFish.transform.position);
        fishLocalRot = Quaternion.Inverse(transform.rotation) * stuckFish.transform.rotation;
    }

    void StickTo(RaycastHit hit, Transform target)
    {
        armed = false; // il faudra le ressaisir pour qu'il soit de nouveau actif
        throwWindow = 0f;

        // Enfonce la pointe dans la surface touchée
        transform.position += hit.point + transform.forward * stickDepth - tip.position;

        Follow(target);
    }

    /// <summary>Fige le harpon et le fait suivre une cible (décor, poisson qui flotte...).</summary>
    void Follow(Transform target)
    {
        stuck = true;

        if (!rb.isKinematic)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
        // On fige le corps plutôt que de le rendre kinématique : le Grab Interactable
        // pourra alors restituer correctement la physique (et le lancer) quand on le reprend.
        rb.constraints = RigidbodyConstraints.FreezeAll;

        followTarget = target;
        localPos = target.InverseTransformPoint(transform.position);
        localRot = Quaternion.Inverse(target.rotation) * transform.rotation;
    }

    void FollowFish()
    {
        if (stuckFish == null) return;
        armed = false;
        Follow(stuckFish.transform);
    }

    /// <summary>Appelé par le poisson quand le joueur le saisit : il est retiré du harpon.</summary>
    public void ReleaseFish(Fish fish)
    {
        if (fish != stuckFish) return;

        foreach (Collider mine in ownColliders)
            foreach (Collider theirs in fish.GetComponentsInChildren<Collider>())
                if (mine != null && theirs != null) Physics.IgnoreCollision(mine, theirs, false);

        stuckFish = null;
        hasLastTip = false;

        // Harpon pas en main : il se détache et coule doucement
        if (stuck) Unstick();

        if (grab.interactorsSelecting.Count > 0)
            Pulse(grab.interactorsSelecting[0], 0.5f, 0.1f);
    }

    /// <summary>Décroche le harpon (appelé automatiquement quand le joueur le saisit).</summary>
    public void Unstick()
    {
        if (!stuck) return;

        stuck = false;
        followTarget = null;
        hasLastTip = false;
        rb.constraints = RigidbodyConstraints.None;
    }

    static void Pulse(IXRInteractor interactor, float amplitude, float duration)
    {
        if (interactor == null || interactor.transform == null) return;
        HapticImpulsePlayer haptics = interactor.transform.GetComponentInParent<HapticImpulsePlayer>();
        if (haptics != null) haptics.SendHapticImpulse(amplitude, duration);
    }

    // ---------------------------------------------------------------- Remise à zéro

    void Update()
    {
        CheckRecall();

        bool reset = false;
        bool throwIt = false;

#if ENABLE_INPUT_SYSTEM
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb != null)
        {
            reset = resetWithKeyR && kb[UnityEngine.InputSystem.Key.R].wasPressedThisFrame;
            throwIt = testThrowWithKeyT && kb[UnityEngine.InputSystem.Key.T].wasPressedThisFrame;
        }
#else
        reset = resetWithKeyR && Input.GetKeyDown(KeyCode.R);
        throwIt = testThrowWithKeyT && Input.GetKeyDown(KeyCode.T);
#endif

        if (reset) ResetToStart();
        if (throwIt) TestThrow();
    }

    /// <summary>Retire le poisson embroché sans passer par la main (remise à zéro, test).</summary>
    void DropFish()
    {
        if (stuckFish == null) return;
        Fish fish = stuckFish;
        fish.SetImpaledBy(null);
        ReleaseFish(fish);
    }

    /// <summary>Lance le harpon droit devant la caméra (pour tester sans casque ni lancer réel).</summary>
    void TestThrow()
    {
        Camera cam = Camera.main;
        if (cam == null || IsHeld) return;

        DropFish();
        Unstick();
        armed = true;
        hovering = false;
        throwWindow = 0f;
        hasLastTip = false;
        rb.constraints = RigidbodyConstraints.None;

        Vector3 forward = cam.transform.forward;
        Quaternion rot = Quaternion.LookRotation(forward, Vector3.up);
        // Départ un peu sous le regard, et assez loin pour que la hampe ne soit pas dans la tête
        Vector3 pos = cam.transform.position + forward * 1.2f - cam.transform.up * 0.2f;
        transform.SetPositionAndRotation(pos, rot);
        rb.position = pos;
        rb.rotation = rot;

        if (!rb.isKinematic)
        {
            rb.linearVelocity = forward * testThrowSpeed;
            rb.angularVelocity = Vector3.zero;
        }
    }

    /// <summary>Remet le harpon à sa position de départ.</summary>
    [ContextMenu("Replacer au départ")]
    public void ResetToStart()
    {
        if (IsHeld) return;

        DropFish();
        Unstick();
        armed = false;
        throwWindow = 0f;
        hasLastTip = false;

        if (!rb.isKinematic)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
        transform.SetPositionAndRotation(startPos, startRot);
        rb.position = startPos;
        rb.rotation = startRot;

        if (hoverUntilGrabbed)
        {
            hovering = true;
            rb.constraints = RigidbodyConstraints.FreezeAll;
        }
    }

    // ---------------------------------------------------------------- Modèle

    /// <summary>
    /// Génère le modèle du harpon (voir HarpoonModel), place la pointe de détection au bout
    /// de la lame et ajuste le collider à la hampe.
    /// </summary>
    [ContextMenu("Générer le modèle")]
    void ApplyModel()
    {
        HarpoonModel.Build(transform);

        // Point de détection tout au bout de la pointe
        if (tip == null || tip == transform)
        {
            Transform tipT = transform.Find("Tip");
            if (tipT == null)
            {
                tipT = new GameObject("Tip").transform;
                tipT.SetParent(transform, false);
            }
            tip = tipT;
        }
        tip.localPosition = new Vector3(0f, 0f, HarpoonModel.TipZ);
        tip.localRotation = Quaternion.identity;

        // Un seul collider capsule le long de Z, du talon à la pointe, fin comme la hampe
        CapsuleCollider cap = GetComponent<CapsuleCollider>();
        if (cap == null) cap = gameObject.AddComponent<CapsuleCollider>();
        cap.direction = 2;
        cap.radius = 0.025f;
        cap.height = HarpoonModel.TipZ - HarpoonModel.ButtZ;
        cap.center = new Vector3(0f, 0f, (HarpoonModel.TipZ + HarpoonModel.ButtZ) * 0.5f);

        if (!Application.isPlaying) GetComponent<Rigidbody>().mass = 0.6f;
    }

    // ---------------------------------------------------------------- Debug

    void OnDrawGizmosSelected()
    {
        if (tip == null) return;
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(tip.position, tipRadius);
        Gizmos.DrawLine(tip.position, tip.position - transform.forward * 0.3f);
    }
}
