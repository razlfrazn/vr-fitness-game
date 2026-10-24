using UnityEngine;

namespace Kickr
{
    /// <summary>Simple chase camera: keeps a fixed offset from the target and looks at it.</summary>
    public class FollowCamera : MonoBehaviour
    {
        public Transform target;
        public Vector3 offset = new Vector3(3f, 3f, -7f);
        public float followSharpness = 8f;

        void LateUpdate()
        {
            if (target == null) return;
            Vector3 desired = target.position + offset;
            float k = 1f - Mathf.Exp(-followSharpness * Time.deltaTime);
            transform.position = Vector3.Lerp(transform.position, desired, k);
            transform.LookAt(target.position + Vector3.up * 0.5f);
        }
    }
}
