using System;
using UnityEngine;

/// <summary>
/// 選択できるキャラの一覧。Bootstrap に置いて全シーンで生き残らせる。
/// 10体は Base_Mesh の同じリグを共有しているので、モデルだけ差し替えれば Avatar は共通で足りる。
/// </summary>
public class CharacterDatabase : MonoBehaviour
{
    [Serializable]
    public class CharacterEntry
    {
        public string displayName;
        public GameObject modelPrefab;
        public Sprite icon;
    }

    [SerializeField] private CharacterEntry[] characters;

    public static CharacterDatabase Instance { get; private set; }

    public int Count => this.characters.Length;

    private void Awake()
    {
        if (CharacterDatabase.Instance)
        {
            Debug.LogWarning("An instance of CharacterDatabase already exists.");
            Destroy(this.gameObject);
        }
        else
        {
            CharacterDatabase.Instance = this;
            DontDestroyOnLoad(this.gameObject);
        }
    }

    public bool IsValidIndex(int index) => index >= 0 && index < this.characters.Length;

    public CharacterEntry Get(int index) => this.IsValidIndex(index) ? this.characters[index] : null;
}
