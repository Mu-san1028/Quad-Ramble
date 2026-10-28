using UnityEngine;

/// <summary>ローカルプレイヤーのチャージ状態を専用UIに渡す。</summary>
public class PlayerChargeFeedback : MonoBehaviour
{
    [SerializeField] private WeaponChargeUI prefab;
    private WeaponChargeUI current;
    private PlayerController player;
    private bool wasCharging;
    private float completedUntil;

    private void Awake()
    {
        player = GetComponent<PlayerController>();
    }

    private void LateUpdate()
    {
        bool available = player != null && player.IsSpawned && player.IsOwner && player.IsControllable;
        bool charging = available && player.IsCharging;
        if (wasCharging && available && !charging && player.ChargeProgress >= 1f)
            completedUntil = Time.unscaledTime + 0.12f;
        wasCharging = charging;
        bool visible = available && (charging || (player.ChargeProgress >= 1f && Time.unscaledTime < completedUntil));
        if (visible && current == null && prefab != null) current = Instantiate(prefab);
        if (current != null) current.SetProgress(visible, player != null ? player.ChargeProgress : 0f);
    }

    private void OnDisable()
    {
        if (current != null) Destroy(current.gameObject);
    }
}
