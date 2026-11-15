using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;

/// <summary>
/// Sacoche à poissons du joueur, portée à la hanche.
///
/// Amener un poisson mort tenu en main jusqu'à la sacoche le range : il disparaît et le
/// compteur gagne +1 (voir Fish.Collect). La sacoche s'illumine et grossit légèrement
/// quand une main tenant un poisson s'en approche.
///
/// La sacoche suit le corps du joueur : elle reste sous la tête, sur le côté, et ne tourne
/// qu'avec un léger retard quand on tourne la tête (on peut donc la regarder).
///
/// Rien à installer : elle se crée toute seule au premier besoin. On peut aussi poser le
/// composant à la main dans la scène pour régler sa position ou brancher onCountChanged.
/// </summary>
public class FishBag : MonoBehaviour
{
    [System.Serializable]
    public class CountEvent : UnityEvent<int> { }

    public enum Side { Left, Right }

    [Header("Références (auto-détectées si vides)")]
    [Tooltip("La tête du joueur (Main Camera)")]
    public Transform head;

    [Header("Position de la sacoche (par rapport à la tête)")]
    [Tooltip("Côté de la hanche qui porte la sacoche")]
    public Side side = Side.Left;
    [Tooltip("Hauteur de la hanche sous la tête (m). Debout : environ 0,7. Assis ou allongé : moins.")]
    public float hipDrop = 0.65f;
    [Tooltip("Écart latéral par rapport au centre du corps (m)")]
    public float sideOffset = 0.24f;
    [Tooltip("Décalage vers l'avant (m)")]
    public float forwardOffset = 0.06f;
    [Tooltip("Vitesse à laquelle le corps suit la rotation de la tête (plus bas = la sacoche reste en place quand on tourne la tête)")]
    public float bodyTurnSpeed = 2.5f;

    [Header("Rangement")]
    [Tooltip("Distance main-sacoche (m) à laquelle le poisson est rangé")]
    public float dropRadius = 0.24f;
    [Tooltip("Distance (m) à partir de laquelle la sacoche s'illumine")]
    public float highlightRadius = 0.7f;

    [Header("Étui du harpon (sur l'autre hanche)")]
    [Tooltip("Affiche l'étui où poser le harpon")]
    public bool showHolster = true;
    [Tooltip("Distance main-étui (m) à laquelle le harpon lâché s'y range")]
    public float holsterRadius = 0.32f;
    public Color holsterColor = new Color(0.20f, 0.24f, 0.28f);

    [Header("Apparence")]
    [Tooltip("Affiche la sacoche (sinon la zone existe mais reste invisible)")]
    public bool showPouch = true;
    public Color pouchColor = new Color(0.55f, 0.36f, 0.20f);
    public Color flapColor = new Color(0.36f, 0.22f, 0.12f);
    public Color highlightColor = new Color(1f, 0.74f, 0.30f);

    [Header("Événements")]
    public CountEvent onCountChanged;

    public int Count { get; private set; }

    private static FishBag instance;
    private Transform pouch, holster;
    private Material pouchMat, flapMat, holsterMat;
    private float holsterProximity, holsterHighlight;
    private float bodyYaw;
    private bool hasYaw;
    private float proximity;      // 0 = loin, 1 = main sur la sacoche (pour cette image)
    private float highlight;      // valeur lissée
    private float punch;          // petit rebond quand un poisson est rangé

    /// <summary>La sacoche du joueur (créée automatiquement si elle n'existe pas dans la scène).</summary>
    public static FishBag Instance
    {
        get
        {
            if (instance != null) return instance;

#if UNITY_2022_2_OR_NEWER
            instance = FindAnyObjectByType<FishBag>();
#else
            instance = FindObjectOfType<FishBag>();
#endif
            if (instance == null)
                instance = new GameObject("Fish Bag").AddComponent<FishBag>();
            return instance;
        }
    }

    void Awake()
    {
        if (instance == null) instance = this;
        if (head == null && Camera.main != null) head = Camera.main.transform;
        if (head == null)
            Debug.LogWarning("FishBag: aucune tête trouvée (Main Camera). Assigne le champ 'head'.", this);
    }

