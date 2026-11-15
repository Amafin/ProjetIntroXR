using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Interface de plongée, fixée devant le regard du joueur (casque VR) :
///   - en bas à gauche  : la bonbonne d'air, « AIR x % » et, au-dessus, l'objectif en cours ;
///   - en haut à droite : la fiche du poisson identifié (image, nom, statut, description) ;
///   - en bas à droite  : le petit cercle qui tourne, uniquement pendant le changement de bonbonne ;
///   - au centre        : le cercle qui se remplit pendant le scan d'un poisson.
///
/// Toute l'interface est construite par le code au lancement (formes dessinées dans des
/// textures) : aucun prefab ni image à importer. Elle lit l'état de DiveGame.
/// </summary>
public class DiveHUD : MonoBehaviour
{
    [Header("Placement devant le regard")]
    [Tooltip("Distance de l'interface devant les yeux (m)")]
    public float distance = 0.9f;
    [Tooltip("Taille de l'interface : mètres par pixel d'interface (1000 x 640 pixels)")]
    public float scale = 0.0007f;
    [Tooltip("Décalage vertical (m) : négatif = plus bas dans le champ de vision")]
    public float verticalOffset = -0.02f;

    [Header("Explication du scan (affichée une seule fois)")]
    [Tooltip("Texte montré la première fois qu'on fixe un poisson")]
    public string scanTutorial = "Appuie sur A pour scanner ce poisson";

    [Header("Couleurs")]
    [Tooltip("Fond de la fiche : le sable du fond marin")]
    public Color cardColor = new Color(0.93f, 0.86f, 0.70f);
    [Tooltip("Traits de la fiche : bleu nuit, comme l'eau profonde")]
    public Color lineColor = new Color(0.05f, 0.16f, 0.26f);
    public Color textColor = new Color(0.05f, 0.16f, 0.26f);
    public Color hudTextColor = new Color(1f, 0.97f, 0.88f);
    [Tooltip("Pêchable : le vert des coraux tabulaires")]
    public Color fishableColor = new Color(0.16f, 0.60f, 0.47f);
    [Tooltip("Protégée : le rouge orangé des coraux branchus")]
    public Color protectedColor = new Color(0.85f, 0.29f, 0.20f);
    [Tooltip("Anneau de scan")]
    public Color scanColor = new Color(1f, 0.74f, 0.30f);
    public Color airColor = new Color(0.35f, 0.85f, 0.95f);
    public Color lowAirColor = new Color(1f, 0.62f, 0.15f);

    const float Line = 5f; // épaisseur des traits de la fiche

    private DiveGame game;
    private Transform head;
    private RectTransform canvasRect;

    // Objectif + air
    private TextMeshProUGUI objectiveText, tankPercent;
    private Image tankFill;

    // Fiche poisson
    private CanvasGroup cardGroup;
    private RawImage cardImage;
    private Image cardDot;
    private TextMeshProUGUI cardName, cardStatus, cardDescription;
    private int cardSerial = -1;
    private readonly Dictionary<int, Texture> portraits = new Dictionary<int, Texture>();

    // Changement de bonbonne
    private RectTransform spinner;
    private Image spinnerImage;
    private TextMeshProUGUI tankHint;

    // Espèces protégées : avertissement, puis partie perdue
    private CanvasGroup warningGroup, gameOverGroup;
    private TextMeshProUGUI warningTitle, warningText, gameOverText, restartText;

    // Scan d'un poisson
    private Image scanTrack, scanFill;
    private TextMeshProUGUI tutorialText;
    private CanvasGroup tutorialGroup;

    void Start()
    {
        game = GetComponent<DiveGame>();
        if (game == null) game = DiveGame.Instance;
        TryBuild();
    }

    void TryBuild()
    {
        if (canvasRect != null) return;
        if (Camera.main == null) return; // la caméra n'est pas encore prête : on réessaiera
        head = Camera.main.transform;
        Build();
    }

    void OnDestroy()
    {
        foreach (Texture t in portraits.Values)
            if (t != null) Destroy(t);
    }

    // ---------------------------------------------------------------- Construction

