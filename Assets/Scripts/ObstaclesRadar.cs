using System;
using System.Collections;
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
        [SerializeField]
        private bool logVerbose = true;
        [SerializeField]
        private bool disableLogs = false;

        [SerializeField]
        private List<ObstacleConfig> obstacleConfigs;

        [SerializeField]
        private int warningPriorityRefreshRateMilliseconds = 500;
        [SerializeField]
        public AudioSource audioWarningPrefab;

        private List<ActiveWarningObstacle> activeObstacles;
        private List<ActiveWarningAudio> audioSources;

        private void TryPlayObstacleWarnings()
        {
            Log($"TryPlayObstacleWarnings {activeObstacles.Count}", false);

            foreach (var activeAudio in audioSources)
            {
                Log($"TryPlayObstacleWarnings: {activeAudio} >  < activeObstacles: {activeObstacles.Count}", false);

                if (activeAudio.activeObstacle == null)
                    continue;

                activeAudio.remainingWarningTime -= Time.deltaTime;

                Log($"remaining time: {activeAudio.remainingWarningTime}");

                if (!(activeAudio.remainingWarningTime < 0) || activeAudio.assignedAudioSource.isPlaying)
                    continue;

                Log($"[{activeAudio.Name}] Trying play sound");

                var audioSource = activeAudio.assignedAudioSource;
                var warningConfig = activeAudio.warningConfig;
                var animationCurve = warningConfig.animationCurve;

                activeAudio.remainingWarningTime = warningConfig.audioCueFrequency;

                audioSource.clip = warningConfig.audioCue;
                audioSource.transform.position = activeAudio.GetAudioSourcePosition(transform.position);

                audioSource.minDistance = animationCurve.keys[0].time;
                audioSource.maxDistance = animationCurve.keys[animationCurve.length - 1].time;
                audioSource.rolloffMode = AudioRolloffMode.Custom;
                audioSource.SetCustomCurve(AudioSourceCurveType.CustomRolloff, animationCurve);

                audioSource.Play();
            }
        }

        private IEnumerator RefreshWarnings()
        {
            while (true)
            {
                AssignWarningAudios();

                yield return new WaitForSeconds(warningPriorityRefreshRateMilliseconds * 0.001f);
            }
        }

        private void AssignWarningAudios()
        {
            Log($"AssignWarningAudios || activeObstacles.Count: {activeObstacles.Count}", false);

            audioSources[0].activeObstacle = null;
            audioSources[1].activeObstacle = null;

            var position = transform.position;
            var gotFeetPosition = GetFeetPosition(position, out var feetPosition);

            foreach (var currentObstacle in activeObstacles)
            {
                Log($"Calculating for {currentObstacle}");
                SetWarningConfig(currentObstacle, position, feetPosition, gotFeetPosition);

                if (currentObstacle.warningConfig == null)
                    continue;

                Log($"Has config! {currentObstacle}");

                var warningAudio = GetCorrespondentWarningAudioSource(currentObstacle);

                if (warningAudio.activeObstacle == null ||
                    currentObstacle.distanceToPlayer < warningAudio.activeObstacle.distanceToPlayer &&
                    currentObstacle.warningConfig.priority <= warningAudio.activeObstacle.warningConfig.priority)
                {
                    Log($"Replaced warning config to {currentObstacle}");
                    warningAudio.activeObstacle = currentObstacle;
                }
            }

            ResetRemainingWarningTime(audioSources[0]);
            ResetRemainingWarningTime(audioSources[1]);

            Log(audioSources[0].ToString());
            Log(audioSources[1].ToString());
        }

        private void ResetRemainingWarningTime(ActiveWarningAudio warningAudio)
        {
            if (warningAudio.warningConfig == null)
                return;

            warningAudio.remainingWarningTime = Math.Min(
                warningAudio.remainingWarningTime,
                warningAudio.warningConfig.audioCueFrequency);
        }

        private void SetWarningConfig(ActiveWarningObstacle obstacle, Vector3 position, Vector3 feetPosition, bool gotFeetPosition)
        {
            var closestPoint = obstacle.obstacle.ClosestPoint(position);
            obstacle.closestPoint = closestPoint;
            obstacle.distanceToPlayer = (position - closestPoint).magnitude;

            if (!gotFeetPosition)
            {
                obstacle.warningConfig = GetWarningConfig(obstacle);
                return;
            }

            var closestFeetPoint = obstacle.obstacle.ClosestPoint(feetPosition);
            var distance = (feetPosition - closestFeetPoint).magnitude;

            if (!(distance < obstacle.distanceToPlayer))
            {
                obstacle.warningConfig = GetWarningConfig(obstacle);
                return;
            }

            obstacle.closestPoint = closestFeetPoint;
            obstacle.distanceToPlayer = distance;

            obstacle.warningConfig = GetWarningConfig(obstacle);
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

        private ActiveWarningAudio GetCorrespondentWarningAudioSource(ActiveWarningObstacle obstacle)
        {
            return audioSources[0].obstacleConfig == obstacle.obstacleConfig ? audioSources[0] : audioSources[1];
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
            Log($"Update {audioSources.Count}");

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
            audioSources = new List<ActiveWarningAudio>(obstacleConfigs.Count);

            foreach (var obstacleConfig in obstacleConfigs)
            {
                var audioSource = Instantiate(audioWarningPrefab, transform);
                audioSources.Add(new ActiveWarningAudio()
                {
                    assignedAudioSource = audioSource,
                    obstacleConfig = obstacleConfig
                });
            }
        }

        private void Awake()
        {
            Debug.Assert(obstacleConfigs.Count == 2, "There should always 2 obstacles config");

            InitializeAudioSources();
            activeObstacles = new List<ActiveWarningObstacle>();

            StartCoroutine(RefreshWarnings());
        }

        private void Log(string message, bool isVerbose = true)
        {
            if (disableLogs)
                return;

            if (!logVerbose && isVerbose)
                return;

            Debug.Log($"{Time.time} || {message}");
        }
    }

    public class ActiveWarningObstacle
    {
        public ObstacleConfig obstacleConfig;
        public Collider obstacle;

        public Vector3 closestPoint;
        public float distanceToPlayer;
        public WarningConfig warningConfig;

        public string Name => obstacleConfig.name;

        public override string ToString()
        {
            return $"[ActiveWarningObstacle] | {Name} |";
        }

        public ActiveWarningAudio ParentAudio;
    }

    public class ActiveWarningAudio
    {
        public AudioSource assignedAudioSource;
        public ObstacleConfig obstacleConfig;

        public float remainingWarningTime;
        public ActiveWarningObstacle activeObstacle;

        public WarningConfig warningConfig => activeObstacle?.warningConfig;
        public int priority => warningConfig?.priority ?? Int32.MaxValue;
        public string Name => obstacleConfig.name;

        public Vector3 GetAudioSourcePosition(Vector3 playerPosition)
        {
            // TODO: Maybe add an extra offset if we guide the player to keep the device at chest level
            return new Vector3(activeObstacle.closestPoint.x, playerPosition.y, activeObstacle.closestPoint.z);
        }

        public override string ToString()
        {
            if (activeObstacle == null)
                return $"[ActiveWarningAudio] | {Name} | No Active Obstacle |";

            if (warningConfig == null)
                return $"[ActiveWarningAudio] | {Name} | No Warning Config |";

            return $"[ActiveWarningAudio] | {Name} | {warningConfig.audioCue} | {remainingWarningTime} |";
        }
    }
}