    void Start()
    {
        BuildPouch();
        Follow(true);
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
        if (pouchMat != null) Destroy(pouchMat);
        if (flapMat != null) Destroy(flapMat);
        if (holsterMat != null) Destroy(holsterMat);
    }

    // ---------------------------------------------------------------- Rangement

    /// <summary>Centre de la sacoche dans le monde.</summary>
    public Vector3 PouchPosition => pouch != null ? pouch.position : ComputePosition(out _);

    /// <summary>
    /// À appeler à chaque image pour une main qui tient un poisson : illumine la sacoche à
    /// l'approche, et renvoie vrai quand la main est dessus (le poisson peut être rangé).
    /// </summary>
    public bool IsInBagZone(Vector3 handPosition)
    {
        if (head == null) return false;

        float d = Vector3.Distance(handPosition, PouchPosition);
        proximity = Mathf.Max(proximity, Mathf.InverseLerp(highlightRadius, dropRadius, d));
        return d <= dropRadius;
    }

    // ---------------------------------------------------------------- Étui du harpon

    /// <summary>Orientation du corps du joueur (suit la tête avec retard).</summary>
    public Quaternion BodyRotation => Quaternion.Euler(0f, bodyYaw, 0f);

    /// <summary>Position de l'étui du harpon, sur la hanche opposée à la sacoche.</summary>
    public Vector3 HolsterPosition => HipPoint(side == Side.Left ? Side.Right : Side.Left);

    /// <summary>
    /// À appeler à chaque image pour la main qui tient le harpon : illumine l'étui à l'approche,
    /// et renvoie vrai quand la main est dessus (le harpon lâché ici s'y range).
    /// </summary>
    public bool IsInHolsterZone(Vector3 handPosition)
    {
        if (head == null) return false;

        float d = Vector3.Distance(handPosition, HolsterPosition);
        holsterProximity = Mathf.Max(holsterProximity, Mathf.InverseLerp(holsterRadius * 2.2f, holsterRadius, d));
        return d <= holsterRadius;
    }

    Vector3 HipPoint(Side hip)
    {
        if (head == null) return transform.position;
        float lateral = hip == Side.Left ? -sideOffset : sideOffset;
        return head.position + Vector3.down * hipDrop + BodyRotation * new Vector3(lateral, 0f, forwardOffset);
    }

    /// <summary>Range un poisson dans la sacoche : +1.</summary>
    public void Add(Fish fish)
    {
        Count++;
        punch = 1f;
        onCountChanged?.Invoke(Count);
        if (DiveGame.Instance != null) DiveGame.Instance.NotifyFishBagged(fish);
    }

    // ---------------------------------------------------------------- Suivi du corps

    void LateUpdate()
    {
        if (head == null)
        {
            if (Camera.main != null) head = Camera.main.transform;
            else return;
        }

        Follow(false);
        Animate();
        proximity = 0f; // recalculé par les mains à la prochaine image
        holsterProximity = 0f;
    }

    Vector3 ComputePosition(out Quaternion rotation)
    {
        Quaternion body = Quaternion.Euler(0f, bodyYaw, 0f);
        rotation = body;
        if (head == null) return transform.position;

        float lateral = side == Side.Left ? -sideOffset : sideOffset;
        return head.position + Vector3.down * hipDrop + body * new Vector3(lateral, 0f, forwardOffset);
    }

    void Follow(bool snap)
    {
        if (head == null) return;

        // Orientation du corps : celle de la tête à plat, suivie avec retard
        Vector3 flat = Vector3.ProjectOnPlane(head.forward, Vector3.up);
        if (flat.sqrMagnitude > 0.01f)
        {
            float headYaw = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
            if (snap || !hasYaw)
            {
                bodyYaw = headYaw;
                hasYaw = true;
            }
            else
            {
                bodyYaw = Mathf.LerpAngle(bodyYaw, headYaw, 1f - Mathf.Exp(-bodyTurnSpeed * Time.deltaTime));
            }
        }

        if (holster != null)
        {
            float away = side == Side.Left ? 90f : -90f;
            holster.SetPositionAndRotation(HolsterPosition, BodyRotation * Quaternion.Euler(0f, away, 0f));
        }

        if (pouch == null) return;
        Vector3 pos = ComputePosition(out Quaternion rot);
        // La sacoche est tournée vers l'extérieur de la hanche
        float outward = side == Side.Left ? -90f : 90f;
        pouch.SetPositionAndRotation(pos, rot * Quaternion.Euler(0f, outward, 0f));
    }

