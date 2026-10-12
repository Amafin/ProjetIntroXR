using Unity.XR.CoreUtils;
using UnityEngine;

public class WallPrevent : MonoBehaviour
{
    private CharacterController _characterController;
    private Transform _headTransform;

    void Awake()
    {
        _characterController = GetComponent<CharacterController>();
        var xrOrigin = GetComponentInParent<XROrigin>();
        if (xrOrigin != null && xrOrigin.Camera != null)
        {
            _headTransform = xrOrigin.Camera.transform;
        }
    }

    void Update()
    {
        if (_headTransform == null || _characterController == null) return;

        // Met à jour la position de la capsule physique sur la tête du joueur
        _characterController.center = new Vector3(
            _headTransform.localPosition.x,
            _characterController.height / 2f,
            _headTransform.localPosition.z
        );

        // Force Unity à recalculer les collisions avec les murs et le sol chaque frame
        _characterController.SimpleMove(Vector3.zero);
    }
}
