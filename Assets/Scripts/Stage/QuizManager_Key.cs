using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;
using TMPro;

public enum Difficulty { Easy, Normal, Hard }

public class QuizManager_Key : NetworkBehaviour
{
    [Header("デバッグ設定")]
    [SerializeField] private bool isDebugMode = false;

    [Header("UI設定")]
    [SerializeField] private TMP_Text messageText; // 状況説明用の TextMeshPro

    [Header("床の管理")]
    private List<QuizFloor> floorList = new List<QuizFloor>();

    [Header("素材（10種類の野菜・果物プレハブ）")]
    [SerializeField] private List<GameObject> fruitPrefabs = new List<GameObject>();

    [Header("演出・補足用モデル")]
    [SerializeField] private GameObject questionBlockPrefab;

    [Header("表示位置")]
    [SerializeField] private Transform displayPoint;
    [SerializeField] private Transform[] optionPoints = new Transform[4];

    [Header("演出設定")]
    [SerializeField] private float showInterval = 1.0f; // 果物を1つずつ表示する間隔

    [Header("中央表示のサイズ・間隔調整")]
    [SerializeField] private float fruitScale = 6.0f;
    [SerializeField] private float questionBlockScale = 3.5f;
    [SerializeField] private float totalWidth = 20.0f;

    [Header("選択肢（床側）のサイズ・位置調整")]
    [SerializeField] private float optionFruitScale = 4.0f;
    [SerializeField] private float optionYOffset = 0.5f;
    [SerializeField] private float optionSpacing = 4.5f;

    private int correctIndex;

    private Coroutine gameSequenceCoroutine;
    private Coroutine clientDisplayCoroutine;

    void Start()
    {
        QuizFloor[] foundFloors = FindObjectsByType<QuizFloor>(FindObjectsSortMode.None);
        floorList.AddRange(foundFloors);
    }

    public override void OnNetworkSpawn()
    {
        // サーバー（ホスト）側でのみ自動進行シーケンスを開始する
        if (IsServer && !isDebugMode)
        {
            gameSequenceCoroutine = StartCoroutine(AutoGameSequence());
        }
    }

    private void Update()
    {
        if (!IsSpawned) return;

        // デバッグモードがONの場合のみ手動キー入力を有効化
        if (isDebugMode)
        {
            if (Keyboard.current == null) return;

            if (Keyboard.current.digit1Key.wasPressedThisFrame || Keyboard.current.numpad1Key.wasPressedThisFrame)
            {
                RequestStartQuizServerRpc(Difficulty.Easy);
            }
            if (Keyboard.current.digit2Key.wasPressedThisFrame || Keyboard.current.numpad2Key.wasPressedThisFrame)
            {
                RequestStartQuizServerRpc(Difficulty.Normal);
            }
            if (Keyboard.current.digit3Key.wasPressedThisFrame || Keyboard.current.numpad3Key.wasPressedThisFrame)
            {
                RequestStartQuizServerRpc(Difficulty.Hard);
            }
            if (Keyboard.current.digit4Key.wasPressedThisFrame || Keyboard.current.numpad4Key.wasPressedThisFrame)
            {
                ExecuteAnswerCheckServerRpc(false);
            }
        }
    }

    #region 自動進行シーケンス (90秒構成)

    private IEnumerator AutoGameSequence()
    {
        // 1. 最初の10秒：説明待機時間
        UpdateMessageClientRpc("これから表示される様々なアイテムを順番通りに覚えましょう！問題はどんどん難しくなります！");
        yield return new WaitForSeconds(10.0f);

        // 2. 10秒〜30秒（20秒間）：簡単問題
        // (演出時間約3.6秒 + 思考時間11.4秒 = 計15秒の後に判定)
        StartQuiz(Difficulty.Easy, totalAnswerTime: 11.4f);
        yield return new WaitForSeconds(15.0f);
        ExecuteAnswerCheck(isFinalQuestion: false);
        yield return new WaitForSeconds(5.0f);  // 余韻時間

        // 3. 30秒〜55秒（25秒間）：普通問題
        // (演出時間約6.0秒 + 思考時間14.0秒 = 計20秒の後に判定)
        StartQuiz(Difficulty.Normal, totalAnswerTime: 14.0f);
        yield return new WaitForSeconds(20.0f);
        ExecuteAnswerCheck(isFinalQuestion: false);
        yield return new WaitForSeconds(5.0f);

        // 4. 55秒〜85秒（30秒間）：激ムズ問題
        // (演出時間約8.4秒 + 思考時間16.6秒 = 計25秒の後に判定)
        StartQuiz(Difficulty.Hard, totalAnswerTime: 16.6f);
        yield return new WaitForSeconds(25.0f);
        ExecuteAnswerCheck(isFinalQuestion: true);
        yield return new WaitForSeconds(5.0f);

        // 5. 85秒〜90秒（5秒間）：終了
        ClearAllDisplaysClientRpc();
        UpdateMessageClientRpc(""); // テキストクリア
    }

