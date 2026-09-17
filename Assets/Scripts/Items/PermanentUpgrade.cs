using UnityEngine;

public enum UpgradeKind { Speed, Jump, Power, Range }

[CreateAssetMenu(fileName = "NewUpgrade", menuName = "Items/PermanentUpgrade")]
public class PermanentUpgrade : ItemBase
{
    public UpgradeKind kind;
    public float speedBoost;   // 加算
    public float jumpBoost;    // 加算
    public float powerRate;    // 割合。0.2 で武器の威力 +20%
    public float rangeRate;    // 割合。0.2 で攻撃範囲・爆発半径・弾速 +20%
    public int maxStacks = 3;   // この種類の上限段階数
}
