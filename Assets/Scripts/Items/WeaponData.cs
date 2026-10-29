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
    [Header("武器の種別")]
    public WeaponType weaponType;

    [Header("共通設定")]
    public int bulletStock;      // 初期の残弾数
    public float reloadTime;     // 次の攻撃までに必要な時間
    public float Power;          // 攻撃の強さ
    public float attackWindup;   // 攻撃するまでの待機時間

    [Header("武器SE（未設定なら再生しない）")]
    [Tooltip("Attack Windupが0より大きい武器で、チャージ開始時に1回再生します。")]
    public AudioClip chargeStartSE;
    [Range(0f, 2f)] public float chargeStartSEVolume = 1f;
    public AudioClip useSE;
    [Range(0f, 2f)] public float useSEVolume = 1f;
    [Tooltip("近接武器：敵への命中時。投擲武器：爆発時に下の設定に従って1回再生。")]
    public AudioClip hitSE;
    [Range(0f, 2f)] public float hitSEVolume = 1f;
    [Tooltip("投擲武器のみ。ONなら壁・地面などへの衝突でもSEを再生。OFFなら衝突時の爆発範囲に敵がいた場合のみ再生。飛行時間切れでは常に無音。")]
    public bool playExplosionSEOnMiss = true;

    [Header("Melee：攻撃範囲")]
    public AttackRangeData attackRange;

    [Header("Melee：攻撃演出")]
    public SwordSwingEffect meleeEffectPrefab;
    public Vector3 meleeEffectOffset = new Vector3(0f, 1f, 0.5f);
    public Vector3 meleeEffectRotation;

    [Header("Throw：弾のPrefabと投擲設定")]
    public GameObject bulletPrefab;
    [Tooltip("発射位置の高さ補正。実際の弾と軌道プレビューの両方に適用します。")]
    public float launchHeightOffset = 0f;
    public float throwSpeed = 10f;
    public float blastRadius = 3f;
    public float gravityScale = 9.8f;

    /// <summary>重力を受けずまっすぐ飛ぶ武器。射程は弾速で決まり、照準は十字で出す</summary>
    public bool FliesStraight => this.gravityScale <= 0f;
    public float launchAngle = 45f;

    [Header("直進武器：全プレイヤーに見える照準線")]
    [Tooltip("エイム中だけ照準線を表示します。重力0のThrow武器に適用。")]
    public bool showAimLine = false;
    public Color aimLineColor = new Color(1f, 0.15f, 0.1f, 0.8f);
    [Min(0.001f)] public float aimLineWidth = 0.025f;
    [Tooltip("実際の弾の飛距離と、この値の短い方まで表示します。")]
    [Min(0f)] public float aimLineMaxDistance = 100f;

    [Header("落ちる弾の向きを飛行方向に合わせるか")]
    [Tooltip("弾のローカル＋Z方向を、飛行中の進行方向へ向けます。")]
    public bool faceFlightDirection;

    [Header("Deployable：設置するPrefab")]
    public GameObject deployPrefab;
}