    #endregion

    #region デバッグ・RPC要求処理

    [ServerRpc(RequireOwnership = false)]
    private void RequestStartQuizServerRpc(Difficulty difficulty)
    {
        // デバッグ用（思考時間は仮で10秒設定）
        StartQuiz(difficulty, 10.0f);
    }

    [ServerRpc(RequireOwnership = false)]
    private void ExecuteAnswerCheckServerRpc(bool isFinalQuestion)
    {
        ExecuteAnswerCheck(isFinalQuestion);
    }

    public void ExecuteAnswerCheck(bool isFinalQuestion = false)
    {
        if (IsServer)
        {
            NotifyAnswerClientRpc(correctIndex);

            if (isFinalQuestion)
            {
                UpdateMessageClientRpc("正解はこちら！全問正解者はすごい！");
            }
            else
            {
                UpdateMessageClientRpc("正解はこちら！すぐに次の問題が始まるので準備しましょう！");
            }
        }
    }

    #endregion

    #region クイズ生成・同期コアロジック

    public void StartQuiz(Difficulty difficulty, float totalAnswerTime)
    {
        if (!IsServer) return;

        int itemCount = (difficulty == Difficulty.Easy) ? 3 : (difficulty == Difficulty.Normal) ? 5 : 7;
        int blankCount = (difficulty == Difficulty.Easy) ? 1 : (difficulty == Difficulty.Normal) ? 2 : 3;

        List<int> allIndices = Enumerable.Range(0, fruitPrefabs.Count).ToList();
        List<int> selectedIndices = allIndices.OrderBy(_ => System.Guid.NewGuid()).Take(itemCount).ToList();

        int maxStartIndex = itemCount - blankCount;
        int startIndex = Random.Range(0, maxStartIndex + 1);

        List<int> correctSeq = selectedIndices.GetRange(startIndex, blankCount);

        List<int> dummyPool = (difficulty == Difficulty.Easy) ? allIndices : selectedIndices;
        List<List<int>> options = GenerateOptionsIndices(dummyPool, correctSeq, blankCount);

        int correctPos = Random.Range(0, 4);
        var temp = options[correctPos];
        int originalCorrectPos = options.FindIndex(opt => opt.SequenceEqual(correctSeq));
        options[originalCorrectPos] = temp;
        options[correctPos] = correctSeq;

        int[] seqArray = selectedIndices.ToArray();
        int[] flatOptions = options.SelectMany(x => x).ToArray();

        StartQuizClientRpc(difficulty, seqArray, startIndex, blankCount, flatOptions, correctPos, totalAnswerTime);
    }

    [ClientRpc]
    private void StartQuizClientRpc(Difficulty difficulty, int[] sequence, int startIndex, int blankCount, int[] flatOptions, int correctPos, float totalAnswerTime)
    {
        if (clientDisplayCoroutine != null)
        {
            StopCoroutine(clientDisplayCoroutine);
        }
        clientDisplayCoroutine = StartCoroutine(QuizRoutineClient(difficulty, sequence, startIndex, blankCount, flatOptions, correctPos, totalAnswerTime));
    }

    private IEnumerator QuizRoutineClient(Difficulty difficulty, int[] sequence, int startIndex, int blankCount, int[] flatOptions, int correctPos, float totalAnswerTime)
    {
        ClearAllDisplays();
        correctIndex = correctPos;

        // 1. 記憶フェーズ（問題出題中）
        UpdateMessage("注目！");
        foreach (int index in sequence)
        {
            GameObject obj = Instantiate(fruitPrefabs[index], displayPoint.position, Quaternion.identity, displayPoint);
            SetWorldScale(obj, Vector3.one * fruitScale);
            yield return new WaitForSeconds(showInterval);
            Destroy(obj);
            yield return new WaitForSeconds(0.2f);
        }

        // 2. 中央の出題表示（果物 ＋ ハテナボックス）
        DisplayQuestionPreview(sequence, startIndex, blankCount);

        // 3. 4マスの選択肢表示
        List<List<int>> options = new List<List<int>>();
        for (int i = 0; i < 4; i++)
        {
            options.Add(flatOptions.Skip(i * blankCount).Take(blankCount).ToList());
        }
        DisplayOptions(options, blankCount);

        // 4. 解答時間のカウントダウン表示制御
        UpdateMessage("？にあてはまるアイテムのある場所へ移動しましょう！");

        float remainingTime = totalAnswerTime;
        while (remainingTime > 0)
        {
            // 残り時間が5秒以下になったらカウントダウンテキスト（「5」「4」「3」...）に変更
            if (remainingTime <= 5.0f)
            {
                int countNum = Mathf.CeilToInt(remainingTime);
                UpdateMessage(countNum.ToString());
            }

            yield return null;
            remainingTime -= Time.deltaTime;
        }
    }

