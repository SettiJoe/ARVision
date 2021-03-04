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
        if (Input.GetMouseButtonDown(0))
        {
            surface.BuildNavMesh();
            SetPlayerDestination();
        }
    }

    private void SetPlayerDestination()
    {
        var gotHit = Physics.Raycast(mainCamera.transform.position, Vector3.down, out var cameraBaseHit);
        if (!gotHit)
            return;

        agent.ResetPath();
        agent.Warp(cameraBaseHit.point);

        var ray = mainCamera.ScreenPointToRay(Input.mousePosition);

        if (Physics.Raycast(ray, out var hit))
        {
            agent.SetDestination(hit.point);
        }
    }
}