    void Animate()
    {
        if (pouch == null) return;

        highlight = Mathf.MoveTowards(highlight, proximity, Time.deltaTime * 6f);
        punch = Mathf.MoveTowards(punch, 0f, Time.deltaTime * 3f);

        float pulse = highlight > 0.01f ? 0.5f + 0.5f * Mathf.Sin(Time.time * 8f) : 0f;
        pouch.localScale = Vector3.one * (1f + 0.18f * highlight + 0.06f * pulse * highlight + 0.35f * punch);

        if (holster != null)
        {
            holsterHighlight = Mathf.MoveTowards(holsterHighlight, holsterProximity, Time.deltaTime * 6f);
            float hp = holsterHighlight > 0.01f ? 0.5f + 0.5f * Mathf.Sin(Time.time * 8f) : 0f;
            holster.localScale = Vector3.one * (1f + 0.2f * holsterHighlight + 0.06f * hp * holsterHighlight);
            SetColor(holsterMat, Color.Lerp(holsterColor, highlightColor, Mathf.Clamp01(holsterHighlight * (0.6f + 0.4f * hp))));
        }

        float glow = Mathf.Clamp01(highlight * (0.6f + 0.4f * pulse) + punch);
        SetColor(pouchMat, Color.Lerp(pouchColor, highlightColor, glow));
        SetColor(flapMat, Color.Lerp(flapColor, highlightColor, glow * 0.7f));
    }

    static void SetColor(Material m, Color c)
    {
        if (m == null) return;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Color")) m.SetColor("_Color", c);
    }

    // ---------------------------------------------------------------- Modèle de la sacoche

    void BuildHolster()
    {
        if (holster != null || !showHolster) return;

        holster = new GameObject("Étui du harpon").transform;
        holster.SetParent(transform, false);
        holsterMat = NewMaterial(holsterColor);

        // Un fourreau court, ouvert vers l'avant, tenu par une sangle
        var parts = new List<Vector3[]>();
        AddTapered(parts, new Vector3(0f, 0f, 0f), 0.085f, 0.065f, 0.17f, 0.085f, 0.065f);
        AddTapered(parts, new Vector3(0f, 0.15f, -0.02f), 0.045f, 0.045f, 0.14f, 0.02f, 0.02f);
        AddPartTo(holster, "Fourreau", parts, holsterMat);
    }

    void BuildPouch()
    {
        BuildHolster();
        if (pouch != null || !showPouch) return;

        pouch = new GameObject("Sacoche").transform;
        pouch.SetParent(transform, false);

        pouchMat = NewMaterial(pouchColor);
        flapMat = NewMaterial(flapColor);

        // Corps : petite besace à facettes, plus large en haut qu'en bas (face avant = +Z)
        var body = new List<Vector3[]>();
        AddTapered(body, new Vector3(0f, 0f, 0f), 0.22f, 0.17f, 0.20f, 0.09f, 0.07f);
        AddPart("Corps", body, pouchMat);

        // Rabat : dépasse sur la face avant et descend à mi-hauteur
        var flap = new List<Vector3[]>();
        AddTapered(flap, new Vector3(0f, 0.045f, 0.012f), 0.235f, 0.19f, 0.115f, 0.10f, 0.085f);
        // Sangle : remonte vers la ceinture
        AddTapered(flap, new Vector3(0f, 0.16f, -0.03f), 0.05f, 0.05f, 0.14f, 0.02f, 0.02f);
        AddPart("Rabat", flap, flapMat);
    }

