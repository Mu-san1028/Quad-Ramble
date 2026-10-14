using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 名簿で選ばれているキャラのモデルを ModelRoot の下に生成する。
/// 10体は Base_Mesh の同じリグを共有しているので、Avatar もコントローラも差し替え不要。
/// 全クライアントで走るので、他人の見た目もこれで揃う。
/// </summary>
public class PlayerAppearance : NetworkBehaviour
{
    [SerializeField] private Transform modelRoot;
    [SerializeField] private PlayerAnimator playerAnimator;

    // 名簿に選択が無いとき（Title を通さずラウンドシーンを直接再生したとき）の見た目
    [SerializeField] private int fallbackIndex = 0;

    private GameObject currentModel;
    private int appliedIndex = -1;
    private Quaternion initialModelRootRotation = Quaternion.identity;

    private void Awake()
    {
        if (this.playerAnimator == null) this.playerAnimator = GetComponentInParent<PlayerAnimator>();

        if (this.modelRoot != null)
        {
            this.initialModelRootRotation = this.modelRoot.localRotation;
        }
    }

    /// <summary>Titleの表示用に、プレイヤー本体を動かさずモデルだけを回転する。</summary>
    public void RotateTitleModel(float degrees)
    {
        if (this.modelRoot == null)
            return;

        this.modelRoot.Rotate(0f, degrees, 0f, Space.Self);
    }

    /// <summary>キャラ変更時やTitle終了時に、モデルの向きを初期状態へ戻す。</summary>
    public void ResetTitleModelRotation()
    {
        if (this.modelRoot == null)
            return;

        this.modelRoot.localRotation = this.initialModelRootRotation;
    }

    public override void OnNetworkSpawn()
    {
        ConnectionContainer.OnInstanceReady += this.HandleContainerReady;
        if (ConnectionContainer.Instance != null) this.HandleContainerReady(ConnectionContainer.Instance);
    }

    public override void OnNetworkDespawn()
    {
        ConnectionContainer.OnInstanceReady -= this.HandleContainerReady;
        if (ConnectionContainer.Instance != null)
        {
            ConnectionContainer.Instance.OnConnectionsChanged -= this.HandleRosterChanged;
        }
    }

    private void HandleContainerReady(ConnectionContainer container)
    {
        container.OnConnectionsChanged -= this.HandleRosterChanged;
        container.OnConnectionsChanged += this.HandleRosterChanged;
        this.Apply();
    }

    private void HandleRosterChanged(int _) => this.Apply();

    private void Apply()
    {
        if (this.modelRoot == null)
        {
            Debug.LogError("PlayerAppearance: modelRoot が未設定です");
            return;
        }

        Connection connection = ConnectionContainer.Instance?.GetConnection(this.OwnerClientId);
        int index = connection?.CharacterIndex ?? -1;
        if (index < 0) index = this.fallbackIndex;
        if (index < 0 || index == this.appliedIndex) return;

        CharacterDatabase.CharacterEntry entry = CharacterDatabase.Instance?.Get(index);
        if (entry == null || entry.modelPrefab == null) return;

        if (this.currentModel != null)
        {
            // Destroy はフレーム末なので、先に親から切り離さないと Rebind が古い骨を掴む
            this.currentModel.transform.SetParent(null);
            Destroy(this.currentModel);
        }

        this.currentModel = Instantiate(entry.modelPrefab, this.modelRoot);

        Animator modelAnimator = this.currentModel.GetComponentInChildren<Animator>();
        if (modelAnimator != null)
        {
            modelAnimator.applyRootMotion = false;
            this.playerAnimator?.SetAnimator(modelAnimator);
        }

        this.currentModel.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);

        this.ResetTitleModelRotation();

        this.appliedIndex = index;
    }
}
