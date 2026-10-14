using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;

/// <summary>
/// ラウンドの流れに合わせて BGM を切り替える。
/// シーンをまたいで鳴り続ける必要があるので Bootstrap に置き、DontDestroyOnLoad にする。
/// タイミングは RoundManager の NetworkVariable を読むだけで、専用の通信は持たない。
/// </summary>
public class RoundAudio : MonoBehaviour
{
    public static RoundAudio Instance { get; private set; }

    [Header("シーン名")]
    [SerializeField] private string titleSceneName = "Title";
    [SerializeField] private string resultSceneName = "ResultScene";

    [Header("共通BGM")]
    [SerializeField] private AudioClip titleBgm;
    [SerializeField] private AudioClip resultBgm;

    [Header("ラウンドBGMの既定値")]
    [SerializeField] private AudioClip defaultIntroBgm;   // RoundManager 側が未設定のとき
    [SerializeField] private AudioClip defaultMainBgm;

    [Header("音量")]
    [SerializeField, Range(0f, 1f)] private float volume = 0.6f;
    [SerializeField] private float crossfadeDuration = 0.8f;   // 曲を切り替える速さ
    [SerializeField] private float duckSpeed = 3f;             // 幕が覆っている間の絞り

    [Header("ミキサー（任意）")]
    [SerializeField] private AudioMixerGroup output;

    private AudioSource active;   // いま前面で鳴っている方
    private AudioSource idle;     // 消えていく方
    private AudioClip current;
    private float fade = 1f;      // active の比率。1 で切り替え完了
    private float duck = 1f;      // 幕による全体の絞り
    private bool bgmEnabled;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this.gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(this.gameObject);

        // 席番号とは独立して、会場でBGMを鳴らすプロセスを指定する。
        this.bgmEnabled = ReadBgmEnabled();

        this.active = this.CreateSource();
        this.idle = this.CreateSource();
    }


    private static bool ReadBgmEnabled()
    {
        string[] args = System.Environment.GetCommandLineArgs();

        for (int i = 0; i < args.Length - 1; i++)
        {
            if (!string.Equals(args[i], "-bgm", System.StringComparison.OrdinalIgnoreCase))
                continue;

            if (string.Equals(args[i + 1], "off", System.StringComparison.OrdinalIgnoreCase))
                return false;

            if (string.Equals(args[i + 1], "on", System.StringComparison.OrdinalIgnoreCase))
                return true;
        }

        // 通常起動やEditorでは、既存の再生動作を維持する。
        return true;
    }


    private AudioSource CreateSource()
    {
        var source = this.gameObject.AddComponent<AudioSource>();
        source.loop = true;
        source.playOnAwake = false;
        source.volume = 0f;

        // 鳴らすのは1プロセスだけなので、定位させると他の画面と食い違う
        source.spatialBlend = 0f;

        if (this.output != null) source.outputAudioMixerGroup = this.output;
        return source;
    }

    private void Update()
    {
        if (!this.bgmEnabled)
            return;

        this.Switch(this.Desired());
        this.Advance();
    }

    /// <summary>いま鳴らすべきクリップ。RoundManager が居ればラウンド中</summary>
    private AudioClip Desired()
    {
        var round = RoundManager.Instance;
        if (round != null)
        {
            // イントロとカウントダウンは静かな層。Start! で本編へ切り替わる
            bool beforeStart = round.IsIntro || round.CountdownRemaining > 0f;
            if (beforeStart) return round.IntroBgm != null ? round.IntroBgm : this.defaultIntroBgm;
            return round.MainBgm != null ? round.MainBgm : this.defaultMainBgm;
        }

        string scene = SceneManager.GetActiveScene().name;
        if (scene == this.resultSceneName) return this.resultBgm;
        if (scene == this.titleSceneName) return this.titleBgm;
        return null;   // シーン遷移中など
    }

    /// <summary>鳴っている方を idle に回し、新しい方を前面に出す</summary>
    private void Switch(AudioClip next)
    {
        if (next == this.current) return;

        this.current = next;
        (this.active, this.idle) = (this.idle, this.active);
        this.fade = 0f;

        if (next == null)
        {
            this.active.Stop();
            return;
        }

        this.active.clip = next;
        this.active.volume = 0f;
        this.active.Play();
    }

    private void Advance()
    {
        // 読み込み直後の1フレームは deltaTime が数百ms に跳ねるので上限を掛ける
        float dt = Mathf.Min(Time.deltaTime, 0.05f);

        bool covering = SceneTransitionUI.Instance != null && SceneTransitionUI.Instance.IsCovering;
        this.duck = Mathf.MoveTowards(this.duck, covering ? 0f : 1f, this.duckSpeed * dt);

        float step = this.crossfadeDuration <= 0f ? 1f : dt / this.crossfadeDuration;
        this.fade = Mathf.MoveTowards(this.fade, 1f, step);

        float master = this.volume * this.duck;
        this.active.volume = master * this.fade;
        this.idle.volume = master * (1f - this.fade);

        if (this.fade >= 1f && this.idle.isPlaying) this.idle.Stop();
    }
}
