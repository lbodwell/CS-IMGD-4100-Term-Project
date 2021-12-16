using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Random = System.Random;

public class Enemy1Controller : MonoBehaviour {
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
    public GameObject nearestDownwardHole;
    public GameObject nearestUpwardHole;
    public float playerDetectionRange = 50;
    public float pushRange = 25;
    public float holeDetectionRange = 100;
    public float communicationRange = 25;
    public int currentFloor = 5;
    public bool isNearHole;

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
                agent.SetDestination(transform.position + transform.forward);

                if (!_canPush) {
                    foreach (var ally in EnemyManager.Instance.enemies) {
                        if (ally.GetInstanceID() != GetInstanceID()) {
                            var allyPos = ally.transform.position;
                            var allyDist = Vector3.Distance(allyPos, transform.position);
                            var allyController = ally.GetComponent<Enemy1Controller>();
                            if (allyDist < pushRange && allyController.currentFloor == currentFloor) {
                                var hole = allyController.nearestDownwardHole;
                                if (allyController.isNearHole && hole != null) {
                                    var allyDirection = Vector3.Angle(allyPos, transform.position);
                                    var holeDirection = Vector3.Angle(hole.transform.position, allyPos);

                                    if (Math.Abs(allyDirection - holeDirection) < 5) {
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
                    Debug.Log("State of enemy is changing from Roaming to BeingPushed.");
                    state = EnemyState.BeingPushed;
                } else if ((_isAtIntersection && _rand.NextDouble() > 0.5) || IsWallInFront() != 0) {
                    //Debug.Log("State of enemy is changing from Roaming to Turning.");
                    state = EnemyState.Turning;
                } else if (playerDist < playerDetectionRange && _player.GetComponent<PlayerController>().currentFloor == currentFloor) {
                     _target = _player;
                     Debug.Log("State of enemy is changing from Roaming to Chasing.");
                    state = EnemyState.Chasing;
                } else if (_isReceivingComms) {
                    _isReceivingComms = false;
                    Debug.Log("State of enemy is changing from Roaming to CommsWithAllyOtherInitiated.");
                    state = EnemyState.CommsWithAllyOtherInitiated;
                } else if (_isReceivingManipulatorComms) {
                    _isReceivingManipulatorComms = false;
                    Debug.Log("State of enemy is changing from Roaming to CommsWithAllyManipulatorInitiated.");
                    state = EnemyState.CommsWithAllyManipulatorInitiated;
                } else if (_canPush) {
                    Debug.Log("State of enemy is changing from Roaming to AbleToPush.");
                    state = EnemyState.AbleToPush;
                } else {
                    // For debugging
                    //print("dist: " + playerDist + ", range: " + playerDetectionRange);
                }

                break;
        }
            
            case EnemyState.Turning: {
                if (Time.time > _turnTimer) {
                    Debug.Log("Time to turn.");
                    if (_rand.Next(0, 2) == 0) {
                        transform.rotation = Quaternion.Euler(new Vector3(0, transform.rotation.y + 90, 0));
                        Debug.Log(transform.localRotation.eulerAngles.y);
                        //transform.Rotate(new Vector3 (0, 90, 0));
                        Debug.Log(transform.localRotation.eulerAngles.y);
                        //transform.rotation *= Quaternion.Euler(0, 90*Time.deltaTime, 0);
                        if (IsWallInFront() != 0) {
                            transform.rotation = Quaternion.Euler(new Vector3(0, transform.rotation.y + 180, 0));
                            Debug.Log(transform.localRotation.eulerAngles.y);
                            //transform.Rotate(new Vector3 (0, 180, 0));
                            Debug.Log(transform.localRotation.eulerAngles.y);
                            //transform.rotation *= Quaternion.Euler(0, 180*Time.deltaTime, 0);
                        }
                    } else {
                        transform.rotation = Quaternion.Euler(new Vector3(0, transform.rotation.y - 90, 0));
                        Debug.Log(transform.localRotation.eulerAngles.y);
                        //transform.Rotate(new Vector3 (0, -90, 0));
                        Debug.Log(transform.localRotation.eulerAngles.y);
                        //transform.rotation *= Quaternion.Euler(0, -90*Time.deltaTime, 0);
                        if (IsWallInFront() != 0) {
                            transform.rotation = Quaternion.Euler(new Vector3(0, transform.rotation.y + 180, 0));
                            Debug.Log(transform.localRotation.eulerAngles.y);
                            //transform.Rotate(new Vector3 (0, 180, 0));
                            Debug.Log(transform.localRotation.eulerAngles.y);
                            //transform.rotation *= Quaternion.Euler(0, 180*Time.deltaTime, 0);
                        }
                    }
                    _turnTimer = Time.time + 5;
                }
                
               // Debug.Log("State of enemy is changing from Turning to Roaming.");
                state = EnemyState.Roaming;
                
                break;
            }

            case EnemyState.BeingPushed: {
                if (nearestDownwardHole != null) {
                    agent.SetDestination(nearestDownwardHole.transform.position);
                    
                    // fix in enemy 2
                    if (Math.Abs(transform.position.x - nearestDownwardHole.transform.position.x) < 5 && 
                        Math.Abs(transform.position.z - nearestDownwardHole.transform.position.z) < 5 && 
                        currentFloor == nearestDownwardHole.GetComponent<HoleCollider>().floorNumber) {
                        transform.position = new Vector3(transform.position.x, transform.position.y - 15, transform.position.z);
                        currentFloor--;
                        Debug.Log("State of enemy is changing from BeingPushed to Roaming.");
                        state = EnemyState.Roaming;
                    }
                } else {
                    Debug.Log("State of enemy is changing from BeingPushed to Roaming.");
                    state = EnemyState.Roaming;
                }

                break;
            }

            case EnemyState.AbleToPush: {
                if (_rand.NextDouble() < 0.5 && Time.time > _pushTimer) {
                    Debug.Log("State of enemy is changing from AbleToPush to Pushing.");
                    state = EnemyState.Pushing;
                    _pushTimer = Time.time + 3;
                } else {
                    Debug.Log("State of enemy is changing from AbleToPush to Roaming.");
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
                        Debug.Log("State of enemy is changing from Pushing to Roaming.");
                        state = EnemyState.Roaming;
                    }
                    
                    PushAlly(gameObject, _target);
                    _isPushing = true;
                }
                
                if (transform.position == agent.destination) {
                    _isPushing = false;
                    Debug.Log("State of enemy is changing from Pushing to Roaming.");
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
                    
                    Debug.Log("State of enemy is changing from Chasing to SearchingAlly.");
                    state = EnemyState.SearchingAlly;
                } else if (playerDist < playerDetectionRange && _player.GetComponent<PlayerController>().currentFloor == currentFloor - 1) {
                    Debug.Log("State of enemy is changing from Chasing to JumpingDown.");
                    state = EnemyState.JumpingDown;
                } else if (!(playerDist < playerDetectionRange && _player.GetComponent<PlayerController>().currentFloor == currentFloor)) {
                    Debug.Log("State of enemy is changing from Chasing to Roaming.");
                    transform.rotation = Quaternion.Euler(new Vector3(0, 0, 0));
                    if (IsWallInFront() != 0) {
                        state = EnemyState.Turning;
                    }
                    else {
                        state = EnemyState.Roaming;
                    }
                } else if (transform.position == _target.transform.position) {
                    //game over
                }
                
                break;
            }

            case EnemyState.JumpingDown: {
                if (nearestDownwardHole != null) {
                    agent.SetDestination(nearestDownwardHole.transform.position);
                    
                    // fix this in enemy 2
                    if (Math.Abs(transform.position.x - nearestDownwardHole.transform.position.x) < 5 && 
                        Math.Abs(transform.position.z - nearestDownwardHole.transform.position.z) < 5 && 
                        currentFloor == nearestDownwardHole.GetComponent<HoleCollider>().floorNumber) {
                        transform.position = new Vector3(transform.position.x, transform.position.y - 15, transform.position.z);
                        currentFloor--;
                        Debug.Log("State of enemy is changing from JumpingDown to Roaming.");
                        state = EnemyState.Roaming;
                    }
                } else {
                    Debug.Log("State of enemy is changing from JumpingDown to Roaming.");
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
                        if (allyDist < shortestDist && ally.GetComponent<Enemy1Controller>().currentFloor == currentFloor) {
                            shortestDist = allyDist;
                            closestAlly = ally;
                        }
                    }
                }

                if (closestAlly != null) {
                    _target = closestAlly;
                    Debug.Log("State of enemy is changing from SearchingAlly to ChasingAlly.");
                    state = EnemyState.ChasingAlly;
                } else {
                    nearestUpwardHole = null;
                    Debug.Log("State of enemy is changing from SearchingAlly to Roaming.");
                    state = EnemyState.Roaming;
                }
                
                break;
            }

            case EnemyState.ChasingAlly: {
                if (_isBeingPushed) {
                    Debug.Log("State of enemy is changing from ChasingAlly to BeingPushed.");
                    state = EnemyState.BeingPushed;
                } else {
                    var allyPos = _target.transform.position;
                    agent.SetDestination(allyPos);
                    
                    var allyDist = Vector3.Distance(allyPos, transform.position);
                    if (allyDist < communicationRange) {
                        Debug.Log("State of enemy is changing from ChasingAlly to CommsWithAllySelfInitiated.");
                        state = EnemyState.CommsWithAllySelfInitiated;
                    }
                }

                break;
            }

            case EnemyState.CommsWithAllySelfInitiated: {
                if (boostStatus == BoostStatus.Undefined) {
                    // 0.3-0.5
                    var willingness = _rand.NextDouble() * 0.2 + 0.3;
                    // Send event with reference to ourself, reference to target, and willingness value
                    InitiateComms(gameObject, _target, willingness);
                    boostStatus = BoostStatus.Waiting;
                } else if (allyBoostStatus == BoostStatus.Boosting) {
                    boostStatus = BoostStatus.BeingBoosted;
                    _recentRejections.Clear();
                    Debug.Log("State of enemy is changing from CommsWithAllySelfInitiated to ReturnToHoleBeingBoosted.");
                    state = EnemyState.ReturnToHoleBeingBoosted;
                } else if (allyBoostStatus == BoostStatus.BeingBoosted) {
                    boostStatus = BoostStatus.Boosting;
                    _recentRejections.Clear();
                    Debug.Log("State of enemy is changing from CommsWithAllySelfInitiated to ReturnToHoleBoosting.");
                    state = EnemyState.ReturnToHoleBoosting;
                } else if (allyBoostStatus == BoostStatus.Rejection) {
                    boostStatus = BoostStatus.Rejection;
                    _recentRejections.Add(_target);
                    Debug.Log("State of enemy is changing from CommsWithAllySelfInitiated to Roaming.");
                    state = EnemyState.Roaming;
                }
                
                break;
            }

            case EnemyState.ReturnToHoleBoosting: {
                agent.SetDestination(nearestUpwardHole.transform.position);
                
                // fix in enemy 2
                if (Math.Abs(transform.position.x - nearestUpwardHole.transform.position.x) < 5 && 
                    Math.Abs(transform.position.z - nearestUpwardHole.transform.position.z) < 5 &&
                    nearestUpwardHole.GetComponent<HoleCollider>().floorNumber == currentFloor + 1) {
                    Debug.Log("State of enemy is changing from ReturnToHoleBoosting to Boosting.");
                    state = EnemyState.Boosting;
                }
                
                break;
            }

            case EnemyState.Boosting: {
                if (_wasBoostSuccessful) {
                    _wasBoostSuccessful = false;
                    boostStatus = BoostStatus.Undefined;
                    allyBoostStatus = BoostStatus.Undefined;
                    Debug.Log("State of enemy is changing from Boosting to Roaming.");
                    state = EnemyState.Roaming;
                }
                
                break;
            }

            case EnemyState.ReturnToHoleBeingBoosted: {
                agent.SetDestination(nearestUpwardHole.transform.position);
                
                // float comparison
                if (Math.Abs(transform.position.x - nearestUpwardHole.transform.position.x) < 5 && 
                    Math.Abs(transform.position.z - nearestUpwardHole.transform.position.z) < 5 &&
                    nearestUpwardHole.GetComponent<HoleCollider>().floorNumber == currentFloor + 1) {
                    Debug.Log("State of enemy is changing from ReturnToHoleBeingBoosted to BeingBoosted.");
                    state = EnemyState.BeingBoosted;
                }
                
                break;
            }

            case EnemyState.BeingBoosted: {
                // move to side so it won't fall back down immediately
                transform.position = new Vector3(transform.position.x, transform.position.y + 15, transform.position.z);
                currentFloor++;
                BoostSuccessful(gameObject, _target);
                Debug.Log("State of enemy is changing from BeingBoosted to Roaming.");
                state = EnemyState.Roaming;
                
                break;
            }

            case EnemyState.CommsWithAllyOtherInitiated: {
                if (!_hasResponded) {
                    // 0.3-0.5
                    var willingness = _rand.NextDouble() * 0.2 + 0.3;
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
                            Debug.Log("State of enemy is changing from CommsWithAllyOtherInitiated to ReturnToHoleBoosting.");
                            state = EnemyState.ReturnToHoleBoosting;
                        
                            break;
                        }
                    
                        case BoostStatus.BeingBoosted: {
                            Debug.Log("State of enemy is changing from CommsWithAllyOtherInitiated to ReturnToHoleBeingBoosted.");
                            state = EnemyState.ReturnToHoleBeingBoosted;
                        
                            break;
                        }
                    
                        default: {
                            boostStatus = BoostStatus.Undefined;
                            Debug.Log("State of enemy is changing from CommsWithAllyOtherInitiated to Roaming.");
                            state = EnemyState.Roaming;
                        
                            break;
                        }
                    }
                }

                break;
            }

