using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Construit le modèle 3D du harpon par le code : une hampe fine à facettes, une poignée,
/// et une lame plate à double tranchant avec deux ardillons.
///
/// Tout est aligné sur l'axe Z (la pointe vers l'avant), l'origine étant au niveau de la poignée.
/// Les faces sont plates (pas de lissage) : le harpon a un aspect anguleux, pas arrondi.
/// </summary>
public static class HarpoonModel
{
    /// <summary>Position de l'extrémité de la pointe sur l'axe Z.</summary>
    public const float TipZ = 1.05f;
    /// <summary>Position du talon (arrière de la hampe) sur l'axe Z.</summary>
    public const float ButtZ = -0.80f;

    const string ModelName = "Model";

    /// <summary>Remplace l'enfant "Model" du harpon par le modèle généré. Renvoie le nouveau "Model".</summary>
    public static Transform Build(Transform harpoon)
    {
        // Retire l'ancien modèle (immédiatement en édition, en fin d'image en jeu)
        Transform old = harpoon.Find(ModelName);
        if (old != null)
        {
            old.name = ModelName + " (ancien)";
            old.gameObject.SetActive(false);
            if (Application.isPlaying) Object.Destroy(old.gameObject);
            else Object.DestroyImmediate(old.gameObject);
        }

        Transform model = new GameObject(ModelName).transform;
        model.SetParent(harpoon, false);

        Material shaftMat = MakeMaterial("Harpon - Hampe", new Color(0.13f, 0.15f, 0.17f), 0.6f, 0.55f);
        Material gripMat = MakeMaterial("Harpon - Poignée", new Color(0.04f, 0.04f, 0.045f), 0f, 0.2f);
        Material steelMat = MakeMaterial("Harpon - Acier", new Color(0.78f, 0.80f, 0.83f), 0.95f, 0.8f);
        Material accentMat = MakeMaterial("Harpon - Repère", new Color(0.95f, 0.45f, 0.05f), 0.2f, 0.5f);

        AddPart(model, "Shaft", BuildShaft(), shaftMat);
        AddPart(model, "Grip", BuildGrip(), gripMat);
        AddPart(model, "Head", BuildHead(), steelMat);
        AddPart(model, "Bands", BuildBands(), accentMat);

        return model;
    }

    // ---------------------------------------------------------------- Pièces

    /// <summary>Hampe : tube hexagonal très fin, légèrement effilé vers l'avant, avec un talon.</summary>
    static Mesh BuildShaft()
    {
        var b = new Builder();
        const int sides = 6;

        // Talon : petit bouchon évasé à l'arrière
        b.Frustum(ButtZ, 0.006f, ButtZ + 0.012f, 0.014f, sides, true, false);
        b.Frustum(ButtZ + 0.012f, 0.014f, ButtZ + 0.05f, 0.014f, sides, false, false);
        b.Frustum(ButtZ + 0.05f, 0.014f, ButtZ + 0.07f, 0.0105f, sides, false, false);

        // Hampe
        b.Frustum(ButtZ + 0.07f, 0.0105f, 0.74f, 0.0085f, sides, false, false);

        // Douille : s'élargit un peu pour recevoir la lame
        b.Frustum(0.74f, 0.0085f, 0.76f, 0.012f, sides, false, false);
        b.Frustum(0.76f, 0.012f, 0.80f, 0.012f, sides, false, true);

        return b.ToMesh("Harpon Hampe");
    }

    /// <summary>Poignée : manchon octogonal autour du point de prise (z = 0), avec deux butées.</summary>
    static Mesh BuildGrip()
    {
        var b = new Builder();
        const int sides = 8;

        b.Frustum(-0.135f, 0.0125f, -0.125f, 0.019f, sides, true, false);  // butée arrière
        b.Frustum(-0.125f, 0.019f, -0.110f, 0.019f, sides, false, false);
        b.Frustum(-0.110f, 0.019f, -0.100f, 0.015f, sides, false, false);
        b.Frustum(-0.100f, 0.015f, 0.100f, 0.015f, sides, false, false);    // manchon
        b.Frustum(0.100f, 0.015f, 0.110f, 0.019f, sides, false, false);
        b.Frustum(0.110f, 0.019f, 0.125f, 0.019f, sides, false, false);     // butée avant
        b.Frustum(0.125f, 0.019f, 0.135f, 0.0125f, sides, false, true);

        return b.ToMesh("Harpon Poignée");
    }

