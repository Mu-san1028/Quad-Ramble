using UnityEngine;

[CreateAssetMenu(fileName = "NewWeapon", menuName = "Items/Weapon")]
public class WeaponData : ItemBase
{
    public GameObject bulletPrefab;
    public int bulletStock;
    public float reloadTime;
    public float Power;
}