    private void DisplayQuestionPreview(int[] sequence, int startIndex, int blankCount)
    {
        int itemCount = sequence.Length;
        float calculatedSpacing = (itemCount > 1) ? totalWidth / (itemCount - 1) : 0;

        for (int i = 0; i < itemCount; i++)
        {
            float xPos = (itemCount > 1) ? -(totalWidth / 2.0f) + (i * calculatedSpacing) : 0;
            Vector3 targetPosition = displayPoint.position + new Vector3(xPos, 0, 0);

            if (i >= startIndex && i < startIndex + blankCount)
            {
                if (questionBlockPrefab != null)
                {
                    GameObject block = Instantiate(questionBlockPrefab, targetPosition, displayPoint.rotation, displayPoint);
                    block.transform.localScale = Vector3.one * questionBlockScale;
                }
            }
            else
            {
                GameObject fruit = Instantiate(fruitPrefabs[sequence[i]], targetPosition, displayPoint.rotation, displayPoint);
                SetWorldScale(fruit, Vector3.one * fruitScale);
            }
        }
    }

    private void DisplayOptions(List<List<int>> options, int blankCount)
    {
        for (int i = 0; i < 4; i++)
        {
            Transform point = optionPoints[i];
            if (point == null) continue;

            List<int> sequence = options[i];

            for (int j = 0; j < sequence.Count; j++)
            {
                Vector3 offset = new Vector3((j - (blankCount - 1) / 2.0f) * optionSpacing, optionYOffset, 0);
                Vector3 targetPos = point.position + offset;

                GameObject fruit = Instantiate(fruitPrefabs[sequence[j]], targetPos, point.rotation, point);
                SetWorldScale(fruit, Vector3.one * optionFruitScale);

                Collider[] colliders = fruit.GetComponentsInChildren<Collider>();
                foreach (Collider col in colliders) col.enabled = false;
            }
        }
    }

    [ClientRpc]
    private void NotifyAnswerClientRpc(int correctID)
    {
        foreach (QuizFloor floor in floorList)
        {
            if (floor != null)
            {
                floor.CheckAnswer(correctID);
            }
        }
    }

    [ClientRpc]
    private void ClearAllDisplaysClientRpc()
    {
        ClearAllDisplays();
    }

    [ClientRpc]
    private void UpdateMessageClientRpc(string message)
    {
        UpdateMessage(message);
    }

    private void UpdateMessage(string message)
    {
        if (messageText != null)
        {
            messageText.text = message;
        }
    }

    #endregion

    #region ヘルパー関数

    private List<List<int>> GenerateOptionsIndices(List<int> pool, List<int> correct, int blankCount)
    {
        List<List<int>> optionsList = new List<List<int>> { correct };
        HashSet<string> generatedKeys = new HashSet<string> { string.Join("_", correct) };

        int attempts = 0;
        while (optionsList.Count < 4 && attempts < 200)
        {
            attempts++;
            List<int> dummy = pool.OrderBy(_ => System.Guid.NewGuid()).Take(blankCount).ToList();
            string key = string.Join("_", dummy);

            if (!generatedKeys.Contains(key))
            {
                generatedKeys.Add(key);
                optionsList.Add(dummy);
            }
        }
        return optionsList;
    }

    private void SetWorldScale(GameObject obj, Vector3 targetWorldScale)
    {
        obj.transform.localScale = Vector3.one;
        Vector3 parentScale = obj.transform.lossyScale;
        obj.transform.localScale = new Vector3(
            targetWorldScale.x / (parentScale.x != 0 ? parentScale.x : 1f),
            targetWorldScale.y / (parentScale.y != 0 ? parentScale.y : 1f),
            targetWorldScale.z / (parentScale.z != 0 ? parentScale.z : 1f)
        );
    }

    private void ClearAllDisplays()
    {
        if (displayPoint != null)
        {
            foreach (Transform child in displayPoint) Destroy(child.gameObject);
        }
        if (optionPoints != null)
        {
            foreach (Transform point in optionPoints)
            {
                if (point != null)
                {
                    foreach (Transform child in point) Destroy(child.gameObject);
                }
            }
        }
    }

    #endregion
}