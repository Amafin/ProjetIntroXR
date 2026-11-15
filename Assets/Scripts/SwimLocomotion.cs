using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Interaction.Toolkit.Inputs;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Nage pour XR Origin : joystick (principal), brasse avec les mains, ZQSD au clavier.
///
/// JOYSTICKS (le moyen principal, pour ne pas se fatiguer les bras) :
///   Gauche : avancer / reculer dans la direction du regard (on monte ou descend en regardant
///            vers le haut ou le bas), et se décaler sur les côtés.
///   Droit  : haut / bas = monter / descendre ; gauche / droite = pivoter par crans.
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
///   - Avec les MANETTES : la position vient des objets Left/Right Controller de l'XR Origin.
///     Aucun bouton à tenir : tourne les poignets pour que la paume pousse l'eau.
///   - Avec le SUIVI DES MAINS : la paume est lue directement dans XR Hands.
///   La source est choisie automatiquement à chaque image, pour chaque main.
///
/// IMPORTANT : désactive les autres locomotions de l'XR Origin (Continuous Move, Gravity,
/// Teleportation...) pour qu'elles ne se battent pas avec la nage.
/// </summary>
public class SwimLocomotion : MonoBehaviour
{
    [Header("Références (auto-détectées si vides)")]
    [Tooltip("La tête du joueur (Main Camera)")]
    public Transform head;
    [Tooltip("Espace de tracking (Camera Offset de l'XR Origin)")]
    public Transform trackingSpace;
    [Tooltip("Objet 'Left Controller' de l'XR Origin (celui qui suit la manette gauche)")]
    public Transform leftController;
    [Tooltip("Objet 'Right Controller' de l'XR Origin (celui qui suit la manette droite)")]
    public Transform rightController;

    [Header("Clavier")]
    public bool keyboardEnabled = true;
    [Tooltip("Accélération au clavier (m/s²). Vitesse max atteinte ≈ accélération / waterDrag.")]
    public float keyboardAcceleration = 4f;
    [Tooltip("Détecte le XR Device Simulator (qui utilise déjà ZQSD) et passe alors sur les flèches + PageUp/PageDown")]
    public bool autoDetectSimulator = true;
    [Tooltip("Force les flèches (haut/bas/gauche/droite) + PageUp (monter) / PageDown (descendre) au lieu de ZQSD")]
    public bool useArrowKeys = false;

    [Header("Joysticks")]
    public bool joystickEnabled = true;
    [Tooltip("Accélération au joystick poussé à fond (m/s²)")]
    public float joystickAcceleration = 5f;
    [Tooltip("En dessous de cette inclinaison, le joystick est ignoré")]
    [Range(0f, 0.9f)] public float joystickDeadZone = 0.15f;
    [Tooltip("Angle d'un cran de rotation au joystick droit (0 = pas de rotation)")]
    public float snapTurnAngle = 30f;

    [Header("Mains (brasse)")]
    public bool handsEnabled = true;
    [Tooltip("Une main qui tient un objet (harpon, poisson) ne nage pas. Après l'avoir lâché, elle reste " +
             "ignorée pendant ce délai (secondes), le temps que le geste de lancer se termine.")]
    public float releaseCooldown = 0.7f;
    [Tooltip("Multiplie la poussée de chaque mouvement de bras : plus haut = on va plus loin par mouvement")]
    public float strokeBoost = 2.2f;
    [Tooltip("Vitesse de main (m/s) nécessaire pour qu'un mouvement fasse RECULER. En dessous, la part de la " +
             "poussée dirigée vers l'arrière est ignorée : ramener les bras ne fait plus reculer.")]
    public float backwardStrokeSpeed = 1.3f;
    [Tooltip("Suivi des mains : axe LOCAL de la paume, perpendiculaire à la main (Y pour XR Hands). Le sens n'a pas d'importance.")]
    public Vector3 handPalmAxis = Vector3.up;
    [Tooltip("Manettes : axe LOCAL perpendiculaire à la paume (X = l'axe qui traverse la main quand on tient la manette). Le sens n'a pas d'importance.")]
    public Vector3 controllerPalmAxis = Vector3.right;
    [Tooltip("1 = seul le mouvement perpendiculaire à la paume propulse (brasse réaliste). 0 = tout mouvement de main propulse, quelle que soit l'orientation.")]
    [Range(0f, 1f)] public float paddleStrictness = 1f;
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
    [Tooltip("Multiplie le frottement de l'eau : plus bas = on glisse plus longtemps après chaque mouvement")]
    [Range(0.1f, 1.5f)] public float glide = 0.5f;
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

