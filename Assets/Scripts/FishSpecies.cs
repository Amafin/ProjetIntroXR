using UnityEngine;

/// <summary>
/// Les espèces de poissons du jeu : nom, statut (pêchable ou protégée) et teinte.
///
/// Pour l'instant toutes les espèces utilisent le même modèle 3D (le poisson-ange) :
/// seule la teinte les distingue. Pour ajouter une espèce, ajoute une ligne au tableau All.
/// </summary>
public class FishSpecies
{
    public readonly int id;
    public readonly string displayName;
    public readonly bool isProtected;
    /// <summary>Couleur multipliée à la texture du modèle (blanc = modèle d'origine).</summary>
    public readonly Color tint;
    /// <summary>Nom au pluriel, pour les objectifs (« Pêcher 3 poissons-anges empereurs »).</summary>
    public readonly string pluralName;
    /// <summary>Texte de la fiche affichée quand on identifie le poisson.</summary>
    public readonly string description;

    FishSpecies(int id, string displayName, string pluralName, bool isProtected, Color tint, string description)
    {
        this.id = id;
        this.displayName = displayName;
        this.isProtected = isProtected;
        this.tint = tint;
        this.pluralName = pluralName;
        this.description = description;
    }

    public static readonly FishSpecies[] All =
    {
        new FishSpecies(0, "Poisson-ange empereur", "poissons-anges empereurs", false, Color.white,
                        "Commun sur les récifs coralliens. Reconnaissable à ses rayures bleues et jaunes. " +
                        "Il vit seul ou en couple et se nourrit d'éponges."),
        new FishSpecies(1, "Poisson-ange flamme", "poissons-anges flammes", true, new Color(1f, 0.5f, 0.4f),
                        "Rare et menacé par la surpêche. Robe rouge orangé. " +
                        "Il ne doit pas être pêché : laisse-le sur le récif."),
    };

    /// <summary>L'espèce pêchée par défaut (le poisson-ange).</summary>
    public static FishSpecies Default => All[0];

    public static FishSpecies Get(int id)
    {
        return id >= 0 && id < All.Length ? All[id] : All[0];
    }

    /// <summary>Une espèce protégée au hasard (null s'il n'y en a aucune).</summary>
    public static FishSpecies RandomProtected()
    {
        int count = 0;
        foreach (FishSpecies s in All) if (s.isProtected) count++;
        if (count == 0) return null;

        int pick = Random.Range(0, count);
        foreach (FishSpecies s in All)
        {
            if (!s.isProtected) continue;
            if (pick-- == 0) return s;
        }
        return null;
    }
}