            case EnemyState.CommsWithAllyManipulatorInitiated: {
                if (!_hasResponded) {
                    // 0.3-0.5
                    var willingness = _rand.NextDouble() * 0.2 + 0.3;
                    boostStatus = willingness > _rand.NextDouble() ? BoostStatus.Boosting : BoostStatus.Rejection;
                    
                    SendCommsResponse(gameObject, _target, boostStatus);
                    _hasResponded = true;

                    if (boostStatus == BoostStatus.Boosting) {
                        Debug.Log("State of enemy is changing from CommsWithAllyManipulatorInitiated to ReturnToHoleBoosting.");
                        state = EnemyState.ReturnToHoleBoosting;
                    } else {
                        boostStatus = BoostStatus.Undefined;
                        Debug.Log("State of enemy is changing from CommsWithAllyManipulatorInitiated to Roaming.");
                        state = EnemyState.Roaming;
                    }
                }

                break;
            }
            
            default: {
                Debug.Log("State of enemy is changing from Default to Roaming.");
                state = EnemyState.Roaming;
                
                break;
            }
        }
    }

    private float IsWallInFront() {
        //print("checking for wall");
        const int layerMask = 1 << 6;

        if (Physics.Raycast(transform.position, transform.forward, out var hit, 5, layerMask)) {
            //Debug.Log("Wall detected");
            return hit.distance;
        }
        //Debug.Log("No wall detected");
        
        return 0;
    }

    private void OnTriggerEnter(Collider other) {
        //Debug.Log("Trigger.");
        if (other.CompareTag("Intersection")) {
            _isAtIntersection = true;
            Debug.Log("At intersection");
        }
    }

    private void OnTriggerExit(Collider other) {
        //Debug.Log("Trigger End.");
        if (other.CompareTag("Intersection")) {
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