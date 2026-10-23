using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Nage pour XR Origin (Hands) : ZQSD au clavier + brasse avec les mains.
///
/// À placer sur la RACINE de l'XR Origin.
///
/// CLAVIER (si un clavier est branché) :
///   Z / S = avancer / reculer dans la direction du regard (nage en 3D)
///   Q / D = déplacement latéral
///   Espace = monter, Ctrl gauche = descendre
///
/// MAINS (brasse) :
///   Chaque main se comporte comme une palette : quand elle bouge perpendiculairement à sa paume,
///   elle repousse l'eau, et le corps est propulsé dans le sens opposé.
///   Une main qui glisse "de chant" (paume parallèle au mouvement) ne propulse presque pas :
///   le retour des bras en brasse ne te fait donc pas reculer.
///
/// IMPORTANT : désactive les autres locomotions de l'XR Origin (Continuous Move, Gravity,
/// Teleportation...) pour qu'elles ne se battent pas avec la nage.
/// </summary>
public class SwimLocomotion : MonoBehaviour
{
    [Header("Références (auto-détectées si vides)")]
    [Tooltip("La tête du joueur (Main Camera)")]
    public Transform head;
    [Tooltip("Transform de la paume gauche (ex : L_Palm sous Left Hand Tracking)")]
    public Transform leftHand;
    [Tooltip("Transform de la paume droite (ex : R_Palm sous Right Hand Tracking)")]
    public Transform rightHand;

    [Header("Clavier")]
    public bool keyboardEnabled = true;
    [Tooltip("Accélération au clavier (m/s²). Vitesse max atteinte ≈ accélération / waterDrag.")]
    public float keyboardAcceleration = 4f;
    [Tooltip("Détecte le XR Device Simulator (qui utilise déjà ZQSD) et passe alors sur les flèches + PageUp/PageDown")]
    public bool autoDetectSimulator = true;
    [Tooltip("Force les flèches (haut/bas/gauche/droite) + PageUp (monter) / PageDown (descendre) au lieu de ZQSD")]
    public bool useArrowKeys = false;

    [Header("Mains (brasse)")]
    public bool handsEnabled = true;
    [Tooltip("Axe LOCAL de la paume, perpendiculaire à la main (Y pour XR Hands). Le sens n'a pas d'importance.")]
    public Vector3 palmNormalAxis = Vector3.up;
    [Tooltip("Puissance de la nage : plus haut = chaque mouvement propulse plus fort")]
    public float strokePower = 4f;
    [Tooltip("Vitesse minimale (m/s) pour qu'un mouvement de main compte. Évite que les tremblements propulsent.")]
    public float minStrokeSpeed = 0.35f;
    [Tooltip("Lissage de la vitesse des mains (plus haut = plus réactif)")]
    public float handSmoothing = 20f;
    [Tooltip("Au-delà de cette vitesse, c'est un saut de tracking et il est ignoré")]
    public float maxHandSpeed = 8f;
    [Tooltip("Écrit dans la console, chaque seconde, les paumes trouvées, la vitesse des mains et la poussée")]
    public bool debugLog = false;

    [Header("Eau")]
    [Tooltip("Frottement de l'eau : plus haut = on s'arrête plus vite")]
    public float waterDrag = 1.5f;
    public float maxSpeed = 3f;

    [Header("Surface")]
    [Tooltip("Empêche la tête de sortir de l'eau")]
    public bool limitToSurface = false;
    [Tooltip("Hauteur Y de la surface de l'eau")]
    public float waterSurfaceY = 0f;
    [Tooltip("Optionnel : un objet placé à la surface de l'eau (ex : ton spawner). Prioritaire sur waterSurfaceY.")]
    public Transform waterSurface;
    [Tooltip("De combien la tête peut dépasser la surface")]
    public float headAboveSurfaceMargin = 0.3f;

    [Header("Collisions (terrain, rochers...)")]
    public bool collisionsEnabled = true;
    [Tooltip("Layers qui bloquent le joueur. Exclure l'eau (si elle a un collider) et éventuellement les poissons.")]
    public LayerMask collisionMask = ~0;
    [Tooltip("Rayon de la sphère qui entoure la tête")]
    public float bodyRadius = 0.3f;

