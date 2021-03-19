using System;
using System.Collections.Generic;
using UnityEngine;

namespace Navigator
{
    public class ObstaclesRadar : MonoBehaviour
    {
        [SerializeField]
        private List<ObstacleConfig> obstacleConfigs;

        [SerializeField]
        private int simultaneousAudioWarnings;
        [SerializeField]
        public AudioSource audioWarningPrefab;

        private List<AudioSource> audioSourcePool;
        private List<ActiveObstacleWarningInfo> activeObstacles;

        private void TryPlayObstacleWarnings()
        {
            foreach (var activeObstacle in activeObstacles)
            {
                activeObstacle.remainingWarningTime -= Time.deltaTime;

                if (activeObstacle.currentWarningConfig == null || activeObstacle.assignedAudioSource == null)
                    continue;

                if (activeObstacle.remainingWarningTime < 0 && activeObstacle.assignedAudioSource.isPlaying == false)
                {
                    activeObstacle.remainingWarningTime = activeObstacle.currentWarningConfig.audioCueFrequency;
                    activeObstacle.assignedAudioSource.Play();
                }
            }
        }

        private void AssignAudioSources()
        {
            var maxAudioSources = Math.Min(simultaneousAudioWarnings, activeObstacles.Count);

            for (int i = 0; i < maxAudioSources; i++)
            {
                var activeObstacle = activeObstacles[i];

                if (activeObstacle.assignedAudioSource == null)
                {
                    var audioSource = audioSourcePool[0];
                    activeObstacle.assignedAudioSource = audioSource;
                    audioSource.clip = activeObstacle.currentWarningConfig.audioCue;

                    audioSourcePool.RemoveAt(0);
                }

                activeObstacle.assignedAudioSource.transform.position = activeObstacle.closesPoint;
            }
        }

        private void FreeDeprioritizedAudioSources()
        {
            for (int i = simultaneousAudioWarnings; i < activeObstacles.Count; i++)
            {
                var activeObstacle = activeObstacles[i];
                if (activeObstacle.assignedAudioSource != null)
                {
                    audioSourcePool.Add(activeObstacle.assignedAudioSource);
                }
            }
        }

        private void ReassignAudioSources()
        {
            FreeDeprioritizedAudioSources();
            AssignAudioSources();
            TryPlayObstacleWarnings();
        }

        private void RecalculatePriorities()
        {
            var position = transform.position;

            foreach (var activeObstacle in activeObstacles)
            {
                var closestPoint = activeObstacle.obstacle.ClosestPoint(position);
                var distance = (position - closestPoint).magnitude;

                activeObstacle.closesPoint = closestPoint;
                SetPriority(activeObstacle, distance);
            }

            activeObstacles.Sort((obstacle1, obstacle2) =>
            {
                if (obstacle1.currentWarningConfig == null)
                {
                    if (obstacle2.currentWarningConfig == null)
                        return 0;

                    return -1;
                }

                if (obstacle2.currentWarningConfig == null)
                    return 1;

                return obstacle1.currentWarningConfig.priority.CompareTo(obstacle2.currentWarningConfig.priority);
            });
        }

        private void SetPriority(ActiveObstacleWarningInfo activeObstacle, float distance)
        {
            foreach (var warningRange in activeObstacle.obstacleConfig.warningRanges)
            {
                if (warningRange.range > distance)
                {
                    activeObstacle.currentWarningConfig = warningRange;
                    return;
                }
            }

            activeObstacle.currentWarningConfig = null;
        }

        private void Update()
        {
            Debug.Log($"Update {activeObstacles.Count}");
            RecalculatePriorities();
            ReassignAudioSources();
            TryPlayObstacleWarnings();
        }

        private void AddObstacle(Collider other, ObstacleConfig obstacleConfig)
        {
            Debug.Log("AddObstacle");
            var activeObstacle = new ActiveObstacleWarningInfo
            {
                obstacle = other,
                remainingWarningTime = 0,
                obstacleConfig = obstacleConfig
            };

            activeObstacles.Add(activeObstacle);
        }

        private void TryRemoveObstacle(Collider other)
        {
            var obstacle = activeObstacles.Find(obstacleConfig => obstacleConfig.obstacle == other);

            if (obstacle != null)
            {
                activeObstacles.Remove(obstacle);

                if (obstacle.assignedAudioSource != null)
                    audioSourcePool.Add(obstacle.assignedAudioSource);
            }
        }

        private bool GetObstacleConfig(GameObject other, out ObstacleConfig config)
        {
            foreach (var obstacleConfig in obstacleConfigs)
            {
                if ((obstacleConfig.layerMask.value & (1 << other.layer)) > 0)
                {
                    config = obstacleConfig;
                    return true;
                }
            }

            config = null;
            return false;
        }

        private void OnTriggerEnter(Collider other)
        {
            Debug.Log($"OnTriggerEnter {other.name}");

            if (GetObstacleConfig(other.gameObject, out var obstacleConfig))
            {
                AddObstacle(other, obstacleConfig);
            }
        }

        private void OnTriggerExit(Collider other)
        {
            TryRemoveObstacle(other);
        }

        private void InitializeAudioSources()
        {
            audioSourcePool = new List<AudioSource>(simultaneousAudioWarnings);

            for (int i = 0; i < simultaneousAudioWarnings; i++)
            {
                var audioSource = Instantiate(audioWarningPrefab, transform);
                audioSourcePool.Add(audioSource);
            }
        }

        private void Awake()
        {
            InitializeAudioSources();
            activeObstacles = new List<ActiveObstacleWarningInfo>();
        }
    }

    public class ActiveObstacleWarningInfo
    {
        public ObstacleConfig obstacleConfig;
        public WarningConfig currentWarningConfig;
        public Vector3 closesPoint;

        public float remainingWarningTime;
        public Collider obstacle;
        public AudioSource assignedAudioSource;
    }
}