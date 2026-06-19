using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// プレイヤーが脱落したときの処理とか
/// </summary>
public class PlayerManager : MonoBehaviour
{
    
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

    public List<WeaponData> GetAllPlayerWeapons()
    {
        List<WeaponData> allWeapons = new List<WeaponData>();
        foreach (var player in players)
        {
            var pItem = player.GetComponent<PlayerItem>();
            if (pItem != null)
            {
                // スロット（箱）を一つずつ確認する
                foreach (var slot in pItem.Weapons)
                {
                    // 箱が空っぽではなく、かつ中身が WeaponData ならリストに追加
                    if (slot != null && slot.itemData is WeaponData weaponData)
                    {
                        allWeapons.Add(weaponData);
                    }
                }
            }
        }
        return allWeapons;
    }
}