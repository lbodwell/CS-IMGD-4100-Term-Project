using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Random = System.Random;

public class EnemyController : MonoBehaviour {
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
    public Enemy.EnemyType enemyType;
    public GameObject nearestDownwardHole;
    public GameObject nearestUpwardHole;
    public float playerDetectionRange = 50;
    public float pushRange = 25;
    public float holeDetectionRange = 100;
    public float communicationRange = 25;
    public int currentFloor = 3;
    public int enemyCheckCount = 3;
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
        _player = PlayerManager.Instance.player;
        EnemyManager.Instance.addEnemy(gameObject, enemyType);
        
        if (enemyType == Enemy.EnemyType.Type2) {
            playerDetectionRange = 75;
            pushRange = 15;
            communicationRange = 15;
        }
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
        var playerPos = _player.transform.position;
        var playerDist = Vector3.Distance(playerPos, transform.position);

        var shortestHoleDist = holeDetectionRange + 1;
        isNearHole = false;
        nearestDownwardHole = null;
        foreach (var hole in HoleManager.Instance.holes) {
            var holeCollider = hole.GetComponent<Hole>();
            var holePos = holeCollider.transform.position;
            var holeDist = Vector3.Distance(holePos, transform.position);
            if (holeDist < shortestHoleDist && hole.GetComponent<Hole>().floorNumber == currentFloor) {
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
                        var allyObj = ally.obj;
                        var allyType = ally.type;
                        
                        if (allyObj.GetInstanceID() != GetInstanceID()) {
                            var allyPos = allyObj.transform.position;
                            var allyDist = Vector3.Distance(allyPos, transform.position);

                            if (allyType == Enemy.EnemyType.Type1) {
                                var allyController = allyObj.GetComponent<EnemyController>();
                                if (allyDist < pushRange && allyController.currentFloor == currentFloor) {
                                    var hole = allyController.nearestDownwardHole;
                                    if (allyController.isNearHole && hole != null) {
                                        var allyDirection = Vector3.Angle(allyPos, transform.position);
                                        var holeDirection = Vector3.Angle(hole.transform.position, allyPos);

                                        if (Math.Abs(allyDirection - holeDirection) < 5) {
                                            _canPush = true;
                                            _target = allyObj;
                                            break;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                if (_isBeingPushed) {
                    state = EnemyState.BeingPushed;
                } else if ((_isAtIntersection && _rand.NextDouble() > 0.5) || IsWallInFront()) {
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
                }

                break;
        }
            
            case EnemyState.Turning: {
                if (Time.time > _turnTimer) {
                    if (_rand.Next(0, 2) == 0) {
                        transform.rotation = Quaternion.Euler(new Vector3(transform.rotation.x, transform.rotation.y + 90, transform.rotation.z));
                        if (IsWallInFront()) {
                            transform.rotation = Quaternion.Euler(new Vector3(transform.rotation.x, transform.rotation.y + 180, transform.rotation.z));
                        } else if (enemyType == Enemy.EnemyType.Type2 && CheckEnemyCount() > enemyCheckCount) {
                            transform.rotation = Quaternion.Euler(new Vector3(transform.rotation.x, transform.rotation.y - 90, transform.rotation.z));
                        }
                    } else {
                        transform.rotation = Quaternion.Euler(new Vector3(transform.rotation.x, transform.rotation.y - 90, transform.rotation.z));
                        if (IsWallInFront()) {
                            transform.rotation = Quaternion.Euler(new Vector3(transform.rotation.x, transform.rotation.y + 180, transform.rotation.z));
                        } else if(CheckEnemyCount() > enemyCheckCount) {
                            transform.rotation = Quaternion.Euler(new Vector3(transform.rotation.x, transform.rotation.y + 90, transform.rotation.z));
                        }
                    }

                    _turnTimer = Time.time + (enemyType == Enemy.EnemyType.Type2 ? 3 : 1);
                }
                
                state = EnemyState.Roaming;
                
                break;
            }

            case EnemyState.BeingPushed: {
                if (nearestDownwardHole != null) {
                    agent.SetDestination(nearestDownwardHole.transform.position);
                    
                    if (Math.Abs(transform.position.x - nearestDownwardHole.transform.position.x) < 5 && 
                        Math.Abs(transform.position.z - nearestDownwardHole.transform.position.z) < 5 && 
                        currentFloor == nearestDownwardHole.GetComponent<Hole>().floorNumber) {
                        transform.position = new Vector3(transform.position.x, transform.position.y - 9, transform.position.z);
                        currentFloor--;
                        state = EnemyState.Roaming;
                    }
                } else {
                    state = EnemyState.Roaming;
                }

                break;
            }

            case EnemyState.AbleToPush: {
                if (_rand.NextDouble() < (enemyType == Enemy.EnemyType.Type2 ? 0.25 : 0.5) && Time.time > _pushTimer) {
                    state = EnemyState.Pushing;
                    _pushTimer = Time.time + (enemyType == Enemy.EnemyType.Type2 ? 5 : 3);
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
                        var holeCollider = hole.GetComponent<Hole>();
                        var holePos = holeCollider.transform.position;
                        var holeDist = Vector3.Distance(holePos, transform.position);
                        if (holeDist < shortestUpHoleDist && hole.GetComponent<Hole>().floorNumber == currentFloor + 1) {
                            shortestUpHoleDist = holeDist;
                            nearestUpwardHole = hole;
                        }
                    }
                    
                    state = EnemyState.SearchingAlly;
                } else if (playerDist < playerDetectionRange && _player.GetComponent<PlayerController>().currentFloor == currentFloor - 1) {
                    state = EnemyState.JumpingDown;
                } else if (!(playerDist < playerDetectionRange && _player.GetComponent<PlayerController>().currentFloor == currentFloor)) {
                    state = EnemyState.Roaming;
                } else if (Vector3.Distance(transform.position, _target.transform.position) < 5) {
                    print("Game over!");
                }
                
                break;
            }

            case EnemyState.JumpingDown: {
                if (nearestDownwardHole != null) {
                    agent.SetDestination(nearestDownwardHole.transform.position);
                    
                    if (Math.Abs(transform.position.x - nearestDownwardHole.transform.position.x) < 5 && 
                        Math.Abs(transform.position.z - nearestDownwardHole.transform.position.z) < 5 && 
                        currentFloor == nearestDownwardHole.GetComponent<Hole>().floorNumber) {
                        transform.position = new Vector3(transform.position.x, transform.position.y - 9, transform.position.z);
                        currentFloor--;
                        state = EnemyState.Roaming;
                    }
                } else {
                    state = EnemyState.Roaming;
                }

                break;
            }

            case EnemyState.SearchingAlly: {
                var shortestDist = float.MaxValue;
                GameObject closestAlly = null;
                
                foreach (var ally in EnemyManager.Instance.enemies) {
                    var allyObj = ally.obj;
                    
                    if (!_recentRejections.Contains(allyObj)) {
                        var allyDist = Vector3.Distance(transform.position, allyObj.transform.position);
                        if (allyDist < shortestDist && allyObj.GetComponent<EnemyController>().currentFloor == currentFloor) {
                            shortestDist = allyDist;
                            closestAlly = allyObj;
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
                    var allyPos = _target.transform.position;
                    agent.SetDestination(allyPos);
                    
                    var allyDist = Vector3.Distance(allyPos, transform.position);
                    if (allyDist < communicationRange) {
                        state = EnemyState.CommsWithAllySelfInitiated;
                    }
                }

                break;
            }

            case EnemyState.CommsWithAllySelfInitiated: {
                if (boostStatus == BoostStatus.Undefined) {
                    double willingness;
                    if (enemyType == Enemy.EnemyType.Type2) {
                        // 0.5-0.8
                        willingness = _rand.NextDouble() * 0.3 + 0.5;
                    } else {
                        // 0.3-0.5
                        willingness = _rand.NextDouble() * 0.2 + 0.3;
                    }
                    
                    InitiateComms(gameObject, _target, willingness);
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
                
                if (Math.Abs(transform.position.x - nearestUpwardHole.transform.position.x) < 5 && 
                    Math.Abs(transform.position.z - nearestUpwardHole.transform.position.z) < 5 &&
                    nearestUpwardHole.GetComponent<Hole>().floorNumber == currentFloor + 1) {
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
                
                if (Math.Abs(transform.position.x - nearestUpwardHole.transform.position.x) < 5 && 
                    Math.Abs(transform.position.z - nearestUpwardHole.transform.position.z) < 5 &&
                    nearestUpwardHole.GetComponent<Hole>().floorNumber == currentFloor + 1) {
                    state = EnemyState.BeingBoosted;
                }
                
                break;
            }

            case EnemyState.BeingBoosted: {
                transform.position = new Vector3(transform.position.x, transform.position.y + 9, transform.position.z);
                currentFloor++;
                BoostSuccessful(gameObject, _target);
                state = EnemyState.Roaming;
                
                break;
            }

            case EnemyState.CommsWithAllyOtherInitiated: {
                if (!_hasResponded) {
                    // 0.3-0.5
                    double willingness;
                    if (enemyType == Enemy.EnemyType.Type2) {
                        // 0.5-0.8
                        willingness = _rand.NextDouble() * 0.3 + 0.5;
                    } else {
                        // 0.3-0.5
                        willingness = _rand.NextDouble() * 0.2 + 0.3;
                    }
                    
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

            case EnemyState.CommsWithAllyManipulatorInitiated: {
                if (!_hasResponded) {
                    double willingness;
                    if (enemyType == Enemy.EnemyType.Type2) {
                        // 0.5-0.8
                        willingness = _rand.NextDouble() * 0.3 + 0.5;
                    } else {
                        // 0.3-0.5
                        willingness = _rand.NextDouble() * 0.2 + 0.3;
                    }
                    
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
        const int layerMask = 1 << 6;

        if (Physics.Raycast(transform.position, transform.forward, out var hit, 25, layerMask)) {
            Debug.DrawRay(transform.position, transform.forward * hit.distance, Color.yellow);
            
            return true;
        }

        Debug.DrawRay(transform.position, transform.forward * 50, Color.white);
        
        return false;
    }
    
    private int CheckEnemyCount() {
        var enemyCount = 0;

        foreach (var ally in EnemyManager.Instance.enemies) {
            var allyObj = ally.obj;
            if (allyObj.GetInstanceID() != GetInstanceID()) {
                var allyPos = allyObj.transform.position;
                var allyDist = Vector3.Distance(allyPos, transform.position);
                
                if (allyDist < pushRange && allyObj.GetComponent<EnemyController>().currentFloor == currentFloor) {
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

    public void OnCommsResponse(GameObject sender, GameObject recipient, BoostStatus allyStatus) {
        if (gameObject.GetInstanceID() == recipient.gameObject.GetInstanceID()) {
            allyBoostStatus = allyStatus;
        }
    }
    
    public void OnCommsInitiated(GameObject sender, GameObject recipient, double allyWillingness) {
        if (gameObject.GetInstanceID() == recipient.gameObject.GetInstanceID()) {
            _allyWillingnessToBoost = allyWillingness;
            _isReceivingComms = true;
        }
    }
    
    public void OnCommsInitiatedByManipulator(GameObject sender, GameObject recipient, double allyWillingness) {
        if (gameObject.GetInstanceID() == recipient.gameObject.GetInstanceID()) {
            _allyWillingnessToBoost = allyWillingness;
            _isReceivingManipulatorComms = true;
        }
    }
    
    public void OnBoostSuccessful(GameObject sender, GameObject recipient) {
        if (gameObject.GetInstanceID() == recipient.gameObject.GetInstanceID()) {
            _wasBoostSuccessful = true;
        }
    }

    public void OnPush(GameObject sender, GameObject recipient) {
        if (gameObject.GetInstanceID() == recipient.gameObject.GetInstanceID()) {
            _isBeingPushed = true;
        }
    }
}