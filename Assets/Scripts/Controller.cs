using UnityEngine;
using UnityEngine.AI;

public class Controller : MonoBehaviour
{
    public NavMeshSurface surface;
    public NavMeshAgent agent;

    public Camera mainCamera;

    private void Start()
    {
        mainCamera = Camera.main;
    }

    private void Update()
    {
        
        foreach (Touch touch in Input.touches)

        {
            Debug.Log("Touches");
            if (touch.phase == TouchPhase.Began)
            {
                surface.BuildNavMesh();
                SetPlayerDestination();
                return;
            }
        }
    }

    private void SetPlayerDestination()
    {
        Debug.Log("SetPlayerDestination");

        var gotHit = Physics.Raycast(mainCamera.transform.position,
            Vector3.down,
            out var cameraBaseHit,
            5,
            LayerMask.GetMask("Floor"));
        if (!gotHit)
            return;
        Debug.Log("Floor Below Player");

        agent.ResetPath();
        agent.Warp(cameraBaseHit.point);

        var ray = mainCamera.ScreenPointToRay(Input.mousePosition);

        if (Physics.Raycast(ray, out var hit))
        {
            agent.SetDestination(hit.point);
        }
    }
}
