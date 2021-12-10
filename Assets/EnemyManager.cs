using System.Collections.Generic;
using UnityEngine;

public class EnemyManager : MonoBehaviour {
    public static EnemyManager Instance;
    public List<GameObject> enemies;
    private void Awake() {
        Instance = this;
    }
}
