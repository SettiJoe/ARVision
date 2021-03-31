using System;
using System.Collections.Generic;
using UnityEngine;

namespace Navigator
{
    [CreateAssetMenu(fileName = "New Obstacle Config", menuName = "Configs/ObstacleConfig", order = 0)]
    public class ObstacleConfig : ScriptableObject
    {
        public string configName;
        public LayerMask layerMask;
        public List<WarningConfig> warningRanges;
    }

    [Serializable]
    public class WarningConfig
    {
        public AudioClip audioCue;
        public float audioCueFrequency;

        public float range;
        public int priority;
    }
}