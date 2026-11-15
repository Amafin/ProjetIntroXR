using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// Verrou global du joueur. Pendant le changement de bonbonne, le joueur est figé sur place
/// et ne peut rien faire (nager, saisir, harponner, identifier).
/// </summary>
public static class PlayerLock
{
    /// <summary>Vrai pendant le changement de bonbonne : aucune action possible.</summary>
    public static bool Active;
    /// <summary>Vrai quand la bonbonne est vide : on ne peut plus nager tant qu'elle n'est pas changée.</summary>
    public static bool SwimBlocked;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        Active = false;
        SwimBlocked = false;
    }
}

/// <summary>
/// Règles de la plongée : bonbonne d'air, identification des poissons, objectifs.
/// L'affichage est fait par DiveHUD, qui lit l'état exposé ici.
///
/// Rien à installer : créé automatiquement au lancement si la scène contient un FishSpawner.
///
/// BOUTONS (manettes)                                  CLAVIER (tests)
///   B (droite) ou Y (gauche) : changer la bonbonne      touche O (ou B)
///   A (droite) ou X (gauche) : scanner le poisson       touche P (ou I)
///                              que l'on regarde
///
/// IDENTIFICATION : on regarde un poisson, on appuie, un cercle se remplit pendant le scan
/// (il faut garder le poisson dans le regard), puis sa fiche apparaît en haut à droite.
/// La toute première fois qu'on fixe un poisson 2 secondes, le jeu explique la touche ; ensuite plus rien.
/// </summary>
public class DiveGame : MonoBehaviour
{
    public enum ObjectiveType { Swim, Catch, Identify }

    public class Objective
    {
        public ObjectiveType type;
        public FishSpecies species;
        public float target;
        public float progress;

        public bool Done => progress >= target;
        public float Ratio => target > 0f ? Mathf.Clamp01(progress / target) : 1f;

        public string Title
        {
            get
            {
                switch (type)
                {
                    case ObjectiveType.Swim:
                        return "Nager " + Mathf.RoundToInt(target) + " m";
                    case ObjectiveType.Catch:
                        return "Pêcher " + Count(species);
                    default:
                        return "Identifier " + Count(species);
                }
            }
        }

        /// <summary>« 1 poisson-ange empereur », « 3 poissons-anges empereurs », ou « 4 poissons » si toute espèce convient.</summary>
        string Count(FishSpecies s)
        {
            int n = Mathf.RoundToInt(target);
            if (s == null) return n + (n > 1 ? " poissons" : " poisson");
            return n + " " + (n > 1 ? s.pluralName : s.displayName.ToLowerInvariant());
        }

        public string ProgressText
        {
            get
            {
                if (type == ObjectiveType.Swim)
                    return Mathf.FloorToInt(Mathf.Min(progress, target)) + "/" + Mathf.RoundToInt(target) + " m";
                return Mathf.FloorToInt(Mathf.Min(progress, target)) + "/" + Mathf.RoundToInt(target);
            }
        }
    }

    [Header("Bonbonne d'air")]
    [Tooltip("Durée d'une bonbonne quand on ne bouge pas (secondes)")]
    public float tankDuration = 180f;
    [Tooltip("Temps de survie une fois la bonbonne vide (secondes). Passé ce délai, la partie est perdue.")]
    public float suffocationTime = 5f;
    [Tooltip("Surconsommation à pleine vitesse de nage : 2 = l'air part 3 fois plus vite à vitesse max")]
    public float speedDrainFactor = 2f;
    [Tooltip("Durée du changement de bonbonne, pendant laquelle le joueur est figé (secondes)")]
    public float changeDuration = 3f;
    [Tooltip("En dessous de cette part d'air, la jauge passe en alerte")]
    [Range(0f, 1f)] public float lowAirRatio = 0.25f;
    [Tooltip("On ne peut changer la bonbonne que si elle est descendue sous cette part (1 = n'importe quand)")]
    [Range(0f, 1f)] public float changeAllowedBelow = 1f;

