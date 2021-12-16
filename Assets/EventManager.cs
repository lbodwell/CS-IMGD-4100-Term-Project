using System;
using UnityEngine;

public class EventManager : MonoBehaviour {
    public static EventManager Instance;
    private void Awake() {
        Instance = this;
    }

    public event Action<GameObject, GameObject, EnemyController.BoostStatus> OnCommsResponse;
    public event Action<GameObject, GameObject, GameObject, double> OnCommsInitiated;
    public event Action<GameObject> OnBoostSuccessful;
    public event Action<GameObject> OnPush;

    public void SendCommsResponse(GameObject sender, GameObject recipient, EnemyController.BoostStatus allyStatus) {
        OnCommsResponse?.Invoke(sender, recipient, allyStatus);
    }

    public void InitiateComms(GameObject sender, GameObject recipient, GameObject targetHole, double allyWillingness) {
        OnCommsInitiated?.Invoke(sender, recipient, targetHole, allyWillingness);
    }

    public void ReportBoostSuccess(GameObject recipient) {
        OnBoostSuccessful?.Invoke(recipient);
    }

    public void PushAlly(GameObject recipient) {
        OnPush?.Invoke(recipient);
    }
}