    private enum HandSource { None, Controller, TrackedHand }

    private class HandState
    {
        public bool isLeft;
        public Transform controller;
        // Ce qui permet à cette main de saisir (manette + main suivie), pour savoir si elle tient un objet
        public readonly List<IXRSelectInteractor> interactors = new List<IXRSelectInteractor>();
        public float busyUntil;
        public HandSource source;
        public Vector3 lastPos;   // dans l'espace de tracking
        public Vector3 vel;       // dans l'espace de tracking
        public bool has;
    }

    private readonly HandState left = new HandState { isLeft = true };
    private readonly HandState right = new HandState { isLeft = false };
    private static readonly List<XRHandSubsystem> handSubsystems = new List<XRHandSubsystem>();
    private XRHandSubsystem handSubsystem;
    private float handSubsystemRetry;
    private readonly RaycastHit[] hitBuffer = new RaycastHit[8];
    private Vector3 velocity;

    /// <summary>Vitesse de nage actuelle du joueur (m/s). Lue par les poissons (fuite) et la bonbonne (consommation).</summary>
    public static float PlayerSpeed { get; private set; }
    /// <summary>Effort de nage en cours, de 0 à 1 (poussée + vitesse). Sert au son de mouvement dans l'eau.</summary>
    public static float Effort { get; private set; }

    private InputAction moveStick, turnStick;
    private bool turnReady = true;

    void OnEnable()
    {
        if (moveStick == null)
        {
            moveStick = new InputAction("Nager (joystick gauche)", InputActionType.Value, "<XRController>{LeftHand}/thumbstick");
            turnStick = new InputAction("Monter / pivoter (joystick droit)", InputActionType.Value, "<XRController>{RightHand}/thumbstick");
        }
        moveStick.Enable();
        turnStick.Enable();
    }

    void OnDestroy()
    {
        if (moveStick != null) moveStick.Dispose();
        if (turnStick != null) turnStick.Dispose();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        PlayerSpeed = 0f;
        Effort = 0f;
    }

    void OnDisable()
    {
        PlayerSpeed = 0f;
        Effort = 0f;
        if (moveStick != null) moveStick.Disable();
        if (turnStick != null) turnStick.Disable();
    }
    private bool simulatorDetected;
    private float debugTimer;
    private const float Skin = 0.02f;

