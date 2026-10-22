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
    // レースラウンド用。ゴールした順に入る
    public List<PlayerController> FinishedPlayers { get; private set; } = new ();

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

    /// <summary>
    /// レースラウンドでゴールしたときの処理。記録できたら true。
    /// </summary>
    public bool Finish(PlayerController player)
    {
        if (!this.Players.Remove(player)) return false;

        this.FinishedPlayers.Add(player);
        Debug.Log($"ゴール！ {this.FinishedPlayers.Count}着 / 残り{this.Players.Count}人");

        this.CheckRoundEnd();
        return true;
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
        this.FinishedPlayers.Remove(player);

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

        // ゴールしたプレイヤーが最優先。着順がそのまま順位になる
        foreach (var player in FinishedPlayers)
        {
            ranking.Add(new List<PlayerController> { player });
        }

        if (Players.Count > 0)
        {
            var raceGoal = FinishedPlayers.Count == 0
                ? FindAnyObjectByType<RaceGoal>()
                : null;

            if (raceGoal != null)
            {
                // Raceで誰もゴールしていない場合は、終了時のゴールへの直線距離で順位を決める。
                // Players自体は並べ替えず、同距離ではClientIdで順序を固定する。
                Vector3 goalPosition = raceGoal.transform.position;
                var unfinishedPlayers = new List<PlayerController>(Players);
                unfinishedPlayers.Sort((a, b) =>
                {
                    float distanceA = (a.transform.position - goalPosition).sqrMagnitude;
                    float distanceB = (b.transform.position - goalPosition).sqrMagnitude;
                    int comparison = distanceA.CompareTo(distanceB);
                    return comparison != 0 ? comparison : a.OwnerClientId.CompareTo(b.OwnerClientId);
                });

                foreach (var player in unfinishedPlayers)
                    ranking.Add(new List<PlayerController> { player });
            }
            else
            {
                // 通常は残っているプレイヤーを同順位として追加。
                ranking.Add(new List<PlayerController>(Players));
            }
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
        FinishedPlayers.Clear();
        Players = new List<PlayerController>(AllPlayers);
    }
}
