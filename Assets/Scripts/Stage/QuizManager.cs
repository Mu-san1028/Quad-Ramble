using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

public enum Difficulty { Easy, Normal, Hard }

public class QuizManager : MonoBehaviour
{
    [Header("床の管理")]
    private List<QuizFloor> floorList = new List<QuizFloor>();

    [Header("素材（10種類の野菜・果物プレハブ）")]
    [SerializeField] private List<GameObject> fruitPrefabs = new List<GameObject>();

    [Header("演出・補足用モデル")]
    [SerializeField] private GameObject questionBlockPrefab; // ハテナブロックの3Dモデル

    [Header("表示位置")]
    [SerializeField] private Transform displayPoint; // 中央の展示場所
    [SerializeField] private Transform[] optionPoints = new Transform[4]; // 4マスの解答表示場所（各床に対応）

    [Header("演出設定")]
    [SerializeField] private float showInterval = 1.2f; // 出題時に順番に見せる間隔（秒）

    [Header("中央表示のサイズ・間隔調整")]
    [SerializeField] private float fruitScale = 4.0f;        // 中央の野菜・果物の倍率
    [SerializeField] private float questionBlockScale = 2.0f; // 中央のハテナブロックの倍率
    [SerializeField] private float itemSpacing = 4.5f;        // 中央で並べる際の間隔

    [Header("選択肢（床側）のサイズ・位置調整")]
    [SerializeField] private float optionFruitScale = 1.0f;  // 選択肢側の野菜・果物の倍率
    [SerializeField] private float optionYOffset = 5.0f;     // 選択肢側のY軸浮かせ量
    [SerializeField] private float optionSpacing = 2.0f;     // 選択肢側で並べる際の間隔

    private List<GameObject> currentSequence = new List<GameObject>();
    private int correctIndex; // 4マスのうちどこが正解か (0~3)

    void Start()
    {
        // シーン内の全てのQuizFloorを取得してリストに格納
        QuizFloor[] foundFloors = FindObjectsByType<QuizFloor>(FindObjectsSortMode.None);
        floorList.AddRange(foundFloors);

        // 設定チェック
        if (questionBlockPrefab == null)
        {
            Debug.LogError("QuizManager: Question Block Prefab が Inspector で設定されていません！");
        }
    }

    private void Update()
    {
        if (Keyboard.current == null) return;

        // テスト用キー入力（クイズ開始）
        if (Keyboard.current.digit1Key.wasPressedThisFrame || Keyboard.current.numpad1Key.wasPressedThisFrame) StartQuiz(Difficulty.Easy);
        if (Keyboard.current.digit2Key.wasPressedThisFrame || Keyboard.current.numpad2Key.wasPressedThisFrame) StartQuiz(Difficulty.Normal);
        if (Keyboard.current.digit3Key.wasPressedThisFrame || Keyboard.current.numpad3Key.wasPressedThisFrame) StartQuiz(Difficulty.Hard);

        // テスト用キー入力（正解判定）
        if (Keyboard.current.numpad4Key.wasPressedThisFrame || Keyboard.current.digit4Key.wasPressedThisFrame)
        {
            ExecuteAnswerCheck();
        }
    }

    public void StartQuiz(Difficulty difficulty)
    {
        StopAllCoroutines();
        StartCoroutine(QuizRoutine(difficulty));
    }

    private IEnumerator QuizRoutine(Difficulty difficulty)
    {
        ClearAllDisplays();

        int itemCount = 7;
        int blankCount = 3;

        switch (difficulty)
        {
            case Difficulty.Easy:
                itemCount = 5;
                blankCount = 1;
                break;
            case Difficulty.Normal:
                itemCount = 7;
                blankCount = 3;
                break;
            case Difficulty.Hard:
                itemCount = 9;
                blankCount = 4;
                break;
        }

        List<GameObject> selectedPrefabs = fruitPrefabs.OrderBy(_ => System.Guid.NewGuid()).Take(itemCount).ToList();
        currentSequence = new List<GameObject>(selectedPrefabs);

        // 3. 【記憶フェーズ】プレイヤーに順番に見せる（中央：4倍）
        Debug.Log($"【{difficulty}】順番を覚えてください！");
        foreach (var prefab in currentSequence)
        {
            GameObject obj = Instantiate(prefab, displayPoint.position, Quaternion.identity, displayPoint);
            SetWorldScale(obj, Vector3.one * fruitScale);
            yield return new WaitForSeconds(showInterval);
            Destroy(obj);
            yield return new WaitForSeconds(0.2f);
        }

        int maxStartIndex = itemCount - blankCount;
        int startIndex = Random.Range(0, maxStartIndex + 1);

        List<GameObject> correctAnswer = currentSequence.GetRange(startIndex, blankCount);

        // 5. 中央の展示場所に全体像（ハテナブロック含む）を表示
        DisplayQuestionPreview(itemCount, startIndex, blankCount);

        // 6. 選択肢（4択）の作成
        List<GameObject> dummyPool = (difficulty == Difficulty.Easy) ? fruitPrefabs : selectedPrefabs;
        List<List<GameObject>> options = GenerateOptions(dummyPool, correctAnswer, blankCount);

        correctIndex = Random.Range(0, 4);
        var temp = options[correctIndex];
        int originalCorrectPos = options.FindIndex(opt => SequenceEqual(opt, correctAnswer));
        options[originalCorrectPos] = temp;
        options[correctIndex] = correctAnswer;

        // 7. 4つの床に選択肢を3D配置（5f上、サイズ1.0f、変形防止）
        DisplayOptions(options, blankCount);

        Debug.Log($"出題完了！正解の床は Index: {correctIndex} です。");
    }

