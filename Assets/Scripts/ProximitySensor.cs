using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ProximitySensor : MonoBehaviour
{
    public Dictionary<string, AudioClip> layerToAudioClip;
    public List<AudioClip> layerAudios;
    public List<string> layerNames;
    public List<float> ranges;
    public float alertFrequency;
    public List<AudioSource> audioSources;

    private List<Collider> obstaclesInRange;
    private float remainingCoolDown;


    // Start is called before the first frame update
    void Start()
    {
        layerToAudioClip = new Dictionary<string, AudioClip>();
        for (int i = 0; i < layerNames.Count;i ++)
        {
            layerToAudioClip[layerNames[i]] = layerAudios[i];
        }
    }


    // Update is called once per frame
    void Update()
    {
        remainingCoolDown -= Time.deltaTime;
        if (remainingCoolDown < 0)
        {
            remainingCoolDown = alertFrequency;
        }
    }

    public void OnTriggerEnter(Collider other)
    {
        obstaclesInRange.Add(other);
    }
    public void OnTriggerExit(Collider other)
    {
        obstaclesInRange.Remove(other);
    }
}
