using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Génère des coraux par le code : maillages low-poly à faces plates + colliders simples.
///
/// Cinq familles : branchu (corne de cerf), gorgone (éventail), tabulaire, massif (cerveau),
/// tubes (éponges), plus un rocher servant de socle aux massifs.
///
/// Chaque corail est créé avec sa base à l'origine de son GameObject, l'axe Y vers le haut.
/// Les colliders sont volontairement des formes simples (capsules, boîtes, enveloppes convexes) :
/// suffisant pour bloquer poissons, joueur et harpon, et léger pour un casque autonome.
/// </summary>
public static class CoralGenerator
{
    public enum Kind { Staghorn, SeaFan, Table, Brain, Tubes }

    public const int KindCount = 5;

    static readonly Color TipColor = new Color(0.98f, 0.91f, 0.80f);
    static readonly Color HollowColor = new Color(0.20f, 0.07f, 0.13f);

    static readonly Color[][] Palettes =
    {
        new[] { Rgb(238, 132, 78), Rgb(244, 170, 110), Rgb(230, 100, 96) },   // branchu
        new[] { Rgb(176, 70, 170), Rgb(220, 90, 140), Rgb(120, 90, 200) },    // gorgone
        new[] { Rgb(64, 170, 160), Rgb(90, 150, 200), Rgb(120, 190, 120) },   // tabulaire
        new[] { Rgb(214, 176, 78), Rgb(190, 200, 110) },                      // massif
        new[] { Rgb(226, 84, 110), Rgb(240, 140, 60), Rgb(150, 110, 220) },   // tubes
    };

    static readonly Dictionary<Color32, Material> materials = new Dictionary<Color32, Material>();

    static Color Rgb(int r, int g, int b) => new Color(r / 255f, g / 255f, b / 255f);

    // ---------------------------------------------------------------- API

    /// <summary>Crée un corail d'une famille donnée. 'size' = taille de référence en mètres (hauteur ou rayon).</summary>
    public static GameObject Create(Kind kind, System.Random rng, float size, bool withColliders)
    {
        Color[] palette = Palettes[(int)kind];
        Color color = palette[rng.Next(palette.Length)];

        GameObject go = new GameObject("Corail " + kind);
        var b = new Builder();

        switch (kind)
        {
            case Kind.Staghorn: BuildStaghorn(go, b, rng, size, color, withColliders); break;
            case Kind.SeaFan: BuildSeaFan(go, b, rng, size, color, withColliders); break;
            case Kind.Table: BuildTable(go, b, rng, size, color, withColliders); break;
            case Kind.Brain: BuildBrain(go, b, rng, size, color, withColliders); break;
            default: BuildTubes(go, b, rng, size, color, withColliders); break;
        }

        b.Apply(go, go.name);
        return go;
    }

    /// <summary>Crée un rocher (socle d'un massif de coraux).</summary>
    public static GameObject CreateRock(System.Random rng, float size, bool withColliders)
    {
        GameObject go = new GameObject("Rocher");
        var b = new Builder();
        Color color = Rgb(92, 104, 112) * Range(rng, 0.85f, 1.1f);
        color.a = 1f;

        const int rows = 4, cols = 8;
        float[,] off = new float[rows + 1, cols];
        for (int i = 0; i <= rows; i++)
            for (int j = 0; j < cols; j++)
                off[i, j] = Range(rng, 0.75f, 1.15f);

        Vector3 Pt(int i, int j)
        {
            j %= cols;
            float th = Mathf.PI * 0.5f * i / rows;
            float ph = Mathf.PI * 2f * j / cols;
            float r = size * (i == 0 ? off[0, 0] : off[i, j]);
            return new Vector3(Mathf.Sin(th) * Mathf.Cos(ph) * r * 1.3f, Mathf.Cos(th) * r * 0.6f,
                               Mathf.Sin(th) * Mathf.Sin(ph) * r);
        }

        Vector3 inside = new Vector3(0f, -size * 0.2f, 0f);
        for (int i = 0; i < rows; i++)
        {
            for (int j = 0; j < cols; j++)
            {
                if (i == 0) b.Tri(Pt(0, j), Pt(1, j + 1), Pt(1, j), inside, color);
                else b.Quad(Pt(i, j), Pt(i, j + 1), Pt(i + 1, j + 1), Pt(i + 1, j), inside, color);
            }
        }

        Mesh mesh = b.Apply(go, go.name);
        if (withColliders) AddConvex(go, mesh);
        return go;
    }

