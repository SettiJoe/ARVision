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
            Debug.Log($"TryPlayObstacleWarnings {activeObstacles.Count}");
            foreach (var activeObstacle in activeObstacles)
            {
                activeObstacle.remainingWarningTime -= Time.deltaTime;
                Debug.Log($"activeObstacle: {activeObstacle.obstacleConfig.name}");

                if (activeObstacle.currentWarningConfig == null || activeObstacle.assignedAudioSource == null)
                    continue;

                Debug.Log($"activeObstacle properly setup {activeObstacle.obstacleConfig.name}");

                if (activeObstacle.remainingWarningTime < 0 && activeObstacle.assignedAudioSource.isPlaying == false)
                //if (activeObstacle.remainingWarningTime < 0)
                {
                    Debug.Log($"Trying  {activeObstacle.obstacleConfig.name}");
                    activeObstacle.remainingWarningTime = activeObstacle.currentWarningConfig.audioCueFrequency;
                    activeObstacle.assignedAudioSource.Play();
                }
            }
        }

        private void AssignAudioSources()
        {
            var maxAudioSources = Math.Min(simultaneousAudioWarnings, activeObstacles.Count);
            Debug.Log($"AssignAudioSources {activeObstacles.Count} | maxAudioSources:  {maxAudioSources} | simultaneousAudioWarnings: {simultaneousAudioWarnings}");

            for (int i = 0; i < maxAudioSources; i++)
            {
                var activeObstacle = activeObstacles[i];

                if (activeObstacle.currentWarningConfig == null)
                    continue;

                Debug.Log($"Obstacle Audio assign: {activeObstacle.obstacle.name} | assignedAudioSource: {activeObstacle.assignedAudioSource}");

                if (activeObstacle.assignedAudioSource == null)
                {
                    Debug.Log($"Selecting audioSource, poolsize: {audioSourcePool.Count}");
                    var audioSource = audioSourcePool[0];
                    Debug.Log($"Selected audioSource: {audioSource}");

                    if (audioSource.isPlaying)
                        break;

                    activeObstacle.assignedAudioSource = audioSource;
                    audioSource.clip = activeObstacle.currentWarningConfig.audioCue;

                    audioSourcePool.RemoveAt(0);
                    Debug.Log($"Removed audioSource from pool: {audioSource}");
                }

                Debug.Log($"Reposition audioSource, assigned: {activeObstacle.assignedAudioSource}");
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

            audioSourcePool.Sort((audioSource1, audioSource2) =>
            {
                if (audioSource1.isPlaying)
                {
                    if (audioSource2.isPlaying)
                        return 0;

                    return 1;
                }

                if (audioSource2.isPlaying)
                    return -1;

                return 0;
            });
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
            var deletedObstacles = new List<ActiveObstacleWarningInfo>(activeObstacles.Count);

            foreach (var activeObstacle in activeObstacles)
            {
                if (activeObstacle.obstacle == null)
                {
                    deletedObstacles.Add(activeObstacle);

                    if (activeObstacle.assignedAudioSource != null)
                        audioSourcePool.Add(activeObstacle.assignedAudioSource);

                    continue;
                }

                var closestPoint = activeObstacle.obstacle.ClosestPoint(position);
                var distance = (position - closestPoint).magnitude;

                activeObstacle.closesPoint = closestPoint;
                SetPriority(activeObstacle, distance);
            }

            activeObstacles.RemoveAll(entry => deletedObstacles.Contains(entry));

            activeObstacles.Sort((obstacle1, obstacle2) =>
            {
                if (obstacle1.currentWarningConfig == null)
                {
                    if (obstacle2.currentWarningConfig == null)
                        return 0;

                    return 1;
                }

                if (obstacle2.currentWarningConfig == null)
                    return -1;

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
            Debug.Log($"AddObstacle | {other.name} | LayerMask {obstacleConfig.layerMask}");
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
            Debug.Log($"TryRemoveObstacle");
            var obstacle = activeObstacles.Find(obstacleConfig => obstacleConfig.obstacle == other);

            if (obstacle != null)
            {
                Debug.Log($"Removed obstacle");
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
            Debug.LogWarning("OnTriggerEnter");
            if (GetObstacleConfig(other.gameObject, out var obstacleConfig))
            {
                AddObstacle(other, obstacleConfig);
            }
        }

        private void OnTriggerExit(Collider other)
        {
            Debug.LogWarning("OnTriggerExit");
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