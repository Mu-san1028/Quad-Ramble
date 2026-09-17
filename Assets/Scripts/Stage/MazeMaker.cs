using UnityEngine;

public class MazeMaker : MonoBehaviour
{
    [SerializeField] private GameObject wallPrefab;
    [SerializeField] private int[][] mazeData = new int[][]
    {
        new int[] {1, 1, 1, 1, 1},
        new int[] {1, 0, 0, 0, 1},
        new int[] {1, 0, 1, 0, 1},
        new int[] {1, 0, 0, 0, 1},
        new int[] {1, 1, 1, 1, 1}
    };
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        MakeMaze();
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    void MakeMaze()
    {
        
    }
    public struct BlockData
    {
        public int[][] data;
    }
}
