using UnityEngine;
using System.Collections;
namespace Artngame.Oceanis.Utils
{
    public class OceanisFollowObject : MonoBehaviour
    {
        public Transform ObjectToFollow;
        public float Dumper = 0.25f;
        public float RotSpeed = 6f;
        public Vector3 Displace = new Vector3(0f, 15f, 0f);

        public bool EnableFollow = true;

        private Transform localTransform;
        private Vector3 velocity = Vector3.zero;

        void Start()
        {
            localTransform = this.transform;
        }

        void Update()
        {
            if (EnableFollow && ObjectToFollow != null)
            {
                Vector3 target = ObjectToFollow.TransformPoint(Displace);
                localTransform.position = Vector3.SmoothDamp(localTransform.position, target, ref velocity, Dumper);
                if (RotSpeed > 0)
                {
                    Quaternion targetRot = Quaternion.LookRotation(ObjectToFollow.position - localTransform.position);
                    transform.rotation = Quaternion.Slerp(localTransform.rotation, targetRot, RotSpeed * Time.deltaTime);
                }
            }
        }
    }
}