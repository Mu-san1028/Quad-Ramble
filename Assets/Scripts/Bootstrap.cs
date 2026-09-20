using UnityEngine;
using UnityEngine.SceneManagement;

public class Bootstrap : MonoBehaviour
{
    [SerializeField] private string titleSceneName = "Title";

    void Start()
    {
        SceneManager.LoadScene(titleSceneName, LoadSceneMode.Single);
    }
}
//