    // ---------------------------------------------------------------- Familles

    /// <summary>Corail branchu (corne de cerf) : branches récursives effilées, pointes claires.</summary>
    static void BuildStaghorn(GameObject go, Builder b, System.Random rng, float height, Color color, bool colliders)
    {
        void Grow(Vector3 p, Vector3 d, float length, float radius, int depth)
        {
            d = (d + new Vector3(Range(rng, -0.12f, 0.12f), Range(rng, 0f, 0.1f), Range(rng, -0.12f, 0.12f))).normalized;
            Vector3 q = p + d * length;
            Color c = color * (0.8f + 0.07f * depth);
            c.a = 1f;
            b.Segment(p, q, radius, radius * 0.72f, 5, c, depth == 0);

            // Seules les grosses branches bloquent : on peut frôler les brindilles
            if (colliders && depth >= 2) AddCapsule(go, p, q, radius * 1.5f);

            if (depth == 0)
            {
                b.Segment(q, q + d * radius * 1.6f, radius * 0.72f, radius * 0.25f, 5, TipColor, true);
                return;
            }

            int n = rng.Next(3) == 0 ? 3 : 2;
            float start = Range(rng, 0f, Mathf.PI * 2f);
            for (int i = 0; i < n; i++)
            {
                float ang = start + Mathf.PI * 2f * i / n + Range(rng, -0.4f, 0.4f);
                Vector3 side = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                Grow(q, d + side * Range(rng, 0.45f, 0.85f), length * Range(rng, 0.68f, 0.85f), radius * 0.72f, depth - 1);
            }
        }

        for (int k = 0; k < 3; k++)
        {
            float a = Mathf.PI * 2f * k / 3f + Range(rng, -0.3f, 0.3f);
            Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            Grow(dir * 0.25f, Vector3.up + dir * 0.35f, height * 0.3f, height * 0.045f, 3);
        }
    }

    /// <summary>Gorgone (éventail) : réseau de branches fines dans un seul plan (XY).</summary>
    static void BuildSeaFan(GameObject go, Builder b, System.Random rng, float height, Color color, bool colliders)
    {
        float minX = 0f, maxX = 0f, maxY = 0f;

        void Grow(Vector3 p, float ang, float length, float radius, int depth)
        {
            Vector3 d = new Vector3(Mathf.Sin(ang), Mathf.Cos(ang), Range(rng, -0.04f, 0.04f)).normalized;
            Vector3 q = p + d * length;
            Color c = color * (0.75f + 0.08f * depth);
            c.a = 1f;
            b.Segment(p, q, radius, radius * 0.75f, 4, c, depth == 0);

            minX = Mathf.Min(minX, q.x); maxX = Mathf.Max(maxX, q.x); maxY = Mathf.Max(maxY, q.y);
            if (depth == 0) return;

            Grow(q, ang - Range(rng, 0.25f, 0.5f), length * Range(rng, 0.7f, 0.85f), radius * 0.75f, depth - 1);
            Grow(q, ang + Range(rng, 0.25f, 0.5f), length * Range(rng, 0.7f, 0.85f), radius * 0.75f, depth - 1);
            if (rng.NextDouble() < 0.35)
                Grow(q, ang + Range(rng, -0.1f, 0.1f), length * 0.8f, radius * 0.75f, depth - 1);
        }

        Color foot = color * 0.6f;
        foot.a = 1f;
        Vector3 fork = Vector3.up * height * 0.12f;
        b.Segment(Vector3.zero, fork, height * 0.035f, height * 0.028f, 5, foot, false);
        for (int k = -1; k <= 1; k++)
            Grow(fork, k * 0.55f + Range(rng, -0.1f, 0.1f), height * 0.24f, height * 0.022f, 4);

        if (colliders)
        {
            // Une seule boîte fine : l'éventail entier fait écran
            BoxCollider box = go.AddComponent<BoxCollider>();
            float top = maxY * 0.92f;
            box.center = new Vector3((minX + maxX) * 0.5f, top * 0.5f, 0f);
            box.size = new Vector3((maxX - minX) * 0.85f, top, Mathf.Max(0.3f, height * 0.08f));
        }
    }

