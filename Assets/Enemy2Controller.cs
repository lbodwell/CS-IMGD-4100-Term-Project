using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Random = System.Random;

public class Enemy2Controller : MonoBehaviour {
    public enum EnemyState {
        Roaming,
        Turning,
        AbleToPush,
        Pushing,
        BeingPushed,
        Chasing,
        JumpingDown,
        ChasingAlly,
        SearchingAlly,
        CommsWithAllySelfInitiated,
        CommsWithAllyOtherInitiated,
        CommsWithAllyManipulatorInitiated,
        Boosting,
        BeingBoosted,
        ReturnToHoleBoosting,
        ReturnToHoleBeingBoosted,
    }

    public enum BoostStatus {
        Boosting,
        BeingBoosted,
        Rejection,
        Waiting,
        Undefined
    }

    public delegate void CommsResponse(GameObject sender, GameObject recipient, BoostStatus allyStatus);
    public delegate void CommsInitiated(GameObject sender, GameObject recipient, double allyWillingness);
    public delegate void BoostSuccess(GameObject sender, GameObject recipient);
    public delegate void Push(GameObject sender, GameObject recipient);

    public static event CommsResponse SendCommsResponse;
    public static event CommsInitiated InitiateComms;
    public static event BoostSuccess BoostSuccessful;
    public static event Push PushAlly;

    public NavMeshAgent agent;
    public EnemyState state;
    public BoostStatus boostStatus;
    public BoostStatus allyBoostStatus;
    public float playerDetectionRange = 75; // 50 -> 75
    public float pushRange = 15; // 25 -> 15
    public float holeDetectionRange = 100;
    public float communicationRange = 15; // 25 -> 15
    public float intersectionRadius = 25;
    public int currentFloor = 5;
    public bool isNearHole;
    public int enemyCheckCount = 3;
    public GameObject nearestDownwardHole;
    public GameObject nearestUpwardHole;

    private GameObject _player;
    private GameObject _target;
    private List<GameObject> _recentRejections;
    private Random _rand;
    private bool _isAtIntersection;
    private bool _isBeingPushed;
    private bool _isReceivingComms;
    private bool _isReceivingManipulatorComms;
    private bool _hasResponded;
    private bool _wasBoostSuccessful;
    private bool _isPushing;
    private bool _canPush;
    private double _allyWillingnessToBoost;
    private float _turnTimer;
    private float _pushTimer;


    private void Start() {
        _rand = new Random();
        _recentRejections = new List<GameObject>();
    }

    private void OnEnable() {
        SendCommsResponse += OnCommsResponse;
        InitiateComms += OnCommsInitiated;
        BoostSuccessful += OnBoostSuccessful;
        PushAlly += OnPush;
    }

    private void OnDisable() {
        SendCommsResponse -= OnCommsResponse;
        InitiateComms -= OnCommsInitiated;
        BoostSuccessful -= OnBoostSuccessful;
        PushAlly -= OnPush;
    }

