using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Récif de corail procédural.
///
/// Les coraux sont regroupés en MASSIFS serrés (un rocher + quelques coraux), séparés par des
/// COULOIRS de nage libres : on peut circuler partout pour chasser, et les colliders des coraux
/// empêchent les poissons (et le joueur, et le harpon) de les traverser.
///
/// Rien à installer : si la scène contient un FishSpawner et aucun CoralReef, un récif est créé
/// automatiquement au lancement, centré sur le spawner et de la taille de son océan.
/// Pour le régler, ajoute le composant sur un GameObject vide placé au centre de la zone.
///
/// Le récif est généré dans Awake, donc AVANT l'apparition des poissons : ceux-ci tiennent
/// compte des coraux dès leur création.
/// </summary>
public class CoralReef : MonoBehaviour
{
    [Header("Zone (centrée sur ce GameObject)")]
    [Tooltip("Taille de la zone en X / Z. (0, 0) = reprend la taille de l'océan du FishSpawner.")]
    public Vector2 areaSize = Vector2.zero;
    [Tooltip("Même graine = même récif à chaque lancement. Change-la pour un autre récif.")]
    public int seed = 12345;

    [Header("Massifs et couloirs")]
    [Tooltip("Rayon d'un massif de coraux (m)")]
    public float clusterRadius = 4f;
    [Tooltip("Largeur libre entre deux massifs (m) : c'est là qu'on nage et que les poissons passent")]
    public float corridorWidth = 8f;
    [Tooltip("Proportion d'emplacements réellement occupés (1 = récif dense, 0.5 = un massif sur deux)")]
    [Range(0f, 1f)] public float fillRatio = 0.75f;
    [Tooltip("Nombre de coraux par massif (min / max)")]
    public Vector2Int coralsPerCluster = new Vector2Int(3, 5);
    [Tooltip("Multiplie la taille de tous les coraux")]
    public float sizeScale = 1f;

    [Header("Familles de corail")]
    public bool staghorn = true;
    public bool seaFan = true;
    public bool table = true;
    public bool brain = true;
    public bool tubes = true;
    public bool rocks = true;

    [Header("Règles")]
    [Tooltip("Zone laissée libre autour du joueur et du harpon au départ (m)")]
    public float clearRadiusAtStart = 9f;
    [Tooltip("Ajoute les colliders (sinon le corail est purement décoratif et traversable)")]
    public bool colliders = true;
    [Tooltip("Hauteur Y maximale du sommet d'un corail (ex : la surface de l'eau). Désactivé si <= au sol.")]
    public float maxTopY = float.PositiveInfinity;
    [Tooltip("Layers considérés comme le fond quand il n'y a pas de Terrain sous le point")]
    public LayerMask groundMask = ~0;
    [Tooltip("Layer donné aux coraux")]
    public int coralLayer = 0;

    private readonly List<Vector3> keepClear = new List<Vector3>();
    private Transform root;

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
        if (FindAnyObjectByType<CoralReef>() != null) return;

        FishSpawner spawner = FindAnyObjectByType<FishSpawner>();
        if (spawner == null) return; // pas une scène de chasse : on ne touche à rien