    /// <summary>Corail tabulaire : pied court + grand plateau irrégulier sous lequel on peut passer.</summary>
    static void BuildTable(GameObject go, Builder b, System.Random rng, float radius, Color color, bool colliders)
    {
        float h = radius * Range(rng, 0.75f, 1f);
        Color stem = color * 0.58f; stem.a = 1f;
        Color edge = color * 0.8f; edge.a = 1f;
        Color under = color * 0.5f; under.a = 1f;

        b.Segment(Vector3.zero, Vector3.up * h * 0.55f, radius * 0.22f, radius * 0.13f, 7, stem, false);
        b.Segment(Vector3.up * h * 0.55f, Vector3.up * h * 0.92f, radius * 0.13f, radius * 0.4f, 7, stem, false);

        const int n = 14;
        float thickness = radius * 0.07f;
        var rim = new Vector3[n];
        var inner = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            float a = Mathf.PI * 2f * i / n;
            float r = radius * Range(rng, 0.78f, 1.08f);
            rim[i] = new Vector3(Mathf.Cos(a) * r, h + Range(rng, -0.06f, 0.1f) * radius, Mathf.Sin(a) * r);
            inner[i] = new Vector3(rim[i].x * 0.4f, h * 0.92f, rim[i].z * 0.4f);
        }

        Vector3 top = Vector3.up * (h * 0.98f);
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            Vector3 lowI = rim[i] - Vector3.up * thickness;
            Vector3 lowJ = rim[j] - Vector3.up * thickness;
            Vector3 outward = new Vector3(rim[i].x + rim[j].x, 0f, rim[i].z + rim[j].z);

