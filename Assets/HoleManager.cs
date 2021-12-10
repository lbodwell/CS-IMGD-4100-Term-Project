using System.Collections.Generic;
using UnityEngine;

public class HoleManager : MonoBehaviour {
    public static HoleManager Instance;
    public List<GameObject> holes;
    public Dictionary<int, float> floorMapping;
    private void Awake() {
        Instance = this;
        floorMapping = new Dictionary<int, float>();
        //get actual correct y values
        floorMapping.Add(1, -200);
        floorMapping.Add(2, -100);
        floorMapping.Add(3, 0);
        floorMapping.Add(4, 100);
        floorMapping.Add(5, 200); 
    }
}