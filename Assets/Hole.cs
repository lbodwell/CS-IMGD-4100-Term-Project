using UnityEngine;

public class Hole : MonoBehaviour {
    public int floorNumber;

    private void Start() {
        HoleManager.Instance.holes.Add(gameObject);
    }
}