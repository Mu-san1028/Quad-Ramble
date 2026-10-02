using UnityEngine;

/// <summary>
/// 自分の恒久強化の段数が増えたとき、種類に対応する通知行を表示する。
/// 常に有効なCanvasなど、通知行の外側に配置する。
/// </summary>
public class BuffNotificationUI : MonoBehaviour
{
    [System.Serializable]
    private class RowBinding
    {
        public UpgradeKind kind;
        public string displayName;
        public Color color = Color.white;
        public BuffRowUI row;

        [System.NonSerialized] public int previousCount;
    }

    [Header("強化の種類と通知行")]
    [SerializeField] private RowBinding[] rows =
    {
        new RowBinding { kind = UpgradeKind.Speed, displayName = "Speed", color = new Color(0.2f, 0.8f, 1f) },
        new RowBinding { kind = UpgradeKind.Jump, displayName = "Jump", color = new Color(0.4f, 1f, 0.4f) },
        new RowBinding { kind = UpgradeKind.Power, displayName = "Power", color = new Color(1f, 0.25f, 0.15f) },
        new RowBinding { kind = UpgradeKind.Range, displayName = "Weapon", color = new Color(1f, 0.8f, 0.2f) }
    };

    private PlayerStatus trackedStatus;
    private bool bindingsValid;


    private void OnEnable()
    {
        this.trackedStatus = null;
        this.bindingsValid = this.ValidateBindings();

        if (this.bindingsValid)
            this.HideAllRows();
    }


    private void Update()
    {
        if (!this.bindingsValid)
            return;

        PlayerStatus status = PlayerStatus.Local;

        if (status == null || !status.IsSpawned)
        {
            // Unityで破棄済みの参照も検出し、以前の通知を残さない。
            if (!ReferenceEquals(this.trackedStatus, null))
            {
                this.HideAllRows();
                this.trackedStatus = null;
            }

            return;
        }

        if (this.trackedStatus != status)
        {
            this.HideAllRows();
            this.trackedStatus = status;

            // 初回・プレイヤー交代時の所持数は、取得通知として扱わない。
            foreach (RowBinding binding in this.rows)
                binding.previousCount = status.GetStacks(binding.kind);

            return;
        }

        foreach (RowBinding binding in this.rows)
        {
            int count = status.GetStacks(binding.kind);
            int previousCount = binding.previousCount;
            binding.previousCount = count;

            if (binding.row == null)
                continue;

            if (count > previousCount)
            {
                binding.row.ShowGain(binding.displayName, binding.color, count);
            }
            else if (count < previousCount)
            {
                // ラウンドリセットなどで段数が減ったら、古い通知を消す。
                binding.row.gameObject.SetActive(false);
            }
        }
    }


    private void OnDisable()
    {
        if (this.bindingsValid)
            this.HideAllRows();

        this.trackedStatus = null;
    }


    private void HideAllRows()
    {
        foreach (RowBinding binding in this.rows)
        {
            binding.previousCount = 0;

            if (binding.row != null)
                binding.row.gameObject.SetActive(false);
        }
    }


    private bool ValidateBindings()
    {
        if (this.rows == null || this.rows.Length != 4)
        {
            Debug.LogWarning("BuffNotificationUI: Rowsを4件にし、Speed・Jump・Power・Weaponを設定してください。", this);
            return false;
        }

        for (int i = 0; i < this.rows.Length; i++)
        {
            RowBinding binding = this.rows[i];

            if (binding == null || binding.row == null)
            {
                Debug.LogWarning($"BuffNotificationUI: Rowsの要素{i}に、対応するBuffRowUIを割り当ててください。設定後、コンポーネントを有効にし直してください。", this);
                return false;
            }

            if (this.transform.IsChildOf(binding.row.transform))
            {
                Debug.LogWarning("BuffNotificationUI: 管理スクリプトは通知行の外側にあるCanvasへ移動してください。行と一緒に無効になる配置では通知を再表示できません。", this);
                return false;
            }

            if (!System.Enum.IsDefined(typeof(UpgradeKind), binding.kind))
            {
                Debug.LogWarning($"BuffNotificationUI: Rowsの要素{i}のKindを、Speed・Jump・Power・Weaponから選び直してください。", this);
                return false;
            }

            for (int j = 0; j < i; j++)
            {
                if (this.rows[j].kind == binding.kind || this.rows[j].row == binding.row)
                {
                    Debug.LogWarning("BuffNotificationUI: KindとRowが重複しています。4種類それぞれに別の通知行を割り当ててください。", this);
                    return false;
                }
            }
        }

        return true;
    }
}
