using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// プレイヤーが脱落したときの処理とか
/// </summary>
public class PlayerManager : MonoBehaviour
{
    Dictionary<PlayerController, WeaponData[]> playerWeapons = new Dictionary<PlayerController, WeaponData[]>();
    public static PlayerManager Instance { get; private set; }
    public List<PlayerController> players = new List<PlayerController>();
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void DropOut(PlayerController player)
    {
        players.Remove(player);
    }

    public void AddPlayer(PlayerController player)
    {
        players.Add(player);
    }

    public void SavePlayerWeapons(PlayerController player)
    {
        if (!playerWeapons.ContainsKey(player))
        {
            playerWeapons[player] = player.weapons;
        }
    }
}
