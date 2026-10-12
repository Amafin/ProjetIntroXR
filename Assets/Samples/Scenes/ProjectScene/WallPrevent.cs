using Unity.XR.CoreUtils;
using UnityEngine;

public class WallPrevent : MonoBehaviour
{
    [Header("Physique Aquatique")]
    [Tooltip("Accélération de la descente")]
    public float accelerationGravite = -1.5f;

    [Tooltip("Vitesse maximale de descente dans l'eau (plus la valeur est petite, plus c'est lent)")]
    public float vitesseChuteMax = -0.8f; // Descente très douce et constante

    private CharacterController _characterController;
    private Transform _headTransform;
    private float _vitesseVerticale = 0f;

    void Awake()
    {
        _characterController = GetComponent<CharacterController>();
        var xrOrigin = GetComponent<XROrigin>();
        if (xrOrigin != null && xrOrigin.Camera != null)
        {
            _headTransform = xrOrigin.Camera.transform;
        }
    }

    void Update()
    {
        if (_headTransform == null || _characterController == null) return;

        // 1. Recentre la capsule sur la caméra pour bloquer les murs
        _characterController.center = new Vector3(
            _headTransform.localPosition.x,
            _characterController.height / 2f,
            _headTransform.localPosition.z
        );

        // 2. Gestion de la gravité avec résistance de l'eau
        if (_characterController.isGrounded)
        {
            // Petit contact permanent avec le sol pour ne pas flotter à 2 cm
            _vitesseVerticale = -0.2f;
        }
        else
        {
            // Accélération progressive
            _vitesseVerticale += accelerationGravite * Time.deltaTime;

            // Frein de l'eau : on bloque la vitesse à la valeur max autorisée
            if (_vitesseVerticale < vitesseChuteMax)
            {
                _vitesseVerticale = vitesseChuteMax;
            }
        }

        // 3. Application du mouvement vertical
        _characterController.Move(new Vector3(0, _vitesseVerticale * Time.deltaTime, 0));
    }
}