    void Build()
    {
        GameObject go = new GameObject("Dive HUD", typeof(RectTransform), typeof(Canvas));
        go.layer = head.gameObject.layer;
        go.transform.SetParent(head, false);

        Canvas canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = Camera.main;

        canvasRect = go.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(1000f, 640f);
        canvasRect.localPosition = new Vector3(0f, verticalOffset, distance);
        canvasRect.localRotation = Quaternion.identity;
        canvasRect.localScale = Vector3.one * scale;

        Vector2 bottomLeft = new Vector2(0f, 0f);
        Vector2 topLeft = new Vector2(0f, 1f);
        Vector2 center = new Vector2(0.5f, 0.5f);

        // ---- Bonbonne (bas gauche)
        RectTransform tank = NewRect("Bonbonne", canvasRect, bottomLeft, new Vector2(8f, 0f), new Vector2(96f, 220f));
        NewImage("Silhouette", tank, HudSprites.TankBody, new Color(0.02f, 0.10f, 0.16f, 0.85f), center, Vector2.zero, new Vector2(96f, 220f));
        tankFill = NewImage("Air", tank, HudSprites.TankInner, airColor, center, Vector2.zero, new Vector2(96f, 220f));
        tankFill.type = Image.Type.Filled;
        tankFill.fillMethod = Image.FillMethod.Vertical;
        tankFill.fillOrigin = (int)Image.OriginVertical.Bottom;
        NewImage("Contour", tank, HudSprites.TankOutline, new Color(0.85f, 0.93f, 0.97f, 0.95f), center, Vector2.zero, new Vector2(96f, 220f));
        NewImage("Reflet", tank, HudSprites.TankGloss, new Color(1f, 1f, 1f, 0.22f), center, Vector2.zero, new Vector2(96f, 220f));

        // ---- Objectif, puis « AIR x % », à droite de la bonbonne
        objectiveText = NewText("Objectif", canvasRect, "", 27f, hudTextColor, bottomLeft, new Vector2(122f, 132f), new Vector2(420f, 80f), TextAlignmentOptions.BottomLeft);
        Outline(objectiveText);
        objectiveText.enableAutoSizing = true;
        objectiveText.fontSizeMin = 19f;
        objectiveText.fontSizeMax = 27f;

        TextMeshProUGUI airLabel = NewText("Air", canvasRect, "AIR", 25f, hudTextColor, bottomLeft, new Vector2(122f, 62f), new Vector2(200f, 34f), TextAlignmentOptions.BottomLeft);
        Outline(airLabel);
        tankPercent = NewText("Pourcentage", canvasRect, "100 %", 40f, hudTextColor, bottomLeft, new Vector2(122f, 10f), new Vector2(220f, 52f), TextAlignmentOptions.BottomLeft);
        Outline(tankPercent);

        // ---- Fiche poisson (haut droite) : image à gauche, nom + statut, trait, description
        const float cardW = 440f, cardH = 300f, imageW = 156f;
        RectTransform card = NewRect("Fiche poisson", canvasRect, new Vector2(1f, 1f), new Vector2(-6f, -6f), new Vector2(cardW, cardH));
        cardGroup = card.gameObject.AddComponent<CanvasGroup>();
        cardGroup.alpha = 0f;

        Sliced(NewImage("Fond", card, HudSprites.CardFill, cardColor, topLeft, Vector2.zero, new Vector2(cardW, cardH)));
        Sliced(NewImage("Bord", card, HudSprites.CardOutline, lineColor, topLeft, Vector2.zero, new Vector2(cardW, cardH)));

        // Cadre de l'image
        Sliced(NewImage("Cadre image", card, HudSprites.CardOutline, lineColor, topLeft, Vector2.zero, new Vector2(imageW, cardH)));
        // Fond à facettes (eau), comme les maillages low-poly du jeu, puis le poisson par-dessus
        Image water = NewImage("Fond image", card, HudSprites.Facets, Color.white, topLeft, new Vector2(Line + 3f, -(Line + 3f)), new Vector2(imageW - 2f * (Line + 3f), cardH - 2f * (Line + 3f)));
        water.preserveAspect = false;
        RectTransform imageRect = NewRect("Image poisson", card, topLeft, new Vector2(12f, -70f), new Vector2(imageW - 24f, cardH - 140f));
        cardImage = imageRect.gameObject.AddComponent<RawImage>();
        cardImage.raycastTarget = false;
        cardImage.color = Color.white;

        float textX = imageW + 14f;
        float textW = cardW - textX - 16f;

        cardName = NewText("Nom", card, "", 24f, textColor, topLeft, new Vector2(textX, -14f), new Vector2(textW - 34f, 34f), TextAlignmentOptions.TopLeft);
        cardName.enableAutoSizing = true;
        cardName.fontSizeMin = 15f;
        cardName.fontSizeMax = 24f;
        cardDot = NewImage("Pastille", card, HudSprites.Disc, fishableColor, new Vector2(1f, 1f), new Vector2(-18f, -16f), new Vector2(30f, 30f));
        cardStatus = NewText("Statut", card, "", 16f, fishableColor, topLeft, new Vector2(textX, -50f), new Vector2(textW, 24f), TextAlignmentOptions.TopLeft);

        // Trait de séparation, de l'image jusqu'au bord droit
        NewImage("Trait", card, null, lineColor, topLeft, new Vector2(imageW - Line, -80f), new Vector2(cardW - imageW + Line, Line));

        cardDescription = NewText("Description", card, "", 18f, textColor, topLeft, new Vector2(textX, -96f), new Vector2(textW, cardH - 112f), TextAlignmentOptions.TopLeft);
        cardDescription.lineSpacing = 12f;

        // ---- Changement de bonbonne (bas droite) : petit cercle qui tourne + texte
        spinnerImage = NewImage("Cercle qui tourne", canvasRect, HudSprites.Ring, hudTextColor, new Vector2(1f, 0f), new Vector2(-312f, 34f), new Vector2(40f, 40f));
        spinnerImage.rectTransform.pivot = center; // tourne autour de son centre
        spinnerImage.type = Image.Type.Filled;
        spinnerImage.fillMethod = Image.FillMethod.Radial360;
        spinnerImage.fillAmount = 0.75f;
        spinner = spinnerImage.rectTransform;

        tankHint = NewText("Texte bonbonne", canvasRect, "Changement bonbonne", 21f, hudTextColor, new Vector2(1f, 0f), new Vector2(-8f, 18f), new Vector2(276f, 32f), TextAlignmentOptions.BottomLeft);
        Outline(tankHint);
        spinnerImage.enabled = false;
        tankHint.enabled = false;

        // ---- Scan (centre du regard) : cercle transparent qui se remplit
        scanTrack = NewImage("Scan - anneau", canvasRect, HudSprites.ScanRing, new Color(1f, 1f, 1f, 0.26f), center, Vector2.zero, new Vector2(150f, 150f));
        scanFill = NewImage("Scan - remplissage", canvasRect, HudSprites.ScanRing, scanColor, center, Vector2.zero, new Vector2(150f, 150f));
        scanFill.type = Image.Type.Filled;
        scanFill.fillMethod = Image.FillMethod.Radial360;
        scanFill.fillOrigin = (int)Image.Origin360.Top;
        scanFill.fillClockwise = true;
        scanFill.fillAmount = 0f;
        scanTrack.enabled = false;
        scanFill.enabled = false;

        // ---- Explication du scan, sous le centre du regard (une seule fois)
        tutorialText = NewText("Explication scan", canvasRect, scanTutorial, 24f, hudTextColor, center, new Vector2(0f, -72f), new Vector2(620f, 40f), TextAlignmentOptions.Center);
        Outline(tutorialText);
        tutorialGroup = tutorialText.gameObject.AddComponent<CanvasGroup>();
        tutorialGroup.alpha = 0f;

        // ---- Avertissement « espèce protégée » (haut, centre) : bandeau rouge corail aux coins coupés
        const float warnW = 470f, warnH = 104f; // tient à gauche de la fiche poisson
        RectTransform warning = NewRect("Avertissement", canvasRect, new Vector2(0.5f, 1f), new Vector2(-185f, -6f), new Vector2(warnW, warnH));
        warningGroup = warning.gameObject.AddComponent<CanvasGroup>();
        warningGroup.alpha = 0f;
        Sliced(NewImage("Fond", warning, HudSprites.CardFill, protectedColor, topLeft, Vector2.zero, new Vector2(warnW, warnH)));
        Sliced(NewImage("Bord", warning, HudSprites.CardOutline, lineColor, topLeft, Vector2.zero, new Vector2(warnW, warnH)));
        warningTitle = NewText("Titre", warning, "ESPÈCE PROTÉGÉE !", 30f, cardColor, new Vector2(0.5f, 1f), new Vector2(0f, -14f), new Vector2(warnW - 40f, 38f), TextAlignmentOptions.Top);
        warningTitle.fontStyle = FontStyles.Bold;
        warningText = NewText("Texte", warning, "", 20f, cardColor, new Vector2(0.5f, 0f), new Vector2(0f, 14f), new Vector2(warnW - 40f, 30f), TextAlignmentOptions.Bottom);

        // ---- Partie perdue : voile sombre sur tout le champ de vision + message
        RectTransform over = NewRect("Partie perdue", canvasRect, center, Vector2.zero, new Vector2(1000f, 640f));
        gameOverGroup = over.gameObject.AddComponent<CanvasGroup>();
        gameOverGroup.alpha = 0f;
        Color veil = lineColor;
        veil.a = 0.9f;
        NewImage("Voile", over, null, veil, center, Vector2.zero, new Vector2(6000f, 6000f)); // déborde largement du regard

        const float overW = 640f, overH = 280f;
        Sliced(NewImage("Fond", over, HudSprites.CardFill, cardColor, center, Vector2.zero, new Vector2(overW, overH)));
        Sliced(NewImage("Bord", over, HudSprites.CardOutline, lineColor, center, Vector2.zero, new Vector2(overW, overH)));
        TextMeshProUGUI overTitle = NewText("Titre", over, "PARTIE PERDUE", 46f, protectedColor, center, new Vector2(0f, 82f), new Vector2(overW - 60f, 60f), TextAlignmentOptions.Center);
        overTitle.fontStyle = FontStyles.Bold;
        gameOverText = NewText("Texte", over, "", 23f, textColor, center, new Vector2(0f, 4f), new Vector2(overW - 80f, 90f), TextAlignmentOptions.Center);
        restartText = NewText("Recommencer", over, "", 22f, textColor, center, new Vector2(0f, -92f), new Vector2(overW - 60f, 34f), TextAlignmentOptions.Center);
        restartText.fontStyle = FontStyles.Bold;
    }