    private class HandState
    {
        public Transform t;
        public Vector3 lastRel;
        public Vector3 vel;
        public bool has;
    }

    private readonly HandState left = new HandState();
    private readonly HandState right = new HandState();
    private readonly RaycastHit[] hitBuffer = new RaycastHit[8];
    private Vector3 velocity;
    private bool simulatorDetected;
    private float debugTimer;
    private const float Skin = 0.02f;

    void Awake()
    {
        if (head == null && Camera.main != null) head = Camera.main.transform;
        if (head == null)
            Debug.LogError("SwimLocomotion: aucune tête trouvée (Main Camera). Assigne le champ 'head'.", this);

        if (leftHand == null || rightHand == null) AutoFindHands();

        left.t = leftHand;
        right.t = rightHand;

        if (autoDetectSimulator && SimulatorPresent())
        {
            simulatorDetected = true;
            useArrowKeys = true;
            Debug.Log("SwimLocomotion: XR Device Simulator détecté. Nage au clavier avec les flèches " +
                      "+ PageUp/PageDown (ZQSD est utilisé par le simulateur). " +
                      "La brasse avec les mains n'est pas testable dans le simulateur.", this);
        }

        if (handsEnabled && !simulatorDetected && (leftHand == null || rightHand == null))
            Debug.LogWarning("SwimLocomotion: paumes non trouvées. Assigne leftHand et rightHand " +
                             "(L_Palm / R_Palm sous les mains de l'XR Origin).", this);
    }

    static bool SimulatorPresent()
    {
#if UNITY_2022_2_OR_NEWER
        MonoBehaviour[] all = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
#else
        MonoBehaviour[] all = FindObjectsOfType<MonoBehaviour>();
#endif
        foreach (MonoBehaviour m in all)
        {
            if (m == null) continue;
            string n = m.GetType().Name;
            if (n.Contains("DeviceSimulator") || n.Contains("InteractionSimulator")) return true;
        }
        return false;
    }

    /// <summary>Cherche les transforms "L_Palm" / "R_Palm" créés par les mains XR Hands.</summary>
    void AutoFindHands()
    {
        Transform leftPalm = null, rightPalm = null, leftWrist = null, rightWrist = null;

        foreach (Transform t in GetComponentsInChildren<Transform>(true))
        {
            string n = t.name.ToLowerInvariant();
            bool isPalm = n.Contains("palm");
            bool isWrist = n.Contains("wrist");
            if (!isPalm && !isWrist) continue;

            bool isLeft = n.StartsWith("l_") || n.Contains("left");
            bool isRight = n.StartsWith("r_") || n.Contains("right");

            if (isLeft)
            {
                if (isPalm && leftPalm == null) leftPalm = t;
                if (isWrist && leftWrist == null) leftWrist = t;
            }
            else if (isRight)
            {
                if (isPalm && rightPalm == null) rightPalm = t;
                if (isWrist && rightWrist == null) rightWrist = t;
            }
        }

        // La paume est préférée ; à défaut on utilise le poignet (même orientation)
        if (leftHand == null) leftHand = leftPalm != null ? leftPalm : leftWrist;
        if (rightHand == null) rightHand = rightPalm != null ? rightPalm : rightWrist;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f || head == null) return;

        // ---- Accélérations
        Vector3 accel = Vector3.zero;
        if (keyboardEnabled) accel += KeyboardAcceleration();
        if (handsEnabled)
        {
            Vector3 aL = HandAcceleration(left, dt);
            Vector3 aR = HandAcceleration(right, dt);
            accel += aL + aR;

            if (debugLog)
            {
                debugTimer += dt;
                if (debugTimer >= 1f)
                {
                    debugTimer = 0f;
                    Debug.Log("[Swim] mains : G=" + (leftHand != null ? leftHand.name : "NON TROUVÉE") +
                              " D=" + (rightHand != null ? rightHand.name : "NON TROUVÉE") +
                              " | vitesse main G=" + left.vel.magnitude.ToString("F2") +
                              " D=" + right.vel.magnitude.ToString("F2") + " m/s" +
                              " | poussée G=" + aL.magnitude.ToString("F2") +
                              " D=" + aR.magnitude.ToString("F2") +
                              " | vitesse de nage=" + velocity.magnitude.ToString("F2") + " m/s", this);
                }
            }
        }

