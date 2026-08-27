using UnityEngine;

public enum UpgradeKind { Speed, Jump, Turn }

[CreateAssetMenu(fileName = "NewUpgrade", menuName = "Items/PermanentUpgrade")]
public class PermanentUpgrade : ItemBase
{
    public UpgradeKind kind;
    public float speedBoost;
    public float jumpBoost;
    public float turnSpeedBoost;
    public int maxStacks = 3;   // この種類の上限段階数
}