    [Header("Identification des poissons")]
    [Tooltip("Distance maximale à laquelle on peut scanner un poisson (m)")]
    public float identifyRange = 90f;
    [Tooltip("Tolérance du regard, en degrés autour du centre de la vue. Le poisson n'a pas besoin d'être " +
             "pile au centre : il suffit que son corps entre dans ce cône. Plus grand = plus indulgent.")]
    [Range(1f, 45f)] public float gazeAngle = 14f;
    [Tooltip("Pendant un scan, le cône est élargi d'autant : le poisson qui bouge reste accroché")]
    public float scanAngleBonus = 1.8f;
    [Tooltip("Le corps du poisson compte comme plus gros que son collider (1.5 = moitié plus gros)")]
    public float fishSizeBonus = 1.5f;
    [Tooltip("Le décor (corail, terrain) entre le joueur et le poisson empêche le scan")]
    public bool sceneryBlocksGaze = true;
    [Tooltip("Durée du scan d'un poisson : il faut le garder dans le regard pendant ce temps (secondes)")]
    public float scanDuration = 1.5f;
    [Tooltip("Tolérance : temps pendant lequel on peut perdre le poisson du regard sans annuler le scan (secondes)")]
    public float scanGazeGrace = 0.8f;
    [Tooltip("Durée d'affichage de la fiche du poisson (secondes)")]
    public float popupDuration = 6f;
    [Tooltip("Temps à fixer le PREMIER poisson avant que le jeu explique comment le scanner (secondes)")]
    public float tutorialGazeTime = 2f;
    [Tooltip("Durée d'affichage de cette explication (secondes). Elle n'apparaît qu'une seule fois.")]
    public float tutorialDuration = 6f;
    public LayerMask gazeMask = ~0;

    [Header("Espèces protégées")]
    [Tooltip("Nombre de poissons protégés tués qui fait perdre la partie. Les précédents ne valent qu'un avertissement.")]
    public int protectedKillsToLose = 3;
    [Tooltip("Durée d'affichage de l'avertissement (secondes)")]
    public float warningDuration = 6f;
    [Tooltip("Délai avant de pouvoir recommencer après une partie perdue (secondes) : évite de zapper l'écran par erreur")]
    public float restartDelay = 2f;

    [Header("Objectifs")]
    [Tooltip("Distance de l'objectif « nager » (m)")]
    public float swimDistance = 100f;
    [Tooltip("Nombre de poissons à pêcher (min / max, tiré au hasard)")]
    public Vector2Int catchCount = new Vector2Int(1, 5);
    [Tooltip("Nombre de poissons DIFFÉRENTS à identifier (min / max, tiré au hasard)")]
    public Vector2Int identifyCount = new Vector2Int(1, 4);
    [Tooltip("Chance qu'un objectif d'identification porte sur une espèce protégée")]
    [Range(0f, 1f)] public float identifyProtectedChance = 0.6f;
    [Tooltip("Chance qu'un objectif d'identification accepte n'importe quelle espèce (« Identifier 4 poissons »)")]
    [Range(0f, 1f)] public float identifyAnyChance = 0.3f;
    [Tooltip("Durée d'affichage de « Objectif terminé » avant de passer au suivant (secondes)")]
    public float completedPause = 2f;

    // ---------------------------------------------------------------- État lu par l'interface

    public static DiveGame Instance { get; private set; }

    /// <summary>Air restant, de 0 (vide) à 1 (pleine).</summary>
    public float Air01 { get; private set; } = 1f;
    public bool TankEmpty => Air01 <= 0f;
    public bool LowAir => Air01 <= lowAirRatio;
    public bool IsChangingTank { get; private set; }
    /// <summary>Avancement du changement de bonbonne, de 0 à 1.</summary>
    public float ChangeProgress { get; private set; }

    /// <summary>Poisson actuellement regardé (null si aucun).</summary>
    public Fish GazedFish { get; private set; }
    /// <summary>Espèce affichée dans la fiche en haut à droite (null si la fiche est fermée).</summary>
    public FishSpecies PopupSpecies { get; private set; }
    public float PopupTimeLeft { get; private set; }
    /// <summary>Augmente à chaque identification : sert à relancer l'animation de la fiche.</summary>
    public int PopupSerial { get; private set; }