    // ---------------------------------------------------------------- Mise à jour

    void LateUpdate()
    {
        if (canvasRect == null)
        {
            TryBuild();
            if (canvasRect == null) return;
        }
        if (game == null)
        {
            game = DiveGame.Instance;
            if (game == null) return;
        }

        UpdateObjective();
        UpdateTank();
        UpdateCard();
        UpdateTankChange();
        UpdateScan();
        UpdateProtected();
    }

    void UpdateProtected()
    {
        // ---- Avertissement
        float target = game.WarningTimeLeft > 0f && !game.GameOver ? Mathf.Clamp01(game.WarningTimeLeft / 0.5f) : 0f;
        if (target > 0f)
        {
            int left = Mathf.Max(0, game.protectedKillsToLose - game.ProtectedKills);
            warningText.text = left <= 1
                ? "Dernier avertissement : à la prochaine, la partie est perdue."
                : "Avertissement " + game.ProtectedKills + " sur " + (game.protectedKillsToLose - 1) + " : ne pêche pas cette espèce.";
        }
        warningGroup.alpha = Mathf.MoveTowards(warningGroup.alpha, target, Time.unscaledDeltaTime * 5f);

        // ---- Partie perdue
        gameOverGroup.alpha = Mathf.MoveTowards(gameOverGroup.alpha, game.GameOver ? 1f : 0f, Time.unscaledDeltaTime * 2.5f);
        if (game.GameOver)
        {
            gameOverText.text = "Tu as pêché " + game.ProtectedKills + " poissons d'une espèce protégée.\n" +
                                "Un bon pêcheur ne prend que ce que le récif peut donner.";
            restartText.text = game.CanRestart ? "Appuie sur A pour recommencer" : "";
        }
    }

