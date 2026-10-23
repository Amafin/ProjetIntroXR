using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawner de poissons pour un océan rectangulaire (ex : 100 x 100).
/// - Placer ce script sur un GameObject au centre de l'océan, à la hauteur de la surface de l'eau.
/// - Fait apparaître un premier banc au lancement, puis en ajoute régulièrement
///   jusqu'à atteindre maxFish poissons vivants.
/// - Les poissons ne spawnent jamais trop près du joueur, ni trop près d'un autre poisson,
///   ni dans le sol (raycast vers le fond).
/// - Les poissons morts ne comptent plus dans la population (le spawner en refait d'autres)
///   et peuvent être nettoyés automatiquement après un délai.
/// </summary>
public class FishSpawner : MonoBehaviour
{
    [Header("Prefab & joueur")]
    public Fish fishPrefab;
    [Tooltip("Laisser vide pour chercher l'objet taggé 'Player'")]
    public Transform player;

    [Header("Zone (centrée sur ce GameObject, Y = surface de l'eau)")]
    public Vector2 oceanSize = new Vector2(100f, 100f);
    [Tooltip("Marge par rapport aux bords (les poissons se baladent jusqu'à 20 m de leur spawn)")]
    public float edgeMargin = 10f;

    [Header("Profondeur (sous la surface)")]
    public float minDepth = 2f;
    public float maxDepth = 25f;
    [Tooltip("Layers considérés comme le fond. Exclure le layer de l'eau si elle a un collider !")]
    public LayerMask seabedMask = ~0;
    [Tooltip("Distance minimale à garder au-dessus du fond")]
    public float seabedMargin = 2f;

    [Header("Population")]
    [Tooltip("Nombre de poissons créés au lancement")]
    public int initialCount = 30;
    [Tooltip("Nombre max de poissons vivants en même temps")]
    public int maxFish = 40;
    [Tooltip("Délai entre deux apparitions (secondes)")]
    public float spawnInterval = 2f;

    [Header("Taille aléatoire")]
    [Tooltip("Multiplicateur de taille minimum (1 = taille du prefab)")]
    public float minScale = 0.7f;
    [Tooltip("Multiplicateur de taille maximum")]
    public float maxScale = 1.6f;
    [Tooltip("1 = tailles réparties uniformément. Plus haut = beaucoup de petits poissons et de rares gros.")]
    public float bigFishRarity = 1.5f;

    [Header("Règles d'apparition")]
    [Tooltip("Pas de spawn plus près que ça du joueur (évite l'effet pop-in)")]
    public float minDistanceFromPlayer = 15f;
    [Tooltip("Écart minimum entre deux poissons au spawn (répartit les maisons)")]
    public float minDistanceBetweenFish = 8f;
    public int maxPlacementAttempts = 20;

    [Header("Nettoyage")]
    [Tooltip("Supprime un poisson mort après X secondes (0 = jamais)")]
    public float deadFishLifetime = 30f;

    private readonly List<Fish> fishes = new List<Fish>();
    private readonly Dictionary<Fish, float> deathTimes = new Dictionary<Fish, float>();
    private float timer;