    private void Update() {
        _player = PlayerManager.Instance.player;
        var playerPos = _player.transform.position;
        var playerDist = Vector3.Distance(playerPos, transform.position);

        var shortestHoleDist = holeDetectionRange + 1;
        isNearHole = false;
        nearestDownwardHole = null;
        foreach (var hole in HoleManager.Instance.holes) {
            var holeCollider = hole.GetComponent<HoleCollider>();
            var holePos = holeCollider.transform.position;
            var holeDist = Vector3.Distance(holePos, transform.position);
            if (holeDist < shortestHoleDist && hole.GetComponent<HoleCollider>().floorNumber == currentFloor) {
                shortestHoleDist = holeDist;
                if (holeDist < holeDetectionRange) {
                    isNearHole = true;
                }
                nearestDownwardHole = hole;
            }
        }

        switch (state) {
            case EnemyState.Roaming: {
                // TODO: tune this
                agent.SetDestination(transform.position + transform.forward * 5);

                if (!_canPush) {
                    foreach (var ally in EnemyManager.Instance.enemies) {
                        if (ally.GetInstanceID() != GetInstanceID()) {
                            var allyPos = ally.transform.position;
                            var allyDist = Vector3.Distance(allyPos, transform.position);
                            // Create re-usable interfaces for controllers for other AI implementations
                            var allyController = ally.GetComponent<Enemy2Controller>();
                            if (allyDist < pushRange && allyController.currentFloor == currentFloor) {
                                var hole = allyController.nearestDownwardHole;
                                if (allyController.isNearHole && hole != null) {
                                    var allyDirection = Vector3.Angle(allyPos, transform.position);
                                    var holeDirection = Vector3.Angle(hole.transform.position, allyPos);

                                    if (Math.Abs(allyDirection - holeDirection) < 5) {
                                        // Ally is within certain range of us
                                        // Ally is within certain range of hole
                                        // Angle between ally, hole, and us is a straight line
                                        _canPush = true;
                                        _target = ally;
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }

                if (_isBeingPushed) {
                    state = EnemyState.BeingPushed;
                } else if (_isAtIntersection && (_rand.NextDouble() > 0.5 || IsWallInFront())) {
                    state = EnemyState.Turning;
                } else if (playerDist < playerDetectionRange && _player.GetComponent<PlayerController>().currentFloor == currentFloor) {
                     _target = _player;
                    state = EnemyState.Chasing;
                } else if (_isReceivingComms) {
                    _isReceivingComms = false;
                    state = EnemyState.CommsWithAllyOtherInitiated;
                } else if (_isReceivingManipulatorComms) {
                    _isReceivingManipulatorComms = false;
                    state = EnemyState.CommsWithAllyManipulatorInitiated;
                } else if (_canPush) {
                    state = EnemyState.AbleToPush;
                } else {
                    print("dist: " + playerDist + ", range: " + playerDetectionRange);
                }

                break;
        }

            case EnemyState.Turning: {
                if (Time.time > _turnTimer) {
                    if (_rand.Next(0, 1) == 0) {
                        transform.rotation = Quaternion.Euler(new Vector3(transform.rotation.x, transform.rotation.y + 90, transform.rotation.z));
                        if (IsWallInFront()) {
                            transform.rotation = Quaternion.Euler(new Vector3(transform.rotation.x, transform.rotation.y + 180, transform.rotation.z));
                        }
                        else if(checkEnemyCount() > enemyCheckCount) {
                          transform.rotation = Quaternion.Euler(new Vector3(transform.rotation.x, transform.rotation.y - 90, transform.rotation.z));
                        }
                    } else {
                        transform.rotation = Quaternion.Euler(new Vector3(transform.rotation.x, transform.rotation.y - 90, transform.rotation.z));
                        if (IsWallInFront()) {
                            transform.rotation = Quaternion.Euler(new Vector3(transform.rotation.x, transform.rotation.y + 180, transform.rotation.z));
                        }
                        else if(checkEnemyCount() > enemyCheckCount) {
                          transform.rotation = Quaternion.Euler(new Vector3(transform.rotation.x, transform.rotation.y + 90, transform.rotation.z));
                        }
                    }
                    _turnTimer = Time.time + 3; // 1 -> 3
                }

                state = EnemyState.Roaming;

                break;
            }

            case EnemyState.BeingPushed: {
                if (nearestDownwardHole != null) {
                    agent.SetDestination(nearestDownwardHole.transform.position);
                } else {
                    state = EnemyState.Roaming;
                }

                // float comparison
                if (transform.position.y == HoleManager.Instance.floorMapping[currentFloor - 1]) {
                    state = EnemyState.Roaming;
                    currentFloor--;
                }

                break;
            }

            case EnemyState.AbleToPush: {
                if (_rand.NextDouble() < 0.25 && Time.time > _pushTimer) { // 0.5 -> 0.25
                    state = EnemyState.Pushing;
                    _pushTimer = Time.time + 3; // increase pushTimer wait to something more than 3?
                } else {
                    state = EnemyState.Roaming;
                    _pushTimer = Time.time + 1;
                }

                break;
            }

            case EnemyState.Pushing: {
                if (!_isPushing) {
                    if (_target != null) {
                        agent.SetDestination(_target.transform.position);
                    } else {
                        state = EnemyState.Roaming;
                    }

                    PushAlly(gameObject, _target);
                    _isPushing = true;
                }

                if (transform.position == agent.destination) {
                    _isPushing = false;
                    state = EnemyState.Roaming;
                }

                break;
            }

            case EnemyState.Chasing: {
                agent.SetDestination(_player.transform.position);

                if (playerDist < playerDetectionRange && _player.GetComponent<PlayerController>().currentFloor == currentFloor + 1) {
                    var shortestUpHoleDist = float.MaxValue;
                    nearestUpwardHole = null;
                    foreach (var hole in HoleManager.Instance.holes) {
                        var holeCollider = hole.GetComponent<HoleCollider>();
                        var holePos = holeCollider.transform.position;
                        var holeDist = Vector3.Distance(holePos, transform.position);
                        if (holeDist < shortestUpHoleDist && hole.GetComponent<HoleCollider>().floorNumber == currentFloor + 1) {
                            shortestUpHoleDist = holeDist;
                            nearestUpwardHole = hole;
                        }
                    }

                    state = EnemyState.SearchingAlly;
                } else if (playerDist < playerDetectionRange && _player.GetComponent<PlayerController>().currentFloor == currentFloor - 1) {
                    state = EnemyState.JumpingDown;
                } else if (!(playerDist < playerDetectionRange && _player.GetComponent<PlayerController>().currentFloor == currentFloor)) {
                    state = EnemyState.Roaming;
                } else if (transform.position == _target.transform.position) {
                    //game over
                }

                break;
            }

            case EnemyState.JumpingDown: {
                if (nearestDownwardHole != null) {
                    agent.SetDestination(nearestDownwardHole.transform.position);
                } else {
                    state = EnemyState.Roaming;
                }

                // float comparison
                if (transform.position.x == nearestDownwardHole.transform.position.x &&
                transform.position.z == nearestDownwardHole.transform.position.z &&
                transform.position.y == HoleManager.Instance.floorMapping[currentFloor - 1]) {
                    state = EnemyState.Roaming;
                }
                break;
            }

            case EnemyState.SearchingAlly: {
                var shortestDist = float.MaxValue;
                GameObject closestAlly = null;

                foreach (var ally in EnemyManager.Instance.enemies) {
                    if (!_recentRejections.Contains(ally)) {
                        var allyDist = Vector3.Distance(transform.position, ally.transform.position);
                        if (allyDist < shortestDist && ally.GetComponent<Enemy1Controller>().currentFloor == currentFloor) { // add rand likeliness to this? or change shortest distance range?
                            shortestDist = allyDist;
                            closestAlly = ally;
                        }
                    }
                }

                if (closestAlly != null) {
                    _target = closestAlly;
                    state = EnemyState.Chasing;
                } else {
                    nearestUpwardHole = null;
                    state = EnemyState.Roaming;
                }

                break;
            }

            case EnemyState.ChasingAlly: {
                if (_isBeingPushed) {
                    state = EnemyState.BeingPushed;
                } else {
                    // Set nav mesh destination to target position

                    var allyDist = Vector3.Distance(_target.transform.position, transform.position);
                    if (allyDist < communicationRange) {
                        state = EnemyState.CommsWithAllySelfInitiated;
                    }
                }

                break;
            }

            case EnemyState.CommsWithAllySelfInitiated: {
                if (boostStatus == BoostStatus.Undefined) {
                    // 0.5-0.8
                    var willingness = _rand.NextDouble() * 0.3 + 0.5; // increase this to increase willingness to boost?
                    // Send event with reference to ourself, reference to target, and willingness value
                    InitiateComms(gameObject, _target, willingness); // check how this works with willingness
                    boostStatus = BoostStatus.Waiting;
                } else if (allyBoostStatus == BoostStatus.Boosting) {
                    boostStatus = BoostStatus.BeingBoosted;
                    _recentRejections.Clear();
                    state = EnemyState.ReturnToHoleBeingBoosted;
                } else if (allyBoostStatus == BoostStatus.BeingBoosted) {
                    boostStatus = BoostStatus.Boosting;
                    _recentRejections.Clear();
                    state = EnemyState.ReturnToHoleBoosting;
                } else if (allyBoostStatus == BoostStatus.Rejection) {
                    boostStatus = BoostStatus.Rejection;
                    _recentRejections.Add(_target);
                    state = EnemyState.Roaming;
                }

                break;
            }

            case EnemyState.ReturnToHoleBoosting: {
                agent.SetDestination(nearestUpwardHole.transform.position);

                // float comparison
                if (transform.position.x == nearestUpwardHole.transform.position.x &&
                    transform.position.z == nearestUpwardHole.transform.position.z) {
                    state = EnemyState.Boosting;
                }

                break;
            }

            case EnemyState.Boosting: {
                if (_wasBoostSuccessful) {
                    _wasBoostSuccessful = false;
                    boostStatus = BoostStatus.Undefined;
                    allyBoostStatus = BoostStatus.Undefined;
                    state = EnemyState.Roaming;
                }

                break;
            }

            case EnemyState.ReturnToHoleBeingBoosted: {
                agent.SetDestination(nearestUpwardHole.transform.position);

                // float comparison
                if (transform.position.x == nearestUpwardHole.transform.position.x &&
                    transform.position.z == nearestUpwardHole.transform.position.z) {
                    state = EnemyState.BeingBoosted;
                }

                break;
            }

            case EnemyState.BeingBoosted: {
                // if using physical holes, move to side so it won't fall back down immediately
                transform.position = new Vector3(transform.position.x, HoleManager.Instance.floorMapping[currentFloor + 1], transform.position.z);
                currentFloor++;
                BoostSuccessful(gameObject, _target);
                state = EnemyState.Roaming;

                break;
            }

            case EnemyState.CommsWithAllyOtherInitiated: {
                if (!_hasResponded) {
                    // 0.5-0.8
                    var willingness = _rand.NextDouble() * 0.3 + 0.5; // increase this willingness to make sure it goees into the boosting if more?
                    if (willingness > _rand.NextDouble()) {
                        boostStatus = BoostStatus.Boosting;
                    } else if (_allyWillingnessToBoost > _rand.NextDouble()) {
                        boostStatus = BoostStatus.BeingBoosted;
                    } else {
                        boostStatus = BoostStatus.Rejection;
                    }

                    SendCommsResponse(gameObject, _target, boostStatus);
                    _hasResponded = true;

                    switch (boostStatus) {
                        case BoostStatus.Boosting: {
                            state = EnemyState.ReturnToHoleBoosting;

                            break;
                        }

                        case BoostStatus.BeingBoosted: {
                            state = EnemyState.ReturnToHoleBeingBoosted;

                            break;
                        }

                        default: {
                            boostStatus = BoostStatus.Undefined;
                            state = EnemyState.Roaming;

                            break;
                        }
                    }
                }

                break;
            }

            case EnemyState.CommsWithAllyManipulatorInitiated: { // Built for enemy 3 purposes, not being used right now
                if (!_hasResponded) {
                    // 0.3-0.5
                    var willingness = _rand.NextDouble() * 0.2 + 0.3; // willingness increase again?
                    boostStatus = willingness > _rand.NextDouble() ? BoostStatus.Boosting : BoostStatus.Rejection;

                    SendCommsResponse(gameObject, _target, boostStatus);
                    _hasResponded = true;

                    if (boostStatus == BoostStatus.Boosting) {
                        state = EnemyState.ReturnToHoleBoosting;
                    } else {
                        boostStatus = BoostStatus.Undefined;
                        state = EnemyState.Roaming;
                    }
                }

                break;
            }

            default: {
                state = EnemyState.Roaming;

                break;
            }
        }
    }

    private bool IsWallInFront() {
        print("checking for wall");
        const int layerMask = 1 << 6;

        if (Physics.Raycast(transform.position, transform.forward, out var hit, 20, layerMask)) {
            Debug.DrawRay(transform.position, transform.forward * hit.distance, Color.yellow);
            Debug.Log("Wall detected");
            return true;
        }

        Debug.DrawRay(transform.position, transform.forward * 20, Color.white);
        Debug.Log("No wall detected");

        return false;
    }

    private int checkEnemyCount() {
        int enemyCount = 0;
        foreach (var ally in EnemyManager.Instance.enemies) {
            if (ally.GetInstanceID() != GetInstanceID()) {
                var allyPos = ally.transform.position;
                var allyDist = Vector3.Distance(allyPos, transform.position);
                // Create re-usable interfaces for controllers for other AI implementations
                var allyController = ally.GetComponent<Enemy2Controller>();
                if (allyDist < pushRange && allyController.currentFloor == currentFloor) {
                  enemyCount++;
                }
            }
        }
        return enemyCount;
    }

    private void OnCollisionEnter(Collision other) {
        if (other.gameObject.CompareTag("Intersection")) {
            _isAtIntersection = true;
        }
    }

    private void OnCollisionExit(Collision other) {
        if (other.gameObject.CompareTag("Intersection")) {
            _isAtIntersection = false;
        }
    }

    private void OnCommsResponse(GameObject sender, GameObject recipient, BoostStatus allyStatus) {
        if (gameObject.GetInstanceID() == recipient.gameObject.GetInstanceID()) {
            allyBoostStatus = allyStatus;
        }
    }

    private void OnCommsInitiated(GameObject sender, GameObject recipient, double allyWillingness) {
        if (gameObject.GetInstanceID() == recipient.gameObject.GetInstanceID()) {
            _allyWillingnessToBoost = allyWillingness;
            _isReceivingComms = true;
        }
    }

    private void OnCommsInitiatedByManipulator(GameObject sender, GameObject recipient, double allyWillingness) {
        if (gameObject.GetInstanceID() == recipient.gameObject.GetInstanceID()) {
            _allyWillingnessToBoost = allyWillingness;
            _isReceivingManipulatorComms = true;
        }
    }

    private void OnBoostSuccessful(GameObject sender, GameObject recipient) {
        if (gameObject.GetInstanceID() == recipient.gameObject.GetInstanceID()) {
            _wasBoostSuccessful = true;
        }
    }

    private void OnPush(GameObject sender, GameObject recipient) {
        if (gameObject.GetInstanceID() == recipient.gameObject.GetInstanceID()) {
            _isBeingPushed = true;
        }
    }
}