    void UpdateScan()
    {
        bool scanning = game.IsScanning;
        scanTrack.enabled = scanning;
        scanFill.enabled = scanning;
        if (scanning)
        {
            scanFill.fillAmount = game.ScanProgress;

            // L'anneau entoure le poisson scanné et le suit ; il tourne lentement sur lui-même
            Vector2 pos = FishOnCanvas(game.ScanTarget);
            scanTrack.rectTransform.anchoredPosition = pos;
            scanFill.rectTransform.anchoredPosition = pos;
            float size = Mathf.Lerp(190f, 150f, game.ScanProgress); // se resserre en se remplissant
            scanTrack.rectTransform.sizeDelta = scanFill.rectTransform.sizeDelta = new Vector2(size, size);
            scanTrack.rectTransform.localRotation = Quaternion.Euler(0f, 0f, Time.unscaledTime * 25f);
        }

        // L'explication apparaît et disparaît en fondu
        float target = game.TutorialTimeLeft > 0f && !scanning ? Mathf.Clamp01(game.TutorialTimeLeft / 0.5f) : 0f;
        tutorialGroup.alpha = Mathf.MoveTowards(tutorialGroup.alpha, target, Time.unscaledDeltaTime * 4f);
    }

    void UpdateObjective()
    {
        DiveGame.Objective o = game.Current;
        if (o == null) return;

        bool done = game.CompletedTimeLeft > 0f;
        objectiveText.text = "Objectif : " + o.Title + " [" + o.ProgressText + "]" + (done ? "\nTerminé !" : "");
        objectiveText.color = done ? fishableColor : hudTextColor;
    }

