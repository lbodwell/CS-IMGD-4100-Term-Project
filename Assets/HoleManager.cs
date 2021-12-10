using System.Collections.Generic;
using UnityEngine;

public class HoleManager : MonoBehaviour {
    public static HoleManager Instance;
    public List<GameObject> holes;
    private void Awake() {
        Instance = this;
    }
}