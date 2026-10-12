using UnityEngine;

public class AnimationEau : MonoBehaviour
{
    public float vitesseX = 0.05f;
    public float vitesseY = 0.05f;
    private Renderer rend;

    void Start()
    {
        rend = GetComponent<Renderer>();
    }

    void Update()
    {
        if (rend != null)
        {
            Vector2 offset = new Vector2(Time.time * vitesseX, Time.time * vitesseY);
            rend.material.SetTextureOffset("_BaseMap", offset);
            rend.material.SetTextureOffset("_BumpMap", offset);
        }
    }
}
