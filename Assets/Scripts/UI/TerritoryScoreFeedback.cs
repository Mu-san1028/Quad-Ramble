using Unity.Netcode;
using UnityEngine;

/// <summary>自分の陣地得点だけを音と追従エフェクトで表現する。</summary>
[RequireComponent(typeof(CaptureZoneDirector))]
public sealed class TerritoryScoreFeedback : MonoBehaviour
{
    [SerializeField] private GameObject scoringEffectPrefab;
    [SerializeField] private Vector3 effectOffset = new(0f, 0.15f, 0f);
    [SerializeField] private Vector3 effectRotation = Vector3.zero;
    [SerializeField, Min(0.01f)] private float effectScale = 0.7f;
    [SerializeField, Min(0f)] private float minimumSoundInterval = 0.25f;

    private CaptureZoneDirector director;
    private GameObject effect;
    private float nextSoundTime;

    private void Awake()
    {
        this.director = GetComponent<CaptureZoneDirector>();
    }

    private void OnEnable()
    {
        this.nextSoundTime = float.NegativeInfinity;
        CaptureZone.PointsAwarded += this.OnPointsAwarded;
    }

    private PlayerController LocalPlayer()
    {
        var network = NetworkManager.Singleton;
        if (network == null || !network.IsListening || !network.IsClient) return null;

        var playerObject = network.LocalClient?.PlayerObject;
        var player = playerObject != null ? playerObject.GetComponent<PlayerController>() : null;
        return player != null && player.IsSpawned && player.IsOwner && !player.IsEliminated
            ? player : null;
    }

    private bool RoundActive()
    {
        var round = RoundManager.Instance;
        return round != null && round.IsSpawned && !round.IsIntro && !round.IsRoundOver
            && round.RemainingTime > 0f;
    }

    private void LateUpdate()
    {
        var player = this.LocalPlayer();
        bool scoring = false;
        if (player != null && this.RoundActive() && this.director.IsRunning)
        {
            foreach (var zone in this.director.Zones)
            {
                if (zone != null && zone.IsScoring && zone.OccupantClientId == player.ParticipantId)
                {
                    scoring = true;
                    break;
                }
            }
        }

        if (!scoring || this.scoringEffectPrefab == null)
        {
            this.ClearEffect();
            return;
        }

        Vector3 position = player.transform.position + this.effectOffset;
        Quaternion rotation = Quaternion.Euler(this.effectRotation) * this.scoringEffectPrefab.transform.rotation;
        if (this.effect == null)
        {
            // ローカル演出のみ。プレイヤーのColliderや描画の有効・無効制御に混ぜない。
            this.effect = Instantiate(this.scoringEffectPrefab, position, rotation);
            this.effect.transform.localScale = this.scoringEffectPrefab.transform.localScale * this.effectScale;
        }

        this.effect.transform.SetPositionAndRotation(position, rotation);
    }

    private void OnPointsAwarded(CaptureZone zone, ulong clientId)
    {
        var player = this.LocalPlayer();
        if (player == null || player.ParticipantId != clientId || !this.RoundActive()
            || zone == null || zone.gameObject.scene != this.gameObject.scene
            || Time.unscaledTime < this.nextSoundTime) return;

        this.nextSoundTime = Time.unscaledTime + this.minimumSoundInterval;
        SEAudio.Instance?.Play(SEType.TerritoryPointGain);
    }

    private void ClearEffect()
    {
        if (this.effect == null) return;

        this.effect.SetActive(false);
        Destroy(this.effect);
        this.effect = null;
    }

    private void OnDisable()
    {
        CaptureZone.PointsAwarded -= this.OnPointsAwarded;
        this.ClearEffect();
    }
}
