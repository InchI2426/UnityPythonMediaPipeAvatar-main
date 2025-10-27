using UnityEngine;

public class HandCursor : MonoBehaviour
{
    public PipeServer pipeServer;   // ลิงก์ไปยัง PipeServer
    public bool useRightHand = true; // true = มือขวา, false = มือซ้าย
    public float smoothSpeed = 10f;  // ความลื่นของการเคลื่อนไหว

    void Update()
    {
        if (pipeServer == null)
        {
            Debug.LogWarning("PipeServer not assigned!");
            return;
        }

        // ใช้ landmark จาก MediaPipe (มือ)
        // Landmark.RIGHT_INDEX หรือ LEFT_INDEX (นิ้วชี้)
        Transform target = pipeServer.GetLandmark(
            useRightHand ? Landmark.RIGHT_INDEX : Landmark.LEFT_INDEX
        );

        if (target != null)
        {
            // ขยับตำแหน่งของ HandCursor ให้ตามตำแหน่งมือแบบลื่นๆ
            transform.position = Vector3.Lerp(
                transform.position,
                target.position,
                Time.deltaTime * smoothSpeed
            );
        }
    }

    // ฟังก์ชันเมื่อมือแตะวัตถุที่มี Collider
    private void OnTriggerEnter(Collider other)
    {
        Debug.Log("Touched: " + other.name);

        // ถ้าวัตถุนั้นมี MeshRenderer ให้เปลี่ยนสี
        var renderer = other.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            renderer.material.color = Color.Lerp(renderer.material.color, Color.yellow, 0.5f);
        }
    }
}
