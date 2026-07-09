using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// プレイヤーが脱落したときの処理とか
/// </summary>
public class PlayerManager : MonoBehaviour
{
    public static PlayerManager Instance { get; private set; }
    public List<PlayerController> Players { get; private set; } = new ();

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Debug.LogWarning("PlayerManager follows singleton pattern!");
            Destroy(gameObject);
        }
    }

    public void DropOut(PlayerController player)
    {
        this.Players.Remove(player);
        Debug.Log("残り人数: " + this.Players.Count);
        
        if (this.Players.Count <= 1)
        {
            // プレイヤーが1人になったらラウンド終了
            RoundManager.Instance.EndRound();
        }
    }

    public void AddPlayer(PlayerController player)
    {
        this.Players.Add(player);
    }

    public List<WeaponData> GetAllPlayerWeapons()
    {
        List<WeaponData> allWeapons = new ();
        foreach (var player in Players)
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