            Color c = color * (1f + 0.08f * Mathf.Sin(i * 1.7f));
            c.a = 1f;
            b.TriFacing(top, rim[j], rim[i], Vector3.up, c);                 // dessus
            b.QuadFacing(rim[i], rim[j], lowJ, lowI, outward, edge);         // tranche
            b.QuadFacing(lowI, lowJ, inner[j], inner[i], Vector3.down, under); // dessous
        }

        if (!colliders) return;

        // Pied : une capsule. Plateau : une enveloppe convexe fine, pour pouvoir passer dessous.
        AddCapsule(go, Vector3.zero, Vector3.up * h * 0.9f, radius * 0.2f);

        var cb = new Builder();
        Vector3 topC = Vector3.up * (h + radius * 0.05f);
        Vector3 botC = Vector3.up * (h - thickness * 1.5f);
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            Vector3 a0 = new Vector3(rim[i].x, topC.y, rim[i].z), a1 = new Vector3(rim[j].x, topC.y, rim[j].z);
            Vector3 b0 = new Vector3(rim[i].x, botC.y, rim[i].z), b1 = new Vector3(rim[j].x, botC.y, rim[j].z);
            cb.TriFacing(topC, a1, a0, Vector3.up, Color.white);
            cb.TriFacing(botC, b0, b1, Vector3.down, Color.white);
            cb.QuadFacing(a0, a1, b1, b0, new Vector3(a0.x + a1.x, 0f, a0.z + a1.z), Color.white);
        }
        AddConvex(go, cb.ToMesh("Corail Plateau (collider)"));
    }

    /// <summary>Corail massif (cerveau) : dôme bosselé.</summary>
    static void BuildBrain(GameObject go, Builder b, System.Random rng, float radius, Color color, bool colliders)
    {
        const int rows = 6, cols = 12;
        var seeds = new Vector3[4];
        for (int i = 0; i < seeds.Length; i++)
            seeds[i] = new Vector3(Range(rng, 0f, 6.28f), Range(rng, 0f, 6.28f), rng.Next(2, 5));

        Vector3 Pt(int i, int j)
        {
            float th = Mathf.PI * 0.5f * i / rows;
            float ph = i == 0 ? 0f : Mathf.PI * 2f * (j % cols) / cols; // un seul sommet au pôle
            float bump = 1f;
            foreach (Vector3 s in seeds)
                bump += 0.07f * Mathf.Sin(s.z * ph + s.x) * Mathf.Sin(s.z * th * 2f + s.y);
            float r = radius * bump;
            return new Vector3(Mathf.Sin(th) * Mathf.Cos(ph) * r, Mathf.Cos(th) * r * 0.8f, Mathf.Sin(th) * Mathf.Sin(ph) * r);
        }

        Vector3 inside = new Vector3(0f, -radius * 0.2f, 0f);
        for (int i = 0; i < rows; i++)
        {
            for (int j = 0; j < cols; j++)
            {
                Color c = color * (0.86f + 0.14f * ((i + j) % 2) + 0.06f * (float)rng.NextDouble());
                c.a = 1f;
                if (i == 0) b.Tri(Pt(0, j), Pt(1, j + 1), Pt(1, j), inside, c);
                else b.Quad(Pt(i, j), Pt(i, j + 1), Pt(i + 1, j + 1), Pt(i + 1, j), inside, c);
            }
        }

        if (colliders)
        {
            // Sphère enfoncée dans le sol : épouse le dôme (sommet à 0,8 x rayon)
            SphereCollider sphere = go.AddComponent<SphereCollider>();
            sphere.center = new Vector3(0f, -radius * 0.2f, 0f);
            sphere.radius = radius;
        }
    }

    /// <summary>Éponges tubes : bouquet de tubes creux.</summary>
    static void BuildTubes(GameObject go, Builder b, System.Random rng, float height, Color color, bool colliders)
    {
        const int sides = 7;
        int n = rng.Next(5, 8);
        for (int k = 0; k < n; k++)
        {
            float a = Mathf.PI * 2f * k / n + Range(rng, -0.3f, 0.3f);
            Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            Vector3 root = dir * Range(rng, 0f, 0.55f) * height * 0.3f;
            Vector3 lean = (Vector3.up + dir * Range(rng, 0.05f, 0.3f)).normalized;
            float h = height * Range(rng, 0.45f, 1f);
            float r = height * Range(rng, 0.07f, 0.11f);

            Color c = color * Range(rng, 0.8f, 1.1f); c.a = 1f;
            Color low = c * 0.85f; low.a = 1f;
            Color lip = c * 1.15f; lip.a = 1f;

            Vector3 mid = root + lean * h * 0.6f;
            Vector3 top = root + lean * h;
            b.Segment(root, mid, r * 0.8f, r, sides, low, false);
            b.Segment(mid, top, r, r * 1.15f, sides, c, false);

            // Lèvre + fond sombre : donne l'impression d'un tube creux
            Vector3 bottom = top - lean * r * 0.3f;
            Vector3[] outer = Builder.Ring(top, lean, r * 1.15f, sides);
            Vector3[] inner = Builder.Ring(bottom, lean, r * 0.75f, sides);
            for (int i = 0; i < sides; i++)
            {
                int j = (i + 1) % sides;
                b.QuadFacing(outer[i], outer[j], inner[j], inner[i], lean, lip);
                b.TriFacing(bottom, inner[i], inner[j], lean, HollowColor);
            }

            if (colliders) AddCapsule(go, root, top, r * 1.1f);
        }
    }

    // ---------------------------------------------------------------- Colliders

    static void AddCapsule(GameObject parent, Vector3 from, Vector3 to, float radius)
    {
        Vector3 axis = to - from;
        float length = axis.magnitude;
        if (length < 0.001f) return;

        GameObject go = new GameObject("Collider");
        go.transform.SetParent(parent.transform, false);
        go.transform.localPosition = (from + to) * 0.5f;
        go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, axis / length);

        CapsuleCollider cap = go.AddComponent<CapsuleCollider>();
        cap.direction = 1; // axe Y local
        cap.radius = radius;
        cap.height = length + radius * 2f;
    }

    static void AddConvex(GameObject go, Mesh mesh)
    {
        MeshCollider mc = go.AddComponent<MeshCollider>();
        mc.sharedMesh = mesh;
        mc.convex = true;
    }

    // ---------------------------------------------------------------- Matériaux

    /// <summary>Un matériau partagé par couleur : peu de matériaux différents, donc peu d'appels de rendu.</summary>
    static Material GetMaterial(Color color)
    {
        // Couleurs quantifiées : les nuances très proches partagent le même matériau
        const float step = 20f;
        Color32 key = new Color32((byte)Mathf.Min(255f, Mathf.Round(Mathf.Clamp01(color.r) * 255f / step) * step),
                                  (byte)Mathf.Min(255f, Mathf.Round(Mathf.Clamp01(color.g) * 255f / step) * step),
                                  (byte)Mathf.Min(255f, Mathf.Round(Mathf.Clamp01(color.b) * 255f / step) * step), 255);

        if (materials.TryGetValue(key, out Material m) && m != null) return m;

        // On part du matériau par défaut du pipeline actif : son shader est toujours présent dans un build
        Material template = GraphicsSettings.currentRenderPipeline != null
            ? GraphicsSettings.currentRenderPipeline.defaultMaterial
            : null;

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

        m.name = "Corail " + key.r + "-" + key.g + "-" + key.b;
        Color c = key;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.15f);
        if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.15f);
        m.enableInstancing = true;

        materials[key] = m;
        return m;
    }

    static float Range(System.Random rng, float min, float max)
    {
        return min + (float)rng.NextDouble() * (max - min);
    }

    // ---------------------------------------------------------------- Construction de maillage

    /// <summary>
    /// Constructeur de maillage à faces plates, un sous-maillage par couleur.
    /// Chaque triangle est orienté automatiquement vers l'extérieur.
    /// </summary>
    class Builder
    {
        readonly List<Vector3> vertices = new List<Vector3>();
        readonly List<Vector3> normals = new List<Vector3>();
        readonly Dictionary<Material, List<int>> groups = new Dictionary<Material, List<int>>();

        /// <summary>Triangle dont la face visible s'éloigne du point 'inside'.</summary>
        public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 inside, Color color)
        {
            TriFacing(a, b, c, (a + b + c) / 3f - inside, color);
        }

        /// <summary>Triangle dont la face visible regarde dans le sens 'facing'.</summary>
        public void TriFacing(Vector3 a, Vector3 b, Vector3 c, Vector3 facing, Color color)
        {
            Vector3 n = Vector3.Cross(b - a, c - a);
            if (n.sqrMagnitude < 1e-12f) return; // triangle dégénéré
            n.Normalize();

            if (Vector3.Dot(n, facing) < 0f)
            {
                Vector3 t = b; b = c; c = t;
                n = -n;
            }

            Material mat = GetMaterial(color);
            if (!groups.TryGetValue(mat, out List<int> list))
            {
                list = new List<int>();
                groups[mat] = list;
            }

            int i = vertices.Count;
            vertices.Add(a); vertices.Add(b); vertices.Add(c);
            normals.Add(n); normals.Add(n); normals.Add(n);
            list.Add(i); list.Add(i + 1); list.Add(i + 2);
        }

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 inside, Color color)
        {
            Tri(a, b, c, inside, color);
            Tri(a, c, d, inside, color);
        }

        public void QuadFacing(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 facing, Color color)
        {
            TriFacing(a, b, c, facing, color);
            TriFacing(a, c, d, facing, color);
        }

        /// <summary>Anneau de points autour d'un axe.</summary>
        public static Vector3[] Ring(Vector3 center, Vector3 axis, float radius, int sides)
        {
            axis.Normalize();
            Vector3 reference = Mathf.Abs(Vector3.Dot(axis, Vector3.up)) < 0.9f ? Vector3.up : Vector3.right;
            Vector3 u = Vector3.Cross(axis, reference).normalized;
            Vector3 v = Vector3.Cross(axis, u);

            var ring = new Vector3[sides];
            for (int i = 0; i < sides; i++)
            {
                float a = Mathf.PI * 2f * i / sides;
                ring[i] = center + radius * (Mathf.Cos(a) * u + Mathf.Sin(a) * v);
            }
            return ring;
        }

        /// <summary>Tronçon de branche : prisme effilé de p0 (rayon r0) à p1 (rayon r1).</summary>
        public void Segment(Vector3 p0, Vector3 p1, float r0, float r1, int sides, Color color, bool cap)
        {
            Vector3 axis = p1 - p0;
            if (axis.sqrMagnitude < 1e-8f) return;

            Vector3[] a = Ring(p0, axis, r0, sides);
            Vector3[] b = Ring(p1, axis, r1, sides);
            Vector3 mid = (p0 + p1) * 0.5f;

            for (int i = 0; i < sides; i++)
            {
                int j = (i + 1) % sides;
                Quad(a[i], a[j], b[j], b[i], mid, color);
                if (cap) Tri(p1, b[i], b[j], mid, color);
            }
        }

        public Mesh ToMesh(string name)
        {
            Mesh mesh = new Mesh { name = name };
            if (vertices.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.subMeshCount = Mathf.Max(1, groups.Count);
            int s = 0;
            foreach (KeyValuePair<Material, List<int>> g in groups)
                mesh.SetTriangles(g.Value, s++);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Pose le maillage et ses matériaux sur l'objet.</summary>
        public Mesh Apply(GameObject go, string name)
        {
            Mesh mesh = ToMesh(name);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;

            var mats = new Material[groups.Count];
            groups.Keys.CopyTo(mats, 0);
            MeshRenderer r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = mats;
            return mesh;
        }
    }
}
