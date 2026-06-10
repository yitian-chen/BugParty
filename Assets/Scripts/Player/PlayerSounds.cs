using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerSounds : MonoBehaviour
{
    public static PlayerSounds Instance { get; private set; }
    public event EventHandler OnPlayerFootStep;
    private Player player;
    private float footstempTimer;
    private float footstempTimerMax = 0.1f;
    private void Awake()
    {
        Instance = this;
        player = GetComponent<Player>();
    }
    private void Update()
    {
        footstempTimer -= Time.deltaTime;
        if (footstempTimer < 0f)
        {
            footstempTimer = footstempTimerMax;
            if (player.IsWalking())
            {
                float volume = 1f;
                SoundManager.Instance.PlayFootstepsSound(player.transform.position, volume);
            }
        }

    }

}
