using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class TutorialController : MonoBehaviour
{
    private enum Step { LowFence, Pickup, Aim, Attack, Break, Upgrade, HighFence, Clear }
    [SerializeField] private TMP_Text instruction;
    [SerializeField] private TMP_Text progress;
    [SerializeField] private Button exitButton;
    [SerializeField] private BreakableBlock block;
    [SerializeField] private Collider aimTarget;
    [SerializeField] private Collider supplyTrigger;
    [SerializeField] private Transform startPoint;
    [SerializeField] private Transform lowFence;
    [SerializeField] private Transform highFence;
    [SerializeField] private Transform checkpoint;
    [SerializeField] private Transform goal;
    private PlayerController player;
    private PlayerItem inventory;
    private Step step;
    private float aimedFor;
    private bool jumped;
    private bool exiting;

    private void Awake()
    {
        block.DamageEnabled = false;
        supplyTrigger.enabled = false;
    }

    private IEnumerator Start()
    {
        exitButton.onClick.AddListener(Exit);
        // TutorialはBootstrap経由で必要な共通システムを初期化する。
        if (!TutorialSession.Active)
        {
            instruction.text = "Bootstrapから起動し、タイトルのチュートリアルを選んでください。";
            yield break;
        }
        while (PlayerItem.Local == null || RoundManager.Instance == null || !RoundManager.Instance.IsReady)
            yield return null;
        inventory = PlayerItem.Local;
        player = inventory.GetComponent<PlayerController>();
        for (int i = 0; i < CharacterDatabase.Instance.Count; i++)
            if (string.Equals(CharacterDatabase.Instance.Get(i).displayName, "hero", System.StringComparison.OrdinalIgnoreCase))
            {
                player.GetComponentInChildren<PlayerAppearance>(true).SetLocalCharacter(i);
                break;
            }
        player.Jumped += OnJump;
        player.AttackPerformed += OnAttack;
        supplyTrigger.enabled = false;
        ShowStep();
    }

    private void Update()
    {
        if ((LocalGamepad.Current?.startButton.wasPressedThisFrame ?? false)
            || (LocalGamepad.Seat == 0 && (Keyboard.current?.escapeKey.wasPressedThisFrame ?? false))) Exit();
        bool introducing = RoundManager.Instance == null || RoundManager.Instance.IsIntro;
        instruction.transform.parent.gameObject.SetActive(!introducing);
        if (player == null || exiting || step == Step.Clear || introducing) return;
        if (player.transform.position.y < -5f)
        {
            Transform safe = step == Step.LowFence ? startPoint : checkpoint;
            player.Teleport(safe.position, safe.rotation);
            jumped = false;
        }
        switch (step)
        {
            case Step.LowFence:
                if (jumped && player.IsGrounded && player.transform.position.z > lowFence.position.z + 1f)
                    Advance();
                break;
            case Step.Pickup:
                if (HasBomb()) Advance();
                break;
            case Step.Aim:
                aimedFor = player.IsAimingAt(aimTarget) ? aimedFor + Time.deltaTime : 0f;
                progress.text = $"3 / 7　照準検知：{Mathf.Clamp01(aimedFor / .5f):P0}";
                if (aimedFor >= .5f) Advance();
                break;
            case Step.Break:
                if (block.IsBroken) Advance();
                break;
            case Step.Upgrade:
                if (PlayerStatus.Local != null && PlayerStatus.Local.JumpBonus > 0f) Advance();
                break;
            case Step.HighFence:
                if (jumped && player.IsGrounded && player.transform.position.z > highFence.position.z + 1f
                    && Vector3.Distance(player.transform.position, goal.position) < 4f) Advance();
                break;
        }
    }

    private bool HasBomb()
    {
        var bomb = ItemDatabase.Instance.GetItem(Items.Bomb);
        foreach (var slot in inventory.Weapons)
            if (slot != null && slot.itemData == bomb && slot.currentAmount > 0) return true;
        return false;
    }

    private void OnJump() => jumped = true;
    private void OnAttack()
    {
        if (step == Step.Attack) Advance();
    }

    private void Advance()
    {
        step++;
        jumped = false;
        if (step == Step.Attack) block.DamageEnabled = true;
        if (step == Step.Pickup) supplyTrigger.enabled = true;
        if (step == Step.Clear) player.StopOrMovePlayer(false);
        ShowStep();
    }

    private void ShowStep()
    {
        string[] messages = {
            "柵を越えよう！\n左スティック / WASDで移動、B（下ボタン）/ Spaceでジャンプ",
            "アイテムを取ってみよう！\n光るアイテムに触れると、ボムを取得できます",
            "照準を合わせよう！\nZL / LTを押しながら右スティックで着弾マーカーをブロックへ\nキーボード：Bを押しながらマウス",
            "照準OK！ 攻撃してみよう！\nZR / RT / Enterでボムを投げよう",
            "攻撃OK！ ブロックを壊そう！\nボムを当てよう。なくなったらアイテムを取り直せます",
            "ジャンプ力UPを取ろう！\nブロックから出た強化アイテムに触れよう",
            "高い柵を乗り越えよう！\nジャンプ力UP！ 柵の先のゴールへ進もう",
            "チュートリアル クリア！\n移動・照準・攻撃・強化をマスター！\nタイトルへ戻って対戦しよう"
        };
        instruction.text = messages[(int)step];
        progress.text = step == Step.Clear ? "CLEAR!" : $"{(int)step + 1} / 7";
    }

    public void Exit()
    {
        if (exiting || GameManager.Instance == null) return;
        exiting = true;
        GameManager.Instance.ReturnToTitle();
    }

    private void OnDestroy()
    {
        if (player == null) return;
        player.Jumped -= OnJump;
        player.AttackPerformed -= OnAttack;
    }
}
