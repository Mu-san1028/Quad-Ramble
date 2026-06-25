using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;
using Unity.Netcode;

[System.Serializable]
public class RoundManager : MonoBehaviour
{
    public timerUI timerUI;         // �^�C�}�[�ƕR�t��
    public string nextSceneName;      // ���̃V�[����
    public int survivingPlayers = 4;  //�����v���C���[��
    [SerializeField]public PlayerController[] players; // 操作するプレイヤーたちが入る
    [SerializeField]public PlayerSpawner playerSpawner; // プレイヤーをスポーンさせるためのスクリプト

    private bool _isRoundOver = false;
    void Start()
    {
        if (playerSpawner == null)
        {
            playerSpawner = FindAnyObjectByType<PlayerSpawner>();
        }

        // プレイヤーをスポーンさせる
        for (int i = 0; i < players.Length; i++)
        {
            playerSpawner.SpawnPlayer(players[i], i);
        }
    }

    void Update()
    {
        if (_isRoundOver) return;

        // �I������1:�������Ԃ��I��
        if (timerUI != null && timerUI.GetCurrentTime() <= 0)
        {
            StartCoroutine(EndRoundRoutine("�^�C���A�b�v�I"));
        }
    }

    // �I������2:�v���C���[��1�l�ɂȂ�
    public void OnPlayerDeath()
    {
        if (_isRoundOver) return;

        survivingPlayers--;
        Debug.Log("�c��l��: " + survivingPlayers);

        if (survivingPlayers <= 1)
        {
            StartCoroutine(EndRoundRoutine("�T�o�C�o���I���I"));
        }
    }

    // �V�[���؂�ւ�
    IEnumerator EndRoundRoutine(string message)
    {
        _isRoundOver = true;
        Debug.Log(message);

        // 3�b�ҋ@
        yield return new WaitForSeconds(3f);

        // ���̃V�[���̓ǂݍ���
        NetworkManager.Singleton.SceneManager.LoadScene(nextSceneName, LoadSceneMode.Single);

    }
}