    void UpdateTank()
    {
        float air = game.IsChangingTank ? game.ChangeProgress : game.Air01;
        // L'intérieur du corps occupe de 7 % à 74,5 % de la hauteur de l'image
        tankFill.fillAmount = air > 0f ? Mathf.Lerp(0.07f, 0.745f, air) : 0f;

        Color c;
        if (game.IsChangingTank) c = airColor;
        else if (game.TankEmpty) c = protectedColor;
        else if (game.LowAir) c = Color.Lerp(protectedColor, lowAirColor, 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 7f));
        else c = Color.Lerp(lowAirColor, airColor, Mathf.InverseLerp(game.lowAirRatio, 0.6f, air));
        tankFill.color = c;

        tankPercent.text = Mathf.CeilToInt(game.Air01 * 100f) + " %";
        tankPercent.color = game.LowAir && !game.IsChangingTank ? c : hudTextColor;
    }

    void UpdateCard()
    {
        FishSpecies s = game.PopupSpecies;

        if (s != null && game.PopupSerial != cardSerial)
        {
            cardSerial = game.PopupSerial;

            Color c = s.isProtected ? protectedColor : fishableColor;
            cardDot.color = c;
            cardName.text = s.displayName;
            cardStatus.text = s.isProtected ? "Espèce protégée" : "Espèce non protégée";
            cardStatus.color = c;
            cardDescription.text = s.description;

            Texture portrait = GetPortrait(s);
            cardImage.texture = portrait;
            cardImage.enabled = portrait != null;
        }

        // Apparition immédiate, disparition en fondu sur la fin
        float target = s != null ? Mathf.Clamp01(game.PopupTimeLeft / 0.4f) : 0f;
        cardGroup.alpha = Mathf.MoveTowards(cardGroup.alpha, target, Time.unscaledDeltaTime * 8f);
    }

    void UpdateTankChange()
    {
        // Rien en temps normal : le rond qui tourne n'apparaît que pendant le changement
        bool changing = game.IsChangingTank;
        spinnerImage.enabled = changing;
        tankHint.enabled = changing;
        if (changing) spinner.localRotation = Quaternion.Euler(0f, 0f, -Time.unscaledTime * 300f);
    }

    /// <summary>Position d'un poisson sur l'interface (pixels depuis le centre), bornée à l'écran.</summary>
    Vector2 FishOnCanvas(Fish fish)
    {
        if (fish == null || head == null) return Vector2.zero;

        Vector3 local = head.InverseTransformPoint(fish.WorldBounds.center);
        if (local.z < 0.05f) return Vector2.zero;

        // Projection sur le plan de l'interface, placé à 'distance' devant les yeux
        float k = distance / local.z / Mathf.Max(1e-6f, scale);
        Vector2 pos = new Vector2(local.x * k, (local.y * distance / local.z - verticalOffset) / Mathf.Max(1e-6f, scale));
        pos.x = Mathf.Clamp(pos.x, -400f, 400f);
        pos.y = Mathf.Clamp(pos.y, -230f, 230f);
        return pos;
    }

    // ---------------------------------------------------------------- Image du poisson

    /// <summary>
    /// Photo de profil de l'espèce, prise une seule fois : on pose un exemplaire du poisson
    /// très loin de la scène, une caméra temporaire le photographie de côté, puis tout est supprimé.
    /// </summary>
    Texture GetPortrait(FishSpecies species)
    {
        if (portraits.TryGetValue(species.id, out Texture cached)) return cached;

        Texture result = null;
        try
        {
            result = RenderPortrait(species);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("DiveHUD: image du poisson impossible à générer (" + e.Message + ").", this);
        }

        portraits[species.id] = result; // même en cas d'échec : on ne réessaie pas à chaque fiche
        return result;
    }

    RenderTexture RenderPortrait(FishSpecies species)
    {
        FishSpawner spawner = FindAnyObjectByType<FishSpawner>();
        if (spawner == null || spawner.fishPrefab == null) return null;

        Vector3 studio = new Vector3(0f, -2000f, 0f);
        Fish model = Instantiate(spawner.fishPrefab, studio, Quaternion.identity);
        GameObject camObject = new GameObject("Photo poisson");
        try
        {
            // Juste un mannequin : ni comportement, ni physique
            model.enabled = false;
            foreach (Collider col in model.GetComponentsInChildren<Collider>()) col.enabled = false;
            Rigidbody body = model.GetComponent<Rigidbody>();
            if (body != null) body.isKinematic = true;
            model.SetSpecies(species);

            Renderer[] renderers = model.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return null;
            Bounds b = renderers[0].bounds;
            foreach (Renderer r in renderers) b.Encapsulate(r.bounds);

            const int w = 264, h = 320;
            var rt = new RenderTexture(w, h, 16, RenderTextureFormat.ARGB32) { name = "Portrait " + species.displayName };

            // Vue de profil : la caméra est sur le côté (axe X), le poisson est allongé sur Z
            Camera cam = camObject.AddComponent<Camera>();
            cam.enabled = false;
            cam.stereoTargetEye = StereoTargetEyeMask.None;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.orthographic = true;
            cam.orthographicSize = Mathf.Max(b.size.y, b.size.z * h / w) * 0.5f * 1.02f;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = b.size.x * 2f + 20f;
            cam.transform.position = b.center + Vector3.right * (b.extents.x + 10f);
            cam.transform.rotation = Quaternion.LookRotation(Vector3.left, Vector3.up);
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = null;
            return rt;
        }
        finally
        {
            model.gameObject.SetActive(false);
            Destroy(model.gameObject);
            Destroy(camObject);
        }
    }

    // ---------------------------------------------------------------- Aides de construction

    static void Sliced(Image img)
    {
        img.type = Image.Type.Sliced;
    }

    /// <summary>Contour sombre autour du texte : reste lisible sur l'eau comme sur le sable.</summary>
    static void Outline(TextMeshProUGUI t)
    {
        t.fontStyle = FontStyles.Bold;
        t.outlineWidth = 0.22f;
        t.outlineColor = new Color32(0, 0, 0, 255);
    }

    static RectTransform NewRect(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        return rt;
    }

    static Image NewImage(string name, Transform parent, Sprite sprite, Color color, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        RectTransform rt = NewRect(name, parent, anchor, pos, size);
        Image img = rt.gameObject.AddComponent<Image>();
        img.sprite = sprite;
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    static TextMeshProUGUI NewText(string name, Transform parent, string text, float size, Color color,
                                   Vector2 anchor, Vector2 pos, Vector2 box, TextAlignmentOptions align)
    {
        RectTransform rt = NewRect(name, parent, anchor, pos, box);
        TextMeshProUGUI t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.text = text;
        t.fontSize = size;
        t.color = color;
        t.alignment = align;
        t.raycastTarget = false;
        t.overflowMode = TextOverflowModes.Overflow;
        return t;
    }
}