    /// <summary>Nombre de poissons d'espèce protégée tués depuis le début de la partie.</summary>
    public int ProtectedKills { get; private set; }
    /// <summary>Temps restant d'affichage de l'avertissement « espèce protégée » (0 = masqué).</summary>
    public float WarningTimeLeft { get; private set; }
    /// <summary>La partie est perdue (trop d'espèces protégées tuées).</summary>
    public bool GameOver { get; private set; }
    /// <summary>Phrase expliquant pourquoi la partie est perdue.</summary>
    public string GameOverReason { get; private set; } = "";
    /// <summary>Asphyxie : 0 tant qu'il reste de l'air, puis monte jusqu'à 1 (mort) une fois la bonbonne vide.</summary>
    public float Suffocation01 { get; private set; }
    /// <summary>Vrai quand on peut appuyer pour recommencer.</summary>
    public bool CanRestart => GameOver && gameOverTime >= restartDelay;

    /// <summary>Un scan est en cours.</summary>
    public bool IsScanning => scanTarget != null;
    /// <summary>Le poisson en cours de scan (null si aucun).</summary>
    public Fish ScanTarget => scanTarget;
    /// <summary>Avancement du scan, de 0 à 1.</summary>
    public float ScanProgress { get; private set; }
    /// <summary>Temps restant d'affichage de l'explication « appuie pour scanner » (0 = masquée).</summary>
    public float TutorialTimeLeft { get; private set; }

    public Objective Current { get; private set; }
    public int CompletedCount { get; private set; }
    /// <summary>Temps restant d'affichage de « Objectif terminé » (0 = objectif en cours).</summary>
    public float CompletedTimeLeft { get; private set; }

    private InputAction changeTankAction;
    private InputAction identifyAction;
    private Fish scanTarget;
    private float scanLostTime;
    private Fish gazeTimerFish;
    private float gazeTimer;
    private bool tutorialShown;
    private float gameOverTime;
    private Transform head;
    private SwimLocomotion swim;
    private Vector3 lastPlayerPos;
    private bool hasLastPlayerPos;
    private ObjectiveType lastType = ObjectiveType.Identify;
    private readonly RaycastHit[] gazeHits = new RaycastHit[16];
    // Poissons déjà comptés pour l'objectif d'identification en cours (le même ne compte qu'une fois)
    private readonly System.Collections.Generic.HashSet<Fish> identified = new System.Collections.Generic.HashSet<Fish>();