    /// <summary>Deux bagues de couleur sur la hampe (repères visuels sous l'eau).</summary>
    static Mesh BuildBands()
    {
        var b = new Builder();
        const int sides = 6;

        b.Frustum(0.60f, 0.0115f, 0.63f, 0.0115f, sides, true, true);
        b.Frustum(-0.62f, 0.0125f, -0.59f, 0.0125f, sides, true, true);

        return b.ToMesh("Harpon Bagues");
    }

    /// <summary>
    /// Tête : lame plate à section en losange (deux tranchants), longue et effilée,
    /// et deux ardillons en flèche tournés vers l'arrière.
    /// </summary>
    static Mesh BuildHead()
    {
        var b = new Builder();

        // Sections de la lame : (z, demi-largeur sur X, demi-épaisseur sur Y)
        b.Blade(new[]
        {
            new Vector3(0.78f, 0.0090f, 0.0090f),  // sort de la douille
            new Vector3(0.83f, 0.0100f, 0.0065f),  // col
            new Vector3(0.875f, 0.0230f, 0.0050f), // épaules (partie la plus large)
            new Vector3(0.96f, 0.0140f, 0.0038f),
            new Vector3(TipZ, 0f, 0f),             // pointe
        });

        // Ardillons : lames triangulaires plates partant des épaules vers l'arrière
        for (int side = -1; side <= 1; side += 2)
        {
            float s = side;
            Vector3 rootFront = new Vector3(s * 0.016f, 0f, 0.905f);
            Vector3 rootBack = new Vector3(s * 0.011f, 0f, 0.845f);
            Vector3 point = new Vector3(s * 0.050f, 0f, 0.775f);
            b.Wedge(rootFront, rootBack, point, 0.0038f);
        }

        return b.ToMesh("Harpon Tête");
    }

    // ---------------------------------------------------------------- Assemblage

