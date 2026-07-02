using UnityEngine;
using System.Collections.Generic;

public class WindReceiver : MonoBehaviour
{
    private PlayerController playerController;
    private HashSet<WindArea> activeWindAreas = new HashSet<WindArea>();

    void Awake()
    {
        playerController = GetComponent<PlayerController>();
    }

    void OnTriggerEnter(Collider other)
    {
        var area = other.GetComponent<WindArea>();
        if (area != null) activeWindAreas.Add(area);
    }

    void OnTriggerExit(Collider other)
    {
        var area = other.GetComponent<WindArea>();
        if (area != null) activeWindAreas.Remove(area);
    }

    void FixedUpdate()
    {
        // ”ÍˆÍ“à‚É‚ ‚é‚·‚×‚Ä‚Ì•—‚Ì‰e‹¿‚ğŒvZ
        foreach (var area in activeWindAreas)
        {
            // PlayerController‚ÌVelocityChangeƒƒ\ƒbƒh‚É—Í‚ğ“n‚·
            // •—‚ÌŒü‚« * ‹­‚³ * ŠÔ ‚ğ‰ÁZ
            playerController.VelocityChange(area.windDirection.normalized * area.force * Time.fixedDeltaTime);
        }
    }
}