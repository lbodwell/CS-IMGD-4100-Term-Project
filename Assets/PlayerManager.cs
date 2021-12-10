using UnityEngine;

public class PlayerManager : MonoBehaviour {
    public static PlayerManager Instance;
    public GameObject player;
    private void Awake() {
        Instance = this;
    }
}
