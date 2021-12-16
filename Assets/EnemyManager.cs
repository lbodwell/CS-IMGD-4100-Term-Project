using System.Collections.Generic;
using UnityEngine;


public class Enemy {
    public enum EnemyType {
        Undefined,
        Type1,
        Type2
    }
    
    public GameObject obj;
    public EnemyType type;
    
    public Enemy(GameObject obj, EnemyType type) {
        this.obj = obj;
        this.type = type;
    }
}

public class EnemyManager : MonoBehaviour {
    public static EnemyManager Instance;
    public List<Enemy> enemies;
    
    private void Awake() {
        Instance = this;
        enemies = new List<Enemy>();
    }

    public void addEnemy(GameObject obj, Enemy.EnemyType type) {
        enemies.Add(new Enemy(obj, type));
    }
}