/// <summary>
/// Formes de l'interface, dessinées dans des textures au premier besoin :
/// la fiche (fond et contour arrondis), le disque, l'anneau, et la bonbonne
/// (silhouette, intérieur, contour, reflet).
/// </summary>
public static class HudSprites
{
    static Sprite cardFill, cardOutline, disc, ring, scanRing, facets, tankBody, tankInner, tankOutline, tankGloss;

    const float CardCut = 30f;      // taille des coins coupés de la fiche
    const float CardLine = 5f;      // épaisseur de son trait

    /// <summary>Rectangle aux coins coupés, anguleux comme les maillages du jeu (étirable).</summary>
    public static Sprite CardFill => cardFill != null ? cardFill : cardFill = MakeSliced(96,
        (x, y) => Alpha(CutBox(x, y, 48f, 48f, 48f, 48f, CardCut)), 44f);

    /// <summary>Le même rectangle, mais seulement son trait de contour (étirable).</summary>
    public static Sprite CardOutline => cardOutline != null ? cardOutline : cardOutline = MakeSliced(96,
        (x, y) =>
        {
            float d = CutBox(x, y, 48f, 48f, 48f, 48f, CardCut);
            return Mathf.Min(Alpha(d), 1f - Alpha(d + CardLine));
        }, 44f);