    void Awake()
    {
        if (head == null && Camera.main != null) head = Camera.main.transform;
        if (head == null)
            Debug.LogError("SwimLocomotion: aucune tête trouvée (Main Camera). Assigne le champ 'head'.", this);

        XROrigin origin = GetComponentInParent<XROrigin>();
        if (trackingSpace == null && origin != null && origin.CameraFloorOffsetObject != null)
            trackingSpace = origin.CameraFloorOffsetObject.transform;
        if (trackingSpace == null && head != null && head.parent != null) trackingSpace = head.parent;
        if (trackingSpace == null) trackingSpace = transform;

        if (leftController == null || rightController == null) AutoFindControllers();

        left.controller = leftController;
        right.controller = rightController;
        CollectInteractors();

        if (autoDetectSimulator && SimulatorPresent())
        {
            simulatorDetected = true;
            useArrowKeys = true;
            Debug.Log("SwimLocomotion: XR Device Simulator détecté. Nage au clavier avec les flèches " +
                      "+ PageUp/PageDown (ZQSD est utilisé par le simulateur). " +
                      "La brasse avec les mains n'est pas testable dans le simulateur.", this);
        }

        if (handsEnabled && !simulatorDetected && (leftController == null || rightController == null))
            Debug.LogWarning("SwimLocomotion: objets des manettes non trouvés. Assigne leftController et " +
                             "rightController (Left Controller / Right Controller sous Camera Offset). " +
                             "Seul le suivi des mains permettra de nager.", this);
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

    /// <summary>
    /// Cherche les objets qui suivent les manettes : ceux déclarés dans l'XR Input Modality Manager
    /// de l'XR Origin, sinon les objets nommés "Left Controller" / "Right Controller".
    /// </summary>
    void AutoFindControllers()
    {
        XRInputModalityManager modality = GetComponentInChildren<XRInputModalityManager>(true);
        if (modality != null)
        {
            if (leftController == null && modality.leftController != null)
                leftController = modality.leftController.transform;
            if (rightController == null && modality.rightController != null)
                rightController = modality.rightController.transform;
        }

        if (leftController != null && rightController != null) return;

        foreach (Transform t in GetComponentsInChildren<Transform>(true))
        {
            string n = t.name.ToLowerInvariant();
            if (!n.Contains("controller") || n.Contains("visual") || n.Contains("stabilized") || n.Contains("teleport"))
                continue;

            if (leftController == null && n.StartsWith("left")) leftController = t;
            else if (rightController == null && n.StartsWith("right")) rightController = t;
        }
    }

    /// <summary>
    /// Position (dans l'espace de tracking) et normale de la paume (monde) d'une main,
    /// depuis la manette si elle est active, sinon depuis le suivi des mains.
    /// </summary>
    bool TryGetPalm(HandState h, out Vector3 trackingPos, out Vector3 worldNormal, out HandSource source)
    {
        // Manette (l'XR Input Modality Manager désactive l'objet quand on passe au suivi des mains)
        if (h.controller != null && h.controller.gameObject.activeInHierarchy)
        {
            trackingPos = trackingSpace.InverseTransformPoint(h.controller.position);
            worldNormal = h.controller.TransformDirection(controllerPalmAxis).normalized;
            source = HandSource.Controller;
            return true;
        }

        // Suivi des mains : paume lue directement dans XR Hands
        XRHandSubsystem hands = GetHandSubsystem();
        if (hands != null)
        {
            XRHand hand = h.isLeft ? hands.leftHand : hands.rightHand;
            if (hand.isTracked && hand.GetJoint(XRHandJointID.Palm).TryGetPose(out Pose palm))
            {
                trackingPos = palm.position;
                worldNormal = trackingSpace.TransformDirection(palm.rotation * handPalmAxis).normalized;
                source = HandSource.TrackedHand;
                return true;
            }
        }

        trackingPos = default;
        worldNormal = default;
        source = HandSource.None;
        return false;
    }

    XRHandSubsystem GetHandSubsystem()
    {
        if (handSubsystem != null && handSubsystem.running) return handSubsystem;

        // Pas encore démarré (ou pas de suivi des mains) : on ne recherche qu'une fois par seconde
        if (Time.unscaledTime < handSubsystemRetry) return null;
        handSubsystemRetry = Time.unscaledTime + 1f;

        handSubsystem = null;
        SubsystemManager.GetSubsystems(handSubsystems);
        for (int i = 0; i < handSubsystems.Count; i++)
        {
            if (handSubsystems[i].running)
            {
                handSubsystem = handSubsystems[i];
                break;
            }
        }
        return handSubsystem;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f || head == null) return;

        // ---- Changement de bonbonne : le joueur est figé sur place
        if (PlayerLock.Active)
        {
            velocity = Vector3.zero;
            PlayerSpeed = 0f;
            Effort = 0f;
            left.has = false;   // pas d'élan accumulé pendant l'arrêt
            right.has = false;
            return;
        }

        // ---- Accélérations
        Vector3 accel = Vector3.zero;
        if (keyboardEnabled) accel += KeyboardAcceleration();
        if (joystickEnabled) accel += JoystickAcceleration();
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
                    Debug.Log("[Swim] sources : G=" + left.source + " D=" + right.source +
                              " | vitesse main G=" + left.vel.magnitude.ToString("F2") +
                              " D=" + right.vel.magnitude.ToString("F2") + " m/s" +
                              " | poussée G=" + aL.magnitude.ToString("F2") +
                              " D=" + aR.magnitude.ToString("F2") +
                              " | vitesse de nage=" + velocity.magnitude.ToString("F2") + " m/s", this);
                }
            }
        }

        // ---- Bonbonne vide : plus de poussée (on continue seulement sur son élan)
        if (PlayerLock.SwimBlocked) accel = Vector3.zero;

        // ---- Intégration + frottement de l'eau
        velocity += accel * dt;
        velocity *= Mathf.Exp(-waterDrag * glide * dt);
        velocity = Vector3.ClampMagnitude(velocity, maxSpeed);

        // ---- Déplacement
        Translate(velocity * dt);
        PlayerSpeed = velocity.magnitude;
        Effort = Mathf.Clamp01(Mathf.Max(accel.magnitude / 8f, maxSpeed > 0f ? PlayerSpeed / maxSpeed : 0f));

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

    // ---------------------------------------------------------------- Joysticks

    Vector3 JoystickAcceleration()
    {
        Vector2 move = DeadZone(moveStick.ReadValue<Vector2>());
        Vector2 aux = DeadZone(turnStick.ReadValue<Vector2>());

        // Le joystick d'une main qui tient un objet est ignoré : en armant ou en lançant le harpon,
        // le pouce le pousse sans le vouloir (on plongeait en ramenant le bras).
        if (HandIsBusy(left)) move = Vector2.zero;
        if (HandIsBusy(right)) aux = Vector2.zero;

        // Joystick droit, gauche / droite : pivote d'un cran, puis attend qu'on relâche
        if (snapTurnAngle > 0f)
        {
            if (turnReady && Mathf.Abs(aux.x) > 0.7f && Mathf.Abs(aux.x) > Mathf.Abs(aux.y))
            {
                float angle = Mathf.Sign(aux.x) * snapTurnAngle;
                transform.RotateAround(head.position, Vector3.up, angle); // autour de la tête : pas de saut de position
                velocity = Quaternion.Euler(0f, angle, 0f) * velocity;
                turnReady = false;
            }
            else if (Mathf.Abs(aux.x) < 0.3f)
            {
                turnReady = true;
            }
        }

        // Joystick gauche : dans la direction du regard. Joystick droit, haut / bas : monter / descendre.
        float vertical = Mathf.Abs(aux.y) > Mathf.Abs(aux.x) ? aux.y : 0f;
        Vector3 dir = head.forward * move.y + head.right * move.x + Vector3.up * vertical;
        if (dir.sqrMagnitude > 1f) dir.Normalize();
        return dir * joystickAcceleration;
    }

    Vector2 DeadZone(Vector2 v)
    {
        float m = v.magnitude;
        if (m <= joystickDeadZone) return Vector2.zero;
        // Repart de zéro au bord de la zone morte : dosage fin pour approcher les poissons lentement
        return v / m * Mathf.InverseLerp(joystickDeadZone, 1f, Mathf.Min(m, 1f));
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

    /// <summary>Relève les interacteurs de chaque main (sous la manette et sous la main suivie).</summary>
    void CollectInteractors()
    {
        XRInputModalityManager modality = GetComponentInChildren<XRInputModalityManager>(true);
        AddInteractors(left, leftController, modality != null && modality.leftHand != null ? modality.leftHand.transform : null);
        AddInteractors(right, rightController, modality != null && modality.rightHand != null ? modality.rightHand.transform : null);
    }

    static void AddInteractors(HandState h, Transform controller, Transform trackedHand)
    {
        h.interactors.Clear();
        if (controller != null) h.interactors.AddRange(controller.GetComponentsInChildren<IXRSelectInteractor>(true));
        if (trackedHand != null) h.interactors.AddRange(trackedHand.GetComponentsInChildren<IXRSelectInteractor>(true));
    }

    /// <summary>
    /// Vrai si la main tient un objet (harpon, poisson), ou vient de le lâcher : elle ne nage pas.
    /// Sans ça, armer le harpon derrière l'épaule ou le lancer propulsait le joueur.
    /// </summary>
    bool HandIsBusy(HandState h)
    {
        for (int i = 0; i < h.interactors.Count; i++)
        {
            IXRSelectInteractor it = h.interactors[i];
            if (it != null && it.hasSelection)
            {
                h.busyUntil = Time.time + releaseCooldown; // le geste se termine après le lâcher
                return true;
            }
        }
        return Time.time < h.busyUntil;
    }

    Vector3 HandAcceleration(HandState h, float dt)
    {
        if (HandIsBusy(h))
        {
            h.has = false;
            h.vel = Vector3.zero;
            return Vector3.zero;
        }

        // Main non suivie (tracking perdu, manette éteinte) : on réinitialise
        if (!TryGetPalm(h, out Vector3 pos, out Vector3 n, out HandSource source))
        {
            h.source = HandSource.None;
            h.has = false;
            h.vel = Vector3.zero;
            return Vector3.zero;
        }

        // Première mesure, ou passage manette <-> main : pas de vitesse fiable
        if (!h.has || source != h.source)
        {
            h.source = source;
            h.lastPos = pos;
            h.vel = Vector3.zero;
            h.has = true;
            return Vector3.zero;
        }

        // Vitesse mesurée dans l'espace de tracking : ni la nage elle-même, ni les rotations
        // de l'XR Origin ne sont comptées comme des mouvements de main.
        Vector3 raw = (pos - h.lastPos) / dt;
        h.lastPos = pos;

        if (raw.magnitude > maxHandSpeed) // saut de tracking
        {
            h.vel = Vector3.zero;
            return Vector3.zero;
        }

        h.vel = Vector3.Lerp(h.vel, raw, 1f - Mathf.Exp(-handSmoothing * dt));
        Vector3 handVelWorld = trackingSpace.TransformDirection(h.vel);

        // Modèle de palette : la vitesse perpendiculaire à la paume repousse l'eau
        // (paddleStrictness < 1 laisse aussi passer une part du mouvement "de chant").
        Vector3 push = Vector3.Lerp(handVelWorld, Vector3.Project(handVelWorld, n), paddleStrictness);

        float speed = push.magnitude;
        if (speed <= minStrokeSpeed) return Vector3.zero;

        // La poussée est opposée au mouvement de la main
        Vector3 thrust = -(push / speed) * (speed - minStrokeSpeed) * strokePower * strokeBoost;

        // Pas de recul parasite : la part de la poussée dirigée vers l'arrière (par rapport au regard)
        // n'est gardée que pour un geste franc. Ramener doucement les bras vers l'avant ne fait plus reculer.
        if (speed < backwardStrokeSpeed)
        {
            Vector3 forward = head.forward;
            float along = Vector3.Dot(thrust, forward);
            if (along < 0f) thrust -= forward * along;
        }

        return thrust;
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

            // Objets mobiles (harpon tenu en main, poissons...) : ils ne bloquent pas la nage,
            // sinon on reste coincé dès que le harpon passe près de la tête.
            if (h.collider.attachedRigidbody != null) continue;

            // Déjà en contact au départ (distance nulle) : on laisse le joueur se dégager
            if (h.distance <= 0f && h.point == Vector3.zero) continue;

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
        // Axe de la paume des manettes (doit traverser la main : sinon, change controllerPalmAxis)
        Gizmos.color = Color.red;
        DrawPalm(leftController);
        DrawPalm(rightController);

        if (head != null)
        {
            Gizmos.color = new Color(0f, 0.8f, 1f, 0.5f);
            Gizmos.DrawWireSphere(head.position, bodyRadius);
        }
    }

    void DrawPalm(Transform t)
    {
        if (t == null) return;
        Vector3 n = t.TransformDirection(controllerPalmAxis).normalized * 0.15f;
        Gizmos.DrawLine(t.position - n, t.position + n);
        Gizmos.DrawSphere(t.position, 0.01f);
    }
}