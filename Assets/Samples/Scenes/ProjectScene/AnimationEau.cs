using UnityEngine;

public class AnimationEau : MonoBehaviour
{
    [Header("Vitesse globale (très lente)")]
    public float vitesseDerive = 0.001f;

    [Header("Oscillation des vagues")]
    public float frequenceVague = 0.5f;
    public float amplitudeVague = 0.02f;

    private Renderer rend;

    void Start()
    {
        rend = GetComponent<Renderer>();
    }

    void Update()
    {
        if (rend != null)
        {
            // Dérive lente continue
            float temps = Time.time;
            float baseOffset = temps * vitesseDerive;

            // Mouvement ondulant organique non rectiligne
            float offsetX = baseOffset + Mathf.Sin(temps * frequenceVague) * amplitudeVague;
            float offsetY = baseOffset + Mathf.Cos(temps * frequenceVague * 0.7f) * amplitudeVague;

            Vector2 offset = new Vector2(offsetX, offsetY);

            rend.material.SetTextureOffset("_BaseMap", offset);
            rend.material.SetTextureOffset("_BumpMap", offset);
        }
    }
}
