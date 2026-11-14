using Unity.Netcode;
using UnityEngine;

// Cámara detrás del jugador local. Clic derecho + mouse para girar, rueda para zoom.
public class CameraFollow : MonoBehaviour
{
    public Transform target;
    [SerializeField] float distance = 9f;
    [SerializeField] float height = 2f;
    [SerializeField] float mouseSensitivity = 3f;

    float yaw;
    float pitch = 20f;

    void LateUpdate()
    {
        if (target == null)
        {
            // Cada escena trae su propia cámara: busca al jugador de esta PC.
            var nm = NetworkManager.Singleton;
            if (nm != null && nm.IsClient && nm.LocalClient != null && nm.LocalClient.PlayerObject != null)
            {
                target = nm.LocalClient.PlayerObject.transform;
                yaw = target.eulerAngles.y;
            }
            if (target == null) return;
        }

        if (Input.GetMouseButton(1))
        {
            yaw += Input.GetAxis("Mouse X") * mouseSensitivity;
            pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * mouseSensitivity, -10f, 60f);
        }
        distance = Mathf.Clamp(distance - Input.mouseScrollDelta.y, 3f, 20f);

        var rot = Quaternion.Euler(pitch, yaw, 0f);
        var normalPos = target.position + Vector3.up * height - rot * Vector3.forward * distance;
        var normalLook = target.position + Vector3.up * 1.5f;
        // Toma cinematográfica (muerte del jefe): se acerca al punto y gira despacio a su alrededor; luego vuelve.
        float f = Time.unscaledTime < focusUntil ? Mathf.Clamp01((Time.unscaledTime - focusStart) / 0.6f)
                : Mathf.Clamp01(1f - (Time.unscaledTime - focusUntil) / 0.8f);
        if (f > 0f)
        {
            float orbit = (Time.unscaledTime - focusStart) * 9f;
            var flat = Vector3.ProjectOnPlane(normalPos - focusPoint, Vector3.up);
            if (flat.sqrMagnitude < 0.01f) flat = Vector3.back;
            var dir = Quaternion.Euler(0f, orbit, 0f) * flat.normalized;
            var camPos = focusPoint + dir * focusDistance + Vector3.up * (focusHeight * 0.5f + 1.6f);   // por encima de rocas y plantas
            float s = f * f * (3f - 2f * f);
            transform.position = Vector3.Lerp(normalPos, camPos, s);
            transform.LookAt(Vector3.Lerp(normalLook, focusPoint + Vector3.up * focusHeight * 0.4f, s));
        }
        else
        {
            transform.position = normalPos;
            transform.LookAt(normalLook);
        }

        // Sacudida (golpes de área, muerte del jefe): se va apagando sola.
        if (Time.time < shakeUntil)
        {
            float k = (shakeUntil - Time.time) / shakeDuration;
            transform.position += Random.insideUnitSphere * shakeStrength * k;
        }
    }

    static float shakeUntil, shakeDuration = 1f, shakeStrength;
    static Vector3 focusPoint;
    static float focusStart = -100f, focusUntil = -100f, focusDistance = 9f, focusHeight = 4f;

    // Enfoca un punto unos segundos (tiempo real, funciona en cámara lenta). height = alto del jefe.
    public static void Focus(Vector3 point, float seconds, float dist, float height)
    {
        focusPoint = point;
        focusStart = Time.unscaledTime;
        focusUntil = Time.unscaledTime + seconds;
        focusDistance = dist;
        focusHeight = height;
    }

    public static void Shake(float strength, float duration)
    {
        shakeStrength = strength;
        shakeDuration = duration;
        shakeUntil = Time.time + duration;
    }
}
