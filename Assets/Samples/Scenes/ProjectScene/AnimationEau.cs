using UnityEngine;

public class AnimationEau : MonoBehaviour
{
    public float vitesseX = 0.02f;
    public float vitesseY = 0.03f;
    private Renderer rendu;

    void Start()
    {
        rendu = GetComponent<Renderer>();
    }

    void Update()
    {
        // On calcule le décalage progressif dans le temps
        float decalageX = Time.time * vitesseX;
        float decalageY = Time.time * vitesseY;

        // On applique ce décalage à la texture de vagues (Normal Map)
        if (rendu != null && rendu.material != null)
        {
            rendu.material.SetTextureOffset("_BumpMap", new Vector2(decalageX, decalageY));
        }
    }
}