    /// <summary>Anneau de scan : douze segments droits séparés, plutôt qu'un cercle lisse.</summary>
    public static Sprite ScanRing => scanRing != null ? scanRing : scanRing = Make(192, 192, (x, y) =>
    {
        const int segments = 12;
        float dx = x - 96f, dy = y - 96f;
        float sector = Mathf.PI * 2f / segments;
        float a = Mathf.Atan2(dy, dx);
        float local = Mathf.Repeat(a, sector) - sector * 0.5f;          // angle dans le segment
        float r = new Vector2(dx, dy).magnitude;
        float flat = r * Mathf.Cos(local);                               // distance au bord droit du polygone
        float band = Mathf.Min(Alpha(flat - 90f), 1f - Alpha(flat - 72f));
        float edge = Mathf.Abs(Mathf.Abs(local) - sector * 0.5f) * r;    // distance à la limite du segment
        return Mathf.Min(band, 1f - Alpha(edge - 4f));
    });

    /// <summary>Fond à facettes triangulaires dans les bleus de l'eau.</summary>
    public static Sprite Facets => facets != null ? facets : facets = MakeFacets();

    static Sprite MakeFacets()
    {
        const int w = 96, h = 192, cell = 32;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "HUD facettes" };
        var pixels = new Color32[w * h];
        Color deep = new Color(0.06f, 0.30f, 0.45f), light = new Color(0.20f, 0.55f, 0.70f);

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int cx = x / cell, cy = y / cell;
                float u = (x % cell + 0.5f) / cell, v = (y % cell + 0.5f) / cell;
                // Chaque case est coupée en deux triangles, la diagonale alternant d'une case à l'autre
                bool upper = ((cx + cy) & 1) == 0 ? u + v > 1f : v > u;
                float n = Mathf.Abs(Mathf.Sin((cx * 12.9898f + cy * 78.233f + (upper ? 37.719f : 0f)) * 43758.5453f)) % 1f;
                float t = Mathf.Clamp01(0.15f + 0.6f * n + 0.25f * y / h); // plus clair vers le haut, comme la surface
                Color c = Color.Lerp(deep, light, t);
                // Coins coupés comme le cadre de l'image, pour ne pas dépasser dessous
                c.a = Alpha(CutBox(x + 0.5f, y + 0.5f, w * 0.5f, h * 0.5f, w * 0.5f, h * 0.5f, 17f));
                pixels[y * w + x] = c;
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply(false, false);
        return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
    }

    public static Sprite Disc => disc != null ? disc : disc = Make(48, 48, (x, y) => Circle(x, y, 24f, 24f, 22f));
    public static Sprite Ring => ring != null ? ring : ring = Make(128, 128, (x, y) =>
        Mathf.Min(Circle(x, y, 64f, 64f, 60f), 1f - Circle(x, y, 64f, 64f, 44f)));

    public static Sprite TankBody => tankBody != null ? tankBody : tankBody = Make(96, 220, (x, y) => Alpha(Tank(x, y)));
    public static Sprite TankInner => tankInner != null ? tankInner : tankInner = Make(96, 220, (x, y) => Alpha(Body(x, y) + 9f));
    public static Sprite TankOutline => tankOutline != null ? tankOutline : tankOutline = Make(96, 220, (x, y) =>
        Mathf.Min(Alpha(Tank(x, y)), 1f - Alpha(Tank(x, y) + 4.5f)));
    public static Sprite TankGloss => tankGloss != null ? tankGloss : tankGloss = Make(96, 220, (x, y) =>
        Alpha(RoundBox(x, y, 30f, 92f, 4f, 56f, 4f)));

    // ---- Formes (distance signée : négative à l'intérieur)

    /// <summary>Corps de la bonbonne : un grand cylindre aux bouts arrondis.</summary>
    static float Body(float x, float y)
    {
        return RoundBox(x, y, 48f, 88f, 36f, 84f, 30f);
    }

    /// <summary>Bonbonne complète : corps + col + robinet + molette.</summary>
    static float Tank(float x, float y)
    {
        float d = Body(x, y);
        d = Mathf.Min(d, RoundBox(x, y, 48f, 181f, 10f, 13f, 1f));   // col
        d = Mathf.Min(d, RoundBox(x, y, 48f, 198f, 25f, 8f, 4f));    // robinet
        d = Mathf.Min(d, RoundBox(x, y, 48f, 211f, 9f, 6f, 2f));     // molette
        return d;
    }

    static float RoundBox(float x, float y, float cx, float cy, float hx, float hy, float r)
    {
        float qx = Mathf.Abs(x - cx) - (hx - r);
        float qy = Mathf.Abs(y - cy) - (hy - r);
        float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude;
        return outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
    }

    /// <summary>Rectangle aux quatre coins coupés à 45°.</summary>
    static float CutBox(float x, float y, float cx, float cy, float hx, float hy, float cut)
    {
        float qx = Mathf.Abs(x - cx) - hx;
        float qy = Mathf.Abs(y - cy) - hy;
        float corner = (qx + qy + cut) * 0.70710678f;
        return Mathf.Max(Mathf.Max(qx, qy), corner);
    }

    static float Circle(float x, float y, float cx, float cy, float r)
    {
        return Alpha(new Vector2(x - cx, y - cy).magnitude - r);
    }

    /// <summary>Distance signée -> opacité, avec un pixel de lissage au bord.</summary>
    static float Alpha(float distance)
    {
        return Mathf.Clamp01(0.5f - distance);
    }

    // ---- Textures

    static Sprite MakeSliced(int size, System.Func<float, float, float> alpha, float border)
    {
        Texture2D tex = Draw(size, size, alpha);
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                             SpriteMeshType.FullRect, new Vector4(border, border, border, border));
    }

    static Sprite Make(int w, int h, System.Func<float, float, float> alpha)
    {
        Texture2D tex = Draw(w, h, alpha);
        return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
    }

    static Texture2D Draw(int w, int h, System.Func<float, float, float> alpha)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            name = "HUD"
        };

        var pixels = new Color32[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float a = Mathf.Clamp01(alpha(x + 0.5f, y + 0.5f));
                pixels[y * w + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply(false, false);
        return tex;
    }
}
