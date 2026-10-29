using UnityEngine;

/// <summary>KO通知を本人用UIへ渡す。プレイヤーの破棄・ラウンド変更で残さない。</summary>
public class PlayerKOFeedback : MonoBehaviour
{
    [SerializeField] private KnockoutUI prefab;
    private KnockoutUI current;

    public void Play()
    {
        if (prefab == null) return;
        if (current == null) current = Instantiate(prefab);
        current.Play();
    }

    private void OnDisable()
    {
        if (current != null) Destroy(current.gameObject);
    }
}
