using System.Collections;
using UnityEngine;

/// <summary>近接攻撃の見た目のみを再生する。命中判定はPlayerControllerが担当する。</summary>
public class SwordSwingEffect : MonoBehaviour
{
    [SerializeField] private Transform swingPivot;
    [SerializeField] private ParticleSystem slashEffect;

    [Header("振り方")]
    [SerializeField] private float startAngle = -70f;
    [SerializeField] private float endAngle = 70f;
    [SerializeField, Min(0.01f)] private float followThroughTime = 0.15f;

    [Header("エフェクトが消えるまでの余韻")]
    [SerializeField, Min(0f)] private float effectTailTime = 0.4f;

    private PlayerController owner;

    public void Play(PlayerController player, float hitDelay)
    {
        if (this.swingPivot == null || this.swingPivot == transform
            || !this.swingPivot.IsChildOf(transform))
        {
            Debug.LogWarning("SwordSwingEffect: Swing Pivotに子オブジェクトを設定してください。", this);
            Destroy(this.gameObject);
            return;
        }

        this.owner = player;
        if (this.slashEffect != null)
            this.slashEffect.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        StartCoroutine(this.Swing(Mathf.Max(0f, hitDelay)));
    }

    private IEnumerator Swing(float hitDelay)
    {
        Quaternion baseRotation = this.swingPivot.localRotation;

        // 正面を通過する時刻を、既存のattackWindupによる命中判定に合わせる。
        yield return this.Rotate(baseRotation, this.startAngle, 0f, hitDelay);

        if (this.slashEffect != null)
            this.slashEffect.Play(true);

        yield return this.Rotate(baseRotation, 0f, this.endAngle, this.followThroughTime);

        // 斬撃はSwingPivotの兄弟に置き、剣だけ先に消す。
        this.swingPivot.gameObject.SetActive(false);
        yield return new WaitForSeconds(this.effectTailTime);

        Destroy(this.gameObject);
    }

    private IEnumerator Rotate(Quaternion baseRotation, float from, float to, float duration)
    {
        float elapsed = 0f;

        while (elapsed < duration)
        {
            this.swingPivot.localRotation = baseRotation
                * Quaternion.Euler(0f, Mathf.Lerp(from, to, elapsed / duration), 0f);

            elapsed += Time.deltaTime;
            yield return null;
        }

        this.swingPivot.localRotation = baseRotation * Quaternion.Euler(0f, to, 0f);
    }

    private void Update()
    {
        // 本人の攻撃中断、ラウンド終了、プレイヤー消滅時に演出を残さない。
        if (this.owner == null || !this.owner.IsSpawned || this.owner.IsTitleMode
            || (this.owner.IsOwner && !this.owner.IsControllable)
            || (RoundManager.Instance != null && RoundManager.Instance.IsRoundOver))
        {
            Destroy(this.gameObject);
        }
    }
}
