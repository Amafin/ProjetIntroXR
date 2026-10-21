using System.Reflection;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Harpon pour la chasse sous-marine (VR).
///
/// - Se saisit avec un XR Grab Interactable (à ajouter sur le même objet) et se lance
///   (cocher "Throw On Detach" sur le Grab Interactable).
/// - Détecte l'impact de la POINTE en balayant son trajet à chaque pas de physique :
///   fonctionne aussi bien lancé qu'en coup d'estoc tenu en main, même à grande vitesse.
/// - Sur un poisson : appelle Fish.Die(). Lancé, le harpon reste planté dans le poisson ;
///   tenu en main, il ne fait que le tuer.
/// - Sur le décor : se plante dans le terrain s'il est lancé.
/// - Sous l'eau : gravité réduite, frottements, et le harpon s'oriente selon sa trajectoire.
///
/// Orientation : la pointe doit être sur l'axe Z (avant) de l'objet.
/// Clic droit sur le composant > "Générer un modèle provisoire" crée un harpon de test.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class Harpoon : MonoBehaviour
{
    [System.Serializable]
    public class FishEvent : UnityEvent<Fish> { }

    [Header("Pointe")]
    [Tooltip("Point situé tout au bout de la pointe (créé par 'Générer un modèle provisoire')")]
    public Transform tip;
    public float tipRadius = 0.04f;
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

    [Header("Vol sous l'eau")]
    [Tooltip("1 = gravité normale, 0 = flotte. Sous l'eau le harpon coule lentement.")]
    [Range(0f, 1f)] public float gravityScale = 0.35f;
    public float flightDrag = 0.4f;
    public float flightAngularDrag = 1f;
    [Tooltip("Vitesse à laquelle le harpon s'aligne sur sa trajectoire (0 = désactivé)")]
    public float alignToVelocity = 4f;

    [Header("Événements")]
    [Tooltip("Appelé quand un poisson est tué (pour le score, un son, une vibration...)")]
    public FishEvent onFishKilled;

    private Rigidbody rb;
    private float baseDrag;
    private float baseAngularDrag;

    private Component grabInteractable;
    private PropertyInfo isSelectedProp;

    private readonly RaycastHit[] hitBuffer = new RaycastHit[8];
    private Vector3 lastTipPos;
    private bool hasLastTip;

    [Header("Test")]
    [Tooltip("Touche R : remet le harpon à sa position de départ (pratique pour tester)")]
    public bool resetWithKeyR = true;
    [Tooltip("Touche T : lance le harpon droit devant la caméra (utile avec le XR Device Simulator). Lâche-le d'abord s'il est en main.")]
    public bool testThrowWithKeyT = true;
    public float testThrowSpeed = 10f;

    [Header("Départ")]
    [Tooltip("Reste immobile, en suspension, à sa position de départ tant qu'on ne l'a pas saisi (sinon il coule au fond avant d'être attrapé)")]
    public bool hoverUntilGrabbed = true;
    private bool hovering;

    // Le harpon ne peut tuer ou se planter qu'après avoir été saisi au moins une fois :
    // sinon, au lancement de la scène, il se plantait seul dans le sol en tombant.
    private bool armed;
    private Vector3 startPos;
    private Quaternion startRot;

    private bool stuck;
    private Transform followTarget;
    private Vector3 localPos;
    private Quaternion localRot;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        startPos = transform.position;
        startRot = transform.rotation;
        baseDrag = rb.linearDamping;
        baseAngularDrag = rb.angularDamping;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        if (tip == null)
        {
            tip = transform;
            Debug.LogWarning("Harpoon: aucune pointe (tip) assignée, le centre de l'objet est utilisé.", this);
        }

        FindGrabInteractable();

        if (hoverUntilGrabbed)
        {
            hovering = true;
            rb.constraints = RigidbodyConstraints.FreezeAll;
        }
    }

    /// <summary>
    /// Cherche le XR Grab Interactable par réflexion (propriété 'isSelected'), pour que le script
    /// fonctionne quelle que soit la version de XR Interaction Toolkit.
    /// </summary>
    void FindGrabInteractable()
    {
        foreach (Component c in GetComponents<Component>())
        {
            if (c == null) continue;
            PropertyInfo p = c.GetType().GetProperty("isSelected",
                BindingFlags.Public | BindingFlags.Instance);
            if (p != null && p.PropertyType == typeof(bool))
            {
                grabInteractable = c;
                isSelectedProp = p;
                return;
            }
        }
        Debug.LogWarning("Harpoon: aucun XR Grab Interactable trouvé sur l'objet. " +
                         "Le harpon ne pourra pas être attrapé.", this);
    }

    bool IsHeld()
    {
        return grabInteractable != null && (bool)isSelectedProp.GetValue(grabInteractable);
    }

    void FixedUpdate()
    {
        bool held = IsHeld();
        if (held) armed = true;

        if (hovering)
        {
            if (!held) return; // immobile tant qu'on ne l'a pas saisi
            hovering = false;
            rb.constraints = RigidbodyConstraints.None;
        }

        if (stuck)
        {
            if (held) Unstick(); // le joueur reprend le harpon planté
            else return;
        }

        if (held)
        {
            rb.linearDamping = baseDrag;
            rb.angularDamping = baseAngularDrag;
        }
        else
        {
            ApplyFlight();
        }

        SweepTip(held);
    }

    void LateUpdate()
    {
        if (!stuck) return;

        // La cible a disparu (poisson supprimé par le spawner) : le harpon retombe
        if (followTarget == null)
        {
            Unstick();
            return;
        }

        transform.SetPositionAndRotation(
            followTarget.TransformPoint(localPos),
            followTarget.rotation * localRot);
    }

    // ---------------------------------------------------------------- Vol

    void ApplyFlight()
    {
        if (rb.isKinematic) return;

        rb.linearDamping = flightDrag;
        rb.angularDamping = flightAngularDrag;

        // Gravité réduite (poussée d'Archimède simplifiée)
        rb.AddForce(-Physics.gravity * (1f - gravityScale), ForceMode.Acceleration);

        // S'oriente selon la trajectoire, comme une flèche
        Vector3 v = rb.linearVelocity;
        if (alignToVelocity > 0f && v.sqrMagnitude > 1f)
        {
            Quaternion target = Quaternion.LookRotation(v.normalized, Vector3.up);
            rb.MoveRotation(Quaternion.Slerp(rb.rotation, target, alignToVelocity * Time.fixedDeltaTime));
        }
    }

    // ---------------------------------------------------------------- Impact

    void SweepTip(bool held)
    {
        Vector3 tipPos = tip.position;

        if (hasLastTip)
        {
            Vector3 delta = tipPos - lastTipPos;
            float dist = delta.magnitude;

            if (dist > 0.001f)
            {
                Vector3 dir = delta / dist;
                float speed = dist / Time.fixedDeltaTime;
                bool forward = Vector3.Dot(dir, transform.forward) >= minForwardDot;

                if (speed >= minHitSpeed && forward &&
                    FindHit(lastTipPos, dir, dist, out RaycastHit hit))
                {
                    HandleHit(hit, held);
                }
            }
        }

        lastTipPos = tipPos;
        hasLastTip = true;
    }

    bool FindHit(Vector3 origin, Vector3 dir, float distance, out RaycastHit best)
    {
        best = default;
        int count = Physics.SphereCastNonAlloc(origin, tipRadius, dir, hitBuffer,
                                               distance, hitMask, QueryTriggerInteraction.Ignore);

        float bestDist = float.MaxValue;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            RaycastHit h = hitBuffer[i];
            if (h.collider.transform.IsChildOf(transform)) continue; // le harpon lui-même

            Fish f = h.collider.GetComponentInParent<Fish>();
            if (f != null && f.IsDead) continue; // on traverse les poissons déjà morts

            if (h.distance < bestDist)
            {
                bestDist = h.distance;
                best = h;
                found = true;
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
            if (!held) StickTo(hit, fish.transform);
            return;
        }

        if (!held && stickInTerrain)
            StickTo(hit, hit.collider.transform);
    }

    // ---------------------------------------------------------------- Plantage

    void StickTo(RaycastHit hit, Transform target)
    {
        stuck = true;
        armed = false; // il faudra le ressaisir pour qu'il soit de nouveau actif

        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        // On fige le corps plutôt que de le rendre kinématique : le Grab Interactable
        // pourra alors restituer correctement la physique (et le lancer) quand on le reprend.
        rb.constraints = RigidbodyConstraints.FreezeAll;

        // Enfonce la pointe dans la surface touchée
        transform.position += hit.point + transform.forward * stickDepth - tip.position;

        // Le harpon suit sa cible (poisson qui flotte, objet mobile...)
        followTarget = target;
        localPos = target.InverseTransformPoint(transform.position);
        localRot = Quaternion.Inverse(target.rotation) * transform.rotation;
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

    // ---------------------------------------------------------------- Remise à zéro

    void Update()
    {
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

    /// <summary>Lance le harpon droit devant la caméra (pour tester sans casque ni lancer réel).</summary>
    void TestThrow()
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        Unstick();
        armed = true;
        hovering = false;
        rb.constraints = RigidbodyConstraints.None;

        Vector3 forward = cam.transform.forward;
        transform.SetPositionAndRotation(cam.transform.position + forward * 0.8f,
                                         Quaternion.LookRotation(forward, Vector3.up));

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
        Unstick();
        armed = false;

        if (!rb.isKinematic)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
        transform.SetPositionAndRotation(startPos, startRot);

        if (hoverUntilGrabbed)
        {
            hovering = true;
            rb.constraints = RigidbodyConstraints.FreezeAll;
        }
    }

    // ---------------------------------------------------------------- Modèle provisoire

    [ContextMenu("Générer un modèle provisoire")]
    void BuildPlaceholder()
    {
        // Repart de zéro si un ancien modèle provisoire existe
        Transform old = transform.Find("Model");
        if (old != null) DestroyImmediate(old.gameObject);

        Transform model = new GameObject("Model").transform;
        model.SetParent(transform, false);

        Material wood = MakeMaterial(new Color(0.45f, 0.30f, 0.15f));
        Material metal = MakeMaterial(new Color(0.65f, 0.70f, 0.75f));

        // Hampe : cylindre couché le long de Z (1,6 m de long, 3 cm de diamètre)
        MakePart(PrimitiveType.Cylinder, "Shaft", model,
                 Vector3.zero, Quaternion.Euler(90f, 0f, 0f),
                 new Vector3(0.03f, 0.8f, 0.03f), wood);

        // Pointe : ellipsoïde allongé
        MakePart(PrimitiveType.Sphere, "Head", model,
                 new Vector3(0f, 0f, 0.9f), Quaternion.identity,
                 new Vector3(0.07f, 0.07f, 0.3f), metal);

        // Deux ardillons
        MakePart(PrimitiveType.Cube, "BarbRight", model,
                 new Vector3(0.04f, 0f, 0.78f), Quaternion.Euler(0f, -25f, 0f),
                 new Vector3(0.015f, 0.02f, 0.14f), metal);
        MakePart(PrimitiveType.Cube, "BarbLeft", model,
                 new Vector3(-0.04f, 0f, 0.78f), Quaternion.Euler(0f, 25f, 0f),
                 new Vector3(0.015f, 0.02f, 0.14f), metal);

        // Point de détection tout au bout de la pointe
        Transform tipT = transform.Find("Tip");
        if (tipT == null)
        {
            tipT = new GameObject("Tip").transform;
            tipT.SetParent(transform, false);
        }
        tipT.localPosition = new Vector3(0f, 0f, 1.05f);
        tip = tipT;

        // Un seul collider capsule le long de Z pour la physique
        CapsuleCollider cap = GetComponent<CapsuleCollider>();
        if (cap == null) cap = gameObject.AddComponent<CapsuleCollider>();
        cap.direction = 2;
        cap.radius = 0.04f;
        cap.height = 1.85f;
        cap.center = new Vector3(0f, 0f, 0.125f);

        GetComponent<Rigidbody>().mass = 0.6f;

        Debug.Log("Harpoon: modèle provisoire généré. Pense à ajouter un XR Grab Interactable.", this);
    }

    static void MakePart(PrimitiveType type, string partName, Transform parent,
                         Vector3 pos, Quaternion rot, Vector3 scale, Material mat)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = partName;

        Collider col = go.GetComponent<Collider>();
        if (col != null) DestroyImmediate(col);

        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = rot;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = mat;
    }

    static Material MakeMaterial(Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        Material m = new Material(shader);
        m.color = color;
        return m;
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