    /// <summary>Boîte effilée centrée en 'center' : largeur en haut / en bas, hauteur, épaisseur en haut / en bas.</summary>
    static void AddTapered(List<Vector3[]> quads, Vector3 center, float topW, float botW, float height, float topD, float botD)
    {
        float h = height * 0.5f;
        Vector3[] t =
        {
            center + new Vector3(-topW * 0.5f, h, -topD * 0.5f), center + new Vector3(topW * 0.5f, h, -topD * 0.5f),
            center + new Vector3(topW * 0.5f, h, topD * 0.5f), center + new Vector3(-topW * 0.5f, h, topD * 0.5f),
        };
        Vector3[] b =
        {
            center + new Vector3(-botW * 0.5f, -h, -botD * 0.5f), center + new Vector3(botW * 0.5f, -h, -botD * 0.5f),
            center + new Vector3(botW * 0.5f, -h, botD * 0.5f), center + new Vector3(-botW * 0.5f, -h, botD * 0.5f),
        };

        quads.Add(new[] { t[0], t[1], t[2], t[3], center });       // dessus
        quads.Add(new[] { b[0], b[1], b[2], b[3], center });       // dessous
        for (int i = 0; i < 4; i++)
        {
            int j = (i + 1) % 4;
            quads.Add(new[] { t[i], t[j], b[j], b[i], center });   // côtés
        }
    }

    /// <summary>Crée un maillage à faces plates à partir de quadrilatères (le 5e point est l'intérieur de la pièce).</summary>
    void AddPart(string name, List<Vector3[]> quads, Material material)
    {
        AddPartTo(pouch, name, quads, material);
    }

    void AddPartTo(Transform parent, string name, List<Vector3[]> quads, Material material)
    {
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var triangles = new List<int>();

        foreach (Vector3[] q in quads)
        {
            Vector3 inside = q[4];
            for (int k = 0; k < 2; k++)
            {
                Vector3 a = q[0], b = q[k + 1], c = q[k + 2];
                Vector3 n = Vector3.Cross(b - a, c - a);
                if (n.sqrMagnitude < 1e-12f) continue;
                n.Normalize();

                // Face visible tournée vers l'extérieur
                if (Vector3.Dot(n, (a + b + c) / 3f - inside) < 0f)
                {
                    Vector3 tmp = b; b = c; c = tmp;
                    n = -n;
                }

                int i = vertices.Count;
                vertices.Add(a); vertices.Add(b); vertices.Add(c);
                normals.Add(n); normals.Add(n); normals.Add(n);
                triangles.Add(i); triangles.Add(i + 1); triangles.Add(i + 2);
            }
        }

        Mesh mesh = new Mesh { name = "Sacoche " + name };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();

        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = material;
        r.shadowCastingMode = ShadowCastingMode.Off;
    }

    static Material NewMaterial(Color color)
    {
        // Matériau par défaut du pipeline actif : son shader est toujours présent dans un build
        Material template = GraphicsSettings.currentRenderPipeline != null
            ? GraphicsSettings.currentRenderPipeline.defaultMaterial
            : null;

        Material m;
        if (template != null)
        {
            m = new Material(template);
        }
        else
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            m = new Material(shader);
        }

        m.name = "Sacoche";
        SetColor(m, color);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.1f);
        if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.1f);
        return m;
    }

    // ---------------------------------------------------------------- Debug

    void OnDrawGizmosSelected()
    {
        Transform h = head != null ? head : (Camera.main != null ? Camera.main.transform : null);
        if (h == null) return;

        Vector3 flat = Vector3.ProjectOnPlane(h.forward, Vector3.up);
        if (flat.sqrMagnitude < 0.01f) return;
        Quaternion body = Quaternion.LookRotation(flat.normalized, Vector3.up);
        float lateral = side == Side.Left ? -sideOffset : sideOffset;
        Vector3 pos = h.position + Vector3.down * hipDrop + body * new Vector3(lateral, 0f, forwardOffset);

        Gizmos.color = new Color(1f, 0.74f, 0.30f, 0.9f);
        Gizmos.DrawWireSphere(pos, dropRadius);
        Gizmos.color = new Color(1f, 0.74f, 0.30f, 0.3f);
        Gizmos.DrawWireSphere(pos, highlightRadius);
    }
}
