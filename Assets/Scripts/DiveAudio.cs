using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Sons de la plongée.
///   - Ambiance sous l'eau : en boucle, en permanence, à faible volume.
///   - Mouvement dans l'eau : seulement pendant un gros mouvement (le joueur nage, ou lance le harpon).
///   - Remplissage d'air    : pendant le changement de bonbonne, coupé dès qu'elle est pleine.
///   - Poissons             : très faible, placé sur les poissons les plus proches (son 3D).
///
/// Les fichiers sont dans Assets/Resources/Audio et chargés par leur nom.
/// Créé automatiquement par DiveGame.
/// </summary>
public class DiveAudio : MonoBehaviour
{
    [Header("Volumes (0 à 1)")]
    [Range(0f, 1f)] public float ambienceVolume = 0.22f;
    [Range(0f, 1f)] public float movementVolume = 0.55f;
    [Range(0f, 1f)] public float tankVolume = 0.7f;
    [Range(0f, 1f)] public float fishVolume = 0.07f;

    [Header("Mouvement dans l'eau")]
    [Tooltip("Effort de nage (0 à 1) à partir duquel le son commence")]
    [Range(0f, 1f)] public float movementThreshold = 0.12f;
    [Tooltip("Vitesse d'apparition du son (volume par seconde)")]
    public float movementFadeIn = 6f;
    [Tooltip("Vitesse de disparition du son (volume par seconde) : il s'arrête vite quand on ne bouge plus")]
    public float movementFadeOut = 2.5f;

    [Header("Poissons")]
    [Tooltip("Nombre de poissons proches qui portent un son")]
    [Range(0, 4)] public int fishVoices = 2;
    [Tooltip("Distance au-delà de laquelle on n'entend plus un poisson (m)")]
    public float fishHearingDistance = 12f;

    public static DiveAudio Instance { get; private set; }

    private AudioSource ambience, movement, tank;
    private readonly List<AudioSource> fishSources = new List<AudioSource>();
    private readonly List<Fish> fishTargets = new List<Fish>();
    private float burst;          // lancer du harpon : effort ajouté pendant un court instant
    private float fishTimer;
    private Transform head;

    void Awake()
    {
        Instance = this;

        ambience = NewSource("Ambiance", "underwater_ambience", true, false);
        movement = NewSource("Mouvement dans l'eau", "swim_movement", true, false);
        tank = NewSource("Remplissage bonbonne", "tank_fill", true, false);

        for (int i = 0; i < fishVoices; i++)
        {
            AudioSource s = NewSource("Poisson " + (i + 1), "fish_movement", true, true);
            fishSources.Add(s);
            fishTargets.Add(null);
        }
    }

    void Start()
    {
        if (ambience.clip != null)
        {
            ambience.volume = ambienceVolume;
            ambience.Play();
        }
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>Gros mouvement ponctuel (lancer du harpon) : fait entendre le son de mouvement un court instant.</summary>
    public static void Burst(float strength = 1f)
    {
        if (Instance != null) Instance.burst = Mathf.Max(Instance.burst, Mathf.Clamp01(strength));
    }

    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        if (head == null && Camera.main != null) head = Camera.main.transform;

        ambience.volume = ambienceVolume;

        UpdateMovement(dt);
        UpdateTank();
        UpdateFish(dt);
    }

    // ---------------------------------------------------------------- Mouvement dans l'eau

    void UpdateMovement(float dt)
    {
        if (movement.clip == null) return;

        burst = Mathf.MoveTowards(burst, 0f, dt * 1.6f);

        // Son présent seulement pendant un vrai mouvement, proportionnel à l'effort
        float effort = Mathf.Max(SwimLocomotion.Effort, burst);
        float target = effort > movementThreshold ? Mathf.InverseLerp(movementThreshold, 0.8f, effort) * movementVolume : 0f;

        float speed = target > movement.volume ? movementFadeIn : movementFadeOut;
        movement.volume = Mathf.MoveTowards(movement.volume, target, speed * dt);

        if (movement.volume > 0.004f)
        {
            if (!movement.isPlaying)
            {
                // Départ à un endroit au hasard : deux mouvements ne sonnent pas pareil
                movement.time = Random.Range(0f, Mathf.Max(0f, movement.clip.length - 0.5f));
                movement.Play();
            }
        }
        else if (movement.isPlaying)
        {
            movement.Stop();
        }
    }

