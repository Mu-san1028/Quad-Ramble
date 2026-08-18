using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

/// <summary>
/// プレイヤーが脱落したときの処理とか
/// </summary>
public class PlayerManager : MonoBehaviour
{
    public static PlayerManager Instance { get; private set; }
    // 全プレイヤーのリストと、残っているプレイヤーのリストを保持する。脱落順のプレイヤーも
    public List<PlayerController> AllPlayers { get; private set; } = new ();
    public List<PlayerController> Players { get; private set; } = new ();
    public List<PlayerController> EliminatedPlayers { get; private set; } = new ();

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
        if (!Players.Contains(player)) return;
        
        this.Players.Remove(player);
        Debug.Log("残り人数: " + this.Players.Count);
        // 脱落したプレイヤーをリストに追加
        EliminatedPlayers.Add(player);
        
        this.CheckRoundEnd();
    }

    public void AddPlayer(PlayerController player)
    {
        this.AllPlayers.Add(player);
        this.Players.Add(player);
    }

    public void RemovePlayer(PlayerController player)
    {
        bool wasAlive = this.Players.Remove(player);
        this.AllPlayers.Remove(player);
        this.EliminatedPlayers.Remove(player);

        Debug.Log($"プレイヤーが離脱しました。残り人数: {this.Players.Count}");

        if (wasAlive) this.CheckRoundEnd();
    }

    private void CheckRoundEnd()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;
        if (NetworkManager.Singleton.ShutdownInProgress) return;
        if (GameManager.Instance == null || GameManager.Instance.currentState != GameState.Playing) return;
        if (this.Players.Count > 1) return;

        RoundManager.Instance?.EndRound();
    }


    public List<WeaponData> GetAllPlayerWeapons()
    {
        List<WeaponData> allWeapons = new ();
        foreach (var player in AllPlayers)
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

    public List<List<PlayerController>> GetRanking()
    {
        var ranking = new List<List<PlayerController>>();
        
        if (Players.Count > 0)
        {
            // 残っているプレイヤーを1位として追加
            ranking.Add(new List<PlayerController>(Players));
        }

        for (int i = EliminatedPlayers.Count - 1; i >= 0; i--)
        {
            // 脱落したプレイヤーを脱落順に追加
            ranking.Add(new List<PlayerController> { EliminatedPlayers[i] });
        }
        return ranking;
    }

    public void ResetPlayers()
    {
        EliminatedPlayers.Clear();
        Players = new List<PlayerController>(AllPlayers);
    }
}