        GameObject go = new GameObject("Coral Reef (auto)");
        go.transform.position = spawner.transform.position;
        go.AddComponent<CoralReef>();
    }

    void Awake()
    {
        Generate();
    }

    // ---------------------------------------------------------------- Génération

    [ContextMenu("Régénérer le récif")]
    public void Generate()
    {
        Clear();

        root = new GameObject("Coraux").transform;
        root.SetParent(transform, false);

        Vector2 size = areaSize;
        if (size.x <= 0f || size.y <= 0f)
        {
            FishSpawner spawner = FindAnyObjectByType<FishSpawner>();
            size = spawner != null ? spawner.oceanSize : new Vector2(60f, 60f);
        }

        CollectKeepClear();

        var rng = new System.Random(seed);
        var kinds = EnabledKinds();
        if (kinds.Count == 0 && !rocks) return;

        // Grille décalée : un emplacement de massif tous les (diamètre + couloir) mètres
        float cell = Mathf.Max(1f, clusterRadius * 2f + corridorWidth);
        int nx = Mathf.Max(1, Mathf.FloorToInt(size.x / cell));
        int nz = Mathf.Max(1, Mathf.FloorToInt(size.y / cell));
        Vector3 origin = transform.position - new Vector3((nx - 1) * cell, 0f, (nz - 1) * cell) * 0.5f;

        // Le décalage aléatoire d'un massif ne doit jamais manger le couloir
        float jitter = Mathf.Min(corridorWidth * 0.2f, cell * 0.25f);

        int clusters = 0, corals = 0;
        for (int ix = 0; ix < nx; ix++)
        {
            for (int iz = 0; iz < nz; iz++)
            {
                // Tirages faits dans tous les cas : le récif reste identique d'un lancement à l'autre
                float jx = Range(rng, -jitter, jitter);
                float jz = Range(rng, -jitter, jitter);
                bool keep = rng.NextDouble() < fillRatio;
                int count = rng.Next(coralsPerCluster.x, Mathf.Max(coralsPerCluster.x, coralsPerCluster.y) + 1);
                int clusterSeed = rng.Next();

                if (!keep) continue;

                Vector3 center = origin + new Vector3(ix * cell + jx, 0f, iz * cell + jz);
                if (TooCloseToStart(center, clusterRadius)) continue;

                int made = BuildCluster(new System.Random(clusterSeed), center, count, kinds);
                if (made > 0 || rocks) clusters++;
                corals += made;
            }
        }

        Physics.SyncTransforms();
        Debug.Log("CoralReef: " + corals + " coraux dans " + clusters + " massifs (zone " + size.x + " x " + size.y +
                  " m, couloirs de " + corridorWidth + " m).", this);
    }

    int BuildCluster(System.Random rng, Vector3 center, int count, List<CoralGenerator.Kind> kinds)
    {
        if (!TryGetGround(center, out float centerY)) return 0;

        Transform cluster = new GameObject("Massif").transform;
        cluster.SetParent(root, false);
        cluster.position = new Vector3(center.x, centerY, center.z);

        int made = 0;

        if (rocks)
        {
            float rockSize = Range(rng, 1.6f, 2.6f) * sizeScale;
            GameObject rock = CoralGenerator.CreateRock(rng, rockSize, colliders);
            Place(rock, cluster, new Vector3(center.x, centerY - rockSize * 0.15f, center.z), rng);
        }

        var placed = new List<Vector2>();
        for (int i = 0; i < count && kinds.Count > 0; i++)
        {
            CoralGenerator.Kind kind = kinds[rng.Next(kinds.Count)];
            float size = SizeFor(kind, rng) * sizeScale;
            float footprint = FootprintFor(kind, size);

            // Cherche une place dans le massif, sans dépasser dans le couloir ni chevaucher un voisin
            bool found = false;
            Vector2 p = default;
            for (int attempt = 0; attempt < 12 && !found; attempt++)
            {
                float a = Range(rng, 0f, Mathf.PI * 2f);
                float r = Range(rng, 0.2f, 1f) * Mathf.Max(0.5f, clusterRadius - footprint);
                p = new Vector2(center.x + Mathf.Cos(a) * r, center.z + Mathf.Sin(a) * r);

                found = true;
                foreach (Vector2 q in placed)
                {
                    if ((q - p).sqrMagnitude < 1.8f * 1.8f) { found = false; break; }
                }
            }
            if (!found) continue;

            Vector3 pos = new Vector3(p.x, 0f, p.y);
            if (!TryGetGround(pos, out float y)) continue;
            if (y + HeightFor(kind, size) > maxTopY) continue; // dépasserait de l'eau
            if (TooCloseToStart(pos, footprint)) continue;

            placed.Add(p);
            GameObject coral = CoralGenerator.Create(kind, rng, size, colliders);
            Place(coral, cluster, new Vector3(p.x, y - 0.15f * sizeScale, p.y), rng); // légèrement enfoncé
            made++;
        }

        if (made == 0 && !rocks)
        {
            if (Application.isPlaying) Destroy(cluster.gameObject);
            else DestroyImmediate(cluster.gameObject);
        }
        return made;
    }

    void Place(GameObject go, Transform parent, Vector3 position, System.Random rng)
    {
        go.transform.SetParent(parent, true);
        go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, Range(rng, 0f, 360f), 0f));
        SetLayer(go.transform, coralLayer);
    }

    static void SetLayer(Transform t, int layer)
    {
        t.gameObject.layer = layer;
        for (int i = 0; i < t.childCount; i++) SetLayer(t.GetChild(i), layer);
    }

    void Clear()
    {
        Transform old = transform.Find("Coraux");
        if (old == null) return;
        old.name = "Coraux (ancien)";
        old.gameObject.SetActive(false);
        if (Application.isPlaying) Destroy(old.gameObject);
        else DestroyImmediate(old.gameObject);
    }

    // ---------------------------------------------------------------- Tailles par famille

    /// <summary>Taille de référence : hauteur (branchu, gorgone, tubes) ou rayon (tabulaire, massif).</summary>
    static float SizeFor(CoralGenerator.Kind kind, System.Random rng)
    {
        switch (kind)
        {
            case CoralGenerator.Kind.Staghorn: return Range(rng, 3.2f, 5.5f);
            case CoralGenerator.Kind.SeaFan: return Range(rng, 3.5f, 6f);
            case CoralGenerator.Kind.Table: return Range(rng, 2.2f, 3.4f);
            case CoralGenerator.Kind.Brain: return Range(rng, 1.2f, 2.2f);
            default: return Range(rng, 2.4f, 4f);
        }
    }

    /// <summary>Rayon occupé au sol (approximatif), pour rester dans le massif.</summary>
    static float FootprintFor(CoralGenerator.Kind kind, float size)
    {
        switch (kind)
        {
            case CoralGenerator.Kind.Staghorn: return size * 0.45f;
            case CoralGenerator.Kind.SeaFan: return size * 0.4f;
            case CoralGenerator.Kind.Table: return size;
            case CoralGenerator.Kind.Brain: return size;
            default: return size * 0.35f;
        }
    }

    static float HeightFor(CoralGenerator.Kind kind, float size)
    {
        switch (kind)
        {
            case CoralGenerator.Kind.Table: return size * 1.1f;
            case CoralGenerator.Kind.Brain: return size * 0.8f;
            default: return size;
        }
    }

    List<CoralGenerator.Kind> EnabledKinds()
    {
        var list = new List<CoralGenerator.Kind>();
        if (staghorn) list.Add(CoralGenerator.Kind.Staghorn);
        if (seaFan) list.Add(CoralGenerator.Kind.SeaFan);
        if (table) list.Add(CoralGenerator.Kind.Table);
        if (brain) list.Add(CoralGenerator.Kind.Brain);
        if (tubes) list.Add(CoralGenerator.Kind.Tubes);
        return list;
    }

    // ---------------------------------------------------------------- Zone de départ

    void CollectKeepClear()
    {
        keepClear.Clear();
        if (Camera.main != null) keepClear.Add(Camera.main.transform.position);

        Harpoon harpoon = FindAnyObjectByType<Harpoon>();
        if (harpoon != null) keepClear.Add(harpoon.transform.position);
    }

    bool TooCloseToStart(Vector3 p, float extra)
    {
        float limit = clearRadiusAtStart + extra;
        foreach (Vector3 c in keepClear)
        {
            float dx = p.x - c.x, dz = p.z - c.z;
            if (dx * dx + dz * dz < limit * limit) return true;
        }
        return false;
    }

    // ---------------------------------------------------------------- Fond marin

    /// <summary>Hauteur du fond sous un point : le Terrain s'il y en a un, sinon le collider le plus bas.</summary>
    bool TryGetGround(Vector3 p, out float y)
    {
        foreach (Terrain t in Terrain.activeTerrains)
        {
            if (t == null || t.terrainData == null) continue;
            Vector3 min = t.transform.position;
            Vector3 max = min + t.terrainData.size;
            if (p.x < min.x || p.x > max.x || p.z < min.z || p.z > max.z) continue;

            y = t.SampleHeight(p) + min.y;
            return true;
        }

        // Pas de Terrain : on prend le collider fixe le plus bas (la surface de l'eau, au-dessus, est ignorée)
        y = 0f;
        bool found = false;
        RaycastHit[] hits = Physics.RaycastAll(new Vector3(p.x, transform.position.y + 500f, p.z), Vector3.down,
                                               2000f, groundMask, QueryTriggerInteraction.Ignore);
        foreach (RaycastHit h in hits)
        {
            if (h.collider.attachedRigidbody != null) continue;            // objet mobile
            if (root != null && h.collider.transform.IsChildOf(transform)) continue; // un corail déjà posé
            if (!found || h.point.y < y)
            {
                y = h.point.y;
                found = true;
            }
        }
        return found;
    }

    static float Range(System.Random rng, float min, float max)
    {
        return min + (float)rng.NextDouble() * (max - min);
    }

    // ---------------------------------------------------------------- Debug

    void OnDrawGizmosSelected()
    {
        Vector2 size = areaSize;
        if (size.x <= 0f || size.y <= 0f)
        {
            FishSpawner spawner = FindAnyObjectByType<FishSpawner>();
            size = spawner != null ? spawner.oceanSize : new Vector2(60f, 60f);
        }

        Gizmos.color = new Color(1f, 0.5f, 0.3f, 0.7f);
        Gizmos.DrawWireCube(transform.position, new Vector3(size.x, 1f, size.y));
    }
}