    // ---------------------------------------------------------------- Bonbonne

    void UpdateTank()
    {
        if (tank.clip == null) return;

        bool changing = DiveGame.Instance != null && DiveGame.Instance.IsChangingTank;
        if (changing && !tank.isPlaying)
        {
            tank.volume = tankVolume;
            tank.time = 0f;
            tank.Play();
        }
        else if (!changing && tank.isPlaying)
        {
            tank.Stop(); // bonbonne pleine : le son s'arrête net
        }
    }

    // ---------------------------------------------------------------- Poissons

    void UpdateFish(float dt)
    {
        if (fishSources.Count == 0 || head == null) return;

        // Choix des poissons les plus proches, quelques fois par seconde seulement
        fishTimer -= dt;
        if (fishTimer <= 0f)
        {
            fishTimer = 0.3f;
            PickNearestFish();
        }

        for (int i = 0; i < fishSources.Count; i++)
        {
            AudioSource s = fishSources[i];
            if (s.clip == null) continue;

            Fish f = fishTargets[i];
            float target = 0f;
            if (f != null && !f.IsDead)
            {
                s.transform.position = f.transform.position;
                // Un poisson qui file s'entend un peu plus qu'un poisson qui flâne
                target = fishVolume * Mathf.Lerp(0.5f, 1f, Mathf.Clamp01(f.Speed / 4f));
            }

            s.volume = Mathf.MoveTowards(s.volume, target, dt * 0.25f);
            if (s.volume > 0.002f && !s.isPlaying)
            {
                s.time = Random.Range(0f, Mathf.Max(0f, s.clip.length - 0.5f));
                s.Play();
            }
            else if (s.volume <= 0.002f && s.isPlaying)
            {
                s.Stop();
            }
        }
    }

    void PickNearestFish()
    {
        Vector3 origin = head.position;
        float limit = fishHearingDistance * fishHearingDistance;

        for (int i = 0; i < fishTargets.Count; i++) fishTargets[i] = null;

        IReadOnlyList<Fish> all = Fish.All;
        for (int n = 0; n < all.Count; n++)
        {
            Fish f = all[n];
            if (f == null || f.IsDead) continue;
            float d = (f.transform.position - origin).sqrMagnitude;
            if (d > limit) continue;

            // Insère dans la petite liste triée des plus proches
            Fish candidate = f;
            float candidateDist = d;
            for (int i = 0; i < fishTargets.Count; i++)
            {
                Fish cur = fishTargets[i];
                float curDist = cur != null ? (cur.transform.position - origin).sqrMagnitude : float.MaxValue;
                if (candidateDist < curDist)
                {
                    fishTargets[i] = candidate;
                    candidate = cur;
                    candidateDist = curDist;
                    if (candidate == null) break;
                }
            }
        }
    }

    // ---------------------------------------------------------------- Création des sources

    AudioSource NewSource(string name, string clipName, bool loop, bool spatial)
    {
        GameObject go = new GameObject("Son - " + name);
        go.transform.SetParent(transform, false);

        AudioSource s = go.AddComponent<AudioSource>();
        s.clip = Resources.Load<AudioClip>("Audio/" + clipName);
        if (s.clip == null)
            Debug.LogWarning("DiveAudio: son introuvable : Assets/Resources/Audio/" + clipName + " (mp3, wav ou ogg).", this);

        s.loop = loop;
        s.playOnAwake = false;
        s.volume = 0f;
        s.spatialBlend = spatial ? 1f : 0f;
        if (spatial)
        {
            s.rolloffMode = AudioRolloffMode.Linear;
            s.minDistance = 1f;
            s.maxDistance = fishHearingDistance;
            s.dopplerLevel = 0f;
        }
        return s;
    }
}
