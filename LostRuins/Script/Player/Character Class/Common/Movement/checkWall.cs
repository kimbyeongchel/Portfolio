using UnityEngine;

public class checkWall : MonoBehaviour
{
    public Vector3 [] ray = new Vector3 [2];
    float scope = 1f;

    public bool CheckHitWall(Vector3 moveDir)
    {
        moveDir = moveDir.normalized;

        foreach (Vector3 pos in ray)
        {
            Vector3 worldPos = transform.TransformPoint(pos);
            if (Physics.Raycast(worldPos, moveDir, out RaycastHit hit, scope))
            {
                if (hit.collider.CompareTag("Wall"))
                    return true;
            }
        }
        return false;
    }
}