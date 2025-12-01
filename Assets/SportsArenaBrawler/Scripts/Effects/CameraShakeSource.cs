using Cinemachine;
using UnityEngine;

public class CameraShakeSource : MonoBehaviour
{
    [SerializeField] private float _strength = 1f;
    
    [SerializeField] private CinemachineImpulseSource _impulseSource;

    public void Shake()
    {
        Vector3 velocity = Random.insideUnitCircle.normalized * _strength;
        _impulseSource.GenerateImpulse(velocity);
    }
}
