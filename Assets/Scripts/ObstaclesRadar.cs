using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Navigator
{
    public class ObstaclesRadar : MonoBehaviour
    {
        private static CancellationTokenSource cancellationTokenSource;

        [SerializeField]
        private bool logVerbose = true;

        [SerializeField]
        private List<ObstacleConfig> obstacleConfigs;

        [SerializeField]
        private int simultaneousAudioWarnings;
        [SerializeField]
        private int warningPriorityRefreshRateMilliseconds = 500;
        [SerializeField]
        public AudioSource audioWarningPrefab;

        private List<AudioSource> audioSourcePool;
        private Dictionary<AudioSource, bool> isAudioSourceAssigned;

        private List<ActiveWarningObstacle> activeObstacles;
        private List<ActiveWarningCluster> activeClusters;
        private List<ActiveWarningCluster> clustersPool;

        private float clusterThreshold = 1.5f;
        private int assignedAudioSources;

        private void TryPlayObstacleWarnings()
        {
            Log($"TryPlayObstacleWarnings {activeClusters.Count}", false);

            foreach (var activeCluster in activeClusters)
            {
                activeCluster.remainingWarningTime -= Time.deltaTime;
                Log($"{activeCluster}: reduced remainingTime: {activeCluster.remainingWarningTime}");

                if (activeCluster.warningConfig == null || activeCluster.assignedAudioSource == null)
                    continue;

                Log($"{activeCluster}: audio properly setup");

                if (activeCluster.remainingWarningTime < 0 && activeCluster.assignedAudioSource.isPlaying == false)
                {
                    Log($"{activeCluster}: Trying play sound");

                    var audioSource = activeCluster.assignedAudioSource;
                    var warningConfig = activeCluster.warningConfig;
                    var animationCurve = warningConfig.animationCurve;

                    activeCluster.remainingWarningTime = warningConfig.audioCueFrequency;

                    audioSource.clip = warningConfig.audioCue;
                    audioSource.transform.position = activeCluster.closestPoint;

                    audioSource.minDistance = animationCurve.keys[0].time;
                    audioSource.maxDistance = animationCurve.keys[animationCurve.length - 1].time;
                    audioSource.rolloffMode = AudioRolloffMode.Custom;
                    audioSource.SetCustomCurve(AudioSourceCurveType.CustomRolloff, animationCurve);

                    audioSource.Play();
                }
            }
        }

        private async void RefreshWarnings()
        {
            Log($"RefreshWarnings activeObstacles.Count: {activeObstacles.Count}", false);

            DeassignAudioSources();
            CreateClusters();
            RecalculatePriorities();

            ReprioritizeAudioSources();
            AssignAudioSources();

            await Task.Delay(warningPriorityRefreshRateMilliseconds, cancellationTokenSource.Token);

            if (cancellationTokenSource.IsCancellationRequested)
                return;

            RefreshWarnings();
        }

        private void DeassignAudioSources()
        {
            assignedAudioSources = 0;

            foreach (var key in isAudioSourceAssigned.Keys.ToList())
                isAudioSourceAssigned[key] = false;
        }

        private void ReprioritizeAudioSources()
        {
            audioSourcePool.Sort((audioSource1, audioSource2) =>
            {
                if (audioSource1.isPlaying)
                    return audioSource2.isPlaying ? 0 : 1;

                if (audioSource2.isPlaying)
                    return -1;

                return 0;
            });
        }

        private void AssignAudioSources()
        {
            var maxAudioSources = Math.Min(simultaneousAudioWarnings, activeClusters.Count);

            Log($"AssignAudioSources | active Clusters: {activeClusters.Count} | Assigned Audio Sources: {assignedAudioSources}", false);

            for (int i = 0; i < maxAudioSources; i++)
            {
                var activeCluster = activeClusters[i];

                if (activeCluster.warningConfig == null)
                    continue;

                Log("AssignAudioSources | Got WarningConfig");

                if (activeCluster.assignedAudioSource != null)
                    continue;

                Log("AssignAudioSources | Got no assigned audio source");

                var audioSource = GetAvailableAudioSource();
                if (audioSource == null)
                    return;

                activeCluster.assignedAudioSource = audioSource;

                Log($"{activeCluster}: Assigned audio source: {activeCluster.assignedAudioSource}");
            }
        }

        private AudioSource GetAvailableAudioSource()
        {
            for (int i = 0; i < audioSourcePool.Count; i++)
            {
                var audioSource = audioSourcePool[i];

                if (isAudioSourceAssigned[audioSource] || audioSource.isPlaying)
                    continue;

                isAudioSourceAssigned[audioSource] = true;
                assignedAudioSources++;
                return audioSource;
            }

            return null;
        }

        private void RecalculatePriorities()
        {
            activeClusters.Sort((cluster1, cluster2) =>
            {
                if (cluster1.warningConfig == null)
                {
                    return cluster2.warningConfig == null ? 0 : 1;
                }

                if (cluster2.warningConfig == null)
                    return -1;

                return cluster1.warningConfig.priority.CompareTo(cluster2.warningConfig.priority);
            });
        }

        private void CreateClusters()
        {
            var remainingObstacles = activeObstacles.ToList(); // Make a copy
            RecalculateClosestPoints(remainingObstacles);

            var newClusters = new List<ActiveWarningCluster>(activeClusters.Count);

            while (remainingObstacles.Count > 0)
            {
                newClusters.Add(CreateCluster(remainingObstacles));
            }

            foreach (var cluster in newClusters)
            {
                var equalCluster = activeClusters.Find(entry => entry.mainObstacle == cluster.mainObstacle || cluster.childObstacles.Contains(entry.mainObstacle));

                if (equalCluster == null) continue;

                Log($"{cluster} Found equal cluster");

                if (equalCluster.assignedAudioSource != null && equalCluster.assignedAudioSource.isPlaying)
                {
                    cluster.assignedAudioSource = equalCluster.assignedAudioSource;
                    isAudioSourceAssigned[cluster.assignedAudioSource] = true;
                    assignedAudioSources++;
                    Log($"{cluster} Reused audioSource");
                }

                cluster.remainingWarningTime = equalCluster.remainingWarningTime;
            }

            clustersPool.AddRange(activeClusters);

            activeClusters = newClusters;
        }

        private ActiveWarningCluster CreateCluster(List<ActiveWarningObstacle> remainingObstacles)
        {
            var targetObstacle = remainingObstacles[0];
            Log($"Trying to cluster {targetObstacle.Name}", false);

            var cluster = CreateBaseCluster(targetObstacle);
            var clusteredIndexes = new List<int>();

            for (var i = 1; i < remainingObstacles.Count; i++) // Skips the first entry
            {
                var currentObstacle = remainingObstacles[i];

                if (currentObstacle.obstacleConfig != targetObstacle.obstacleConfig)
                    continue;

                Log($"{currentObstacle.Name} has same obstacle config");

                if (currentObstacle.distanceToPlayer - targetObstacle.distanceToPlayer > clusterThreshold)
                    break;

                Log($"{currentObstacle.Name} is in the broad distance threshold");

                var distanceToEachOther = (currentObstacle.closestPoint - targetObstacle.closestPoint).magnitude;

                if (distanceToEachOther < clusterThreshold)
                {
                    Log($"{currentObstacle.Name} is in the broad distance threshold");
                    clusteredIndexes.Add(i);
                    cluster.childObstacles.Add(currentObstacle);
                    currentObstacle.parentCluster = cluster;
                }
            }

            for (var i = clusteredIndexes.Count - 1; i >= 0; i--)
                remainingObstacles.RemoveAt(i);
            remainingObstacles.RemoveAt(0);

            return cluster;
        }

        private ActiveWarningCluster CreateBaseCluster(ActiveWarningObstacle targetObstacle)
        {
            var cluster = GetClusterFromPool();

            cluster.mainObstacle = targetObstacle;
            cluster.closestPoint = targetObstacle.closestPoint;
            cluster.warningConfig = GetWarningConfig(targetObstacle);
            cluster.obstacleConfig = targetObstacle.obstacleConfig;
            cluster.childObstacles.Add(targetObstacle);
            cluster.remainingWarningTime = cluster.warningConfig?.audioCueFrequency * 0.5f ?? 0;

            return cluster;
        }

        private ActiveWarningCluster GetClusterFromPool()
        {
            if (clustersPool.Count == 0)
            {
                Debug.LogWarning("Created new cluster");
                return new ActiveWarningCluster()
                {
                    childObstacles = new List<ActiveWarningObstacle>()
                };
            }


            var cluster = clustersPool[0];
            clustersPool.RemoveAt(0);

            cluster.childObstacles.Clear();
            cluster.assignedAudioSource = null;

            return cluster;
        }

        private WarningConfig GetWarningConfig(ActiveWarningObstacle activeWarningObstacle)
        {
            foreach (var warningRange in activeWarningObstacle.obstacleConfig.warningRanges)
            {
                Log($"{activeWarningObstacle.obstacleConfig.name}: {activeWarningObstacle.distanceToPlayer} <> {warningRange.range}");
                if (warningRange.range > activeWarningObstacle.distanceToPlayer)
                {
                    Log($"{activeWarningObstacle.obstacleConfig.name}: Set warning range: {warningRange.audioCue}, range: {warningRange.range}", false);
                    return warningRange;
                }
            }

            return null;
        }

        private void RecalculateClosestPoints(List<ActiveWarningObstacle> remainingObstacles)
        {
            var position = transform.position;
            var gotFeetPosition = GetFeetPosition(position, out var feetPosition);

            foreach (var activeWarningObstacle in remainingObstacles)
            {
                var closestPoint = activeWarningObstacle.obstacle.ClosestPoint(position);
                activeWarningObstacle.closestPoint = closestPoint;
                activeWarningObstacle.distanceToPlayer = (position - closestPoint).magnitude;

                if (!gotFeetPosition)
                    continue;

                var closestFeetPoint = activeWarningObstacle.obstacle.ClosestPoint(feetPosition);
                var distance = (feetPosition - closestFeetPoint).magnitude;

                if (!(distance < activeWarningObstacle.distanceToPlayer))
                    continue;

                activeWarningObstacle.closestPoint = closestFeetPoint;
                activeWarningObstacle.distanceToPlayer = distance;
            }

            remainingObstacles.Sort((obstacle1, obstacle2) => obstacle1.distanceToPlayer.CompareTo(obstacle2.distanceToPlayer));
        }

        private bool GetFeetPosition(Vector3 position, out Vector3 feetPosition)
        {
            feetPosition = Vector3.zero;
            var gotHit = Physics.Raycast(position, Vector3.down, out var cameraBaseHit, 50, LayerMask.GetMask("Floor"));

            if (!gotHit)
                return false;

            feetPosition = cameraBaseHit.point;
            return true;
        }

        private void Update()
        {
            Log($"Update {activeClusters.Count}");

            AssignAudioSources();
            TryPlayObstacleWarnings();
        }

        private void AddObstacle(Collider other, ObstacleConfig obstacleConfig)
        {
            Log($"AddObstacle | {other.name} | ObstacleConfig {obstacleConfig.name}", false);

            var activeObstacle = new ActiveWarningObstacle
            {
                obstacle = other,
                obstacleConfig = obstacleConfig
            };

            activeObstacles.Add(activeObstacle);
        }

        private void TryRemoveObstacle(Collider other)
        {
            var obstacle = activeObstacles.Find(obstacleConfig => obstacleConfig.obstacle == other);

            if (obstacle == null) return;

            Log($"TryRemoveObstacle | {obstacle.obstacleConfig.name}", false);
            activeObstacles.Remove(obstacle);
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
            Log("OnTriggerEnter");
            if (GetObstacleConfig(other.gameObject, out var obstacleConfig))
            {
                AddObstacle(other, obstacleConfig);
            }
        }

        private void OnTriggerExit(Collider other)
        {
            Log("OnTriggerExit");
            TryRemoveObstacle(other);
        }

        private void InitializeAudioSources()
        {
            audioSourcePool = new List<AudioSource>(simultaneousAudioWarnings);
            isAudioSourceAssigned = new Dictionary<AudioSource, bool>(simultaneousAudioWarnings);

            for (int i = 0; i < simultaneousAudioWarnings; i++)
            {
                var audioSource = Instantiate(audioWarningPrefab, transform);
                audioSourcePool.Add(audioSource);
                isAudioSourceAssigned[audioSource] = false;
            }
        }

        private void Awake()
        {
            InitializeAudioSources();
            activeObstacles = new List<ActiveWarningObstacle>();
            activeClusters = new List<ActiveWarningCluster>();

            clustersPool = new List<ActiveWarningCluster>();

            cancellationTokenSource = new CancellationTokenSource();
            RefreshWarnings();

#if UNITY_EDITOR
            EditorApplication.playModeStateChanged += PlayModeStateChanged;
#endif
        }

        private void Log(string message, bool isVerbose = true)
        {
            if (!logVerbose && isVerbose)
                return;

            Debug.Log($"{Time.time} || {message}");
        }

#if UNITY_EDITOR
        private void PlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode)
            {
                EditorApplication.playModeStateChanged -= PlayModeStateChanged;
                cancellationTokenSource.Cancel();
                cancellationTokenSource = null;
            }
        }
#endif
    }

    public class ActiveWarningObstacle
    {
        public ObstacleConfig obstacleConfig;
        public Vector3 closestPoint;
        public float distanceToPlayer;
        public Collider obstacle;

        public string Name => obstacleConfig.name;

        public ActiveWarningCluster parentCluster;
    }

    public class ActiveWarningCluster
    {
        public float remainingWarningTime;
        public AudioSource assignedAudioSource;
        public Vector3 closestPoint;
        public ActiveWarningObstacle mainObstacle;

        public List<ActiveWarningObstacle> childObstacles;
        public ObstacleConfig obstacleConfig;
        public WarningConfig warningConfig;

        public string Name => obstacleConfig.name;

        public override string ToString()
        {
            return $"[Cluster] {Name}";
        }
    }
}