    static void AddPart(Transform parent, string name, Mesh mesh, Material material)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = material;
        r.shadowCastingMode = ShadowCastingMode.On;
    }

    static Material MakeMaterial(string name, Color color, float metallic, float smoothness)
    {
        // On part du matériau par défaut du pipeline de rendu actif : son shader est toujours
        // présent, y compris dans un build (un Shader.Find peut y renvoyer un shader absent -> rose).
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

        m.name = name;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
        if (m.HasProperty("_Color")) m.SetColor("_Color", color);
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
        if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smoothness);
        return m;
    }

    // ---------------------------------------------------------------- Construction de maillage

    /// <summary>
    /// Petit constructeur de maillage à faces plates. Chaque triangle est orienté automatiquement
    /// vers l'extérieur (à l'opposé d'un point "intérieur" fourni), donc aucune face à l'envers.
    /// </summary>
    class Builder
    {
        readonly List<Vector3> vertices = new List<Vector3>();
        readonly List<Vector3> normals = new List<Vector3>();
        readonly List<int> triangles = new List<int>();

        public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 inside)
        {
            Vector3 n = Vector3.Cross(b - a, c - a);
            if (n.sqrMagnitude < 1e-14f) return; // triangle dégénéré
            n.Normalize();

            // La normale doit s'éloigner de l'intérieur de la pièce
            Vector3 center = (a + b + c) / 3f;
            if (Vector3.Dot(n, center - inside) < 0f)
            {
                Vector3 t = b; b = c; c = t;
                n = -n;
            }

            int i = vertices.Count;
            vertices.Add(a); vertices.Add(b); vertices.Add(c);
            normals.Add(n); normals.Add(n); normals.Add(n);
            triangles.Add(i); triangles.Add(i + 1); triangles.Add(i + 2);
        }

        /// <summary>Triangle d'une pièce centrée sur l'axe Z : l'intérieur est le point de l'axe à sa hauteur.</summary>
        public void TriAroundAxis(Vector3 a, Vector3 b, Vector3 c)
        {
            Tri(a, b, c, new Vector3(0f, 0f, (a.z + b.z + c.z) / 3f));
        }

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 inside)
        {
            Tri(a, b, c, inside);
            Tri(a, c, d, inside);
        }

        /// <summary>Tronc de prisme à N côtés le long de Z, de (z0, r0) à (z1, r1).</summary>
        public void Frustum(float z0, float r0, float z1, float r1, int sides, bool capStart, bool capEnd)
        {
            Vector3 mid = new Vector3(0f, 0f, (z0 + z1) * 0.5f);

            for (int i = 0; i < sides; i++)
            {
                float a0 = (i + 0.5f) / sides * Mathf.PI * 2f;
                float a1 = (i + 1.5f) / sides * Mathf.PI * 2f;
                Vector3 d0 = new Vector3(Mathf.Cos(a0), Mathf.Sin(a0), 0f);
                Vector3 d1 = new Vector3(Mathf.Cos(a1), Mathf.Sin(a1), 0f);

                Vector3 p00 = d0 * r0 + Vector3.forward * z0;
                Vector3 p01 = d1 * r0 + Vector3.forward * z0;
                Vector3 p10 = d0 * r1 + Vector3.forward * z1;
                Vector3 p11 = d1 * r1 + Vector3.forward * z1;

                Quad(p00, p01, p11, p10, mid);

                if (capStart) Tri(new Vector3(0f, 0f, z0), p00, p01, mid);
                if (capEnd) Tri(new Vector3(0f, 0f, z1), p10, p11, mid);
            }
        }

        /// <summary>
        /// Lame à section en losange. Chaque section = (z, demi-largeur X, demi-épaisseur Y).
        /// Une section de taille nulle forme une pointe.
        /// </summary>
        public void Blade(Vector3[] sections)
        {
            for (int s = 0; s < sections.Length - 1; s++)
            {
                Vector3[] r0 = Ring(sections[s]);
                Vector3[] r1 = Ring(sections[s + 1]);

                for (int i = 0; i < 4; i++)
                {
                    int j = (i + 1) % 4;
                    TriAroundAxis(r0[i], r0[j], r1[j]);
                    TriAroundAxis(r0[i], r1[j], r1[i]);
                }
            }

            // Ferme l'arrière de la lame
            Vector3[] first = Ring(sections[0]);
            Vector3 back = new Vector3(0f, 0f, sections[0].x);
            Vector3 inside = new Vector3(0f, 0f, sections[0].x + 0.01f);
            for (int i = 0; i < 4; i++)
                Tri(back, first[i], first[(i + 1) % 4], inside);
        }

        static Vector3[] Ring(Vector3 section)
        {
            float z = section.x, w = section.y, t = section.z;
            return new[]
            {
                new Vector3(w, 0f, z),
                new Vector3(0f, t, z),
                new Vector3(-w, 0f, z),
                new Vector3(0f, -t, z),
            };
        }

        /// <summary>
        /// Lame triangulaire plate : épaisse le long de l'arête (rootA-rootB), tranchante à la pointe.
        /// </summary>
        public void Wedge(Vector3 rootA, Vector3 rootB, Vector3 point, float halfThickness)
        {
            Vector3 up = Vector3.up * halfThickness;
            Vector3 aTop = rootA + up, aBot = rootA - up;
            Vector3 bTop = rootB + up, bBot = rootB - up;
            Vector3 inside = (rootA + rootB + point) / 3f;

            Tri(aTop, bTop, point, inside);     // dessus
            Tri(aBot, bBot, point, inside);     // dessous
            Tri(aTop, aBot, point, inside);     // tranchant avant
            Tri(bTop, bBot, point, inside);     // tranchant arrière
            Quad(aTop, bTop, bBot, aBot, inside); // côté collé à la lame
        }

        public Mesh ToMesh(string name)
        {
            Mesh mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