        // ---- Intégration + frottement de l'eau
        velocity += accel * dt;
        velocity *= Mathf.Exp(-waterDrag * dt);
        velocity = Vector3.ClampMagnitude(velocity, maxSpeed);

        // ---- Déplacement
        Translate(velocity * dt);

        // ---- Surface
        if (limitToSurface)
        {
            float surfaceY = waterSurface != null ? waterSurface.position.y : waterSurfaceY;
            float limit = surfaceY + headAboveSurfaceMargin;
            float excess = head.position.y - limit;

            // Au-delà de 3 m au-dessus de la limite, la tête n'est sans doute pas dans l'eau
            // (surface mal réglée) : on ne touche à rien plutôt que de t'envoyer au fond.
            if (excess > 0f && excess < 3f)
            {
                Translate(Vector3.down * excess);
                if (velocity.y > 0f) velocity.y = 0f;
            }
        }
    }

    // ---------------------------------------------------------------- Clavier

    Vector3 KeyboardAcceleration()
    {
        Vector3 input = ReadKeyboard(); // x = droite, y = haut, z = avant
        if (input == Vector3.zero) return Vector3.zero;

        Vector3 dir = head.forward * input.z + head.right * input.x + Vector3.up * input.y;
        if (dir.sqrMagnitude > 1f) dir.Normalize();
        return dir * keyboardAcceleration;
    }

    Vector3 ReadKeyboard()
    {
        float x = 0f, y = 0f, z = 0f;

#if ENABLE_INPUT_SYSTEM
        Keyboard kb = Keyboard.current;
        if (kb == null) return Vector3.zero; // pas de clavier branché

        if (useArrowKeys)
        {
            // Mode simulateur : flèches + PageUp / PageDown
            if (kb[Key.UpArrow].isPressed) z += 1f;
            if (kb[Key.DownArrow].isPressed) z -= 1f;
            if (kb[Key.RightArrow].isPressed) x += 1f;
            if (kb[Key.LeftArrow].isPressed) x -= 1f;
            if (kb[Key.PageUp].isPressed) y += 1f;
            if (kb[Key.PageDown].isPressed) y -= 1f;
        }
        else
        {
            // Les touches du nouvel Input System sont repérées par leur position physique (clavier US) :
            // sur un clavier AZERTY, W = Z, A = Q.
            if (kb[Key.W].isPressed) z += 1f; // Z
            if (kb[Key.S].isPressed) z -= 1f; // S
            if (kb[Key.D].isPressed) x += 1f; // D
            if (kb[Key.A].isPressed) x -= 1f; // Q
            if (kb[Key.Space].isPressed) y += 1f;
            if (kb[Key.LeftCtrl].isPressed) y -= 1f;
        }
#else
        if (useArrowKeys)
        {
            if (Input.GetKey(KeyCode.UpArrow)) z += 1f;
            if (Input.GetKey(KeyCode.DownArrow)) z -= 1f;
            if (Input.GetKey(KeyCode.RightArrow)) x += 1f;
            if (Input.GetKey(KeyCode.LeftArrow)) x -= 1f;
            if (Input.GetKey(KeyCode.PageUp)) y += 1f;
            if (Input.GetKey(KeyCode.PageDown)) y -= 1f;
        }
        else
        {
            if (Input.GetKey(KeyCode.Z)) z += 1f;
            if (Input.GetKey(KeyCode.S)) z -= 1f;
            if (Input.GetKey(KeyCode.D)) x += 1f;
            if (Input.GetKey(KeyCode.Q)) x -= 1f;
            if (Input.GetKey(KeyCode.Space)) y += 1f;
            if (Input.GetKey(KeyCode.LeftControl)) y -= 1f;
        }
#endif

        return new Vector3(x, y, z);
    }

    // ---------------------------------------------------------------- Brasse

    Vector3 HandAcceleration(HandState h, float dt)
    {
        // Main non suivie (tracking perdu) : on réinitialise
        if (h.t == null || !h.t.gameObject.activeInHierarchy)
        {
            h.has = false;
            return Vector3.zero;
        }

        // Position de la main par rapport à la tête, exprimée dans les axes de l'XR Origin :
        // ni nos déplacements, ni les rotations de l'origine, ni le mouvement de la tête
        // ne sont comptés comme des mouvements de main.
        Vector3 rel = transform.InverseTransformDirection(h.t.position - head.position);

        if (!h.has)
        {
            h.lastRel = rel;
            h.vel = Vector3.zero;
            h.has = true;
            return Vector3.zero;
        }

        Vector3 raw = (rel - h.lastRel) / dt;
        h.lastRel = rel;

        if (raw.magnitude > maxHandSpeed) // saut de tracking
        {
            h.vel = Vector3.zero;
            return Vector3.zero;
        }

        h.vel = Vector3.Lerp(h.vel, raw, 1f - Mathf.Exp(-handSmoothing * dt));
        Vector3 handVelWorld = transform.TransformDirection(h.vel);

        // Modèle de palette : seule la vitesse perpendiculaire à la paume repousse l'eau.
        Vector3 n = h.t.TransformDirection(palmNormalAxis).normalized;
        float vn = Vector3.Dot(handVelWorld, n);

        float effective = Mathf.Max(0f, Mathf.Abs(vn) - minStrokeSpeed) * Mathf.Sign(vn);

        // La poussée est opposée au mouvement de la main (sans dépendre du sens de n)
        return -n * effective * strokePower;
    }

    // ---------------------------------------------------------------- Déplacement + collisions

    /// <summary>Déplace l'XR Origin en glissant le long des obstacles. Les poussées contre un mur sont annulées.</summary>
    void Translate(Vector3 delta)
    {
        if (!collisionsEnabled)
        {
            transform.position += delta;
            return;
        }

        for (int i = 0; i < 2; i++)
        {
            float dist = delta.magnitude;
            if (dist < 1e-5f) return;
            Vector3 dir = delta / dist;

            if (CastBody(dir, dist + Skin, out RaycastHit hit))
            {
                float allowed = Mathf.Max(0f, hit.distance - Skin);
                Vector3 step = dir * allowed;
                transform.position += step;

                // On glisse le long de la surface avec le reste du mouvement
                delta = Vector3.ProjectOnPlane(delta - step, hit.normal);
                velocity = Vector3.ProjectOnPlane(velocity, hit.normal);
            }
            else
            {
                transform.position += delta;
                return;
            }
        }
    }

    bool CastBody(Vector3 dir, float distance, out RaycastHit best)
    {
        best = default;
        int count = Physics.SphereCastNonAlloc(head.position, bodyRadius, dir, hitBuffer,
                                               distance, collisionMask,
                                               QueryTriggerInteraction.Ignore);

        float bestDist = float.MaxValue;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            RaycastHit h = hitBuffer[i];
            if (h.collider.transform.IsChildOf(transform)) continue; // le joueur lui-même

            if (h.distance < bestDist)
            {
                bestDist = h.distance;
                best = h;
                found = true;
            }
        }
        return found;
    }

    // ---------------------------------------------------------------- Debug

    void OnDrawGizmosSelected()
    {
        // Axe de la paume (doit être perpendiculaire à la main : si c'est dans le plan de la main, change palmNormalAxis)
        Gizmos.color = Color.red;
        DrawPalm(leftHand);
        DrawPalm(rightHand);

        if (head != null)
        {
            Gizmos.color = new Color(0f, 0.8f, 1f, 0.5f);
            Gizmos.DrawWireSphere(head.position, bodyRadius);
        }
    }

    void DrawPalm(Transform t)
    {
        if (t == null) return;
        Vector3 n = t.TransformDirection(palmNormalAxis).normalized * 0.15f;
        Gizmos.DrawLine(t.position - n, t.position + n);
        Gizmos.DrawSphere(t.position, 0.01f);
    }
}