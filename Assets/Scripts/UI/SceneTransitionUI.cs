using TMPro;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// シーン遷移を1枚のパネルで覆う。右から入って画面を覆い、左へ抜けて開く。
/// シーンをまたいで生き残る必要があるので Bootstrap に置き、DontDestroyOnLoad にする。
/// </summary>
public class SceneTransitionUI : MonoBehaviour
{
    public static SceneTransitionUI Instance { get; private set; }

    /// <summary>幕が動いている、または覆っている間は true</summary>
    public bool IsCovering => this.phase != Phase.Idle;

    [SerializeField] private RectTransform panel;
    [SerializeField] private TextMeshProUGUI roundText;

    [Header("速さ")]
    [SerializeField] private float closeDuration = 0.5f;
    [SerializeField] private float openDuration = 0.6f;
    [SerializeField] private float safetyTimeout = 3f;
    [SerializeField] private float minCoveredTime = 0.8f;   // 中央で必ず止まる時間
    private bool openRequested;


    private enum Phase { Idle, Closing, Covered, Opening }
    private Phase phase = Phase.Idle;
    private float elapsed;
    private float coveredTime;

    private NetworkSceneManager subscribed;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this.gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(this.gameObject);
        this.SetOffset(1.5f);   // 右の画面外で待機
    }

    private void Update()
    {
        this.TrackSceneManager();
        this.Animate();
    }

    /// <summary>SceneManager は NetworkManager の起動後に現れ、Shutdown で消える</summary>
    private void TrackSceneManager()
    {
        var sm = NetworkManager.Singleton != null ? NetworkManager.Singleton.SceneManager : null;
        if (sm == this.subscribed) return;

        if (this.subscribed != null) this.subscribed.OnSceneEvent -= this.HandleSceneEvent;
        this.subscribed = sm;
        if (sm != null) sm.OnSceneEvent += this.HandleSceneEvent;
    }

    private void HandleSceneEvent(SceneEvent e)
    {
        if (e.SceneEventType == SceneEventType.Load)
        {
            this.Close();
        }
        else if (e.SceneEventType == SceneEventType.LoadComplete
                 && e.ClientId == NetworkManager.Singleton.LocalClientId)
        {
            this.Open();
        }
    }

    private void Close()
    {
        if (this.phase == Phase.Closing || this.phase == Phase.Covered) return;
        this.phase = Phase.Closing;
        this.elapsed = 0f;
        this.openRequested = false;
        this.SetOffset(1.5f);
    }

    private void Open()
    {
        this.openRequested = true;
    }

    private void Animate()
    {
        if (this.phase != Phase.Idle) this.RefreshRoundText();

        switch (this.phase)
        {
            case Phase.Closing:
                if (this.Slide(1.5f, 0f, this.closeDuration))
                {
                    this.phase = Phase.Covered;
                    this.coveredTime = Time.time;
                }
                break;

            case Phase.Covered:
                float held = Time.time - this.coveredTime;
                if (held < this.minCoveredTime) break;   // ここで必ず止まる

                bool timedOut = this.safetyTimeout > 0f && held > this.safetyTimeout;
                if (timedOut) Debug.LogWarning("[Transition] 開く合図が来なかったので強制的に開きます");

                if (this.openRequested || timedOut)
                {
                    this.phase = Phase.Opening;
                    this.elapsed = 0f;
                    this.openRequested = false;
                }
                break;


            case Phase.Opening:
                if (this.Slide(0f, -1.5f, this.openDuration))
                {
                    this.phase = Phase.Idle;
                    this.SetOffset(1.5f);
                }
                break;
        }
    }

    /// <summary>動かしきったら true</summary>
    private bool Slide(float from, float to, float duration)
    {
        // 読み込み直後の1フレームは deltaTime が数百ms に跳ねる。
        // そのまま足すと開く動きが一瞬で終わるので上限を掛ける
        this.elapsed += Mathf.Min(Time.deltaTime, 0.05f);

        float t = duration <= 0f ? 1f : Mathf.Clamp01(this.elapsed / duration);
        this.SetOffset(Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t)));
        return t >= 1f;
    }

    /// <summary>-1 = 左の画面外 / 0 = 中央 / +1 = 右の画面外</summary>
    private void SetOffset(float normalized)
    {
        float width = ((RectTransform)this.panel.parent).rect.width;
        this.panel.anchoredPosition = new Vector2(normalized * width, 0f);
    }

    private void RefreshRoundText()
    {
        int round = ConnectionContainer.Instance != null ? ConnectionContainer.Instance.CurrentRound : 0;
        this.roundText.text = round > 0 ? $"Round {round}" : "";
    }
}