    // ---------------------------------------------------------------- Création automatique

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        // Aussi après chaque rechargement de scène (« recommencer » après une partie perdue)
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Ensure();
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Ensure();
    }

    static void Ensure()
    {
        if (FindAnyObjectByType<DiveGame>() != null) return;
        if (FindAnyObjectByType<FishSpawner>() == null) return; // pas une scène de chasse

        new GameObject("Dive Game (auto)").AddComponent<DiveGame>();
    }

    void Awake()
    {
        Instance = this;

        changeTankAction = new InputAction("Changer la bonbonne", InputActionType.Button);
        changeTankAction.AddBinding("<XRController>{RightHand}/secondaryButton"); // B
        changeTankAction.AddBinding("<XRController>{LeftHand}/secondaryButton");  // Y
        changeTankAction.AddBinding("<Keyboard>/o");
        changeTankAction.AddBinding("<Keyboard>/b");

        identifyAction = new InputAction("Identifier le poisson", InputActionType.Button);
        identifyAction.AddBinding("<XRController>{RightHand}/primaryButton");     // A
        identifyAction.AddBinding("<XRController>{LeftHand}/primaryButton");      // X
        identifyAction.AddBinding("<Keyboard>/p");
        identifyAction.AddBinding("<Keyboard>/i");

        if (GetComponent<DiveHUD>() == null) gameObject.AddComponent<DiveHUD>();
        if (GetComponent<DiveAudio>() == null) gameObject.AddComponent<DiveAudio>();
    }

    void OnEnable()
    {
        changeTankAction.Enable();
        identifyAction.Enable();
    }

    void OnDisable()
    {
        changeTankAction.Disable();
        identifyAction.Disable();
        PlayerLock.Active = false;
        PlayerLock.SwimBlocked = false;
    }

    void OnDestroy()
    {
        changeTankAction.Dispose();
        identifyAction.Dispose();
        if (Instance == this) Instance = null;
    }

    void Start()
    {
        if (Camera.main != null) head = Camera.main.transform;
        swim = FindAnyObjectByType<SwimLocomotion>();
        NextObjective();
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (head == null && Camera.main != null) head = Camera.main.transform;

        if (WarningTimeLeft > 0f) WarningTimeLeft = Mathf.Max(0f, WarningTimeLeft - dt);

        // Partie perdue : tout est figé, on attend que le joueur recommence
        if (GameOver)
        {
            gameOverTime += Time.unscaledDeltaTime;
            if (CanRestart && (identifyAction.WasPressedThisFrame() || changeTankAction.WasPressedThisFrame()))
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
            return;
        }

        float moved = MeasureMovement();

        UpdateTank(dt);
        UpdateGaze();
        UpdateIdentify(dt);
        UpdateObjectives(dt, moved);
    }

    // ---------------------------------------------------------------- Déplacement du joueur

    /// <summary>Distance parcourue à la nage depuis la dernière image (m).</summary>
    float MeasureMovement()
    {
        Transform player = swim != null ? swim.transform : head;
        if (player == null) return 0f;

        Vector3 pos = player.position;
        float moved = hasLastPlayerPos ? Vector3.Distance(pos, lastPlayerPos) : 0f;
        lastPlayerPos = pos;
        hasLastPlayerPos = true;

        // Une téléportation ou un replacement n'est pas de la nage
        return moved > 2f ? 0f : moved;
    }

    // ---------------------------------------------------------------- Bonbonne

    void UpdateTank(float dt)
    {
        if (IsChangingTank)
        {
            ChangeProgress += dt / Mathf.Max(0.1f, changeDuration);
            if (ChangeProgress >= 1f)
            {
                // Bonbonne neuve : le joueur est libéré
                IsChangingTank = false;
                ChangeProgress = 0f;
                Air01 = 1f;
                PlayerLock.Active = false;
            }
        }
        else
        {
            // L'air part plus vite quand on nage vite
            float speed01 = swim != null && swim.maxSpeed > 0f
                ? Mathf.Clamp01(SwimLocomotion.PlayerSpeed / swim.maxSpeed)
                : 0f;
            float drain = (1f + speedDrainFactor * speed01) / Mathf.Max(1f, tankDuration);
            Air01 = Mathf.Max(0f, Air01 - drain * dt);

            if (changeTankAction.WasPressedThisFrame() && Air01 <= changeAllowedBelow && Air01 < 0.999f)
            {
                IsChangingTank = true;
                ChangeProgress = 0f;
                PlayerLock.Active = true;
            }
        }

        // Bonbonne vide : il reste quelques secondes pour la changer, sinon c'est l'asphyxie.
        // Le compte à rebours s'arrête dès que le changement est lancé.
        if (TankEmpty && !IsChangingTank)
        {
            Suffocation01 += dt / Mathf.Max(0.1f, suffocationTime);
            if (Suffocation01 >= 1f)
            {
                Suffocation01 = 1f;
                Lose("Tu as manqué d'air.\nChange ta bonbonne avant qu'elle soit vide.");
            }
        }
        else
        {
            Suffocation01 = 0f;
        }

        PlayerLock.SwimBlocked = false; // on peut encore nager : c'est le manque d'air qui tue
    }

    /// <summary>Fin de partie : tout est figé, l'écran de fin s'affiche.</summary>
    void Lose(string reason)
    {
        if (GameOver) return;

        GameOver = true;
        GameOverReason = reason;
        gameOverTime = 0f;
        WarningTimeLeft = 0f;
        CancelScan();
        IsChangingTank = false;
        PlayerLock.Active = true; // plus aucune action possible
    }

    // ---------------------------------------------------------------- Identification

    /// <summary>
    /// Cherche le poisson regardé. Visée indulgente : on prend, parmi les poissons à portée, celui dont
    /// le corps est le plus proche du centre de la vue, à l'intérieur d'un cône de gazeAngle degrés.
    /// La taille apparente du poisson est comptée : un gros poisson proche est très facile à viser.
    /// </summary>
    void UpdateGaze()
    {
        GazedFish = null;
        if (head == null) return;

        Vector3 origin = head.position;
        Vector3 forward = head.forward;
        float bestScore = float.MaxValue;
        Fish best = null;

        IReadOnlyList<Fish> all = Fish.All;
        for (int i = 0; i < all.Count; i++)
        {
            Fish f = all[i];
            if (f == null) continue;

            Bounds bounds = f.WorldBounds;
            Vector3 to = bounds.center - origin;
            float dist = to.magnitude;
            if (dist > identifyRange || dist < 0.01f) continue;

            // Écart entre le centre de la vue et le BORD du poisson (pas son centre)
            float radius = bounds.extents.magnitude * 0.6f * fishSizeBonus;
            float angle = Vector3.Angle(forward, to);
            float apparent = Mathf.Atan2(radius, dist) * Mathf.Rad2Deg;
            float offset = Mathf.Max(0f, angle - apparent);

            // Le poisson en cours de scan reste accroché plus facilement
            float limit = f == scanTarget ? gazeAngle * scanAngleBonus : gazeAngle;
            if (offset > limit) continue;

            // À écart égal, le poisson en cours de scan puis le plus proche l'emportent
            float score = offset + dist * 0.02f - (f == scanTarget ? 1000f : 0f);
            if (score >= bestScore) continue;

            if (sceneryBlocksGaze && SceneryBetween(origin, to / dist, dist - radius)) continue;

            bestScore = score;
            best = f;
        }

        GazedFish = best;
    }

    /// <summary>Vrai si un décor fixe (corail, rocher, terrain) cache le poisson.</summary>
    bool SceneryBetween(Vector3 origin, Vector3 dir, float distance)
    {
        if (distance <= 0f) return false;

        int count = Physics.RaycastNonAlloc(origin, dir, gazeHits, distance, gazeMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Collider c = gazeHits[i].collider;
            // Ni les poissons, ni les objets tenus (harpon), ni le joueur ne cachent la vue
            if (c.attachedRigidbody != null) continue;
            if (c.GetComponentInParent<Unity.XR.CoreUtils.XROrigin>() != null) continue;
            return true;
        }
        return false;
    }

    void UpdateIdentify(float dt)
    {
        if (PopupTimeLeft > 0f)
        {
            PopupTimeLeft -= dt;
            if (PopupTimeLeft <= 0f) PopupSpecies = null;
        }

        UpdateTutorial(dt);

        // Changement de bonbonne : aucune action, le scan en cours est abandonné
        if (PlayerLock.Active)
        {
            CancelScan();
            return;
        }

        // Lancement du scan : il faut regarder un poisson au moment d'appuyer
        if (!IsScanning)
        {
            if (identifyAction.WasPressedThisFrame() && GazedFish != null)
            {
                scanTarget = GazedFish;
                ScanProgress = 0f;
                scanLostTime = 0f;

                // Le joueur a trouvé la touche : l'explication n'a plus lieu d'être
                tutorialShown = true;
                TutorialTimeLeft = 0f;
            }
            return;
        }

        // Scan en cours : le poisson doit rester dans le regard (avec une petite tolérance)
        if (scanTarget == null) // poisson détruit entre-temps
        {
            CancelScan();
            return;
        }

        if (GazedFish == scanTarget)
        {
            scanLostTime = 0f;
            ScanProgress += dt / Mathf.Max(0.05f, scanDuration);
        }
        else
        {
            scanLostTime += dt;
            if (scanLostTime > scanGazeGrace)
            {
                CancelScan();
                return;
            }
        }

        if (ScanProgress >= 1f) CompleteScan();
    }

    void CancelScan()
    {
        scanTarget = null;
        ScanProgress = 0f;
        scanLostTime = 0f;
    }

    /// <summary>Scan terminé : la fiche du poisson apparaît et l'objectif d'identification avance.</summary>
    void CompleteScan()
    {
        Fish fish = scanTarget;
        CancelScan();

        PopupSpecies = fish.Species;
        PopupTimeLeft = popupDuration;
        PopupSerial++;

        // species == null : n'importe quelle espèce convient
        if (Current != null && CompletedTimeLeft <= 0f && Current.type == ObjectiveType.Identify &&
            (Current.species == null || Current.species == PopupSpecies) && identified.Add(fish))
        {
            Current.progress += 1f;
        }
    }

    /// <summary>
    /// Explication donnée une seule fois : la première fois que le joueur fixe un poisson
    /// assez longtemps sans avoir encore jamais scanné.
    /// </summary>
    void UpdateTutorial(float dt)
    {
        if (TutorialTimeLeft > 0f) TutorialTimeLeft = Mathf.Max(0f, TutorialTimeLeft - dt);
        if (tutorialShown) return;

        if (GazedFish != null && GazedFish == gazeTimerFish)
        {
            gazeTimer += dt;
            if (gazeTimer >= tutorialGazeTime)
            {
                tutorialShown = true;
                TutorialTimeLeft = tutorialDuration;
            }
        }
        else
        {
            gazeTimerFish = GazedFish;
            gazeTimer = 0f;
        }
    }

    // ---------------------------------------------------------------- Espèces protégées

    /// <summary>
    /// Appelé quand un poisson est tué. Espèce protégée : avertissement les premières fois,
    /// partie perdue à la dernière (protectedKillsToLose).
    /// </summary>
    public void NotifyFishKilled(Fish fish)
    {
        if (GameOver || fish == null || !fish.Species.isProtected) return;

        ProtectedKills++;
        if (ProtectedKills >= Mathf.Max(1, protectedKillsToLose))
        {
            Lose("Tu as pêché " + ProtectedKills + " poissons d'une espèce protégée.\n" +
                 "Un bon pêcheur ne prend que ce que le récif peut donner.");
        }
        else
        {
            WarningTimeLeft = warningDuration;
        }
    }

    // ---------------------------------------------------------------- Objectifs

    /// <summary>Appelé par le sac quand un poisson y est rangé.</summary>
    public void NotifyFishBagged(Fish fish)
    {
        if (Current == null || CompletedTimeLeft > 0f || fish == null) return;
        if (Current.type == ObjectiveType.Catch && Current.species == fish.Species)
            Current.progress += 1f;
    }

    void UpdateObjectives(float dt, float moved)
    {
        if (Current == null) return;

        // Objectif terminé : on laisse le message affiché un instant, puis on passe au suivant
        if (CompletedTimeLeft > 0f)
        {
            CompletedTimeLeft -= dt;
            if (CompletedTimeLeft <= 0f) NextObjective();
            return;
        }

        if (Current.type == ObjectiveType.Swim) Current.progress += moved;

        if (Current.Done)
        {
            CompletedCount++;
            CompletedTimeLeft = Mathf.Max(0.1f, completedPause);
        }
    }

    void NextObjective()
    {
        // Jamais deux fois de suite le même type d'objectif
        ObjectiveType type;
        do
        {
            type = (ObjectiveType)Random.Range(0, 3);
        }
        while (type == lastType);
        lastType = type;

        var o = new Objective { type = type, progress = 0f };
        switch (type)
        {
            case ObjectiveType.Swim:
                o.target = swimDistance;
                break;

            case ObjectiveType.Catch:
                o.species = FishSpecies.Default; // pour l'instant : uniquement le poisson-ange
                o.target = Random.Range(catchCount.x, Mathf.Max(catchCount.x, catchCount.y) + 1);
                break;

            default:
                identified.Clear();
                if (Random.value < identifyAnyChance)
                {
                    o.species = null; // n'importe quel poisson
                }
                else
                {
                    FishSpecies protectedSpecies = FishSpecies.RandomProtected();
                    bool pickProtected = protectedSpecies != null && Random.value < identifyProtectedChance;
                    o.species = pickProtected ? protectedSpecies : FishSpecies.All[Random.Range(0, FishSpecies.All.Length)];
                }
                o.target = Random.Range(identifyCount.x, Mathf.Max(identifyCount.x, identifyCount.y) + 1);
                break;
        }

        Current = o;
        CompletedTimeLeft = 0f;
    }
}
