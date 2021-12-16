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

    public NavMeshAgent agent;
    public EnemyState state;
    public BoostStatus boostStatus = BoostStatus.Undefined;
    public BoostStatus allyBoostStatus = BoostStatus.Undefined;
    public Enemy.EnemyType enemyType;
    public GameObject nearestDownwardHole;
    public GameObject nearestUpwardHole;
    public float playerDetectionRange = 50;
    public float pushRange = 25;
    public float communicationRange = 10;
    public float holeDetectionRange = 100;
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
        }
        
        EventManager.Instance.OnCommsResponse += OnCommsResponse;
        EventManager.Instance.OnCommsInitiated += OnCommsInitiated;
        EventManager.Instance.OnBoostSuccessful += OnBoostSuccessful;
        EventManager.Instance.OnPush += OnPush;
        EventManager.Instance.OnEcho += OnEcho;
    }

    private void OnDestroy() {
        EventManager.Instance.OnCommsResponse -= OnCommsResponse;
        EventManager.Instance.OnCommsInitiated -= OnCommsInitiated;
        EventManager.Instance.OnBoostSuccessful -= OnBoostSuccessful;
        EventManager.Instance.OnPush -= OnPush;
        EventManager.Instance.OnEcho -= OnEcho;
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
                agent.SetDestination(transform.position + transform.forward);

                if (!_canPush) {
                    foreach (var ally in EnemyManager.Instance.enemies) {
                        var allyObj = ally.obj;
                        var allyController = allyObj.GetComponent<EnemyController>();
                        
                        if (allyObj.GetInstanceID() != GetInstanceID() && allyController.state == EnemyState.Roaming) {
                            var allyPos = allyObj.transform.position;
                            var allyDist = Vector3.Distance(allyPos, transform.position);
                            
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

                if (_isBeingPushed) {
                    Debug.Log("State of enemy is changing from Roaming to BeingPushed.");
                    state = EnemyState.BeingPushed;
                } else if ((_isAtIntersection && _rand.NextDouble() > 0.5) || !IsWallInFront()) {
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
                } else if (_canPush) {
                    // Debug.Log("State of enemy is changing from Roaming to AbleToPush.");
                    state = EnemyState.AbleToPush;
                }

                break;
        }
            
            case EnemyState.Turning: {
                if (Time.time > _turnTimer) {
                    if (_rand.Next(0, 2) == 0) {
                        transform.Rotate(0, 90, 0);
                        if (!IsWallInFront()) {
                            transform.Rotate(0, 180, 0);
                        } else if (enemyType == Enemy.EnemyType.Type2 && CheckEnemyCount() > enemyCheckCount) {
                            transform.Rotate(0, -90, 0);
                        }
                    } else {
                        transform.Rotate(0, -90, 0);
                        if (!IsWallInFront()) {
                            transform.Rotate(0, 190, 0);
                        } else if (enemyType == Enemy.EnemyType.Type2 && CheckEnemyCount() > enemyCheckCount) {
                            transform.Rotate(0, 90, 0);
                        }
                    }

                    _turnTimer = Time.time + (enemyType == Enemy.EnemyType.Type2 ? 3 : 1);
                }
                
                //Debug.Log("State of enemy is changing from Turning to Roaming.");
                state = EnemyState.Roaming;
                
                break;
            }

            case EnemyState.BeingPushed: {
                if (nearestDownwardHole != null) {
                    var holePos = nearestDownwardHole.transform.position;
                    agent.Warp(new Vector3(holePos.x, holePos.y - 9, holePos.z));
                    currentFloor--;
                    _isBeingPushed = false;
                }

                Debug.Log("State of enemy is changing from BeingPushed to Roaming.");
                state = EnemyState.Roaming;

                break;
            }

            case EnemyState.AbleToPush: {
                if (_rand.NextDouble() < (enemyType == Enemy.EnemyType.Type2 ? 0.25 : 0.5) && Time.time > _pushTimer) {
                    Debug.Log("State of enemy is changing from AbleToPush to Pushing.");
                    state = EnemyState.Pushing;
                    _pushTimer = Time.time + (enemyType == Enemy.EnemyType.Type2 ? 5 : 3);
                } else {
                    // Debug.Log("State of enemy is changing from AbleToPush to Roaming.");
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
                    
                    // Verify this works
                    EventManager.Instance.PushAlly(_target);
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
                        var holeCollider = hole.GetComponent<Hole>();
                        var holePos = holeCollider.transform.position;
                        var holeDist = Vector3.Distance(holePos, transform.position);
                        if (holeDist < shortestUpHoleDist && hole.GetComponent<Hole>().floorNumber == currentFloor + 1) {
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
                    if (!IsWallInFront()) {
                        state = EnemyState.Turning;
                    } else {
                        state = EnemyState.Roaming;
                    }
                } else if (Vector3.Distance(transform.position, _target.transform.position) < 5) {
                    // render game over text
                    Debug.Log("Game over!");
                    //Time.timeScale = 0;
                }
                
                break;
            }

            case EnemyState.JumpingDown: {
                if (nearestDownwardHole != null) {
                    agent.SetDestination(nearestDownwardHole.transform.position);
                    
                    if (Math.Abs(transform.position.x - nearestDownwardHole.transform.position.x) < 5 && 
                        Math.Abs(transform.position.z - nearestDownwardHole.transform.position.z) < 5 && 
                        currentFloor == nearestDownwardHole.GetComponent<Hole>().floorNumber) {
                        agent.Warp(new Vector3(transform.position.x, transform.position.y - 9, transform.position.z));
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
                    var allyObj = ally.obj;
                    
                    if (allyObj.GetInstanceID() != gameObject.GetInstanceID() && !_recentRejections.Contains(allyObj)) {
                        var allyDist = Vector3.Distance(transform.position, allyObj.transform.position);
                        if (allyDist < shortestDist && allyObj.GetComponent<EnemyController>().currentFloor == currentFloor) {
                            shortestDist = allyDist;
                            closestAlly = allyObj;
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
                    double willingness;
                    if (enemyType == Enemy.EnemyType.Type2) {
                        // 0.5-0.8
                        willingness = _rand.NextDouble() * 0.3 + 0.5;
                    } else {
                        // 0.3-0.5
                        willingness = _rand.NextDouble() * 0.2 + 0.3;
                    }

                    var allyState = _target.GetComponent<EnemyController>().state;
                    if (allyState == EnemyState.Roaming || allyState == EnemyState.ChasingAlly || allyState == EnemyState.SearchingAlly || allyState == EnemyState.Turning || allyState == EnemyState.AbleToPush) {
                        EventManager.Instance.InitiateComms(gameObject, _target, nearestUpwardHole, willingness);
                        boostStatus = BoostStatus.Waiting;
                    } else {
                        _recentRejections.Add(_target);
                        state = EnemyState.SearchingAlly;
                    }
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
                    boostStatus = BoostStatus.Undefined;
                    allyBoostStatus = BoostStatus.Undefined;
                    _recentRejections.Add(_target);
                    Debug.Log("Rejection!");
                    Debug.Log("State of enemy is changing from CommsWithAllySelfInitiated to Roaming.");
                    state = EnemyState.Roaming;
                }
                
                break;
            }

            case EnemyState.ReturnToHoleBoosting: {
                var nearestUpwardHolePos = nearestUpwardHole.transform.position;
                agent.SetDestination(new Vector3(nearestUpwardHolePos.x, transform.position.y, nearestUpwardHolePos.z));

                if (Math.Abs(transform.position.x - nearestUpwardHole.transform.position.x) < 5 && 
                    Math.Abs(transform.position.z - nearestUpwardHole.transform.position.z) < 5 &&
                    nearestUpwardHole.GetComponent<Hole>().floorNumber == currentFloor + 1) {
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
                var nearestUpwardHolePos = nearestUpwardHole.transform.position;
                agent.SetDestination(new Vector3(nearestUpwardHolePos.x, transform.position.y, nearestUpwardHolePos.z));

                if (Math.Abs(transform.position.x - nearestUpwardHole.transform.position.x) < 5 && 
                    Math.Abs(transform.position.z - nearestUpwardHole.transform.position.z) < 5 &&
                    nearestUpwardHole.GetComponent<Hole>().floorNumber == currentFloor + 1) {
                    Debug.Log("State of enemy is changing from ReturnToHoleBeingBoosted to BeingBoosted.");
                    state = EnemyState.BeingBoosted;
                    boostStatus = BoostStatus.Undefined;
                    allyBoostStatus = BoostStatus.Undefined;
                }
                
                break;
            }

            case EnemyState.BeingBoosted: {
                agent.Warp(new Vector3(transform.position.x, transform.position.y + 9, transform.position.z));
                currentFloor++;
                EventManager.Instance.ReportBoostSuccess(_target);
                Debug.Log("State of enemy is changing from BeingBoosted to Roaming.");
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
                        nearestUpwardHole = null;
                    }
                    
                    EventManager.Instance.SendCommsResponse(gameObject, _target, boostStatus);
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

            default: {
                //Debug.Log("State of enemy is changing from Default to Roaming.");
                state = EnemyState.Roaming;
                
                break;
            }
        }
    }

    private bool IsWallInFront() {
        const int layerMask = 1 << 6;

        if (Physics.Raycast(transform.position, transform.forward, out var hit, 5, layerMask)) {
            Debug.DrawRay(transform.position, transform.forward * hit.distance, Color.red);
            //Debug.Log("Wall detected");
            return true;
        }

        Debug.DrawRay(transform.position, transform.forward * 50, Color.yellow);
        //Debug.Log("No wall detected");
        
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

    private void OnTriggerEnter(Collider other) {
        if (other.CompareTag("Intersection")) {
            _isAtIntersection = true;
        }
    }

    private void OnTriggerExit(Collider other) {
        if (other.CompareTag("Intersection")) {
            _isAtIntersection = false;
        }
    }

    private void OnCommsResponse(GameObject sender, GameObject recipient, BoostStatus allyStatus) {
        if (gameObject.GetInstanceID() == recipient.gameObject.GetInstanceID()) {
            _target = sender;
            allyBoostStatus = allyStatus;
        }
    }
    
    private void OnCommsInitiated(GameObject sender, GameObject recipient, GameObject targetHole, double allyWillingness) {
        if (gameObject.GetInstanceID() == recipient.gameObject.GetInstanceID()) {
            _allyWillingnessToBoost = allyWillingness;
            _target = sender;
            nearestUpwardHole = targetHole;
            _isReceivingComms = true;
        }
    }

    private void OnBoostSuccessful(GameObject recipient) {
        if (gameObject.GetInstanceID() == recipient.gameObject.GetInstanceID()) {
            _wasBoostSuccessful = true;
        }
    }

    private void OnPush(GameObject recipient) {
        if (gameObject.GetInstanceID() == recipient.gameObject.GetInstanceID()) {
            _isBeingPushed = true;
        }
    }

    private void OnEcho(string message) {
        Debug.Log(message);
    }
}