    /// <summary>
    /// 中央の出題スペースに全体の並びを表示し、虫食い部分にハテナブロックを置く
    /// </summary>
    private void DisplayQuestionPreview(int itemCount, int startIndex, int blankCount)
    {
        for (int i = 0; i < itemCount; i++)
        {
            Vector3 offset = new Vector3((i - (itemCount - 1) / 2.0f) * itemSpacing, 0, 0);

            if (i >= startIndex && i < startIndex + blankCount)
            {
                if (questionBlockPrefab != null)
                {
                    GameObject block = Instantiate(questionBlockPrefab, displayPoint.position + offset, Quaternion.identity, displayPoint);
                    SetWorldScale(block, Vector3.one * questionBlockScale);
                }
            }
            else
            {
                GameObject fruit = Instantiate(currentSequence[i], displayPoint.position + offset, Quaternion.identity, displayPoint);
                SetWorldScale(fruit, Vector3.one * fruitScale);
            }
        }
    }

    /// <summary>
    /// 4マスの選択肢位置にモデルを並べて表示する
    /// </summary>
    private void DisplayOptions(List<List<GameObject>> options, int blankCount)
    {
        for (int i = 0; i < 4; i++)
        {
            Transform point = optionPoints[i];
            List<GameObject> sequence = options[i];

            for (int j = 0; j < sequence.Count; j++)
            {
                Vector3 offset = new Vector3((j - (blankCount - 1) / 2.0f) * optionSpacing, optionYOffset, 0);

                GameObject fruit = Instantiate(sequence[j], point.position + offset, Quaternion.identity, point);

                // 親（point）の変形を引き継がないように見た目の大きさを直す
                SetWorldScale(fruit, Vector3.one * optionFruitScale);
            }
        }
    }

    /// <summary>
    /// 親オブジェクトのスケール影響を打ち消し、見た目の絶対サイズを指定する補助関数
    /// </summary>
    private void SetWorldScale(GameObject obj, Vector3 targetWorldScale)
    {
        obj.transform.localScale = Vector3.one;
        Vector3 parentScale = obj.transform.lossyScale;

        // 親のスケールで割ることで、親の変形を相殺する
        obj.transform.localScale = new Vector3(
            targetWorldScale.x / (parentScale.x != 0 ? parentScale.x : 1f),
            targetWorldScale.y / (parentScale.y != 0 ? parentScale.y : 1f),
            targetWorldScale.z / (parentScale.z != 0 ? parentScale.z : 1f)
        );
    }

    private List<List<GameObject>> GenerateOptions(List<GameObject> availablePool, List<GameObject> correct, int blankCount)
    {
        List<List<GameObject>> optionsList = new List<List<GameObject>> { correct };
        HashSet<string> generatedKeys = new HashSet<string> { GetSequenceKey(correct) };

        int attempts = 0;
        while (optionsList.Count < 4 && attempts < 200)
        {
            attempts++;

            List<GameObject> dummy = availablePool.OrderBy(_ => System.Guid.NewGuid()).Take(blankCount).ToList();
            string key = GetSequenceKey(dummy);

            if (!generatedKeys.Contains(key))
            {
                generatedKeys.Add(key);
                optionsList.Add(dummy);
            }
        }

        return optionsList;
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

    private bool SequenceEqual(List<GameObject> a, List<GameObject> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++) if (a[i] != b[i]) return false;
        return true;
    }

    private string GetSequenceKey(List<GameObject> list)
    {
        return string.Join("_", list.Select(x => x.name));
    }

    public void ExecuteAnswerCheck()
    {
        NotifyAnswer(correctIndex);
    }

    public void NotifyAnswer(int correctID)
    {
        foreach (QuizFloor floor in floorList)
        {
            if (floor != null)
            {
                floor.CheckAnswer(correctID);
            }
        }
    }
}