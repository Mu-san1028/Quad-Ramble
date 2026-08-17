using UnityEngine;

public enum WeaponType
{
    Melee, // 近接武器
    Throw, // 遠距離武器
    Deployable // 設置型武器
}

[CreateAssetMenu(fileName = "NewWeapon", menuName = "Items/Weapon")]
public class WeaponData : ItemBase
{
    [Header("弾のPrefab")]
    public GameObject bulletPrefab;
    [Header("初期の残弾数")]
    public int bulletStock;
    [Header("次の攻撃までに必要な時間")]
    public float reloadTime;
    [Header("攻撃の強さ")]
    public float Power;
    [Header("攻撃するまでの待機時間")]
    public float attackWindup;
    [Header("攻撃範囲")]
    public AttackRangeData attackRange;

    [Header("投擲設定")]
    public float throwSpeed = 10f;
    public float blastRadius = 3f;
    public float gravityScale = 9.8f;
    public float launchAngle = 45f; // 投擲の角度


}