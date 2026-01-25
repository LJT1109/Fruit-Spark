using UnityEngine;

public class IKPhysicsFollow : MonoBehaviour
{
    public Transform dataTarget; // MediaPipe 數據點
    public float followForce = 50f;
    private Rigidbody rb;

    void Start() => rb = GetComponent<Rigidbody>();

    void FixedUpdate()
    {
        // 使用物理速度去追蹤數據點，而不是直接設置 Position
        // 這樣遇到頭部的 Collider 時，物理引擎會自動把它推開
        Vector3 direction = dataTarget.position - transform.position;
        rb.linearVelocity = direction * followForce;
    }
}