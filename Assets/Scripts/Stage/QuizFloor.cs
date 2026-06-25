/// <summary>
/// クイズなど、特定の床を消すギミックに使用するスクリプト。
/// CheckAnswerメソッドに正解のIDを渡すことで、床が消えるかどうかを判定する。
/// 逆に, 特定のIDの床のみを消す場合, DeleteFloorByIDメソッドを使用することで、指定したIDの床のみを消すことができる。
/// </summary>
using UnityEngine;
using System.Collections;
public class QuizFloor : MonoBehaviour
{
    [SerializeField] private int floorID; // インスペクターで各床にユニークなIDを設定
    private bool isDissolved = false;

    // 正解のIDがどれかを管理するクラス（適宜外部からアクセスできるようにしてください）
    public void CheckAnswer(int correctAnswerID)
    {
        if (floorID == correctAnswerID)
        {
            Debug.Log("正解！床はそのままです。");
        }
        else
        {
            if (!isDissolved)
            {
                StartCoroutine(DissolveFloor());
            }
        }
    }

    public void DeleteFloorByID(int targetID)
    {
        if (floorID == targetID && !isDissolved)
        {
            Debug.Log("IDが一致しました。床を消します。");
            StartCoroutine(DissolveFloor());
        }
    }

    private IEnumerator DissolveFloor()
    {
        isDissolved = true;

        // 床を消す
        gameObject.SetActive(false);
        Debug.Log("不正解！床が消えます。");

        // 5秒待つ
        yield return new WaitForSeconds(5.0f);

        // 床を復活させる
        gameObject.SetActive(true);
        isDissolved = false;
        Debug.Log("床が復活しました。");
    }
}