using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>
/// Outil de diagnostic : à poser sur un objet qui devrait pouvoir être saisi (le harpon, un cube de test).
/// Écrit dans la console :
///  - si un XR Interaction Manager existe,
///  - les colliders de l'objet,
///  - tous les interacteurs de la scène (actifs ou non), leurs layers et leur distance à l'objet,
///  - chaque fois que l'objet est survolé / saisi.
/// Fonctionne avec toutes les versions de XR Interaction Toolkit (réflexion, aucune dépendance).
/// À retirer une fois le problème résolu.
/// </summary>
public class GrabDiagnostic : MonoBehaviour
{
    [Tooltip("Délai entre deux relevés de distance (secondes)")]
    public float interval = 1f;

    private Component grab;
    private PropertyInfo isHoveredProp;
    private PropertyInfo isSelectedProp;
    private Collider[] colliders;
    private bool lastHover;
    private bool lastSelect;
    private float timer;

    void Start()
    {
        colliders = GetComponentsInChildren<Collider>();

        foreach (Component c in GetComponents<Component>())
        {
            if (c == null) continue;
            PropertyInfo sel = c.GetType().GetProperty("isSelected", BindingFlags.Public | BindingFlags.Instance);
            PropertyInfo hov = c.GetType().GetProperty("isHovered", BindingFlags.Public | BindingFlags.Instance);
            if (sel != null && hov != null)
            {
                grab = c;
                isSelectedProp = sel;
                isHoveredProp = hov;
                break;
            }
        }

        FullReport();
    }

    void Update()
    {
        if (grab == null) return;

        bool hover = (bool)isHoveredProp.GetValue(grab);
        bool select = (bool)isSelectedProp.GetValue(grab);

        if (hover != lastHover)
        {
            Debug.Log("[Grab] " + name + " survolé par une main : " + hover, this);
            lastHover = hover;
        }
        if (select != lastSelect)
        {
            Debug.Log("[Grab] " + name + " saisi : " + select, this);
            lastSelect = select;
        }

        timer += Time.deltaTime;
        if (timer >= interval)
        {
            timer = 0f;
            DistanceReport();
        }
    }

    // ---------------------------------------------------------------- Rapports

    void FullReport()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("[Grab] ===== DIAGNOSTIC DE SAISIE : " + name + " =====");

        // Interaction Manager
        int managers = 0;
        foreach (MonoBehaviour m in AllBehaviours())
            if (m != null && m.GetType().Name == "XRInteractionManager") managers++;
        sb.AppendLine(managers > 0
            ? "XR Interaction Manager : OK (" + managers + ")"
            : "XR Interaction Manager : ABSENT -> rien ne peut être saisi. Ajoute-en un (GameObject > XR > Interaction Manager).");

        // Interactable
        if (grab == null)
            sb.AppendLine("XR Grab Interactable : ABSENT sur cet objet -> ajoute le composant.");
        else
            sb.AppendLine("Interactable : " + grab.GetType().Name + " | Interaction Layers = " + LayersText(ReadLayers(grab)));

        // Colliders
        if (colliders == null || colliders.Length == 0)
        {
            sb.AppendLine("Colliders : AUCUN -> impossible à saisir. Ajoute un collider.");
        }
        else
        {
            sb.AppendLine("Colliders : " + colliders.Length);
            foreach (Collider c in colliders)
                sb.AppendLine("   - " + c.GetType().Name + " sur " + c.name + (c.isTrigger ? " (trigger)" : "") + (c.enabled ? "" : " (DÉSACTIVÉ)"));
        }

        // Interacteurs
        int grabLayers = grab != null ? ReadLayers(grab) : -1;
        int count = 0;
        foreach (MonoBehaviour m in AllBehaviours())
        {
            if (m == null) continue;
            string n = m.GetType().Name;
            if (!n.Contains("Interactor") || n.Contains("Interactable")) continue;

            count++;
            bool active = m.isActiveAndEnabled;
            int layers = ReadLayers(m);
            string compat = (grabLayers >= 0 && layers >= 0)
                ? ((grabLayers & layers) != 0 ? "layers compatibles" : "LAYERS INCOMPATIBLES")
                : "layers inconnus";
            sb.AppendLine("Interacteur : " + n + " sur '" + Path(m.transform) + "' | " +
                          (active ? "ACTIF" : "inactif") + " | " + compat);
        }
        if (count == 0)
            sb.AppendLine("Interacteurs : AUCUN dans la scène -> les mains ne peuvent rien saisir.");

        Debug.Log(sb.ToString(), this);
    }

    void DistanceReport()
    {
        if (colliders == null || colliders.Length == 0) return;

        var sb = new System.Text.StringBuilder();
        bool any = false;

        foreach (MonoBehaviour m in AllBehaviours())
        {
            if (m == null || !m.isActiveAndEnabled) continue;
            string n = m.GetType().Name;
            if (!n.Contains("Interactor") || n.Contains("Interactable")) continue;

            Vector3 p = m.transform.position;
            float best = float.MaxValue;
            foreach (Collider c in colliders)
            {
                if (c == null || !c.enabled) continue;
                float d = Vector3.Distance(p, c.ClosestPoint(p));
                if (d < best) best = d;
            }

            if (best < float.MaxValue)
            {
                sb.Append("  " + Path(m.transform) + " (" + n + ") à " + best.ToString("F2") + " m");
                any = true;
            }
        }

        if (any) Debug.Log("[Grab] distance au harpon :" + sb, this);
        else Debug.Log("[Grab] aucun interacteur ACTIF pour l'instant (mains non suivies ? manettes posées ?)", this);
    }

    // ---------------------------------------------------------------- Utilitaires

    static IEnumerable<MonoBehaviour> AllBehaviours()
    {
#if UNITY_2022_2_OR_NEWER
        return FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
        return FindObjectsOfType<MonoBehaviour>(true);
#endif
    }

    static int ReadLayers(Component c)
    {
        try
        {
            PropertyInfo p = c.GetType().GetProperty("interactionLayers", BindingFlags.Public | BindingFlags.Instance);
            if (p == null) return -1;
            object mask = p.GetValue(c);
            PropertyInfo v = mask.GetType().GetProperty("value", BindingFlags.Public | BindingFlags.Instance);
            return v != null ? (int)v.GetValue(mask) : -1;
        }
        catch
        {
            return -1;
        }
    }

    static string LayersText(int v)
    {
        return v < 0 ? "inconnus" : "0x" + v.ToString("X");
    }

    static string Path(Transform t)
    {
        string s = t.name;
        int depth = 0;
        while (t.parent != null && depth < 3)
        {
            t = t.parent;
            s = t.name + "/" + s;
            depth++;
        }
        return s;
    }
}
