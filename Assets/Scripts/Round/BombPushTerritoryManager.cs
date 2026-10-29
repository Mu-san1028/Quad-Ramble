using Unity.Netcode;
using UnityEngine;

/// <summary>
/// BombPushの陣地所有者と、空き陣地への爆弾進入を管理する。
/// 共通のPlayerSpawnerには依存せず、このラウンドのシーンだけに配置する。
/// </summary>
public class BombPushTerritoryManager : NetworkBehaviour
{
    [SerializeField] private BombSensorField[] territoryFields;
    // 各ビットが陣地の所有有無を表す。値はサーバーが決めて全端末へ同期する。
    private readonly NetworkVariable<ulong> occupiedTerritoryMask = new();
    // 全プレイヤーのスポーンが完了した後、ラウンドごとに一度だけ陣地を確定する。
    private bool territoriesInitialized;

    public override void OnNetworkSpawn()
    {
        this.occupiedTerritoryMask.OnValueChanged += this.OnOccupiedTerritoriesChanged;

        // Clientはサーバーから届いた現在の設定を適用する。
        // ServerはRoundManagerの準備完了後に人数を確定してから適用する。
        if (!IsServer)
            this.ApplyTerritoryAvailability(this.occupiedTerritoryMask.Value);
    }

    private void Update()
    {
        // 陣地の空き判定は、プレイヤーが移動してもやり直さない。
        // RoundManager.IsReadyは全プレイヤーのスポーン完了後にtrueになる。
        if (!IsServer || this.territoriesInitialized
            || RoundManager.Instance == null || !RoundManager.Instance.IsReady
            || PlayerManager.Instance == null || this.territoryFields == null)
            return;

        this.territoriesInitialized = true;
        this.AssignTerritoryOwners();
    }

    public override void OnNetworkDespawn()
    {
        this.occupiedTerritoryMask.OnValueChanged -= this.OnOccupiedTerritoriesChanged;
    }

    /// <summary>ラウンド開始時のプレイヤー数を使い、陣地の持ち主を一度だけ決める。</summary>
    private void AssignTerritoryOwners()
    {
        if (PlayerManager.Instance == null || this.territoryFields == null) return;

        var players = PlayerManager.Instance.Players;
        ulong occupiedMask = 0;

        // RoundManagerがPlayerSpawnerで配置した順番と、陣地配列の順番を対応させる。
        int assignedCount = Mathf.Min(players.Count, this.territoryFields.Length);
        for (int i = 0; i < this.territoryFields.Length; i++)
        {
            BombSensorField field = this.territoryFields[i];
            if (field == null) continue;

            bool hasOwner = i < assignedCount && players[i] != null;
            if (hasOwner)
            {
                field.SetPlayerController(players[i]);
                // 最大64陣地までビットマスクで表現する。
                if (i < 64) occupiedMask |= 1UL << i;
            }
            else
            {
                field.SetPlayerController(null);
            }
        }

        // NetworkVariableに保存すると、後から接続した端末にも現在の設定が届く。
        this.occupiedTerritoryMask.Value = occupiedMask;
        this.ApplyTerritoryAvailability(occupiedMask);
    }

    private void OnOccupiedTerritoriesChanged(ulong previousMask, ulong currentMask)
    {
        this.ApplyTerritoryAvailability(currentMask);
    }

    /// <summary>開始時に確定した所有者の有無を各端末の陣地Colliderに適用する。</summary>
    private void ApplyTerritoryAvailability(ulong occupiedMask)
    {
        if (this.territoryFields == null) return;

        for (int i = 0; i < this.territoryFields.Length; i++)
        {
            BombSensorField field = this.territoryFields[i];
            if (field == null) continue;

            bool hasOwner = i < 64 && (occupiedMask & (1UL << i)) != 0;
            field.SetTerritoryActive(hasOwner);
        }
    }
}