    void Start()
    {
        if (fishPrefab == null)
        {
            Debug.LogError("FishSpawner: aucun prefab de poisson assigné.", this);
            enabled = false;
            return;
        }

        // En VR, on utilise la tête du joueur (Main Camera de l'XR Origin)
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
            }
        }

        for (int i = 0; i < initialCount && AliveCount() < maxFish; i++)
            TrySpawn();
    }

    void Update()
    {
        CleanUp();

        timer += Time.deltaTime;
        if (timer >= spawnInterval)
        {
            timer = 0f;
            if (AliveCount() < maxFish)
                TrySpawn();
        }
    }

    // ---------------------------------------------------------------- Spawn

    bool TrySpawn()
    {
        if (!TryGetSpawnPosition(out Vector3 pos))
            return false;

        Quaternion rot = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        Fish fish = Instantiate(fishPrefab, pos, rot, transform);

        // Taille aléatoire (les gros sont plus rares si bigFishRarity > 1)
        float t = Mathf.Pow(Random.value, Mathf.Max(0.01f, bigFishRarity));
        float scale = Mathf.Lerp(minScale, maxScale, t);
        fish.transform.localScale = fishPrefab.transform.localScale * scale;

        // Les tailles de détection suivent la taille du poisson
        fish.obstacleRadius *= scale;
        fish.groundMargin *= scale;

        // Réglages communs (appliqués avant le Start() du poisson)
        fish.player = player;
        fish.useWaterSurface = true;
        fish.waterSurfaceY = transform.position.y;

        fishes.Add(fish);
        return true;
    }

    bool TryGetSpawnPosition(out Vector3 pos)
    {
        pos = default;

        Vector3 c = transform.position;
        float surfaceY = c.y;
        float halfX = Mathf.Max(0f, oceanSize.x * 0.5f - edgeMargin);
        float halfZ = Mathf.Max(0f, oceanSize.y * 0.5f - edgeMargin);

        for (int i = 0; i < maxPlacementAttempts; i++)
        {
            float x = c.x + Random.Range(-halfX, halfX);
            float z = c.z + Random.Range(-halfZ, halfZ);

            // Profondeur max possible à cet endroit (selon le fond)
            float deepest = maxDepth;
            if (Physics.Raycast(new Vector3(x, surfaceY, z), Vector3.down, out RaycastHit hit,
                                maxDepth + 50f, seabedMask, QueryTriggerInteraction.Ignore))
            {
                float floorDepth = surfaceY - hit.point.y;
                deepest = Mathf.Min(maxDepth, floorDepth - seabedMargin);
            }

            if (deepest < minDepth) continue; // trop peu profond ici

            float depth = Random.Range(minDepth, deepest);
            Vector3 candidate = new Vector3(x, surfaceY - depth, z);

            if (player != null &&
                Vector3.Distance(candidate, player.position) < minDistanceFromPlayer)
                continue;

            if (TooCloseToAnotherFish(candidate)) continue;

            pos = candidate;
            return true;
        }

        return false;
    }

    bool TooCloseToAnotherFish(Vector3 p)
    {
        float sqr = minDistanceBetweenFish * minDistanceBetweenFish;
        for (int i = 0; i < fishes.Count; i++)
        {
            Fish f = fishes[i];
            if (f == null || f.IsDead) continue;
            if ((f.transform.position - p).sqrMagnitude < sqr) return true;
        }
        return false;
    }

    // ---------------------------------------------------------------- Population

    int AliveCount()
    {
        int n = 0;
        for (int i = 0; i < fishes.Count; i++)
            if (fishes[i] != null && !fishes[i].IsDead) n++;
        return n;
    }

    void CleanUp()
    {
        for (int i = fishes.Count - 1; i >= 0; i--)
        {
            Fish f = fishes[i];

            // Détruit ailleurs (poisson ramassé, par ex.)
            if (f == null)
            {
                fishes.RemoveAt(i);
                continue;
            }

            if (!f.IsDead) continue;

            if (!deathTimes.ContainsKey(f))
                deathTimes[f] = Time.time;

            if (deadFishLifetime > 0f && Time.time - deathTimes[f] >= deadFishLifetime)
            {
                deathTimes.Remove(f);
                fishes.RemoveAt(i);
                Destroy(f.gameObject);
            }
        }
    }

    // ---------------------------------------------------------------- Debug

    void OnDrawGizmosSelected()
    {
        Vector3 c = transform.position;

        // Zone complète
        Gizmos.color = new Color(0f, 0.6f, 1f, 0.5f);
        Gizmos.DrawWireCube(c + Vector3.down * (maxDepth * 0.5f),
                            new Vector3(oceanSize.x, maxDepth, oceanSize.y));

        // Zone réelle de spawn (avec marge)
        float sx = Mathf.Max(0f, oceanSize.x - edgeMargin * 2f);
        float sz = Mathf.Max(0f, oceanSize.y - edgeMargin * 2f);
        Gizmos.color = new Color(0f, 1f, 0.5f, 0.6f);
        Gizmos.DrawWireCube(c + Vector3.down * ((minDepth + maxDepth) * 0.5f),
                            new Vector3(sx, maxDepth - minDepth, sz));
    }
}
